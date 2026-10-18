using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Commands;
using Zevision.Core.Configuration;
using Zevision.Storage;
using Zevision.ViewModels.Editor;

namespace Zevision.Tests
{
    /// <summary>修改配置窗口 (实机数据模式) 中材料、膜、工艺的新建与删除。</summary>
    public class ObjectEditTests
    {
        [Fact]
        public async Task Create_Material_UsesFirstFreeNumberAndRebuildsTab()
        {
            var device = NewDevice();
            device.Templates['M'] = id => new[] { (id, string.Empty, "Material " + id), (id + "01", "Material", "Name"), (id + "03", "1.000", "Density (g/cm^3)") };
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);
            editor.SelectedTabIndex = TabIndex(editor, "材料");

            await editor.CreateObjectAsync('M');

            Assert.Empty(dialogs.Errors);
            Assert.Contains("AM02", device.CommandsSnapshot()); // M01、M03 已存在,第一个空编号是 M02
            Assert.Contains("?M", device.CommandsSnapshot());   // 用组帮助重新读取,不读完整的 ?
            var created = editor.Groups.Single(g => g.Key == "M02");
            Assert.True(created.IsExpanded);
            Assert.True(created.CanDelete);
            Assert.All(created.Items, i => Assert.True(i.IsChecked, i.Id)); // 全部参数勾选,可以直接修改
            Assert.Equal(TabIndex(editor, "材料"), editor.SelectedTabIndex); // 重新生成后仍在材料页
            Assert.Contains("已新建材料 M02", editor.ResultText); // 结果显示在窗口底部,不弹窗
            Assert.Contains("已新建材料 M02", editor.CompletionMessage);
        }

        [Fact]
        public async Task Create_Film_SetsFirstMaterialAndChecksAllSettings()
        {
            var device = NewDevice();
            device.Templates['F'] = id => new[] { (id, string.Empty, "Film " + id), (id + "01", "Film", "Name"), (id + "02", "0", "Material ID") };
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.CreateObjectAsync('F');

            Assert.Empty(dialogs.Errors);
            Assert.Contains("材料 ID 设为第一个材料 1 - Al", Assert.Single(dialogs.Confirms));
            var commands = device.CommandsSnapshot().SkipWhile(c => c != "AF02").Where(c => c[0] != 'Q').ToList();
            Assert.Equal(new[] { "AF02", "UF0202:1", "?F" }, commands);
            Assert.Equal("1", device.Get("F0202"));
            var film = editor.Groups.Single(g => g.Key == "F02");
            Assert.All(film.Items, i => Assert.True(i.IsChecked, i.Id)); // 全部参数勾选,可以直接修改
            Assert.Equal("1", film.Items.Single(i => i.Id == "F0202").Value);
            Assert.False(editor.CanChangeObjects); // 有勾选的项
        }

        [Fact]
        public async Task Create_Layer_AddsFirstFilmAndChecksAllSettings()
        {
            var device = NewLayerDevice();
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.CreateObjectAsync('L');

            Assert.Empty(dialogs.Errors);
            Assert.Contains("第一个膜系 1 - Film1", Assert.Single(dialogs.Confirms));
            var commands = device.CommandsSnapshot().SkipWhile(c => c != "AL02").Where(c => c[0] != 'Q').ToList();
            Assert.Equal(new[] { "AL02", "AL020200", "UL02020101:1", "?L" }, commands);

            // 设备状态 (G03、C09) 合为一帧,整个新建只检查一次:7 帧 (原来 12 帧)。
            Assert.Equal(
                new[] { "QG03;QC09", "AL02", "AL020200", "QL0202FE", "UL02020101:1", "QL02020101", "?L" },
                device.Frames.SkipWhile(f => f != "QG03;QC09"));
            var layer = editor.Groups.Single(g => g.Key == "L02");
            Assert.Equal(new[] { "L0201", "L02020101", "L02020102", "L02020103" }, layer.Items.Select(i => i.Id));
            Assert.All(layer.Items, i => Assert.True(i.IsChecked, i.Id)); // 全部参数勾选,可以直接修改
            Assert.Equal("1", layer.Items.Single(i => i.Id == "L02020101").Value);
            Assert.Empty(layer.EmptyLists);
        }

        [Fact]
        public async Task Create_Layer_NoFilm_AddsNoFilmEntry()
        {
            // 确认框说明"没有膜系,膜层不添加膜系":不能追加一个膜系为设备默认值的条目。
            var device = NewLayerDevice().Remove("F01").Remove("FFE");
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.CreateObjectAsync('L');

            Assert.Empty(dialogs.Errors);
            Assert.Contains("没有膜系", Assert.Single(dialogs.Confirms));
            Assert.DoesNotContain("AL020200", device.CommandsSnapshot());
            Assert.Single(editor.Groups.Single(g => g.Key == "L02").EmptyLists); // 空的膜系列表显示说明和添加按钮
        }

        [Fact]
        public async Task File_Create_Layer_FileContainsFilmEntry()
        {
            var device = NewLayerDevice();
            using (var file = await FileEditor.OpenAsync(NewDevice(), device))
            {
                await file.Editor.CreateObjectAsync('L');

                Assert.Empty(file.Dialogs.Errors);
                var saved = RecipeFile.Load(file.Path).Value.Entries;
                Assert.Contains(saved, e => e.Id == "L02020101" && e.Value == "1");
                Assert.Contains(saved, e => e.Id == "L02020103");
            }
        }

