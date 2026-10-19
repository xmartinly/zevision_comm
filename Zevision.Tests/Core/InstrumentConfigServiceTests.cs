using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Core.Configuration;

namespace Zevision.Tests
{
    public class InstrumentConfigServiceTests
    {
        private static FakeDeviceService NewDevice() => new FakeDeviceService()
            .With("C", string.Empty, "Process Control Info")
            .With("C01", "0", "Stop/Start Process")
            .With("C09", "Ready", "Current State")
            .With("F01", string.Empty, "Film F01")
            .With("F0101", "L1", "Name")
            .With("F0102", "1", "Material")
            .With("F0103FE", "2", "Number of Pre Conditions")
            .With("G03", "IMC300", "Device Name")
            .With("G05", "IMC", "Model Number")
            .With("G06", "XXXXXXXX", "System Serial Number")
            .With("G08", "1.8.11.202606101229", "System Software Version")
            .With("G1303", "115200", "RS232 Baud Rate (bps)")
            .With("M01", string.Empty, "Material M01")
            .With("M0101", "M1", "Name")
            .With("M0103", "5.890", "Density (g/cm^3)");

        [Fact]
        public async Task ReadSnapshot_ParsesHelpTable()
        {
            var service = new InstrumentConfigService(NewDevice());

            var snapshot = await service.ReadSnapshotAsync(CancellationToken.None);

            Assert.True(snapshot.IsSuccess, snapshot.Error);
            Assert.Equal("IMC300", snapshot.Value.DeviceName);
            Assert.Equal("1.8.11.202606101229", snapshot.Value.FirmwareVersion);
            Assert.Equal("Ready", snapshot.Value.CurrentState);
        }

        [Fact]
        public async Task HelpReads_AreHidden_OtherCommandsShown()
        {
            // 帮助指令只在指令输入框中发送时显示;程序内部读取 (保存、恢复、修改配置) 时不显示、不记录。
            var device = NewDevice().With("MFE", "1", "Number of Materials");
            var service = new InstrumentConfigService(device);

            Assert.True((await service.ReadSnapshotAsync(CancellationToken.None)).IsSuccess);
            Assert.True((await service.ReadGroupAsync('M', CancellationToken.None)).IsSuccess);
            Assert.True((await service.QueryAsync(new[] { "G03" }, CancellationToken.None)).IsSuccess);

            Assert.Equal(new[] { "?", "?M" }, device.HiddenFrames);
            Assert.Equal(new[] { "?", "?M", "QG03" }, device.Frames);
        }

        [Fact]
        public async Task ToRecipe_KeepsOnlyRestorableItems()
        {
            var service = new InstrumentConfigService(NewDevice());
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;

            var recipe = snapshot.ToRecipe(new DateTimeOffset(2026, 10, 15, 16, 30, 0, TimeSpan.FromHours(8)));

            Assert.Equal(new[] { "F0101", "F0102", "M0101", "M0103" }, recipe.Entries.Select(e => e.Id));
            Assert.Equal("IMC300", recipe.DeviceName);
            Assert.Equal("XXXXXXXX", recipe.SerialNumber);
            Assert.Equal("2026-10-15T16:30:00.0000000+08:00", recipe.SavedAt);
        }

        [Fact]
        public async Task Plan_OnlyWritesChangedItems_MaterialsFirst()
        {
            var device = NewDevice();
            var service = new InstrumentConfigService(device);
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var recipe = snapshot.ToRecipe(DateTimeOffset.Now);
            Set(recipe, "F0101", "L9");
            Set(recipe, "M0103", "2.700");

            var plan = RestorePlanner.Plan(recipe, snapshot);

            Assert.True(plan.IsSuccess, plan.Error);
            Assert.Equal(new[] { "M0103", "F0101" }, plan.Value.Writes.Select(w => w.Id));
            Assert.Equal("5.890", plan.Value.Writes[0].CurrentValue);
            Assert.Equal(2, plan.Value.UnchangedCount);
            Assert.Empty(plan.Value.Warnings);
        }

