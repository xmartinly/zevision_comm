using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Zevision.Commands;
using Zevision.Localization;

namespace Zevision.Services
{
    /// <summary>对象的子列表在修改窗口中的操作方式。</summary>
    public enum ObjectListStyle
    {
        /// <summary>分组标题中的"+ / −"按钮:在末尾追加一项 (设备默认值)、删除最后一项。</summary>
        HeaderButtons,

        /// <summary>同 <see cref="HeaderButtons"/>;列表为空时在分组中显示说明和添加按钮。</summary>
        HeaderButtonsWithEmptyRow,

        /// <summary>每一项有删除按钮 (删除中间项时后面的项前移),标题中有选择被引用对象的"添加"行。</summary>
        ItemRows,
    }

    /// <summary>对象的一个子列表,如膜系的预条件 (<c>F**03</c>)。</summary>
    /// <param name="Suffix">列表在对象中的编号,如 <c>03</c>。</param>
    /// <param name="Style">修改窗口中的操作方式。</param>
    public sealed record ObjectList(string Suffix, ObjectListStyle Style);

    /// <summary>
    /// 新建对象后程序设置的默认引用:把被引用组中编号最小的对象写入新对象的一项。
    /// </summary>
    /// <param name="ReferencedGroup">被引用的组,如膜系引用材料 <c>M</c>。</param>
    /// <param name="ListSuffix">先在新对象的这个列表末尾追加一项 (如膜层的膜系列表 <c>02</c>);为 null 时直接写对象自己的项。</param>
    /// <param name="SettingSuffix">写入的项,相对于新对象或追加的条目 (如膜系的材料 <c>02</c>);为空时写追加的条目本身。</param>
    public sealed record ObjectDefault(char ReferencedGroup, string? ListSuffix, string SettingSuffix);

    /// <summary>
    /// 可以按编号新建/删除的对象类型:材料、膜系、膜层、工艺。层级 (用户确认):工艺包含膜层,膜层包含膜系,膜系包含材料。
    /// </summary>
    public sealed class ObjectType
    {
        private readonly Regex _settingOfObject;

        internal ObjectType(char group, string prefix, ObjectDefault? newDefault, params ObjectList[] lists)
        {
            Group = group;
            Prefix = prefix;
            NewDefault = newDefault;
            Lists = lists;
            _settingOfObject = new Regex("^(" + prefix + "[0-9A-F]{2})[0-9A-F]{2}", RegexOptions.CultureInvariant);
        }

        /// <summary>组字母,如 <c>M</c>。</summary>
        public char Group { get; }

        /// <summary>对象 ID 的前缀:组字母;工艺为 <c>P02</c> (<c>P01</c> 是活动工艺,不是对象)。</summary>
        public string Prefix { get; }

        /// <summary>新建后设置的默认引用;没有时为 null。</summary>
        public ObjectDefault? NewDefault { get; }

        /// <summary>修改窗口中可以增删的子列表。</summary>
        public IReadOnlyList<ObjectList> Lists { get; }

        /// <summary>名称 (界面语言),如"材料" / "Material"。</summary>
        public string Kind => CommandText.ObjectKind(Group);

        /// <summary>子列表在指令集中的模式,如 <c>F**03</c> (用于取列表名称和条目的定义)。</summary>
        public string ListPattern(ObjectList list) => Prefix + "**" + (list ?? throw new ArgumentNullException(nameof(list))).Suffix;

        /// <summary>
        /// 第一个未使用的编号 (1~<see cref="CommandDefinition.MaxIndex"/>),如 <c>M02</c>、<c>P0203</c>;用完时为 null。
        /// </summary>
        public string? NextFreeId(ISet<string> used)
        {
            if (used is null)
            {
                throw new ArgumentNullException(nameof(used));
            }

            return Enumerable.Range(CommandDefinition.MinIndex, CommandDefinition.MaxIndex - CommandDefinition.MinIndex + 1)
                .Select(number => DeviceIds.Item(Prefix, number))
                .FirstOrDefault(id => !used.Contains(id));
        }

