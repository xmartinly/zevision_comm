using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Zevision.Bridge;
using Zevision.Communication;
using Zevision.Core;
using Zevision.Xtc3;

namespace Zevision.Tests
{
    /// <summary>
    /// 端到端:主机侧串口用 <see cref="FakeSerialTransport"/> (测试写入主机的命令包,读取中间层的响应),设备用 <see cref="BridgeTestDevice"/>,
    /// 中间层的后台连接、采样和串口循环都在运行。
    /// </summary>
    public class Xtc3BridgeTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task HostPacket_AnsweredOverTheHostPort()
        {
            using var run = await Running.StartAsync(BridgeTestDevice.Create(1).Set("LB1020102", "4.500"));

            var response = await run.SendAsync(Xtc3Command.Hello(1), Xtc3Command.QueryFilm(8, 1), Xtc3Command.Status(4), Xtc3Command.Status(22));

            Assert.Equal(0, response.ConditionCode);
            Assert.Equal(new[] { "ACK [String \"XTC/3S Version 1.00\"]", "ACK [Float 4.5]", "ACK [Byte 0]", "C" }, response.Results.Select(r => r.ToString()));
            Assert.Equal("COM2, 115200, 8N1", Assert.Single(run.Endpoints).ToString());
        }

        [Fact]
        public async Task TimerAdvancesEvery250Milliseconds()
        {
            using var run = await Running.StartAsync(BridgeTestDevice.Create(1));

            // 范围按实际经过的时间算:机器繁忙时 Task.Delay(600) 可能明显超时 (2026-10-23 一次为 5 个 tick)。
            var first = await run.SendAsync(Xtc3Command.Status(4));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(600);
            var atLeast = clock.Elapsed;
            var second = await run.SendAsync(Xtc3Command.Status(4));
            var atMost = clock.Elapsed;

            var ticks = (byte)(second.Timer - first.Timer);
            Assert.InRange(ticks, (int)(atLeast.TotalMilliseconds / 250) - 1, (int)(atMost.TotalMilliseconds / 250) + 2);
            Assert.True(ticks >= 1);
        }

        [Fact]
        public async Task BadChecksum_PacketErrorResponse()
        {
            using var run = await Running.StartAsync(BridgeTestDevice.Create(1));
            var packet = Xtc3HostCodec.EncodeCommands(new[] { Xtc3Command.Status(4) });
            packet[packet.Length - 1] ^= 0xFF;

            var response = await run.SendRawAsync(packet, Array.Empty<Xtc3Command>());

            Assert.Equal(Xtc3Protocol.PacketErrorFlag, response.ConditionCode);
            Assert.Equal(Xtc3PacketError.InvalidChecksum, response.PacketError);
        }

        [Fact]
        public async Task BaudRateChange_AnsweredAtOldRate_ThenPortReopened()
        {
            using var run = await Running.StartAsync(BridgeTestDevice.Create(1));

            var response = await run.SendAsync(Xtc3Command.UpdateGeneral(14, 2));
            await WaitUntilAsync(() => run.Endpoints.Count == 2 && run.Bridge.IsHostPortOpen);

            Assert.Equal("ACK", response.Results[0].ToString());
            Assert.Equal(38400, run.Endpoints[1].BaudRate);
            Assert.True(run.Transports[0].Written.Count == 1 && !run.Transports[0].IsOpen);
            Assert.Equal("ACK [Int 2]", (await run.SendAsync(Xtc3Command.QueryGeneral(14))).Results[0].ToString());
        }

        [Fact]
        public async Task DeviceNotConnected_DeviceCommandsE_HelloAnswered()
        {
            var device = BridgeTestDevice.Create(1);
            device.ConnectResult = _ => Result.Fail("no route");
            using var run = await Running.StartAsync(device, waitForDevice: false);

            var response = await run.SendAsync(Xtc3Command.Hello(1), Xtc3Command.Status(2));

            Assert.Equal(new[] { "ACK [String \"XTC/3S Version 1.00\"]", "E" }, response.Results.Select(r => r.ToString()));
            Assert.Contains(run.Logs, l => l.Contains("no route"));
        }

        [Fact]
        public async Task ConnectionLost_ThenReconnected()
        {
            var device = BridgeTestDevice.Create(1);
            using var run = await Running.StartAsync(device);

            device.RaiseConnectionLost(new ConnectionLostEventArgs(new IOException("cable"), ConnectionLostReason.ReadFailed));
            Assert.False(run.Bridge.IsDeviceOnline);
            Assert.Equal("E", (await run.SendAsync(Xtc3Command.QueryFilm(8, 1))).Results[0].ToString());

            await WaitUntilAsync(() => run.Bridge.IsDeviceOnline && device.Connections.Count == 2);
            await WaitUntilAsync(() => run.Bridge.Engine.ShadowChecks[0].Status == ShadowStatus.Ready);
            Assert.Equal("ACK [Float 0]", (await run.SendAsync(Xtc3Command.QueryFilm(8, 1))).Results[0].ToString());
        }

        [Fact]
        public async Task SamplingRunsInTheBackground()
        {
            var device = BridgeTestDevice.Create(1).Set("C11", "33.30");
            using var run = await Running.StartAsync(device);

            await WaitUntilAsync(() => device.Frames.Count(f => f.StartsWith("SC11", StringComparison.Ordinal)) >= 2);

            Assert.Equal("ACK [Float 33.3]", (await run.SendAsync(Xtc3Command.Status(2))).Results[0].ToString());
        }

