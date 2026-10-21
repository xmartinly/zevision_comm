using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Communication;
using Zevision.Core.Devices;
using Zevision.Core.Localization;

namespace Zevision.Tests
{
    /// <summary>
    /// 握手 (<see cref="ZevisionDeviceService.HandshakeAsync"/>):对模拟实机帧格式行为的设备 (<see cref="SimulatedImc300"/>)
    /// 识别并改用设备的帧格式,读取型号和设置;期间的收发不显示。也覆盖适配层转发重发事件和心跳中断。
    /// </summary>
    public class HandshakeTests
    {
        private static readonly FrameOptions LengthAndChecksum = new FrameOptions(checksumEnabled: true, lengthEnabled: true);
        private static readonly FrameOptions LengthOnly = new FrameOptions(checksumEnabled: false, lengthEnabled: true);
        private static readonly FrameOptions ChecksumOnly = new FrameOptions(checksumEnabled: true, lengthEnabled: false);

        [Fact]
        public async Task Handshake_SameFormat_ReadsIdentity()
        {
            var device = new SimulatedImc300(FrameOptions.Plain);
            using (var service = await ConnectAsync(device, FrameOptions.Plain))
            {
                var identity = await service.HandshakeAsync(FrameOptions.Plain, CancellationToken.None);

                Assert.True(identity.IsSuccess, identity.Error);
                Assert.Equal("Zevision IMC300 1.8.11.202606101229", identity.Value.Hello);
                Assert.Equal("IMC300", identity.Value.Model);
                Assert.Equal("XXXXXXXX", identity.Value.SerialNumber);
                Assert.Equal(FrameOptions.Plain, identity.Value.Frame);
                Assert.Empty(identity.Value.Warnings);
                Assert.Equal(new[] { "H01", "QG03;QG06;QG1304;QG1305" }, device.Commands);
            }
        }

        [Fact]
        public async Task Handshake_NoSerialNumber_SucceedsWithoutIt()
        {
            var device = new SimulatedImc300(FrameOptions.Plain) { SerialNumber = null };
            using (var service = await ConnectAsync(device, FrameOptions.Plain))
            {
                var identity = await service.HandshakeAsync(FrameOptions.Plain, CancellationToken.None);

                Assert.True(identity.IsSuccess, identity.Error);
                Assert.Null(identity.Value.SerialNumber);
                Assert.Empty(identity.Value.Warnings);
            }
        }

        public static TheoryData<FrameOptions, FrameOptions, bool> Mismatches => new TheoryData<FrameOptions, FrameOptions, bool>
        {
            // 设备的格式, 界面上的设置, TCP
            { FrameOptions.Plain, LengthAndChecksum, false }, // 2026-10-19 实际情况:RS232 已关闭两项,程序仍开着
            { FrameOptions.Plain, LengthOnly, false },
            { FrameOptions.Plain, ChecksumOnly, false },
            { LengthOnly, FrameOptions.Plain, false },
            { ChecksumOnly, FrameOptions.Plain, false },
            { ChecksumOnly, LengthAndChecksum, false },
            { LengthAndChecksum, FrameOptions.Plain, true },
            { LengthAndChecksum, LengthOnly, true },
        };

        [Theory]
        [MemberData(nameof(Mismatches))]
        public async Task Handshake_DifferentFormat_DetectsAndSwitches(FrameOptions deviceFrame, FrameOptions configured, bool tcp)
        {
            var device = new SimulatedImc300(deviceFrame, tcp);
            using (var service = await ConnectAsync(device, configured, tcp))
            {
                var identity = await service.HandshakeAsync(configured, CancellationToken.None);

                Assert.True(identity.IsSuccess, identity.Error);
                Assert.Equal(deviceFrame, identity.Value.Frame);
                Assert.Empty(identity.Value.Warnings);

                // 之后的指令按设备的格式收发。
                var reply = await service.SendAsync("QG03", CancellationToken.None);
                Assert.Equal("IMC300", reply.Value.Responses.Single().Data);
            }
        }

        [Fact]
        public async Task Handshake_IsNotDisplayed()
        {
            var device = new SimulatedImc300(FrameOptions.Plain);
            using (var service = await ConnectAsync(device, LengthAndChecksum))
            {
                var events = 0;
                service.FrameSent += (s, e) => Interlocked.Increment(ref events);
                service.FrameReceived += (s, e) => Interlocked.Increment(ref events);

                await service.HandshakeAsync(LengthAndChecksum, CancellationToken.None);
                await Task.Delay(50);

                Assert.Equal(0, Volatile.Read(ref events));
                await service.SendAsync("QG03", CancellationToken.None);
                Assert.Equal(2, Volatile.Read(ref events)); // 握手之后恢复显示
            }
        }

        [Fact]
        public async Task Handshake_NoReply_FailsAfterOneAttempt()
        {
            var device = new SimulatedImc300(FrameOptions.Plain) { Silent = true };
            var transport = new FakeSerialTransport { Responder = device.Respond };
            using (var service = await ConnectAsync(transport, FrameOptions.Plain))
            {
                var identity = await service.HandshakeAsync(FrameOptions.Plain, CancellationToken.None);

                Assert.False(identity.IsSuccess);
                Assert.StartsWith("设备没有应答 H01", identity.Error);
                Assert.Single(transport.WrittenSnapshot());
            }
        }

        [Fact]
        public async Task Handshake_Garbage_TriesEveryFormatThenFails()
        {
            var device = new SimulatedImc300(FrameOptions.Plain) { Garbage = new byte[] { 0xF0, 0x0F, (byte)'x' } };
            var transport = new FakeSerialTransport { Responder = device.Respond };
            using (var service = await ConnectAsync(transport, FrameOptions.Plain))
            {
                var identity = await service.HandshakeAsync(FrameOptions.Plain, CancellationToken.None);

                Assert.False(identity.IsSuccess);
                Assert.Contains("无法识别设备的帧格式 (收到: <F0><0F>x", identity.Error);
                Assert.Equal(4, transport.WrittenSnapshot().Count);
            }
        }

