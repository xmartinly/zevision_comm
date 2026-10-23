using System;
using System.Collections.Generic;
using System.Linq;
using Zevision.Core.Configuration;

namespace Zevision.Core.Design
{
    /// <summary>设备上的一个材料。</summary>
    /// <param name="Id">对象 ID,如 <c>M01</c>。</param>
    /// <param name="Name">名称 (<c>M**01</c>)。</param>
    /// <param name="Density">密度,g/cm³ (<c>M**03</c>);没有时为空。</param>
    /// <param name="ZRatio">Z 比 (<c>M**04</c>);没有时为空。</param>
    public sealed record DeviceMaterial(string Id, string Name, string Density, string ZRatio);

    /// <summary>设备上的一个膜系。</summary>
    /// <param name="Id">对象 ID,如 <c>F01</c>。</param>
    /// <param name="Name">名称 (<c>F**01</c>)。</param>
    /// <param name="Material">材料编号 (<c>F**02</c>,十进制);0 = 未选择。</param>
    public sealed record DeviceFilm(string Id, string Name, int Material);

    /// <summary>膜层膜系列表中的一项。</summary>
    /// <param name="Film">膜系编号 (<c>L**02**01</c>);0 = 未选择。</param>
    /// <param name="Rate">速率,Å/s (<c>L**02**02</c>)。</param>
    /// <param name="Thickness">最终厚度,kÅ (<c>L**02**03</c>)。</param>
    public sealed record DeviceLayerFilm(int Film, string Rate, string Thickness);

    /// <summary>设备上的一个膜层。</summary>
    /// <param name="Id">对象 ID,如 <c>L01</c>。</param>
    /// <param name="Name">名称 (<c>L**01</c>)。</param>
    /// <param name="Films">膜系列表,按序号。</param>
    public sealed record DeviceLayer(string Id, string Name, IReadOnlyList<DeviceLayerFilm> Films);

    /// <summary>设备上的一个工艺。</summary>
    /// <param name="Id">对象 ID,如 <c>P0201</c>。</param>
    /// <param name="Name">名称 (<c>P02**01</c>)。</param>
    /// <param name="Layers">膜层列表 (<c>P02**07**</c>) 中的膜层编号,按顺序;值不是数字的项为 0。</param>
    public sealed record DeviceProcess(string Id, string Name, IReadOnlyList<int> Layers);

    /// <summary>
    /// 设备上的材料、膜系、膜层和工艺 (工艺编辑窗口的"设备"页),从帮助表的设置项 (<c>?</c> 或分组帮助) 中取出。
    /// 列表按条目 ID 取 (不用数量项 <c>…FE</c>),所以分组帮助挑出的可恢复项也可以。纯函数。
    /// </summary>
    public sealed class DeviceContents
    {
        private const int MaterialDensity = 0x03;
        private const int MaterialZRatio = 0x04;
        private const int FilmMaterial = 0x02;
        private const int LayerFilms = 0x02;
        private const int ProcessLayers = 0x07;

        private readonly Dictionary<int, DeviceMaterial> _materialByNumber;
        private readonly Dictionary<int, DeviceFilm> _filmByNumber;
        private readonly Dictionary<int, DeviceLayer> _layerByNumber;

        private DeviceContents(IReadOnlyList<DeviceMaterial> materials, IReadOnlyList<DeviceFilm> films, IReadOnlyList<DeviceLayer> layers, IReadOnlyList<DeviceProcess> processes)
        {
            Materials = materials;
            Films = films;
            Layers = layers;
            Processes = processes;

            // 按编号查找 ("设备"页每个工艺的每个膜层都要找膜层、膜系、材料)。
            _materialByNumber = materials.ToDictionary(m => DeviceIds.LastNumber(m.Id));
            _filmByNumber = films.ToDictionary(f => DeviceIds.LastNumber(f.Id));
            _layerByNumber = layers.ToDictionary(l => DeviceIds.LastNumber(l.Id));
        }

        /// <summary>没有任何对象。</summary>
        public static DeviceContents Empty { get; } = new DeviceContents(Array.Empty<DeviceMaterial>(), Array.Empty<DeviceFilm>(), Array.Empty<DeviceLayer>(), Array.Empty<DeviceProcess>());

        /// <summary>材料,按 ID 排列。</summary>
        public IReadOnlyList<DeviceMaterial> Materials { get; }

        /// <summary>膜系,按 ID 排列。</summary>
        public IReadOnlyList<DeviceFilm> Films { get; }

        /// <summary>膜层,按 ID 排列。</summary>
        public IReadOnlyList<DeviceLayer> Layers { get; }

        /// <summary>工艺,按 ID 排列。</summary>
        public IReadOnlyList<DeviceProcess> Processes { get; }

