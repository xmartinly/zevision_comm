using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Communication;
using Zevision.Core.Devices;

namespace Zevision.Tests
{
    /// <summary>
    /// 错误指令数 (数据显示标题栏的 ERR):<see cref="CommandFailures"/> 的计数规则,
    /// 以及 <see cref="ZevisionDeviceService.CommandsFailed"/> 只对显示的发送触发。
    /// </summary>
    public class CommandFailuresTests
    {
        [Theory]
        [InlineData("QG01", "QG01+:x", 0)]
        [InlineData("QG01;QG03", "QG01+:x;QG03+:IMC300", 0)]
        [InlineData("QG01;QM0F01", "QG01+:x;QM0F01-D", 1)] // 一条被拒绝
        [InlineData("UG0E03:20;QG03", "UG0E03-B;QG03-A", 2)]
        [InlineData("QG01;QG03", "-C", 2)] // 不回显命令的 -C:整帧未执行
        [InlineData("QG01;QG03", "QG01+:x", 1)] // 少回一条
        [InlineData("QG01;QG01", "QG01+:x;QG01-T", 1)] // 同一指令两次,按次数对应
        [InlineData("qg01", "QG01+:x", 0)] // 回显不区分大小写
        public void Count_Replies(string command, string reply, int expected) =>
            Assert.Equal(expected, CommandFailures.Count(command, Reply(reply)));

        [Theory]
        [InlineData(ZevisionErrorKind.Timeout, 3)]
        [InlineData(ZevisionErrorKind.InvalidFrame, 3)]
        [InlineData(ZevisionErrorKind.UnparseableResponse, 3)]
        [InlineData(ZevisionErrorKind.SendFailed, 3)]
        [InlineData(ZevisionErrorKind.NotConnected, 0)] // 没有发出
        [InlineData(ZevisionErrorKind.InvalidCommand, 0)]
        [InlineData(ZevisionErrorKind.Disconnected, 0)] // 等待时断开
        public void Count_Failures(ZevisionErrorKind kind, int expected)
        {
            var failed = ZevisionResult<ZevisionReply>.Fail(new ZevisionError(kind, "x"));

            Assert.Equal(expected, CommandFailures.Count("QG01; QG03 ;QC09;", failed));
        }

        [Fact]
        public async Task Service_RaisesForVisibleSendsOnly()
        {
            var transport = new FakeSerialTransport { Responder = request => Encoding.ASCII.GetBytes("{" + Encoding.ASCII.GetString(request, 1, request.Length - 2) + "-A}") };
            using (var service = new ZevisionDeviceService(transport))
            {
                await service.ConnectAsync(new ConnectionOptions(new SerialSettings("COM_TEST", 115200), FrameOptions.Plain), CancellationToken.None);
                var events = new List<CommandsFailedEventArgs>();
                service.CommandsFailed += (s, e) => events.Add(e);

                await service.SendAsync("QX", CancellationToken.None);
                await service.SendHiddenAsync("?Z", TimeSpan.Zero, CancellationToken.None);

                var raised = Assert.Single(events);
                Assert.Equal("QX", raised.Command);
                Assert.Equal(1, raised.Count);
            }
        }

        [Fact]
        public async Task Service_HandlerException_DoesNotFailTheSend()
        {
            var errors = new FakeErrorLog();
            var transport = new FakeSerialTransport { Responder = _ => Encoding.ASCII.GetBytes("{QX-A}") };
            using (var service = new ZevisionDeviceService(transport, errors))
            {
                await service.ConnectAsync(new ConnectionOptions(new SerialSettings("COM_TEST", 115200), FrameOptions.Plain), CancellationToken.None);
                service.CommandsFailed += (s, e) => throw new InvalidOperationException("handler");

                var result = await service.SendAsync("QX", CancellationToken.None);

                Assert.True(result.IsSuccess);
                Assert.Contains("handler", Assert.Single(errors.Messages));
            }
        }

        private static ZevisionResult<ZevisionReply> Reply(string payload)
        {
            var data = Encoding.ASCII.GetBytes(payload);
            var frame = new ZevisionFrame(data, data, Array.Empty<ByteRange>(), 1, Array.Empty<int>(), FrameError.None);
            return ZevisionResult<ZevisionReply>.Ok(new ZevisionReply(frame, ZevisionResponseParser.Parse(payload).ToList()));
        }
    }
}
