using System;
using System.Collections.Generic;
using System.Linq;
using Zevision.Commands;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;

namespace Zevision.Core.Design
{
    /// <summary>
    /// 在设备上一起删除的对象 (工艺编辑中删除材料或工艺时)。层级关系:工艺包含膜层,膜层包含膜系,膜系包含材料。
    /// <list type="bullet">
    /// <item>删除材料:这些材料、引用它们的膜系 (<c>F**02</c>)、用到这些膜系的膜层 (不论名称和膜系数量)。
    /// 工艺用到这些膜层时不能删除 (<see cref="References"/>)。</item>
    /// <item>删除工艺:这些工艺,以及只被它们使用的膜层;其他工艺也在用的膜层保留 (<see cref="Kept"/>)。
    /// 其他工艺的链接工艺 (<c>P02**0A</c>) 是它时不能删除。活动工艺 (<c>P01</c>) 是它时可以删除 (<see cref="ActiveProcess"/>,
    /// 用户决定 2026-10-23):设备接受删除并把 <c>P01</c> 改为 0 (实测,ZEV-STR-01 的 STR-2026-10-23-05)。</item>
    /// </list>
    /// </summary>
    /// <param name="Objects">
    /// 要删除的对象,按删除顺序:引用别人的组在前 (工艺、膜层、膜系、材料),组内编号大的在前
    /// (按编号删除的指令协议文档没有规定;万一设备删除后重新编号,也不会删到后面的对象)。
    /// </param>
    /// <param name="Kept">保留的膜层 (删除工艺时其他工艺也在用)。</param>
    /// <param name="References">设备上其他设置对要删除的对象的引用,如"工艺 P0201 P1: P02010701 = 10";有引用时不能删除。</param>
    public sealed record DeviceDeletion(IReadOnlyList<DesignObject> Objects, IReadOnlyList<DesignObject> Kept, IReadOnlyList<string> References)
    {
        /// <summary>设置活动工艺的设置项;值为工艺编号,0 为没有活动工艺。</summary>
        public const string ActiveProcessSetting = "P01";

        /// <summary>有其他设置引用要删除的对象:不能删除。</summary>
        public bool IsBlocked => References.Count > 0;

        /// <summary>要删除的工艺中设备的活动工艺 (<see cref="ActiveProcessSetting"/> 指向它),如 <c>P0201</c>;没有时为 null。删除后设备没有活动工艺。</summary>
        public string? ActiveProcess { get; init; }

        /// <summary>删除名称为 <paramref name="name"/> 的材料 (去掉首尾空格后比较,区分大小写;设备上可能有几个)。纯函数。</summary>
        /// <param name="name">材料名称。</param>
        /// <param name="entries">设备的设置项 (<c>?</c>,或 <c>?M</c>、<c>?F</c>、<c>?L</c>、<c>?P</c>、<c>?S</c>)。</param>
        public static DeviceDeletion ForMaterialNamed(string name, IReadOnlyList<RecipeEntry> entries)
        {
            var contents = DeviceContents.From(entries ?? throw new ArgumentNullException(nameof(entries)));
            return ForMaterials(contents.MaterialsNamed(name).Select(m => m.Id), entries, contents);
        }

        /// <summary>删除这些材料,以及引用它们的膜系、用到这些膜系的膜层。设备上没有的 ID 忽略。纯函数。</summary>
        /// <param name="materialIds">材料 ID,如 <c>M01</c>。</param>
        /// <param name="entries">设备的设置项。</param>
        public static DeviceDeletion ForMaterials(IEnumerable<string> materialIds, IReadOnlyList<RecipeEntry> entries) =>
            ForMaterials(materialIds, entries, DeviceContents.From(entries ?? throw new ArgumentNullException(nameof(entries))));

