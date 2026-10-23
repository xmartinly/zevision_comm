using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Communication;
using Zevision.Core.Configuration;
using Zevision.Core.Design;

namespace Zevision.Tests
{
    /// <summary>
    /// 工艺编辑的检查 (<see cref="DesignRules"/>) 和上传内容 (<see cref="DesignBuilder"/>):
    /// 编号取设备上空闲的,引用值是新对象的编号,上传经恢复流程新建对象、追加子列表、写入并校验。
    /// </summary>
    public class DesignBuilderTests
    {
        private static readonly FrameOptions LengthAndChecksum = new FrameOptions(checksumEnabled: true, lengthEnabled: true);

        /// <summary>实机 (固件 1.8.11) 的 <c>?</c>:材料 M01~M0A、M0C、M10,膜系 F01~F0A、F20,膜层 L01~L09,工艺 P0201~P0208、P0242、P02FA。</summary>
        internal static InstrumentSnapshot CaptureSnapshot()
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "imc300_1.8.11_h01_h02_help.bin"));
            var frames = new ZevisionFrameDecoder(LengthAndChecksum).Push(bytes, 0, bytes.Length);
            return new InstrumentSnapshot(HelpTableParser.Parse(ZevisionResponseParser.Parse(frames[2].Payload)[0].Data!));
        }

        /// <summary>两种材料、三步 (Al、SiO2、Al,第三步带速率斜坡)。</summary>
        private static (DesignDocument Document, ProcessDesign Process) Sample()
        {
            var al = new MaterialDesign { Name = "Al", Density = "2.700", ZRatio = "1.080", Sensor = 1, CrystalPosition = "2", Source = 1, Crucible = 2, MaxPower = "80.0", MaxPowerOption = "3", DelayOption = "1", ShutterDelayAccuracy = "8.00", TransferSensor = 2 };
            al.PreConditions[0].RampTime = "0:00:30";
            al.PreConditions[0].RampPower = "10.00";
            al.PreConditions[1].HoldTime = "0:01:00";
            al.PostConditions[0].RampPower = "5.00";
            var sio2 = new MaterialDesign { Name = "SiO2", Density = "2.648", ZRatio = "1.000", Sensor = 2, Source = 2, PTerm = "20", ITerm = "1.5", DTerm = "0.2" };
            var process = new ProcessDesign
            {
                Name = "AR",
                Steps =
                {
                    new ProcessStepDesign { MaterialId = al.Id, Rate = "5.0", Thickness = "1.000" },
                    new ProcessStepDesign { MaterialId = sio2.Id, Rate = "3.0", Thickness = "0.800" },
                    new ProcessStepDesign
                    {
                        MaterialId = al.Id,
                        Rate = "5.0",
                        Thickness = "0.500",
                        RateRamps = { new RateRampDesign { StartThickness = "0.400", RampTime = "0:00:10", NewRate = "2.0" } },
                    },
                },
            };
            var document = new DesignDocument { Materials = { al, sio2 }, Processes = { process } };
            return (document, process);
        }

        [Fact]
        public void NewMaterial_HasTwoPreConditionsOnePostConditionAndPid10()
        {
            var material = new MaterialDesign();

            Assert.Equal(2, material.PreConditions.Count);
            Assert.Single(material.PostConditions);
            Assert.Equal(("10", "0", "0"), (material.PTerm, material.ITerm, material.DTerm));
            Assert.Equal(0, material.Crucible); // 任意
        }

        [Fact]
        public void Build_RealDevice_UsesFreeNumbersAndNewReferences()
        {
            var (document, process) = Sample();

            var build = DesignBuilder.Build(document, process, CaptureSnapshot());

            Assert.True(build.IsSuccess, build.Error);
            Assert.Equal(
                new[] { "M10* Al", "M0B SiO2", "F0B Al", "F0C SiO2", "L0A Al-AR-1", "L0B SiO2-AR-2", "L0C Al-AR-3", "P0209 AR" },
                build.Value.Objects.Select(o => o.Id + (o.IsNew ? string.Empty : "*") + " " + o.Name));
            var values = build.Value.Recipe.Entries.ToDictionary(e => e.Id, e => e.Value);

            // 材料:实机上的 M10 就是 Al (名称、密度、Z 比、工具因子都相同),复用,不写入;SiO2 新建
            Assert.DoesNotContain(build.Value.Recipe.Entries, e => e.Id.StartsWith("M10", StringComparison.Ordinal));
            Assert.Equal("SiO2", values["M0B01"]);
            Assert.Equal("2.648", values["M0B03"]);
            Assert.Equal("1.000", values["M0B04"]);
            Assert.Equal("100.00", values["M0B07"]);

            // 膜系:引用新材料 (十进制编号)、预熔两段、后处理一段、传感器、源、坩埚、PID、最大功率及其处理、延迟选项及延迟设置
            Assert.Equal("Al", values["F0B01"]);
            Assert.Equal("16", values["F0B02"]); // M10
            Assert.Equal("11", values["F0C02"]); // M0B
            Assert.Equal("0:00:30", values["F0B030101"]);
            Assert.Equal("10.00", values["F0B030102"]);
            Assert.Equal("0:01:00", values["F0B030203"]);
            Assert.False(values.ContainsKey("F0B030301"));
            Assert.Equal("5.00", values["F0B050102"]);
            Assert.False(values.ContainsKey("F0B050201"));
            Assert.Equal("1", values["F0B0401"]);
            Assert.Equal("1", values["F0B070101"]);
            Assert.Equal("2", values["F0B070102"]);
            Assert.Equal("2", values["F0C070101"]);
            Assert.Equal("1", values["F0B0901"]);
            Assert.Equal("2", values["F0B0902"]);
            Assert.Equal(("10", "0", "0"), (values["F0B0903"], values["F0B0904"], values["F0B0905"]));
            Assert.Equal(("20", "1.5", "0.2"), (values["F0C0903"], values["F0C0904"], values["F0C0905"]));
            Assert.Equal("80.0", values["F0B0906"]);
            Assert.Equal(("3", "0"), (values["F0B0907"], values["F0C0907"]));

            // 延迟设置只在延迟选项不为 0 时写入 (SiO2 为 0)
            Assert.Equal(("0:00:00", "8.00", "0:01:00", "2", "100.00"), (values["F0B0402"], values["F0B0403"], values["F0B0404"], values["F0B0405"], values["F0B0406"]));
            Assert.Equal("0", values["F0C0401"]);
            Assert.DoesNotContain(build.Value.Recipe.Entries, e => e.Id.StartsWith("F0C040", StringComparison.Ordinal) && e.Id != "F0C0401");

            // 膜层:每步一个,名称为 材料-工艺-步骤号,膜系列表第 1 项是该材料的膜系
            Assert.Equal("Al-AR-1", values["L0A01"]);
            Assert.All(build.Value.Objects.Skip(1), o => Assert.True(o.IsNew)); // 实机上没有同名的膜系,膜层也新建
            Assert.Equal(("11", "5.0", "1.000"), (values["L0A020101"], values["L0A020102"], values["L0A020103"]));
            Assert.Equal("12", values["L0B020101"]);
            Assert.Equal("11", values["L0C020101"]);
            Assert.Equal(("0.400", "0:00:10", "2.0"), (values["L0C0201060101"], values["L0C0201060102"], values["L0C0201060103"]));

            // 工艺:膜层按步骤顺序,活动膜层 1,自动启动下一膜层;不设活动工艺 (P01)
            Assert.Equal("AR", values["P020901"]);
            Assert.Equal(("10", "11", "12"), (values["P02090701"], values["P02090702"], values["P02090703"]));
            Assert.Equal("1", values["P020902"]);
            Assert.Equal("1", values["P020904"]);
            Assert.DoesNotContain(build.Value.Recipe.Entries, e => e.Id == "P01");
            Assert.Equal("IMC300", build.Value.Recipe.DeviceName);
        }

        [Fact]
        public void Build_ThenPlan_OnlyCreatesNewObjects()
        {
            var (document, process) = Sample();
            var snapshot = CaptureSnapshot();
            var build = DesignBuilder.Build(document, process, snapshot).Value;

            var plan = RestorePlanner.Plan(build.Recipe, snapshot);

            Assert.True(plan.IsSuccess, plan.Error);
            Assert.Equal(new[] { "AM0B", "AF0B", "AF0C", "AL0A", "AL0B", "AL0C", "AP0209" }, plan.Value.Creates.Select(c => c.Command));
            Assert.Empty(plan.Value.Writes);   // 新对象的设置项在新建、重新读取之后写入
            Assert.Empty(plan.Value.Skipped);
            Assert.Empty(plan.Value.Warnings);
        }

        [Fact]
        public void Build_DeviceChecks_SensorSourceAndCrucible()
        {
            var (document, process) = Sample();
            document.Materials[0].Crucible = 7;  // S01 有 6 个坩埚
            document.Materials[1].Sensor = 3;    // 设备只有 N01、N02
            document.Materials[1].Source = 5;

            var build = DesignBuilder.Build(document, process, CaptureSnapshot());

            Assert.False(build.IsSuccess);
            var errors = build.Error!.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Contains(errors, e => e.StartsWith("材料 Al: 坩埚: ", StringComparison.Ordinal) && e.Contains("6"));
            Assert.Contains("材料 SiO2: 设备上没有传感器 3", errors);
            Assert.Contains("材料 SiO2: 设备上没有输出源 5", errors);
            Assert.DoesNotContain(errors, e => e.Contains("传输传感器")); // Al 的传输传感器 2 存在

            document.Materials[0].TransferSensor = 4;
            var transfer = DesignBuilder.Build(document, process, CaptureSnapshot()).Error!;
            Assert.Contains("材料 Al: 传输传感器 ID: 设备上没有传感器 4", transfer.Split(new[] { Environment.NewLine }, StringSplitOptions.None));
            document.Materials[0].DelayOption = "0"; // 没有延迟时不检查 (也不写入)
            Assert.DoesNotContain("传输传感器", DesignBuilder.Build(document, process, CaptureSnapshot()).Error!);
        }

        [Fact]
        public void Validate_ReportsEveryProblemWithContext()
        {
            var (document, process) = Sample();
            document.Materials[0].Density = "abc";
            document.Materials[0].PreConditions[1].RampPower = "120";
            document.Materials[1].Name = "al"; // 与 Al 重名 (不区分大小写)
            process.Steps[1].Rate = string.Empty;
            process.Steps[2].RateRamps[0].RampTime = "x";
            document.Processes.Add(new ProcessDesign { Name = " " });

            var errors = DesignRules.Validate(document);

            Assert.Contains(errors, e => e.StartsWith("材料 Al: 密度 (g/cm³): ", StringComparison.Ordinal));
            Assert.Contains(errors, e => e.StartsWith("材料 Al: 预熔 2 · 斜坡功率 (%): 超出范围", StringComparison.Ordinal));
            Assert.Contains("材料 al: 材料名称: 与其他名称重复", errors);
            Assert.Contains(errors, e => e.StartsWith("工艺 AR 第 2 步: 速率 (Å/s): ", StringComparison.Ordinal));
            Assert.Contains(errors, e => e.StartsWith("工艺 AR 第 3 步速率斜坡 1: 斜坡时间: ", StringComparison.Ordinal));
            Assert.Contains("工艺 (未命名): 工艺名称: 不能为空", errors);
            Assert.Contains("工艺 (未命名): 没有步骤", errors);
            Assert.Equal(7, errors.Count);
        }

        [Fact]
        public void Validate_StepWithoutMaterial_AndProtocolCharacters()
        {
            var (document, process) = Sample();
            document.Materials[0].Name = "Al;x";
            process.Steps[0].MaterialId = "gone";

            var errors = DesignRules.Validate(document);

            Assert.Contains(errors, e => e.StartsWith("材料 Al;x: 材料名称: ", StringComparison.Ordinal));
            Assert.Contains("工艺 AR 第 1 步: 没有选择材料", errors);
        }

        [Fact]
        public void Validate_SampleIsValid()
        {
            Assert.Empty(DesignRules.Validate(Sample().Document));
        }

        [Fact]
        public void Validate_NamesAtMost30Characters_IncludingGeneratedLayerNames()
        {
            // 实机最多保存 31 个字符,帮助表 ? 只显示前 30 个:复用和删除按 ? 读到的名称比较。
            var (document, process) = Sample();
            process.Name = new string('P', 25); // Al-<25>-1 = 30 个字符,SiO2-<25>-2 = 32 个
            document.Materials[1].Name = "SiO2";

            var errors = DesignRules.Validate(document);

            Assert.Equal(new[] { "工艺 " + process.Name + " 第 2 步: 膜层名称 SiO2-" + process.Name + "-2 有 32 个字符,最多 30 个,应缩短材料名称或工艺名称" }, errors);

            document.Materials[1].Name = new string('S', 31);
            process.Name = "AR";
            errors = DesignRules.Validate(document);

            Assert.Contains("材料 " + document.Materials[1].Name + ": 材料名称: 最多 30 个字符", errors);
            Assert.Contains(errors, e => e.StartsWith("工艺 AR 第 2 步: 膜层名称 ", StringComparison.Ordinal));
        }

        [Fact]
        public void Validate_RateAndThickness_AtMostThreeDecimals()
        {
            var (document, process) = Sample();
            process.Steps[0].Rate = "5.0001";
            process.Steps[1].Thickness = "0.8000";      // 末尾的 0 也算
            process.Steps[2].RateRamps[0].NewRate = "2.125";
            process.Steps[2].RateRamps[0].StartThickness = "0.4005";

            var errors = DesignRules.Validate(document);

            Assert.Equal(
                new[] { "工艺 AR 第 1 步: 速率 (Å/s): 最多 3 位小数", "工艺 AR 第 2 步: 最终厚度 (kÅ): 最多 3 位小数", "工艺 AR 第 3 步速率斜坡 1: 起始厚度 (kÅ): 最多 3 位小数" },
                errors);
        }

        [Theory]
        [InlineData(StepField.Rate, "5.125", null)]
        [InlineData(StepField.Rate, "5.1250", "最多 3 位小数")]
        [InlineData(StepField.Rate, " ", null)]                  // 空值在保存时指出
        [InlineData(StepField.Thickness, "1", null)]
        [InlineData(StepField.Thickness, "0.0001", "最多 3 位小数")]
        [InlineData(StepField.RampStartThickness, "0.4005", "最多 3 位小数")]
        [InlineData(StepField.RampNewRate, "2.5", null)]
        [InlineData(StepField.RampTime, "0:00:10", null)]
        public void CheckStepValue_ThreeDecimals(StepField field, string value, string? expected)
        {
            Assert.Equal(expected, DesignRules.CheckStepValue(field, value));
        }

        [Fact]
        public void CheckStepValue_SameChecksAsSaving_NumberRangeAndTime()
        {
            Assert.NotNull(DesignRules.CheckStepValue(StepField.Rate, "abc"));
            Assert.StartsWith("超出范围", DesignRules.CheckStepValue(StepField.Thickness, "1000.5"), StringComparison.Ordinal);
            Assert.NotNull(DesignRules.CheckStepValue(StepField.RampTime, "x"));
            Assert.NotEqual("最多 3 位小数", DesignRules.CheckStepValue(StepField.Rate, "1e3")); // 不是数字,先报数字的问题
        }

        [Fact]
        public void DeviceObjects_FromGroupHelp_NamesAndCrucibleCounts()
        {
            var entries = new[]
            {
                new RecipeEntry { Id = "S0101", Value = "E-Gun" },
                new RecipeEntry { Id = "S0107", Value = "6" },
                new RecipeEntry { Id = "S0201", Value = "Boat" },
                new RecipeEntry { Id = "S0208", Value = "1" },
                new RecipeEntry { Id = "S0C0101", Value = "0" },
                new RecipeEntry { Id = "N0101", Value = "Sensor" },
            };

            var sources = DesignDevice.Objects(entries, DesignDevice.SourceGroup);
            var sensors = DesignDevice.Objects(entries, DesignDevice.SensorGroup);

            Assert.Equal(new[] { new DeviceObjectInfo(1, "E-Gun", 6), new DeviceObjectInfo(2, "Boat", 0) }, sources);
            Assert.Equal(new[] { new DeviceObjectInfo(1, "Sensor", 0) }, sensors);
        }

        [Fact]
        public async Task Upload_ThroughRestore_CreatesListsAndWritesEveryValue()
        {
            var device = NewDevice();
            var service = new InstrumentConfigService(device);
            var material = new MaterialDesign { Name = "Al", Density = "2.700", ZRatio = "1.080", Crucible = 3, PTerm = "10", MaxPower = "90.0" };
            material.PreConditions[1].RampPower = "20.00";
            var process = new ProcessDesign
            {
                Name = "P1",
                Steps =
                {
                    new ProcessStepDesign { MaterialId = material.Id, Rate = "4.0", Thickness = "0.300" },
                    new ProcessStepDesign
                    {
                        MaterialId = material.Id,
                        Rate = "2.0",
                        Thickness = "0.100",
                        RateRamps = { new RateRampDesign { StartThickness = "0.050", RampTime = "0:00:05", NewRate = "1.0" } },
                    },
                },
            };
            var document = new DesignDocument { Materials = { material }, Processes = { process } };
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var build = DesignBuilder.Build(document, process, snapshot).Value;
            var plan = RestorePlanner.Plan(build.Recipe, snapshot).Value;

            var report = await service.ExecuteRestoreAsync(build.Recipe, plan, null, CancellationToken.None);

            Assert.True(report.IsSuccess, report.Error + string.Join("; ", report.VerifyMismatches));
            Assert.Equal(new[] { "M02 Al", "F02 Al", "L02 Al-P1-1", "L03 Al-P1-2", "P0202 P1" }, build.Objects.Select(o => o.Id + " " + o.Name));
            foreach (var entry in build.Recipe.Entries)
            {
                Assert.True(ParameterValue.AreEquivalent(entry.Value, device.Get(entry.Id) ?? "(missing)"), entry.Id + " = " + device.Get(entry.Id));
            }

            Assert.Equal("2", device.Get("F0203FE"));
            Assert.Equal("1", device.Get("F0205FE"));
            Assert.Equal("1", device.Get("F0207FE"));
            Assert.Equal("2", device.Get("P020207FE"));
            Assert.Equal("1", device.Get("L03020106FE"));

            // 只新建和写入新对象:已有的材料、膜系、工艺不动,不设活动工艺。
            Assert.DoesNotContain(device.Commands, c => c.StartsWith("UM01", StringComparison.Ordinal) || c.StartsWith("UF01", StringComparison.Ordinal)
                || c.StartsWith("UL01", StringComparison.Ordinal) || c.StartsWith("UP0201", StringComparison.Ordinal) || c.StartsWith("UP01", StringComparison.Ordinal));
            Assert.Contains("AF020300", device.Commands);
            Assert.Contains("AP02020700", device.Commands);
        }

        /// <summary>一种材料 Al、两步 (第二步带速率斜坡) 的工艺 P1。</summary>
        private static (DesignDocument Document, ProcessDesign Process) Simple()
        {
            var material = new MaterialDesign { Name = "Al", Density = "2.700", ZRatio = "1.080", Crucible = 3 };
            var process = new ProcessDesign
            {
                Name = "P1",
                Steps =
                {
                    new ProcessStepDesign { MaterialId = material.Id, Rate = "4.0", Thickness = "0.300" },
                    new ProcessStepDesign
                    {
                        MaterialId = material.Id,
                        Rate = "2.0",
                        Thickness = "0.100",
                        RateRamps = { new RateRampDesign { StartThickness = "0.050", RampTime = "0:00:05", NewRate = "1.0" } },
                    },
                },
            };
            return (new DesignDocument { Materials = { material }, Processes = { process } }, process);
        }

        /// <summary>读取设备、生成、规划、执行上传;返回上传内容。</summary>
        private static async Task<DesignBuild> UploadAsync(FakeDeviceService device, DesignDocument document, ProcessDesign process)
        {
            var service = new InstrumentConfigService(device);
            var snapshot = (await service.ReadSnapshotAsync(CancellationToken.None)).Value;
            var build = DesignBuilder.Build(document, process, snapshot);
            Assert.True(build.IsSuccess, build.Error);
            var plan = RestorePlanner.Plan(build.Value.Recipe, snapshot);
            Assert.True(plan.IsSuccess, plan.Error);
            var report = await service.ExecuteRestoreAsync(build.Value.Recipe, plan.Value, null, CancellationToken.None);
            Assert.True(report.IsSuccess, report.Error + string.Join("; ", report.VerifyMismatches));
            return build.Value;
        }

        private static string Describe(DesignBuild build) =>
            string.Join(", ", build.Objects.Select(o => o.Id + (o.IsNew ? string.Empty : "*")));

        [Fact]
        public async Task Upload_Again_ReusesMaterialFilmAndLayers_OnlyProcessIsNew()
        {
            var device = NewDevice();
            var (document, process) = Simple();
            await UploadAsync(device, document, process);
            var commands = device.Commands.Count;

            var second = await UploadAsync(device, document, process);

            Assert.Equal("M02*, F02*, L02*, L03*, P0203", Describe(second));
            Assert.All(second.Recipe.Entries, e => Assert.StartsWith("P0203", e.Id, StringComparison.Ordinal));
            Assert.Equal(("2", "3"), (device.Get("P02030701"), device.Get("P02030702")));
            Assert.DoesNotContain(device.Commands.Skip(commands), c => c.StartsWith("AM", StringComparison.Ordinal)
                || c.StartsWith("AF", StringComparison.Ordinal) || c.StartsWith("AL", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Upload_ChangedThickness_OnlyThatLayerIsNew()
        {
            var device = NewDevice();
            var (document, process) = Simple();
            await UploadAsync(device, document, process);
            process.Steps[0].Thickness = "0.400";

            var second = await UploadAsync(device, document, process);

            Assert.Equal("M02*, F02*, L04, L03*, P0203", Describe(second));
            Assert.Equal("Al-P1-1", device.Get("L0401"));
            Assert.Equal(("4", "3"), (device.Get("P02030701"), device.Get("P02030702")));
        }

        [Fact]
        public async Task Upload_ChangedPid_NewFilmAndLayers_MaterialReused()
        {
            var device = NewDevice();
            var (document, process) = Simple();
            await UploadAsync(device, document, process);
            document.Materials[0].PTerm = "12";

            var second = await UploadAsync(device, document, process);

            // 膜层引用膜系:新膜系的膜层也要新建。
            Assert.Equal("M02*, F03, L04, L05, P0203", Describe(second));
            Assert.Equal("2", device.Get("F0302"));
            Assert.Equal("3", device.Get("L04020101"));
        }

        [Fact]
        public async Task Upload_ChangedName_IsNotReused()
        {
            var device = NewDevice();
            var (document, process) = Simple();
            await UploadAsync(device, document, process);
            document.Materials[0].Name = "Al2";

            var second = await UploadAsync(device, document, process);

            Assert.Equal("M03, F03, L04, L05, P0203", Describe(second));
            Assert.Equal("Al2-P1-1", device.Get("L0401"));
        }

        [Fact]
        public async Task Upload_OtherProcess_ReusesALayerWithTheSameParametersWhateverItsName()
        {
            var device = NewDevice();
            var (document, process) = Simple();
            await UploadAsync(device, document, process);
            var same = process.Steps[1];
            var other = new ProcessDesign
            {
                Name = "P2",
                Steps =
                {
                    new ProcessStepDesign { MaterialId = same.MaterialId, Rate = same.Rate, Thickness = same.Thickness, RateRamps = { same.RateRamps[0] } },
                    new ProcessStepDesign { MaterialId = same.MaterialId, Rate = "6.0", Thickness = "0.200" },
                },
            };
            document.Processes.Add(other);

            var second = await UploadAsync(device, document, other);

            // 第 1 步与 P1 的第 2 步参数相同:复用 L03 (名称是 P1 的);第 2 步新建,名称按 P2。
            Assert.Equal(new[] { "M02* Al", "F02* Al", "L03* Al-P1-2", "L04 Al-P2-2", "P0203 P2" }, second.Objects.Select(o => o.Id + (o.IsNew ? string.Empty : "*") + " " + o.Name));
            Assert.Equal("Al-P2-2", device.Get("L0401"));
            Assert.Equal(("3", "4"), (device.Get("P02030701"), device.Get("P02030702")));
        }

        [Fact]
        public async Task Upload_SameStepTwice_SharesOneLayer()
        {
            var device = NewDevice();
            var (document, process) = Simple();
            process.Steps.Add(new ProcessStepDesign { MaterialId = document.Materials[0].Id, Rate = "4.00", Thickness = "0.3" });

            var build = await UploadAsync(device, document, process);

            Assert.Equal("M02, F02, L02, L03, P0202", Describe(build));
            Assert.Equal(("2", "3", "2"), (device.Get("P02020701"), device.Get("P02020702"), device.Get("P02020703")));
            Assert.Equal("Al-P1-1", device.Get("L0201")); // 共用的膜层用第一步的名称
        }

        /// <summary>
        /// 模拟设备:已有材料 M01、膜系 F01、膜层 L01、工艺 P0201,传感器 N01,输出源 S01 (6 个坩埚)。
        /// 新建的对象按实机的结构 (各子列表为空,有"追加到末尾"项),追加的条目带设备默认值。
        /// </summary>
        internal static FakeDeviceService NewDevice()
        {
            var device = new FakeDeviceService()
                .With("C09", "Ready", "Current State")
                .With("G03", "IMC300", "Device Name")
                .With("G05", "IMC", "Model Number")
                .With("G08", "1.8.11.202606101229", "System Software Version")
                .With("M01", string.Empty, "Material M01")
                .With("M0101", "M1", "Name")
                .With("MFE", "1", "Number of Materials")
                .With("F01", string.Empty, "Film F01")
                .With("F0101", "L1", "Name")
                .With("FFE", "1", "Number of Films")
                .With("L01", string.Empty, "Layer L01")
                .With("L0101", "L1", "Layer Name")
                .With("LFE", "1", "Number of Layers")
                .With("P01", "1", "Set Active Process")
                .With("P02", string.Empty, "Process List")
                .With("P0201", string.Empty, "Process")
                .With("P020101", "Process 1", "Name")
                .With("P02FE", "1", "Number Of Processes")
                .With("N01", string.Empty, "Sensor N01")
                .With("N0101", "Sensor", "Name")
                .With("S01", string.Empty, "Source S01")
                .With("S0101", "Source", "Name")
                .With("S0107", "6", "Number of Crucibles")
                .With("SFE", "1", "Number Of Sources");
            device.Templates['M'] = id => new[]
            {
                (id, string.Empty, "Material " + id),
                (id + "01", "M", "Name"),
                (id + "03", "1.000", "Density (g/cm^3)"),
                (id + "04", "1.000", "Z Ratio"),
                (id + "07", "100.00", "Tooling"),
            };
            device.Templates['F'] = id => new[]
            {
                (id, string.Empty, "Film " + id),
                (id + "01", "F", "Name"),
                (id + "02", "0", "Material"),
                (id + "03", string.Empty, "Pre Condition List"),
                (id + "0300", string.Empty, "Add or Delete Pre Condition from the end of the list"),
                (id + "03FE", "0", "Number of Pre Conditions"),
                (id + "04", string.Empty, "Delay Settings"),
                (id + "0401", "0", "Delay Option"),
                (id + "0402", "0:00:00", "Control Delay Time"),
                (id + "0403", "5.00", "Shutter Delay Accuracy"),
                (id + "0404", "0:01:00", "Shutter Delay Timeout"),
                (id + "0405", "0", "Transfer Sensor"),
                (id + "0406", "100.00", "Transfer Tooling"),
                (id + "05", string.Empty, "Post Condition List"),
                (id + "0500", string.Empty, "Add or Delete Post Condition from the end of the list"),
                (id + "05FE", "0", "Number of Post Conditions"),
                (id + "07", string.Empty, "Sensor List"),
                (id + "0700", string.Empty, "Add or Delete Sensor from the end of the list"),
                (id + "07FE", "0", "Number of Sensors"),
                (id + "09", string.Empty, "Source Settings"),
                (id + "0901", "0", "Source Setting"),
                (id + "0902", "0", "Crucible"),
                (id + "0903", "5.000", "P Term"),
                (id + "0904", "0.000", "I Term"),
                (id + "0905", "0.000", "D Term"),
                (id + "0906", "0.00", "Maximum Power"),
                (id + "0907", "0", "Max Power Option"),
            };
            device.Templates['L'] = id => new[]
            {
                (id, string.Empty, "Layer " + id),
                (id + "01", "L", "Layer Name"),
                (id + "02", string.Empty, "Film List"),
                (id + "0200", string.Empty, "Add or Delete Film from the end of the list"),
                (id + "02FE", "0", "Number of Layer Films"),
            };
            device.Templates['P'] = id => new[]
            {
                (id, string.Empty, "Process"),
                (id + "01", "P", "Name"),
                (id + "02", "1", "Active Layer"),
                (id + "04", "0", "Auto Start Next"),
                (id + "07", string.Empty, "Layer List"),
                (id + "0700", string.Empty, "Add or Delete Layer from the end of the list"),
                (id + "07FE", "0", "Number Of Layers"),
            };
            device.AppendTemplate = AppendedItem;
            return device;
        }

        /// <summary>追加到子列表末尾的条目及其默认值,按列表区分。</summary>
        private static IEnumerable<(string, string, string)> AppendedItem(string item)
        {
            if (item[0] == 'P')
            {
                return new[] { (item, "0", "Layer") };
            }

            if (item[0] == 'L' && item.Length == 7)
            {
                return new[]
                {
                    (item, string.Empty, "Film"),
                    (item + "01", "0", "Film"),
                    (item + "02", "0.000", "Rate"),
                    (item + "03", "0.000", "Final Thickness"),
                    (item + "06", string.Empty, "Rate Ramp List"),
                    (item + "0600", string.Empty, "Add or Delete Rate Ramp from the end of the list"),
                    (item + "06FE", "0", "Number of Rate Ramps"),
                };
            }

            if (item[0] == 'L')
            {
                return new[] { (item, string.Empty, "Rate Ramp"), (item + "01", "0.0000", "Start Thickness"), (item + "02", "0:00:00", "Ramp Time"), (item + "03", "0.000", "New Rate") };
            }

            if (item.Substring(3, 2) == "07")
            {
                return new[] { (item, string.Empty, "Sensor"), (item + "01", "1", "Sensor Number"), (item + "02", "0", "Crystal Position") };
            }

            return new[] { (item, string.Empty, "Step"), (item + "01", "0:00:00", "Ramp time"), (item + "02", "0.00", "Ramp Power"), (item + "03", "0:00:00", "Hold Time") };
        }
    }
}
