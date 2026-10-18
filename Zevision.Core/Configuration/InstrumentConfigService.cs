using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Core.Devices;

namespace Zevision.Core.Configuration
{
    /// <summary>
    /// <see cref="IInstrumentConfigService"/> 的实现:修改设备 (创建对象、写入、校验);读取由 <see cref="InstrumentConfigReader"/> 完成,
    /// 恢复计划的计算见 <see cref="RestorePlanner"/>。
    /// </summary>
    /// <remarks>
    /// 设备上已被删除的材料/膜层/层/工艺会用 A 指令按原编号重新创建 (见 <see cref="ObjectTypes"/>);
    /// 子列表 (预熔/后处理步骤等) 缺少的条目用"追加到末尾"创建。不删除设备上多出的对象或条目。
    /// </remarks>
    public sealed class InstrumentConfigService : InstrumentConfigReader, IInstrumentConfigService
    {
        /// <summary>
        /// 创建对象的最大轮数。每轮之后要重新读取设备 (约 10 秒)。
        /// 实际层级:顶层对象 → 子列表条目 → 子列表条目的子列表 (如层中膜层的速率斜坡),3 轮足够,留 1 轮余量。
        /// </summary>
        private const int MaxCreateRounds = 4;

        /// <summary>创建服务。</summary>
        public InstrumentConfigService(IDeviceService device)
            : base(device)
        {
        }

        /// <inheritdoc />
        public async Task<ObjectCreation> CreateObjectAsync(ObjectType type, string objectId, string? defaultValue, string deviceName, CancellationToken cancellationToken)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            // 指令由指令集生成并检查动作是否支持 (只有 M**、F**、L**、P02** 支持按编号增删)。
            var error = await SendWhenReadyAsync(ZevisionCommandSet.Add(objectId), deviceName, cancellationToken).ConfigureAwait(false);
            if (error != null || type.NewDefault == null || defaultValue == null)
            {
                return new ObjectCreation(error, null);
            }

            return new ObjectCreation(null, await SetDefaultAsync(objectId, type.NewDefault, defaultValue, cancellationToken).ConfigureAwait(false));
        }

        /// <inheritdoc />
        public async Task<Result> DeleteObjectAsync(string objectId, string deviceName, CancellationToken cancellationToken)
        {
            var error = await SendWhenReadyAsync(ZevisionCommandSet.Build(CommandActions.Delete, objectId), deviceName, cancellationToken).ConfigureAwait(false);
            return error == null ? Result.Ok() : Result.Fail(error);
        }

        /// <inheritdoc />
        public async Task<Result<string>> RemoveLastListItemAsync(string listId, string deviceName, CancellationToken cancellationToken)
        {
            var remove = ZevisionCommandSet.Build(CommandActions.Delete, DeviceIds.AppendSlotOf(listId));
            var notReady = await CheckDeviceReadyAsync(deviceName, cancellationToken).ConfigureAwait(false);
            if (notReady != null)
            {
                return Result<string>.Fail(notReady);
            }

            var before = await ReadListCountAsync(listId, cancellationToken).ConfigureAwait(false);
            if (!before.IsSuccess)
            {
                return Result<string>.Fail(before.Error);
            }

            if (before.Value == 0)
            {
                return Result<string>.Fail(Texts.NoItemToRemove(listId));
            }

            var error = await SendExpectingSuccessAsync(remove, cancellationToken).ConfigureAwait(false);
            if (error != null)
            {
                return Result<string>.Fail($"{remove}: {error}");
            }

            var after = await ReadListCountAsync(listId, cancellationToken).ConfigureAwait(false);
            if (!after.IsSuccess || after.Value != before.Value - 1)
            {
                return Result<string>.Fail(Texts.ListCountAfterRemove(remove, listId, before.Value - 1) + (after.IsSuccess ? after.Value.ToString(CultureInfo.InvariantCulture) : after.Error));
            }

            return Result<string>.Ok(DeviceIds.Item(listId, before.Value));
        }