        [Fact]
        public async Task Plan_SkipsMissingExcludedAndUnsendableItems()
        {
            var service = new InstrumentConfigService(NewDevice());
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var recipe = snapshot.ToRecipe(DateTimeOffset.Now);
            recipe.Entries.Add(new RecipeEntry { Id = "F0201", Value = "L2" });       // 设备上没有膜层 F02:应创建,而不是跳过
            recipe.Entries.Add(new RecipeEntry { Id = "G1303", Value = "9600" });     // 通讯参数,手工加入文件
            recipe.Entries.Add(new RecipeEntry { Id = "M0101", Value = "Dup" });      // 重复
            Set(recipe, "F0101", "a;b");                                              // 协议不支持的字符

            var plan = RestorePlanner.Plan(recipe, snapshot).Value;

            Assert.Empty(plan.Writes);
            Assert.Equal(new[] { "M0101", "F0101", "G1303" }.OrderBy(x => x), plan.Skipped.Select(s => s.Id).OrderBy(x => x));
            Assert.Equal("F02", Assert.Single(plan.Creates).Id);
        }

        [Fact]
        public async Task Plan_SkippedItem_CarriesValueAndDescription()
        {
            var service = new InstrumentConfigService(NewDevice());
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var recipe = snapshot.ToRecipe(DateTimeOffset.Now);
            recipe.Entries.Add(new RecipeEntry { Id = "N0101", Value = "Sensor", Description = "Name" }); // 传感器不能自动创建

            var skip = Assert.Single(RestorePlanner.Plan(recipe, snapshot).Value.Skipped);

            Assert.Equal("Sensor", skip.Value);
            Assert.Equal("Name", skip.Description);
            Assert.StartsWith("N0101 Name = Sensor  (", skip.ToString());
            Assert.Contains("N01", skip.Reason);
        }

        [Fact]
        public async Task Plan_DifferentDevice_Fails()
        {
            var service = new InstrumentConfigService(NewDevice());
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var recipe = snapshot.ToRecipe(DateTimeOffset.Now);
            recipe.DeviceName = "IMC200";

            var plan = RestorePlanner.Plan(recipe, snapshot);

            Assert.False(plan.IsSuccess);
            Assert.Contains("型号不符", plan.Error);
        }

        [Fact]
        public async Task Plan_DeviceNotReady_Fails()
        {
            var device = new FakeDeviceService()
                .With("C09", "Deposit")
                .With("G03", "IMC300")
                .With("M0101", "M1");
            var service = new InstrumentConfigService(device);
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var recipe = snapshot.ToRecipe(DateTimeOffset.Now);

            var plan = RestorePlanner.Plan(recipe, snapshot);

            Assert.False(plan.IsSuccess);
            Assert.Contains("Deposit", plan.Error);
        }

        [Fact]
        public async Task Plan_DifferentFirmware_Warns()
        {
            var service = new InstrumentConfigService(NewDevice());
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var recipe = snapshot.ToRecipe(DateTimeOffset.Now);
            recipe.FirmwareVersion = "1.7.0";

            var plan = RestorePlanner.Plan(recipe, snapshot);

            Assert.True(plan.IsSuccess);
            Assert.Contains("固件版本不同", Assert.Single(plan.Value.Warnings));
        }

        [Fact]
        public async Task ExecuteRestore_WritesAndVerifies()
        {
            var device = NewDevice();
            var (service, recipe, plan) = await PlanWithChangesAsync(device, ("F0101", "L9"), ("M0103", "2.700"));
            var reports = new System.Collections.Generic.List<RestoreProgress>();

            var report = await service.ExecuteRestoreAsync(recipe, plan, new SyncProgress(reports), CancellationToken.None);

            Assert.True(report.IsSuccess);
            Assert.Equal(2, report.WrittenCount);
            Assert.Equal("L9", device.Get("F0101"));
            Assert.Equal("2.700", device.Get("M0103"));
            Assert.Contains("UM0103:2.700", device.Commands);
            Assert.Contains("QF0101", device.Commands);
            Assert.Equal(new[] { "写入", "写入", "校验", "校验" }, reports.Select(r => r.Stage));
        }

        [Fact]
        public async Task ExecuteRestore_DeviceRejects_StopsImmediately()
        {
            var device = NewDevice();
            var (service, recipe, plan) = await PlanWithChangesAsync(device, ("F0101", "L9"), ("M0103", "2.700"));
            device.RejectWrites.Add("M0103");

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.False(report.IsSuccess);
            Assert.Equal("M0103", report.FailedId);
            Assert.Contains("非法参数值", report.Error);
            Assert.Equal(0, report.WrittenCount);
            Assert.Equal("L1", device.Get("F0101")); // 后面的项没有被写入
        }

