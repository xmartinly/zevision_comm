using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Zevision.Bridge;
using Zevision.Bridge.Hosting;
using Zevision.Communication;
using Zevision.Xtc3;

namespace Zevision.Tests
{
    public sealed class Xtc3BridgeHostTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "zevision-host-" + Guid.NewGuid().ToString("N"));

        public Xtc3BridgeHostTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose() => Directory.Delete(_folder, true);

        [Fact]
        public void Settings_FileWithBomAndMissingFields_DefaultsAndBaseFolder()
        {
            var path = Path.Combine(_folder, Xtc3BridgeSettings.DefaultFileName);
            File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{\"device\":\"192.168.5.88\"}")).ToArray());

            var settings = Xtc3BridgeSettings.Load(path);

            Assert.Equal(("COM2", 115200, "192.168.5.88", "B0", false), (settings.HostPort, settings.HostBaudRate, settings.Device, settings.ShadowBase, settings.CreateShadowObjects));
            Assert.Equal(Path.GetFullPath(_folder), Path.GetFullPath(settings.BaseFolder));
            Assert.Equal(Path.Combine(Path.GetFullPath(_folder), "xtc3bridge.json"), settings.Resolve(settings.StateFile));
            Assert.Equal(string.Empty, settings.Language);
        }

        [Fact]
        public void Settings_SaveAndLoad_RoundTrip()
        {
            var path = Path.Combine(_folder, "s.json");
            new Xtc3BridgeSettings { HostPort = "COM7", HostBaudRate = 9600, Device = "COM3:57600", ShadowBase = "C0", CreateShadowObjects = true, Language = "zh" }.Save(path);

            var loaded = Xtc3BridgeSettings.Load(path);

            Assert.Equal(("COM7", 9600, "COM3:57600", "C0", true, "zh"), (loaded.HostPort, loaded.HostBaudRate, loaded.Device, loaded.ShadowBase, loaded.CreateShadowObjects, loaded.Language));
            Assert.Contains("\"hostPort\": \"COM7\"", File.ReadAllText(path).Replace("\":\"", "\": \""));
        }

        [Fact]
        public void Settings_InvalidJson_IOExceptionWithPath()
        {
            var path = Path.Combine(_folder, "bad.json");
            File.WriteAllText(path, "{ hostPort: ");

            var error = Assert.Throws<IOException>(() => Xtc3BridgeSettings.Load(path));

            Assert.Contains("bad.json", error.Message);
        }

        [Fact]
        public void ToOptions_DeviceKinds()
        {
            var tcp = new Xtc3BridgeSettings { Device = "192.168.5.88" }.ToOptions();
            var serial = new Xtc3BridgeSettings { Device = "com3:9600", HostPort = "com2", ShadowBase = "c0" }.ToOptions();

            Assert.Equal("192.168.5.88:2101", tcp.Device.Endpoint.ToString());
            Assert.Equal(2, tcp.Device.RetryCount);
            Assert.Equal("COM3, 9600, 8N1", serial.Device.Endpoint.ToString());
            Assert.Equal("COM2, 115200, 8N1", serial.HostPort!.ToString());
            Assert.Equal(0xC0, serial.ShadowBase);
        }

        [Theory]
        [InlineData("", "COM2", "B0")]
        [InlineData("COM3:fast", "COM2", "B0")]
        [InlineData("192.168.5.88", "", "B0")]
        [InlineData("192.168.5.88", "COM2", "ZZ")]
        [InlineData("192.168.5.88", "COM2", "F8")]
        public void ToOptions_Invalid_ArgumentException(string device, string hostPort, string shadowBase)
        {
            Assert.Throws<ArgumentException>(() => new Xtc3BridgeSettings { Device = device, HostPort = hostPort, ShadowBase = shadowBase }.ToOptions());
        }

        [Fact]
        public async Task Start_OpensHostPort_AnswersWhileDeviceUnreachable_DisposeStops()
        {
            var hosts = new List<FakeSerialTransport>();
            var logs = new List<string>();
            var settings = new Xtc3BridgeSettings
            {
                HostPort = "COM2",
                Device = "192.168.5.88",
                StateFile = Path.Combine(_folder, "state.json"),
                LogFolder = Path.Combine(_folder, "logs"),
            };

            using (var host = Xtc3BridgeHost.Start(
                settings,
                (_, e) =>
                {
                    lock (logs)
                    {
                        logs.Add(e.ToString());
                    }
                },
                _ => new FakeSerialTransport { OpenException = new IOException("no device") },
                _ =>
                {
                    var transport = new FakeSerialTransport();
                    lock (hosts)
                    {
                        hosts.Add(transport);
                    }

                    return transport;
                }))
            {
                await WaitUntilAsync(() => host.Bridge.IsHostPortOpen && Snapshot(logs).Any(l => l.Contains("no device")));
                var hostPort = Snapshot(hosts).Single();
                var commands = new[] { Xtc3Command.Hello(1), Xtc3Command.Status(2) };
                hostPort.Inject(Xtc3HostCodec.EncodeCommands(commands));
                await WaitUntilAsync(() => hostPort.WrittenSnapshot().Count == 1);

                var response = hostPort.WrittenSnapshot()[0];
                var message = new Xtc3PacketDecoder().Push(response, response.Length, TimeSpan.Zero).Single().Message;
                Assert.Equal(new[] { "ACK [String \"XTC/3S Version 1.00\"]", "E" }, Xtc3HostCodec.DecodeResponse(message, commands).Results.Select(r => r.ToString()));
                Assert.Equal(Path.Combine(_folder, "logs", Xtc3BridgeHost.ErrorLogFileName), host.ErrorLogPath);
                Assert.Equal(Path.Combine(_folder, "state.json"), host.StateFilePath);
            }

            Assert.False(Snapshot(hosts).Single().IsOpen);
            Assert.Contains("no device", File.ReadAllText(Path.Combine(_folder, "logs", Xtc3BridgeHost.ErrorLogFileName)));
        }

        private static List<T> Snapshot<T>(List<T> list)
        {
            lock (list)
            {
                return list.ToList();
            }
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("The condition was not met in 10 s.");
                }

                await Task.Delay(20);
            }
        }
    }
}
