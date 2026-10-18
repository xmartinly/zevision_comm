using System;
using System.Collections.Generic;
using System.Linq;
using Zevision.Commands;
using Zevision.Communication;

namespace Zevision.Core.Configuration
{
    /// <summary>
    /// 恢复计划:对比配置文件与设备快照,算出需要创建的对象/子列表条目、需要写入的项和跳过的项。
    /// 纯计算,不与设备通讯,便于测试。执行见 <see cref="InstrumentConfigService"/>。
    /// </summary>
    public static class RestorePlanner
    {
        /// <summary>
        /// 生成恢复计划。设备型号不符或设备不在空闲状态时返回失败。
        /// </summary>
        public static Result<RestorePlan> Plan(InstrumentRecipe recipe, InstrumentSnapshot snapshot)
        {
            if (recipe is null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.DeviceName.Length == 0)
            {
                return Result<RestorePlan>.Fail(Texts.DeviceUnidentified);
            }

            if (!string.Equals(recipe.DeviceName, snapshot.DeviceName, StringComparison.OrdinalIgnoreCase)
                || (recipe.ModelNumber.Length > 0 && snapshot.ModelNumber.Length > 0
                    && !string.Equals(recipe.ModelNumber, snapshot.ModelNumber, StringComparison.OrdinalIgnoreCase)))
            {
                return Result<RestorePlan>.Fail(
                    Texts.RestoreModelMismatch(recipe.DeviceName, recipe.ModelNumber, snapshot.DeviceName, snapshot.ModelNumber));
            }

            if (!string.Equals(snapshot.CurrentState, InstrumentSnapshot.ReadyState, StringComparison.OrdinalIgnoreCase))
            {
                return Result<RestorePlan>.Fail(
                    Texts.RestoreNotReady(snapshot.CurrentState ?? Texts.UnknownValue, InstrumentSnapshot.ReadyState));
            }

            var warnings = new List<string>();
            if (!string.Equals(recipe.FirmwareVersion, snapshot.FirmwareVersion, StringComparison.Ordinal))
            {
                warnings.Add(Texts.FirmwareDiffers(recipe.FirmwareVersion, snapshot.FirmwareVersion));
            }

            // 文件中有设置项、设备上缺少的对象,分两类创建:
            // 1. 顶层对象 (材料/膜层/层/工艺) 按原编号创建,如 AM03;
            // 2. 已有对象中的子列表条目 (如膜层的预熔步骤) 用"追加到末尾"创建,如 AF010300。
            // 新建顶层对象的子列表要等对象创建、重新读取设备后才看得到,由执行阶段的下一轮处理。
            var creates = new List<RestoreCreate>();
            var createIds = new HashSet<string>(StringComparer.Ordinal);
            var deferred = new HashSet<string>(StringComparer.Ordinal);
            var notCreatable = new Dictionary<string, string>(StringComparer.Ordinal);
            var appendTo = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in recipe.Entries)
            {
                if (!RecipeRules.IsRestorable(entry.Id, entry.Value, out _) || snapshot.GetValue(entry.Id) != null)
                {
                    continue;
                }

                if (ObjectTypes.TryGetObject(entry.Id, out var type, out var itemId) && snapshot.GetValue(itemId) == null)
                {
                    if (createIds.Add(itemId))
                    {
                        creates.Add(new RestoreCreate(itemId, type.Kind.ToLowerInvariant(), ZevisionCommandSet.Add(itemId)));
                    }

                    deferred.Add(entry.Id);
                }
                else if (TryFindSubListAppend(entry.Id, snapshot, out var listId, out var number, out var reason))
                {
                    appendTo[listId] = Math.Max(number, appendTo.TryGetValue(listId, out var existing) ? existing : 0);
                    deferred.Add(entry.Id);
                }
                else
                {
                    notCreatable[entry.Id] = reason;
                }
            }

            // 被引用的对象在前:材料 → 膜层 → 层 → 工艺;子列表条目按列表、编号升序追加。
            creates = creates.OrderBy(c => RecipeRules.GroupRank(c.Id)).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
            foreach (var list in appendTo.Keys.OrderBy(id => RecipeRules.GroupRank(id)).ThenBy(id => id, StringComparer.Ordinal))
            {
                var kind = snapshot.GetDescription(list) is string description && description.Length > 0 ? description + Texts.EntrySuffix : Texts.SubListEntry;
                for (var n = CountItems(snapshot, list) + 1; n <= appendTo[list]; n++)
                {
                    var itemId = DeviceIds.Item(list, n);
                    createIds.Add(itemId);
                    creates.Add(new RestoreCreate(itemId, kind, ZevisionCommandSet.Add(DeviceIds.AppendSlotOf(list))));
                }
            }

