using System;
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
    /// 帧格式与设备不一致时的提示 (2026-10-18 实测:程序关闭了长度和校验和,设备的 G1407/G1408 仍开着,
    /// <c>{H01}</c> 的回答 <c>{&lt;00&gt;&lt;02&gt;-Cp}</c> 只报告"无法解析响应 (5 字节)")。
    /// </summary>
    public class FramingCheckTests
    {
        private static readonly FrameOptions LengthAndChecksum = new FrameOptions(checksumEnabled: true, lengthEnabled: true);
        private static readonly byte[] RejectedWithLength = { (byte)'{', 0x00, 0x02, (byte)'-', (byte)'C', (byte)'p', (byte)'}' };

        [Fact]
        public void Plain_ReplyWithLengthAndChecksum_IsMismatch()
        {
            Assert.Equal(FramingMismatch.ReplyHasLength, Check(RejectedWithLength, FrameOptions.Plain));
            Assert.Equal(FramingMismatch.ReplyHasLength, Check(ZevisionFrameCodec.Encode("H01+:Zevision IMC300 1.8.11", LengthAndChecksum), FrameOptions.Plain));
            Assert.Equal(FramingMismatch.ReplyHasLength, Check(RejectedWithLength, new FrameOptions(checksumEnabled: true, lengthEnabled: false)));
        }

        [Theory]
        [InlineData("{H01+:Zevision}")]
        [InlineData("{-C}")]
        [InlineData("{?+:table}")]
        public void Plain_PlainReply_IsFine(string reply)
        {
            Assert.Equal(FramingMismatch.None, Check(Ascii(reply), FrameOptions.Plain));
        }

        [Fact]
        public void Length_PlainReplyReportedAsIncomplete_IsMismatch()
        {
            // "H0" 被当作长度 18480:等不到数据,超时时作为不完整数据报告。
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);
            Assert.Empty(decoder.Push(Ascii("{H01+:Zevision}"), 0, 15));

            Assert.Equal(FramingMismatch.ReplyWithoutLength, FramingCheck.Check(Assert.Single(decoder.FlushIncomplete()), LengthAndChecksum));
        }

        [Fact]
        public void Length_FramedReplies_AreFine()
        {
            var framed = ZevisionFrameCodec.Encode("H01+:Zevision", LengthAndChecksum);
            Assert.Equal(FramingMismatch.None, Check(framed, LengthAndChecksum));

            // 真正带长度、只是没收完的帧:长度的高字节不是回显。
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);
            decoder.Push(framed, 0, 8);
            Assert.Equal(FramingMismatch.None, FramingCheck.Check(Assert.Single(decoder.FlushIncomplete()), LengthAndChecksum));
        }

        [Fact]
        public async Task Send_ReplyWithLength_ErrorSaysSoWithSerialSettingIds()
        {
            var transport = new FakeSerialTransport { Responder = _ => RejectedWithLength };
            using (var service = await ConnectAsync(transport, new SerialSettings("COM_TEST", 115200), FrameOptions.Plain))
            {
                var result = await service.SendAsync("H01", CancellationToken.None);

                Assert.False(result.IsSuccess);
                Assert.StartsWith("无法解析响应 (5 字节)。设备的响应带长度字节,而程序未启用长度", result.Error);
                Assert.Contains("G1305", result.Error);
                Assert.Contains("G1304", result.Error);
            }
        }

        [Fact]
        public async Task Send_Tcp_HintNamesEthernetSettings()
        {
            var transport = new FakeSerialTransport { Responder = _ => RejectedWithLength };
            using (var service = await ConnectAsync(transport, new TcpSettings("127.0.0.1", TcpSettings.DefaultPort), FrameOptions.Plain))
            {
                var result = await service.SendAsync("H01", CancellationToken.None);

                Assert.Contains("G1408", result.Error);
                Assert.Contains("G1407", result.Error);
            }
        }

        [Fact]
        public async Task Send_FramedReplyTakenAsUnsolicited_TimeoutSaysSo()
        {
            // RS232 实测:程序未启用长度,连接后第一条的回答仍带长度和校验和,回显对不上 (显示为未请求的数据),随后超时。
            var transport = new FakeSerialTransport { Responder = _ => ZevisionFrameCodec.Encode("H01+:Zevision IMC300 1.8.11", LengthAndChecksum) };
            using (var service = await ConnectAsync(transport, new SerialSettings("COM_TEST", 115200), FrameOptions.Plain, TimeSpan.FromMilliseconds(500)))
            {
                var result = await service.SendAsync("H01", CancellationToken.None);

                Assert.StartsWith("等待响应超时", result.Error);
                Assert.Contains("设备的响应带长度字节", result.Error);
            }
        }

        [Fact]
        public async Task Send_PlainReplyInLengthMode_TimeoutSaysSo()
        {
            var transport = new FakeSerialTransport { Responder = _ => Ascii("{H01+:Zevision}") };
            using (var service = await ConnectAsync(transport, new SerialSettings("COM_TEST", 115200), LengthAndChecksum, TimeSpan.FromMilliseconds(500)))
            {
                var result = await service.SendAsync("H01", CancellationToken.None);

                Assert.StartsWith("等待响应超时", result.Error);
                Assert.Contains("设备的响应不带长度字节,而程序启用了长度", result.Error);
            }
        }

        [Fact]
        public async Task Send_OnlyTheSendThatReceivedTheMismatch_GetsTheHint()
        {
            var replies = new[] { RejectedWithLength, null };
            var count = 0;
            var transport = new FakeSerialTransport { Responder = _ => replies[Math.Min(Interlocked.Increment(ref count) - 1, 1)] };
            // 超时 500 ms:100 ms 时,机器繁忙 (整套测试并行、刚构建完) 偶尔在假串口送出回复之前就超时 (2026-10-23 的 Release 测试)。
            using (var service = await ConnectAsync(transport, new SerialSettings("COM_TEST", 115200), FrameOptions.Plain, TimeSpan.FromMilliseconds(500)))
            {
                var first = await service.SendAsync("H01", CancellationToken.None);
                var second = await service.SendAsync("H01", CancellationToken.None);

                Assert.Contains("长度字节", first.Error);
                Assert.StartsWith("等待响应超时", second.Error);
                Assert.DoesNotContain("长度字节", second.Error); // 没有收到任何数据的超时,与帧格式无关
            }
        }

        private static FramingMismatch Check(byte[] received, FrameOptions options) =>
            FramingCheck.Check(Assert.Single(new ZevisionFrameDecoder(options).Push(received, 0, received.Length)), options);

        private static async Task<ZevisionDeviceService> ConnectAsync(FakeSerialTransport transport, ConnectionEndpoint endpoint, FrameOptions frame, TimeSpan? timeout = null)
        {
            var service = new ZevisionDeviceService(transport);
            var connected = await service.ConnectAsync(new ConnectionOptions(endpoint, frame, timeout), CancellationToken.None);
            Assert.True(connected.IsSuccess, connected.Error);
            return service;
        }

        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
    }
}
