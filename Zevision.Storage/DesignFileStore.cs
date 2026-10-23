using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Zevision.Core;
using Zevision.Core.Design;

namespace Zevision.Storage
{
    /// <summary>
    /// 工艺编辑的文件,按设备对象分目录保存在 <c>recipes</c> 下,每个对象一个缩进的 UTF-8 JSON 文件 (先写临时文件再替换):
    /// <list type="bullet">
    /// <item><c>material\名称.json</c>:材料 (名称、中文名、密度、Z 比、工具因子)。</item>
    /// <item><c>film\名称.json</c>:同名膜系 (传感器、输出源、坩埚、预熔、后处理、PID、最大功率、延迟设置)。</item>
    /// <item><c>layer\材料名称-工艺名称-步骤号.json</c>:工艺中每个不同的步骤一个膜层 (材料、速率、最终厚度、速率斜坡;同一工艺中参数相同的步骤共用第一步的文件)。</item>
    /// <item><c>process\名称.json</c>:工艺 (自动启动下一膜层、按顺序引用的膜层、上次上传的记录)。</item>
    /// </list>
    /// 保存时写入全部对象 (内容没有变化的文件不重写),并删除这四个目录中读取或写入过、这次不再对应任何对象的 <c>.json</c> 文件
    /// (如被删除的材料);窗口打开期间别处放进来的文件不删除。
    /// 材料库是程序旁边的 <c>materials.json</c> (只读,随程序发布;源文件 <c>docs\materials.json</c>,IMC300 操作手册附录 A 的材料表)。
    /// </summary>
    public sealed class DesignFileStore : IDesignStore
    {
        /// <summary>材料的目录名。</summary>
        public const string MaterialFolder = "material";

        /// <summary>膜系的目录名。</summary>
        public const string FilmFolder = "film";

        /// <summary>膜层的目录名。</summary>
        public const string LayerFolder = "layer";

        /// <summary>工艺的目录名。</summary>
        public const string ProcessFolder = "process";

        /// <summary>材料库的文件名。</summary>
        public const string MaterialLibraryFileName = "materials.json";

        /// <summary>当前的文件格式版本。</summary>
        public const int CurrentFormatVersion = 1;

        private const string Extension = ".json";

        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        private readonly string _folder;
        private readonly string _libraryPath;

        /// <summary>读取或写入过的对象文件 (完整路径);保存时只删除其中不再对应对象的文件。</summary>
        private readonly HashSet<string> _known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 创建存储。
        /// </summary>
        /// <param name="folder">工艺编辑的目录 (其下为 material、film、layer、process 四个目录)。</param>
        /// <param name="materialLibraryPath">材料库文件。</param>
        public DesignFileStore(string folder, string materialLibraryPath)
        {
            if (string.IsNullOrEmpty(folder))
            {
                throw new ArgumentException(Texts.PathEmpty, nameof(folder));
            }

            if (string.IsNullOrEmpty(materialLibraryPath))
            {
                throw new ArgumentException(Texts.PathEmpty, nameof(materialLibraryPath));
            }

            _folder = folder;
            _libraryPath = materialLibraryPath;
        }

        /// <summary>四个对象目录的名称。</summary>
        public static IReadOnlyList<string> ObjectFolders { get; } = new[] { MaterialFolder, FilmFolder, LayerFolder, ProcessFolder };

        /// <inheritdoc />
        /// <remarks><c>recipes</c> 目录。</remarks>
        public string Path => _folder;

        /// <summary>默认位置的存储:<c>recipes</c> (见 <see cref="AppFolders.GetDesignFolder"/>) 和程序旁边的材料库。</summary>
        public static DesignFileStore CreateDefault() => new DesignFileStore(
            AppFolders.GetDesignFolder(),
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MaterialLibraryFileName));

        /// <summary>目录中是否已有工艺编辑的文件 (任一对象目录存在)。</summary>
        internal static bool HasDesignFolders(string folder) =>
            ObjectFolders.Any(name => Directory.Exists(System.IO.Path.Combine(folder, name)));

