using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zevision.Communication
{
    /// <summary>
    /// <c>?</c> 帮助响应中的一行:命令 ID、当前值、说明。
    /// </summary>
    public sealed class HelpTableEntry
    {
        /// <summary>创建条目。</summary>
        public HelpTableEntry(string id, string value, string description)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        /// <summary>命令 ID (不含动作字母),如 <c>F0101</c>。</summary>
        public string Id { get; }

        /// <summary>当前值,原样保留设备给出的文本;分组标题和动作命令为空字符串。</summary>
        public string Value { get; }

        /// <summary>说明。</summary>
        public string Description { get; }
    }

    /// <summary>
    /// 解析 <c>?</c> 帮助响应 (<c>?+:</c> 之后的数据)。
    /// </summary>
    /// <remarks>
    /// 实测格式 (IMC300 固件 1.8.11):以 LF 分隔的定宽表格,首行为表头
    /// <c>[CMD ID]  [VALUE]  [DESCRIPTION]</c>,列宽 30。列位置从表头读取,表头缺失时退回 30/60。
    /// 值可能含空格 (如 <c>Film 10</c>),因此必须按列位置截取,不能按空白分割。
    /// </remarks>
    public static class HelpTableParser
    {
        private const string ValueHeader = "[VALUE]";
        private const string DescriptionHeader = "[DESCRIPTION]";
        private const int DefaultValueColumn = 30;
        private const int DefaultDescriptionColumn = 60;

        private static readonly Regex IdPattern = new Regex("^[A-Z][0-9A-F]*$", RegexOptions.CultureInvariant);

        /// <summary>
        /// 解析帮助表。无法识别的行 (空行、表头、ID 格式不对的行) 被忽略。
        /// </summary>
        public static IReadOnlyList<HelpTableEntry> Parse(string text)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var valueColumn = DefaultValueColumn;
            var descriptionColumn = DefaultDescriptionColumn;
            var entries = new List<HelpTableEntry>();

            foreach (var line in lines)
            {
                var valueHeaderIndex = line.IndexOf(ValueHeader, StringComparison.Ordinal);
                var descriptionHeaderIndex = line.IndexOf(DescriptionHeader, StringComparison.Ordinal);
                if (valueHeaderIndex > 0 && descriptionHeaderIndex > valueHeaderIndex)
                {
                    valueColumn = valueHeaderIndex;
                    descriptionColumn = descriptionHeaderIndex;
                    continue;
                }

                var id = Column(line, 0, valueColumn);
                if (!IdPattern.IsMatch(id))
                {
                    continue;
                }

                entries.Add(new HelpTableEntry(
                    id,
                    Column(line, valueColumn, descriptionColumn),
                    Column(line, descriptionColumn, line.Length)));
            }

            return entries;
        }

        private static string Column(string line, int start, int end)
        {
            if (start >= line.Length)
            {
                return string.Empty;
            }

            return line.Substring(start, Math.Min(end, line.Length) - start).Trim();
        }
    }
}