        [Fact]
        public async Task HostExchangeEvent_ReportsRequestAndResponse()
        {
            using var run = await Running.StartAsync(BridgeTestDevice.Create(1));
            var exchanges = new List<HostExchangeEventArgs>();
            run.Bridge.HostExchange += (_, e) => exchanges.Add(e);

            await run.SendAsync(Xtc3Command.Echo("x"));

            var exchange = Assert.Single(exchanges);
            Assert.Equal(new byte[] { (byte)'E', (byte)'x', 0 }, exchange.Request);
            Assert.Equal("E [String \"x\"]", Assert.Single(exchange.Commands).ToString());
            Assert.NotNull(exchange.Response);
        }

        [Fact]
        public void Options_InvalidShadowBase_Rejected()
        {
            var options = new Xtc3BridgeOptions(new ConnectionOptions(new TcpSettings("127.0.0.1", TcpSettings.DefaultPort), new FrameOptions(true, true))) { ShadowBase = 0xF8 };

            Assert.Throws<ArgumentException>(() => new Xtc3Bridge(options, BridgeTestDevice.Create(), new MemoryBridgeStateStore()));
        }

        [Fact]
        public void JsonStateStore_RoundTrip_BomAndMissingFields()
        {
            var path = Path.Combine(Path.GetTempPath(), "zevision-bridge-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new JsonFileBridgeStateStore(path);
                Assert.Equal(new int[9], store.Load().GraphLabels);

                var state = new BridgeLocalState { RecorderMode = 2, BaudRate = 1, RemoteLock = true, PowerOnCount = 243 };
                state.GraphScales[8] = 3;
                state.ToolFactor2[0] = 150f;
                store.Save(state);
                store.Save(state);
                var loaded = store.Load();
                Assert.Equal((2, 1, true, 243L, 3, 150f), (loaded.RecorderMode, loaded.BaudRate ?? -1, loaded.RemoteLock, loaded.PowerOnCount ?? 0, loaded.GraphScales[8], loaded.ToolFactor2[0]));

                File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{\"formatVersion\":1,\"graphLabels\":[1,1]}")).ToArray());
                var partial = store.Load();
                Assert.Equal(new[] { 1, 1, 0, 0, 0, 0, 0, 0, 0 }, partial.GraphLabels);
                Assert.Equal(100f, partial.ToolFactor2[8]);
                Assert.Equal(1, partial.RecorderFilter);
                Assert.Null(partial.BaudRate);

                File.WriteAllText(path, "{ not json");
                Assert.Throws<IOException>(() => store.Load());
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("The condition was not met in " + Timeout.TotalSeconds + " s.");
                }

                await Task.Delay(20);
            }
        }

        /// <summary>运行中的中间层和它的主机侧串口。</summary>
        private sealed class Running : IDisposable
        {
            private readonly object _sync = new object();

            private Running(FakeDeviceService device)
            {
                Device = device;
                var options = new Xtc3BridgeOptions(new ConnectionOptions(new TcpSettings("127.0.0.1", TcpSettings.DefaultPort), new FrameOptions(true, true)))
                {
                    HostPort = new SerialSettings("COM2", 115200),
                };
                Bridge = new Xtc3Bridge(options, device, new MemoryBridgeStateStore(), null, endpoint =>
                {
                    var transport = new FakeSerialTransport();
                    lock (_sync)
                    {
                        Endpoints.Add((SerialSettings)endpoint);
                        Transports.Add(transport);
                    }

                    return transport;
                });
                Bridge.Log += (_, e) =>
                {
                    lock (Logs)
                    {
                        Logs.Add(e.ToString());
                    }
                };
            }

            public FakeDeviceService Device { get; }

            public Xtc3Bridge Bridge { get; }

            public List<SerialSettings> Endpoints { get; } = new List<SerialSettings>();

            public List<FakeSerialTransport> Transports { get; } = new List<FakeSerialTransport>();

            public List<string> Logs { get; } = new List<string>();

            private FakeSerialTransport Host
            {
                get
                {
                    lock (_sync)
                    {
                        return Transports.Last();
                    }
                }
            }

            public static async Task<Running> StartAsync(FakeDeviceService device, bool waitForDevice = true)
            {
                var run = new Running(device);
                await run.Bridge.StartAsync();
                // 等到主机侧串口打开、设备就绪并已采样 (由采样得到的状态有数据)。
                await WaitUntilAsync(() => run.Bridge.IsHostPortOpen
                    && (!waitForDevice || (run.Bridge.IsDeviceOnline && device.CommandsSnapshot().Contains("SC11"))));
                return run;
            }

            public Task<Xtc3ResponsePacket> SendAsync(params Xtc3Command[] commands) => SendRawAsync(Xtc3HostCodec.EncodeCommands(commands), commands);

            /// <summary>写入主机的字节,等待一个响应包并按 <paramref name="commands"/> 解析。</summary>
            public async Task<Xtc3ResponsePacket> SendRawAsync(byte[] packet, IReadOnlyList<Xtc3Command> commands)
            {
                var host = Host;
                var before = host.WrittenSnapshot().Count;
                host.Inject(packet);
                await WaitUntilAsync(() => host.WrittenSnapshot().Count > before);
                var response = host.WrittenSnapshot()[before];
                var message = Assert.Single(new Xtc3PacketDecoder().Push(response, response.Length, TimeSpan.Zero)).Message;
                return Xtc3HostCodec.DecodeResponse(message, commands);
            }

            public void Dispose() => Bridge.Dispose();
        }
    }
}