        /// <inheritdoc />
        public Result<DesignDocument> Load()
        {
            string? current = null;
            try
            {
                var films = Read<FilmFile>(FilmFolder, ref current);
                var layers = Read<LayerFile>(LayerFolder, ref current);
                var document = new DesignDocument();
                foreach (var (key, file) in Read<MaterialFile>(MaterialFolder, ref current))
                {
                    var material = ToMaterial(file);
                    var film = films.FirstOrDefault(f => f.File.MaterialId == material.Id).File
                        ?? films.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)).File;
                    if (film != null)
                    {
                        ApplyFilm(material, film);
                    }

                    document.Materials.Add(material);
                }

                var layerByKey = layers.ToDictionary(l => l.Key, l => l.File, StringComparer.OrdinalIgnoreCase);
                foreach (var (_, file) in Read<ProcessFile>(ProcessFolder, ref current))
                {
                    document.Processes.Add(ToProcess(file, layerByKey));
                }

                return Result<DesignDocument>.Ok(document);
            }
            catch (Exception ex) when (JsonFile.IsExpectedFailure(ex))
            {
                return Result<DesignDocument>.Fail(Texts.DesignReadFailed(current ?? _folder, ex.Message));
            }
            catch (UnsupportedFormatException ex)
            {
                return Result<DesignDocument>.Fail(Texts.DesignFormatUnsupported(ex.Path, ex.Version, CurrentFormatVersion));
            }
        }

        /// <inheritdoc />
        public Result Save(DesignDocument document)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            try
            {
                var written = ObjectFolders.ToDictionary(f => f, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                foreach (var material in document.Materials)
                {
                    var key = UniqueKey(material.Name, written[MaterialFolder]);
                    Write(MaterialFolder, key, ToFile(material));
                    Write(FilmFolder, key, ToFilmFile(material));
                    written[FilmFolder].Add(key);
                }

                // 工艺中每个不同的步骤 (材料、速率、厚度、速率斜坡) 一个膜层文件,名称与上传时新建的膜层相同 (材料名称-工艺名称-步骤号);
                // 工艺按顺序引用,同一工艺中相同的步骤共用第一步的文件。
                foreach (var process in document.Processes)
                {
                    var layers = new List<(ProcessStepDesign Step, string Key)>();
                    var keys = new List<string>();
                    for (var i = 0; i < process.Steps.Count; i++)
                    {
                        var step = process.Steps[i];
                        var layer = layers.FirstOrDefault(l => DesignBuilder.IsSameLayer(l.Step, step));
                        if (layer.Key == null)
                        {
                            var material = document.FindMaterial(step.MaterialId);
                            var name = DesignBuilder.LayerName(material ?? new MaterialDesign(), process, i + 1);
                            layer = (step, UniqueKey(name, written[LayerFolder]));
                            layers.Add(layer);
                            Write(LayerFolder, layer.Key, ToLayerFile(step, material, name));
                        }

                        keys.Add(layer.Key);
                    }

                    Write(ProcessFolder, UniqueKey(process.Name, written[ProcessFolder]), ToProcessFile(process, keys));
                }

                RemoveStale(written);
                return Result.Ok();
            }
            catch (Exception ex) when (JsonFile.IsExpectedFailure(ex))
            {
                return Result.Fail(Texts.DesignSaveFailed(ex.Message));
            }
        }

        /// <inheritdoc />
        public Result<IReadOnlyList<MaterialLibraryEntry>> LoadMaterialLibrary()
        {
            if (!File.Exists(_libraryPath))
            {
                return Result<IReadOnlyList<MaterialLibraryEntry>>.Ok(Array.Empty<MaterialLibraryEntry>());
            }

            try
            {
                var table = JsonFile.Read<MaterialTable>(_libraryPath);
                var entries = new List<MaterialLibraryEntry>();
                foreach (var row in table?.Materials ?? new List<MaterialRow?>())
                {
                    // 没有化学式或数值的行不能用,跳过。
                    if (row?.Formula is string formula && formula.Trim().Length > 0 && row.Density is double density && row.ZRatio is double zRatio)
                    {
                        entries.Add(new MaterialLibraryEntry(
                            formula.Trim(), row.Name?.Trim() ?? string.Empty, row.ChineseName?.Trim() ?? string.Empty, DeviceNumber(density), DeviceNumber(zRatio), row.ZRatioEstablished ?? true));
                    }
                }

                return Result<IReadOnlyList<MaterialLibraryEntry>>.Ok(entries);
            }
            catch (Exception ex) when (JsonFile.IsExpectedFailure(ex))
            {
                return Result<IReadOnlyList<MaterialLibraryEntry>>.Fail(Texts.MaterialLibraryReadFailed(_libraryPath, ex.Message));
            }
        }

        /// <summary>
        /// 由名称得到文件名 (不含扩展名):文件名中不允许的字符换成 <c>_</c>,空名称为 <c>unnamed</c>,
        /// 系统保留名 (第一个点之前是 <c>CON</c>、<c>COM1</c> 等,如 <c>CON.1</c>) 前加 <c>_</c>;已用过时 (不区分大小写) 加 <c>_2</c>、<c>_3</c>…。
        /// </summary>
        internal static string UniqueKey(string? name, ISet<string> used)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var safe = new string((name ?? string.Empty).Trim().Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).TrimEnd('.', ' ');
            if (safe.Length == 0)
            {
                safe = "unnamed";
            }

            if (ReservedNames.Contains(safe.Split('.')[0].ToUpperInvariant()))
            {
                safe = "_" + safe;
            }

            var key = safe;
            for (var n = 2; !used.Add(key); n++)
            {
                key = safe + "_" + n.ToString(CultureInfo.InvariantCulture);
            }

            return key;
        }

        /// <summary>密度、Z 比按设备的格式写成三位小数,如 2.7 → <c>2.700</c>。</summary>
        private static string DeviceNumber(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);

        private static MaterialDesign ToMaterial(MaterialFile file) => new MaterialDesign
        {
            Id = string.IsNullOrEmpty(file.Id) ? MaterialDesign.NewId() : file.Id!,
            Name = file.Name ?? string.Empty,
            ChineseName = file.ChineseName ?? string.Empty,
            Density = file.Density ?? string.Empty,
            ZRatio = file.ZRatio ?? string.Empty,
            Tooling = file.Tooling ?? new MaterialDesign().Tooling,
        };

        private static void ApplyFilm(MaterialDesign material, FilmFile film)
        {
            var defaults = new MaterialDesign();
            material.Sensor = film.Sensor ?? defaults.Sensor;
            material.CrystalPosition = film.CrystalPosition ?? defaults.CrystalPosition;
            material.Source = film.Source ?? defaults.Source;
            material.Crucible = film.Crucible ?? defaults.Crucible;
            material.PreConditions = Steps(film.PreConditions);
            material.PostConditions = Steps(film.PostConditions);
            material.PTerm = film.PTerm ?? defaults.PTerm;
            material.ITerm = film.ITerm ?? defaults.ITerm;
            material.DTerm = film.DTerm ?? defaults.DTerm;
            material.MaxPower = film.MaxPower ?? defaults.MaxPower;
            material.MaxPowerOption = film.MaxPowerOption ?? defaults.MaxPowerOption;
            material.DelayOption = film.DelayOption ?? defaults.DelayOption;
            material.ControlDelayTime = film.ControlDelayTime ?? defaults.ControlDelayTime;
            material.ShutterDelayAccuracy = film.ShutterDelayAccuracy ?? defaults.ShutterDelayAccuracy;
            material.ShutterDelayTimeout = film.ShutterDelayTimeout ?? defaults.ShutterDelayTimeout;
            material.TransferSensor = film.TransferSensor ?? defaults.TransferSensor;
            material.TransferTooling = film.TransferTooling ?? defaults.TransferTooling;
        }

        private static List<ConditionStep> Steps(List<StepFile?>? steps) =>
            (steps ?? new List<StepFile?>()).OfType<StepFile>().Select(s => new ConditionStep
            {
                RampTime = s.RampTime ?? string.Empty,
                RampPower = s.RampPower ?? string.Empty,
                HoldTime = s.HoldTime ?? string.Empty,
            }).ToList();

        private static ProcessDesign ToProcess(ProcessFile file, IReadOnlyDictionary<string, LayerFile> layers) => new ProcessDesign
        {
            Id = string.IsNullOrEmpty(file.Id) ? MaterialDesign.NewId() : file.Id!,
            Name = file.Name ?? string.Empty,
            AutoStartNext = file.AutoStartNext ?? true,
            LastUpload = file.LastUpload ?? string.Empty,

            // 找不到的膜层文件:保留这一步 (没有材料),保存前的检查会指出。
            Steps = (file.Layers ?? new List<string?>()).Select(key => key != null && layers.TryGetValue(key, out var layer)
                ? new ProcessStepDesign
                {
                    MaterialId = layer.MaterialId ?? string.Empty,
                    Rate = layer.Rate ?? string.Empty,
                    Thickness = layer.Thickness ?? string.Empty,
                    RateRamps = (layer.RateRamps ?? new List<RampFile?>()).OfType<RampFile>().Select(r => new RateRampDesign
                    {
                        StartThickness = r.StartThickness ?? string.Empty,
                        RampTime = r.RampTime ?? string.Empty,
                        NewRate = r.NewRate ?? string.Empty,
                    }).ToList(),
                }
                : new ProcessStepDesign()).ToList(),
        };

        private static MaterialFile ToFile(MaterialDesign material) => new MaterialFile
        {
            FormatVersion = CurrentFormatVersion,
            Id = material.Id,
            Name = material.Name,
            ChineseName = material.ChineseName,
            Density = material.Density,
            ZRatio = material.ZRatio,
            Tooling = material.Tooling,
        };

        private static FilmFile ToFilmFile(MaterialDesign material) => new FilmFile
        {
            FormatVersion = CurrentFormatVersion,
            MaterialId = material.Id,
            Name = material.Name,
            Sensor = material.Sensor,
            CrystalPosition = material.CrystalPosition,
            Source = material.Source,
            Crucible = material.Crucible,
            PreConditions = material.PreConditions.Select(ToStepFile).ToList(),
            PostConditions = material.PostConditions.Select(ToStepFile).ToList(),
            PTerm = material.PTerm,
            ITerm = material.ITerm,
            DTerm = material.DTerm,
            MaxPower = material.MaxPower,
            MaxPowerOption = material.MaxPowerOption,
            DelayOption = material.DelayOption,
            ControlDelayTime = material.ControlDelayTime,
            ShutterDelayAccuracy = material.ShutterDelayAccuracy,
            ShutterDelayTimeout = material.ShutterDelayTimeout,
            TransferSensor = material.TransferSensor,
            TransferTooling = material.TransferTooling,
        };

        private static StepFile? ToStepFile(ConditionStep step) => new StepFile { RampTime = step.RampTime, RampPower = step.RampPower, HoldTime = step.HoldTime };

        private static LayerFile ToLayerFile(ProcessStepDesign step, MaterialDesign? material, string name) => new LayerFile
        {
            FormatVersion = CurrentFormatVersion,
            Name = material == null ? null : name,
            MaterialId = step.MaterialId,
            Material = material?.Name,
            Rate = step.Rate,
            Thickness = step.Thickness,
            RateRamps = step.RateRamps.Select(r => (RampFile?)new RampFile { StartThickness = r.StartThickness, RampTime = r.RampTime, NewRate = r.NewRate }).ToList(),
        };

        private static ProcessFile ToProcessFile(ProcessDesign process, IReadOnlyList<string> layers) => new ProcessFile
        {
            FormatVersion = CurrentFormatVersion,
            Id = process.Id,
            Name = process.Name,
            AutoStartNext = process.AutoStartNext,
            Layers = layers.Select(k => (string?)k).ToList(),
            LastUpload = process.LastUpload,
        };

        /// <summary>读取一个目录中的全部文件 (按文件名排序);目录不存在时为空。格式版本高于当前版本时抛出 <see cref="UnsupportedFormatException"/>。</summary>
        private List<(string Key, T File)> Read<T>(string folder, ref string? current)
            where T : class, IVersioned
        {
            var directory = System.IO.Path.Combine(_folder, folder);
            var files = new List<(string, T)>();
            if (!Directory.Exists(directory))
            {
                return files;
            }

            foreach (var path in Directory.GetFiles(directory, "*" + Extension).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                current = path;
                var file = JsonFile.Read<T>(path) ?? throw new SerializationException(Texts.RecipeEmpty);
                if (file.FormatVersion > CurrentFormatVersion)
                {
                    throw new UnsupportedFormatException(path, file.FormatVersion ?? 0);
                }

                files.Add((System.IO.Path.GetFileNameWithoutExtension(path), file));
                _known.Add(path);
            }

            current = null;
            return files;
        }

        private void Write<T>(string folder, string key, T file)
        {
            var path = System.IO.Path.Combine(_folder, folder, key + Extension);
            JsonFile.WriteIfChanged(path, file);
            _known.Add(path);
        }

        /// <summary>
        /// 删除对象目录中这次没有写、以前读取或写入过的 <c>.json</c> 文件 (已删除或改名的对象)。
        /// 没有读取过的文件 (窗口打开期间别处放进来的) 保留。
        /// </summary>
        private void RemoveStale(IReadOnlyDictionary<string, HashSet<string>> written)
        {
            foreach (var folder in ObjectFolders)
            {
                var directory = System.IO.Path.Combine(_folder, folder);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var path in Directory.GetFiles(directory, "*" + Extension))
                {
                    if (!written[folder].Contains(System.IO.Path.GetFileNameWithoutExtension(path)) && _known.Remove(path))
                    {
                        File.Delete(path);
                    }
                }
            }
        }

        /// <summary>带格式版本的文件。</summary>
        private interface IVersioned
        {
            int? FormatVersion { get; }
        }

        /// <summary>文件的格式版本高于本程序支持的版本 (更新版本的程序保存的)。</summary>
        private sealed class UnsupportedFormatException : Exception
        {
            public UnsupportedFormatException(string path, int version)
                : base(path)
            {
                Path = path;
                Version = version;
            }

            public string Path { get; }

            public int Version { get; }
        }

        [DataContract]
        private sealed class MaterialFile : IVersioned
        {
            [DataMember(Name = "formatVersion", Order = 0)]
            public int? FormatVersion { get; set; }

            [DataMember(Name = "id", Order = 1)]
            public string? Id { get; set; }

            [DataMember(Name = "name", Order = 2)]
            public string? Name { get; set; }

            [DataMember(Name = "chineseName", Order = 3)]
            public string? ChineseName { get; set; }

            [DataMember(Name = "density", Order = 4)]
            public string? Density { get; set; }

            [DataMember(Name = "zRatio", Order = 5)]
            public string? ZRatio { get; set; }

            [DataMember(Name = "tooling", Order = 6)]
            public string? Tooling { get; set; }
        }

        [DataContract]
        private sealed class FilmFile : IVersioned
        {
            [DataMember(Name = "formatVersion", Order = 0)]
            public int? FormatVersion { get; set; }

            [DataMember(Name = "materialId", Order = 1)]
            public string? MaterialId { get; set; }

            [DataMember(Name = "name", Order = 2)]
            public string? Name { get; set; }

            [DataMember(Name = "sensor", Order = 3)]
            public int? Sensor { get; set; }

            [DataMember(Name = "crystalPosition", Order = 4)]
            public string? CrystalPosition { get; set; }

            [DataMember(Name = "source", Order = 5)]
            public int? Source { get; set; }

            [DataMember(Name = "crucible", Order = 6)]
            public int? Crucible { get; set; }

            [DataMember(Name = "preConditions", Order = 7)]
            public List<StepFile?>? PreConditions { get; set; }

            [DataMember(Name = "postConditions", Order = 8)]
            public List<StepFile?>? PostConditions { get; set; }

            [DataMember(Name = "pTerm", Order = 9)]
            public string? PTerm { get; set; }

            [DataMember(Name = "iTerm", Order = 10)]
            public string? ITerm { get; set; }

            [DataMember(Name = "dTerm", Order = 11)]
            public string? DTerm { get; set; }

            [DataMember(Name = "maxPower", Order = 12)]
            public string? MaxPower { get; set; }

            [DataMember(Name = "maxPowerOption", Order = 13)]
            public string? MaxPowerOption { get; set; }

            [DataMember(Name = "delayOption", Order = 14)]
            public string? DelayOption { get; set; }

            [DataMember(Name = "controlDelayTime", Order = 15)]
            public string? ControlDelayTime { get; set; }

            [DataMember(Name = "shutterDelayAccuracy", Order = 16)]
            public string? ShutterDelayAccuracy { get; set; }

            [DataMember(Name = "shutterDelayTimeout", Order = 17)]
            public string? ShutterDelayTimeout { get; set; }

            [DataMember(Name = "transferSensor", Order = 18)]
            public int? TransferSensor { get; set; }

            [DataMember(Name = "transferTooling", Order = 19)]
            public string? TransferTooling { get; set; }
        }

        [DataContract]
        private sealed class StepFile
        {
            [DataMember(Name = "rampTime", Order = 0)]
            public string? RampTime { get; set; }

            [DataMember(Name = "rampPower", Order = 1)]
            public string? RampPower { get; set; }

            [DataMember(Name = "holdTime", Order = 2)]
            public string? HoldTime { get; set; }
        }

        [DataContract]
        private sealed class LayerFile : IVersioned
        {
            [DataMember(Name = "formatVersion", Order = 0)]
            public int? FormatVersion { get; set; }

            [DataMember(Name = "name", Order = 1)]
            public string? Name { get; set; }

            [DataMember(Name = "materialId", Order = 2)]
            public string? MaterialId { get; set; }

            [DataMember(Name = "material", Order = 3)]
            public string? Material { get; set; }

            [DataMember(Name = "rate", Order = 4)]
            public string? Rate { get; set; }

            [DataMember(Name = "thickness", Order = 5)]
            public string? Thickness { get; set; }

            [DataMember(Name = "rateRamps", Order = 6)]
            public List<RampFile?>? RateRamps { get; set; }
        }

        [DataContract]
        private sealed class RampFile
        {
            [DataMember(Name = "startThickness", Order = 0)]
            public string? StartThickness { get; set; }

            [DataMember(Name = "rampTime", Order = 1)]
            public string? RampTime { get; set; }

            [DataMember(Name = "newRate", Order = 2)]
            public string? NewRate { get; set; }
        }

        [DataContract]
        private sealed class ProcessFile : IVersioned
        {
            [DataMember(Name = "formatVersion", Order = 0)]
            public int? FormatVersion { get; set; }

            [DataMember(Name = "id", Order = 1)]
            public string? Id { get; set; }

            [DataMember(Name = "name", Order = 2)]
            public string? Name { get; set; }

            [DataMember(Name = "autoStartNext", Order = 3)]
            public bool? AutoStartNext { get; set; }

            [DataMember(Name = "layers", Order = 4)]
            public List<string?>? Layers { get; set; }

            [DataMember(Name = "lastUpload", Order = 5)]
            public string? LastUpload { get; set; }
        }

        /// <summary>
        /// 材料库文件 (<c>docs\materials.json</c> 的格式):<c>{ "materials": [ { "formula": "Al", "material_name": "aluminum",
        /// "material_name_zh": "铝", "density": 2.7, "z_ratio": 1.08, "z_ratio_established": true } ] }</c>;其他字段 (说明、列定义、备注) 不读。
        /// </summary>
        [DataContract]
        private sealed class MaterialTable
        {
            [DataMember(Name = "materials")]
            public List<MaterialRow?>? Materials { get; set; }
        }

        /// <summary>材料表的一行。</summary>
        [DataContract]
        private sealed class MaterialRow
        {
            [DataMember(Name = "formula")]
            public string? Formula { get; set; }

            [DataMember(Name = "material_name")]
            public string? Name { get; set; }

            [DataMember(Name = "material_name_zh")]
            public string? ChineseName { get; set; }

            [DataMember(Name = "density")]
            public double? Density { get; set; }

            [DataMember(Name = "z_ratio")]
            public double? ZRatio { get; set; }

            [DataMember(Name = "z_ratio_established")]
            public bool? ZRatioEstablished { get; set; }
        }
    }
}