        [Fact]
        public async Task Create_Process_UsesP02Prefix_AndAddsFirstLayer()
        {
            var device = NewProcessDevice();
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.CreateObjectAsync('P');

            Assert.Empty(dialogs.Errors);
            Assert.Contains("第一个膜层 1 - Layer1", Assert.Single(dialogs.Confirms));
            var commands = device.CommandsSnapshot().SkipWhile(c => c != "AP0202").ToList();
            Assert.Equal(new[] { "AP0202", "AP02020700", "QP020207FE", "UP02020701:1", "QP02020701", "?P" }, commands); // 设备状态只检查一次
            var process = editor.Groups.Single(g => g.Key == "P0202");
            Assert.Contains(process.Items, i => i.Id == "P02020701" && i.Value == "1" && i.CanRemove);
            Assert.All(process.Items, i => Assert.True(i.IsChecked, i.Id));
            Assert.False(editor.Groups.Single(g => g.Key == "P01").CanDelete); // P01 (活动工艺) 不是对象
        }

        [Fact]
        public async Task Create_Process_NoLayer_AddsNoLayer()
        {
            var device = NewProcessDevice().Remove("L01").Remove("L0101").Remove("LFE");
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.CreateObjectAsync('P');

            Assert.Empty(dialogs.Errors);
            Assert.Contains("没有膜层", Assert.Single(dialogs.Confirms));
            Assert.DoesNotContain("AP02020700", device.CommandsSnapshot());
            Assert.Contains(editor.Groups, g => g.Key == "P0202");
        }

        [Fact]
        public async Task File_Create_Process_FileContainsFirstLayer()
        {
            using (var file = await FileEditor.OpenAsync(NewDevice(), NewProcessDevice()))
            {
                await file.Editor.CreateObjectAsync('P');

                Assert.Empty(file.Dialogs.Errors);
                var saved = RecipeFile.Load(file.Path).Value.Entries;
                Assert.Contains(saved, e => e.Id == "P02020701" && e.Value == "1");
            }
        }

        [Fact]
        public async Task Delete_ReferencedMaterial_IsBlockedAndListsReferences()
        {
            var device = NewDevice();
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);
            var before = device.CommandsSnapshot().Count;

            await editor.DeleteObjectAsync("M03");