        /// <summary>配置项所属的对象,如 <c>M0301</c> → <c>M03</c>。编号 00 (增删指令) 和 FE (数量) 不是对象。</summary>
        public bool TryGetObjectId(string id, out string objectId)
        {
            var match = _settingOfObject.Match(id ?? string.Empty);
            objectId = match.Success ? match.Groups[1].Value : string.Empty;
            return match.Success && DeviceIds.IsItemNumber(objectId.Substring(objectId.Length - 2));
        }

        /// <inheritdoc />
        public override string ToString() => Prefix;
    }

    /// <summary>
    /// 对象类型表:哪些组可以新建/删除、对象 ID 的前缀、可增删的子列表和新建后的默认引用。
    /// 修改窗口、恢复计划和引用检查都按这张表处理对象,不各自判断组字母。
    /// </summary>
    public static class ObjectTypes
    {
        /// <summary>材料:<c>M**</c>。</summary>
        public static readonly ObjectType Material = new ObjectType('M', "M", null);

        /// <summary>膜系:<c>F**</c>,材料 ID (<c>F**02</c>) 默认为第一个材料;预条件、后条件可以增删。</summary>
        public static readonly ObjectType Film = new ObjectType(
            'F', "F", new ObjectDefault('M', null, "02"), new ObjectList("03", ObjectListStyle.HeaderButtons), new ObjectList("05", ObjectListStyle.HeaderButtons));

        /// <summary>
        /// 膜层:<c>L**</c>,膜系列表 (<c>L**02</c>) 默认添加一项,其膜系为第一个膜系。
        /// 膜层的速率和最终厚度属于膜系列表中的每一项,新膜层的列表为空 (实机 <c>L02FE = 0</c>),所以空列表要提示。
        /// </summary>
        public static readonly ObjectType Layer = new ObjectType(
            'L', "L", new ObjectDefault('F', "02", "01"), new ObjectList("02", ObjectListStyle.HeaderButtonsWithEmptyRow));

        /// <summary>工艺:<c>P02**</c>,膜层列表 (<c>P02**07</c>) 默认添加第一个膜层,可以添加任意膜层、删除任意一项。</summary>
        public static readonly ObjectType Process = new ObjectType(
            'P', "P02", new ObjectDefault('L', "07", string.Empty), new ObjectList("07", ObjectListStyle.ItemRows));

        /// <summary>全部类型,按被引用的在前排列 (材料 → 膜系 → 膜层 → 工艺)。</summary>
        public static IReadOnlyList<ObjectType> All { get; } = new[] { Material, Film, Layer, Process };

        /// <summary>组字母对应的类型;不能新建/删除的组为 null。</summary>
        public static ObjectType? Find(char group) => All.FirstOrDefault(t => t.Group == group);

        /// <summary>配置项所属的对象,如 <c>F01030101</c> → 膜系 <c>F01</c>。</summary>
        public static bool TryGetObject(string id, out ObjectType type, out string objectId)
        {
            foreach (var candidate in All)
            {
                if (candidate.TryGetObjectId(id, out objectId))
                {
                    type = candidate;
                    return true;
                }
            }

            type = Material;
            objectId = string.Empty;
            return false;
        }

        /// <summary>对象 ID (如 <c>M03</c>、<c>P0201</c>) 的类型;不是对象 ID 时 (如 <c>P01</c>) 为 null。</summary>
        public static ObjectType? OfObjectId(string objectId) =>
            TryGetObject(DeviceIds.NameOf(objectId ?? string.Empty), out var type, out var id) && id == objectId ? type : null;

        /// <summary>
        /// 配置项在修改窗口中所属的分组:对象 ID (如 <c>F01030101</c> → <c>F01</c>,<c>P020101</c> → <c>P0201</c>),
        /// 其他组为组字母 + 第一级编号 (如 <c>G0E02</c> → <c>G0E</c>)。
        /// </summary>
        public static string GroupKey(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException(Lang.T("ID 不能为空。", "The ID must not be empty."), nameof(id));
            }

            var prefix = Find(id[0])?.Prefix;
            var length = (prefix != null && id.StartsWith(prefix, StringComparison.Ordinal) ? prefix.Length : 1) + 2;
            return id.Length >= length ? id.Substring(0, length) : id.Substring(0, Math.Min(id.Length, 3));
        }
    }
}
