using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Communication;

namespace Zevision.Tests
{
    /// <summary>
    /// 通讯库的会话功能:传输出错重发、心跳、连接后改变帧格式、按优先级排队 (1.2.0)。
    /// </summary>
    public class ClientSessionTests
    {
        private static readonly SerialSettings Port = new SerialSettings("COM_TEST", 115200);

        // ---------- 重发 ----------

        [Fact]
        public async Task Retry_Timeout_ResendsAndRaisesRetrying()
        {
            var calls = 0;
            var transport = new FakeSerialTransport { Responder = _ => ++calls < 3 ? null : Ascii("{QG01+:1}") };
            using (var client = await ConnectAsync(transport, new ConnectionOptions(Port, FrameOptions.Plain, TimeSpan.FromMilliseconds(80)) { RetryCount = 2 }))
            {
                var retries = new List<RetryEventArgs>();
                client.Retrying += (s, e) => retries.Add(e);

                var result = await client.SendAsync("QG01");

                Assert.True(result.IsSuccess);
                Assert.Equal(3, transport.Written.Count);
                Assert.Equal(new[] { 1, 2 }, retries.Select(r => r.Attempt));
                Assert.All(retries, r => Assert.Equal(ZevisionErrorKind.Timeout, r.Error!.Kind));
                Assert.All(retries, r => Assert.Equal(2, r.MaxRetries));
            }
        }

        [Fact]
        public async Task Retry_DeviceChecksumError_Resends()
        {
            var calls = 0;
            var transport = new FakeSerialTransport { Responder = _ => Ascii(++calls == 1 ? "{-C}" : "{UG0E03+}") };
            using (var client = await ConnectAsync(transport, new ConnectionOptions(Port, FrameOptions.Plain) { RetryCount = 1 }))
            {
                RetryEventArgs? retry = null;
                client.Retrying += (s, e) => retry = e;

                var result = await client.SendAsync("UG0E03:20");

                Assert.True(result.Value.Responses.Single().IsSuccess);
                Assert.Equal(ZevisionErrorCode.ChecksumError, retry!.DeviceError);
                Assert.Null(retry.Error);
            }
        }

        [Fact]
        public async Task Retry_ExhaustedRetries_ReturnsLastFailure()
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{QG01-T}") };
            using (var client = await ConnectAsync(transport, new ConnectionOptions(Port, FrameOptions.Plain) { RetryCount = 2 }))
            {
                var result = await client.SendAsync("QG01");

                Assert.Equal(ZevisionErrorCode.Timeout, result.Value.Responses.Single().ErrorCode);
                Assert.Equal(3, transport.Written.Count);
            }
        }

        [Theory]
        [InlineData("AM00")] // 追加:重发会多加一项
        [InlineData("DM03")] // 删除
        [InlineData("RS011E")] // 执行动作
        [InlineData("QG01;AM00")] // 一帧中有一条不能重复,整帧不重发
        public async Task Retry_NotRepeatableCommand_IsNotResent(string command)
        {
            var transport = new FakeSerialTransport { Responder = _ => null };
            using (var client = await ConnectAsync(transport, new ConnectionOptions(Port, FrameOptions.Plain, TimeSpan.FromMilliseconds(50)) { RetryCount = 3 }))
            {
                var result = await client.SendAsync(command);

                Assert.Equal(ZevisionErrorKind.Timeout, result.Error!.Kind);
                Assert.Single(transport.Written);
            }
        }

