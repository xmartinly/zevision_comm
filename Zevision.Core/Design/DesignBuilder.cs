using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Zevision.Commands;
using Zevision.Core.Configuration;

namespace Zevision.Core.Design
{
    /// <summary>工艺一步中可输入的值 (输入框的检查,<see cref="DesignRules.CheckStepValue"/>)。</summary>
    public enum StepField
    {
        /// <summary>速率,Å/s (<c>L**020102</c>)。</summary>
        Rate,

        /// <summary>最终厚度,kÅ (<c>L**020103</c>)。</summary>
        Thickness,

        /// <summary>速率斜坡的起始厚度,kÅ (<c>L**020106**01</c>)。</summary>
        RampStartThickness,

        /// <summary>速率斜坡的斜坡时间,H:MM:SS (<c>L**020106**02</c>)。</summary>
        RampTime,

        /// <summary>速率斜坡的新速率,Å/s (<c>L**020106**03</c>)。</summary>
        RampNewRate,
    }

    /// <summary>上传一个工艺用到的设备对象,如材料 <c>M0B</c> "Al"。</summary>
    /// <param name="Id">对象 ID,如 <c>M0B</c>、<c>P0209</c>。</param>
    /// <param name="Group">组字母:M、F、L 或 P。</param>
    /// <param name="Name">名称。</param>
    /// <param name="IsNew">上传时新建;为 false 时是设备上已有、名称和参数都相同的对象 (复用,不写入)。</param>
    public sealed record DesignObject(string Id, char Group, string Name, bool IsNew);

    /// <summary>
    /// 一个工艺的上传内容:只含新建对象的配置 (交给 <c>RestorePlanner</c> 和
    /// <c>IInstrumentConfigWriter.ExecuteRestoreAsync</c> 新建、写入并回读校验),以及用到的对象 (新建的和复用的)。
    /// </summary>
    /// <param name="Recipe">新建对象的全部设置项;文件头取自设备 (型号、固件版本)。</param>
    /// <param name="Objects">用到的对象,按材料、膜系、膜层、工艺排列。</param>
    public sealed record DesignBuild(InstrumentRecipe Recipe, IReadOnlyList<DesignObject> Objects);

    /// <summary>
    /// 把工艺转换为设备上的对象:用到的每种材料对应一个材料 (M) 和一个同名膜系 (F),每一步对应一个膜层 (L),
    /// 再新建一个工艺 (P02),膜层列表按步骤顺序。设备上已有名称和参数都相同的材料、膜系,参数相同的膜层 (不比较名称) 时复用它 (不写入),
    /// 只有不一致时才新建;新建的膜层名为 <c>材料名称-工艺名称-步骤号</c>;同一工艺中参数相同的步骤共用一个膜层。
    /// 新建的对象取设备上空闲的最小编号,不修改设备上已有的对象,不设活动工艺。纯函数,不访问设备。
    /// </summary>
    public static class DesignBuilder
    {
        // 设置项在对象中的编号 (见指令集 M**、F**、L**、P02**)。
        private const int MaterialDensity = 0x03;
        private const int MaterialZRatio = 0x04;
        private const int MaterialTooling = 0x07;
        private const int FilmMaterial = 0x02;
        private const int FilmPreConditions = 0x03;
        private const int FilmDelay = 0x04;
        private const int FilmPostConditions = 0x05;
        private const int FilmSensors = 0x07;
        private const int FilmSource = 0x09;
        private const int LayerFilms = 0x02;
        private const int LayerRateRamps = 0x06;
        private const int ProcessActiveLayer = 0x02;
        private const int ProcessAutoStartNext = 0x04;
        private const int ProcessLayers = 0x07;

        /// <summary>新建的膜层名称中材料名称、工艺名称和步骤号之间的分隔符:<c>Al-AR-1</c>。</summary>
        public const string LayerNameSeparator = "-";

