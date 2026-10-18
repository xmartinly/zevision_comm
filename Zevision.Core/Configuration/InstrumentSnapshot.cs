using System;
using System.Collections.Generic;
using System.Globalization;
using Zevision.Commands;
using Zevision.Communication;

namespace Zevision.Core.Configuration
{
    /// <summary>
    /// 通过 <c>?</c> 读取的设备配置快照。
    /// </summary>
    public sealed class InstrumentSnapshot
    {
        /// <summary>设备名 (型号) 的 ID。</summary>
        public const string DeviceNameId = "G03";

        /// <summary>当前状态的 ID (工艺状态)。</summary>
        public const string StateId = "C09";

        /// <summary>空闲状态的值:只有在这个状态下才修改设置。</summary>
        public const string ReadyState = "Ready";

        private readonly Dictionary<string, HelpTableEntry> _byId = new Dictionary<string, HelpTableEntry>(StringComparer.Ordinal);

        /// <summary>创建快照。ID 重复时以第一次出现为准。</summary>
        public InstrumentSnapshot(IReadOnlyList<HelpTableEntry> entries)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
            foreach (var entry in entries)
            {
                if (!_byId.ContainsKey(entry.Id))
                {
                    _byId.Add(entry.Id, entry);
                }
            }
        }

        /// <summary>帮助表中的全部条目,保持设备给出的顺序。</summary>
        public IReadOnlyList<HelpTableEntry> Entries { get; }

        /// <summary>设备名称 (G03)。</summary>
        public string DeviceName => GetValue(DeviceNameId) ?? string.Empty;

        /// <summary>型号 (G05)。</summary>
        public string ModelNumber => GetValue("G05") ?? string.Empty;

        /// <summary>固件版本 (G08)。</summary>
        public string FirmwareVersion => GetValue("G08") ?? string.Empty;

        /// <summary>序列号 (G06)。</summary>
        public string SerialNumber => GetValue("G06") ?? string.Empty;

        /// <summary>当前运行状态 (C09),空闲时为 <c>Ready</c>。</summary>
        public string? CurrentState => GetValue(StateId);

        /// <summary>
        /// 生成配置文件的内容:设备信息,以及按 <see cref="RecipeRules.SelectRestorable"/> 挑出的可恢复项。
        /// </summary>
        public InstrumentRecipe ToRecipe(DateTimeOffset savedAt) => new InstrumentRecipe
        {
            DeviceName = DeviceName,
            ModelNumber = ModelNumber,
            FirmwareVersion = FirmwareVersion,
            SerialNumber = SerialNumber,
            SavedAt = savedAt.ToString("o", CultureInfo.InvariantCulture),
            Entries = RecipeRules.SelectRestorable(Entries),
        };

        /// <summary>按 ID 取当前值;设备上没有该项时返回 null。</summary>
        public string? GetValue(string id) => _byId.TryGetValue(id, out var entry) ? entry.Value : null;

        /// <summary>按 ID 取说明;设备上没有该项时返回 null。</summary>
        public string? GetDescription(string id) => _byId.TryGetValue(id, out var entry) ? entry.Description : null;
    }
}
