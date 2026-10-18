using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Core.Devices;
using Zevision.Core.Localization;

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
                return Result<string>.Fail(before.Error!); // 失败的 Result 总有原因
            }

            if (before.Value == 0)
            {
                return Result<string>.Fail(Lang.T($"{listId} 中没有可删除的条目。", $"{listId} has no item to remove."));
            }

            var error = await SendExpectingSuccessAsync(remove, cancellationToken).ConfigureAwait(false);
            if (error != null)
            {
                return Result<string>.Fail($"{remove}: {error}");
            }

            var after = await ReadListCountAsync(listId, cancellationToken).ConfigureAwait(false);
            if (!after.IsSuccess || after.Value != before.Value - 1)
            {
                return Result<string>.Fail(Lang.T(
                    $"已发送 {remove},但 {listId} 的条数不是预期的 {before.Value - 1}: ",
                    $"{remove} was sent, but {listId} does not have the expected {before.Value - 1} item(s): ") + (after.IsSuccess ? after.Value.ToString(CultureInfo.InvariantCulture) : after.Error));
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
                // 1. 系统设置 (传感器、源、输入输出、逻辑、系统偏好):不依赖可创建的对象,最先写入。
                var systemWrites = plan.Writes.Where(w => RecipeRules.IsSystemSetting(w.Id)).ToList();
                report.PlannedWriteCount = systemWrites.Count;
                if (!await WriteAllAsync(systemWrites, Lang.T("写入系统设置", "Writing system settings"), written, report, progress, cancellationToken).ConfigureAwait(false))
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

                // 3. 对象设置,按 材料 → 膜层 → 层 → 工艺 的顺序 (计划已按 RecipeRules.GroupRank 排序)。
                //    系统设置已在第 1 步写过,重新计划中即使再次出现也不重复写入,由最后的校验发现问题。
                var objectWrites = current.Writes.Where(w => !RecipeRules.IsSystemSetting(w.Id)).ToList();
                report.PlannedWriteCount += objectWrites.Count;
                if (!await WriteAllAsync(objectWrites, Lang.T("写入", "Writing"), written, report, progress, cancellationToken).ConfigureAwait(false))
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
                foreach (var edit in edits)
                {
                    if (!RecipeRules.IsRestorable(edit.Id, edit.Value, out var reason))
                    {
                        report.FailedId = edit.Id;
                        report.Error = Lang.T($"{edit.Id} 不能修改: {reason}", $"{edit.Id} cannot be changed: {reason}");
                        return report;
                    }

                    if (ProtocolText.ValidateValue(edit.Value) != null)
                    {
                        report.FailedId = edit.Id;
                        report.Error = Lang.T($"{edit.Id} 的值含有协议不支持的字符 (非 ASCII、; 或 {{ }})。", $"The value of {edit.Id} contains characters the protocol does not allow (non-ASCII, ; or {{ }}).");
                        return report;
                    }
                }

                // 快速确认设备:用单条查询代替约 10 秒的 ? 读取。
                progress?.Report(new RestoreProgress(Lang.T("检查设备", "Checking the device"), 0, 0));
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
                if (!await WriteAllAsync(writes, Lang.T("写入", "Writing"), written, report, progress, cancellationToken).ConfigureAwait(false))
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
                    ? Lang.T($"已在 {listId} 末尾追加一项,但条数 {count.Value} 不合法。", $"Appended an item to {listId}, but its count {count.Value} is invalid.")
                    : Lang.T($"已在 {listId} 末尾追加一项。", $"Appended an item to {listId}. ") + count.Error);
            }

            var itemId = DeviceIds.Item(listId, count.Value);
            var writeError = value == null ? null : await WriteVerifiedAsync(itemId, value, cancellationToken).ConfigureAwait(false);
            return writeError == null
                ? Result<string>.Ok(itemId)
                : Result<string>.Fail(Lang.T($"已在末尾追加 {itemId},但", $"Appended {itemId}, but ") + writeError);
        }

        /// <summary>写入一项 (<c>U&lt;id&gt;:&lt;值&gt;</c>) 并读回校验。</summary>
        /// <returns>失败的原因;成功时为 null。</returns>
        private async Task<string?> WriteVerifiedAsync(string id, string value, CancellationToken cancellationToken)
        {
            var write = ZevisionCommandSet.Update(id, value);
            var error = await SendExpectingSuccessAsync(write, cancellationToken).ConfigureAwait(false);
            if (error != null)
            {
                return Lang.T($"写入失败 ({write}): {error}", $"writing failed ({write}): {error}");
            }

            var readBack = await QueryValueAsync(id, cancellationToken).ConfigureAwait(false);
            return readBack.IsSuccess && ParameterValue.AreEquivalent(value, readBack.Value)
                ? null
                : Lang.T($"{id} 写入后读回不一致: 写入 {value},读回 ", $"{id} read back differs: wrote {value}, read ") + (readBack.IsSuccess ? readBack.Value : readBack.Error);
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
                    report.VerifyMismatches.Add(Lang.T($"{write.Id}: 期望 \"{write.Value}\",读回 \"{readBack ?? actual.Error}\"", $"{write.Id}: expected \"{write.Value}\", read back \"{readBack ?? actual.Error}\""));
                }

                progress?.Report(new RestoreProgress(Lang.T("校验", "Verifying"), i + 1, written.Count));
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
                    report.Error = Lang.T($"创建对象超过 {MaxCreateRounds} 轮仍未完成,已停止,未写入任何设置。", $"Creating objects did not finish within {MaxCreateRounds} rounds; stopped without writing any settings.");
                    return null;
                }

                var stage = round == 1 ? Lang.T("创建", "Creating") : Lang.T($"创建 (第 {round} 轮)", $"Creating (round {round})");
                for (var i = 0; i < current.Creates.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var create = current.Creates[i];
                    var error = await SendExpectingSuccessAsync(create.Command, cancellationToken).ConfigureAwait(false);
                    if (error != null)
                    {
                        report.FailedId = create.Id;
                        report.Error = Lang.T($"创建{create.Kind} {create.Id} ({create.Command}) 失败: {error}", $"Creating {create.Kind} {create.Id} ({create.Command}) failed: {error}");
                        return null;
                    }

                    report.CreatedCount++;
                    progress?.Report(new RestoreProgress(stage, i + 1, current.Creates.Count));
                }

                progress?.Report(new RestoreProgress(Lang.T("重新读取设备", "Re-reading the device"), 0, 0));
                var snapshot = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                if (!snapshot.IsSuccess)
                {
                    report.Error = Lang.T("创建对象后", "After creating objects: ") + snapshot.Error;
                    return null;
                }

                var replanned = RestorePlanner.Plan(recipe, snapshot.Value);
                if (!replanned.IsSuccess)
                {
                    report.Error = Lang.T("创建对象后重新计划失败: ", "Re-planning after creating objects failed: ") + replanned.Error;
                    return null;
                }

                // 设备接受了创建指令,但对象没有出现在预期编号上:继续创建只会重复失败。
                var created = new HashSet<string>(current.Creates.Select(c => c.Id), StringComparer.Ordinal);
                var stillMissing = replanned.Value.Creates.FirstOrDefault(c => created.Contains(c.Id));
                if (stillMissing != null)
                {
                    report.FailedId = stillMissing.Id;
                    report.Error = Lang.T($"已发送创建指令,但设备上仍没有{stillMissing.Kind} {stillMissing.Id},已停止,未写入任何设置。", $"The create command was sent, but {stillMissing.Kind} {stillMissing.Id} still does not exist on the device; stopped without writing any settings.");
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