        [Fact]
        public async Task ExecuteRestore_StateError_ReportsCurrentState()
        {
            var device = NewDevice();
            var (service, recipe, plan) = await PlanWithChangesAsync(device, ("M0103", "2.700"));
            device.StateErrorWrites.Add("M0103");

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            // F:不自动切换设备状态,只读出 C09 一并报告。
            Assert.Equal("M0103", report.FailedId);
            Assert.EndsWith("设备当前状态 (C09): Ready。", report.Error);
            Assert.Equal("QC09", device.Commands.Last());
        }

        [Fact]
        public async Task ExecuteRestore_ValueNotApplied_ReportsVerifyMismatch()
        {
            var device = NewDevice();
            var (service, recipe, plan) = await PlanWithChangesAsync(device, ("F0101", "L9"));
            device.IgnoreWrites.Add("F0101");

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.False(report.IsSuccess);
            Assert.Contains("F0101", Assert.Single(report.VerifyMismatches));
        }

        [Fact]
        public async Task ExecuteRestore_Cancelled_StopsAndReports()
        {
            var device = NewDevice();
            var (service, recipe, plan) = await PlanWithChangesAsync(device, ("F0101", "L9"), ("M0103", "2.700"));
            using (var cancellation = new CancellationTokenSource())
            {
                device.BeforeWrite = n =>
                {
                    if (n == 2)
                    {
                        cancellation.Cancel();
                    }
                };

                var report = await service.ExecuteRestoreAsync(recipe, plan, null, cancellation.Token);

                Assert.True(report.Cancelled);
                Assert.Equal(1, report.WrittenCount);
            }
        }

        /// <summary>
        /// 复现实机问题:保存配置后在仪器上删除了材料 M03,膜层 F03 的材料随之变为 0。
        /// 旧版本只发送 UF0302:3,设备因 M03 不存在返回 B。
        /// </summary>
        private static FakeDeviceService NewDeviceWithMaterial3()
        {
            var device = NewDevice()
                .With("M03", string.Empty, "Material M03")
                .With("M0301", "M3", "Name")
                .With("M0303", "4.090", "Density (g/cm^3)")
                .With("F03", string.Empty, "Film F03")
                .With("F0301", "L3", "Name")
                .With("F0302", "3", "Material");
            device.Templates['M'] = id => new[]
            {
                (id, string.Empty, "Material " + id),
                (id + "01", "Material", "Name"),
                (id + "03", "1.000", "Density (g/cm^3)"),
            };
            return device;
        }

