using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Core.Configuration;
using Zevision.Core.Design;
using Zevision.Storage;
using Zevision.ViewModels.Design;
using Zevision.ViewModels.Display;
using Zevision.ViewModels.Operations;

namespace Zevision.Tests
{
    /// <summary>
    /// 工艺编辑窗口 (<see cref="ProcessDesignerViewModel"/>):新建材料的默认值、材料库、坩埚选项、删除材料和工艺 (同时删除设备上的对象);
    /// "保存"先检查、存到本地,再 (确认后) 把选中的工艺上传到设备;"设备"页列出设备上的工艺和材料。设备是 <see cref="DesignBuilderTests.NewDevice"/>
    /// (传感器 1、输出源 1 有 6 个坩埚、材料 M01、膜系 F01、膜层 L01、工艺 P0201 "Process 1")。
    /// </summary>
    public class ProcessDesignerTests
    {
        private static readonly IReadOnlyList<MaterialLibraryEntry> Library = new[]
        {
            new MaterialLibraryEntry("Al", "aluminum", "铝", "2.700", "1.080", true),
            new MaterialLibraryEntry("AlN", "aluminum nitride", "氮化铝", "3.260", "1.000", false),
            new MaterialLibraryEntry("C", "carbon (graphite)", "碳（石墨）", "2.250", "3.260", true),
            new MaterialLibraryEntry("C", "carbon (diamond)", "碳（金刚石）", "3.520", "0.220", true),
        };

        /// <summary>打开窗口时读取的设备配置 (<c>?</c>);读取的指令随后清除。</summary>
        private static InstrumentSnapshot Read(FakeDeviceService device)
        {
            var snapshot = new InstrumentConfigService(device).ReadSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult().Value;
            device.Commands.Clear();
            device.Frames.Clear();
            device.HiddenFrames.Clear();
            return snapshot;
        }

        private static ProcessDesignerViewModel NewDesigner(MemoryDesignStore store, FakeDeviceService device, FakeDialogService dialogs) =>
            new ProcessDesignerViewModel(store.Document, Library, Read(device), store, new InstrumentConfigService(device), dialogs, new FakeErrorLog());

        private static void ClearRecords(FakeDeviceService device, FakeDialogService dialogs)
        {
            device.Commands.Clear();
            device.HiddenFrames.Clear();
            dialogs.Confirms.Clear();
        }

        private static IEnumerable<string> Deletes(FakeDeviceService device) => device.Commands.Where(c => c.StartsWith("D", StringComparison.Ordinal));

        /// <summary>一种材料 (Al)、一个两步的工艺,都填好。</summary>
        private static ProcessDesignerViewModel Filled(MemoryDesignStore store, FakeDeviceService device, FakeDialogService dialogs)
        {
            var designer = NewDesigner(store, device, dialogs);
            designer.AddMaterialCommand.Execute(null);
            designer.SelectedMaterial!.LibraryPick = designer.Library[0];
            designer.AddProcessCommand.Execute(null);
            var process = designer.SelectedProcess!;
            process.Name = "P1";
            process.AddStepCommand.Execute(null);
            process.AddStepCommand.Execute(null);
            process.Steps[0].Rate = "4.0";
            process.Steps[0].Thickness = "0.300";
            process.Steps[1].Rate = "2.0";
            process.Steps[1].Thickness = "0.100";
            return designer;
        }

        [Fact]
        public void AddMaterial_HasDefaults_AndIsSelected()
        {
            var designer = NewDesigner(new MemoryDesignStore(), DesignBuilderTests.NewDevice(), new FakeDialogService());

            designer.AddMaterialCommand.Execute(null);

            var material = designer.SelectedMaterial!;
            Assert.Equal(new[] { "预熔 1", "预熔 2" }, material.PreConditions.Select(s => s.Title));
            Assert.Equal(new[] { "后处理 1" }, material.PostConditions.Select(s => s.Title));
            Assert.Equal(("10", "0", "0"), (material.PTerm, material.ITerm, material.DTerm));
            Assert.Equal("(未命名)", material.DisplayName);
            Assert.True(designer.IsDirty);
            Assert.Equal(0, designer.SelectedTabIndex);
        }

        [Fact]
        public void Library_PickFillsFormulaAndValues_TypingAUniqueFormulaFillsEmptyValues()
        {
            var designer = NewDesigner(new MemoryDesignStore(), DesignBuilderTests.NewDevice(), new FakeDialogService());
            designer.AddMaterialCommand.Execute(null);
            designer.AddMaterialCommand.Execute(null);
            designer.AddMaterialCommand.Execute(null);
            var picked = designer.Materials[0];
            var typed = designer.Materials[1];
            var ambiguous = designer.Materials[2];

            picked.LibraryPick = designer.Library[3];
            typed.Name = "al";  // 化学式区分大小写
            ambiguous.Name = "C"; // 石墨和金刚石:不自动填

            Assert.Equal(("C", "碳（金刚石）", "3.520", "0.220"), (picked.Name, picked.ChineseName, picked.Density, picked.ZRatio));
            Assert.Equal("C 碳（金刚石）", picked.DisplayName);
            Assert.Equal(("", ""), (typed.Density, typed.ZRatio));
            Assert.Equal(("", ""), (ambiguous.Density, ambiguous.ZRatio));

            typed.Name = "Al";
            Assert.Equal(("铝", "2.700", "1.080"), (typed.ChineseName, typed.Density, typed.ZRatio));

            // 已经填了值时,改名不覆盖数值;中文名跟着化学式。
            typed.Density = "3.000";
            typed.Name = "AlN";
            Assert.Equal(("3.000", "氮化铝"), (typed.Density, typed.ChineseName));

            Assert.Equal("Al - 铝", designer.Library[0].Text);
            Assert.Equal("氮化铝 / aluminum nitride,密度 3.260 g/cm³,Z 比 1.000 (Z 比未确定,按 1.000)", designer.Library[1].Description);
        }

