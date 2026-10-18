using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Communication;
using Zevision.Core.Devices;

namespace Zevision.Tests
{
    public class ZevisionDeviceServiceTests
    {
        private static readonly SerialSettings Settings = new SerialSettings("COM_TEST", 115200);

        [Fact]
        public async Task SendAsync_NotConnected_Fails()
        {
            using (var service = new ZevisionDeviceService(new FakeSerialTransport()))
            {
                var result = await service.SendAsync("H01", CancellationToken.None);

                Assert.False(result.IsSuccess);
            }
        }

        [Fact]
        public async Task ConnectAsync_PortInUse_ReturnsFailure()
        {
            var transport = new FakeSerialTransport { OpenException = new UnauthorizedAccessException("拒绝访问") };
            using (var service = new ZevisionDeviceService(transport))
            {
                var result = await service.ConnectAsync(new ConnectionOptions(Settings, FrameOptions.Plain), CancellationToken.None);

                Assert.False(result.IsSuccess);
                Assert.False(service.IsConnected);
            }
        }

        [Fact]
        public async Task SendAsync_DeviceReplies_ReturnsParsedResponse()
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{H01+:IMC300 1.0}") };
            using (var service = new ZevisionDeviceService(transport))
            {
                await ConnectAsync(service);

                var result = await service.SendAsync("H01", CancellationToken.None);

                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal("{H01}", Encoding.ASCII.GetString(Assert.Single(transport.Written)));
                var response = Assert.Single(result.Value.Responses);
                Assert.Equal("H01", response.Command);
                Assert.Equal("IMC300 1.0", response.Data);
            }
        }

        [Fact]
        public async Task SendAsync_UsesFrameOptions()
        {
            var options = new FrameOptions(checksumEnabled: true, lengthEnabled: true);
            var transport = new FakeSerialTransport { Responder = _ => ZevisionFrameCodec.Encode("H02+:1,0", options) };
            using (var service = new ZevisionDeviceService(transport))
            {
                Assert.True((await service.ConnectAsync(new ConnectionOptions(Settings, options), CancellationToken.None)).IsSuccess);

                var result = await service.SendAsync("H02", CancellationToken.None);

                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal(ZevisionFrameCodec.Encode("H02", options), transport.Written[0]);
            }
        }