        /// <inheritdoc />
        public async Task<Result<string>> AppendListItemAsync(string listId, string? value, string deviceName, CancellationToken cancellationToken)
        {
            // 先生成指令:ID 或值不合法时在发送任何指令之前失败。
            ZevisionCommandSet.Add(DeviceIds.AppendSlotOf(listId));
            if (value != null)
            {
                ZevisionCommandSet.Update(DeviceIds.Item(listId, CommandDefinition.MinIndex), value);
            }

            var notReady = await CheckDeviceReadyAsync(deviceName, cancellationToken).ConfigureAwait(false);
            return notReady != null ? Result<string>.Fail(notReady) : await AppendAsync(listId, value, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<RestoreReport> ExecuteRestoreAsync(InstrumentRecipe recipe, RestorePlan plan, IProgress<RestoreProgress>? progress, CancellationToken cancellationToken)
        {
            if (recipe is null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            if (plan is null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var report = new RestoreReport();
            var written = new List<RestoreWrite>();
            try
            {
                // 计划是在确认框之前读取的设备数据上做的;确认期间设备可能已开始工艺。写入前再确认型号和空闲状态 (与其他写入方法相同)。
                report.Error = await CheckDeviceReadyAsync(recipe.DeviceName, cancellationToken).ConfigureAwait(false);
                if (report.Error != null)
                {
                    return report;
                }

                // 1. 系统设置 (传感器、输出源、输入输出、逻辑、系统偏好):不依赖可创建的对象,最先写入。
                var systemWrites = plan.Writes.Where(w => RecipeRules.IsSystemSetting(w.Id)).ToList();
                report.PlannedWriteCount = systemWrites.Count;
                if (!await WriteAllAsync(systemWrites, Texts.StageWritingSystem, written, report, progress, cancellationToken).ConfigureAwait(false))
                {
                    return report;
                }

                // 2. 创建缺失的材料/膜层/层/工艺及子列表条目,重新读取并重新计划。
                var current = plan;
                if (plan.Creates.Count > 0)
                {
                    var replanned = await CreateObjectsAndReplanAsync(recipe, plan, report, progress, cancellationToken).ConfigureAwait(false);
                    if (replanned == null)
                    {
                        report.FailedDuringCreate = true;
                        return report;
                    }

                    current = replanned;
                }

                // 3. 对象设置,按 材料 → 膜系 → 膜层 → 工艺 的顺序 (计划已按 RecipeRules.GroupRank 排序),
                //    以及创建后才出现的系统设置 (如输出源新追加的坩埚材料 S**09**)。
                //    第 1 步写过的项即使在重新计划中再次出现也不重复写入,由最后的校验发现问题。
                var writtenIds = new HashSet<string>(written.Select(w => w.Id), StringComparer.Ordinal);
                var objectWrites = current.Writes.Where(w => !writtenIds.Contains(w.Id)).ToList();
                report.PlannedWriteCount += objectWrites.Count;
                if (!await WriteAllAsync(objectWrites, Texts.StageWriting, written, report, progress, cancellationToken).ConfigureAwait(false))
                {
                    return report;
                }

                // 4. 逐项读回校验。
                await VerifyAsync(written, report, progress, cancellationToken).ConfigureAwait(false);
                return report;
            }
            catch (OperationCanceledException)
            {
                report.Cancelled = true;
                return report;
            }
        }

        /// <inheritdoc />
        public async Task<RestoreReport> ApplyEditsAsync(
            InstrumentRecipe recipe,
            IReadOnlyList<RecipeEntry> edits,
            IProgress<RestoreProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (recipe is null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            if (edits is null)
            {
                throw new ArgumentNullException(nameof(edits));
            }

            var report = new RestoreReport();
            try
            {
                // 写入前逐项检查,任何一项不合格都不写,避免设备只改了一半。
                var valueAfterEdit = ValuesAfterEdit(recipe, edits);
                foreach (var edit in edits)
                {
                    if (!RecipeRules.IsRestorable(edit.Id, edit.Value, out var reason))
                    {
                        report.FailedId = edit.Id;
                        report.Error = Texts.CannotChange(edit.Id, reason);
                        return report;
                    }

                    if (ProtocolText.ValidateValue(edit.Value) != null)
                    {
                        report.FailedId = edit.Id;
                        report.Error = Texts.ValueHasProtocolChars(edit.Id);
                        return report;
                    }

                    // 同时修改的输出源 (F**0901)、坩埚数量 (S**07) 按修改后的值判断。
                    var crucible = CrucibleRule.Check(edit.Id, edit.Value, valueAfterEdit);
                    if (crucible != null)
                    {
                        report.FailedId = edit.Id;
                        report.Error = Texts.CannotChange(edit.Id, crucible);
                        return report;
                    }
                }

                // 快速确认设备:用单条查询代替约 10 秒的 ? 读取。
                progress?.Report(new RestoreProgress(Texts.StageCheckingDevice, 0, 0));
                report.Error = await CheckDeviceReadyAsync(recipe.DeviceName, cancellationToken).ConfigureAwait(false);
                if (report.Error != null)
                {
                    return report;
                }

                // 与恢复相同的顺序:系统设置 → 材料 → 膜层 → 层 → 工艺。
                var writes = edits
                    .OrderBy(e => RecipeRules.GroupRank(e.Id))
                    .Select(e => new RestoreWrite(e.Id, e.Value, string.Empty, e.Description))
                    .ToList();
                report.PlannedWriteCount = writes.Count;
                var written = new List<RestoreWrite>();
                if (!await WriteAllAsync(writes, Texts.StageWriting, written, report, progress, cancellationToken).ConfigureAwait(false))
                {
                    return report;
                }

                await VerifyAsync(written, report, progress, cancellationToken).ConfigureAwait(false);
                return report;
            }
            catch (OperationCanceledException)
            {
                report.Cancelled = true;
                return report;
            }
        }

        /// <summary>写入后各项的值:修改的项取新值,其他取配置中的值 (ID 重复时取第一次出现的)。</summary>
        private static Func<string, string?> ValuesAfterEdit(InstrumentRecipe recipe, IReadOnlyList<RecipeEntry> edits)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in edits.Concat(recipe.Entries))
            {
                if (!values.ContainsKey(entry.Id))
                {
                    values.Add(entry.Id, entry.Value);
                }
            }

            return id => values.TryGetValue(id, out var value) ? value : null;
        }

        /// <summary>确认设备型号与空闲状态后发送一条指令。</summary>
        /// <returns>失败的原因;成功时为 null。</returns>
        private async Task<string?> SendWhenReadyAsync(string command, string deviceName, CancellationToken cancellationToken)
        {
            var notReady = await CheckDeviceReadyAsync(deviceName, cancellationToken).ConfigureAwait(false);
            if (notReady != null)
            {
                return notReady;
            }

            var error = await SendExpectingSuccessAsync(command, cancellationToken).ConfigureAwait(false);
            return error == null ? null : $"{command}: {error}";
        }

        /// <summary>
        /// 新建对象的默认引用 (<see cref="ObjectDefault"/>):需要时先在对象的列表末尾追加一项,再写入并读回校验。
        /// </summary>
        /// <returns>失败的原因;成功时为 null。</returns>
        private async Task<string?> SetDefaultAsync(string objectId, ObjectDefault initial, string value, CancellationToken cancellationToken)
        {
            var owner = objectId;
            if (initial.ListSuffix != null)
            {
                // 条目本身就是引用 (工艺的膜层) 时追加并写入条目;否则追加后再写条目中的一项 (膜层膜系条目的膜系)。
                var itemValue = initial.SettingSuffix.Length == 0 ? value : null;
                var appended = await AppendAsync(objectId + initial.ListSuffix, itemValue, cancellationToken).ConfigureAwait(false);
                if (!appended.IsSuccess || itemValue != null)
                {
                    return appended.Error;
                }

                owner = appended.Value;
            }

            return await WriteVerifiedAsync(owner + initial.SettingSuffix, value, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 在列表末尾追加一项 (不检查设备状态):<c>A&lt;list&gt;00</c>,读条数得到新条目的编号 (列表从 01 连续编号),
        /// <paramref name="value"/> 不为 null 时写入新条目并读回校验。
        /// </summary>
        /// <returns>成功时为新条目的 ID。</returns>
        private async Task<Result<string>> AppendAsync(string listId, string? value, CancellationToken cancellationToken)
        {
            var append = ZevisionCommandSet.Add(DeviceIds.AppendSlotOf(listId));
            var appendError = await SendExpectingSuccessAsync(append, cancellationToken).ConfigureAwait(false);
            if (appendError != null)
            {
                return Result<string>.Fail($"{append}: {appendError}");
            }

            var count = await ReadListCountAsync(listId, cancellationToken).ConfigureAwait(false);
            if (!count.IsSuccess || count.Value < CommandDefinition.MinIndex || count.Value > CommandDefinition.MaxIndex)
            {
                return Result<string>.Fail(count.IsSuccess
                    ? Texts.AppendedInvalidCount(listId, count.Value)
                    : Texts.AppendedItem(listId) + count.Error);
            }

            var itemId = DeviceIds.Item(listId, count.Value);
            var writeError = value == null ? null : await WriteVerifiedAsync(itemId, value, cancellationToken).ConfigureAwait(false);
            return writeError == null
                ? Result<string>.Ok(itemId)
                : Result<string>.Fail(Texts.AppendedBut(itemId) + writeError);
        }

        /// <summary>写入一项 (<c>U&lt;id&gt;:&lt;值&gt;</c>) 并读回校验。</summary>
        /// <returns>失败的原因;成功时为 null。</returns>
        private async Task<string?> WriteVerifiedAsync(string id, string value, CancellationToken cancellationToken)
        {
            var write = ZevisionCommandSet.Update(id, value);
            var error = await SendExpectingSuccessAsync(write, cancellationToken).ConfigureAwait(false);
            if (error != null)
            {
                return Texts.WriteFailed(write, error);
            }

            var readBack = await QueryValueAsync(id, cancellationToken).ConfigureAwait(false);
            return readBack.IsSuccess && ParameterValue.AreEquivalent(value, readBack.Value)
                ? null
                : Texts.ReadBackDiffers(id, value) + (readBack.IsSuccess ? readBack.Value : readBack.Error);
        }

        /// <summary>
        /// 用 Q 指令逐项读回已写入的值,与期望值不一致的记入 <see cref="RestoreReport.VerifyMismatches"/>。
        /// </summary>
        private async Task VerifyAsync(
            IReadOnlyList<RestoreWrite> written,
            RestoreReport report,
            IProgress<RestoreProgress>? progress,
            CancellationToken cancellationToken)
        {
            for (var i = 0; i < written.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var write = written[i];
                var actual = await QueryValueAsync(write.Id, cancellationToken).ConfigureAwait(false);
                var readBack = actual.IsSuccess ? actual.Value : null;
                if (!ParameterValue.AreEquivalent(write.Value, readBack))
                {
                    report.VerifyMismatches.Add(Texts.VerifyMismatch(write.Id, write.Value, readBack ?? actual.Error));
                }

                progress?.Report(new RestoreProgress(Texts.StageVerifying, i + 1, written.Count));
            }
        }

        /// <summary>
        /// 按顺序写入,遇到第一个设备拒绝即停止。
        /// </summary>
        /// <returns>全部写入成功返回 true;失败时原因已写入 <paramref name="report"/>。</returns>
        private async Task<bool> WriteAllAsync(
            IReadOnlyList<RestoreWrite> writes,
            string stage,
            List<RestoreWrite> written,
            RestoreReport report,
            IProgress<RestoreProgress>? progress,
            CancellationToken cancellationToken)
        {
            for (var i = 0; i < writes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var write = writes[i];
                var error = await SendExpectingSuccessAsync(write.Command, cancellationToken).ConfigureAwait(false);
                if (error != null)
                {
                    report.FailedId = write.Id;
                    report.Error = error;
                    return false;
                }

                written.Add(write);
                report.WrittenCount++;
                progress?.Report(new RestoreProgress(stage, i + 1, writes.Count));
            }

            return true;
        }

        /// <summary>
        /// 分轮创建缺失的对象:每轮发送创建指令后重新读取设备并重新计划,直到不再需要创建。
        /// 需要多轮是因为新对象的子列表 (如新膜层的预熔步骤) 要等对象创建后才出现在设备上。
        /// 新对象带的是设备默认值,只有重新读取才知道该写哪些项。
        /// </summary>
        /// <returns>最后一轮的计划;失败时返回 null,原因已写入 <paramref name="report"/>。</returns>
        private async Task<RestorePlan?> CreateObjectsAndReplanAsync(
            InstrumentRecipe recipe,
            RestorePlan plan,
            RestoreReport report,
            IProgress<RestoreProgress>? progress,
            CancellationToken cancellationToken)
        {
            var current = plan;
            for (var round = 1; current.Creates.Count > 0; round++)
            {
                if (round > MaxCreateRounds)
                {
                    report.Error = Texts.CreateRoundsExceeded(MaxCreateRounds);
                    return null;
                }

                var stage = round == 1 ? Texts.StageCreating : Texts.StageCreatingRound(round);
                for (var i = 0; i < current.Creates.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var create = current.Creates[i];
                    var error = await SendExpectingSuccessAsync(create.Command, cancellationToken).ConfigureAwait(false);
                    if (error != null)
                    {
                        report.FailedId = create.Id;
                        report.Error = Texts.CreateFailed(create.Kind, create.Id, create.Command, error);
                        return null;
                    }

                    report.CreatedCount++;
                    progress?.Report(new RestoreProgress(stage, i + 1, current.Creates.Count));
                }

                progress?.Report(new RestoreProgress(Texts.StageRereading, 0, 0));
                var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                if (!snapshot.IsSuccess)
                {
                    report.Error = Texts.AfterCreatingObjects + snapshot.Error;
                    return null;
                }

                var replanned = RestorePlanner.Plan(recipe, snapshot.Value);
                if (!replanned.IsSuccess)
                {
                    report.Error = Texts.ReplanFailed + replanned.Error;
                    return null;
                }

                // 设备接受了创建指令,但对象没有出现在预期编号上:继续创建只会重复失败。
                var created = new HashSet<string>(current.Creates.Select(c => c.Id), StringComparer.Ordinal);
                var stillMissing = replanned.Value.Creates.FirstOrDefault(c => created.Contains(c.Id));
                if (stillMissing != null)
                {
                    report.FailedId = stillMissing.Id;
                    report.Error = Texts.CreatedObjectMissing(stillMissing.Kind, stillMissing.Id);
                    return null;
                }

                current = replanned.Value;
            }

            var initiallySkipped = new HashSet<string>(plan.Skipped.Select(s => s.Id), StringComparer.Ordinal);
            report.AdditionalSkipped.AddRange(current.Skipped.Where(s => !initiallySkipped.Contains(s.Id)));
            return current;
        }

    }
}