            var writes = new List<RestoreWrite>();
            var skipped = new List<RestoreSkip>();
            var unchanged = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // OrderBy 是稳定排序:组内保持文件 (即设备帮助表) 中的顺序。
            foreach (var entry in recipe.Entries.OrderBy(e => RecipeRules.GroupRank(e.Id)))
            {
                if (!seen.Add(entry.Id))
                {
                    skipped.Add(new RestoreSkip(entry, Texts.DuplicateInFile));
                    continue;
                }

                if (!RecipeRules.IsRestorable(entry.Id, entry.Value, out var reason))
                {
                    skipped.Add(new RestoreSkip(entry, Texts.NotRestorablePrefix + reason));
                    continue;
                }

                if (ProtocolText.ValidateValue(entry.Value) != null)
                {
                    skipped.Add(new RestoreSkip(entry, Texts.SkipProtocolChars));
                    continue;
                }

                var current = snapshot.GetValue(entry.Id);
                if (current == null)
                {
                    // 所属对象将被创建:创建后重新计划时再处理,这里不算跳过。
                    if (!deferred.Contains(entry.Id))
                    {
                        skipped.Add(new RestoreSkip(entry, notCreatable.TryGetValue(entry.Id, out var why) ? why : Texts.NotOnDevice));
                    }

                    continue;
                }

                if (string.Equals(current, entry.Value, StringComparison.Ordinal))
                {
                    unchanged++;
                    continue;
                }

                // 引用的对象既不在设备上、也不会被创建时,设备必然拒绝 (B 非法参数值),提前跳过。
                if (RecipeRules.TryGetReference(entry.Id, entry.Value, out var targetId, out var targetKind)
                    && snapshot.GetValue(targetId) == null
                    && !createIds.Contains(targetId))
                {
                    skipped.Add(new RestoreSkip(entry, Texts.ReferenceMissing(targetKind, targetId)));
                    continue;
                }

                writes.Add(new RestoreWrite(entry.Id, entry.Value, current, entry.Description));
            }

            return Result<RestorePlan>.Ok(new RestorePlan(creates, writes, unchanged, skipped, warnings));
        }

        /// <summary>
        /// 设置项在设备上不存在时,找出它第一个缺失的祖先 (如 <c>F01030101</c> → 预熔步骤 <c>F010301</c>),
        /// 判断能否通过向所在子列表 (<c>F0103</c>) 末尾追加条目 (<c>AF010300</c>) 来创建。
        /// 设置项本身也可能就是列表条目 (值直接挂在条目上,没有下级),
        /// 如工艺的层列表条目 <c>P02010701</c> (Layer = 1) → 向 <c>P020107</c> 追加 (<c>AP02010700</c>)。
        /// </summary>
        /// <remarks>
        /// 只有设备提供了 <c>&lt;列表&gt;00</c> (追加/删除) 项、且现有条目编号从 01 连续时才可追加:
        /// 追加的条目编号依次为"现有数量 + 1",编号不连续就无法保证落在文件中的编号上。
        /// </remarks>
        /// <param name="id">设备上不存在的设置项。</param>
        /// <param name="snapshot">当前快照。</param>
        /// <param name="listId">需要追加条目的子列表。</param>
        /// <param name="number">需要存在的最大条目编号。</param>
        /// <param name="reason">不能创建时的原因。</param>
        private static bool TryFindSubListAppend(string id, InstrumentSnapshot snapshot, out string listId, out int number, out string reason)
        {
            listId = string.Empty;
            number = 0;

            // ID = 组字母 + 若干两位十六进制;从最短的祖先开始找第一个缺失的,最后检查设置项本身。
            for (var length = 3; length <= id.Length; length += 2)
            {
                var ancestor = id.Substring(0, length);
                if (snapshot.GetValue(ancestor) != null)
                {
                    continue;
                }

                if (length == 3)
                {
                    reason = Texts.OwnerObjectMissing(ancestor);
                    return false;
                }

                var list = ancestor.Substring(0, length - 2);
                if (snapshot.GetValue(DeviceIds.AppendSlotOf(list)) == null || !DeviceIds.IsItemNumber(ancestor.Substring(length - 2)))
                {
                    reason = Texts.OwnerEntryMissing(ancestor, list);
                    return false;
                }

                var count = CountItems(snapshot, list);
                for (var n = 1; n <= count; n++)
                {
                    if (snapshot.GetValue(DeviceIds.Item(list, n)) == null)
                    {
                        reason = Texts.SubListNotContiguous(list, ancestor);
                        return false;
                    }
                }

                listId = list;
                number = DeviceIds.LastNumber(ancestor);
                reason = string.Empty;
                return true;
            }

            reason = Texts.SettingNotOnDevice;
            return false;
        }

        /// <summary>子列表的现有条目数:优先用设备给出的计数 (<c>&lt;列表&gt;FE</c>),没有时数从 01 开始连续存在的条目。</summary>
        private static int CountItems(InstrumentSnapshot snapshot, string list)
        {
            if (DeviceIds.TryParseCount(snapshot.GetValue(DeviceIds.CountOf(list)), out var count))
            {
                return count;
            }

            count = 0;
            while (count < CommandDefinition.MaxIndex && snapshot.GetValue(DeviceIds.Item(list, count + 1)) != null)
            {
                count++;
            }

            return count;
        }
    }
}
