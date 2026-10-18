using System;
using System.Collections.Generic;
using System.Linq;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Localization;
using Zevision.Models;

namespace Zevision.Services
{
    /// <summary>
    /// 恢复计划:对比配置文件与设备快照,算出需要创建的对象/子列表条目、需要写入的项和跳过的项。
    /// 纯计算,不与设备通讯,便于测试。执行见 <see cref="InstrumentConfigService"/>。
    /// </summary>
    public static class RestorePlanner
    {
        /// <summary>设备空闲时 C09 的值。</summary>
        public const string ReadyState = "Ready";

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
                return Result<RestorePlan>.Fail(Lang.T("无法识别设备 (帮助表中没有 G03 设备名称)。", "Cannot identify the device (no G03 device name in the help table)."));
            }

            if (!string.Equals(recipe.DeviceName, snapshot.DeviceName, StringComparison.OrdinalIgnoreCase)
                || (recipe.ModelNumber.Length > 0 && snapshot.ModelNumber.Length > 0
                    && !string.Equals(recipe.ModelNumber, snapshot.ModelNumber, StringComparison.OrdinalIgnoreCase)))
            {
                return Result<RestorePlan>.Fail(
                    Lang.T($"设备型号不符:文件来自 {recipe.DeviceName} {recipe.ModelNumber},当前设备是 {snapshot.DeviceName} {snapshot.ModelNumber}。", $"Device model mismatch: the file is from {recipe.DeviceName} {recipe.ModelNumber}, the connected device is {snapshot.DeviceName} {snapshot.ModelNumber}."));
            }

            if (!string.Equals(snapshot.CurrentState, ReadyState, StringComparison.OrdinalIgnoreCase))
            {
                return Result<RestorePlan>.Fail(
                    Lang.T(
                        $"设备当前状态为 {snapshot.CurrentState ?? "(未知)"},只有在 {ReadyState} (空闲) 状态下才能恢复配置。",
                        $"The device state is {snapshot.CurrentState ?? "(unknown)"}; the configuration can only be restored in the {ReadyState} (idle) state."));
            }

            var warnings = new List<string>();
            if (!string.Equals(recipe.FirmwareVersion, snapshot.FirmwareVersion, StringComparison.Ordinal))
            {
                warnings.Add(Lang.T($"固件版本不同:文件 {recipe.FirmwareVersion},设备 {snapshot.FirmwareVersion}。部分设置项的含义可能不同。", $"Different firmware: file {recipe.FirmwareVersion}, device {snapshot.FirmwareVersion}. Some settings may have a different meaning."));
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
                var kind = snapshot.GetDescription(list) is string description && description.Length > 0 ? description + Lang.T(" 条目", " entry") : Lang.T("子列表条目", "sub-list entry");
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
                    skipped.Add(new RestoreSkip(entry, Lang.T("文件中重复出现", "appears more than once in the file")));
                    continue;
                }

                if (!RecipeRules.IsRestorable(entry.Id, entry.Value, out var reason))
                {
                    skipped.Add(new RestoreSkip(entry, Lang.T("不在可恢复范围: ", "not restorable: ") + reason));
                    continue;
                }

                if (ProtocolText.ValidateValue(entry.Value) != null)
                {
                    skipped.Add(new RestoreSkip(entry, Lang.T("值含有协议不支持的字符 (非 ASCII、; 或 { })", "the value contains characters the protocol does not allow (non-ASCII, ; or { })")));
                    continue;
                }

                var current = snapshot.GetValue(entry.Id);
                if (current == null)
                {
                    // 所属对象将被创建:创建后重新计划时再处理,这里不算跳过。
                    if (!deferred.Contains(entry.Id))
                    {
                        skipped.Add(new RestoreSkip(entry, notCreatable.TryGetValue(entry.Id, out var why) ? why : Lang.T("设备上不存在该项", "not present on the device")));
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
                    skipped.Add(new RestoreSkip(entry, Lang.T($"引用的{targetKind} {targetId} 在设备上不存在,文件中也没有它的设置", $"the referenced {targetKind} {targetId} does not exist on the device and is not in the file")));
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
                    reason = Lang.T($"所属对象 {ancestor} 在设备上不存在,且该类对象不能自动创建", $"the owning object {ancestor} does not exist on the device and cannot be created automatically");
                    return false;
                }

                var list = ancestor.Substring(0, length - 2);
                if (snapshot.GetValue(DeviceIds.AppendSlotOf(list)) == null || !DeviceIds.IsItemNumber(ancestor.Substring(length - 2)))
                {
                    reason = Lang.T($"所属条目 {ancestor} 在设备上不存在,且子列表 {list} 不支持追加", $"the owning entry {ancestor} does not exist on the device and the sub-list {list} cannot be appended to");
                    return false;
                }

                var count = CountItems(snapshot, list);
                for (var n = 1; n <= count; n++)
                {
                    if (snapshot.GetValue(DeviceIds.Item(list, n)) == null)
                    {
                        reason = Lang.T($"子列表 {list} 的现有条目编号不连续,无法通过追加对齐到 {ancestor}", $"the existing entries of sub-list {list} are not numbered contiguously, so appending cannot reach {ancestor}");
                        return false;
                    }
                }

                listId = list;
                number = DeviceIds.LastNumber(ancestor);
                reason = string.Empty;
                return true;
            }

            reason = Lang.T("设备上不存在该项 (所属对象存在,但没有这个设置)", "not present on the device (the object exists but has no such setting)");
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