        /// <summary>删除名称为 <paramref name="name"/> 的工艺 (去掉首尾空格后比较,区分大小写;设备上可能有几个)。纯函数。</summary>
        /// <param name="name">工艺名称。</param>
        /// <param name="entries">设备的设置项 (<c>?</c>,或 <c>?P</c>、<c>?L</c>)。</param>
        public static DeviceDeletion ForProcessNamed(string name, IReadOnlyList<RecipeEntry> entries)
        {
            var contents = DeviceContents.From(entries ?? throw new ArgumentNullException(nameof(entries)));
            return ForProcesses(contents.ProcessesNamed(name).Select(p => p.Id), entries, contents);
        }

        /// <summary>删除这些工艺,以及只被它们使用的膜层。设备上没有的 ID 忽略。纯函数。</summary>
        /// <param name="processIds">工艺 ID,如 <c>P0201</c>。</param>
        /// <param name="entries">设备的设置项。</param>
        public static DeviceDeletion ForProcesses(IEnumerable<string> processIds, IReadOnlyList<RecipeEntry> entries) =>
            ForProcesses(processIds, entries, DeviceContents.From(entries ?? throw new ArgumentNullException(nameof(entries))));

        /// <summary>删除设备上的全部材料,以及引用它们的膜系、用到这些膜系的膜层 (工艺用到这些膜层时不能删除)。纯函数。</summary>
        /// <param name="entries">设备的设置项。</param>
        public static DeviceDeletion ForAllMaterials(IReadOnlyList<RecipeEntry> entries)
        {
            var contents = DeviceContents.From(entries ?? throw new ArgumentNullException(nameof(entries)));
            return ForMaterials(contents.Materials.Select(m => m.Id), entries, contents);
        }

        /// <summary>删除设备上的全部工艺,以及它们用到的膜层 (没有其他工艺保留它们)。纯函数。</summary>
        /// <param name="entries">设备的设置项。</param>
        public static DeviceDeletion ForAllProcesses(IReadOnlyList<RecipeEntry> entries)
        {
            var contents = DeviceContents.From(entries ?? throw new ArgumentNullException(nameof(entries)));
            return ForProcesses(contents.Processes.Select(p => p.Id), entries, contents);
        }

        private static DeviceDeletion ForMaterials(IEnumerable<string> materialIds, IReadOnlyList<RecipeEntry> entries, DeviceContents contents)
        {
            if (materialIds is null)
            {
                throw new ArgumentNullException(nameof(materialIds));
            }

            var ids = new HashSet<string>(materialIds, StringComparer.Ordinal);
            var materials = contents.Materials.Where(m => ids.Contains(m.Id)).ToList();
            var materialNumbers = new HashSet<int>(materials.Select(m => DeviceIds.LastNumber(m.Id)));
            var films = contents.Films.Where(f => materialNumbers.Contains(f.Material)).ToList();
            var filmNumbers = new HashSet<int>(films.Select(f => DeviceIds.LastNumber(f.Id)));
            var layers = contents.Layers.Where(l => l.Films.Any(f => filmNumbers.Contains(f.Film))).ToList();

            var objects = Descending(layers.Select(l => new DesignObject(l.Id, ObjectTypes.Layer.Group, l.Name, false)))
                .Concat(Descending(films.Select(f => new DesignObject(f.Id, ObjectTypes.Film.Group, f.Name, false))))
                .Concat(Descending(materials.Select(m => new DesignObject(m.Id, ObjectTypes.Material.Group, m.Name, false))))
                .ToList();
            return new DeviceDeletion(objects, Array.Empty<DesignObject>(), ExternalReferences(entries, objects, RecipeRules.IndexReferences(entries)));
        }

