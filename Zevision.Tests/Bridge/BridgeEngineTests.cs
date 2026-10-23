using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Bridge;
using Zevision.Communication;
using Zevision.Xtc3;

namespace Zevision.Tests
{
    public class BridgeEngineTests
    {
        // ---------------------------------------------------------------- 影子对象

        [Fact]
        public async Task Prepare_CompleteShadowObjects_Ready_OthersMissing()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1, 2));

            var checks = harness.Engine.ShadowChecks;

            Assert.Equal(new[] { ShadowStatus.Ready, ShadowStatus.Ready }, checks.Take(2).Select(c => c.Status));
            Assert.All(checks.Skip(2), c => Assert.Equal(ShadowStatus.Missing, c.Status));
            Assert.Equal(new[] { "MB3", "FB3", "LB3", "P02B3" }, checks[2].MissingObjects);
            Assert.DoesNotContain(harness.Device.Commands, c => c.StartsWith("A", StringComparison.Ordinal) || c.StartsWith("U", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Prepare_NumberUsedByAnotherObject_Occupied_NotTouched()
        {
            var device = BridgeTestDevice.Create(1).Set("FB101", "My film");

            var harness = await BridgeHarness.ReadyAsync(device);

            var check = harness.Engine.ShadowChecks[0];
            Assert.Equal(ShadowStatus.Occupied, check.Status);
            Assert.Contains("FB1", check.Detail);
            Assert.Equal("E", (await harness.RunOneAsync(Xtc3Command.QueryFilm(8, 1))).ToString());
        }

        [Fact]
        public async Task Prepare_ListStructureChanged_Invalid()
        {
            var device = BridgeTestDevice.Create(1).Set("FB103FE", "3");

            var harness = await BridgeHarness.ReadyAsync(device);

            Assert.Equal(ShadowStatus.Invalid, harness.Engine.ShadowChecks[0].Status);
            Assert.Contains("FB103FE = 3, expected 2", harness.Engine.ShadowChecks[0].Detail);
        }

        [Fact]
        public async Task Prepare_CreateMissing_BuildsAllNineFilmsByNumber()
        {
            var device = DesignBuilderTests.NewDevice();
            var options = new Xtc3BridgeOptions(new ConnectionOptions(new TcpSettings("127.0.0.1", TcpSettings.DefaultPort), new FrameOptions(true, true)))
            {
                CreateMissingShadowObjects = true,
            };

            var harness = await BridgeHarness.ReadyAsync(device, options);

            Assert.All(harness.Engine.ShadowChecks, c => Assert.Equal(ShadowStatus.Ready, c.Status));
            Assert.Contains("AMB1", device.Commands);
            Assert.Contains("AP02B9", device.Commands);
            Assert.Equal("XTC Film 9", device.Get("P02B901"));
            Assert.Equal("185", device.Get("LB9020101"));
            Assert.Equal("2", device.Get("FB903FE"));
            Assert.Equal("1", device.Get("LB9020106FE"));
            Assert.Equal("1", device.Get("P01")); // 不设活动工艺
        }

        // ---------------------------------------------------------------- Film 参数

        [Fact]
        public async Task QueryFilm_ConvertsTypesAndUnits()
        {
            var device = BridgeTestDevice.Create(1).Set("LB1020102", "4.500").Set("FB1030101", "0:01:30").Set("FB10807", "12.6").Set("MB103", "2.700");
            var harness = await BridgeHarness.ReadyAsync(device);

            var results = await harness.RunAsync(Xtc3Command.QueryFilm(8, 1), Xtc3Command.QueryFilm(0, 1), Xtc3Command.QueryFilm(20, 1), Xtc3Command.QueryFilm(28, 1));

            Assert.Equal(new[] { "ACK [Float 4.5]", "ACK [Int 90]", "ACK [Int 13]", "ACK [Float 2.7]" }, results.Select(r => r.ToString()));
            Assert.Contains("QLB1020102", harness.Device.Frames);
        }

        [Fact]
        public async Task UpdateFilm_WritesDeviceFormat_AndReadsBack()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1));

            var results = await harness.RunAsync(
                Xtc3Command.UpdateFilm(8, 1, Xtc3Value.FromFloat(2.55f)),
                Xtc3Command.UpdateFilm(9, 1, Xtc3Value.FromFloat(1.25f)),
                Xtc3Command.UpdateFilm(0, 1, Xtc3Value.FromInteger(125)),
                Xtc3Command.UpdateFilm(28, 1, Xtc3Value.FromFloat(5.89f)),
                Xtc3Command.UpdateFilm(13, 1, Xtc3Value.FromInteger(20)));

            Assert.All(results, r => Assert.True(r.IsSuccess, r.ToString()));
            Assert.Equal(("2.550", "1.250", "0:02:05", "5.890", "20"),
                (harness.Device.Get("LB1020102"), harness.Device.Get("LB1020103"), harness.Device.Get("FB1030101"), harness.Device.Get("MB103"), harness.Device.Get("FB10603")));
            Assert.Contains("ULB1020102:2.550", harness.Device.Commands);
            Assert.Contains("QLB1020102", harness.Device.Commands);
        }

        [Fact]
        public async Task UpdateFilm_SpecialConversions()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1));

            await harness.RunAsync(
                Xtc3Command.UpdateFilm(1, 1, Xtc3Value.FromFloat(100f)),
                Xtc3Command.UpdateFilm(14, 1, Xtc3Value.FromInteger(30)),
                Xtc3Command.UpdateFilm(30, 1, Xtc3Value.FromInteger(1)),
                Xtc3Command.UpdateFilm(32, 1, Xtc3Value.FromInteger(1)));

            Assert.Equal("99.99", harness.Device.Get("FB1030102")); // IMC300 预熔功率上限
            Assert.Equal(("1", "0:00:30"), (harness.Device.Get("FB10601"), harness.Device.Get("FB10602")));
            Assert.Equal("4", harness.Device.Get("FB1070105")); // 失败动作 = 时间功率
            Assert.Equal("2", harness.Device.Get("FB10405")); // 另一个传感器
            var read = await harness.RunAsync(Xtc3Command.QueryFilm(14, 1), Xtc3Command.QueryFilm(30, 1), Xtc3Command.QueryFilm(32, 1));
            Assert.Equal(new[] { "ACK [Int 30]", "ACK [Int 1]", "ACK [Int 1]" }, read.Select(r => r.ToString()));

            await harness.RunAsync(Xtc3Command.UpdateFilm(14, 1, Xtc3Value.FromInteger(0)), Xtc3Command.UpdateFilm(30, 1, Xtc3Value.FromInteger(0)));
            Assert.Equal(("0", "2"), (harness.Device.Get("FB10601"), harness.Device.Get("FB1070105")));
            Assert.Equal("ACK [Int 0]", (await harness.RunOneAsync(Xtc3Command.QueryFilm(14, 1))).ToString());
        }

        [Fact]
        public async Task UpdateFilm_LocalParameters_SavedAndReadBack()
        {
            var store = new MemoryBridgeStateStore();
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1), store: store);

            await harness.RunAsync(
                Xtc3Command.UpdateFilm(36, 1, Xtc3Value.FromInteger(1)),
                Xtc3Command.UpdateFilm(37, 1, Xtc3Value.FromInteger(3)),
                Xtc3Command.UpdateFilm(17, 1, Xtc3Value.FromFloat(150f)));
            var again = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1), store: store);

            var read = await again.RunAsync(Xtc3Command.QueryFilm(36, 1), Xtc3Command.QueryFilm(37, 1), Xtc3Command.QueryFilm(17, 1));
            Assert.Equal(new[] { "ACK [Int 1]", "ACK [Int 3]", "ACK [Float 150]" }, read.Select(r => r.ToString()));
            Assert.DoesNotContain(harness.Device.Commands, c => c.StartsWith("U", StringComparison.Ordinal));
        }

        [Fact]
        public async Task QueryAndUpdateFilmAll_RoundTrip38Values()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(2));
            var values = Xtc3Parameters.Film.Select(p => p.Type == Xtc3ValueType.Float
                ? Xtc3Value.FromFloat((float)Math.Max(p.Min, Math.Min(p.Max, 10.5m)))
                : Xtc3Value.FromInteger((int)Math.Max(p.Min, Math.Min(p.Max, p.Id == 14 ? 30 : p.Id == 18 || p.Id == 19 ? 0 : 1)))).ToList();

            var write = await harness.RunOneAsync(Xtc3Command.UpdateFilmAll(2, values));
            var read = await harness.RunOneAsync(Xtc3Command.QueryFilm(Xtc3Parameters.AllId, 2));

            Assert.True(write.IsSuccess, write.ToString());
            Assert.Equal(values.Select(v => v.ToString()), read.Values.Select(v => v.ToString()));
        }

        [Theory]
        [InlineData(0, "B")]
        [InlineData(10, "B")]
        [InlineData(3, "E")] // 影子对象不存在
        public async Task Film_NumberOutOfRangeOrNotReady(int film, string expected)
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1));

            Assert.Equal(expected, (await harness.RunOneAsync(Xtc3Command.QueryFilm(8, film))).ToString());
        }

        [Fact]
        public async Task UpdateFilm_OutOfXtcRange_B_NothingWritten()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1));

            var results = await harness.RunAsync(
                Xtc3Command.UpdateFilm(8, 1, Xtc3Value.FromFloat(1000f)),
                Xtc3Command.UpdateFilm(8, 1, Xtc3Value.FromFloat(1f)));

            Assert.Equal(new[] { "B", "P" }, results.Select(r => r.ToString()));
            Assert.DoesNotContain(harness.Device.Commands, c => c.StartsWith("U", StringComparison.Ordinal));
        }

        [Fact]
        public async Task UpdateFilm_CrucibleAboveSourceCount_B()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1));

            Assert.Equal("B", (await harness.RunOneAsync(Xtc3Command.UpdateFilm(23, 1, Xtc3Value.FromInteger(5)))).ToString());
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.UpdateFilm(23, 1, Xtc3Value.FromInteger(4)))).ToString());
        }

        [Fact]
        public async Task UpdateFilm_SensorWhileDepositing_F()
        {
            var device = BridgeTestDevice.Create(1).Set("C09", "Deposit");
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            Assert.Equal("F", (await harness.RunOneAsync(Xtc3Command.UpdateFilm(15, 1, Xtc3Value.FromInteger(2)))).ToString());
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.UpdateFilm(8, 1, Xtc3Value.FromFloat(3f)))).ToString());
        }

        [Fact]
        public async Task DeviceRejection_MapsToXtcCodes_LaterCommandsPrior()
        {
            var device = BridgeTestDevice.Create(1);
            device.RejectWrites.Add("LB1020102");
            device.StateErrorWrites.Add("LB1020103");
            var harness = await BridgeHarness.ReadyAsync(device);

            var rejected = await harness.RunAsync(Xtc3Command.UpdateFilm(8, 1, Xtc3Value.FromFloat(2f)), Xtc3Command.Status(4));
            var state = await harness.RunOneAsync(Xtc3Command.UpdateFilm(9, 1, Xtc3Value.FromFloat(2f)));

            Assert.Equal(new[] { "B", "P" }, rejected.Select(r => r.ToString()));
            Assert.Equal("F", state.ToString());
        }

        [Fact]
        public async Task ReadBackDiffers_B()
        {
            var device = BridgeTestDevice.Create(1);
            device.IgnoreWrites.Add("MB104");
            var harness = await BridgeHarness.ReadyAsync(device);

            Assert.Equal("B", (await harness.RunOneAsync(Xtc3Command.UpdateFilm(29, 1, Xtc3Value.FromFloat(2f)))).ToString());
        }

        // ---------------------------------------------------------------- 通用参数

        [Fact]
        public async Task General_FilmToRun_MapsToActiveProcess()
        {
            var device = BridgeTestDevice.Create(1, 2).Set("P01", "177");
            var harness = await BridgeHarness.ReadyAsync(device);

            Assert.Equal("ACK [Int 1]", (await harness.RunOneAsync(Xtc3Command.QueryGeneral(0))).ToString());
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.UpdateGeneral(0, 2))).ToString());
            Assert.Equal("178", device.Get("P01"));
            Assert.Equal("E", (await harness.RunOneAsync(Xtc3Command.UpdateGeneral(0, 3))).ToString()); // Film 3 没有影子对象
        }

        [Fact]
        public async Task General_FilmToRun_OnlyWhenReadyOrIdle()
        {
            var device = BridgeTestDevice.Create(1, 2).Set("P01", "177").Set("C09", "Deposit");
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            Assert.Equal("F", (await harness.RunOneAsync(Xtc3Command.UpdateGeneral(0, 2))).ToString());
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.UpdateGeneral(0, 1))).ToString()); // 已是该 Film
        }

        [Fact]
        public async Task General_PerFilmSettings_WrittenToEveryShadowFilm()
        {
            var device = BridgeTestDevice.Create(1, 2).Set("P01", "178");
            var harness = await BridgeHarness.ReadyAsync(device);

            await harness.RunAsync(Xtc3Command.UpdateGeneral(3, 1), Xtc3Command.UpdateGeneral(4, 1), Xtc3Command.UpdateGeneral(1, 1));

            Assert.Equal(("2", "2"), (device.Get("FB10907"), device.Get("FB20907")));
            Assert.Equal(("1", "1"), (device.Get("P02B108"), device.Get("P02B208")));
            Assert.Equal(("1", "1"), (device.Get("P02B10B"), device.Get("P02B20B")));
            var read = await harness.RunAsync(Xtc3Command.QueryGeneral(3), Xtc3Command.QueryGeneral(4));
            Assert.Equal(new[] { "ACK [Int 1]", "ACK [Int 1]" }, read.Select(r => r.ToString()));
        }

        [Fact]
        public async Task General_SensorTypeAndVoltage_Mapped()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1));

            await harness.RunAsync(Xtc3Command.UpdateGeneral(6, 1), Xtc3Command.UpdateGeneral(7, 4), Xtc3Command.UpdateGeneral(8, 3));

            Assert.Equal(("2", "5"), (harness.Device.Get("N0102"), harness.Device.Get("N0202")));
            Assert.Equal(("-5.00", "-5.00"), (harness.Device.Get("S010A"), harness.Device.Get("S020A")));
            var read = await harness.RunAsync(Xtc3Command.QueryGeneral(6), Xtc3Command.QueryGeneral(7), Xtc3Command.QueryGeneral(8));
            Assert.Equal(new[] { "ACK [Int 1]", "ACK [Int 4]", "ACK [Int 3]" }, read.Select(r => r.ToString()));
        }

        [Fact]
        public async Task General_All_SixteenIntegers_LocalOnesWithoutDevice()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1).Set("P01", "177"));

            var all = await harness.RunOneAsync(Xtc3Command.QueryGeneral(Xtc3Parameters.AllId));

            Assert.Equal(16, all.Values.Count);
            Assert.Equal(new[] { 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 4, 0 }, all.Values.Select(v => v.Integer));

            var values = all.Values.Select(v => v.Integer).ToArray();
            values[13] = 30;
            values[9] = 2;
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.UpdateGeneralAll(values))).ToString());
            Assert.Equal("30", harness.Device.Get("G0E04"));
            Assert.Equal("ACK [Int 2]", (await harness.RunOneAsync(Xtc3Command.QueryGeneral(9))).ToString());
        }

        [Fact]
        public async Task General_BaudRate_LocalAndPendingForHostPort_ProtocolOnlyStandard()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1));

            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.UpdateGeneral(14, 0))).ToString());
            Assert.Equal(9600, harness.Engine.TakePendingBaudRate());
            Assert.Equal(9600, harness.Engine.HostBaudRate(115200));
            Assert.Equal("ACK [Int 0]", (await harness.RunOneAsync(Xtc3Command.QueryGeneral(14))).ToString());
            Assert.Equal("B", (await harness.RunOneAsync(Xtc3Command.UpdateGeneral(15, 1))).ToString());
        }

        // ---------------------------------------------------------------- 基础命令、离线

        [Fact]
        public async Task EchoAndHello()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create());

            var results = await harness.RunAsync(Xtc3Command.Echo("abc"), Xtc3Command.Hello(1), Xtc3Command.Hello(2), Xtc3Command.Hello(3));

            Assert.Equal(new[] { "ACK [String \"abc\"]", "ACK [String \"XTC/3S Version 1.00\"]", "ACK [Int 1, Int 83, Int 0, Int 0]", "ACK [Float 1]" },
                results.Select(r => r.ToString()));
        }

        [Fact]
        public async Task Offline_DeviceCommandsE_LocalAndBasicCommandsAnswer()
        {
            var harness = new BridgeHarness(BridgeTestDevice.Create(1));

            var results = await harness.RunAsync(Xtc3Command.Hello(1));
            var device = await harness.RunAsync(Xtc3Command.Status(2));
            var film = await harness.RunAsync(Xtc3Command.QueryFilm(8, 1));
            var local = await harness.RunAsync(Xtc3Command.QueryGeneral(9));

            Assert.True(results[0].IsSuccess);
            Assert.Equal("E", device[0].ToString());
            Assert.Equal("E", film[0].ToString());
            Assert.Equal("ACK [Int 0]", local[0].ToString());
            Assert.Empty(harness.Device.Commands);
        }

        // ---------------------------------------------------------------- 采样和状态

        [Fact]
        public async Task Status_FromLatestSample()
        {
            var device = BridgeTestDevice.Create(1).Set("C11", "42.50").Set("C13", "2.0000").Set("C15", "1.2340").Set("C16", "5980000.0000")
                .Set("C09", "Deposit").Set("P01", "177").Set("O0205", "1").Set("I0104", "1").Set("I0804", "1").Set("C17", "12");
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            var results = await harness.RunAsync(
                Xtc3Command.Status(2), Xtc3Command.Status(3), Xtc3Command.Status(4), Xtc3Command.Status(7), Xtc3Command.Status(8),
                Xtc3Command.Status(9), Xtc3Command.Status(10), Xtc3Command.Status(11), Xtc3Command.Status(12), Xtc3Command.Status(13),
                Xtc3Command.Status(14), Xtc3Command.Status(18), Xtc3Command.Status(34));

            Assert.Equal(new[]
            {
                "ACK [Float 42.5]", "ACK [Float 1.234]", "ACK [Byte 7]", "ACK [Byte 1]", "ACK [Byte 1]",
                "ACK [Byte 12]", "ACK [Byte 1]", "ACK [Int 2]", "ACK [Word 0x8100]", "ACK [Int " + BridgeEngine.DdsUnits(5980000) + "]",
                "ACK [Byte 0]", "ACK [Byte 0]", "ACK [Float 2]",
            }, results.Select(r => r.ToString()));
            Assert.Contains(device.Frames, f => f.StartsWith("SC11;SC12;SC13;SC15;SC16;SN0120;QC09;QC18;QC1F;QN0109;QC17;QC20;QC21;QP01;QC1C;QO0105", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Status_NoFreshSample_E()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1));
            Assert.Equal("E", (await harness.RunOneAsync(Xtc3Command.Status(2))).ToString());

            await harness.SampleAsync();
            harness.Now += TimeSpan.FromSeconds(3);

            Assert.Equal("E", (await harness.RunOneAsync(Xtc3Command.Status(2))).ToString());
        }

        [Fact]
        public async Task ProcessInformation_ByTick_RateOnlyWhileDepositing()
        {
            var device = BridgeTestDevice.Create(1).Set("C13", "3.0000").Set("C15", "0.5000").Set("C11", "20.00").Set("N0120", "-0.5");
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();
            var readyTick = harness.Engine.Timer;
            device.Set("C09", "Deposit");
            await harness.SampleAsync();
            var depositTick = harness.Engine.Timer;

            var results = await harness.RunAsync(Xtc3Command.ProcessInformation(readyTick), Xtc3Command.ProcessInformation(depositTick));

            Assert.Equal("ACK [Byte 0, Byte 0, Byte 1, Byte 1, Float 0, Float 0.5, Float 20, Float 0, Int " + BridgeEngine.DdsUnits(5980000) + "]", results[0].ToString());
            Assert.Equal("ACK [Byte 0, Byte 7, Byte 1, Byte 1, Float 3, Float 0.5, Float 20, Float -0.5, Int " + BridgeEngine.DdsUnits(5980000) + "]", results[1].ToString());
            Assert.Equal("E", (await harness.RunOneAsync(Xtc3Command.ProcessInformation(unchecked((byte)(depositTick + 1))))).ToString());
        }

        [Fact]
        public async Task ProcessInformation_CurrentTickNotSampledYet_SamplesOnDemand()
        {
            var device = BridgeTestDevice.Create(1).Set("C11", "20.00");
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();
            var sampled = harness.Engine.Timer;
            harness.Now += Xtc3Protocol.TickInterval; // 新的 tick 开始,这个 tick 的采样还没有结果
            device.Set("C11", "30.00");
            var before = harness.Sent("SC11").Count;

            var current = await harness.RunOneAsync(Xtc3Command.ProcessInformation(harness.Engine.Timer));
            var previous = await harness.RunOneAsync(Xtc3Command.ProcessInformation(sampled));

            Assert.Equal(30f, current.Values[6].Float); // 功率:当场采样的值
            Assert.Equal(20f, previous.Values[6].Float);
            Assert.Equal(before + 1, harness.Sent("SC11").Count); // 只采样一次;之前的 tick 不重新采样
        }

        [Fact]
        public async Task ProcessInformation_OlderThan256Ticks_E()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1));
            await harness.SampleAsync();
            var tick = harness.Engine.Timer;

            // 257 个 tick 之后,同一个 Timer 指的是上一个 tick (没有采样);缓冲中 257 个 tick 之前的采样不能当作它。
            // (正好 256 个 tick 时,Timer 就是当前 tick,当场采样后回答。)
            harness.Now += TimeSpan.FromSeconds(64) + Xtc3Protocol.TickInterval;

            Assert.Equal("E", (await harness.RunOneAsync(Xtc3Command.ProcessInformation(tick))).ToString());
        }

        [Fact]
        public async Task Status_RateAverages_OneSecondAndRolling()
        {
            var device = BridgeTestDevice.Create(1);
            var harness = await BridgeHarness.ReadyAsync(device);
            for (var i = 1; i <= 8; i++)
            {
                device.Set("C13", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await harness.SampleAsync();
            }

            var results = await harness.RunAsync(Xtc3Command.Status(1), Xtc3Command.Status(31));

            Assert.Equal(new[] { "ACK [Float 6.5]", "ACK [Float 4.5]" }, results.Select(r => r.ToString())); // (5+6+7+8)/4,(1+…+8)/8
        }

        [Fact]
        public async Task Status_StateHistory_LayerCountEndOfProcessStateTime()
        {
            var device = BridgeTestDevice.Create(1);
            var harness = await BridgeHarness.ReadyAsync(device);
            async Task<string> Run(int id) => (await harness.RunOneAsync(Xtc3Command.Status(id))).ToString();

            await harness.SampleAsync();
            device.Set("C09", "Rise 1");
            await harness.SampleAsync();
            device.Set("C09", "Deposit");
            await harness.SampleAsync();
            harness.Now += TimeSpan.FromSeconds(2);
            await harness.SampleAsync();
            Assert.Equal("ACK [Int 2]", await Run(5));
            Assert.Equal("ACK [Byte 0]", await Run(17));

            device.Set("C09", "Idle");
            await harness.SampleAsync();
            Assert.Equal("ACK [Byte 1]", await Run(17));
            Assert.Equal("ACK [Int 1]", await Run(6));
            Assert.NotEqual(0, (await harness.RunOneAsync(Xtc3Command.Status(33))).Values[0].Integer & unchecked((int)Xtc3StatusMessages.EndOfProcess));

            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(0))).ToString());
            device.Set("C09", "Rise 1");
            await harness.SampleAsync();
            Assert.Equal("ACK [Byte 0]", await Run(17));
            Assert.Equal("ACK [Int 2]", await Run(6));
        }

        [Fact]
        public async Task Status_UnknownStateText_E_LoggedOnce()
        {
            var device = BridgeTestDevice.Create(1).Set("C09", "Pre-Conditioning");
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();
            await harness.SampleAsync();

            Assert.Equal("E", (await harness.RunOneAsync(Xtc3Command.Status(4))).ToString());
            Assert.Single(harness.Logs, l => l.Contains("Pre-Conditioning"));
        }

        [Fact]
        public async Task Status_StopAndMaxPowerAndCrystalFail()
        {
            var device = BridgeTestDevice.Create(1).Set("P01", "177").Set("C1F", "6").Set("C11", "80.00").Set("C18", "1").Set("C1C", "1");
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            var results = await harness.RunAsync(Xtc3Command.Status(14), Xtc3Command.Status(15), Xtc3Command.Status(18), Xtc3Command.Status(33));

            Assert.Equal(new[] { "ACK [Byte 1]", "ACK [Byte 1]", "ACK [Byte 1]" }, results.Take(3).Select(r => r.ToString()));
            var expected = Xtc3StatusMessages.MaxPower | Xtc3StatusMessages.CrystalFail | Xtc3StatusMessages.Test;
            Assert.Equal(unchecked((int)expected), results[3].Values[0].Integer);
        }

        [Fact]
        public async Task Status_CrystalStatesAndEthernet_QueriedOnDemand()
        {
            var device = BridgeTestDevice.Create(1).Set("N0110FE", "2").With("N01100202", "0", "QCM Health");
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            var results = await harness.RunAsync(Xtc3Command.Status(30), Xtc3Command.Status(35), Xtc3Command.Status(19));

            Assert.Equal("ACK [Word 0x0001]", results[0].ToString());
            Assert.Equal("ACK [Bytes C0-A8-05-58-FF-FF-FF-00]", results[1].ToString());
            Assert.Equal("E", results[2].ToString()); // 数据日志未实现
        }

        [Fact]
        public async Task Status_PowerCycleFlag_SetWhenPowerOnCountGrows_ClearedByR22()
        {
            var store = new MemoryBridgeStateStore();
            await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1), store: store);
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1).Set("G0C", "244"), store: store);

            Assert.Equal("ACK [Byte 4]", (await harness.RunOneAsync(Xtc3Command.Status(21))).ToString());
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(22))).ToString());
            Assert.Equal("ACK [Byte 0]", (await harness.RunOneAsync(Xtc3Command.Status(21))).ToString());
        }

        // ---------------------------------------------------------------- 远程动作

        [Fact]
        public async Task Remote_StartStopReset()
        {
            var device = BridgeTestDevice.Create(1);
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(0))).ToString());
            Assert.Equal("1", device.Get("C01"));
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(1))).ToString());
            Assert.Equal("0", device.Get("C01"));
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(2))).ToString()); // 已是 Ready:不需要复位
            Assert.DoesNotContain("RC04", device.Commands);

            device.Set("C09", "Dep Rate Hold 1");
            await harness.SampleAsync();
            Assert.Equal("F", (await harness.RunOneAsync(Xtc3Command.Remote(2))).ToString()); // 运行中

            device.Set("C09", "Stopped");
            await harness.SampleAsync();
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(2))).ToString());
            Assert.Contains("RC04", device.Commands);
        }

        [Theory]
        [InlineData("Ready", Xtc3State.Ready)]
        [InlineData("Precon Rise 1", Xtc3State.Rise1)]
        [InlineData("Precon Soak 1", Xtc3State.Soak1)]
        [InlineData("Precon Rise 2", Xtc3State.Rise2)]
        [InlineData("Precon Soak 2", Xtc3State.Soak2)]
        [InlineData("Precon Rise 3", Xtc3State.Rise2)]
        [InlineData("Dep Rate Hold 1", Xtc3State.Deposit)]
        [InlineData("Dep Rate Ramp 1", Xtc3State.RateRamp)]
        [InlineData("Dep Manual 1", Xtc3State.Manual)]
        [InlineData("Feed Ramp 1", Xtc3State.IdleRamp)]
        [InlineData("Feed Hold 1", Xtc3State.IdleRamp)]
        [InlineData("Idle", Xtc3State.Idle)]
        [InlineData("Shutter Delay", Xtc3State.ShutterDelay)]
        [InlineData("Idle Ramp", Xtc3State.IdleRamp)]
        public void DeviceStates_RealImc300Texts(string text, Xtc3State expected)
        {
            Assert.True(DeviceStates.TryMap(text, out var state), text);
            Assert.Equal(expected, state);
        }

        [Fact]
        public void DeviceStates_Stopped_IsNotAStateButAStop()
        {
            Assert.True(DeviceStates.IsStopped("Stopped"));
            Assert.False(DeviceStates.TryMap("Stopped", out _));
            Assert.False(DeviceStates.TryMap("Something new", out _));
        }

        [Fact]
        public async Task Status_StoppedText_KeepsStateBeforeStop_S18()
        {
            var device = BridgeTestDevice.Create(1).Set("C09", "Dep Rate Hold 1");
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();
            device.Set("C09", "Stopped");
            await harness.SampleAsync();

            var results = await harness.RunAsync(Xtc3Command.Status(4), Xtc3Command.Status(18), Xtc3Command.Status(17));

            Assert.Equal(new[] { "ACK [Byte 7]", "ACK [Byte 1]", "ACK [Byte 0]" }, results.Select(r => r.ToString()));
            Assert.DoesNotContain(harness.Logs, l => l.Contains("Stopped"));
        }

        /// <summary>
        /// 模拟实测的 IMC300:Idle 时 UC01:1 回答 -B,UC01:0 进入 Stopped,RC04 回到 Ready,Ready 时 UC01:1 开始预熔;未运行时 UC01:0 回答 -B。
        /// </summary>
        private static void SimulateProcess(FakeDeviceService device) => device.Intercept = command =>
        {
            var state = device.Get("C09");
            switch (command)
            {
                case "UC01:1":
                    if (state != "Ready")
                    {
                        return "UC01-B";
                    }

                    device.Set("C09", "Precon Rise 1");
                    return "UC01+";
                case "UC01:0":
                    if (state == "Ready" || state == "Stopped")
                    {
                        return "UC01-B";
                    }

                    device.Set("C09", "Stopped");
                    return "UC01+";
                case "RC04":
                    if (state != "Stopped")
                    {
                        return "RC04-F";
                    }

                    device.Set("C09", "Ready");
                    return "RC04+";
                default:
                    return null;
            }
        };

        [Fact]
        public async Task Remote_StartAfterLayerEnded_StopsResetsStarts_NextLayer()
        {
            var device = BridgeTestDevice.Create(1).Set("P01", "177");
            SimulateProcess(device);
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(0))).ToString());
            await harness.SampleAsync();
            device.Set("C09", "Dep Rate Hold 1");
            await harness.SampleAsync();
            device.Set("C09", "Idle");
            await harness.SampleAsync();
            Assert.Equal("ACK [Byte 1]", (await harness.RunOneAsync(Xtc3Command.Status(17))).ToString());
            var before = device.Commands.Count;

            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(0))).ToString());

            // 查询 (Q) 和采样 (S) 之外发出的指令
            var sent = device.Commands.Skip(before).Where(c => !c.StartsWith("Q", StringComparison.Ordinal) && !c.StartsWith("S", StringComparison.Ordinal)).ToList();
            Assert.Equal(new[] { "UC01:1", "UC01:0", "RC04", "UC01:1" }, sent);
            Assert.Equal("Precon Rise 1", device.Get("C09"));

            // R0 之后立即采样,层计数在启动时加 1:不等下一个 tick (实测时主机在 R0 之后立即查询,看到的是复位过程中的 S18 = 1)。
            var results = await harness.RunAsync(Xtc3Command.Status(6), Xtc3Command.Status(17), Xtc3Command.Status(4), Xtc3Command.Status(18));
            Assert.Equal(new[] { "ACK [Int 2]", "ACK [Byte 0]", "ACK [Byte 2]", "ACK [Byte 0]" }, results.Select(r => r.ToString()));

            // 设备开始运行后的采样不再加 1;复位后回到第 1 层。
            await harness.SampleAsync();
            device.Set("C09", "Dep Rate Hold 1");
            await harness.SampleAsync();
            Assert.Equal("ACK [Int 2]", (await harness.RunOneAsync(Xtc3Command.Status(6))).ToString());
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(1))).ToString());
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(2))).ToString());
            Assert.Equal("ACK [Int 1]", (await harness.RunOneAsync(Xtc3Command.Status(6))).ToString());
        }

        [Fact]
        public async Task Remote_StopResetStart_StatusIsFreshRightAfterwards()
        {
            var device = BridgeTestDevice.Create(1).Set("C09", "Dep Rate Hold 1");
            SimulateProcess(device);
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();
            async Task<string[]> StateAndStop() =>
                (await harness.RunAsync(Xtc3Command.Status(4), Xtc3Command.Status(18))).Select(r => r.ToString()).ToArray();
            string Byte(Xtc3State state) => "ACK [Byte " + (int)state + "]";

            // 每个动作之后不经 harness.SampleAsync,状态已是设备的新状态。
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(1))).ToString());
            Assert.Equal(new[] { Byte(Xtc3State.Deposit), "ACK [Byte 1]" }, await StateAndStop()); // 停止:S4 保持停止前的状态

            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(2))).ToString());
            Assert.Equal(new[] { Byte(Xtc3State.Ready), "ACK [Byte 0]" }, await StateAndStop());

            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(0))).ToString());
            Assert.Equal(new[] { Byte(Xtc3State.Rise1), "ACK [Byte 0]" }, await StateAndStop());
            Assert.Equal("ACK [Int 1]", (await harness.RunOneAsync(Xtc3Command.Status(6))).ToString()); // 从 Ready 启动:仍是第 1 层
        }

        [Fact]
        public async Task Remote_StopWhenAlreadyStopped_Ack_ResetFromIdle_StopsThenResets()
        {
            var device = BridgeTestDevice.Create(1).Set("C09", "Idle");
            SimulateProcess(device);
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(2))).ToString());
            Assert.Equal("Ready", device.Get("C09"));
            await harness.SampleAsync();
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(1))).ToString()); // 设备回答 -B:已经停止
            Assert.Equal("ACK [Int 1]", (await harness.RunOneAsync(Xtc3Command.Status(6))).ToString());
        }

        [Fact]
        public async Task Remote_StateReadWhenNeeded_StartThenResetInOnePacket_F()
        {
            var device = BridgeTestDevice.Create(1).Set("C09", "Idle");
            SimulateProcess(device);
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            // 采样仍是 Idle;R2 按当场读取的状态 (已开始预熔) 回答 F,不再停止、复位。
            var results = await harness.RunAsync(Xtc3Command.Remote(0), Xtc3Command.Remote(2));

            Assert.Equal(new[] { "ACK", "F" }, results.Select(r => r.ToString()));
            Assert.Equal("Precon Rise 1", device.Get("C09"));
        }

        [Fact]
        public async Task Remote_ManualThenSetPowerInOnePacket()
        {
            var device = BridgeTestDevice.Create(1).Set("C09", "Dep Rate Hold 1");
            device.Intercept = command =>
            {
                if (command == "UC06:1")
                {
                    device.Set("C09", "Dep Manual 1");
                }

                return null;
            };
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            var results = await harness.RunAsync(Xtc3Command.Remote(9), Xtc3Command.Remote(11, Xtc3Value.FromFloat(20f)));

            Assert.Equal(new[] { "ACK", "ACK" }, results.Select(r => r.ToString()));
            Assert.Equal("20.00", device.Get("C07"));
        }

        [Fact]
        public async Task Write_SameValueAsDevice_NotSent()
        {
            var device = BridgeTestDevice.Create(1);
            device.RejectWrites.Add("FB1070101"); // 实机对写入相同的传感器编号回答 -B
            var harness = await BridgeHarness.ReadyAsync(device);

            var results = await harness.RunAsync(Xtc3Command.UpdateFilm(15, 1, Xtc3Value.FromInteger(1)), Xtc3Command.UpdateFilm(22, 1, Xtc3Value.FromInteger(1)));

            Assert.Equal(new[] { "ACK", "ACK" }, results.Select(r => r.ToString()));
            Assert.DoesNotContain(device.Commands, c => c.StartsWith("U", StringComparison.Ordinal));
        }

        [Fact]
        public async Task ActiveProcessNotShadow_FilmToRunAndActiveFilmAreZero()
        {
            var device = BridgeTestDevice.Create(1).Set("P01", "0");
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            var results = await harness.RunAsync(Xtc3Command.QueryGeneral(0), Xtc3Command.Status(7), Xtc3Command.QueryGeneral(255));

            Assert.Equal("ACK [Int 0]", results[0].ToString());
            Assert.Equal("ACK [Byte 0]", results[1].ToString());
            Assert.True(results[2].IsSuccess);
        }

        [Fact]
        public async Task Remote_DeviceActions()
        {
            var device = BridgeTestDevice.Create(1);
            var harness = await BridgeHarness.ReadyAsync(device);

            foreach (var id in new[] { 9, 12, 13, 14, 19, 20, 21, 26, 28, 10 })
            {
                Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(id))).ToString());
            }

            Assert.Equal(new[] { "RC03", "RC1E", "RN0118", "RG16:1", "RN011B", "RC05" }, harness.Sent("R"));
            Assert.Equal(("0", "0"), (device.Get("C06"), device.Get("G0E01")));
        }

        [Fact]
        public async Task Remote_SetPower_OnlyInManual()
        {
            var device = BridgeTestDevice.Create(1);
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            Assert.Equal("F", (await harness.RunOneAsync(Xtc3Command.Remote(11, Xtc3Value.FromFloat(50f)))).ToString());
            device.Set("C09", "Manual");
            await harness.SampleAsync();
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(11, Xtc3Value.FromFloat(50.004f)))).ToString());
            Assert.Equal("50.00", device.Get("C07"));
            Assert.Equal("B", (await harness.RunOneAsync(Xtc3Command.Remote(11, Xtc3Value.FromFloat(101f)))).ToString());
        }

        [Fact]
        public async Task Remote_Unsupported_AndCommIoMode()
        {
            var harness = await BridgeHarness.ReadyAsync(BridgeTestDevice.Create(1));

            foreach (var id in new[] { 5, 6, 7, 8, 15, 16 })
            {
                Assert.Equal("A", (await harness.RunOneAsync(Xtc3Command.Remote(id))).ToString());
            }

            Assert.Equal("F", (await harness.RunOneAsync(Xtc3Command.Remote(17, Xtc3Value.FromByte(3)))).ToString());
            Assert.Equal("B", (await harness.RunOneAsync(Xtc3Command.Remote(18, Xtc3Value.FromByte(21)))).ToString());
        }

        [Fact]
        public async Task Remote_DeviceRejection_RotateHeadA_RemoteLockFlag()
        {
            var device = BridgeTestDevice.Create(1);
            device.RejectRemotes["RN0119"] = 'A';
            var harness = await BridgeHarness.ReadyAsync(device);
            await harness.SampleAsync();

            Assert.Equal("A", (await harness.RunOneAsync(Xtc3Command.Remote(27))).ToString());
            Assert.Equal("ACK", (await harness.RunOneAsync(Xtc3Command.Remote(3))).ToString());
            Assert.Equal("1", device.Get("G0E02"));
            var messages = (await harness.RunOneAsync(Xtc3Command.Status(33))).Values[0].Integer;
            Assert.NotEqual(0, messages & (int)Xtc3StatusMessages.RemoteLock);
        }
    }
}