        [Fact]
        public void Library_PickAgain_FollowsUntilTheUserTypesNames()
        {
            var designer = NewDesigner(new MemoryDesignStore(), DesignBuilderTests.NewDevice(), new FakeDialogService());
            designer.AddMaterialCommand.Execute(null);
            var material = designer.SelectedMaterial!;

            material.LibraryPick = designer.Library[0];
            material.LibraryPick = designer.Library[1];

            Assert.Equal(("AlN", "氮化铝", "3.260"), (material.Name, material.ChineseName, material.Density));

            // 用户改过名称后,再选择只填中文名和数值 (中文名没有输入框,总是材料库的)。
            material.Name = "AL1";
            material.LibraryPick = designer.Library[0];

            Assert.Equal(("AL1", "铝", "2.700"), (material.Name, material.ChineseName, material.Density));
            Assert.Equal("AL1 铝", material.DisplayName);
        }

        [Fact]
        public void LibrarySearch_FormulaOrNames_ExactAndPrefixFirst_OpensTheDropDown()
        {
            var library = new[]
            {
                new MaterialLibraryEntry("Cs", "cesium", "铯", "1.870", "1.000", false),
                new MaterialLibraryEntry("SiO", "silicon monoxide", "一氧化硅", "2.130", "0.870", true),
                new MaterialLibraryEntry("Si", "silicon", "硅", "2.320", "0.712", true),
                new MaterialLibraryEntry("NiSi", "nickel silicide", "硅化镍", "4.800", "1.000", false),
            };
            var store = new MemoryDesignStore();
            var device = DesignBuilderTests.NewDevice();
            var designer = new ProcessDesignerViewModel(store.Document, library, Read(device), store, new InstrumentConfigService(device), new FakeDialogService(), new FakeErrorLog());
            designer.AddMaterialCommand.Execute(null);
            var material = designer.SelectedMaterial!;

            Assert.Equal(4, material.LibraryMatches.Count);
            Assert.False(material.IsLibraryOpen);

            material.LibrarySearch = "si";
            Assert.True(material.IsLibraryOpen);
            Assert.Equal(new[] { "Si", "SiO", "Cs", "NiSi" }, material.LibraryMatches.Select(c => c.Value.Formula)); // 相同、开头,其余按材料表 (cesium 含 si)

            material.LibrarySearch = " 硅 ";
            Assert.Equal(new[] { "SiO", "Si", "NiSi" }, material.LibraryMatches.Select(c => c.Value.Formula));

            // 选择一项:下拉框显示它的文字,不再作为关键字 (列出全部)。
            material.LibraryPick = material.LibraryMatches[1];
            material.IsLibraryOpen = false;
            material.LibrarySearch = "Si - 硅";
            Assert.False(material.IsLibraryOpen);
            Assert.Equal(4, material.LibraryMatches.Count);
            Assert.Equal(("Si", "硅", "2.320"), (material.Name, material.ChineseName, material.Density));

            material.LibrarySearch = "xyz";
            Assert.True(material.IsLibraryOpen);
            Assert.Empty(material.LibraryMatches);
            material.LibrarySearch = string.Empty;
            Assert.False(material.IsLibraryOpen);
            Assert.Equal("Si", store.Document.Materials.Single().Name); // 输入关键字不改材料
        }

        [Fact]
        public void Library_TypingAFormula_ValuesFollowTheName_WithdrawnWhenNoLongerAFormula()
        {
            var library = new[]
            {
                new MaterialLibraryEntry("Si", "silicon", "硅", "2.320", "0.712", true),
                new MaterialLibraryEntry("SiO", "silicon monoxide", "一氧化硅", "2.130", "0.870", true),
                new MaterialLibraryEntry("SiO2", "silicon dioxide", "二氧化硅", "2.648", "1.000", true),
            };
            var store = new MemoryDesignStore();
            var device = DesignBuilderTests.NewDevice();
            var designer = new ProcessDesignerViewModel(store.Document, library, Read(device), store, new InstrumentConfigService(device), new FakeDialogService(), new FakeErrorLog());
            designer.AddMaterialCommand.Execute(null);
            var material = designer.SelectedMaterial!;

            // 逐字输入:中途填入的 Si、SiO 的数值跟着换成 SiO2 的 (以前停在 Si 的 2.320)。
            foreach (var name in new[] { "S", "Si", "SiO", "SiO2" })
            {
                material.Name = name;
            }

            Assert.Equal(("二氧化硅", "2.648", "1.000"), (material.ChineseName, material.Density, material.ZRatio));

            // 名称不再是化学式:撤回自动填入的中文名和数值 (没有改过的)。
            material.Name = "SiO2x";
            Assert.Equal(("", "", ""), (material.ChineseName, material.Density, material.ZRatio));

            // 用户改过的数值不动;从材料库选择的值改名时保留。
            material.Name = "Si";
            material.Density = "2.330";
            material.Name = "SiO";
            Assert.Equal(("一氧化硅", "2.330", "0.712"), (material.ChineseName, material.Density, material.ZRatio));
            material.LibraryPick = designer.Library[2];
            material.Name = "SiO2_hot";
            Assert.Equal(("二氧化硅", "2.648"), (material.ChineseName, material.Density));
        }

        [Fact]
        public void DelaySettings_OnlyWhenTheOptionIsNotZero_MaxPowerOptionIsADropDown()
        {
            var designer = NewDesigner(new MemoryDesignStore(), DesignBuilderTests.NewDevice(), new FakeDialogService());
            designer.AddMaterialCommand.Execute(null);
            var material = designer.SelectedMaterial!;
            var changed = new List<string?>();
            material.PropertyChanged += (s, e) => changed.Add(e.PropertyName);

            Assert.False(material.HasDelaySettings);
            material.DelayOption = "2";

            Assert.True(material.HasDelaySettings);
            Assert.Contains(nameof(MaterialDesignViewModel.HasDelaySettings), changed);
            Assert.Equal(new[] { "0", "1", "2", "3" }, material.DelayOptions.Select(o => o.Value));
            Assert.Equal(new[] { "0", "1", "2", "3" }, material.MaxPowerOptions.Select(o => o.Value));
            Assert.Contains("沉积后处理", material.MaxPowerOptions[1].Text);
            Assert.Equal(new[] { "0 - 无", "1 - Sensor" }, material.TransferSensorChoices.Select(c => c.Text));

            material.TransferSensor = 4;
            material.MaxPowerOption = "3";
            Assert.Equal("4 (不存在)", material.TransferSensorChoices.Last().Text);
            Assert.Equal((4, "3"), (material.Model.TransferSensor, material.Model.MaxPowerOption));

            material.DelayOption = "0";
            Assert.False(material.HasDelaySettings);
        }

