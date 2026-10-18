using System;
using System.Collections.Generic;
using System.Linq;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Models;

namespace Zevision.Services
{
    /// <summary>
    /// 修改窗口中正在编辑的配置 (<see cref="InstrumentRecipe"/>) 及对它的操作:查找对象和列表、替换重新读取的组、
    /// 加入或删除对象的项。不与设备通讯,也不读写文件。
    /// </summary>
    /// <remarks>
    /// 分组视图模型直接引用 <see cref="Entries"/> 中的 <see cref="RecipeEntry"/> 对象 (例如刷新分组标题),
    /// 因此修改值时改的是这些对象本身;要先确认能保存文件再修改时用 <see cref="Clone"/>。
    /// </remarks>
    public sealed class RecipeDocument
    {
        /// <summary>创建文档。</summary>
        public RecipeDocument(InstrumentRecipe recipe)
        {
            Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        }

        /// <summary>配置。</summary>
        public InstrumentRecipe Recipe { get; }

        /// <summary>全部配置项。</summary>
        public List<RecipeEntry> Entries => Recipe.Entries;

        /// <summary>对象的名称 (第 01 项,如材料 <c>M0301</c>);没有时为 null。</summary>
        public string? ObjectName(string objectId) => Entries.FirstOrDefault(e => e.Id == DeviceIds.NameOf(objectId))?.Value;

        /// <summary>配置中该组现有的对象 ID,如 <c>M01</c>、<c>P0201</c>。</summary>
        public HashSet<string> ObjectIds(char group) => ObjectIdsIn(Entries, group);

        /// <summary>配置项中该组的对象 ID。</summary>
        public static HashSet<string> ObjectIdsIn(IEnumerable<RecipeEntry> entries, char group)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (ObjectTypes.TryGetObject(entry.Id, out var type, out var objectId) && type.Group == group)
                {
                    ids.Add(objectId);
                }
            }

            return ids;
        }

        /// <summary>列表的条数,如 <c>F0103</c> 的预熔数 (不含 00 增删项和 FE 数量项)。</summary>
        public int ListItemCount(string listId) =>
            Entries
                .Where(e => e.Id.Length >= listId.Length + 2 && e.Id.StartsWith(listId, StringComparison.Ordinal))
                .Select(e => e.Id.Substring(listId.Length, 2))
                .Where(DeviceIds.IsItemNumber)
                .Distinct(StringComparer.Ordinal)
                .Count();

        /// <summary>用重新读取的数据替换该组的全部项 (保持在原来的位置)。</summary>
        public void ReplaceGroup(char group, IReadOnlyList<RecipeEntry> entries)
        {
            var index = Entries.FindIndex(e => e.Id[0] == group);
            Entries.RemoveAll(e => e.Id[0] == group);
            Entries.InsertRange(index < 0 ? Entries.Count : Math.Min(index, Entries.Count), entries);
        }

        /// <summary>从 <paramref name="source"/> (如重新读取的组) 复制对象的全部项,放在同组之后。</summary>
        public void AddObject(string objectId, IEnumerable<RecipeEntry> source) =>
            Insert(Copy(source.Where(e => BelongsTo(e, objectId))), objectId.Substring(0, 1));

        /// <summary>删除对象的全部项。</summary>
        public void RemoveObject(string objectId) => Entries.RemoveAll(e => BelongsTo(e, objectId));

        /// <summary>从 <paramref name="source"/> 复制以 <paramref name="itemId"/> 开头的项 (如新增的一条预熔),放在列表 <paramref name="listId"/> 之后。</summary>
        public void AddListItem(string listId, string itemId, IEnumerable<RecipeEntry> source) =>
            Insert(Copy(source.Where(e => e.Id.StartsWith(itemId, StringComparison.Ordinal))), listId);

        /// <summary>删除列表条目 (如 <c>F010302</c>) 及其子项。</summary>
        public void RemoveListItem(string itemId) => Entries.RemoveAll(e => e.Id.StartsWith(itemId, StringComparison.Ordinal));

        /// <summary>修改项的值 (按 ID)。</summary>
        public void SetValues(IReadOnlyDictionary<string, string> values)
        {
            foreach (var entry in Entries)
            {
                if (values.TryGetValue(entry.Id, out var value))
                {
                    entry.Value = value;
                }
            }
        }

        /// <summary>复制整份配置 (含全部项),修改副本不影响本文档。</summary>
        public RecipeDocument Clone() => new RecipeDocument(new InstrumentRecipe
        {
            FormatVersion = Recipe.FormatVersion,
            DeviceName = Recipe.DeviceName,
            ModelNumber = Recipe.ModelNumber,
            FirmwareVersion = Recipe.FirmwareVersion,
            SerialNumber = Recipe.SerialNumber,
            SavedAt = Recipe.SavedAt,
            Md5 = Recipe.Md5,
            Entries = Copy(Entries),
        });

        /// <summary>
        /// 核对新建/删除后的对象集合:新建的对象出现、删除的对象消失,且没有其他对象变化。
        /// </summary>
        /// <returns>应有但没有的对象和多出的对象;都为空表示符合预期。</returns>
        public static (IReadOnlyList<string> Missing, IReadOnlyList<string> Extra) CompareObjectChange(CommandActions action, string objectId, ISet<string> before, ISet<string> after)
        {
            var expected = new HashSet<string>(before, StringComparer.Ordinal);
            if (action == CommandActions.Add)
            {
                expected.Add(objectId);
            }
            else
            {
                expected.Remove(objectId);
            }

            return (
                expected.Except(after).OrderBy(i => i, StringComparer.Ordinal).ToList(),
                after.Except(expected).OrderBy(i => i, StringComparer.Ordinal).ToList());
        }

        private static bool BelongsTo(RecipeEntry entry, string objectId) =>
            ObjectTypes.TryGetObject(entry.Id, out _, out var owner) && owner == objectId;

        private static List<RecipeEntry> Copy(IEnumerable<RecipeEntry> entries) =>
            entries.Select(e => new RecipeEntry { Id = e.Id, Value = e.Value, Description = e.Description }).ToList();

        /// <summary>把项插入到以 <paramref name="afterPrefix"/> 开头的最后一项之后 (没有时插在同组之后,再没有时放在最后)。</summary>
        private void Insert(IReadOnlyList<RecipeEntry> entries, string afterPrefix)
        {
            var index = Entries.FindLastIndex(e => e.Id.StartsWith(afterPrefix, StringComparison.Ordinal));
            if (index < 0)
            {
                index = Entries.FindLastIndex(e => e.Id[0] == afterPrefix[0]);
            }

            Entries.InsertRange(index < 0 ? Entries.Count : index + 1, entries);
        }
    }
}