        /// <summary>
        /// 生成上传内容:先按 <see cref="DesignRules.ValidateUpload"/> 检查工艺、用到的材料和设备 (传感器、输出源、坩埚数量),
        /// 再找可复用的对象、为其余对象分配编号、生成设置项。
        /// </summary>
        /// <param name="document">材料和工艺。</param>
        /// <param name="process">要上传的工艺 (属于 <paramref name="document"/>)。</param>
        /// <param name="snapshot">设备的当前配置 (<c>?</c>)。</param>
        /// <returns>上传内容;检查不通过或设备上没有空闲编号时失败,原因每行一条。</returns>
        public static Result<DesignBuild> Build(DesignDocument document, ProcessDesign process, InstrumentSnapshot snapshot)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (process is null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var errors = DesignRules.ValidateUpload(document, process, snapshot);
            if (errors.Count > 0)
            {
                return Result<DesignBuild>.Fail(string.Join(Environment.NewLine, errors));
            }

            var existing = ExistingObjectIds(snapshot);
            var used = existing.ToDictionary(e => e.Key, e => new HashSet<string>(e.Value, StringComparer.Ordinal));
            var entries = new List<RecipeEntry>();
            var objects = new List<DesignObject>();
            var films = new Dictionary<string, (string Id, bool IsNew)>(StringComparer.Ordinal);
            string? noFree = null;

            string? Allocate(ObjectType type)
            {
                var id = type.NextFreeId(used[type.Group]);
                if (id == null)
                {
                    noFree ??= Texts.DesignNoFreeNumber(type.Kind.ToLowerInvariant());
                    return null;
                }

                used[type.Group].Add(id);
                return id;
            }

            // 设备上第一个所有设置项都与要写入的值相同、子列表条数也相同的对象。
            string? Reusable(ObjectType type, Func<string, IEnumerable<DesignField>> fields, Func<string, IEnumerable<(string ListId, int Count)>> lists) =>
                existing[type.Group]
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .FirstOrDefault(id => fields(id).All(f => Matches(snapshot, f)) && lists(id).All(l => CountOf(snapshot, l.ListId) == l.Count));

            foreach (var material in MaterialsOf(document, process))
            {
                var name = material.Name.Trim();
                var materialId = Reusable(ObjectTypes.Material, id => MaterialObjectFields(material, id), _ => Array.Empty<(string, int)>());
                var materialIsNew = materialId == null;
                materialId ??= Allocate(ObjectTypes.Material);
                if (materialId == null)
                {
                    break;
                }

                // 新材料没有引用它的膜系:膜系也要新建。
                var filmId = materialIsNew ? null : Reusable(ObjectTypes.Film, id => FilmFields(material, id, materialId), id => FilmLists(material, id));
                var filmIsNew = filmId == null;
                filmId ??= Allocate(ObjectTypes.Film);
                if (filmId == null)
                {
                    break;
                }

                films[material.Id] = (filmId, filmIsNew);
                if (materialIsNew)
                {
                    entries.AddRange(MaterialObjectFields(material, materialId).Select(ToEntry));
                }

                if (filmIsNew)
                {
                    entries.AddRange(FilmFields(material, filmId, materialId).Select(ToEntry));
                }

                objects.Add(new DesignObject(materialId, ObjectTypes.Material.Group, name, materialIsNew));
                objects.Add(new DesignObject(filmId, ObjectTypes.Film.Group, name, filmIsNew));
            }

            var layerIds = new List<string>();
            var stepLayers = new List<(ProcessStepDesign Step, string LayerId)>();
            for (var i = 0; noFree == null && i < process.Steps.Count; i++)
            {
                var step = process.Steps[i];
                var material = document.FindMaterial(step.MaterialId)!;
                var film = films[step.MaterialId];
                var name = LayerName(material, process, i + 1);

                // 本工艺中参数相同的前一步:共用它的膜层 (名称是那一步的)。
                var same = stepLayers.FirstOrDefault(s => IsSameLayer(s.Step, step));
                if (same.LayerId != null)
                {
                    layerIds.Add(same.LayerId);
                    continue;
                }

                // 复用不比较名称:设备上参数相同的膜层可能是别的工艺或别的步骤新建的,名称不同。
                var layerId = film.IsNew ? null : Reusable(ObjectTypes.Layer, id => StepFields(step, id, null, film.Id), id => StepLists(step, id));
                var layerIsNew = layerId == null;
                layerId ??= Allocate(ObjectTypes.Layer);
                if (layerId == null)
                {
                    break;
                }

                if (layerIsNew)
                {
                    entries.AddRange(StepFields(step, layerId, name, film.Id).Select(ToEntry));
                }

                layerIds.Add(layerId);
                stepLayers.Add((step, layerId));
                var objectName = layerIsNew ? name : snapshot.GetValue(DeviceIds.NameOf(layerId))?.Trim() ?? string.Empty;
                objects.Add(new DesignObject(layerId, ObjectTypes.Layer.Group, objectName, layerIsNew));
            }

            var processId = noFree == null ? Allocate(ObjectTypes.Process) : null;
            if (noFree != null || processId == null)
            {
                return Result<DesignBuild>.Fail(noFree ?? Texts.DesignNoFreeNumber(ObjectTypes.Process.Kind.ToLowerInvariant()));
            }

            entries.AddRange(ProcessFields(process, processId, layerIds).Select(ToEntry));
            objects.Add(new DesignObject(processId, ObjectTypes.Process.Group, process.Name.Trim(), true));

            var recipe = new InstrumentRecipe
            {
                DeviceName = snapshot.DeviceName,
                ModelNumber = snapshot.ModelNumber,
                FirmwareVersion = snapshot.FirmwareVersion,
                SerialNumber = snapshot.SerialNumber,
                SavedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                Entries = entries,
            };

            return Result<DesignBuild>.Ok(new DesignBuild(recipe, objects.OrderBy(o => RecipeRules.GroupRank(o.Id)).ToList()));
        }