        /// <summary>从设置项中取出 M、F、L、P 各组的对象。</summary>
        /// <param name="entries">设置项 (ID 重复时以第一次出现为准)。</param>
        public static DeviceContents From(IEnumerable<RecipeEntry> entries)
        {
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var objects = new Dictionary<char, SortedSet<string>>();
            foreach (var type in ObjectTypes.All)
            {
                objects[type.Group] = new SortedSet<string>(StringComparer.Ordinal);
            }

            foreach (var entry in entries)
            {
                if (!values.ContainsKey(entry.Id))
                {
                    values.Add(entry.Id, entry.Value ?? string.Empty);
                }

                if (ObjectTypes.TryGetObject(entry.Id, out var type, out var objectId))
                {
                    objects[type.Group].Add(objectId);
                }
            }

            string Value(string id) => values.TryGetValue(id, out var value) ? value.Trim() : string.Empty;
            int Number(string id) => DeviceIds.TryParseCount(Value(id), out var number) ? number : 0;

            // 列表中的条目编号:有 "列表 + 两位编号 + 后缀" 这一项的编号,按顺序 (00 增删项、FE 数量项除外)。
            IReadOnlyList<string> Items(string listId, string suffix) =>
                values.Keys
                    .Where(id => id.Length == listId.Length + 2 + suffix.Length && id.StartsWith(listId, StringComparison.Ordinal) && id.EndsWith(suffix, StringComparison.Ordinal))
                    .Select(id => id.Substring(listId.Length, 2))
                    .Where(n => DeviceIds.IsItemNumber(n) && Uri.IsHexDigit(n[0]) && Uri.IsHexDigit(n[1]))
                    .Select(n => listId + n)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList();

            var materials = objects[ObjectTypes.Material.Group]
                .Select(id => new DeviceMaterial(id, Value(DeviceIds.NameOf(id)), Value(DeviceIds.Item(id, MaterialDensity)), Value(DeviceIds.Item(id, MaterialZRatio))))
                .ToList();
            var films = objects[ObjectTypes.Film.Group]
                .Select(id => new DeviceFilm(id, Value(DeviceIds.NameOf(id)), Number(DeviceIds.Item(id, FilmMaterial))))
                .ToList();
            var layers = objects[ObjectTypes.Layer.Group]
                .Select(id => new DeviceLayer(
                    id,
                    Value(DeviceIds.NameOf(id)),
                    Items(DeviceIds.Item(id, LayerFilms), "01")
                        .Select(item => new DeviceLayerFilm(Number(DeviceIds.Item(item, 1)), Value(DeviceIds.Item(item, 2)), Value(DeviceIds.Item(item, 3))))
                        .ToList()))
                .ToList();
            var processes = objects[ObjectTypes.Process.Group]
                .Select(id => new DeviceProcess(id, Value(DeviceIds.NameOf(id)), Items(DeviceIds.Item(id, ProcessLayers), string.Empty).Select(Number).ToList()))
                .ToList();
            return new DeviceContents(materials, films, layers, processes);
        }

        /// <summary>编号为 <paramref name="number"/> 的材料 (如 16 = <c>M10</c>);没有时为 null。</summary>
        public DeviceMaterial? FindMaterial(int number) => _materialByNumber.TryGetValue(number, out var material) ? material : null;

        /// <summary>编号为 <paramref name="number"/> 的膜系;没有时为 null。</summary>
        public DeviceFilm? FindFilm(int number) => _filmByNumber.TryGetValue(number, out var film) ? film : null;

        /// <summary>编号为 <paramref name="number"/> 的膜层;没有时为 null。</summary>
        public DeviceLayer? FindLayer(int number) => _layerByNumber.TryGetValue(number, out var layer) ? layer : null;

        /// <summary>引用该材料的膜系 (<c>F**02</c>)。</summary>
        public IReadOnlyList<DeviceFilm> FilmsOf(DeviceMaterial material) =>
            Films.Where(f => f.Material == DeviceIds.LastNumber((material ?? throw new ArgumentNullException(nameof(material))).Id)).ToList();

        /// <summary>名称为 <paramref name="name"/> 的材料 (去掉首尾空格后比较,区分大小写);名称为空时没有。</summary>
        public IReadOnlyList<DeviceMaterial> MaterialsNamed(string? name) => Named(Materials, name, m => m.Name);

        /// <summary>名称为 <paramref name="name"/> 的工艺 (去掉首尾空格后比较,区分大小写);名称为空时没有。</summary>
        public IReadOnlyList<DeviceProcess> ProcessesNamed(string? name) => Named(Processes, name, p => p.Name);

        private static IReadOnlyList<T> Named<T>(IEnumerable<T> objects, string? name, Func<T, string> nameOf)
        {
            var trimmed = name?.Trim() ?? string.Empty;
            return trimmed.Length == 0
                ? Array.Empty<T>()
                : objects.Where(o => string.Equals(nameOf(o), trimmed, StringComparison.Ordinal)).ToList();
        }
    }
}