        [Fact]
        public async Task Handshake_OtherModelOrSettings_SucceedsWithWarnings()
        {
            var device = new SimulatedImc300(FrameOptions.Plain) { Model = "IMC200", ReportedSettings = ("1", "1") };
            using (var service = await ConnectAsync(device, FrameOptions.Plain))
            {
                var identity = await service.HandshakeAsync(FrameOptions.Plain, CancellationToken.None);

                Assert.True(identity.IsSuccess);
                Assert.Equal(2, identity.Value.Warnings.Count);
                Assert.Contains("IMC200", identity.Value.Warnings[0]);
                Assert.Contains("G1304 = 1", identity.Value.Warnings[1]);
            }
        }

        [Fact]
        public async Task Handshake_NoModel_Fails()
        {
            var device = new SimulatedImc300(FrameOptions.Plain);
            var transport = new FakeSerialTransport
            {
                Responder = request => Encoding.ASCII.GetString(request).Contains("QG03") ? Encoding.ASCII.GetBytes("{QG03-A;QG1304+:0;QG1305+:0}") : device.Respond(request),
            };
            using (var service = await ConnectAsync(transport, FrameOptions.Plain))
            {
                var identity = await service.HandshakeAsync(FrameOptions.Plain, CancellationToken.None);

                Assert.Equal("设备没有返回型号 (G03)。", identity.Error);
            }
        }

        [Fact]
        public async Task Handshake_NotConnected_Fails()
        {
            using (var service = new ZevisionDeviceService(new FakeSerialTransport()))
            {
                var identity = await service.HandshakeAsync(FrameOptions.Plain, CancellationToken.None);

                Assert.Equal("未连接。", identity.Error);
            }
        }

        [Fact]
        public async Task Handshake_AfterFrameSettingChange_UsesNewFormat()
        {
            var device = new SimulatedImc300(FrameOptions.Plain);
            using (var service = await ConnectAsync(device, FrameOptions.Plain))
            {
                var update = await service.SendAsync("UG1304:1", CancellationToken.None);
                Assert.True(update.Value.Responses.Single().IsSuccess); // 回答仍按旧格式

                var identity = await service.HandshakeAsync(ChecksumOnly, CancellationToken.None);

                Assert.Equal(ChecksumOnly, identity.Value.Frame);
                Assert.Equal(0, device.RejectedFrames); // 先试新格式,一次成功
            }
        }

        // ---------- 适配层转发 ----------

        [Fact]
        public async Task Retrying_IsForwardedExceptForHiddenSends()
        {
            var calls = 0;
            var transport = new FakeSerialTransport { Responder = _ => Encoding.ASCII.GetBytes(++calls % 2 == 1 ? "{-C}" : "{QG01+:1}") };
            using (var service = new ZevisionDeviceService(transport))
            {
                await service.ConnectAsync(new ConnectionOptions(new SerialSettings("COM_TEST", 115200), FrameOptions.Plain) { RetryCount = 1 }, CancellationToken.None);
                var retries = 0;
                service.Retrying += (s, e) => retries++;

                await service.SendAsync("QG01", CancellationToken.None);
                await service.SendHiddenAsync("QG01", TimeSpan.Zero, CancellationToken.None);

                Assert.Equal(1, retries);
                Assert.Equal(4, transport.WrittenSnapshot().Count);
            }
        }

        [Fact]
        public async Task HeartbeatLost_IsNotLoggedAsReadLoopError()
        {
            var errors = new FakeErrorLog();
            var transport = new FakeSerialTransport { Responder = _ => null };
            using (var service = new ZevisionDeviceService(transport, errors))
            {
                var lost = new TaskCompletionSource<ConnectionLostEventArgs>();
                service.ConnectionLost += (s, e) => lost.TrySetResult(e);
                var options = new ConnectionOptions(new SerialSettings("COM_TEST", 115200), FrameOptions.Plain, TimeSpan.FromMilliseconds(30))
                {
                    HeartbeatInterval = TimeSpan.FromMilliseconds(20),
                    HeartbeatMissLimit = 2,
                };
                await service.ConnectAsync(options, CancellationToken.None);

                var completed = await Task.WhenAny(lost.Task, Task.Delay(TimeSpan.FromSeconds(5)));

                Assert.Same(lost.Task, completed);
                Assert.Equal("设备连续 2 次没有应答心跳。", CommunicationText.DescribeConnectionLost(await lost.Task, options.HeartbeatMissLimit));
                Assert.Empty(errors.Messages);
            }
        }

        private static Task<ZevisionDeviceService> ConnectAsync(SimulatedImc300 device, FrameOptions frame, bool tcp = false) =>
            ConnectAsync(new FakeSerialTransport { Responder = device.Respond }, frame, tcp);

        private static async Task<ZevisionDeviceService> ConnectAsync(FakeSerialTransport transport, FrameOptions frame, bool tcp = false)
        {
            var service = new ZevisionDeviceService(transport);
            ConnectionEndpoint endpoint = tcp ? (ConnectionEndpoint)new TcpSettings("127.0.0.1", TcpSettings.DefaultPort) : new SerialSettings("COM_TEST", 115200);
            var connected = await service.ConnectAsync(new ConnectionOptions(endpoint, frame, TimeSpan.FromMilliseconds(150)), CancellationToken.None);
            Assert.True(connected.IsSuccess);
            return service;
        }
    }
}