        /// <summary>工艺用到的材料,按第一次出现的顺序,每种一次;找不到的材料不在其中。</summary>
        public static IReadOnlyList<MaterialDesign> MaterialsOf(DesignDocument document, ProcessDesign process)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (process is null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            return process.Steps
                .Select(s => document.FindMaterial(s.MaterialId))
                .OfType<MaterialDesign>()
                .Distinct()
                .ToList();
        }

        /// <summary>新建的膜层名称:<c>材料名称-工艺名称-步骤号</c>,如工艺 AR 第 1 步的 Al 为 <c>Al-AR-1</c>。</summary>
        /// <param name="material">这一步的材料。</param>
        /// <param name="process">工艺。</param>
        /// <param name="stepNumber">步骤号,从 1 起。</param>
        public static string LayerName(MaterialDesign material, ProcessDesign process, int stepNumber) =>
            (material ?? throw new ArgumentNullException(nameof(material))).Name.Trim()
            + LayerNameSeparator + (process ?? throw new ArgumentNullException(nameof(process))).Name.Trim()
            + LayerNameSeparator + stepNumber.ToString(CultureInfo.InvariantCulture);

        /// <summary>材料的设置项。</summary>
        internal static IEnumerable<DesignField> MaterialObjectFields(MaterialDesign material, string materialId)
        {
            yield return new DesignField(DeviceIds.NameOf(materialId), material.Name);
            yield return new DesignField(DeviceIds.Item(materialId, MaterialDensity), material.Density);
            yield return new DesignField(DeviceIds.Item(materialId, MaterialZRatio), material.ZRatio);
            yield return new DesignField(DeviceIds.Item(materialId, MaterialTooling), material.Tooling);
        }

        /// <summary>膜系的设置项 (名称与材料相同,材料为 <paramref name="materialId"/>)。</summary>
        internal static IEnumerable<DesignField> FilmFields(MaterialDesign material, string filmId, string materialId)
        {
            yield return new DesignField(DeviceIds.NameOf(filmId), material.Name);
            yield return DesignField.Generated(DeviceIds.Item(filmId, FilmMaterial), Number(DeviceIds.LastNumber(materialId)));
            foreach (var field in ConditionFields(DeviceIds.Item(filmId, FilmPreConditions), material.PreConditions))
            {
                yield return field;
            }

            var delay = DeviceIds.Item(filmId, FilmDelay);
            yield return new DesignField(DeviceIds.Item(delay, 1), material.DelayOption);
            if (material.HasDelaySettings)
            {
                // 延迟选项为 0 (无) 时这些设置不起作用,不写入,复用时也不比较。
                yield return new DesignField(DeviceIds.Item(delay, 2), material.ControlDelayTime);
                yield return new DesignField(DeviceIds.Item(delay, 3), material.ShutterDelayAccuracy);
                yield return new DesignField(DeviceIds.Item(delay, 4), material.ShutterDelayTimeout);
                yield return new DesignField(DeviceIds.Item(delay, 5), Number(material.TransferSensor));
                yield return new DesignField(DeviceIds.Item(delay, 6), material.TransferTooling);
            }

            foreach (var field in ConditionFields(DeviceIds.Item(filmId, FilmPostConditions), material.PostConditions))
            {
                yield return field;
            }

            var sensor = DeviceIds.Item(DeviceIds.Item(filmId, FilmSensors), 1);
            yield return new DesignField(DeviceIds.Item(sensor, 1), Number(material.Sensor), isListItem: true);
            yield return new DesignField(DeviceIds.Item(sensor, 2), material.CrystalPosition, isListItem: true);

            var source = DeviceIds.Item(filmId, FilmSource);
            yield return new DesignField(DeviceIds.Item(source, 1), Number(material.Source));
            yield return new DesignField(DeviceIds.Item(source, 2), Number(material.Crucible));
            yield return new DesignField(DeviceIds.Item(source, 3), material.PTerm);
            yield return new DesignField(DeviceIds.Item(source, 4), material.ITerm);
            yield return new DesignField(DeviceIds.Item(source, 5), material.DTerm);
            yield return new DesignField(DeviceIds.Item(source, 6), material.MaxPower);
            yield return new DesignField(DeviceIds.Item(source, 7), material.MaxPowerOption);
        }