        [Fact]
        public void Choices_CrucibleFollowsSource_MissingObjectsKept()
        {
            var designer = NewDesigner(new MemoryDesignStore(), DesignBuilderTests.NewDevice(), new FakeDialogService());
            designer.AddMaterialCommand.Execute(null);
            var material = designer.SelectedMaterial!;

            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, material.CrucibleChoices.Select(c => c.Value));
            Assert.Equal("0 - 任意", material.CrucibleChoices[0].Text);

            material.Source = 2; // 坩埚数量未知:只有任意和 1
            Assert.Equal(new[] { 0, 1 }, material.CrucibleChoices.Select(c => c.Value));

            material.Sensor = 3;
            Assert.Equal("3 (不存在)", material.SensorChoices.Last().Text);
            Assert.Equal("1 - Sensor", material.SensorChoices[0].Text);
        }

        [Fact]
        public async Task DeleteMaterial_UsedByProcess_IsRefused()
        {
            var dialogs = new FakeDialogService();
            var store = new MemoryDesignStore();
            var designer = Filled(store, DesignBuilderTests.NewDevice(), dialogs);

            await designer.DeleteMaterialAsync();

            Assert.Single(designer.Materials);
            Assert.Empty(dialogs.Errors); // 设备和文件都没有改变:红字,不弹窗
            Assert.Contains("被以下工艺使用,不能删除", designer.ResultText);
            Assert.True(designer.ResultIsError);
            Assert.Empty(dialogs.Questions);
        }

        [Theory]
        [InlineData(null, 1)]  // 取消:不删除
        [InlineData(false, 0)] // 否:只删除本地
        public async Task DeleteMaterial_CancelOrLocalOnly_DoesNotTouchTheDevice(bool? answer, int remaining)
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService { YesNoCancelResult = answer };
            var store = new MemoryDesignStore();
            var designer = NewDesigner(store, device, dialogs);
            designer.AddMaterialCommand.Execute(null);
            designer.SelectedMaterial!.LibraryPick = designer.Library[0];

            await designer.DeleteMaterialAsync();