            var error = Assert.Single(dialogs.Errors);
            Assert.Contains("不能删除材料 M03 (Au)", error);
            Assert.Contains("膜系 F01  Film1", error);   // 引用它的对象
            Assert.Contains("F0102 = 3", error);         // 引用它的设置项
            Assert.Contains("或 0 (未选择)", error);
            Assert.Empty(dialogs.Confirms);
            Assert.Equal(before, device.CommandsSnapshot().Count); // 没有发送任何指令
            Assert.Contains(editor.Groups, g => g.Key == "M03");
        }

        [Fact]
        public async Task Delete_UnreferencedMaterial_DeletesAndRereadsOnlyMaterials()
        {
            var device = NewDevice();
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.DeleteObjectAsync("M01");

            Assert.Empty(dialogs.Errors);
            var commands = device.CommandsSnapshot();
            Assert.Contains("DM01", commands);
            Assert.Contains("?M", commands);
            Assert.DoesNotContain("?F", commands); // 只重新读取当前页 (用户要求)
            Assert.DoesNotContain(editor.Groups, g => g.Key == "M01");
        }

        [Fact]
        public async Task Delete_ActiveProcess_IsBlocked()
        {
            var (editor, dialogs) = await OpenDeviceEditorAsync(NewDevice()); // P01 = 1 → P0201

            await editor.DeleteObjectAsync("P0201");

            var error = Assert.Single(dialogs.Errors);
            Assert.Contains("P01 = 1", error);
            Assert.DoesNotContain("或 0", error); // 活动工艺不能为 0,只能改为其他工艺
        }

        [Fact]
        public async Task Delete_FilmUsedByLayer_IsBlocked()
        {
            var device = NewDevice().With("L0102", string.Empty, "Film List").With("L010201", string.Empty, "Film").With("L01020101", "1", "Film ID");
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.DeleteObjectAsync("F01");

            Assert.Contains("L01020101 = 1", Assert.Single(dialogs.Errors));
        }

        [Fact]
        public async Task Delete_LayerUsedByProcess_IsBlocked()
        {
            // 工艺包含膜层:工艺的膜层列表 (P02??07??) 引用膜层编号。
            var device = NewDevice().With("P020107", string.Empty, "Layer List").With("P02010701", "1", "Layer").With("P020107FE", "1", "Number of Layers");
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.DeleteObjectAsync("L01");

            var error = Assert.Single(dialogs.Errors);
            Assert.Contains("工艺 P0201  Process1", error);
            Assert.Contains("P02010701 = 1", error);
            Assert.DoesNotContain("P020107FE", error); // 数量项不算引用
            Assert.DoesNotContain("DL01", device.CommandsSnapshot());
        }

        // ---------- 工艺的膜层:添加 / 删除 ----------

        [Fact]
        public async Task ProcessLayers_AddRowAndRemoveButtons()
        {
            var (editor, _) = await OpenDeviceEditorAsync(ProcessWithLayers("1", "2"));
            var process = editor.Groups.Single(g => g.Key == "P0201");

            Assert.Equal(new[] { "1 - Layer1", "2 - Layer2" }, process.LayerAdd!.Options.Select(o => o.Label));
            Assert.Equal("1", process.LayerAdd.SelectedValue); // 默认第一个膜层
            Assert.True(process.Items.Single(i => i.Id == "P02010701").CanRemove);
            Assert.True(process.Items.Single(i => i.Id == "P02010702").CanRemove);
            Assert.False(process.Items.Single(i => i.Id == "P020101").CanRemove);
            Assert.Null(editor.Groups.Single(g => g.Key == "P01").LayerAdd);
            Assert.False(editor.CanSave); // 添加/删除立即生效,不是待保存的修改
        }

        [Fact]
        public async Task ProcessLayers_Add_AppendsWritesVerifiesAndRereadsProcesses()
        {
            var device = ProcessWithLayers("1");
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);
            var add = editor.Groups.Single(g => g.Key == "P0201").LayerAdd!;
            add.SelectedValue = "2";

            await editor.AppendListEntryAsync(add.ListId, "膜层", add.SelectedValue);

            Assert.Empty(dialogs.Errors);
            Assert.Empty(dialogs.Confirms); // 追加不确认 (可以用"−"删除)
            var commands = device.CommandsSnapshot().SkipWhile(c => c != "AP02010700").ToList();
            Assert.Equal(new[] { "AP02010700", "QP020107FE", "UP02010702:2", "QP02010702", "?P" }, commands);
            Assert.Equal("2", device.Get("P02010702"));
            Assert.Contains("P02010702", editor.ResultText); // 结果显示在窗口底部,不弹窗
            var process = editor.Groups.Single(g => g.Key == "P0201");
            Assert.True(process.IsExpanded);
            Assert.Contains(process.Items, i => i.Id == "P02010702" && i.Value == "2" && i.CanRemove);
            Assert.Contains("新增膜层", editor.CompletionMessage);
        }

        [Fact]
        public async Task ProcessLayers_Add_Rejected_ReportsAndStillRereads()
        {
            var device = ProcessWithLayers("1").Remove("P02010700"); // 设备不认这个增删指令 → -A
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.AppendListEntryAsync("P020107", "膜层", "2");

            Assert.Contains("AP02010700", Assert.Single(dialogs.Errors));
            Assert.Contains("?P", device.CommandsSnapshot());
            Assert.Null(editor.ResultText);
        }

        [Fact]
        public async Task ProcessLayers_RemoveMiddle_ShiftsUpThenRemovesLast()
        {
            var device = ProcessWithLayers("1", "2", "1");
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.RemoveListEntryAsync("P020107", "膜层", 1);

            Assert.Empty(dialogs.Errors);
            Assert.Contains("前移", Assert.Single(dialogs.Confirms));
            var commands = device.CommandsSnapshot().Where(c => c.StartsWith("U", StringComparison.Ordinal) || c.StartsWith("D", StringComparison.Ordinal) || c == "?P").ToList();
            Assert.Equal(new[] { "UP02010701:2", "UP02010702:1", "DP02010700", "?P" }, commands);
            Assert.Equal("2", device.Get("P02010701"));
            Assert.Equal("1", device.Get("P02010702"));
            Assert.Null(device.Get("P02010703"));
            Assert.Equal("2", device.Get("P020107FE"));
            var process = editor.Groups.Single(g => g.Key == "P0201");
            Assert.Equal(new[] { "2", "1" }, process.Items.Where(i => i.Id.StartsWith("P020107", StringComparison.Ordinal)).Select(i => i.Value));
            Assert.Contains("P02010701", editor.ResultText); // 结果显示在窗口底部,不弹窗
        }

        [Fact]
        public async Task ProcessLayers_RemoveLast_NoShift()
        {
            var device = ProcessWithLayers("1", "2");
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.RemoveListEntryAsync("P020107", "膜层", 2);

            Assert.Empty(dialogs.Errors);
            Assert.DoesNotContain(device.CommandsSnapshot(), c => c.StartsWith("U", StringComparison.Ordinal));
            Assert.Contains("DP02010700", device.CommandsSnapshot());
            Assert.Null(device.Get("P02010702"));
            Assert.Equal("1", device.Get("P02010701"));
        }

        // ---------- 关闭确认、勾选统计 ----------

        [Fact]
        public async Task ConfirmClose_OnlyAsksWhenModifiedSettingsAreUnsaved()
        {
            var (editor, dialogs) = await OpenDeviceEditorAsync(NewDevice());
            var name = editor.Groups.SelectMany(g => g.Items).Single(i => i.Id == "M0101");

            name.IsChecked = true; // 只勾选、未改动:不算未保存
            Assert.True(editor.ConfirmClose());
            Assert.Empty(dialogs.Confirms);

            name.Value = "Gold";
            dialogs.ConfirmResult = false;
            Assert.False(editor.ConfirmClose());
            Assert.Contains("1 项已修改但未保存", Assert.Single(dialogs.Confirms));

            dialogs.ConfirmResult = true;
            Assert.True(editor.ConfirmClose());
        }

        [Fact]
        public async Task SelectionStats_FollowCheckAndValueChanges()
        {
            var (editor, _) = await OpenDeviceEditorAsync(NewDevice());
            var density = editor.Groups.SelectMany(g => g.Items).Single(i => i.Id == "M0103");
            Assert.Equal(0, editor.CheckedCount);
            Assert.False(editor.CanSave);

            density.IsChecked = true;
            Assert.Equal(1, editor.CheckedCount);
            Assert.Equal(0, editor.ModifiedCount);
            Assert.True(editor.CanSave);
            Assert.False(editor.CanChangeObjects);

            density.Value = "abc"; // 不合法
            Assert.Equal(1, editor.ModifiedCount);
            Assert.False(editor.CanSave);
            Assert.Contains("1 项的值不合法", editor.SelectionText);

            density.IsChecked = false; // 取消勾选:值还原
            Assert.Equal(0, editor.CheckedCount);
            Assert.True(editor.CanChangeObjects);
        }

        // ---------- 新建/修改后刷新引用它的下拉框 ----------

        [Fact]
        public async Task Create_Layer_AppearsInProcessLayerDropDowns()
        {
            var device = NewLayerDevice()
                .With("P020107", string.Empty, "Layer List")
                .With("P02010700", string.Empty, "Add or Delete Layer from the end of the list")
                .With("P02010701", "1", "Layer")
                .With("P020107FE", "1", "Number of Layers");
            var (editor, _) = await OpenDeviceEditorAsync(device);
            Assert.Equal(new[] { "1 - Layer1" }, editor.Groups.Single(g => g.Key == "P0201").LayerAdd!.Options.Select(o => o.Label));

            await editor.CreateObjectAsync('L');

            var process = editor.Groups.Single(g => g.Key == "P0201");
            Assert.Equal(new[] { "1 - Layer1", "2 - Layer" }, process.LayerAdd!.Options.Select(o => o.Label));
            Assert.Contains("2 - Layer", process.Items.Single(i => i.Id == "P02010701").Options.Select(o => o.Label));
        }

        [Fact]
        public async Task Save_LayerName_RefreshesProcessLayerDropDowns()
        {
            var device = ProcessWithLayers("1", "2");
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);
            var name = editor.Groups.SelectMany(g => g.Items).Single(i => i.Id == "L0201");
            name.IsChecked = true;
            name.Value = "SiO2";

            await editor.SaveAsync();

            Assert.Empty(dialogs.Errors);
            var process = editor.Groups.Single(g => g.Key == "P0201");
            Assert.Contains("2 - SiO2", process.LayerAdd!.Options.Select(o => o.Label));
            Assert.Equal("2 - SiO2", process.Items.Single(i => i.Id == "P02010702").DisplayValue);
            Assert.Equal("膜层 L02  SiO2", editor.Groups.Single(g => g.Key == "L02").Title);
        }

        [Fact]
        public async Task Save_FilmName_RefreshesLayerFilmDropDowns()
        {
            var device = NewDevice()
                .With("L0102", string.Empty, "Film List")
                .With("L010200", string.Empty, "Add or Delete Film from the end of the list")
                .With("L010201", string.Empty, "Film")
                .With("L01020101", "1", "Film")
                .With("L01020102", "5.600", "Rate")
                .With("L0102FE", "1", "Number of Layer Films");
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);
            var name = editor.Groups.SelectMany(g => g.Items).Single(i => i.Id == "F0101");
            name.IsChecked = true;
            name.Value = "F_SiO2";

            await editor.SaveAsync();

            Assert.Empty(dialogs.Errors);
            var film = editor.Groups.Single(g => g.Key == "L01").Items.Single(i => i.Id == "L01020101");
            Assert.Equal("1 - F_SiO2", film.DisplayValue);
            Assert.Contains("1 - F_SiO2", film.Options.Select(o => o.Label));
        }

        // ---------- 膜系的预熔 / 后处理 ----------

        [Fact]
        public async Task FilmConditions_HeaderButtons()
        {
            var (editor, _) = await OpenDeviceEditorAsync(FilmWithConditions());
            var actions = editor.Groups.Single(g => g.Key == "F01").ListActions;

            Assert.Equal(new[] { "F0103", "F0105" }, actions.Select(a => a.ListId));
            Assert.Equal(new[] { "+预熔", "+后处理" }, actions.Select(a => a.AddText));
            Assert.Equal(new[] { "−预熔", "−后处理" }, actions.Select(a => a.RemoveText));
            Assert.True(actions[0].RemoveLastCommand.CanExecute(null));  // 已有 1 条预熔
            Assert.False(actions[1].RemoveLastCommand.CanExecute(null)); // 后处理为空
            Assert.Empty(editor.Groups.Single(g => g.Key == "F01").EmptyLists); // 膜系末尾不显示"添加后处理"行
        }

        [Fact]
        public async Task FilmConditions_Add_AppendsAndRereadsFilms()
        {
            var device = FilmWithConditions();
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);
            var pre = editor.Groups.Single(g => g.Key == "F01").ListActions[0];

            await editor.AppendListEntryAsync(pre.ListId, pre.ListName);

            Assert.Empty(dialogs.Errors);
            var commands = device.CommandsSnapshot().SkipWhile(c => c != "AF010300").ToList();
            Assert.Equal(new[] { "AF010300", "QF0103FE", "?F" }, commands); // 不写值:新的一条使用设备默认值
            var film = editor.Groups.Single(g => g.Key == "F01");
            Assert.True(film.IsExpanded);
            Assert.Contains(film.Items, i => i.Id == "F01030201");
            Assert.Equal(2, film.ListActions[0].Count);
            Assert.Contains("F010302", editor.ResultText); // 结果显示在窗口底部,不弹窗
        }

        [Fact]
        public async Task FilmConditions_RemoveLast_DeletesFromEndAndRereads()
        {
            var device = FilmWithConditions();
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.RemoveListEntryAsync("F0103", "预熔");

            Assert.Empty(dialogs.Errors);
            var commands = device.CommandsSnapshot().SkipWhile(c => c != "DF010300").ToList();
            Assert.Equal(new[] { "DF010300", "QF0103FE", "?F" }, commands);
            Assert.Null(device.Get("F01030101"));
            var film = editor.Groups.Single(g => g.Key == "F01");
            Assert.DoesNotContain(film.Items, i => i.Id.StartsWith("F010301", StringComparison.Ordinal));
            Assert.Equal(0, film.ListActions[0].Count);
            Assert.Contains("删除预熔 (F010301)", editor.ResultText); // 结果显示在窗口底部,不弹窗
        }

        [Fact]
        public async Task FilmConditions_DisabledWhileItemsChecked()
        {
            var (editor, _) = await OpenDeviceEditorAsync(FilmWithConditions());
            var actions = editor.Groups.Single(g => g.Key == "F01").ListActions[0];
            Assert.True(actions.AddCommand.CanExecute(null));

            editor.Groups.SelectMany(g => g.Items).First(i => i.Id == "F0101").IsChecked = true;

            Assert.False(actions.AddCommand.CanExecute(null));
            Assert.False(actions.RemoveLastCommand.CanExecute(null));
        }

        // ---------- 膜层的膜系列表 (膜系、速率、最终厚度) ----------

        [Fact]
        public async Task LayerFilms_NewLayerGetsFilmRateThickness_AndCanRemove()
        {
            var device = NewDevice()
                .With("L0102", string.Empty, "Film List")
                .With("L010200", string.Empty, "Add or Delete Film from the end of the list")
                .With("L0102FE", "0", "Number of Layer Films");
            device.AppendTemplate = id => new[]
            {
                (id, string.Empty, "Film"),
                (id + "01", "1", "Film"),
                (id + "02", "0.000", "Rate"),
                (id + "03", "1.000", "Final Thickness"),
            };
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);
            var films = Assert.Single(editor.Groups.Single(g => g.Key == "L01").ListActions);
            Assert.Equal("L0102", films.ListId);
            Assert.Equal("+膜系", films.AddText);
            var empty = Assert.Single(editor.Groups.Single(g => g.Key == "L01").EmptyLists); // 分组中显示说明和添加按钮
            Assert.Equal("添加速率/厚度设置", empty.AddFirstText);
            Assert.Contains("速率", empty.EmptyHint);

            await editor.AppendListEntryAsync(films.ListId, films.ListName);

            Assert.Empty(dialogs.Errors);
            Assert.Contains("AL010200", device.CommandsSnapshot());
            var layer = editor.Groups.Single(g => g.Key == "L01");
            Assert.Equal(new[] { "L0101", "L01020101", "L01020102", "L01020103" }, layer.Items.Select(i => i.Id));
            Assert.Empty(layer.EmptyLists);
            Assert.Equal(new[] { "膜系 1 · 膜系 ID", "膜系 1 · 速率 (Å/s)", "膜系 1 · 最终厚度 (kÅ)" }, layer.Items.Skip(1).Select(i => i.Label));

            await editor.RemoveListEntryAsync("L0102", films.ListName);

            Assert.Empty(dialogs.Errors);
            Assert.Contains("DL010200", device.CommandsSnapshot());
            Assert.Equal(new[] { "L0101" }, editor.Groups.Single(g => g.Key == "L01").Items.Select(i => i.Id));
        }

        private static FakeDeviceService FilmWithConditions()
        {
            var device = NewDevice()
                .With("F0103", string.Empty, "Pre Condition List")
                .With("F010300", string.Empty, "Add or Delete Pre Condition from the end of the list")
                .With("F010301", string.Empty, "Pre Condition")
                .With("F01030101", "0:00:10", "Start Ramp/Rise time")
                .With("F01030102", "10.00", "Ramp Power")
                .With("F01030103", "0:00:05", "Hold Time")
                .With("F0103FE", "1", "Number of Pre Conditions")
                .With("F0105", string.Empty, "Post Condition List")
                .With("F010500", string.Empty, "Add or Delete Post Condition from the end of the list")
                .With("F0105FE", "0", "Number of Post Conditions");
            device.AppendTemplate = id => new[]
            {
                (id, string.Empty, "Pre Condition"),
                (id + "01", "0:00:00", "Start Ramp/Rise time"),
                (id + "02", "0.00", "Ramp Power"),
                (id + "03", "0:00:00", "Hold Time"),
            };
            return device;
        }

        /// <summary>工艺 P0201 的膜层列表依次为 <paramref name="layers"/>;设备有膜层 L01、L02。</summary>
        private static FakeDeviceService ProcessWithLayers(params string[] layers)
        {
            var device = NewDevice()
                .With("L02", string.Empty, "Layer L02")
                .With("L0201", "Layer2", "Layer Name")
                .With("P020107", string.Empty, "Layer List")
                .With("P02010700", string.Empty, "Add or Delete Layer from the end of the list");
            for (var i = 0; i < layers.Length; i++)
            {
                device.With("P020107" + (i + 1).ToString("X2", System.Globalization.CultureInfo.InvariantCulture), layers[i], "Layer");
            }

            device.With("P020107FE", layers.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), "Number of Layers");
            device.AppendTemplate = id => new[] { (id, "0", "Layer") };
            return device;
        }

        [Fact]
        public void FindReferencesTo_IgnoresZeroAndOtherObjects()
        {
            var entries = new[]
            {
                new RecipeEntry { Id = "F0102", Value = "3" },
                new RecipeEntry { Id = "F0202", Value = "0" },
                new RecipeEntry { Id = "F0302", Value = "1" },
                new RecipeEntry { Id = "P01", Value = "2" },
                new RecipeEntry { Id = "P02010A", Value = "2" },
                new RecipeEntry { Id = "P02010701", Value = "1" },   // 工艺的第 1 个膜层 = L01
                new RecipeEntry { Id = "P0201070A", Value = "0" },   // 未选择
                new RecipeEntry { Id = "P020107FE", Value = "1" },   // 数量,不是引用
            };

            Assert.Equal(new[] { "F0102" }, RecipeRules.FindReferencesTo(entries, "M03").Select(e => e.Id));
            Assert.Equal(new[] { "P01", "P02010A" }, RecipeRules.FindReferencesTo(entries, "P0202").Select(e => e.Id));
            Assert.Equal(new[] { "P02010701" }, RecipeRules.FindReferencesTo(entries, "L01").Select(e => e.Id));
            Assert.Empty(RecipeRules.FindReferencesTo(entries, "L02"));
        }

        [Fact]
        public async Task Layer_CreateAndDelete_ReadOnlyLayers()
        {
            var device = NewLayerDevice();
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);
            Assert.True(editor.Tabs.Single(t => t.Letters == "L").CanCreate);

            await editor.CreateObjectAsync('L');
            await editor.DeleteObjectAsync("L01");

            Assert.Empty(dialogs.Errors);
            var commands = device.CommandsSnapshot();
            Assert.Contains("AL02", commands);
            Assert.Contains("DL01", commands);
            Assert.Equal(2, commands.Count(c => c == "?L"));
            Assert.DoesNotContain("?F", commands);
            Assert.Equal(new[] { "L02" }, editor.Groups.Where(g => g.Key[0] == 'L').Select(g => g.Key));
        }

        [Fact]
        public async Task Save_RenamedObject_UpdatesGroupTitle()
        {
            var device = NewDevice();
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);
            var group = editor.Groups.Single(g => g.Key == "M01");
            Assert.Equal("材料 M01  Al", group.Title);
            var name = group.Items.Single(i => i.Id == "M0101");
            name.IsChecked = true;
            name.Value = "Gold";

            await editor.SaveAsync();

            Assert.Empty(dialogs.Errors);
            Assert.Equal("Gold", device.Get("M0101"));
            group = editor.Groups.Single(g => g.Key == "M01"); // 保存后按新值重新生成页面
            Assert.Equal("材料 M01  Gold", group.Title);
            Assert.StartsWith("材料 M01  Gold", group.Header);
            Assert.Equal("1 - Gold", editor.Groups.Single(g => g.Key == "F01").Items.Single(i => i.Id == "F0102").Options.First(o => o.Value == "1").Label); // 膜系的材料下拉框
        }

        [Fact]
        public async Task Delete_Declined_SendsNothing()
        {
            var device = NewDevice();
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);
            dialogs.ConfirmResult = false;
            var before = device.CommandsSnapshot().Count;

            await editor.DeleteObjectAsync("M01");

            Assert.Equal(before, device.CommandsSnapshot().Count);
            Assert.Contains(editor.Groups, g => g.Key == "M01");
        }

        [Fact]
        public async Task Delete_DeviceDeletesAnotherObject_ReportsMismatch()
        {
            var device = NewDevice();
            device.DeleteInstead["M01"] = "M03";
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);

            await editor.DeleteObjectAsync("M01");

            var error = Assert.Single(dialogs.Errors);
            Assert.Contains("M03", error);
            Assert.Contains("M01", error);
            Assert.Contains(editor.Groups, g => g.Key == "M01"); // 页面按设备实际数据重新生成
            Assert.DoesNotContain(editor.Groups, g => g.Key == "M03");
        }

        [Fact]
        public async Task Delete_DeviceNotReady_SendsNoDelete()
        {
            var device = NewDevice();
            var (editor, dialogs) = await OpenDeviceEditorAsync(device);
            device.Set("C09", "Deposit");

            await editor.DeleteObjectAsync("M01");

            Assert.Contains("Deposit", Assert.Single(dialogs.Errors));
            Assert.DoesNotContain("DM01", device.CommandsSnapshot());
            Assert.Contains(editor.Groups, g => g.Key == "M01");
        }

        [Fact]
        public async Task ObjectCommands_DisabledWhileItemsChecked()
        {
            var (editor, _) = await OpenDeviceEditorAsync(NewDevice());
            var materials = editor.Tabs.Single(t => t.Letters == "M");
            Assert.True(materials.NewCommand!.CanExecute(null));

            editor.Groups.SelectMany(g => g.Items).First(i => i.Id == "M0103").IsChecked = true;

            Assert.False(editor.CanChangeObjects);
            Assert.False(materials.NewCommand.CanExecute(null));
            Assert.False(editor.Groups.Single(g => g.Key == "M01").DeleteCommand!.CanExecute(null));
        }

        [Fact]
        public async Task DeleteAllMaterials_TabStaysForCreating()
        {
            var device = NewDevice().Set("F0102", "0"); // 膜系不再引用 M03
            var (editor, _) = await OpenDeviceEditorAsync(device);

            await editor.DeleteObjectAsync("M01");
            await editor.DeleteObjectAsync("M03");

            var materials = editor.Tabs.Single(t => t.Letters == "M");
            Assert.Empty(materials.Groups);
            Assert.True(materials.CanCreate);
        }

        [Fact]
        public void CommandSet_DeleteByNumber_OnlyForObjects()
        {
            Assert.Equal("DM03", ZevisionCommandSet.Build(CommandActions.Delete, "M03"));
            Assert.Equal("DP0202", ZevisionCommandSet.Build(CommandActions.Delete, "P0202"));
            Assert.Equal("DL03", ZevisionCommandSet.Build(CommandActions.Delete, "L03"));
            Assert.Throws<ArgumentException>(() => ZevisionCommandSet.Build(CommandActions.Delete, "M0101")); // 设置项不能删除
        }

        [Fact]
        public async Task ReadGroup_WithoutCountRow_Fails()
        {
            // 组帮助里没有数量项 (MFE) 说明没解析出来,不能当作材料列表为空。
            var device = new FakeDeviceService().With("M0101", "Al", "Name");

            var result = await new InstrumentConfigService(device).ReadGroupAsync('M', CancellationToken.None);

            Assert.False(result.IsSuccess);
        }

        // ---------- 保存的配置 (文件模式):同时修改设备和文件 ----------

        [Fact]
        public async Task File_Create_UsesNumberFreeOnDeviceAndInFile_AndSavesFile()
        {
            // 文件有 M01、M03;设备另有 M02 (文件中没有):新编号要避开两边 → M04。
            var device = NewDevice().With("M02", string.Empty, "Material M02").With("M0201", "Cu", "Name");
            device.Templates['M'] = id => new[] { (id, string.Empty, "Material " + id), (id + "01", "Material", "Name") };
            using (var file = await FileEditor.OpenAsync(NewDevice(), device))
            {
                await file.Editor.CreateObjectAsync('M');

                Assert.Empty(file.Dialogs.Errors);
                Assert.Contains("AM04", device.CommandsSnapshot());
                var saved = RecipeFile.Load(file.Path);
                Assert.True(saved.IsSuccess, saved.Error); // MD5 有效
                Assert.Contains(saved.Value.Entries, e => e.Id == "M0401" && e.Value == "Material");
                Assert.DoesNotContain(saved.Value.Entries, e => e.Id.StartsWith("M02", StringComparison.Ordinal)); // 设备上多出的对象不进文件
                Assert.Contains(file.Editor.Groups, g => g.Key == "M04");
                Assert.Contains("配置文件已保存", file.Editor.ResultText);
            }
        }

        [Fact]
        public async Task File_Create_SaveFails_FileAndPageStayAsBefore()
        {
            // 设备已新建,但文件被占用无法保存:页面仍与文件一致 (先改副本、保存成功才改页面)。
            var device = NewDevice();
            device.Templates['M'] = id => new[] { (id, string.Empty, "Material " + id), (id + "01", "Material", "Name") };
            using (var file = await FileEditor.OpenAsync(NewDevice(), device))
            {
                var before = System.IO.File.ReadAllText(file.Path);
                using (new System.IO.FileStream(file.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
                {
                    await file.Editor.CreateObjectAsync('M');
                }

                Assert.Contains("AM02", device.CommandsSnapshot());
                Assert.Contains("保存文件失败", Assert.Single(file.Dialogs.Errors));
                Assert.Equal(before, System.IO.File.ReadAllText(file.Path));
                Assert.DoesNotContain(file.Editor.Groups, g => g.Key == "M02");
                Assert.False(file.Editor.IsBusy);
            }
        }

        [Fact]
        public async Task File_Delete_OnDevice_DeletesBothAndSavesFile()
        {
            var device = NewDevice();
            using (var file = await FileEditor.OpenAsync(NewDevice(), device))
            {
                await file.Editor.DeleteObjectAsync("M01");

                Assert.Empty(file.Dialogs.Errors);
                Assert.Contains("DM01", device.CommandsSnapshot());
                Assert.Null(device.Get("M0101"));
                Assert.DoesNotContain(RecipeFile.Load(file.Path).Value.Entries, e => e.Id == "M0101");
                Assert.DoesNotContain(file.Editor.Groups, g => g.Key == "M01");
            }
        }

        [Fact]
        public async Task File_Delete_NotOnDevice_RemovesFromFileOnly()
        {
            var device = NewDevice().Remove("M01");
            using (var file = await FileEditor.OpenAsync(NewDevice(), device))
            {
                await file.Editor.DeleteObjectAsync("M01");

                Assert.Empty(file.Dialogs.Errors);
                Assert.DoesNotContain("DM01", device.CommandsSnapshot());
                Assert.DoesNotContain(RecipeFile.Load(file.Path).Value.Entries, e => e.Id == "M0101");
                Assert.Contains("设备上没有该对象", file.Editor.ResultText);
            }
        }

        [Fact]
        public async Task File_ProcessLayer_Add_SavesFile()
        {
            var device = ProcessWithLayers("1");
            using (var file = await FileEditor.OpenAsync(ProcessWithLayers("1"), device))
            {
                await file.Editor.AppendListEntryAsync("P020107", "膜层", "2");

                Assert.Empty(file.Dialogs.Errors);
                Assert.Equal("2", device.Get("P02010702"));
                Assert.Contains(RecipeFile.Load(file.Path).Value.Entries, e => e.Id == "P02010702" && e.Value == "2");
                Assert.Contains("配置文件已保存", file.Editor.ResultText);
            }
        }

        [Fact]
        public async Task File_ProcessLayer_CountMismatch_Refused()
        {
            // 设备上该工艺有 2 个膜层,文件中只有 1 个:新条目编号对不上,不追加。
            var device = ProcessWithLayers("1", "1");
            using (var file = await FileEditor.OpenAsync(ProcessWithLayers("1"), device))
            {
                await file.Editor.AppendListEntryAsync("P020107", "膜层", "2");

                Assert.Contains("设备 2 条,文件 1 条", Assert.Single(file.Dialogs.Errors));
                Assert.DoesNotContain("AP02010700", device.CommandsSnapshot());
            }
        }

        [Fact]
        public async Task File_ProcessLayer_RemoveMiddle_UpdatesFile()
        {
            var device = ProcessWithLayers("1", "2");
            using (var file = await FileEditor.OpenAsync(ProcessWithLayers("1", "2"), device))
            {
                await file.Editor.RemoveListEntryAsync("P020107", "膜层", 1);

                Assert.Empty(file.Dialogs.Errors);
                var saved = RecipeFile.Load(file.Path).Value.Entries;
                Assert.Contains(saved, e => e.Id == "P02010701" && e.Value == "2");
                Assert.DoesNotContain(saved, e => e.Id == "P02010702");
                Assert.Equal("2", device.Get("P02010701"));
                Assert.Null(device.Get("P02010702"));
            }
        }

        [Fact]
        public async Task File_RemoveCondition_RemovesStepFromFile()
        {
            var device = FilmWithConditions();
            using (var file = await FileEditor.OpenAsync(FilmWithConditions(), device))
            {
                await file.Editor.RemoveListEntryAsync("F0103", "预熔");

                Assert.Empty(file.Dialogs.Errors);
                Assert.Contains("DF010300", device.CommandsSnapshot());
                Assert.DoesNotContain(RecipeFile.Load(file.Path).Value.Entries, e => e.Id.StartsWith("F010301", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task File_AddCondition_AddsStepToFile()
        {
            var device = FilmWithConditions();
            using (var file = await FileEditor.OpenAsync(FilmWithConditions(), device))
            {
                var pre = file.Editor.Groups.Single(g => g.Key == "F01").ListActions[0];

                await file.Editor.AppendListEntryAsync(pre.ListId, pre.ListName);

                Assert.Empty(file.Dialogs.Errors);
                Assert.Contains("AF010300", device.CommandsSnapshot());
                var saved = RecipeFile.Load(file.Path).Value.Entries;
                Assert.Contains(saved, e => e.Id == "F01030201");
                Assert.Contains(saved, e => e.Id == "F01030101"); // 原有的一条保留
                Assert.Contains(file.Editor.Groups.Single(g => g.Key == "F01").Items, i => i.Id == "F01030203");
            }
        }

        /// <summary>保存的配置模式的编辑器:文件取自 <c>fileSource</c> 设备的数据,操作在 <c>device</c> 上执行。</summary>
        private sealed class FileEditor : IDisposable
        {
            private FileEditor(RecipeEditorViewModel editor, FakeDialogService dialogs, string path)
            {
                Editor = editor;
                Dialogs = dialogs;
                Path = path;
            }

            public RecipeEditorViewModel Editor { get; }

            public FakeDialogService Dialogs { get; }

            public string Path { get; }

            public static async Task<FileEditor> OpenAsync(FakeDeviceService fileSource, FakeDeviceService device)
            {
                var snapshot = await new InstrumentConfigService(fileSource).ReadSnapshotAsync(CancellationToken.None);
                var config = new InstrumentConfigService(device);
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zevision_obj_" + Guid.NewGuid().ToString("N") + ".json");
                Assert.True(RecipeFile.Save(snapshot.Value.ToRecipe(DateTimeOffset.Now), path).IsSuccess);
                var dialogs = new FakeDialogService();
                var editor = new RecipeEditorViewModel(RecipeEditSession.Open(RecipeFile.Load(path).Value, path, config, new RecipeFileStore()), dialogs, new FakeErrorLog());
                return new FileEditor(editor, dialogs, path);
            }

            public void Dispose() => System.IO.File.Delete(Path);
        }

        /// <summary>和实机一样:新建的膜层带空的膜系列表 (<c>L??02FE = 0</c>),追加的一项有膜系、速率、最终厚度。</summary>
        private static FakeDeviceService NewLayerDevice()
        {
            var device = NewDevice();
            device.Templates['L'] = id => new[]
            {
                (id, string.Empty, "Layer " + id),
                (id + "01", "Layer", "Layer Name"),
                (id + "02", string.Empty, "Film List"),
                (id + "0200", string.Empty, "Add or Delete Film from the end of the list"),
                (id + "02FE", "0", "Number of Layer Films"),
            };
            device.AppendTemplate = id => new[]
            {
                (id, string.Empty, "Film"),
                (id + "01", "0", "Film"),
                (id + "02", "0.000", "Rate"),
                (id + "03", "1.000", "Final Thickness"),
            };
            return device;
        }

        /// <summary>新建的工艺带空的膜层列表 (<c>P02??07FE = 0</c>),追加的一项是膜层编号。</summary>
        private static FakeDeviceService NewProcessDevice()
        {
            var device = NewDevice();
            device.Templates['P'] = id => new[]
            {
                (id, string.Empty, "Process " + id),
                (id + "01", "Process", "Name"),
                (id + "07", string.Empty, "Layer List"),
                (id + "0700", string.Empty, "Add or Delete Layer from the end of the list"),
                (id + "07FE", "0", "Number of Process Layers"),
            };
            device.AppendTemplate = id => new[] { (id, "0", "Layer") };
            return device;
        }

        private static FakeDeviceService NewDevice() => new FakeDeviceService()
            .With("C09", "Ready", "Current State")
            .With("G03", "IMC300", "Device Name")
            .With("M00", string.Empty, "Add or Delete material from the end of the list")
            .With("M01", string.Empty, "Material M01")
            .With("M0101", "Al", "Name")
            .With("M0103", "2.700", "Density (g/cm^3)")
            .With("M03", string.Empty, "Material M03")
            .With("M0301", "Au", "Name")
            .With("MFE", "2", "Number of Materials")
            .With("F01", string.Empty, "Film F01")
            .With("F0101", "Film1", "Name")
            .With("F0102", "3", "Material ID")
            .With("FFE", "1", "Number of Films")
            .With("L01", string.Empty, "Layer L01")
            .With("L0101", "Layer1", "Layer Name")
            .With("LFE", "1", "Number of Layers")
            .With("P01", "1", "Set Active Process")
            .With("P0201", string.Empty, "Process P0201")
            .With("P020101", "Process1", "Name")
            .With("P02FE", "1", "Number of Processes");

        /// <summary>与实际流程相同:读取 ? 生成实机数据的配置,以实机数据模式打开编辑器。</summary>
        private static async Task<(RecipeEditorViewModel Editor, FakeDialogService Dialogs)> OpenDeviceEditorAsync(FakeDeviceService device)
        {
            var config = new InstrumentConfigService(device);
            var snapshot = await config.ReadSnapshotAsync(CancellationToken.None);
            Assert.True(snapshot.IsSuccess, snapshot.Error);
            var dialogs = new FakeDialogService();
            var editor = new RecipeEditorViewModel(RecipeEditSession.Open(snapshot.Value.ToRecipe(DateTimeOffset.Now), null, config, new RecipeFileStore()), dialogs, new FakeErrorLog());
            return (editor, dialogs);
        }

        private static int TabIndex(RecipeEditorViewModel editor, string title) => editor.Tabs.ToList().FindIndex(t => t.Title == title);
    }
}
