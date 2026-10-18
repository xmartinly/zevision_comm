using System.Collections.Generic;
using Zevision.Commands;

namespace Zevision.Core.Localization
{
    /// <summary>
    /// 指令集 (Zevision.Commands) 的界面文本:按 <see cref="Lang.Current"/> 选择中英文。
    /// 指令库本身不依赖界面语言,名称、单位、选项都同时给出中英文。
    /// </summary>
    public static class CommandText
    {
        /// <summary>当前界面语言对应的指令文本语言。</summary>
        public static TextLanguage Language => Lang.IsChinese ? TextLanguage.Chinese : TextLanguage.English;

        /// <summary>说明,如 <c>密度</c> / <c>Density (g/cm^3)</c>。</summary>
        public static string Name(this CommandDefinition definition) => definition.GetName(Language);

        /// <summary>带单位的名称,如 <c>密度 (g/cm³)</c>。</summary>
        public static string Label(this CommandDefinition definition) => definition.GetLabel(Language);

        /// <summary>单位,如 <c>秒</c> / <c>s</c>。</summary>
        public static string UnitText(this CommandDefinition definition) => definition.Unit.Get(Language);

        /// <summary>补充说明。</summary>
        public static string RemarkText(this CommandDefinition definition) => definition.Remark.Get(Language);

        /// <summary>范围或选项说明,如 <c>0.100 ~ 100.000 g/cm³</c>。</summary>
        public static string RangeText(this CommandDefinition definition) => definition.GetRangeText(Language);

        /// <summary>各级编号的含义,如 膜系、预熔。</summary>
        public static IReadOnlyList<string> IndexNames(this CommandDefinition definition) => definition.GetIndexNames(Language);

        /// <summary>某一项的显示名称,如 <c>预熔 2 · 起始斜坡/上升时间</c>。</summary>
        public static string LabelFor(this CommandDefinition definition, string id, string? deviceDescription = null) =>
            definition.LabelFor(id, Language, deviceDescription);

        /// <summary>选项的含义,如 <c>关</c> / <c>Off</c>。</summary>
        public static string TextValue(this CommandOption option) => option.Text.Get(Language);

        /// <summary>中英文文本中当前语言的一段。</summary>
        public static string Value(this BilingualText text) => text.Get(Language);

        /// <summary>对象类型的名称,如 <c>M</c> → 材料 / Material。</summary>
        public static string ObjectKind(char group) => ZevisionCommandSet.ObjectKind(group, Language);
    }
}