            var question = Assert.Single(dialogs.Questions);
            Assert.StartsWith("删除材料 Al。同时删除设备上的对应数据吗?", question, StringComparison.Ordinal);
            Assert.Contains("按最近读取的设备内容,设备上没有它的数据。", question);
            Assert.Equal(remaining, designer.Materials.Count);
            Assert.Equal(remaining, store.Document.Materials.Count);
            Assert.Empty(device.Commands);
            Assert.Equal(0, store.SaveCount); // 本地的删除在保存后写入文件
        }

        /// <summary>
        /// 上传工艺 P1 (M02 Al、F02、L02、L03、P0202),再删除本地的工艺,只剩材料 Al。
        /// <paramref name="keepOnDevice"/> 为 true 时先把本地工艺改名 (删除时设备上没有同名的,P0202 保留),否则 P0202 已在别处删除。
        /// </summary>
        private static async Task<ProcessDesignerViewModel> UploadedThenProcessDeleted(MemoryDesignStore store, FakeDeviceService device, FakeDialogService dialogs, bool keepOnDevice = false)
        {
            var designer = Filled(store, device, dialogs);
            await designer.UploadAsync();
            if (keepOnDevice)
            {
                designer.SelectedProcess!.Name = "P1 local";
            }
            else
            {
                device.Remove("P0202");
            }

            await designer.DeleteProcessAsync();
            Assert.Empty(designer.Processes);
            designer.SelectedMaterial = designer.Materials.Single();
            ClearRecords(device, dialogs);
            return designer;
        }

        [Fact]
        public async Task DeleteMaterial_DeviceToo_DeletesLayersFilmAndMaterial_ThenLocal()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService { YesNoCancelResult = true };
            var store = new MemoryDesignStore();
            var designer = await UploadedThenProcessDeleted(store, device, dialogs);

            await designer.DeleteMaterialAsync();

            Assert.Empty(dialogs.Errors);
            var question = Assert.Single(dialogs.Questions); // 询问时就列出要删除的对象 (按最近读取的内容),对象相同就不再确认
            Assert.Contains("膜层: L03 Al-P1-2, L02 Al-P1-1", question);
            Assert.Contains("膜系: F02 Al", question);
            Assert.Contains("材料: M02 Al", question);
            Assert.DoesNotContain("M01", question); // 同组的其他对象不删除
            Assert.Empty(dialogs.Confirms);
            Assert.Equal(new[] { "DL03", "DL02", "DF02", "DM02" }, Deletes(device)); // 组内编号大的先删
            Assert.Equal(new[] { "?M", "?F", "?L", "?P", "?S", "?L", "?F", "?M" }, device.HiddenFrames); // 读取相关的组,删除后再读取确认
            Assert.Null(device.Get("M02"));
            Assert.NotNull(device.Get("M01"));
            Assert.Empty(designer.Materials);
            Assert.Empty(store.Document.Materials);
            Assert.True(designer.IsDirty);
            Assert.Equal("已从设备删除材料 Al 的数据: 膜层 L03, 膜层 L02, 膜系 F02, 材料 M02。本地材料在保存后删除。", designer.ResultText);
            Assert.Contains(designer.ResultText!, designer.CompletionMessage);
            Assert.Equal(new[] { "M01  M1" }, designer.Device.Materials.Select(m => m.Title)); // "设备"页随之更新
            Assert.False(designer.IsBusy);
        }

        [Fact]
        public async Task DeleteMaterial_DeviceToo_ReferencedByADeviceProcess_DeletesNothing()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService { YesNoCancelResult = true };
            var store = new MemoryDesignStore();
            var designer = await UploadedThenProcessDeleted(store, device, dialogs, keepOnDevice: true);

            await designer.DeleteMaterialAsync();

            Assert.Contains("选“是”时不会删除", Assert.Single(dialogs.Questions)); // 按最近读取的内容已经能看出被引用
            Assert.Empty(dialogs.Errors);
            Assert.True(designer.ResultIsError);
            Assert.StartsWith("设备上的以下设置引用了材料 Al 的数据,没有删除 (本地也未删除)。", designer.ResultText, StringComparison.Ordinal);
            Assert.Contains("工艺 P0202 P1: P02020701 = 2", designer.ResultText);
            Assert.Empty(dialogs.Confirms);
            Assert.Empty(Deletes(device));
            Assert.Single(designer.Materials);
        }

        [Fact]
        public async Task DeleteMaterial_DeviceChangedSinceTheQuestion_ConfirmsAgain_DeclinedDeletesNothing()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService { YesNoCancelResult = true };
            var store = new MemoryDesignStore();
            var designer = await UploadedThenProcessDeleted(store, device, dialogs);
            device.With("L09", string.Empty, "Layer L09").With("L0901", "Extra", "Layer Name").With("L09020101", "2", "Film"); // 别处新建的膜层,也用 F02
            dialogs.ConfirmResult = false;

            await designer.DeleteMaterialAsync();

            var confirm = Assert.Single(dialogs.Confirms);
            Assert.StartsWith("重新读取后设备上的内容有变化。", confirm, StringComparison.Ordinal);
            Assert.Contains("膜层: L09 Extra, L03 Al-P1-2, L02 Al-P1-1", confirm);
            Assert.Empty(Deletes(device));
            Assert.Single(designer.Materials);
        }

        [Fact]
        public async Task DeleteMaterial_DeviceToo_NothingOnTheDevice_DeletesLocally()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService { YesNoCancelResult = true };
            var designer = NewDesigner(new MemoryDesignStore(), device, dialogs);
            designer.AddMaterialCommand.Execute(null);
            designer.SelectedMaterial!.Name = "Au";

            await designer.DeleteMaterialAsync();

            Assert.Empty(dialogs.Confirms);
            Assert.Empty(designer.Materials);
            Assert.Equal("设备上没有材料 Au 的数据。本地材料在保存后删除。", designer.ResultText);
            Assert.Null(designer.CompletionMessage);
        }

        [Fact]
        public async Task DeleteMaterial_DeviceToo_StillThereAfterDeleting_KeepsTheLocalMaterial()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService { YesNoCancelResult = true };
            var store = new MemoryDesignStore();
            var designer = await UploadedThenProcessDeleted(store, device, dialogs);
            device.DeleteInstead["M02"] = "M99"; // 设备回答成功,但材料还在

            await designer.DeleteMaterialAsync();

            var error = Assert.Single(dialogs.Errors);
            Assert.Contains("重新读取后以下对象仍在设备上", error);
            Assert.Contains("材料 M02", error);
            Assert.EndsWith("本地材料未删除。", error, StringComparison.Ordinal);
            Assert.Single(designer.Materials);
            Assert.Contains(designer.Device.Materials, m => m.Id == "M02");
        }

        [Fact]
        public async Task DeleteProcess_DeletesTheSameNamedProcessAndItsLayersOnTheDevice_ThenLocal()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var store = new MemoryDesignStore();
            var designer = Filled(store, device, dialogs);
            await designer.UploadAsync();
            ClearRecords(device, dialogs);

            await designer.DeleteProcessAsync();

            Assert.Empty(dialogs.Errors);
            var confirm = Assert.Single(dialogs.Confirms);
            Assert.StartsWith("删除工艺 P1。设备上的同名工艺和只被它使用的膜层一起删除:", confirm, StringComparison.Ordinal);
            Assert.Contains("工艺: P0202 P1", confirm);
            Assert.Contains("膜层: L03 Al-P1-2, L02 Al-P1-1", confirm);
            Assert.Equal(new[] { "DP0202", "DL03", "DL02" }, Deletes(device)); // 先删工艺 (它引用膜层)
            Assert.Equal(new[] { "?P", "?L", "?P", "?L" }, device.HiddenFrames);
            Assert.Null(device.Get("P0202"));
            Assert.NotNull(device.Get("P0201"));
            Assert.NotNull(device.Get("F02")); // 膜系、材料不删除
            Assert.Empty(designer.Processes);
            Assert.Empty(store.Document.Processes);
            Assert.True(designer.IsDirty);
            Assert.Equal("已删除工艺 P1,设备上删除了: 工艺 P0202, 膜层 L03, 膜层 L02。本地的删除在保存后写入文件。", designer.ResultText);
            Assert.EndsWith(designer.ResultText!, designer.CompletionMessage, StringComparison.Ordinal);
            Assert.Equal(new[] { "P0201" }, designer.Device.Processes.Select(p => p.Id));
        }

        [Fact]
        public async Task DeleteProcess_NotOnTheDevice_ConfirmsAndDeletesLocally()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var designer = NewDesigner(new MemoryDesignStore(), device, dialogs);
            designer.AddProcessCommand.Execute(null);
            designer.SelectedProcess!.Name = "New";

            await designer.DeleteProcessAsync();

            Assert.Equal("删除工艺 New 吗?设备上没有同名的工艺。本地的删除在保存后写入文件。", Assert.Single(dialogs.Confirms));
            Assert.Equal(new[] { "?P", "?L" }, device.HiddenFrames);
            Assert.Empty(Deletes(device));
            Assert.Empty(designer.Processes);
            Assert.Equal("已删除工艺 New (设备上没有同名的工艺)。本地的删除在保存后写入文件。", designer.ResultText);
            Assert.Null(designer.CompletionMessage);
        }

        [Fact]
        public async Task DeleteProcess_Unnamed_DeletesLocallyWithoutReadingTheDevice()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var designer = NewDesigner(new MemoryDesignStore(), device, dialogs);
            designer.AddProcessCommand.Execute(null);

            await designer.DeleteProcessAsync();

            Assert.Equal("删除工艺 (未命名) 吗?本地的删除在保存后写入文件。", Assert.Single(dialogs.Confirms));
            Assert.Empty(device.HiddenFrames);
            Assert.Empty(designer.Processes);
        }

        [Fact]
        public async Task DeleteProcess_DeviceNotReadable_OffersToDeleteLocally()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var log = new FakeErrorLog();
            var store = new MemoryDesignStore();
            var designer = new ProcessDesignerViewModel(store.Document, Library, Read(device), store, new InstrumentConfigService(device), dialogs, log);
            designer.AddProcessCommand.Execute(null);
            designer.SelectedProcess!.Name = "P1";
            device.Remove("P02FE"); // ?P 的回答不能解析 (如连接已断开时的读取失败)

            await designer.DeleteProcessAsync();

            var confirm = Assert.Single(dialogs.Confirms);
            Assert.StartsWith("不能读取设备,无法同时删除设备上的同名工艺:", confirm, StringComparison.Ordinal);
            Assert.EndsWith("只删除本地的工艺 P1 吗?设备上的同名工艺不受影响。", confirm, StringComparison.Ordinal);
            Assert.Empty(dialogs.Errors);
            Assert.Single(log.Messages); // 读取失败写入 error.txt
            Assert.Empty(designer.Processes);
            Assert.StartsWith("已删除本地的工艺 P1", designer.ResultText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DeleteProcess_Declined_DeletesNothing()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var store = new MemoryDesignStore();
            var designer = Filled(store, device, dialogs);
            await designer.UploadAsync();
            ClearRecords(device, dialogs);
            dialogs.ConfirmResult = false;

            await designer.DeleteProcessAsync();

            Assert.Single(dialogs.Confirms);
            Assert.Empty(Deletes(device));
            Assert.Single(designer.Processes);
        }

        [Fact]
        public async Task DeleteProcess_ActiveOnTheDevice_DeletesNothing()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var store = new MemoryDesignStore();
            var designer = Filled(store, device, dialogs);
            await designer.UploadAsync();
            ClearRecords(device, dialogs);
            device.Set("P01", "2"); // 活动工艺是 P0202

            await designer.DeleteProcessAsync();

            Assert.Empty(dialogs.Errors);
            Assert.True(designer.ResultIsError);
            Assert.StartsWith("设备上的以下设置引用了工艺 P1 的数据,没有删除 (本地也未删除)。", designer.ResultText, StringComparison.Ordinal);
            Assert.EndsWith("设置活动工艺 P01 = 2", designer.ResultText, StringComparison.Ordinal);
            Assert.Empty(dialogs.Confirms);
            Assert.Empty(Deletes(device));
            Assert.Single(designer.Processes);
        }

        [Fact]
        public async Task DeleteProcess_LayerUsedByAnotherProcess_IsKept()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var store = new MemoryDesignStore();
            var designer = Filled(store, device, dialogs);
            await designer.UploadAsync();
            designer.AddProcessCommand.Execute(null);
            var second = designer.SelectedProcess!;
            second.Name = "P2";
            second.AddStepCommand.Execute(null);
            second.Steps[0].Rate = "4.0";
            second.Steps[0].Thickness = "0.300";
            await designer.UploadAsync(); // P0203 复用 L02
            designer.SelectedProcess = designer.Processes[0];
            ClearRecords(device, dialogs);

            await designer.DeleteProcessAsync();

            var confirm = Assert.Single(dialogs.Confirms);
            Assert.Contains("膜层: L03 Al-P1-2", confirm);
            Assert.Contains("保留 (其他工艺也在用):" + Environment.NewLine + "  膜层: L02 Al-P1-1", confirm);
            Assert.Equal(new[] { "DP0202", "DL03" }, Deletes(device));
            Assert.NotNull(device.Get("L02"));
            Assert.Equal("设备上: P0203", designer.Processes.Single().DeviceStatus);
        }

        [Fact]
        public async Task DevicePage_ListsTheDevice_LocalItemsShowWhatIsOnTheDevice()
        {
            var device = DesignBuilderTests.NewDevice();
            var store = new MemoryDesignStore();
            var designer = Filled(store, device, new FakeDialogService());
            var page = designer.Device;
            var material = designer.Materials.Single();

            Assert.Equal(new[] { "P0201  Process 1" }, page.Processes.Select(p => p.Title));
            Assert.Equal("没有膜层", page.Processes[0].Details);
            Assert.Equal(new[] { "M01  M1" }, page.Materials.Select(m => m.Title));
            Assert.Equal(("工艺 (1)", "材料 (1)"), (page.ProcessesTitle, page.MaterialsTitle));
            Assert.StartsWith("设备 IMC300 上的工艺和材料,更新于 ", page.StatusText, StringComparison.Ordinal);
            Assert.Equal("设备上没有", material.DeviceStatus);
            Assert.Equal("设备上没有", designer.Processes.Single().DeviceStatus);

            await designer.UploadAsync();

            // 上传后按写入的内容更新,不再读取。
            Assert.Equal(new[] { "P0201  Process 1", "P0202  P1" }, page.Processes.Select(p => p.Title));
            Assert.Equal(
                "1. L02 Al-P1-1: F02 Al (M02 Al), 4.0 Å/s, 0.300 kÅ" + Environment.NewLine + "2. L03 Al-P1-2: F02 Al (M02 Al), 2.0 Å/s, 0.100 kÅ",
                page.Processes[1].Details);
            Assert.Equal("密度 2.700 g/cm³,Z 比 1.080" + Environment.NewLine + "膜系: F02 Al", page.Materials[1].Details);
            Assert.Equal("设备上: M02, F02", material.DeviceStatus);
            Assert.Equal("设备上: P0202", designer.Processes.Single().DeviceStatus);

            var changed = new List<string?>();
            material.PropertyChanged += (s, e) => changed.Add(e.PropertyName);
            material.Name = "Al2";
            Assert.Contains(nameof(MaterialDesignViewModel.DeviceStatus), changed);
            Assert.Equal("设备上没有", material.DeviceStatus);
        }

        [Fact]
        public async Task DevicePage_Refresh_ReadsTheDeviceAgain()
        {
            var device = DesignBuilderTests.NewDevice();
            var designer = NewDesigner(new MemoryDesignStore(), device, new FakeDialogService());
            device.Remove("P0201");

            await designer.RefreshDeviceAsync();

            Assert.Equal(new[] { "?" }, device.HiddenFrames);
            Assert.Empty(designer.Device.Processes);
            Assert.Equal("工艺 (0)", designer.Device.ProcessesTitle);
        }

        [Fact]
        public async Task DevicePage_DeleteProcess_OnlyOnTheDevice()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var store = new MemoryDesignStore();
            var designer = Filled(store, device, dialogs);
            await designer.UploadAsync();
            ClearRecords(device, dialogs);
            designer.Device.SelectedProcess = designer.Device.Processes.Single(p => p.Id == "P0202");

            await designer.DeleteDeviceProcessAsync(designer.Device.SelectedProcess!);

            Assert.Equal(
                "将在设备上删除:" + Environment.NewLine + "  工艺: P0202 P1" + Environment.NewLine + "  膜层: L03 Al-P1-2, L02 Al-P1-1" + Environment.NewLine + "设备上的删除不能撤销。继续吗?",
                Assert.Single(dialogs.Confirms));
            Assert.Equal(new[] { "DP0202", "DL03", "DL02" }, Deletes(device));
            Assert.Equal("已从设备删除: 工艺 P0202, 膜层 L03, 膜层 L02。", designer.ResultText);
            Assert.Single(designer.Processes); // 本地的工艺不动
            Assert.Equal("设备上没有", designer.Processes[0].DeviceStatus);
            Assert.False(designer.IsDirty);
            Assert.Null(designer.Device.SelectedProcess);
        }

        [Fact]
        public async Task DevicePage_DeleteMaterial_BlockedWhileAProcessUsesIt_ThenDeletesFilmsAndLayers()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var store = new MemoryDesignStore();
            var designer = Filled(store, device, dialogs);
            await designer.UploadAsync();
            ClearRecords(device, dialogs);
            var al = designer.Device.Materials.Single(m => m.Id == "M02");

            await designer.DeleteDeviceMaterialAsync(al);

            Assert.Empty(dialogs.Errors);
            Assert.StartsWith("设备上的以下设置引用了材料 M02 Al 的数据,没有删除。", designer.ResultText, StringComparison.Ordinal);
            Assert.Contains("工艺 P0202 P1: P02020701 = 2", designer.ResultText);
            Assert.Empty(Deletes(device));

            device.Remove("P0202");
            await designer.DeleteDeviceMaterialAsync(al);

            Assert.Equal(new[] { "DL03", "DL02", "DF02", "DM02" }, Deletes(device));
            Assert.Equal("已从设备删除: 膜层 L03, 膜层 L02, 膜系 F02, 材料 M02。", designer.ResultText);
            Assert.Single(designer.Materials); // 本地的材料不动
            Assert.Equal("设备上没有", designer.Materials[0].DeviceStatus);
        }

        [Fact]
        public async Task DevicePage_DeleteSomethingAlreadyGone_SaysSo()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var designer = NewDesigner(new MemoryDesignStore(), device, dialogs);
            var process = designer.Device.Processes.Single();
            device.Remove("P0201");

            await designer.DeleteDeviceProcessAsync(process);

            Assert.Empty(dialogs.Confirms);
            Assert.Empty(Deletes(device));
            Assert.Equal("设备上已经没有 P0201。", designer.ResultText);
            Assert.Empty(designer.Device.Processes);
        }

        [Fact]
        public void Steps_MoveRemoveAndRamps_ChangeTheModel()
        {
            var store = new MemoryDesignStore();
            var designer = Filled(store, DesignBuilderTests.NewDevice(), new FakeDialogService());
            var process = designer.SelectedProcess!;
            var model = store.Document.Processes.Single();
            process.Steps[0].AddRampCommand.Execute(null);
            process.Steps[0].AddRampCommand.Execute(null);
            process.Steps[0].RateRamps[0].RemoveCommand.Execute(null);

            process.Steps[1].MoveUpCommand.Execute(null);

            Assert.Equal(new[] { "2.0", "4.0" }, model.Steps.Select(s => s.Rate));
            Assert.Equal(new[] { 1, 2 }, process.Steps.Select(s => s.Number));
            Assert.Single(model.Steps[1].RateRamps);
            Assert.Equal("速率斜坡 1", process.Steps[1].RateRamps[0].Title);
            Assert.False(process.Steps[0].MoveUpCommand.CanExecute(null));

            process.Steps[0].RemoveCommand.Execute(null);
            Assert.Equal(new[] { "4.0" }, model.Steps.Select(s => s.Rate));
        }

        [Fact]
        public void Steps_RateAndThickness_ShowAtMostThreeDecimalsInTheInputBox()
        {
            var designer = Filled(new MemoryDesignStore(), DesignBuilderTests.NewDevice(), new FakeDialogService());
            var step = designer.SelectedProcess!.Steps[0];
            step.AddRampCommand.Execute(null);
            var ramp = step.RateRamps[0];
            IDataErrorInfo stepErrors = step, rampErrors = ramp;

            Assert.Equal(string.Empty, stepErrors[nameof(ProcessStepViewModel.Rate)]); // 4.0
            Assert.Equal(string.Empty, rampErrors[nameof(RateRampViewModel.StartThickness)]); // 刚添加的空值不提示

            step.Rate = "4.0005";
            step.Thickness = "0.300";
            ramp.StartThickness = "0.1234";
            ramp.RampTime = "x";
            ramp.NewRate = "1.5";

            Assert.Equal("最多 3 位小数", stepErrors[nameof(ProcessStepViewModel.Rate)]);
            Assert.Equal("最多 3 位小数", step.RateError);
            Assert.Equal(string.Empty, stepErrors[nameof(ProcessStepViewModel.Thickness)]);
            Assert.Equal("最多 3 位小数", rampErrors[nameof(RateRampViewModel.StartThickness)]);
            Assert.NotEqual(string.Empty, rampErrors[nameof(RateRampViewModel.RampTime)]);
            Assert.Equal(string.Empty, rampErrors[nameof(RateRampViewModel.NewRate)]);
            Assert.Equal("4.0005", designer.SelectedProcess.Model.Steps[0].Rate); // 值照样写入,保存前的检查拦下
        }

        [Fact]
        public void Save_InvalidContent_ListsTheProblemsInTheWindow_SavesNothing()
        {
            var dialogs = new FakeDialogService();
            var store = new MemoryDesignStore();
            var designer = NewDesigner(store, DesignBuilderTests.NewDevice(), dialogs);
            designer.AddMaterialCommand.Execute(null);

            Assert.False(designer.Save());

            Assert.Equal(0, store.SaveCount);
            Assert.Empty(dialogs.Errors); // 红字,不弹窗
            Assert.True(designer.ResultIsError);
            Assert.StartsWith("有以下问题,没有保存:", designer.ResultText, StringComparison.Ordinal);
            Assert.Contains("材料 (未命名): 材料名称: 不能为空", designer.ResultText);
            Assert.True(designer.IsDirty);
        }

        [Fact]
        public void Save_WithAProcessSelected_SavesLocallyOnly_DoesNotTouchTheDevice()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var store = new MemoryDesignStore();
            var designer = Filled(store, device, dialogs);

            Assert.True(designer.Save());

            Assert.Equal(1, store.SaveCount);
            Assert.False(designer.IsDirty);
            Assert.Equal(@"已保存到 C:\Zevision\recipes。", designer.ResultText);
            Assert.False(designer.ResultIsError);
            Assert.Empty(dialogs.Confirms);
            Assert.Empty(device.Commands);
        }

        [Fact]
        public async Task Upload_SavesConfirmsAndUploads()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var store = new MemoryDesignStore();
            var designer = Filled(store, device, dialogs);

            await designer.UploadAsync();

            Assert.Empty(dialogs.Errors);
            var confirm = Assert.Single(dialogs.Confirms);
            Assert.Contains("材料: M02 Al", confirm);
            Assert.Contains("膜系: F02 Al", confirm);
            Assert.Contains("膜层: L02 Al-P1-1, L03 Al-P1-2", confirm);
            Assert.DoesNotContain("复用", confirm);
            Assert.Contains("工艺: P0202 P1", confirm);
            Assert.Equal(2, store.SaveCount); // 上传前保存一次,上传成功后记下上传记录再保存一次
            Assert.StartsWith("已上传工艺 P1:", designer.ResultText, StringComparison.Ordinal);
            Assert.Equal(designer.ResultText, designer.CompletionMessage);
            Assert.EndsWith(" P0202 (M02, F02, L02, L03)", designer.SelectedProcess!.LastUpload, StringComparison.Ordinal);
            Assert.Equal("2.700", device.Get("M0203"));
            Assert.Equal("10", device.Get("F020903"));
            Assert.Equal("1", device.Get("P020204"));
            Assert.Equal(("2", "3"), (device.Get("P02020701"), device.Get("P02020702")));
            Assert.False(designer.IsBusy);
        }

        [Fact]
        public async Task Upload_SameNamedProcessOnTheDevice_ConfirmationSaysAnotherIsCreated()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var designer = Filled(new MemoryDesignStore(), device, dialogs);
            await designer.UploadAsync();
            dialogs.Confirms.Clear();

            await designer.UploadAsync();

            Assert.Contains("设备上已有同名的工艺 P0202,上传后会再新建一个 (原有的不修改)。", Assert.Single(dialogs.Confirms));
        }

        [Fact]
        public async Task Upload_DeviceRejectsAWrite_DeletesWhatItCreated_ErrorBox_NoUploadRecord()
        {
            var device = DesignBuilderTests.NewDevice();
            device.RejectWrites.Add("M0203"); // 新建的材料的密度写不进去
            var dialogs = new FakeDialogService();
            var log = new FakeErrorLog();
            var store = new MemoryDesignStore();
            var designer = new ProcessDesignerViewModel(store.Document, Library, Read(device), store, new InstrumentConfigService(device), dialogs, log);
            designer.AddMaterialCommand.Execute(null);
            designer.SelectedMaterial!.LibraryPick = designer.Library[0];
            designer.AddProcessCommand.Execute(null);
            designer.SelectedProcess!.Name = "P1";
            designer.SelectedProcess.AddStepCommand.Execute(null);
            designer.SelectedProcess.Steps[0].Rate = "1.0";
            designer.SelectedProcess.Steps[0].Thickness = "0.100";

            await designer.UploadAsync();

            // 失败后删除本次新建的对象 (工艺、膜层、膜系、材料),设备回到上传前的样子;已有的对象不动。
            var error = Assert.Single(dialogs.Errors);
            Assert.StartsWith("上传失败:" + Environment.NewLine + "写入 M0203 失败", error, StringComparison.Ordinal);
            Assert.Contains("已删除本次上传在设备上新建的对象: 工艺 P0202, 膜层 L02, 膜系 F02, 材料 M02。", error);
            Assert.Equal(new[] { "DP0202", "DL02", "DF02", "DM02" }, Deletes(device));
            Assert.Null(device.Get("M02"));
            Assert.Null(device.Get("P0202"));
            Assert.NotNull(device.Get("M01"));
            Assert.NotNull(device.Get("P0201"));
            Assert.True(designer.ResultIsError);
            Assert.Equal("工艺 P1 上传失败,本次新建的对象已从设备删除。", designer.ResultText);
            Assert.Equal(designer.ResultText, designer.CompletionMessage); // 主窗口留下记录
            Assert.Equal(string.Empty, designer.SelectedProcess.LastUpload);
            Assert.StartsWith("设备 IMC300 上的工艺和材料,更新于 ", designer.Device.StatusText, StringComparison.Ordinal); // 删除后重新读取过
            Assert.Equal(new[] { "P0201" }, designer.Device.Processes.Select(p => p.Id));
            Assert.NotEmpty(log.Messages);
        }

        [Fact]
        public async Task Upload_DeleteAfterFailureRejected_SaysWhatMayStillBeOnTheDevice()
        {
            var device = DesignBuilderTests.NewDevice();
            device.RejectWrites.Add("M0203");
            var dialogs = new FakeDialogService();
            var designer = Filled(new MemoryDesignStore(), device, dialogs);
            device.DeleteInstead["L02"] = "L99"; // 设备回答删除成功,但膜层还在

            await designer.UploadAsync();

            var error = Assert.Single(dialogs.Errors);
            Assert.Contains("以下对象可能仍在设备上,可在“设备”页刷新后删除: 膜层 L02。", error);
            Assert.Equal("工艺 P1 上传失败,本次新建的对象没有全部删除,设备上可能仍有部分对象。", designer.ResultText);
            Assert.Equal("设备上的内容可能已改变,单击“刷新”重新读取。", designer.Device.StatusText);
        }

        [Fact]
        public async Task Upload_CancelledWhileWriting_DeletesWhatItCreated()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var designer = Filled(new MemoryDesignStore(), device, dialogs);
            device.BeforeWrite = n =>
            {
                if (n == 2)
                {
                    designer.Operation.Cancel(); // 写入阶段单击“取消上传”
                }
            };

            await designer.UploadAsync();

            var error = Assert.Single(dialogs.Errors);
            Assert.StartsWith("上传失败:" + Environment.NewLine + "已取消。", error, StringComparison.Ordinal);
            Assert.Contains("已删除本次上传在设备上新建的对象: 工艺 P0202, 膜层 L03, 膜层 L02, 膜系 F02, 材料 M02。", error);
            Assert.Equal(new[] { "DP0202", "DL03", "DL02", "DF02", "DM02" }, Deletes(device));
            Assert.Null(device.Get("M02"));
            Assert.NotNull(device.Get("P0201"));
            Assert.Equal("工艺 P1 上传失败,本次新建的对象已从设备删除。", designer.ResultText);
            Assert.Equal(string.Empty, designer.SelectedProcess!.LastUpload);
            Assert.False(designer.IsBusy);
        }

        [Fact]
        public async Task Upload_Declined_SavedNotUploaded()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService { ConfirmResult = false };
            var store = new MemoryDesignStore();
            var designer = Filled(store, device, dialogs);

            await designer.UploadAsync();

            Assert.Equal(1, store.SaveCount);
            Assert.Contains("未上传到设备", designer.ResultText);
            Assert.DoesNotContain(device.Commands, c => c.StartsWith("A", StringComparison.Ordinal) || c.StartsWith("U", StringComparison.Ordinal));
            Assert.Equal(string.Empty, store.Document.Processes.Single().LastUpload);
        }

        [Fact]
        public async Task Upload_DeviceCheckFails_ExplainsInTheWindow_DoesNotUpload()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            var store = new MemoryDesignStore();
            var designer = Filled(store, device, dialogs);
            designer.SelectedMaterial!.Crucible = 7; // 设备上的 S01 只有 6 个坩埚

            await designer.UploadAsync();

            Assert.Equal(1, store.SaveCount);
            Assert.Empty(dialogs.Confirms);
            Assert.Empty(dialogs.Errors);
            Assert.True(designer.ResultIsError);
            Assert.StartsWith("已保存到本地,但不能上传:", designer.ResultText, StringComparison.Ordinal);
            Assert.DoesNotContain(device.Commands, c => c.StartsWith("A", StringComparison.Ordinal));
        }

        [Fact]
        public void ConfirmClose_UnsavedChanges_Asks()
        {
            var dialogs = new FakeDialogService { ConfirmResult = false };
            var designer = NewDesigner(new MemoryDesignStore(), DesignBuilderTests.NewDevice(), dialogs);

            Assert.True(designer.ConfirmClose());

            designer.AddProcessCommand.Execute(null);

            Assert.False(designer.ConfirmClose());
            Assert.Contains("有未保存的修改", Assert.Single(dialogs.Confirms));
        }

        [Fact]
        public async Task DesignCommand_ReadsTheDevice_OpensWindowWithSensorsSourcesAndTheDevicePage()
        {
            var device = DesignBuilderTests.NewDevice();
            var dialogs = new FakeDialogService();
            ProcessDesignerViewModel? opened = null;
            dialogs.OnProcessDesigner = vm => opened = (ProcessDesignerViewModel)vm;
            var config = new ConfigOperationsViewModel(new InstrumentConfigService(device), new RecipeFileStore(), new MemoryDesignStore(), dialogs, new FakeErrorLog(), new LogViewModel(new FakeErrorLog()), () => true);

            await config.DesignAsync();

            Assert.NotNull(opened);
            Assert.Equal(new[] { "1 - Sensor" }, opened!.Sensors.Select(s => s.Text));
            Assert.Equal(new[] { "1 - Source" }, opened.Sources.Select(s => s.Text));
            Assert.Equal(new[] { "P0201  Process 1" }, opened.Device.Processes.Select(p => p.Title));
            Assert.Equal(new[] { "M01  M1" }, opened.Device.Materials.Select(m => m.Title));
            Assert.Equal(new[] { "?" }, device.HiddenFrames);
            Assert.False(config.IsBusy);
        }

        [Fact]
        public async Task DesignCommand_CorruptFile_RedLogLineWithoutWindow()
        {
            var dialogs = new FakeDialogService();
            var log = new LogViewModel(new FakeErrorLog());
            var config = new ConfigOperationsViewModel(new InstrumentConfigService(DesignBuilderTests.NewDevice()), new RecipeFileStore(), new MemoryDesignStore { LoadError = "坏了" }, dialogs, new FakeErrorLog(), log, () => true);

            await config.DesignAsync();

            Assert.Empty(dialogs.Errors); // 设备和文件都没有改变:数据显示区的红色错误行,不弹窗
            Assert.Contains(log.Entries, e => e.Kind == LogKind.Error && e.Text == "坏了");
            Assert.Empty(dialogs.Designers);
        }
    }
}
