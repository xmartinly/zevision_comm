using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Communication;

namespace Zevision.Tests
{
    /// <summary>
    /// 通讯库对外入口 <see cref="ZevisionClient"/>:通讯参数在构造时给出,失败以错误类型 + 英文说明返回,不写日志。
    /// 收发、拆帧、回显对应等行为由 <see cref="ZevisionDeviceServiceTests"/> 经应用的适配层覆盖。
    /// </summary>
    public class ZevisionClientTests
    {
        private static readonly ConnectionOptions Options = new ConnectionOptions(new SerialSettings("COM_TEST", 115200), FrameOptions.Plain, TimeSpan.FromMilliseconds(150));

        [Fact]
        public void Constructor_NullOptions_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ZevisionClient(null!));
            Assert.Throws<ArgumentNullException>(() => new ZevisionClient(Options, null!));
        }

        [Fact]
        public void Constructor_KeepsOptions()
        {
            using (var client = new ZevisionClient(Options))
            {
                Assert.Same(Options, client.Options);
                Assert.False(client.IsConnected);
            }
        }

        [Fact]
        public async Task SendAsync_NotConnected_ReturnsNotConnected()
        {
            using (var client = Create(new FakeSerialTransport()))
            {
                var result = await client.SendAsync("H01");

                Assert.False(result.IsSuccess);
                Assert.Equal(ZevisionErrorKind.NotConnected, result.Error!.Kind);
                Assert.Equal("Not connected.", result.Error.Message);
                Assert.Throws<InvalidOperationException>(() => result.Value);
            }
        }

        [Fact]
        public async Task ConnectAsync_Twice_ReturnsAlreadyConnected()
        {
            using (var client = Create(new FakeSerialTransport()))
            {
                Assert.True((await client.ConnectAsync()).IsSuccess);

                var second = await client.ConnectAsync();

                Assert.Equal(ZevisionErrorKind.AlreadyConnected, second.Error!.Kind);
                Assert.True(client.IsConnected);
            }
        }

        [Fact]
        public async Task ConnectAsync_PortInUse_ReturnsConnectFailedWithException()
        {
            var denied = new UnauthorizedAccessException("Access denied");
            using (var client = Create(new FakeSerialTransport { OpenException = denied }))
            {
                var result = await client.ConnectAsync();

                Assert.Equal(ZevisionErrorKind.ConnectFailed, result.Error!.Kind);
                Assert.Same(denied, result.Error.Exception);
                Assert.Same(Options.Endpoint, result.Error.Endpoint);
                Assert.Equal("Cannot connect to COM_TEST, 115200, 8N1: Access denied", result.Error.Message);
                Assert.False(client.IsConnected);
            }
        }

        [Fact]
        public async Task SendAsync_Reply_ReturnsResponses()
        {
            using (var client = Create(new FakeSerialTransport { Responder = _ => Ascii("{H01+:IMC300 1.0}") }))
            {
                await ConnectAsync(client);

                var result = await client.SendAsync("H01");

                Assert.True(result.IsSuccess, result.ToString());
                Assert.Equal("IMC300 1.0", Assert.Single(result.Value.Responses).Data);
            }
        }

        [Fact]
        public async Task SendAsync_NoReply_ReturnsTimeoutWithDetails()
        {
            using (var client = Create(new FakeSerialTransport()))
            {
                await ConnectAsync(client);

                var result = await client.SendAsync("H01", TimeSpan.FromMilliseconds(300));

                Assert.Equal(ZevisionErrorKind.Timeout, result.Error!.Kind);
                Assert.Equal(TimeSpan.FromMilliseconds(300), result.Error.Timeout);
                Assert.False(result.Error.ReceivedIncompleteData);
                Assert.Equal("Timed out waiting for a response (no data within 300 ms).", result.Error.Message);
            }
        }

        [Fact]
        public async Task SendAsync_PartialReply_ReportsIncompleteData()
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{H01+:IMC") };
            using (var client = Create(transport))
            {
                await ConnectAsync(client);

                var result = await client.SendAsync("H01");

                Assert.Equal(ZevisionErrorKind.Timeout, result.Error!.Kind);
                Assert.True(result.Error.ReceivedIncompleteData);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("H{01")]
        [InlineData("H01Å")]
        public async Task SendAsync_InvalidCommand_ReturnsInvalidCommandWithoutWriting(string command)
        {
            var transport = new FakeSerialTransport();
            using (var client = Create(transport))
            {
                await ConnectAsync(client);

                var result = await client.SendAsync(command);

                Assert.Equal(ZevisionErrorKind.InvalidCommand, result.Error!.Kind);
                Assert.Empty(transport.Written);
            }
        }

        [Fact]
        public async Task SendAsync_InvalidFrame_ReturnsFrameError()
        {
            var options = new ConnectionOptions(Options.Endpoint, new FrameOptions(checksumEnabled: true, lengthEnabled: false), Options.ResponseTimeout);
            var transport = new FakeSerialTransport { Responder = _ => new byte[] { (byte)'{', (byte)'H', (byte)'+', 0x00, (byte)'}' } };
            using (var client = new ZevisionClient(options, _ => transport))
            {
                await ConnectAsync(client);

                var result = await client.SendAsync("H01");

                Assert.Equal(ZevisionErrorKind.InvalidFrame, result.Error!.Kind);
                Assert.Equal(FrameError.ChecksumMismatch, result.Error.FrameError);
            }
        }

        [Fact]
        public async Task EventHandlerThrows_RaisesHandlerFailed_AndCommunicationContinues()
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{H01+:x}") };
            using (var client = Create(transport))
            {
                var failures = new System.Collections.Concurrent.ConcurrentQueue<HandlerFailedEventArgs>();
                client.FrameReceived += (s, e) => throw new InvalidOperationException("handler bug");
                client.FrameSent += (s, e) => throw new InvalidOperationException("sent bug");
                client.HandlerFailed += (s, e) =>
                {
                    failures.Enqueue(e);
                    throw new InvalidOperationException("HandlerFailed itself fails"); // 被忽略
                };
                await ConnectAsync(client);

                Assert.True((await client.SendAsync("H01")).IsSuccess);
                Assert.True((await client.SendAsync("H01")).IsSuccess);

                Assert.True(client.IsConnected);
                Assert.Equal(new[] { "FrameSent", "FrameReceived", "FrameSent", "FrameReceived" }, failures.Select(f => f.EventName));
                Assert.All(failures, f => Assert.IsType<InvalidOperationException>(f.Exception));
            }
        }

        [Fact]
        public async Task ReadFailure_RaisesConnectionLostWithOriginalException()
        {
            var transport = new FakeSerialTransport();
            using (var client = Create(transport))
            {
                var lost = new TaskCompletionSource<ConnectionLostEventArgs>();
                client.ConnectionLost += (s, e) => lost.TrySetResult(e);
                await ConnectAsync(client);

                var failure = new NotSupportedException("driver bug");
                transport.SimulateFailure(failure);
                var args = await WithTimeout(lost.Task);

                Assert.Same(failure, args.Exception);
                Assert.False(client.IsConnected);
                Assert.Equal(ZevisionErrorKind.NotConnected, (await client.SendAsync("H01")).Error!.Kind);
            }
        }

        [Fact]
        public async Task Disconnect_ThenConnectAgain_UsesSameOptions()
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{H01+:x}") };

            // 期望成功的发送用 1 s 的超时:150 ms 时,机器繁忙 (整套测试并行) 偶尔在假串口回答之前就超时 (2026-10-23)。
            using (var client = new ZevisionClient(new ConnectionOptions(new SerialSettings("COM_TEST", 115200), FrameOptions.Plain, TimeSpan.FromSeconds(1)), _ => transport))
            {
                await ConnectAsync(client);
                await client.DisconnectAsync();
                Assert.False(client.IsConnected);
                Assert.Equal(ZevisionErrorKind.NotConnected, (await client.SendAsync("H01")).Error!.Kind);

                await ConnectAsync(client);
                Assert.True((await client.SendAsync("H01")).IsSuccess);
            }
        }

        [Fact]
        public async Task Dispose_EndsConnection_AndRejectsConnect()
        {
            var transport = new FakeSerialTransport();
            var client = Create(transport);
            await ConnectAsync(client);
            var pending = client.SendAsync("H01", TimeSpan.FromSeconds(10));

            client.Dispose();

            var result = await WithTimeout(pending);
            Assert.Equal(ZevisionErrorKind.Disconnected, result.Error!.Kind);
            Assert.False(transport.IsOpen);
            Assert.Equal(ZevisionErrorKind.NotConnected, (await client.SendAsync("H01")).Error!.Kind);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync());
        }

        [Fact]
        public async Task ConnectAsync_Cancelled_Throws()
        {
            var transport = new FakeSerialTransport { OpenException = new OperationCanceledException() };
            using (var client = Create(transport))
            {
                await Assert.ThrowsAsync<OperationCanceledException>(() => client.ConnectAsync(new CancellationToken(true)));
                Assert.False(client.IsConnected);

                // 取消后可以再次连接。
                transport.OpenException = null;
                await ConnectAsync(client);
            }
        }

        [Fact]
        public void Result_Fail_RequiresError()
        {
            Assert.Throws<ArgumentNullException>(() => ZevisionResult.Fail(null!));
            Assert.Throws<ArgumentNullException>(() => ZevisionResult<int>.Fail(null!));
            Assert.True(ZevisionResult.Ok().IsSuccess);
            Assert.Equal(5, ZevisionResult<int>.Ok(5).Value);
            Assert.Equal("Timeout: x", ZevisionResult.Fail(new ZevisionError(ZevisionErrorKind.Timeout, "x")).ToString());
        }

        private static ZevisionClient Create(FakeSerialTransport transport) => new ZevisionClient(Options, _ => transport);

        private static async Task ConnectAsync(ZevisionClient client)
        {
            var result = await client.ConnectAsync();
            Assert.True(result.IsSuccess, result.ToString());
        }

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(completed == task, "Timed out waiting.");
            return await task;
        }

        [Fact]
        public async Task Dispose_WhileConnecting_ClosesTheTransportAndConnectThrows()
        {
            // 连接过程中释放:打开完成后不能留下打开的端口 (以前会发布会话并启动读取循环)。
            var transport = new GatedTransport();
            var client = new ZevisionClient(Options, _ => transport);
            var connect = client.ConnectAsync();

            client.Dispose();
            transport.Open.SetResult(true);

            await Assert.ThrowsAsync<ObjectDisposedException>(() => connect);
            Assert.Equal(1, transport.CloseCount);
            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task Connect_OpenFails_ClosesTheTransport()
        {
            var transport = new GatedTransport();
            transport.Open.SetException(new System.IO.IOException("port in use"));
            using (var client = new ZevisionClient(Options, _ => transport))
            {
                var result = await client.ConnectAsync();

                Assert.Equal(ZevisionErrorKind.ConnectFailed, result.Error!.Kind);
                Assert.Equal(1, transport.CloseCount);
            }
        }

        [Fact]
        public async Task SlowFrameReceivedHandler_DoesNotTurnTheReplyIntoTimeout()
        {
            // 响应已收到、FrameReceived 的处理程序比超时还慢:以前超时先到,收到的响应被当成超时。
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{H01+:Zevision}") };
            using (var client = new ZevisionClient(Options, _ => transport))
            {
                client.FrameReceived += (s, e) => Thread.Sleep(400);
                Assert.True((await client.ConnectAsync()).IsSuccess);

                var result = await client.SendAsync("H01");

                Assert.True(result.IsSuccess, result.Error?.Message);
                Assert.Equal("Zevision", Assert.Single(result.Value.Responses).Data);
            }
        }

        [Fact]
        public async Task EmptyResponse_IsUnparseable()
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{}") };
            using (var client = new ZevisionClient(Options, _ => transport))
            {
                Assert.True((await client.ConnectAsync()).IsSuccess);

                var result = await client.SendAsync("H01");

                Assert.Equal(ZevisionErrorKind.UnparseableResponse, result.Error!.Kind);
            }
        }

        [Fact]
        public void Decoder_InvalidRange_Throws()
        {
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);

            Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Push(new byte[4], 2, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Push(new byte[4], -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ZevisionFrameCodec.ComputeChecksum(new byte[4], 0, 5));
        }

        [Fact]
        public void Parser_InvalidResponse_TryParseFalse_ParseThrows()
        {
            Assert.False(ZevisionResponseParser.TryParse("H01", out var responses));
            Assert.Empty(responses);
            Assert.Contains("\"H01\"", Assert.Throws<FormatException>(() => ZevisionResponseParser.Parse("H01")).Message);
            Assert.Equal("UG1304", ZevisionResponseParser.EchoOf(" UG1304:1;H01"));
        }

        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        /// <summary>打开要等测试放行的传输,记录关闭次数。</summary>
        private sealed class GatedTransport : IDeviceTransport
        {
            public TaskCompletionSource<bool> Open { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public int CloseCount { get; private set; }

            public bool IsOpen => false;

            public Task OpenAsync(ConnectionEndpoint endpoint, CancellationToken cancellationToken) => Open.Task;

            public void Close() => CloseCount++;

            public Task WriteAsync(byte[] data, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken) => Task.FromResult(0);

            public void Dispose()
            {
            }
        }
    }
}
