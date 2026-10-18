using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Core.Configuration;
using Zevision.Core.Settings;
using Zevision.Storage;
using Zevision.ViewModels.Display;
using Zevision.ViewModels.Editor;
using Zevision.ViewModels.Main;
using Zevision.ViewModels.Operations;

namespace Zevision.Tests
{
    public sealed class RecipeEditorTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "zevision_editor_" + Guid.NewGuid().ToString("N"));

        public RecipeEditorTests()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }

        // ---------- 分组 ----------

        [Theory]
        [InlineData("M0301", "M03")]
        [InlineData("F01030101", "F01")]
        [InlineData("L01020101", "L01")]
        [InlineData("P020101", "P0201")]
        [InlineData("P01", "P01")]
        [InlineData("N010B01", "N01")]
        [InlineData("G0E03", "G0E")]
        public void GetGroupKey(string id, string key)
        {
            Assert.Equal(key, ObjectTypes.GroupKey(id));
        }

        [Fact]
        public void Build_OrdersSystemFirstAndAddsObjectNames()
        {
            var groups = RecipeGrouping.Build(new[]
            {
                Entry("P020101", "P1", "Name"),
                Entry("F0101", "L1", "Name"),
                Entry("F0102", "1", "Material"),
                Entry("M0101", "Al", "Name"),
                Entry("N0101", "Sensor", "Name"),
                Entry("L0101", "Layer1", "Layer Name"),
            });

            Assert.Equal(new[] { "N01", "M01", "F01", "L01", "P0201" }, groups.Select(g => g.Key));
            Assert.Equal("材料 M01  Al", groups[1].Title);
            Assert.Equal("膜层 L01  Layer1", groups[3].Title);
            Assert.Equal(2, groups[2].Entries.Count);
        }

        // ---------- 单项 ----------

        [Fact]
        public void Item_Uncheck_RevertsValue()
        {
            var item = new RecipeItemViewModel(Entry("M0103", "5.890", "Density"));
            item.IsChecked = true;
            item.Value = "2.700";
            Assert.True(item.IsModified);

            item.IsChecked = false;

            Assert.Equal("5.890", item.Value);
            Assert.False(item.IsModified);
        }

        [Theory]
        [InlineData("", "不能为空")]
        [InlineData("a;b", "; { }")]
        [InlineData("a}b", "; { }")]
        [InlineData("Å", "ASCII")]
        public void Item_InvalidValue_HasError(string value, string expected)
        {
            var item = new RecipeItemViewModel(Entry("M0101", "Al", "Name")) { IsChecked = true, Value = value };

            Assert.Contains(expected, item.Error);
        }

        // ---------- 编辑器 ----------

        [Fact]
        public void Filter_ShowsOnlyMatchingItemsAndExpandsGroups()
        {
            var (editor, _, _, _) = NewEditor(NewDevice());

            editor.FilterText = "density";

            var visible = editor.Groups.Where(g => g.IsVisible).ToList();
            Assert.Equal("M01", Assert.Single(visible).Key);
            Assert.True(visible[0].IsExpanded);
            Assert.Equal("M0103", Assert.Single(visible[0].Items, i => i.IsVisible).Id);
        }

        [Fact]
        public async Task Save_WritesCheckedItemsThenSavesFileWithValidMd5()
        {
            var device = NewDevice();
            var (editor, dialogs, path, _) = NewEditor(device);
            Check(editor, "M0103", "2.700");
            Check(editor, "F0101", "L9");

            await editor.SaveAsync();

            Assert.Empty(dialogs.Errors);
            Assert.Equal("2.700", device.Get("M0103"));
            Assert.Equal("L9", device.Get("F0101"));

            // 写入顺序:材料在膜层之前;写前先用 Q 确认设备型号和状态。
            var commands = device.Commands.Where(c => c != "?").ToList();
            Assert.Equal(new[] { "QG03", "QC09", "UM0103:2.700", "UF0101:L9", "QM0103", "QF0101" }, commands);

            var saved = RecipeFile.Load(path);
            Assert.True(saved.IsSuccess, saved.Error);
            Assert.Equal("2.700", saved.Value.Entries.Single(e => e.Id == "M0103").Value);
            Assert.Equal("L9", saved.Value.Entries.Single(e => e.Id == "F0101").Value);
            Assert.NotNull(editor.CompletionMessage);
            Assert.Equal(0, editor.CheckedCount);
        }

        [Fact]
        public async Task Save_FileSource_RequestsCloseWhenWindowCanClose()
        {
            // 窗口的 OnClosing 在 CanClose 为 false 时取消关闭:关闭请求必须在 IsBusy 复位之后发出。
            var (editor, _, _, _) = NewEditor(NewDevice());
            bool? canCloseAtRequest = null;
            editor.CloseRequested += (s, e) => canCloseAtRequest = editor.CanClose;
            Check(editor, "M0103", "2.700");

            await editor.SaveAsync();

            Assert.True(canCloseAtRequest);
        }

        [Fact]
        public async Task Save_DeviceRejects_FileUnchangedAndItemsStillChecked()
        {
            var device = NewDevice();
            device.RejectWrites.Add("F0101");
            var (editor, dialogs, path, log) = NewEditor(device);
            var before = File.ReadAllText(path);
            Check(editor, "M0103", "2.700");
            Check(editor, "F0101", "L9");

            await editor.SaveAsync();

            Assert.Contains("写入 F0101 失败", Assert.Single(dialogs.Errors));
            Assert.Contains("文件未修改", dialogs.Errors[0]);
            Assert.Equal(before, File.ReadAllText(path));
            Assert.Equal(2, editor.CheckedCount);
            Assert.Null(editor.CompletionMessage);
            Assert.Single(log.Messages);
        }

        [Fact]
        public async Task Save_ConfirmDeclined_SendsNothing()
        {
            var device = NewDevice();
            var (editor, dialogs, _, _) = NewEditor(device);
            dialogs.ConfirmResult = false;
            Check(editor, "M0103", "2.700");

            await editor.SaveAsync();

            Assert.DoesNotContain(device.Commands, c => c.StartsWith("U", StringComparison.Ordinal) || c.StartsWith("Q", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Save_DifferentDevice_FailsBeforeWriting()
        {
            var device = NewDevice().Set("G03", "IMC200");
            var (editor, dialogs, _, _) = NewEditor(device, recipeDevice: "IMC300");
            Check(editor, "M0103", "2.700");

            await editor.SaveAsync();

            Assert.Contains("型号不符", Assert.Single(dialogs.Errors));
            Assert.DoesNotContain(device.Commands, c => c.StartsWith("U", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Save_DeviceNotReady_FailsBeforeWriting()
        {
            var device = NewDevice().Set("C09", "Deposit");
            var (editor, dialogs, _, _) = NewEditor(device);
            Check(editor, "M0103", "2.700");

            await editor.SaveAsync();

            Assert.Contains("Deposit", Assert.Single(dialogs.Errors));
            Assert.DoesNotContain(device.Commands, c => c.StartsWith("U", StringComparison.Ordinal));
        }

        [Fact]
        public void CanSave_RequiresCheckedValidItems()
        {
            var (editor, _, _, _) = NewEditor(NewDevice());
            Assert.False(editor.CanSave);

            var item = Check(editor, "M0101", "Al2");
            Assert.True(editor.CanSave);

            item.Value = "a;b";
            Assert.False(editor.CanSave);
        }

        [Fact]
        public async Task ApplyEdits_ExcludedItem_RejectedBeforeAnyCommand()
        {
            var device = NewDevice();
            var service = new InstrumentConfigService(device);
            var recipe = new InstrumentRecipe { DeviceName = "IMC300" };

            var report = await service.ApplyEditsAsync(recipe, new[] { Entry("G1303", "9600", "Baud") }, null, CancellationToken.None);

            Assert.False(report.IsSuccess);
            Assert.Equal("G1303", report.FailedId);
            Assert.Empty(device.Commands);
        }

        // ---------- 分页、下拉框、规整、只读页 ----------

        [Fact]
        public void Tabs_ByCategoryInOrder_ReadOnlyItemsOnlyOnReadOnlyPage()
        {
            var (editor, _, _, _) = NewEditor(
                NewDevice(),
                "IMC300",
                Entry("T010101", "0", "Input 1: Basic Event"),
                Entry("O0101", "Output1", "Name"),
                Entry("I0101", "Input1", "Name"),
                Entry("N0101", "Sensor", "Name"),
                Entry("N0108", "1", "Number of Positions"),
                Entry("F0101", "F1", "Name"),
                Entry("M0101", "Al", "Name"),
                Entry("G0E03", "78", "Brightness"));

            // 材料、膜系、膜层、工艺页即使为空也显示 (可以新建)。
            Assert.Equal(new[] { "系统", "材料", "膜系", "膜层", "工艺", "传感器", "输入输出", "逻辑" }, editor.Tabs.Select(t => t.Title));
            Assert.DoesNotContain(editor.Groups.SelectMany(g => g.Items), i => i.Id == "N0108");
            Assert.Contains(editor.Info.Sections, s => s.Title == "传感器 N01  Sensor" && s.Items.Any(i => i.Id == "N0108"));
            Assert.Equal(new[] { "I01", "O01" }, editor.Tabs.Single(t => t.Letters == "IO").Groups.Select(g => g.Key));
        }

        [Fact]
        public void TabHeader_CountsCheckedItems()
        {
            var (editor, _, _, _) = NewEditor(NewDevice());

            Check(editor, "M0103", "2.700");

            Assert.Equal("材料 (1)", editor.Tabs.Single(t => t.Title == "材料").Header);
            Assert.Equal("膜系", editor.Tabs.Single(t => t.Title == "膜系").Header);
        }

        [Fact]
        public void Item_LabelUnitAndToolTip()
        {
            var item = RecipeItemViewModel.Create(Entry("M0103", "5.890", "Density (g/cm^3)"), new ParameterOptions(Array.Empty<RecipeEntry>()));

            Assert.Equal("密度 (g/cm³)", item.Label);
            Assert.False(item.UsesOptions);
            Assert.Contains("范围: 0.100 ~ 100.000 g/cm³", item.ToolTip);
            Assert.Contains("设备说明: Density (g/cm^3)", item.ToolTip);
        }

        [Fact]
        public void Item_Undocumented_UsesDeviceDescription()
        {
            var item = RecipeItemViewModel.Create(Entry("T020101", "0", "Advanced Logic"), new ParameterOptions(Array.Empty<RecipeEntry>()));

            Assert.Equal("Advanced Logic", item.Label);
            Assert.Contains("协议文档未列出", item.ToolTip);
            item.IsChecked = true;
            item.Value = "anything";
            Assert.False(item.HasError);
        }

        [Fact]
        public void Item_LogicBasicIo_IsDropdown()
        {
            var item = RecipeItemViewModel.Create(Entry("T010201", "4", "Output 1: Basic Action"), new ParameterOptions(Array.Empty<RecipeEntry>()));

            Assert.Equal("输出 1 · 基本动作", item.Label);
            Assert.True(item.UsesOptions);
            Assert.Equal("4 - 传感器挡板 2", item.DisplayValue);
        }

        [Fact]
        public void Item_EnumWithUnknownCurrentValue_KeepsValueAsOption()
        {
            var item = RecipeItemViewModel.Create(Entry("G0E01", "2", "Turn On/Off Display"), new ParameterOptions(Array.Empty<RecipeEntry>()));

            Assert.Equal(new[] { "0 - 关", "1 - 开", "2 - (未知)" }, item.Options.Select(o => o.Label));
        }

        [Fact]
        public void Item_FilmCrucible_IsAnyThenOneToSourceCrucibleCount()
        {
            // 设备上的坩埚号选项:Any、1 ~ 膜系所选输出源的坩埚数量。
            var entries = new[] { Entry("F010901", "2", "Source Setting"), Entry("F010902", "0", "Crucible"), Entry("S0107", "6", "Number of Crucibles"), Entry("S0207", "3", "Number of Crucibles") };

            var item = RecipeItemViewModel.Create(entries[1], new ParameterOptions(entries));

            Assert.True(item.UsesOptions);
            Assert.Equal(new[] { "0 - 任意", "1", "2", "3" }, item.Options.Select(o => o.Label));
            Assert.Equal("0 - 任意", item.DisplayValue);
        }

        [Fact]
        public void Item_FilmCrucible_SourceUnknown_DefaultsToOneAndKeepsValue()
        {
            var noSource = new[] { Entry("F010901", "0", "Source Setting"), Entry("F010902", "4", "Crucible") };

            var item = RecipeItemViewModel.Create(noSource[1], new ParameterOptions(noSource));

            Assert.Equal(new[] { "0 - 任意", "1", "4 - (未知)" }, item.Options.Select(o => o.Label));
        }

        [Fact]
        public void Item_ReferenceOptionsFromFile_MissingTargetMarked()
        {
            var entries = new[] { Entry("M0101", "Al", "Name"), Entry("F0101", "F1", "Name"), Entry("F0102", "3", "Material") };

            var item = RecipeItemViewModel.Create(entries[2], new ParameterOptions(entries));

            Assert.Equal(new[] { "0 - 未选择", "1 - Al", "3 - (不存在)" }, item.Options.Select(o => o.Label));
        }

        [Fact]
        public void Item_ProcessLayer_IsLayerDropdown_DanglingValueKept()
        {
            // 工艺包含膜层:工艺膜层列表的值选择已有的膜层;实机上指向不存在膜层的值 (如 31) 保留并标出。
            var entries = new[] { Entry("L0101", "Layer1", "Layer Name"), Entry("L0201", "Layer2", "Layer Name"), Entry("P02010701", "2", "Layer"), Entry("P02010702", "49", "Layer") };

            var existing = RecipeItemViewModel.Create(entries[2], new ParameterOptions(entries));
            var dangling = RecipeItemViewModel.Create(entries[3], new ParameterOptions(entries));

            Assert.True(existing.UsesOptions);
            Assert.Equal(new[] { "0 - 未选择", "1 - Layer1", "2 - Layer2" }, existing.Options.Select(o => o.Label));
            Assert.Equal("2 - Layer2", existing.DisplayValue);
            Assert.Equal("49 - (不存在)", dangling.Options.Last().Label);
            Assert.Equal("49", dangling.Value);
        }

        [Fact]
        public void Item_RangeCheckedOnlyAfterModification()
        {
            // 设备现有值超出文档范围时原样写回不拦截;修改后才按范围检查。
            var item = RecipeItemViewModel.Create(Entry("G0E03", "10", "Brightness"), new ParameterOptions(Array.Empty<RecipeEntry>()));
            item.IsChecked = true;
            Assert.False(item.HasError);

            item.Value = "15";
            Assert.Contains("超出范围", item.Error);

            item.Value = "60";
            Assert.False(item.HasError);
        }

        [Fact]
        public async Task Save_NormalizesValuesBeforeWriting()
        {
            var device = NewDevice();
            var (editor, dialogs, path, _) = NewEditor(device);
            var item = Check(editor, "M0103", "2.7");

            await editor.SaveAsync();

            Assert.Empty(dialogs.Errors);
            Assert.Equal("2.700", item.Value);
            Assert.Contains("UM0103:2.700", device.Commands);
            Assert.Contains("5.890 → 2.700", Assert.Single(dialogs.Confirms));
            Assert.Equal("2.700", RecipeFile.Load(path).Value.Entries.Single(e => e.Id == "M0103").Value);
        }

        [Fact]
        public async Task RefreshReadOnly_QueriesInBatchesAndTranslatesEnums()
        {
            var device = NewDevice().With("C18", "1", "Crystal Fail").With("G06", "12345", "Serial");
            var (editor, _, _, _) = NewEditor(device);

            await editor.Info.RefreshAsync();

            var items = editor.Info.Sections.SelectMany(s => s.Items).ToDictionary(i => i.Id);
            Assert.Equal("IMC300", items["G03"].Value);
            Assert.Equal("12345", items["G06"].Value);
            Assert.Equal("1 - 失效", items["C18"].Value);
            Assert.Equal(ReadOnlyItemViewModel.NoValue, items["G02"].Value);
            Assert.Contains("读取失败", items["G02"].ToolTip);
            Assert.StartsWith("读取于", editor.Info.Status);
            Assert.False(editor.Info.IsRefreshing);

            // 批量:每帧最多 QueryBatchSize 条。
            Assert.All(device.Frames, f => Assert.True(f.Split(';').Length <= InstrumentConfigReader.QueryBatchSize));
            Assert.Equal((items.Count + InstrumentConfigReader.QueryBatchSize - 1) / InstrumentConfigReader.QueryBatchSize, device.Frames.Count);
        }

        // ---------- 慢响应的最低超时 ----------

        [Fact]
        public async Task ReadSnapshotAndBatchQuery_UseSlowReplyTimeout()
        {
            var device = NewDevice();
            var service = new InstrumentConfigService(device);

            await service.ReadSnapshotAsync(CancellationToken.None);
            await service.QueryAsync(new[] { "G03", "C09" }, CancellationToken.None);

            Assert.Equal(new[] { InstrumentConfigReader.SlowReplyTimeout, InstrumentConfigReader.SlowReplyTimeout }, device.MinimumTimeouts);
            Assert.True(InstrumentConfigReader.SlowReplyTimeout >= TimeSpan.FromSeconds(1)); // 实机首字节约 380 ms、分包停顿约 350 ms
        }

        // ---------- 修改来源:实机数据 / 保存的配置 ----------

        [Theory]
        [InlineData("File", EditSource.File)]
        [InlineData("Device", EditSource.Device)]
        [InlineData("1", EditSource.Device)]       // 数字不接受
        [InlineData("Garbage", EditSource.Device)]
        public void EditSource_RestoredFromPreferences(string stored, EditSource expected)
        {
            var store = new MemoryPreferencesStore(new ConnectionPreferences { EditSource = stored });

            using (var main = NewMain(store))
            {
                Assert.Equal(expected, main.Config.EditSource);
            }
        }

        [Fact]
        public void EditSource_SavedToPreferences()
        {
            var store = new MemoryPreferencesStore(new ConnectionPreferences());
            using (var main = NewMain(store))
            {
                main.Config.EditSource = EditSource.File;
                main.SavePreferences();
            }

            Assert.Equal("File", store.Saved!.EditSource);
        }

        private static MainViewModel NewMain(IPreferencesStore store)
        {
            var device = NewDevice();
            return new MainViewModel(device, store, new InstrumentConfigService(device), new RecipeFileStore(), new NoCommunicationRecorders(), new FakeDialogService(), new FakeErrorLog());
        }

        [Fact]
        public async Task Edit_DeviceSource_ReadsDeviceThenOpensEditorWithoutFile()
        {
            var device = NewDevice();
            var dialogs = new FakeDialogService();
            var config = NewConfigOperations(device, dialogs);
            Assert.Equal(EditSource.Device, config.EditSource); // 默认实机数据

            await config.EditAsync();

            // 只读页直接用 ? 读到的数据:只有这一次通讯,不再用 Q 重复查询。
            Assert.Equal(new[] { "?" }, device.Frames);
            var editor = Assert.IsType<RecipeEditorViewModel>(Assert.Single(dialogs.Editors));
            var readOnly = editor.Info.Sections.SelectMany(s => s.Items).ToDictionary(i => i.Id);
            Assert.Equal("IMC300", readOnly["G03"].Value);
            Assert.Equal("Ready", readOnly["C09"].Value);
            Assert.Equal(ReadOnlyItemViewModel.NoValue, readOnly["G02"].Value); // 设备上没有的项
            Assert.StartsWith("读取于", editor.Info.Status);
            Assert.True(editor.IsDeviceSource);
            Assert.Equal("修改仪器配置 - 实机数据", editor.Title);
            Assert.StartsWith("实机数据", editor.SourceText);
            Assert.Contains(editor.Groups.SelectMany(g => g.Items), i => i.Id == "M0103" && i.Value == "5.890");
            Assert.False(config.IsBusy);
        }

        [Fact]
        public async Task Edit_FileSource_OpensChosenFile()
        {
            var device = NewDevice();
            var (_, _, path, _) = NewEditor(device);
            var dialogs = new FakeDialogService { OpenFilePath = path };
            var config = NewConfigOperations(device, dialogs);
            config.EditSource = EditSource.File;

            await config.EditAsync();

            var editor = Assert.IsType<RecipeEditorViewModel>(Assert.Single(dialogs.Editors));
            Assert.False(editor.IsDeviceSource);
            Assert.Equal("修改仪器配置 - recipe.json", editor.Title);
            Assert.DoesNotContain("?", device.Commands);
        }

        [Fact]
        public async Task Edit_FileSource_Cancelled_OpensNothing()
        {
            var dialogs = new FakeDialogService { OpenFilePath = null };
            var config = NewConfigOperations(NewDevice(), dialogs);
            config.EditSource = EditSource.File;

            await config.EditAsync();

            Assert.Empty(dialogs.Editors);
        }

        [Fact]
        public async Task Save_DeviceSource_WritesDeviceKeepsWindowOpenAndSavesNoFile()
        {
            var device = NewDevice();
            var dialogs = new FakeDialogService();
            var recipe = new InstrumentRecipe { DeviceName = "IMC300", FirmwareVersion = "1.8.11", SavedAt = "2026-10-16T09:00:00.0000000+08:00" };
            recipe.Entries.Add(Entry("M0101", "Al", "Name"));
            recipe.Entries.Add(Entry("M0103", "5.890", "Density (g/cm^3)"));
            var editor = new RecipeEditorViewModel(RecipeEditSession.Open(recipe, null, new InstrumentConfigService(device), new RecipeFileStore()), dialogs, new FakeErrorLog());
            var closed = false;
            editor.CloseRequested += (s, e) => closed = true;
            var item = Check(editor, "M0103", "2.7");

            await editor.SaveAsync();

            Assert.Empty(dialogs.Errors);
            Assert.Equal("2.700", device.Get("M0103"));
            Assert.Contains("不保存文件", Assert.Single(dialogs.Confirms));
            Assert.Equal("2.700", item.OriginalValue);
            Assert.False(item.IsChecked);
            Assert.Equal("2.700", recipe.Entries.Single(e => e.Id == "M0103").Value);
            Assert.Contains("实机", editor.CompletionMessage);
            Assert.Equal("已写入设备并校验 1 项。", editor.ResultText); // 显示在窗口底部,不弹窗
            Assert.True(editor.HasResultText);
            Assert.False(closed);
            Assert.Empty(Directory.GetFiles(_directory));
        }

        [Fact]
        public async Task SaveConfiguration_Success_LoggedWithoutDialog()
        {
            var path = Path.Combine(_directory, "saved.json");
            var dialogs = new FakeDialogService { SaveFilePath = path };
            var log = new LogViewModel(new FakeErrorLog());
            var config = new ConfigOperationsViewModel(new InstrumentConfigService(NewDevice()), new RecipeFileStore(), dialogs, new FakeErrorLog(), log, () => true);

            await config.SaveAsync();

            Assert.True(File.Exists(path));
            Assert.Empty(dialogs.Errors);
            Assert.Contains(log.Entries, e => e.Kind == LogKind.Info && e.Text.StartsWith("已保存", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Restore_DeviceAlreadyMatches_LoggedWithoutDialog()
        {
            var device = NewDevice();
            var (_, _, path, _) = NewEditor(device);
            var dialogs = new FakeDialogService { OpenFilePath = path };
            var log = new LogViewModel(new FakeErrorLog());
            var config = new ConfigOperationsViewModel(new InstrumentConfigService(device), new RecipeFileStore(), dialogs, new FakeErrorLog(), log, () => true);

            await config.RestoreAsync();

            Assert.Empty(dialogs.Confirms);
            Assert.Empty(dialogs.Errors);
            Assert.Contains(log.Entries, e => e.Kind == LogKind.Info && e.Text.Contains("无需修改"));
        }

        [Fact]
        public async Task Restore_Failure_StillShowsError()
        {
            var device = NewDevice();
            var (_, _, path, _) = NewEditor(device, recipeDevice: "IMC200");
            var dialogs = new FakeDialogService { OpenFilePath = path };
            var config = NewConfigOperations(device, dialogs);

            await config.RestoreAsync();

            Assert.Contains("IMC200", Assert.Single(dialogs.Errors)); // 型号不同,拒绝恢复
        }

        [Fact]
        public void Rows_GroupHeadersAndTheVisibleRowsOfExpandedGroups()
        {
            // "设置"页显示的是一个平铺的行列表 (虚拟化):分组标题,展开时其后是可见的配置项。
            var (editor, _, _, _) = NewEditor(NewDevice());
            var tab = editor.Tabs.Single(t => t.Groups.Any(g => g.Key == "M01"));
            var group = tab.Groups.Single(g => g.Key == "M01");
            Assert.True(group.IsExpanded); // 分组很少的页默认展开
            Assert.Equal(new object[] { group }.Concat(group.Items), tab.Rows);

            group.IsExpanded = false;
            Assert.Equal(new object[] { group }, tab.Rows);

            editor.FilterText = "M0103"; // 匹配的分组自动展开,只显示匹配的项
            Assert.Equal(new object[] { group, group.Items.Single(i => i.Id == "M0103") }, tab.Rows);

            editor.FilterText = "no such setting";
            Assert.Empty(tab.Rows);

            editor.FilterText = string.Empty;
            Assert.Equal(new object[] { group }.Concat(group.Items), tab.Rows);
        }

        private static ConfigOperationsViewModel NewConfigOperations(FakeDeviceService device, FakeDialogService dialogs) =>
            new ConfigOperationsViewModel(new InstrumentConfigService(device), new RecipeFileStore(), dialogs, new FakeErrorLog(), new LogViewModel(new FakeErrorLog()), () => true);

        private static FakeDeviceService NewDevice() => new FakeDeviceService()
            .With("C09", "Ready", "Current State")
            .With("G03", "IMC300", "Device Name")
            .With("F01", string.Empty, "Film F01")
            .With("F0101", "L1", "Name")
            .With("M01", string.Empty, "Material M01")
            .With("M0101", "Al", "Name")
            .With("M0103", "5.890", "Density (g/cm^3)");

        [Fact]
        public void Filter_ManyMatchingGroups_FiltersWithoutExpanding()
        {
            // 短筛选匹配大量分组时不全部展开 (展开的分组逐行生成控件,上百个会卡住窗口)。
            var entries = Enumerable.Range(1, RecipeEditorViewModel.MaxAutoExpandGroups + 5)
                .Select(i => Entry("M" + i.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "01", "Mat" + i, "Name"))
                .ToArray();
            var (editor, _, _, _) = NewEditor(NewDevice(), "IMC300", entries);

            editor.FilterText = "Mat";

            Assert.All(editor.Groups, g => Assert.True(g.IsVisible));
            Assert.DoesNotContain(editor.Groups, g => g.IsExpanded);

            editor.FilterText = "Mat7";
            Assert.True(editor.Groups.Single(g => g.IsVisible).IsExpanded);
        }

        private (RecipeEditorViewModel Editor, FakeDialogService Dialogs, string Path, FakeErrorLog Log) NewEditor(
            FakeDeviceService device, string recipeDevice = "IMC300", params RecipeEntry[] entries)
        {
            var recipe = new InstrumentRecipe { DeviceName = recipeDevice, FirmwareVersion = "1.8.11", SavedAt = "2026-10-15T17:00:00.0000000+08:00" };
            if (entries.Length == 0)
            {
                entries = new[] { Entry("M0101", "Al", "Name"), Entry("M0103", "5.890", "Density (g/cm^3)"), Entry("F0101", "L1", "Name") };
            }

            recipe.Entries.AddRange(entries);
            var path = Path.Combine(_directory, "recipe.json");
            Assert.True(RecipeFile.Save(recipe, path).IsSuccess);

            var dialogs = new FakeDialogService();
            var log = new FakeErrorLog();
            var editor = new RecipeEditorViewModel(RecipeEditSession.Open(RecipeFile.Load(path).Value, path, new InstrumentConfigService(device), new RecipeFileStore()), dialogs, log);
            return (editor, dialogs, path, log);
        }

        private static RecipeItemViewModel Check(RecipeEditorViewModel editor, string id, string value)
        {
            var item = editor.Groups.SelectMany(g => g.Items).Single(i => i.Id == id);
            item.IsChecked = true;
            item.Value = value;
            return item;
        }

        private static RecipeEntry Entry(string id, string value, string description) =>
            new RecipeEntry { Id = id, Value = value, Description = description };
    }
}
