using System;
using System.Collections.Generic;
using System.Linq;
using Zevision.Localization;
using Zevision.Models;

namespace Zevision.Services
{
    /// <summary>
    /// 修改配置时按"项目"(所属对象) 分组的一组配置项。
    /// </summary>
    public sealed class RecipeGroup
    {
        /// <summary>创建分组。</summary>
        public RecipeGroup(string key, string title, IReadOnlyList<RecipeEntry> entries)
        {
            Key = key;
            Title = title;
            Entries = entries;
        }

        /// <summary>对象 ID,如 <c>M03</c>、<c>P0201</c>。</summary>
        public string Key { get; }

        /// <summary>显示标题,如"材料 M03  M3"。</summary>
        public string Title { get; }

        /// <summary>组内配置项,保持文件中的顺序。</summary>
        public IReadOnlyList<RecipeEntry> Entries { get; }
    }

    /// <summary>
    /// 把配置项按所属对象分组,用于修改配置窗口。
    /// </summary>
    public static class RecipeGrouping
    {
        /// <summary>
        /// 分组并排序:按恢复顺序 (系统设置 → 材料 → 膜层 → 层 → 工艺),组内保持文件顺序。
        /// 标题中附上对象名称 (组内说明含 "Name" 的第一项,如材料名、膜层名)。
        /// </summary>
        public static IReadOnlyList<RecipeGroup> Build(IEnumerable<RecipeEntry> entries)
        {
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            return entries
                .GroupBy(e => ObjectTypes.GroupKey(e.Id), StringComparer.Ordinal)
                .OrderBy(g => RecipeRules.GroupRank(g.Key))
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new RecipeGroup(g.Key, BuildTitle(g.Key, g.ToList()), g.ToList()))
                .ToList();
        }

        /// <summary>分组标题,如"材料 M03  Au":对象名称取组内说明含 "Name" 的 <c>01</c> 项。</summary>
        private static string BuildTitle(string key, IReadOnlyList<RecipeEntry> entries)
        {
            var systemTitle = key switch
            {
                "G0E" => Lang.T("显示", "Display"),
                "G0F" => Lang.T("声音", "Sound"),
                "G12" => Lang.T("图表与晶振寿命", "Graph and crystal life"),
                _ => null,
            };
            if (systemTitle != null)
            {
                return Lang.T($"系统 {key}  {systemTitle}", $"System {key}  {systemTitle}");
            }

            var kind = key[0] switch
            {
                'G' => Lang.T("系统", "System"),
                'T' => Lang.T("逻辑", "Logic"),
                'M' or 'F' or 'L' or 'P' or 'N' or 'S' or 'I' or 'O' => CommandText.ObjectKind(key[0]),
                _ => Lang.T("其他", "Other"),
            };
            var name = entries.FirstOrDefault(e =>
                e.Id == DeviceIds.NameOf(key) && e.Description.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0)?.Value;
            return string.IsNullOrEmpty(name) ? $"{kind} {key}" : $"{kind} {key}  {name}";
        }
    }
}