        /// <summary>
        /// 一步 (膜层) 的设置项:名称、膜系列表第 1 项的膜系、速率、最终厚度和速率斜坡。
        /// <paramref name="layerName"/> 为 null 时没有名称 (复用时的比较、检查)。
        /// </summary>
        internal static IEnumerable<DesignField> StepFields(ProcessStepDesign step, string layerId, string? layerName, string filmId)
        {
            if (layerName != null)
            {
                yield return DesignField.Generated(DeviceIds.NameOf(layerId), layerName);
            }

            var film = DeviceIds.Item(DeviceIds.Item(layerId, LayerFilms), 1);
            yield return DesignField.Generated(DeviceIds.Item(film, 1), Number(DeviceIds.LastNumber(filmId)));
            yield return StepValue(layerId, StepField.Rate, 0, step.Rate);
            yield return StepValue(layerId, StepField.Thickness, 0, step.Thickness);
            for (var k = 0; k < step.RateRamps.Count; k++)
            {
                var design = step.RateRamps[k];
                yield return StepValue(layerId, StepField.RampStartThickness, k + 1, design.StartThickness);
                yield return StepValue(layerId, StepField.RampTime, k + 1, design.RampTime);
                yield return StepValue(layerId, StepField.RampNewRate, k + 1, design.NewRate);
            }
        }

        /// <summary>
        /// 一步中可输入的设置项 (膜系列表第 1 项):<paramref name="ramp"/> 为速率斜坡的序号 (从 1 起),速率和最终厚度时为 0。
        /// 速率和厚度最多 <see cref="DesignRules.StepDecimals"/> 位小数。
        /// </summary>
        internal static DesignField StepValue(string layerId, StepField field, int ramp, string value)
        {
            var film = DeviceIds.Item(DeviceIds.Item(layerId, LayerFilms), 1);
            var rampItem = DeviceIds.Item(DeviceIds.Item(film, LayerRateRamps), Math.Max(ramp, 1));
            var id = field switch
            {
                StepField.Rate => DeviceIds.Item(film, 2),
                StepField.Thickness => DeviceIds.Item(film, 3),
                StepField.RampStartThickness => DeviceIds.Item(rampItem, 1),
                StepField.RampTime => DeviceIds.Item(rampItem, 2),
                StepField.RampNewRate => DeviceIds.Item(rampItem, 3),
                _ => throw new ArgumentOutOfRangeException(nameof(field)),
            };
            return new DesignField(id, value, ramp: ramp, maxDecimals: field == StepField.RampTime ? (int?)null : DesignRules.StepDecimals);
        }

        /// <summary>工艺的设置项:名称、活动膜层 (1)、自动启动下一膜层、膜层列表。</summary>
        internal static IEnumerable<DesignField> ProcessFields(ProcessDesign process, string processId, IReadOnlyList<string> layerIds)
        {
            yield return new DesignField(DeviceIds.NameOf(processId), process.Name);
            yield return DesignField.Generated(DeviceIds.Item(processId, ProcessActiveLayer), "1");
            yield return DesignField.Generated(DeviceIds.Item(processId, ProcessAutoStartNext), process.AutoStartNext ? "1" : "0");
            var layers = DeviceIds.Item(processId, ProcessLayers);
            for (var n = 0; n < layerIds.Count; n++)
            {
                yield return DesignField.Generated(DeviceIds.Item(layers, n + 1), Number(DeviceIds.LastNumber(layerIds[n])));
            }
        }

        /// <summary>膜系子列表应有的条数:预熔、后处理与材料相同,传感器 1 个。</summary>
        private static IEnumerable<(string ListId, int Count)> FilmLists(MaterialDesign material, string filmId)
        {
            yield return (DeviceIds.Item(filmId, FilmPreConditions), material.PreConditions.Count);
            yield return (DeviceIds.Item(filmId, FilmPostConditions), material.PostConditions.Count);
            yield return (DeviceIds.Item(filmId, FilmSensors), 1);
        }

        /// <summary>膜层子列表应有的条数:膜系 1 个,速率斜坡与步骤相同。</summary>
        private static IEnumerable<(string ListId, int Count)> StepLists(ProcessStepDesign step, string layerId)
        {
            var films = DeviceIds.Item(layerId, LayerFilms);
            yield return (films, 1);
            yield return (DeviceIds.Item(DeviceIds.Item(films, 1), LayerRateRamps), step.RateRamps.Count);
        }