        [Fact]
        public async Task Plan_DeletedMaterial_IsCreatedAndReferenceKept()
        {
            var device = NewDeviceWithMaterial3();
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            device.Remove("M03").Set("F0302", "0");

            var plan = RestorePlanner.Plan(recipe, (await service.ReadSnapshotAsync(CancellationToken.None)).Value).Value;

            var create = Assert.Single(plan.Creates);
            Assert.Equal("M03", create.Id);
            Assert.Equal("AM03", create.Command);
            Assert.Equal("F0302", Assert.Single(plan.Writes).Id);
            Assert.DoesNotContain(plan.Skipped, s => s.Id.StartsWith("M03", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ExecuteRestore_DeletedMaterial_CreatesThenWritesAndVerifies()
        {
            var device = NewDeviceWithMaterial3();
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            device.Remove("M03").Set("F0302", "0");
            var plan = RestorePlanner.Plan(recipe, (await service.ReadSnapshotAsync(CancellationToken.None)).Value).Value;

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.True(report.IsSuccess, report.Error);
            Assert.Equal(1, report.CreatedCount);
            Assert.Equal("3", device.Get("F0302"));
            Assert.Equal("M3", device.Get("M0301"));
            Assert.Equal("4.090", device.Get("M0303"));

            // 顺序:先创建材料,再写材料的设置,最后写引用它的膜层。
            var writes = device.Commands.Where(c => c.StartsWith("A", StringComparison.Ordinal) || c.StartsWith("U", StringComparison.Ordinal)).ToList();
            Assert.Equal(new[] { "AM03", "UM0301:M3", "UM0303:4.090", "UF0302:3" }, writes);
        }

        [Fact]
        public async Task Plan_ReferenceToObjectMissingEverywhere_IsSkippedNotSent()
        {
            var device = NewDeviceWithMaterial3();
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            recipe.Entries.RemoveAll(e => e.Id.StartsWith("M03", StringComparison.Ordinal));
            device.Remove("M03").Set("F0302", "0");

            var plan = RestorePlanner.Plan(recipe, (await service.ReadSnapshotAsync(CancellationToken.None)).Value).Value;

            Assert.Empty(plan.Creates);
            Assert.Empty(plan.Writes);
            Assert.Contains("M03", Assert.Single(plan.Skipped, s => s.Id == "F0302").Reason);
        }

        [Fact]
        public async Task ExecuteRestore_CreateRejected_StopsBeforeWriting()
        {
            var device = NewDeviceWithMaterial3();
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            device.Remove("M03").Set("F0302", "0");
            device.RejectCreates.Add("M03");
            var plan = RestorePlanner.Plan(recipe, (await service.ReadSnapshotAsync(CancellationToken.None)).Value).Value;

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.False(report.IsSuccess);
            Assert.Equal("M03", report.FailedId);
            Assert.Contains("创建材料 M03 (AM03) 失败", report.Error);
            Assert.DoesNotContain(device.Commands, c => c.StartsWith("U", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ExecuteRestore_CreatedObjectAppearsUnderOtherId_StopsBeforeWriting()
        {
            var device = NewDeviceWithMaterial3();
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            device.Remove("M03").Set("F0302", "0");
            device.Templates['M'] = _ => new[] { ("M0B", string.Empty, "Material M0B"), ("M0B01", "Material", "Name") };
            var plan = RestorePlanner.Plan(recipe, (await service.ReadSnapshotAsync(CancellationToken.None)).Value).Value;

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.False(report.IsSuccess);
            Assert.Contains("仍没有材料 M03", report.Error);
            Assert.DoesNotContain(device.Commands, c => c.StartsWith("U", StringComparison.Ordinal));
        }

        /// <summary>
        /// 复现实机第二个问题:AF01 新建的膜层预熔步骤列表为空 (F0103FE = 0),
        /// 文件中的 2 个预熔步骤无处写入。设备帮助表给出 F010300 = "Add or Delete Pre Condition from the end of the list"。
        /// </summary>
        private static FakeDeviceService NewDeviceWithFilmSteps()
        {
            var device = NewDevice()
                .With("F0103", string.Empty, "Pre Condition List")
                .With("F010300", string.Empty, "Add or Delete Pre Condition from the end of the list")
                .With("F010301", string.Empty, "Pre Condition Step")
                .With("F01030101", "0:59:59", "Start Ramp/Rise time")
                .With("F01030102", "8.00", "Ramp Power")
                .With("F010302", string.Empty, "Pre Condition Step")
                .With("F01030201", "0:00:36", "Start Ramp/Rise time")
                .With("F01030202", "69.15", "Ramp Power")
                .With("F0103FE", "2", "Number of Pre Conditions");
            device.Templates['F'] = id => new[]
            {
                (id, string.Empty, "Film " + id),
                (id + "01", "Film", "Name"),
                (id + "02", "0", "Material"),
                (id + "03", string.Empty, "Pre Condition List"),
                (id + "0300", string.Empty, "Add or Delete Pre Condition from the end of the list"),
                (id + "03FE", "0", "Number of Pre Conditions"),
            };
            device.AppendTemplate = item => new[]
            {
                (item, string.Empty, "Pre Condition Step"),
                (item + "01", "0:00:00", "Start Ramp/Rise time"),
                (item + "02", "0.00", "Ramp Power"),
            };
            return device;
        }

        [Fact]
        public async Task Plan_ExistingListShorterThanFile_AppendsMissingSteps()
        {
            var device = NewDeviceWithFilmSteps();
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            device.Remove("F010302").Set("F0103FE", "1");

            var plan = RestorePlanner.Plan(recipe, (await service.ReadSnapshotAsync(CancellationToken.None)).Value).Value;

            var create = Assert.Single(plan.Creates);
            Assert.Equal("F010302", create.Id);
            Assert.Equal("AF010300", create.Command);
            Assert.Equal("Pre Condition List 条目", create.Kind);
            Assert.Empty(plan.Skipped);
        }

        [Fact]
        public async Task ExecuteRestore_DeletedFilm_CreatesFilmThenStepsThenWrites()
        {
            var device = NewDeviceWithFilmSteps();
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            device.Remove("F01");
            var plan = RestorePlanner.Plan(recipe, (await service.ReadSnapshotAsync(CancellationToken.None)).Value).Value;

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.True(report.IsSuccess, report.Error);
            Assert.Empty(report.AdditionalSkipped);
            Assert.Equal(3, report.CreatedCount);
            Assert.Equal(new[] { "AF01", "AF010300", "AF010300" }, device.Commands.Where(c => c.StartsWith("A", StringComparison.Ordinal)));
            Assert.Equal(3, device.Commands.Count(c => c == "?") - 1); // 初始计划 1 次 + 每轮创建后 1 次
            Assert.Equal("L1", device.Get("F0101"));
            Assert.Equal("69.15", device.Get("F01030202"));
            Assert.Equal("2", device.Get("F0103FE"));
        }

        [Fact]
        public async Task Plan_NonContiguousList_SkipsWithReason()
        {
            var device = NewDeviceWithFilmSteps();
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            device.Remove("F010301"); // 计数仍为 2,但只剩 02:追加的新条目会是 03,对不上 01

            var plan = RestorePlanner.Plan(recipe, (await service.ReadSnapshotAsync(CancellationToken.None)).Value).Value;

            Assert.Empty(plan.Creates);
            Assert.Contains("不连续", plan.Skipped.First(s => s.Id == "F01030101").Reason);
        }

        [Fact]
        public async Task ExecuteRestore_AppendHasNoEffect_StopsBeforeWriting()
        {
            var device = NewDeviceWithFilmSteps();
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            device.Remove("F010302").Set("F0103FE", "1");
            device.AppendTemplate = null;
            var plan = RestorePlanner.Plan(recipe, (await service.ReadSnapshotAsync(CancellationToken.None)).Value).Value;

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.False(report.IsSuccess);
            Assert.Contains("F010302", report.Error);
            Assert.DoesNotContain(device.Commands, c => c.StartsWith("U", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ExecuteRestore_Order_SystemThenCreateThenMaterialFilm()
        {
            // 系统设置 (传感器、源) 先写;然后创建已删除的材料;再按 材料 → 膜层 写对象设置。
            var device = NewDeviceWithMaterial3()
                .With("N01", string.Empty, "Sensor N01")
                .With("N0101", "Sensor", "Name")
                .With("S01", string.Empty, "Source S01")
                .With("S010A", "10.00", "Max Output");
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            device.Remove("M03").Set("F0302", "0").Set("F0301", "Changed").Set("N0101", "Old").Set("S010A", "5.00");
            var plan = RestorePlanner.Plan(recipe, (await service.ReadSnapshotAsync(CancellationToken.None)).Value).Value;

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.True(report.IsSuccess, report.Error);
            var sequence = device.Commands.Where(c => c[0] == 'A' || c[0] == 'U' || c == "?").Skip(2).ToList(); // 去掉计划前的两次读取
            Assert.Equal(
                new[] { "UN0101:Sensor", "US010A:10.00", "AM03", "?", "UM0301:M3", "UM0303:4.090", "UF0301:L3", "UF0302:3" },
                sequence);
            Assert.Equal(6, report.WrittenCount);
            Assert.Equal(6, report.PlannedWriteCount);
        }

        [Fact]
        public async Task ExecuteRestore_SystemWriteRejected_StopsBeforeCreating()
        {
            var device = NewDeviceWithMaterial3()
                .With("N01", string.Empty, "Sensor N01")
                .With("N0101", "Sensor", "Name");
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            device.Remove("M03").Set("N0101", "Old");
            device.RejectWrites.Add("N0101");
            var plan = RestorePlanner.Plan(recipe, (await service.ReadSnapshotAsync(CancellationToken.None)).Value).Value;

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.Equal("N0101", report.FailedId);
            Assert.False(report.FailedDuringCreate);
            Assert.DoesNotContain("AM03", device.Commands);
        }

        /// <summary>
        /// 复现实机问题:工艺的层列表 P020107 为空,文件中的 P02010701 (Layer = 1) 本身就是列表条目,
        /// 旧版只检查祖先,判定为"设备上不存在该项"而跳过。
        /// </summary>
        private static FakeDeviceService NewDeviceWithEmptyProcessLayerList()
        {
            var device = NewDevice()
                .With("P02", string.Empty, "Process List")
                .With("P0201", string.Empty, "Process")
                .With("P020101", "P1", "Name")
                .With("P020107", string.Empty, "Layer List")
                .With("P02010700", string.Empty, "Add or Delete Layer from the end of the list")
                .With("P020107FE", "0", "Number Of Layers");
            device.AppendTemplate = item => new[] { (item, "0", "Layer") };
            return device;
        }

        [Fact]
        public async Task Plan_LeafListItemMissing_AppendsToList()
        {
            var device = NewDeviceWithEmptyProcessLayerList();
            var service = new InstrumentConfigService(device);
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var recipe = snapshot.ToRecipe(DateTimeOffset.Now);
            recipe.Entries.Add(new RecipeEntry { Id = "P02010701", Value = "1", Description = "Layer" });
            recipe.Entries.Add(new RecipeEntry { Id = "P02010702", Value = "2", Description = "Layer" });

            var plan = RestorePlanner.Plan(recipe, snapshot).Value;

            Assert.Equal(new[] { "AP02010700", "AP02010700" }, plan.Creates.Select(c => c.Command));
            Assert.Equal(new[] { "P02010701", "P02010702" }, plan.Creates.Select(c => c.Id));
            Assert.Empty(plan.Skipped);
        }

        [Fact]
        public async Task ExecuteRestore_LeafListItemMissing_AppendsThenWrites()
        {
            var device = NewDeviceWithEmptyProcessLayerList();
            var service = new InstrumentConfigService(device);
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var recipe = snapshot.ToRecipe(DateTimeOffset.Now);
            recipe.Entries.Add(new RecipeEntry { Id = "P02010701", Value = "1", Description = "Layer" });
            var plan = RestorePlanner.Plan(recipe, snapshot).Value;

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.True(report.IsSuccess, report.Error);
            Assert.Empty(report.AdditionalSkipped);
            Assert.Equal("1", device.Get("P02010701"));
            Assert.Equal("1", device.Get("P020107FE"));
        }

        [Fact]
        public async Task ExecuteRestore_DeviceStartedWhileConfirming_WritesNothing()
        {
            // 计划在确认框之前做,确认期间设备开始了工艺:写入前再次检查,不写入运行中的设备。
            var device = NewDevice();
            var (service, recipe, plan) = await PlanWithChangesAsync(device, ("M0103", "2.700"));
            device.Set("C09", "Running");

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.False(report.IsSuccess);
            Assert.Contains("Ready", report.Error);
            Assert.DoesNotContain(device.CommandsSnapshot(), c => c.StartsWith("U", StringComparison.Ordinal));
            Assert.Equal("5.890", device.Get("M0103"));
        }

        [Fact]
        public async Task ExecuteRestore_SystemListItemsCreated_AreWritten()
        {
            // 输出源的坩埚材料 (S**09**) 是系统组中的列表:创建后才出现的条目在第 3 步写入 (以前被跳过,报告却是成功)。
            var device = NewDevice()
                .With("S01", string.Empty, "Source")
                .With("S0109", string.Empty, "Crucible List")
                .With("S010900", string.Empty, "Add or Delete Crucible from the end of the list")
                .With("S010901", "0", "Crucible Material")
                .With("S0109FE", "1", "Number Of Crucibles");
            device.AppendTemplate = item => new[] { (item, "0", "Crucible Material") };
            var service = new InstrumentConfigService(device);
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var recipe = snapshot.ToRecipe(DateTimeOffset.Now);
            recipe.Entries.Add(new RecipeEntry { Id = "S010902", Value = "1", Description = "Crucible Material" });
            var plan = RestorePlanner.Plan(recipe, snapshot).Value;
            Assert.Equal(new[] { "AS010900" }, plan.Creates.Select(c => c.Command));

            var report = await service.ExecuteRestoreAsync(recipe, plan, null, CancellationToken.None);

            Assert.True(report.IsSuccess, report.Error);
            Assert.Equal("1", device.Get("S010902"));
            Assert.Contains("QS010902", device.CommandsSnapshot()); // 也读回校验
        }

        /// <summary>膜系 F01 用输出源 S01 (6 个坩埚),坩埚为任意 (0);另有 S02 (8 个坩埚)。</summary>
        private static FakeDeviceService NewDeviceWithSources() => NewDevice()
            .With("F0109", string.Empty, "Source")
            .With("F010901", "1", "Source Setting")
            .With("F010902", "0", "Crucible")
            .With("S01", string.Empty, "Source S01")
            .With("S0107", "6", "Number of Crucibles")
            .With("S02", string.Empty, "Source S02")
            .With("S0207", "8", "Number of Crucibles");

        [Fact]
        public async Task Plan_CrucibleAboveSourceCount_IsSkippedWithReason()
        {
            var (_, _, plan) = await PlanWithChangesAsync(NewDeviceWithSources(), ("F010902", "7"));

            Assert.DoesNotContain(plan.Writes, w => w.Id == "F010902");
            Assert.Equal("坩埚 7 超出输出源 S01 的坩埚数量 (6)", Assert.Single(plan.Skipped).Reason);
        }

        [Fact]
        public async Task Plan_Crucible_JudgedByValuesAfterRestore()
        {
            // 文件中的输出源 (S02,8 个坩埚) 和坩埚数量也会写入:按恢复后的值判断。
            var (_, _, otherSource) = await PlanWithChangesAsync(NewDeviceWithSources(), ("F010901", "2"), ("F010902", "7"));
            var (_, _, moreCrucibles) = await PlanWithChangesAsync(NewDeviceWithSources(), ("S0107", "8"), ("F010902", "7"));
            var (_, _, fewerCrucibles) = await PlanWithChangesAsync(NewDeviceWithSources(), ("S0207", "4"), ("F010901", "2"), ("F010902", "7"));

            Assert.Empty(otherSource.Skipped);
            Assert.Contains(otherSource.Writes, w => w.Command == "UF010902:7");
            Assert.Empty(moreCrucibles.Skipped);
            Assert.Contains(moreCrucibles.Writes, w => w.Command == "UF010902:7");
            Assert.Equal("F010902", Assert.Single(fewerCrucibles.Skipped).Id);
        }

        [Fact]
        public async Task ApplyEdits_CrucibleAboveSourceCount_RefusedBeforeWriting()
        {
            var device = NewDeviceWithSources();
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);

            var report = await service.ApplyEditsAsync(recipe, new[] { new RecipeEntry { Id = "F010902", Value = "7", Description = "Crucible" } }, null, CancellationToken.None);

            Assert.False(report.IsSuccess);
            Assert.Equal("F010902", report.FailedId);
            Assert.Equal("F010902 不能修改: 坩埚 7 超出输出源 S01 的坩埚数量 (6)", report.Error);
            Assert.DoesNotContain(device.CommandsSnapshot(), c => c.StartsWith("U", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ApplyEdits_SourceChangedInSameEdit_UsesTheNewSourceCount()
        {
            var device = NewDeviceWithSources();
            var service = new InstrumentConfigService(device);
            var recipe = (await service.ReadSnapshotAsync(CancellationToken.None)).Value.ToRecipe(DateTimeOffset.Now);
            var edits = new[]
            {
                new RecipeEntry { Id = "F010901", Value = "2", Description = "Source Setting" },
                new RecipeEntry { Id = "F010902", Value = "7", Description = "Crucible" },
            };

            var report = await service.ApplyEditsAsync(recipe, edits, null, CancellationToken.None);

            Assert.True(report.IsSuccess, report.Error);
            Assert.Equal("7", device.Get("F010902"));
        }

        private static async Task<(InstrumentConfigService Service, InstrumentRecipe Recipe, RestorePlan Plan)> PlanWithChangesAsync(
            FakeDeviceService device, params (string Id, string Value)[] changes)
        {
            var service = new InstrumentConfigService(device);
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var recipe = snapshot.ToRecipe(DateTimeOffset.Now);
            foreach (var (id, value) in changes)
            {
                Set(recipe, id, value);
            }

            return (service, recipe, RestorePlanner.Plan(recipe, snapshot).Value);
        }

        private static void Set(InstrumentRecipe recipe, string id, string value) =>
            recipe.Entries.Single(e => e.Id == id).Value = value;

        /// <summary>同步记录进度,避免 <see cref="Progress{T}"/> 异步回调带来的时序不确定。</summary>
        private sealed class SyncProgress : IProgress<RestoreProgress>
        {
            private readonly System.Collections.Generic.List<RestoreProgress> _reports;

            public SyncProgress(System.Collections.Generic.List<RestoreProgress> reports) => _reports = reports;

            public void Report(RestoreProgress value) => _reports.Add(value);
        }
    }
}