        private static DeviceDeletion ForProcesses(IEnumerable<string> processIds, IReadOnlyList<RecipeEntry> entries, DeviceContents contents)
        {
            if (processIds is null)
            {
                throw new ArgumentNullException(nameof(processIds));
            }

            var ids = new HashSet<string>(processIds, StringComparer.Ordinal);
            var processes = contents.Processes.Where(p => ids.Contains(p.Id)).ToList();
            var deletedProcesses = new HashSet<string>(processes.Select(p => p.Id), StringComparer.Ordinal);
            var layerNumbers = new HashSet<int>(processes.SelectMany(p => p.Layers));
            var references = RecipeRules.IndexReferences(entries);

            // 其他工艺 (或其他对象) 也引用的膜层保留。
            bool UsedElsewhere(DeviceLayer layer) =>
                references[layer.Id].Any(e => !(ObjectTypes.TryGetObject(e.Id, out _, out var owner) && deletedProcesses.Contains(owner)));

            var layers = contents.Layers.Where(l => layerNumbers.Contains(DeviceIds.LastNumber(l.Id))).ToList();
            var kept = layers.Where(UsedElsewhere).Select(l => new DesignObject(l.Id, ObjectTypes.Layer.Group, l.Name, false)).ToList();
            var keptIds = new HashSet<string>(kept.Select(l => l.Id), StringComparer.Ordinal);

            var objects = Descending(processes.Select(p => new DesignObject(p.Id, ObjectTypes.Process.Group, p.Name, false)))
                .Concat(Descending(layers.Where(l => !keptIds.Contains(l.Id)).Select(l => new DesignObject(l.Id, ObjectTypes.Layer.Group, l.Name, false))))
                .ToList();

            // 活动工艺不阻止删除,单独给出 (确认时说明,删除后读回 P01)。
            var active = entries.FirstOrDefault(e => e.Id == ActiveProcessSetting) is { } setting
                && DeviceIds.TryParseCount(setting.Value, out var number)
                && number > 0
                && deletedProcesses.Contains(DeviceIds.Item(ObjectTypes.Process.Prefix, number))
                    ? DeviceIds.Item(ObjectTypes.Process.Prefix, number)
                    : null;
            return new DeviceDeletion(objects, kept, ExternalReferences(entries, objects, references, e => e.Id == ActiveProcessSetting))
            {
                ActiveProcess = active,
            };
        }

        /// <summary>组内按编号从大到小 (ID 等长,按序数比较即按编号)。</summary>
        private static IEnumerable<DesignObject> Descending(IEnumerable<DesignObject> objects) =>
            objects.OrderByDescending(o => o.Id, StringComparer.Ordinal);

        /// <summary>
        /// 要删除的集合之外对它们的引用 (集合中对象自己的设置项,如膜系引用材料,不算;<paramref name="ignore"/> 的也不算)。
        /// 属于对象的设置项写为"工艺 P0202 P2: P02020701 = 1",其他设置项写为"坩埚 1 · 材料 ID S010901 = 1"。
        /// </summary>
        private static IReadOnlyList<string> ExternalReferences(
            IReadOnlyList<RecipeEntry> entries, IReadOnlyList<DesignObject> objects, ILookup<string, RecipeEntry> references, Func<RecipeEntry, bool>? ignore = null)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (!names.ContainsKey(entry.Id))
                {
                    names.Add(entry.Id, entry.Value?.Trim() ?? string.Empty);
                }
            }

            var deleted = new HashSet<string>(objects.Select(o => o.Id), StringComparer.Ordinal);
            var found = new List<string>();
            foreach (var target in objects)
            {
                foreach (var entry in references[target.Id].Where(e => ignore?.Invoke(e) != true))
                {
                    var setting = entry.Id + " = " + entry.Value;
                    if (!ObjectTypes.TryGetObject(entry.Id, out var type, out var owner))
                    {
                        found.Add(ZevisionCommandSet.Find(entry.Id) is CommandDefinition definition ? definition.LabelFor(entry.Id) + " " + setting : setting);
                    }
                    else if (!deleted.Contains(owner))
                    {
                        var name = names.TryGetValue(DeviceIds.NameOf(owner), out var value) ? value : string.Empty;
                        found.Add(CommandText.ObjectKind(type.Group) + " " + owner + " " + name + ": " + setting);
                    }
                }
            }

            return found.Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
