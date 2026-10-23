using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Zevision.Core.Configuration;

namespace Zevision.Core.Design
{
    /// <summary>设备上的一个传感器或输出源 (工艺编辑中材料的下拉框)。</summary>
    /// <param name="Number">编号,如 1 = <c>N01</c>。</param>
    /// <param name="Name">名称 (<c>N**01</c>、<c>S**01</c>)。</param>
    /// <param name="CrucibleCount">坩埚数量 (输出源的 <c>S**07</c>);传感器或未知时为 0。</param>
    public sealed record DeviceObjectInfo(int Number, string Name, int CrucibleCount);

    /// <summary>从设备的设置项 (<c>?</c> 或分组帮助 <c>?N</c>、<c>?S</c>) 中取出传感器和输出源。纯函数。</summary>
    public static class DesignDevice
    {
        /// <summary>传感器的组字母。</summary>
        public const char SensorGroup = 'N';

        /// <summary>输出源的组字母。</summary>
        public const char SourceGroup = 'S';

        private const string CrucibleCountSuffix = "07";

        /// <summary>
        /// 快照的全部设置项 (含数量项、只读项),转换为 <see cref="RecipeEntry"/>;ID 重复时只取第一次出现的。
        /// 用于 <see cref="Objects"/>、<see cref="DeviceContents.From"/> 和 <see cref="DeviceDeletion"/>。
        /// </summary>
        public static List<RecipeEntry> EntriesOf(InstrumentSnapshot snapshot)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            return snapshot.Entries
                .Where(e => seen.Add(e.Id))
                .Select(e => new RecipeEntry { Id = e.Id, Value = e.Value, Description = e.Description })
                .ToList();
        }

        /// <summary>该组的对象,按编号排列。</summary>
        /// <param name="entries">设备的设置项 (<see cref="EntriesOf"/> 或 <see cref="IInstrumentConfigReader.ReadGroupAsync"/>)。</param>
        /// <param name="group"><see cref="SensorGroup"/> 或 <see cref="SourceGroup"/>。</param>
        public static IReadOnlyList<DeviceObjectInfo> Objects(IEnumerable<RecipeEntry> entries, char group)
        {
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var list = entries.ToList();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in list)
            {
                values[entry.Id] = entry.Value;
            }

            var objects = new List<DeviceObjectInfo>();
            foreach (var entry in list)
            {
                // 名称项:组字母 + 两位编号 + 01,如 N0101。
                if (entry.Id.Length != 5 || entry.Id[0] != group || entry.Id.Substring(3) != "01"
                    || !int.TryParse(entry.Id.Substring(1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number)
                    || !DeviceIds.IsItemNumber(entry.Id.Substring(1, 2)) || number == 0)
                {
                    continue;
                }

                var objectId = entry.Id.Substring(0, 3);
                var crucibles = values.TryGetValue(objectId + CrucibleCountSuffix, out var count)
                    && DeviceIds.TryParseCount(count, out var parsed) ? parsed : 0;
                objects.Add(new DeviceObjectInfo(number, entry.Value, group == SourceGroup ? crucibles : 0));
            }

            return objects.OrderBy(o => o.Number).ToList();
        }
    }
}