        [Theory]
        [InlineData("{QG99-A}")] // 非法命令
        [InlineData("{QG01-B}")] // 非法参数
        [InlineData("{QG01-F}")] // 状态错误:不重发,由调用方查询状态
        public async Task Retry_DeviceRejectsCommand_IsNotResent(string reply)
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii(reply) };
            using (var client = await ConnectAsync(transport, new ConnectionOptions(Port, FrameOptions.Plain) { RetryCount = 3 }))
            {
                await client.SendAsync(reply.Substring(1, 4));

                Assert.Single(transport.Written);
            }
        }

        [Fact]
        public async Task Retry_DisabledByDefaultAndPerRequest()
        {
            var transport = new FakeSerialTransport { Responder = _ => null };
            using (var plain = await ConnectAsync(transport, new ConnectionOptions(Port, FrameOptions.Plain, TimeSpan.FromMilliseconds(50))))
            {
                await plain.SendAsync("QG01");
            }

            using (var client = await ConnectAsync(transport, new ConnectionOptions(Port, FrameOptions.Plain, TimeSpan.FromMilliseconds(50)) { RetryCount = 2 }))
            {
                await client.SendAsync("QG01", new RequestOptions { AllowRetry = false });
            }

            Assert.Equal(2, transport.Written.Count);
        }

        // ---------- 心跳 ----------

        [Fact]
        public async Task Heartbeat_IdleConnection_SendsH01WithoutEvents()
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{H01+:Zevision IMC300}") };
            var options = new ConnectionOptions(Port, FrameOptions.Plain) { HeartbeatInterval = TimeSpan.FromMilliseconds(30) };
            using (var client = await ConnectAsync(transport, options))
            {
                var events = 0;
                client.FrameSent += (s, e) => Interlocked.Increment(ref events);
                client.FrameReceived += (s, e) => Interlocked.Increment(ref events);

                await WaitUntil(() => transport.Written.Count >= 3);

                Assert.All(transport.WrittenSnapshot(), frame => Assert.Equal("{H01}", Encoding.ASCII.GetString(frame)));
                Assert.Equal(0, Volatile.Read(ref events));
                Assert.True(client.IsConnected);
                await client.DisconnectAsync();
            }
        }

        [Fact]
        public async Task Heartbeat_NoAnswer_EndsConnectionAfterMissLimit()
        {
            var transport = new FakeSerialTransport { Responder = _ => null };
            var options = new ConnectionOptions(Port, FrameOptions.Plain, TimeSpan.FromMilliseconds(40)) { HeartbeatInterval = TimeSpan.FromMilliseconds(30), HeartbeatMissLimit = 3 };
            using (var client = await ConnectAsync(transport, options))
            {
                var lost = new TaskCompletionSource<ConnectionLostEventArgs>();
                client.ConnectionLost += (s, e) => lost.TrySetResult(e);

                var e2 = await WithTimeout(lost.Task);

                Assert.Equal(ConnectionLostReason.HeartbeatTimeout, e2.Reason);
                Assert.IsType<TimeoutException>(e2.Exception);
                Assert.False(client.IsConnected);
                Assert.Equal(3, transport.Written.Count);
                Assert.False(transport.IsOpen);
            }
        }

        [Fact]
        public async Task Heartbeat_ErrorAnswer_CountsAsAlive()
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{-C}") };
            var options = new ConnectionOptions(Port, FrameOptions.Plain, TimeSpan.FromMilliseconds(40)) { HeartbeatInterval = TimeSpan.FromMilliseconds(20), HeartbeatMissLimit = 1 };
            using (var client = await ConnectAsync(transport, options))
            {
                var lost = false;
                client.ConnectionLost += (s, e) => lost = true;

                await WaitUntil(() => transport.Written.Count >= 4);

                Assert.False(lost);
                Assert.True(client.IsConnected);
                await client.DisconnectAsync();
            }
        }

        [Fact]
        public async Task Heartbeat_Disconnect_WaitsForHeartbeatAndRaisesNothing()
        {
            var transport = new FakeSerialTransport { Responder = _ => null };
            var options = new ConnectionOptions(Port, FrameOptions.Plain, TimeSpan.FromSeconds(5)) { HeartbeatInterval = TimeSpan.FromMilliseconds(10) };
            using (var client = await ConnectAsync(transport, options))
            {
                var lost = false;
                client.ConnectionLost += (s, e) => lost = true;
                await WaitUntil(() => transport.Written.Count >= 1); // 心跳正在等待响应

                await WithTimeout(client.DisconnectAsync());

                Assert.False(lost);
                Assert.False(client.IsConnected);
            }
        }

        // ---------- 改变帧格式 ----------

        [Fact]
        public async Task ChangeFrameOptions_UsesNewFormatForSendingAndReceiving()
        {
            var lengthAndChecksum = new FrameOptions(checksumEnabled: true, lengthEnabled: true);
            var transport = new FakeSerialTransport { Responder = _ => ZevisionFrameCodec.Encode("H01+:Zevision", lengthAndChecksum) };
            using (var client = await ConnectAsync(transport, new ConnectionOptions(Port, FrameOptions.Plain)))
            {
                var changed = await client.ChangeFrameOptionsAsync(lengthAndChecksum);
                var result = await client.SendAsync("H01");

                Assert.True(changed.IsSuccess);
                Assert.Equal(lengthAndChecksum, client.Frame);
                Assert.Equal(ZevisionFrameCodec.Encode("H01", lengthAndChecksum), transport.Written.Single());
                Assert.Equal("Zevision", result.Value.Responses.Single().Data);
                Assert.Same(FrameOptions.Plain, client.Options.Frame);
            }
        }

        [Fact]
        public async Task ChangeFrameOptions_PartialDataIsReportedFirst()
        {
            var transport = new FakeSerialTransport();
            using (var client = await ConnectAsync(transport, new ConnectionOptions(Port, FrameOptions.Plain)))
            {
                var frames = new List<ZevisionFrame>();
                client.FrameReceived += (s, e) => frames.Add(e.Frame);
                transport.Inject(Ascii("{H01+:Zev"));
                await Task.Delay(50);

                await client.ChangeFrameOptionsAsync(new FrameOptions(true, true));

                Assert.Equal(FrameError.Incomplete, Assert.Single(frames).Error);
            }
        }

        [Fact]
        public async Task ChangeFrameOptions_NotConnected_Fails()
        {
            using (var client = new ZevisionClient(new ConnectionOptions(Port, FrameOptions.Plain), _ => new FakeSerialTransport()))
            {
                var result = await client.ChangeFrameOptionsAsync(new FrameOptions(true, true));

                Assert.Equal(ZevisionErrorKind.NotConnected, result.Error!.Kind);
                Assert.Same(FrameOptions.Plain, client.Frame);
            }
        }

        [Fact]
        public async Task ChangeFrameOptions_Reconnect_StartsWithConfiguredFormat()
        {
            var transport = new FakeSerialTransport();
            using (var client = await ConnectAsync(transport, new ConnectionOptions(Port, FrameOptions.Plain)))
            {
                await client.ChangeFrameOptionsAsync(new FrameOptions(true, true));
                await client.DisconnectAsync();
                await client.ConnectAsync();

                Assert.Equal(FrameOptions.Plain, client.Frame);
            }
        }

        // ---------- 参数 ----------

        [Fact]
        public void ConnectionOptions_ValidatesSessionSettings()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectionOptions(Port, FrameOptions.Plain) { RetryCount = -1 });
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectionOptions(Port, FrameOptions.Plain) { RetryCount = ConnectionOptions.MaxRetryCount + 1 });
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectionOptions(Port, FrameOptions.Plain) { HeartbeatInterval = TimeSpan.FromSeconds(-1) });
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectionOptions(Port, FrameOptions.Plain) { HeartbeatMissLimit = 0 });

            var defaults = new ConnectionOptions(Port, FrameOptions.Plain);
            Assert.Equal(0, defaults.RetryCount);
            Assert.Equal(TimeSpan.Zero, defaults.HeartbeatInterval);
            Assert.Equal(3, defaults.HeartbeatMissLimit);
        }

        [Fact]
        public void FrameOptions_EqualByValue()
        {
            Assert.Equal(new FrameOptions(true, false), new FrameOptions(true, false));
            Assert.NotEqual(new FrameOptions(true, false), new FrameOptions(false, true));
            Assert.Equal(FrameOptions.Plain, new FrameOptions(false, false));
            Assert.Equal(new FrameOptions(true, true).GetHashCode(), new FrameOptions(true, true).GetHashCode());
            Assert.False(FrameOptions.Plain.Equals(null));
        }

        [Theory]
        [InlineData("QG01", true)]
        [InlineData("UG0E03:20", true)]
        [InlineData("SC11;SC12", true)]
        [InlineData("H01", true)]
        [InlineData("?", true)]
        [InlineData("?M", true)]
        [InlineData("AM00", false)]
        [InlineData("DF010300", false)]
        [InlineData("RS011E", false)]
        [InlineData("QG01;UG0E03:5;AM00", false)]
        public void RetryPolicy_RepeatableActions(string command, bool repeatable) =>
            Assert.Equal(repeatable, RetryPolicy.IsRepeatable(command));

        // ---------- 优先级 ----------

        [Fact]
        public async Task PriorityLock_GrantsHighestPriorityThenOldest()
        {
            var gate = new PriorityLock();
            Assert.True(gate.TryEnter());
            var order = new List<string>();
            var low = gate.EnterAsync(SendPriority.Low, CancellationToken.None).ContinueWith(_ => Record(order, "low"), TaskScheduler.Default);
            var normal1 = gate.EnterAsync(SendPriority.Normal, CancellationToken.None).ContinueWith(_ => Record(order, "normal1"), TaskScheduler.Default);
            var normal2 = gate.EnterAsync(SendPriority.Normal, CancellationToken.None).ContinueWith(_ => Record(order, "normal2"), TaskScheduler.Default);
            var high = gate.EnterAsync(SendPriority.High, CancellationToken.None).ContinueWith(_ => Record(order, "high"), TaskScheduler.Default);
            Assert.False(gate.TryEnter());

            for (var i = 0; i < 4; i++)
            {
                gate.Exit();
                await WaitUntil(() => Count(order) == i + 1);
            }

            gate.Exit();
            await Task.WhenAll(low, normal1, normal2, high);
            Assert.Equal(new[] { "high", "normal1", "normal2", "low" }, order);
            Assert.True(gate.TryEnter());
        }

        [Fact]
        public async Task PriorityLock_CancelledWaiterIsSkipped()
        {
            var gate = new PriorityLock();
            await gate.EnterAsync(SendPriority.Normal, CancellationToken.None);
            using (var cancel = new CancellationTokenSource())
            {
                var cancelled = gate.EnterAsync(SendPriority.High, cancel.Token);
                var waiting = gate.EnterAsync(SendPriority.Normal, CancellationToken.None);

                cancel.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
                gate.Exit();

                await WithTimeout(waiting);
                Assert.False(gate.TryEnter());
            }
        }

        private static string Record(List<string> order, string name)
        {
            lock (order)
            {
                order.Add(name);
            }

            return name;
        }

        private static int Count(List<string> order)
        {
            lock (order)
            {
                return order.Count;
            }
        }

        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        private static async Task<ZevisionClient> ConnectAsync(FakeSerialTransport transport, ConnectionOptions options)
        {
            var client = new ZevisionClient(options, _ => transport);
            Assert.True((await client.ConnectAsync()).IsSuccess);
            return client;
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("condition not met");
                }

                await Task.Delay(10);
            }
        }

        private static async Task WithTimeout(Task task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(completed == task, "等待超时。");
            await task;
        }

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            await WithTimeout((Task)task);
            return await task;
        }
    }
}
