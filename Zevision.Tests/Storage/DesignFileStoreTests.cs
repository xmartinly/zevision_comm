using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Zevision.Core.Design;
using Zevision.Storage;

namespace Zevision.Tests
{
    /// <summary>
    /// 工艺编辑的文件:<c>recipes</c> 下按对象分目录 (material、film、layer、process),每个对象一个文件;
    /// 材料库 <c>materials.json</c> (源文件 <c>docs\materials.json</c>)。
    /// </summary>
    public sealed class DesignFileStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "zevision_design_" + Guid.NewGuid().ToString("N"));

        private string Recipes => Path.Combine(_dir, "recipes");

        private string LibraryPath => Path.Combine(_dir, DesignFileStore.MaterialLibraryFileName);

        public void Dispose()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }

        private DesignFileStore NewStore() => new DesignFileStore(Recipes, LibraryPath);

        private string[] Files(string folder)
        {
            var directory = Path.Combine(Recipes, folder);
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
        }

        /// <summary>两种材料;工艺 AR:Al 两种参数,第三步与第一步相同 (共用 Al-AR-1);工艺 B 的步骤与 AR 第一步相同,有自己的膜层文件。</summary>
        private static DesignDocument Sample()
        {
            var al = new MaterialDesign { Name = "Al", ChineseName = "铝", Density = "2.700", ZRatio = "1.080", Sensor = 2, Crucible = 3, MaxPowerOption = "2", DelayOption = "1", ShutterDelayAccuracy = "8.00", TransferSensor = 1 };
            al.PreConditions[1].HoldTime = "0:02:00";
            var sio2 = new MaterialDesign { Name = "SiO2", Density = "2.648", ZRatio = "1.000" };
            var ar = new ProcessDesign
            {
                Name = "AR",
                AutoStartNext = false,
                LastUpload = "P0209",
                Steps =
                {
                    new ProcessStepDesign { MaterialId = al.Id, Rate = "5.0", Thickness = "1.000" },
                    new ProcessStepDesign { MaterialId = al.Id, Rate = "5.0", Thickness = "2.000", RateRamps = { new RateRampDesign { StartThickness = "1.5", NewRate = "2.0" } } },
                    new ProcessStepDesign { MaterialId = al.Id, Rate = "5.00", Thickness = "1" },
                    new ProcessStepDesign { MaterialId = sio2.Id, Rate = "3.0", Thickness = "0.800" },
                },
            };
            var b = new ProcessDesign { Name = "B", Steps = { new ProcessStepDesign { MaterialId = al.Id, Rate = "5", Thickness = "1.000" } } };
            return new DesignDocument { Materials = { al, sio2 }, Processes = { ar, b } };
        }

        [Fact]
        public void Load_NothingSaved_IsEmpty()
        {
            var store = NewStore();

            var document = store.Load();
            var library = store.LoadMaterialLibrary();

            Assert.True(document.IsSuccess);
            Assert.Empty(document.Value.Materials);
            Assert.Empty(document.Value.Processes);
            Assert.True(library.IsSuccess);
            Assert.Empty(library.Value);
            Assert.Equal(Recipes, store.Path);
        }

        [Fact]
        public void Save_WritesOneFilePerObjectInFourFolders()
        {
            var saved = NewStore().Save(Sample());

            Assert.True(saved.IsSuccess, saved.Error);
            Assert.Equal(new[] { "Al.json", "SiO2.json" }, Files(DesignFileStore.MaterialFolder));
            Assert.Equal(new[] { "Al.json", "SiO2.json" }, Files(DesignFileStore.FilmFolder));
            Assert.Equal(new[] { "Al-AR-1.json", "Al-AR-2.json", "Al-B-1.json", "SiO2-AR-4.json" }, Files(DesignFileStore.LayerFolder)); // 膜层名:材料-工艺-步骤号
            Assert.Equal(new[] { "AR.json", "B.json" }, Files(DesignFileStore.ProcessFolder));
            var process = File.ReadAllText(Path.Combine(Recipes, "process", "AR.json")).Replace(" ", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
            Assert.Contains("\"layers\":[\"Al-AR-1\",\"Al-AR-2\",\"Al-AR-1\",\"SiO2-AR-4\"]", process); // 第三步与第一步相同,共用它的文件
            Assert.True(DesignFileStore.HasDesignFolders(Recipes));
            Assert.False(DesignFileStore.HasDesignFolders(_dir));
            Assert.Contains("\"name\":\"Al-AR-2\"", File.ReadAllText(Path.Combine(Recipes, "layer", "Al-AR-2.json")).Replace(" ", string.Empty));
        }

        [Fact]
        public void Save_ThenLoad_RoundTrips()
        {
            var original = Sample();
            var store = NewStore();
            store.Save(original);

            var loaded = store.Load();

            Assert.True(loaded.IsSuccess, loaded.Error);
            var al = loaded.Value.Materials.Single(m => m.Name == "Al");
            Assert.Equal((original.Materials[0].Id, "铝", "2.700", "1.080", "100.00", 2, 3), (al.Id, al.ChineseName, al.Density, al.ZRatio, al.Tooling, al.Sensor, al.Crucible));
            Assert.Equal(("2", "1", "8.00", 1), (al.MaxPowerOption, al.DelayOption, al.ShutterDelayAccuracy, al.TransferSensor));
            Assert.Equal(2, al.PreConditions.Count);
            Assert.Equal("0:02:00", al.PreConditions[1].HoldTime);
            Assert.Single(al.PostConditions);
            var ar = loaded.Value.Processes.Single(p => p.Name == "AR");
            Assert.False(ar.AutoStartNext);
            Assert.Equal("P0209", ar.LastUpload);
            Assert.Equal(new[] { "1.000", "2.000", "1.000", "0.800" }, ar.Steps.Select(s => s.Thickness)); // 第三步读回第一步的膜层
            Assert.Equal(new[] { al.Id, al.Id, al.Id }, ar.Steps.Take(3).Select(s => s.MaterialId));
            Assert.Equal(original.Materials[1].Id, ar.Steps[3].MaterialId);
            var ramp = Assert.Single(ar.Steps[1].RateRamps);
            Assert.Equal(("1.5", "0:00:00", "2.0"), (ramp.StartThickness, ramp.RampTime, ramp.NewRate));
            Assert.Equal("1.000", loaded.Value.Processes.Single(p => p.Name == "B").Steps.Single().Thickness);
        }

        [Fact]
        public void Save_DeletedAndRenamedObjects_RemoveTheirFiles()
        {
            var document = Sample();
            var store = NewStore();
            store.Save(document);
            File.WriteAllText(Path.Combine(Recipes, "material", "notes.txt"), "kept");
            document.Processes.RemoveAt(1);
            document.Processes[0].Steps.RemoveAt(1);
            document.Materials[1].Name = "SiO";

            var saved = store.Save(document);

            Assert.True(saved.IsSuccess, saved.Error);
            Assert.Equal(new[] { "Al.json", "SiO.json", "notes.txt" }, Files(DesignFileStore.MaterialFolder));
            Assert.Equal(new[] { "Al.json", "SiO.json" }, Files(DesignFileStore.FilmFolder));
            Assert.Equal(new[] { "Al-AR-1.json", "SiO-AR-3.json" }, Files(DesignFileStore.LayerFolder));
            Assert.Equal(new[] { "AR.json" }, Files(DesignFileStore.ProcessFolder));
        }

        [Fact]
        public void Save_SameNames_GetNumberedFiles()
        {
            var a = new MaterialDesign { Name = "Al" };
            var b = new MaterialDesign { Name = "al", Density = "1.000" };
            var store = NewStore();

            store.Save(new DesignDocument { Materials = { a, b } });
            var loaded = store.Load();

            Assert.Equal(new[] { "Al.json", "al_2.json" }, Files(DesignFileStore.MaterialFolder));
            Assert.Equal(new[] { "Al.json", "al_2.json" }, Files(DesignFileStore.FilmFolder));
            Assert.Equal(new[] { (a.Id, "Al", string.Empty), (b.Id, "al", "1.000") }, loaded.Value.Materials.Select(m => (m.Id, m.Name, m.Density)));
        }

        [Fact]
        public void Save_LayerNamesDifferingInCaseOnly_GetNumberedFiles_KeepTheirNames()
        {
            var al = new MaterialDesign { Name = "Al" };
            ProcessDesign Process(string name) => new ProcessDesign { Name = name, Steps = { new ProcessStepDesign { MaterialId = al.Id, Rate = "1", Thickness = "1" } } };
            var store = NewStore();

            store.Save(new DesignDocument { Materials = { al }, Processes = { Process("AR"), Process("ar") } });

            Assert.Equal(new[] { "Al-AR-1.json", "Al-ar-1_2.json" }, Files(DesignFileStore.LayerFolder));
            Assert.Contains("\"name\":\"Al-ar-1\"", File.ReadAllText(Path.Combine(Recipes, "layer", "Al-ar-1_2.json")).Replace(" ", string.Empty));
            Assert.Equal(2, store.Load().Value.Processes.Count(p => p.Steps.Single().MaterialId == al.Id));
        }

        [Fact]
        public void Load_MissingFieldsAndLayers_GetDefaults()
        {
            Directory.CreateDirectory(Path.Combine(Recipes, "material"));
            Directory.CreateDirectory(Path.Combine(Recipes, "process"));
            File.WriteAllText(Path.Combine(Recipes, "material", "Al.json"), "﻿{ \"name\": \"Al\", \"density\": null }", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Recipes, "process", "P.json"), "{ \"name\": \"P\", \"layers\": [ \"L_gone\", null ] }");

            var loaded = NewStore().Load();

            Assert.True(loaded.IsSuccess, loaded.Error);
            var material = loaded.Value.Materials.Single();
            Assert.Equal(string.Empty, material.Density);
            Assert.Equal("100.00", material.Tooling);
            Assert.Equal("10", material.PTerm); // 没有膜系文件:膜系设置取默认值
            Assert.Equal(2, material.PreConditions.Count);
            Assert.False(string.IsNullOrEmpty(material.Id));
            var process = loaded.Value.Processes.Single();
            Assert.True(process.AutoStartNext);
            Assert.Equal(new[] { string.Empty, string.Empty }, process.Steps.Select(s => s.MaterialId)); // 找不到的膜层:保存前的检查会指出
        }

        [Fact]
        public void Load_CorruptOrNewerFile_FailsNamingTheFile()
        {
            Directory.CreateDirectory(Path.Combine(Recipes, "film"));
            var path = Path.Combine(Recipes, "film", "Al.json");
            File.WriteAllText(path, "{ not json");
            var corrupt = NewStore().Load();
            File.WriteAllText(path, "{ \"formatVersion\": 99 }");
            var newer = NewStore().Load();

            Assert.False(corrupt.IsSuccess);
            Assert.StartsWith("读取工艺文件 " + path + " 失败: ", corrupt.Error, StringComparison.Ordinal);
            Assert.False(newer.IsSuccess);
            Assert.Equal("工艺文件 " + path + " 的格式版本 99 不受支持 (支持 1),可能是更新版本的程序保存的。", newer.Error);
        }

        [Theory]
        [InlineData("Al", "Al")]
        [InlineData(" a/b:c ", "a_b_c")]
        [InlineData("  ", "unnamed")]
        [InlineData(null, "unnamed")]
        [InlineData("con", "_con")]
        [InlineData("CON.1", "_CON.1")] // 带点的保留名 Windows 也不允许
        [InlineData("com1.x", "_com1.x")]
        [InlineData("Console", "Console")]
        [InlineData("x.", "x")]
        public void UniqueKey_MakesSafeFileNames(string? name, string expected)
        {
            Assert.Equal(expected, DesignFileStore.UniqueKey(name, new HashSet<string>()));
        }

        [Fact]
        public void Save_UnchangedFiles_AreNotRewritten()
        {
            var document = Sample();
            var store = NewStore();
            store.Save(document);
            var material = Path.Combine(Recipes, "material", "Al.json");
            var process = Path.Combine(Recipes, "process", "AR.json");
            var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(material, old);
            File.SetLastWriteTimeUtc(process, old);
            document.Processes[0].AutoStartNext = true;

            store.Save(document);

            Assert.Equal(old, File.GetLastWriteTimeUtc(material)); // 内容相同:不重写
            Assert.NotEqual(old, File.GetLastWriteTimeUtc(process));
            Assert.Empty(Directory.GetFiles(Recipes, "*.tmp", SearchOption.AllDirectories));
        }

        [Fact]
        public void Save_FilesThatAppearedWhileOpen_AreKept()
        {
            var store = NewStore();
            store.Save(Sample());
            Assert.True(store.Load().IsSuccess);
            var added = Path.Combine(Recipes, "material", "Copied.json"); // 窗口打开期间别处复制进来的
            File.WriteAllText(added, "{ \"name\": \"Copied\" }");

            store.Save(Sample());

            Assert.True(File.Exists(added));
        }

        [Fact]
        public void UniqueKey_UsedNames_GetANumber_IgnoringCase()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var keys = new[] { "L_Al", "L_AL", "L_Al" }.Select(n => DesignFileStore.UniqueKey(n, used)).ToList();

            Assert.Equal(new[] { "L_Al", "L_AL_2", "L_Al_3" }, keys);
        }

        [Fact]
        public void LoadMaterialLibrary_ReadsTheMaterialTable_SkipsIncompleteRows()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(LibraryPath, "{ \"document\": { \"title\": \"Appendix A\" }, \"record_count\": 4, \"materials\": ["
                + "{ \"formula\": \"Al\", \"material_name\": \"aluminum\", \"material_name_zh\": \"铝\", \"density\": 2.7, \"z_ratio\": 1.08, \"z_ratio_established\": true, \"z_ratio_note\": null },"
                + "{ \"formula\": \"AlN\", \"material_name\": \"aluminum nitride\", \"density\": 3.26, \"z_ratio\": 1.0, \"z_ratio_established\": false, \"z_ratio_note\": \"default\" },"
                + "{ \"formula\": \"\", \"density\": 1.0, \"z_ratio\": 1.0 }, { \"formula\": \"X\", \"density\": null, \"z_ratio\": 1.0 }, null ] }");
            var store = NewStore();

            var library = store.LoadMaterialLibrary();

            Assert.True(library.IsSuccess, library.Error);
            Assert.Equal(
                new[] { new MaterialLibraryEntry("Al", "aluminum", "铝", "2.700", "1.080", true), new MaterialLibraryEntry("AlN", "aluminum nitride", string.Empty, "3.260", "1.000", false) },
                library.Value);

            File.WriteAllText(LibraryPath, "[");
            Assert.StartsWith("读取材料库 ", store.LoadMaterialLibrary().Error, StringComparison.Ordinal);
        }

        [Fact]
        public void ShippedMaterialLibrary_IsTheManualTable_WithinDeviceRanges()
        {
            // docs\materials.json (IMC300 操作手册附录 A) 复制到程序旁边的 materials.json。
            var shipped = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DesignFileStore.MaterialLibraryFileName);

            var library = new DesignFileStore(Recipes, shipped).LoadMaterialLibrary();

            Assert.True(library.IsSuccess, library.Error);
            Assert.Equal(253, library.Value.Count);
            Assert.Contains(new MaterialLibraryEntry("Al", "aluminum", "铝", "2.700", "1.080", true), library.Value);
            Assert.Contains(new MaterialLibraryEntry("SiO2", "silicon dioxide", "二氧化硅", "2.648", "1.000", true), library.Value);
            Assert.All(library.Value, e => Assert.NotEqual(string.Empty, e.ChineseName));
            Assert.Equal(2, library.Value.Count(e => e.Formula == "C"));
            foreach (var entry in library.Value)
            {
                // 化学式用作材料名称,数值写入 M**03、M**04:都要符合设备的规则。
                Assert.Null(Zevision.Core.Configuration.ParameterValue.Validate(Zevision.Commands.ZevisionCommandSet.Find("M0101"), entry.Formula));
                Assert.Null(Zevision.Core.Configuration.ParameterValue.Validate(Zevision.Commands.ZevisionCommandSet.Find("M0103"), entry.Density));
                Assert.Null(Zevision.Core.Configuration.ParameterValue.Validate(Zevision.Commands.ZevisionCommandSet.Find("M0104"), entry.ZRatio));
            }
        }
    }
}