        private static IEnumerable<DesignField> ConditionFields(string listId, IReadOnlyList<ConditionStep> steps)
        {
            for (var i = 0; i < steps.Count; i++)
            {
                var item = DeviceIds.Item(listId, i + 1);
                yield return new DesignField(DeviceIds.Item(item, 1), steps[i].RampTime, isListItem: true);
                yield return new DesignField(DeviceIds.Item(item, 2), steps[i].RampPower, isListItem: true);
                yield return new DesignField(DeviceIds.Item(item, 3), steps[i].HoldTime, isListItem: true);
            }
        }

        /// <summary>
        /// 两步是否是同一个膜层:材料、速率、最终厚度和速率斜坡都相同 (数值按 <see cref="ParameterValue.AreEquivalent"/> 比较)。
        /// 同一工艺中这样的步骤上传时共用一个膜层,保存时共用一个膜层文件。
        /// </summary>
        public static bool IsSameLayer(ProcessStepDesign a, ProcessStepDesign b) =>
            a is null || b is null ? throw new ArgumentNullException(a is null ? nameof(a) : nameof(b)) :
            a.MaterialId == b.MaterialId
            && ParameterValue.AreEquivalent(a.Rate.Trim(), b.Rate.Trim())
            && ParameterValue.AreEquivalent(a.Thickness.Trim(), b.Thickness.Trim())
            && a.RateRamps.Count == b.RateRamps.Count
            && a.RateRamps.Zip(b.RateRamps, (x, y) =>
                ParameterValue.AreEquivalent(x.StartThickness.Trim(), y.StartThickness.Trim())
                && ParameterValue.AreEquivalent(x.RampTime.Trim(), y.RampTime.Trim())
                && ParameterValue.AreEquivalent(x.NewRate.Trim(), y.NewRate.Trim())).All(same => same);

        /// <summary>设备上的值与要写入的值相同 (名称按字符串,数值、时间按值)。</summary>
        private static bool Matches(InstrumentSnapshot snapshot, DesignField field) =>
            snapshot.GetValue(field.Id) is string value && ParameterValue.AreEquivalent(field.Value.Trim(), value.Trim());

        /// <summary>设备上子列表的条数 (<c>…FE</c>);没有时为 0。</summary>
        private static int CountOf(InstrumentSnapshot snapshot, string listId) =>
            DeviceIds.TryParseCount(snapshot.GetValue(DeviceIds.CountOf(listId)), out var count) ? count : 0;

        /// <summary>设备上 M、F、L、P 各组已有的对象。</summary>
        internal static Dictionary<char, HashSet<string>> ExistingObjectIds(InstrumentSnapshot snapshot)
        {
            var existing = ObjectTypes.All.ToDictionary(t => t.Group, _ => new HashSet<string>(StringComparer.Ordinal));
            foreach (var entry in snapshot.Entries)
            {
                if (ObjectTypes.TryGetObject(entry.Id, out var type, out var objectId))
                {
                    existing[type.Group].Add(objectId);
                }
            }

            return existing;
        }

        private static RecipeEntry ToEntry(DesignField field) => new RecipeEntry
        {
            Id = field.Id,
            Value = field.Value.Trim(),
            Description = ZevisionCommandSet.Find(field.Id)?.EnglishName ?? string.Empty,
        };

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>上传时写入的一个设置项。</summary>
    internal readonly struct DesignField
    {
        public DesignField(string id, string value, bool isListItem = false, int ramp = 0, bool isGenerated = false, int? maxDecimals = null)
        {
            Id = id;
            MaxDecimals = maxDecimals;
            Value = value ?? string.Empty;
            IsListItem = isListItem;
            Ramp = ramp;
            IsGenerated = isGenerated;
        }

        /// <summary>设置项 ID。</summary>
        public string Id { get; }

        /// <summary>值 (用户输入的,未去空格)。</summary>
        public string Value { get; }

        /// <summary>属于子列表 (预熔、后处理、传感器):标签带列表名称和序号。</summary>
        public bool IsListItem { get; }

        /// <summary>属于第几个速率斜坡 (从 1 起);不是时为 0。</summary>
        public int Ramp { get; }

        /// <summary>由程序生成 (引用编号、膜层名称等),不检查。</summary>
        public bool IsGenerated { get; }

        /// <summary>最多的小数位数 (速率、厚度);没有限制时为 null。</summary>
        public int? MaxDecimals { get; }

        public static DesignField Generated(string id, string value) => new DesignField(id, value, isGenerated: true);
    }
}