        [Fact]
        public async Task SendAsync_ErrorResponse_IsSuccessfulReplyWithErrorCode()
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{QG99-A}") };
            using (var service = new ZevisionDeviceService(transport))
            {
                await ConnectAsync(service);

                var result = await service.SendAsync("QG99", CancellationToken.None);

                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal(ZevisionErrorCode.IllegalCommand, Assert.Single(result.Value.Responses).ErrorCode);
            }
        }

        [Fact]
        public async Task SendAsync_NoReply_TimesOut()
        {
            var transport = new FakeSerialTransport();
            using (var service = new ZevisionDeviceService(transport))
            {
                await ConnectAsync(service, TimeSpan.FromMilliseconds(100));

                var result = await service.SendAsync("H01", CancellationToken.None);

                Assert.False(result.IsSuccess);
                Assert.Contains("超时", result.Error);
            }
        }

        [Fact]
        public async Task SendAsync_LargeReplyArrivingSlowly_DoesNotTimeOut()
        {
            // 总传输时间 (约 750 ms) 超过超时 (500 ms),但每段间隔 (150 ms) 都小于超时,应视为仍在接收。
            // 余量留足:并行运行的测试多时 Task.Delay 可能明显超时。
            var transport = new FakeSerialTransport();
            using (var service = new ZevisionDeviceService(transport))
            {
                await ConnectAsync(service, TimeSpan.FromMilliseconds(500));
                var help = "?+:" + new string('x', 5000);
                var frame = Ascii("{" + help + "}");

                var send = service.SendAsync("?", CancellationToken.None);
                const int chunks = 5;
                var chunkSize = (frame.Length + chunks - 1) / chunks;
                for (var offset = 0; offset < frame.Length; offset += chunkSize)
                {
                    await Task.Delay(150);
                    var chunk = new byte[Math.Min(chunkSize, frame.Length - offset)];
                    Buffer.BlockCopy(frame, offset, chunk, 0, chunk.Length);
                    transport.Inject(chunk);
                }

                var result = await WithTimeout(send);

                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal(new string('x', 5000), Assert.Single(result.Value.Responses).Data);
            }
        }

        [Fact]
        public async Task SendAsync_MultiPacketHelpReply_IsReassembledAndParsed()
        {
            // 与实测 '?' 响应相同的分包:56931 + 56957 + 2604 字节。
            var options = new FrameOptions(checksumEnabled: true, lengthEnabled: true);
            var text = ZevisionFrameDecoderTests.MakeText(56931 + 56957 + 2604 - 3);
            var data = new byte[text.Length + 3];
            data[0] = (byte)'?';
            data[1] = (byte)'+';
            data[2] = (byte)':';
            Buffer.BlockCopy(text, 0, data, 3, text.Length);
            var frame = ZevisionFrameDecoderTests.BuildMessage(data, new[] { 56931, 56957, 2604 }, options);

            var transport = new FakeSerialTransport();
            using (var service = new ZevisionDeviceService(transport))
            {
                Assert.True((await service.ConnectAsync(new ConnectionOptions(Settings, options), CancellationToken.None)).IsSuccess);

                var send = service.SendAsync("?", CancellationToken.None);
                for (var offset = 0; offset < frame.Length; offset += 4096)
                {
                    var chunk = new byte[Math.Min(4096, frame.Length - offset)];
                    Buffer.BlockCopy(frame, offset, chunk, 0, chunk.Length);
                    transport.Inject(chunk);
                }

                var result = await WithTimeout(send);

                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal(3, result.Value.Frame.PacketCount);
                var response = Assert.Single(result.Value.Responses);
                Assert.Equal("?", response.Command);
                Assert.Equal(text.Length, response.Data!.Length);
            }
        }

        [Fact]
        public async Task SendAsync_TimeoutMidMessage_ReportsPartialFrame()
        {
            var options = new FrameOptions(checksumEnabled: true, lengthEnabled: true);
            var transport = new FakeSerialTransport { Responder = _ => new byte[] { (byte)'{', 0x00, 0x10, (byte)'?', (byte)'+' } };
            using (var service = new ZevisionDeviceService(transport))
            {
                var frames = new System.Collections.Concurrent.ConcurrentQueue<FrameReceivedEventArgs>();
                service.FrameReceived += (s, e) => frames.Enqueue(e);
                Assert.True((await service.ConnectAsync(new ConnectionOptions(Settings, options, TimeSpan.FromMilliseconds(150)), CancellationToken.None)).IsSuccess);

                var result = await service.SendAsync("?", CancellationToken.None);

                Assert.False(result.IsSuccess);
                Assert.Contains("不完整", result.Error);
                Assert.True(frames.TryDequeue(out var partial));
                Assert.Equal(FrameError.Incomplete, partial!.Frame.Error);
                Assert.Equal("?+", partial.Frame.Payload);
            }
        }

        [Fact]
        public async Task SendAsync_ChecksumEqualToStopByte_IsSentWithFillerAndSucceeds()
        {
            // 模拟设备缺陷:校验和为 0x7D 的帧被当作提前结束,回复 -C。
            var options = new FrameOptions(checksumEnabled: true, lengthEnabled: true);
            var transport = new FakeSerialTransport
            {
                Responder = frame =>
                {
                    if (frame[frame.Length - 2] == 0x7D)
                    {
                        return ZevisionFrameCodec.Encode("-C", options);
                    }

                    var command = Encoding.ASCII.GetString(frame, 3, frame.Length - 5);
                    var reply = string.Join(";", command.Split(';').Select(c => c + "+"));
                    return ZevisionFrameCodec.Encode(reply, options);
                },
            };
            using (var service = new ZevisionDeviceService(transport))
            {
                Assert.True((await service.ConnectAsync(new ConnectionOptions(Settings, options), CancellationToken.None)).IsSuccess);

                var result = await service.SendAsync("AL0702010600", CancellationToken.None);

                Assert.True(result.IsSuccess, result.Error);
                var response = Assert.Single(result.Value.Responses); // 追加的 H01 响应已去掉
                Assert.Equal("AL0702010600", response.Command);
                Assert.True(response.IsSuccess);
                Assert.Contains("AL0702010600;H01", Encoding.ASCII.GetString(Assert.Single(transport.Written)));
            }
        }

        [Fact]
        public async Task SendAsync_MultipleCommands_OneFrameAllResponses()
        {
            var options = new FrameOptions(checksumEnabled: true, lengthEnabled: true);
            var transport = new FakeSerialTransport
            {
                Responder = _ => ZevisionFrameCodec.Encode("QG01+:1.00;QG1304+:1;QG99-A", options),
            };
            using (var service = new ZevisionDeviceService(transport))
            {
                Assert.True((await service.ConnectAsync(new ConnectionOptions(Settings, options), CancellationToken.None)).IsSuccess);

                var result = await service.SendAsync("QG01;QG1304;QG99", CancellationToken.None);

                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal(ZevisionFrameCodec.Encode("QG01;QG1304;QG99", options), Assert.Single(transport.Written));
                Assert.Equal(new[] { "QG01", "QG1304", "QG99" }, result.Value.Responses.Select(r => r.Command));
                Assert.Equal("1.00", result.Value.Responses[0].Data);
                Assert.Equal(ZevisionErrorCode.IllegalCommand, result.Value.Responses[2].ErrorCode);
            }
        }

        [Fact]
        public async Task SendAsync_StaleReplyBeforeRealOne_IsReportedAsUnsolicited()
        {
            // 上一个请求超时后迟到的响应 (H02) 先到,随后才是本次请求 (H01) 的响应。
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{H02+:old}{H01+:new}") };
            using (var service = new ZevisionDeviceService(transport))
            {
                var received = new System.Collections.Concurrent.ConcurrentQueue<FrameReceivedEventArgs>();
                service.FrameReceived += (s, e) => received.Enqueue(e);
                await ConnectAsync(service);

                var result = await service.SendAsync("H01", CancellationToken.None);

                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal("new", Assert.Single(result.Value.Responses).Data);
                var events = received.ToArray();
                Assert.False(events[0].IsSolicited);
                Assert.Equal("H02+:old", events[0].Frame.Payload);
                Assert.True(events[1].IsSolicited);
            }
        }

        [Fact]
        public async Task SendAsync_AfterCancelledHelp_TailOfHelpDoesNotAnswerNextCommand()
        {
            // 取消 ? 后设备仍在发送帮助表;下一条指令不能把它当成自己的响应。
            var transport = new FakeSerialTransport();
            transport.Responder = frame =>
                Encoding.ASCII.GetString(frame).Contains("QG01") ? Ascii("{?+:help tail}{QG01+:1.00}") : null;
            using (var service = new ZevisionDeviceService(transport))
            {
                await ConnectAsync(service);

                var result = await service.SendAsync("QG01", CancellationToken.None);

                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal("QG01", Assert.Single(result.Value.Responses).Command);
                Assert.Equal("1.00", result.Value.Responses[0].Data);
            }
        }

        [Fact]
        public async Task SendAsync_EchoLessError_StillAnswersRequest()
        {
            // 设备无法解析帧时只回 "-C",没有回显:仍应作为本次请求的响应。
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{-C}") };
            using (var service = new ZevisionDeviceService(transport))
            {
                await ConnectAsync(service);

                var result = await service.SendAsync("H01", CancellationToken.None);

                Assert.True(result.IsSuccess, result.Error);
                Assert.Equal(ZevisionErrorCode.ChecksumError, Assert.Single(result.Value.Responses).ErrorCode);
            }
        }

        [Fact]
        public async Task FrameReceived_SubscriberThrows_ReadLoopKeepsRunningAndErrorIsLogged()
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{H01+:x}") };
            var log = new FakeErrorLog();
            using (var service = new ZevisionDeviceService(transport, log))
            {
                service.FrameReceived += (s, e) => throw new InvalidOperationException("订阅方出错");
                await ConnectAsync(service);

                Assert.True((await service.SendAsync("H01", CancellationToken.None)).IsSuccess);
                Assert.True((await service.SendAsync("H01", CancellationToken.None)).IsSuccess);

                Assert.True(service.IsConnected);
                Assert.Equal(2, log.Messages.Count);
                Assert.Contains("订阅方出错", log.Messages[0]);
            }
        }

        [Fact]
        public async Task ReadLoop_UnexpectedException_EndsSessionAndRaisesConnectionLost()
        {
            var transport = new FakeSerialTransport();
            var log = new FakeErrorLog();
            using (var service = new ZevisionDeviceService(transport, log))
            {
                var lost = new TaskCompletionSource<ConnectionLostEventArgs>();
                service.ConnectionLost += (s, e) => lost.TrySetResult(e);
                await ConnectAsync(service);

                transport.SimulateFailure(new NotSupportedException("驱动异常"));
                var args = await WithTimeout(lost.Task);

                Assert.IsType<NotSupportedException>(args.Exception);
                Assert.False(service.IsConnected);
                Assert.Contains("驱动异常", Assert.Single(log.Messages));
            }
        }

        [Fact]
        public async Task SendAsync_ChecksumMismatch_Fails()
        {
            var options = new FrameOptions(checksumEnabled: true, lengthEnabled: false);
            var transport = new FakeSerialTransport { Responder = _ => new byte[] { (byte)'{', (byte)'H', (byte)'+', 0x00, (byte)'}' } };
            using (var service = new ZevisionDeviceService(transport))
            {
                Assert.True((await service.ConnectAsync(new ConnectionOptions(Settings, options), CancellationToken.None)).IsSuccess);

                var result = await service.SendAsync("H01", CancellationToken.None);

                Assert.False(result.IsSuccess);
                Assert.Contains("校验和不匹配", result.Error);
            }
        }

        [Fact]
        public async Task SendAsync_InvalidCommand_FailsWithoutWriting()
        {
            var transport = new FakeSerialTransport();
            using (var service = new ZevisionDeviceService(transport))
            {
                await ConnectAsync(service);

                var result = await service.SendAsync("H{01", CancellationToken.None);

                Assert.False(result.IsSuccess);
                Assert.Empty(transport.Written);
            }
        }

        [Fact]
        public async Task FrameReceived_UnsolicitedFrame_IsMarkedUnsolicited()
        {
            var transport = new FakeSerialTransport();
            using (var service = new ZevisionDeviceService(transport))
            {
                var received = new TaskCompletionSource<FrameReceivedEventArgs>();
                service.FrameReceived += (s, e) => received.TrySetResult(e);
                await ConnectAsync(service);

                transport.Inject(Ascii("{SC03+:1.5}"));
                var args = await WithTimeout(received.Task);

                Assert.False(args.IsSolicited);
                Assert.Equal("SC03+:1.5", args.Frame.Payload);
            }
        }

        [Fact]
        public async Task ConnectionLost_RaisedOnReadFailure_AndServiceDisconnects()
        {
            var transport = new FakeSerialTransport();
            using (var service = new ZevisionDeviceService(transport))
            {
                var lost = new TaskCompletionSource<ConnectionLostEventArgs>();
                service.ConnectionLost += (s, e) => lost.TrySetResult(e);
                await ConnectAsync(service);

                transport.SimulateFailure();
                var args = await WithTimeout(lost.Task);

                Assert.IsType<IOException>(args.Exception);
                Assert.False(service.IsConnected);
                Assert.False(transport.IsOpen);
            }
        }

        [Fact]
        public async Task DisconnectAsync_DoesNotRaiseConnectionLost()
        {
            var transport = new FakeSerialTransport();
            using (var service = new ZevisionDeviceService(transport))
            {
                var raised = false;
                service.ConnectionLost += (s, e) => raised = true;
                await ConnectAsync(service);

                await service.DisconnectAsync();

                Assert.False(raised);
                Assert.False(service.IsConnected);
                Assert.False(transport.IsOpen);
            }
        }

        [Fact]
        public async Task SendAsync_MinimumTimeout_WaitsLongerThanConnectionTimeout()
        {
            // 与实机 ? 相同的情况:连接超时较短,响应在首字节前停顿较久。
            var transport = new FakeSerialTransport();
            using (var service = new ZevisionDeviceService(transport))
            {
                await ConnectAsync(service, TimeSpan.FromMilliseconds(200));

                var send = service.SendAsync("H01", TimeSpan.FromMilliseconds(2000), CancellationToken.None);
                await Task.Delay(600);
                transport.Inject(Ascii("{H01+:Zevision}"));
                var result = await WithTimeout(send);

                Assert.True(result.IsSuccess, result.Error);
            }
        }

        [Fact]
        public async Task SendAsync_MinimumTimeoutShorterThanConnection_UsesConnectionTimeout()
        {
            var transport = new FakeSerialTransport();
            using (var service = new ZevisionDeviceService(transport))
            {
                await ConnectAsync(service, TimeSpan.FromMilliseconds(300));

                var result = await WithTimeout(service.SendAsync("H01", TimeSpan.FromMilliseconds(50), CancellationToken.None));

                Assert.False(result.IsSuccess);
                Assert.Contains("300 ms", result.Error);
            }
        }

        private static async Task ConnectAsync(ZevisionDeviceService service, TimeSpan? timeout = null)
        {
            var result = await service.ConnectAsync(new ConnectionOptions(Settings, FrameOptions.Plain, timeout), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error);
        }

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(completed == task, "等待事件超时。");
            return await task;
        }

        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
    }
}
