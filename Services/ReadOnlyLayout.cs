using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Localization;
using Zevision.Models;

namespace Zevision.Services
{
    /// <summary>修改配置窗口"只读"页中的一节。</summary>
    public sealed class ReadOnlySection
    {
        /// <summary>创建一节。</summary>
        public ReadOnlySection(string title, IReadOnlyList<string> ids)
        {
            Title = title;
            Ids = ids;
        }

        /// <summary>标题,如"设备信息"、"传感器 N01  Sensor"。</summary>
        public string Title { get; }

        /// <summary>要查询的 ID。</summary>
        public IReadOnlyList<string> Ids { get; }
    }

    /// <summary>
    /// "只读"页的内容:设备信息、工艺状态、对象数量,以及传感器、源、输入、输出的运行状态。
    /// 实机数据模式直接用刚读取的 <c>?</c> 结果填充;保存的配置模式在打开窗口时用 Q 指令读取。传感器等对象的编号取自配置文件。
    /// </summary>
    public static class ReadOnlyLayout
    {
        private static readonly Regex ObjectId = new Regex("^([NSIO][0-9A-F]{2})", RegexOptions.CultureInvariant);

        private static readonly ReadOnlySection[] FixedSections =
        {
            new ReadOnlySection(Lang.T("设备信息", "Device information"), new[] { "G03", "G02", "G05", "G04", "G06", "G07", "G08", "G01", "G180101", "G180102", "G0A", "G0B", "G0C" }),
            new ReadOnlySection("RS232", new[] { "G1301", "G1302", "G1303" }),
            new ReadOnlySection(Lang.T("工艺状态", "Process status"), new[] { "C09", "C0A", "C0B", "C0C", "C0D", "C0E", "C08", "C10", "C17", "C18", "C19", "C1A", "C1B", "C1F", "C20", "C21" }),
            new ReadOnlySection(Lang.T("对象数量", "Object counts"), new[] { "MFE", "FFE", "LFE", "P02FE", "NFE", "SFE", "IFE", "OFE" }),
        };

        /// <summary>每个对象要查询的项 (对象编号之后的部分)。</summary>
        private static readonly Dictionary<char, string[]> ObjectItems = new Dictionary<char, string[]>
        {
            ['N'] = new[] { "08", "09", "0C", "11", "1C", "1D", "1E", "1F" },
            ['S'] = new[] { "06", "08" },
            ['I'] = new[] { "03", "04", "05" },
            ['O'] = new[] { "03", "04", "05" },
        };

        /// <summary>
        /// 生成各节:固定的几节在前,然后按 传感器 → 源 → 输入 → 输出 列出配置文件中的每个对象。
        /// </summary>
        public static IReadOnlyList<ReadOnlySection> Build(IEnumerable<RecipeEntry> entries)
        {
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var list = entries.ToList();
            var objects = list
                .Select(e => ObjectId.Match(e.Id))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => "NSIO".IndexOf(id[0]))
                .ThenBy(id => id, StringComparer.Ordinal);

            var sections = new List<ReadOnlySection>(FixedSections);
            foreach (var objectId in objects)
            {
                var name = list.FirstOrDefault(e => e.Id == DeviceIds.NameOf(objectId))?.Value;
                var title = CommandText.ObjectKind(objectId[0]) + " " + objectId + (string.IsNullOrEmpty(name) ? string.Empty : "  " + name);
                sections.Add(new ReadOnlySection(title, ObjectItems[objectId[0]].Select(item => objectId + item).ToList()));
            }

            return sections;
        }
    }
}
