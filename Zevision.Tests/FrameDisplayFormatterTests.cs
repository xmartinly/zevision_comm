using System.Text;
using Xunit;
using Zevision.Communication;
using Zevision.ViewModels;

namespace Zevision.Tests
{
    public class FrameDisplayFormatterTests
    {
        [Fact]
        public void ToDisplayLines_SplitsOnLineBreaksInsideData()
        {
            var raw = Encoding.ASCII.GetBytes("{?+:a\r\nb\nc}");

            var lines = FrameDisplayFormatter.ToDisplayLines(raw, new[] { new ByteRange(1, raw.Length - 2) }, 100);

            Assert.Equal(new[] { "{?+:a", "b", "c}" }, lines);
        }

        [Fact]
        public void ToDisplayLines_LengthByteEqualToLineFeed_IsEscapedNotSplit()
        {
            // 长度 0x000A 的低字节是 LF,但它在帧头,不应换行。
            var options = new FrameOptions(checksumEnabled: false, lengthEnabled: true);
            var raw = ZevisionFrameCodec.Encode("H01+:12345", options);

            var lines = FrameDisplayFormatter.ToDisplayLines(raw, new[] { new ByteRange(3, 10) }, 100);

            Assert.Equal("{<00><0A>H01+:12345}", Assert.Single(lines));
        }

        [Fact]
        public void ToDisplayLines_MultiPacket_ShowsFramingBytesInline()
        {
            // 实测的包边界:...Material " > < DE 7D \n F2003...
            var raw = new byte[] { (byte)'{', 0x00, 0x01, (byte)'M', 0x4D, (byte)'>', (byte)'<', 0xDE, 0x7D, (byte)'\n', (byte)'F', 0x00, (byte)'}' };
            var ranges = new[] { new ByteRange(3, 1), new ByteRange(9, 2) };

            var lines = FrameDisplayFormatter.ToDisplayLines(raw, ranges, 100);

            Assert.Equal(new[] { "{<00><01>MM><<DE>}", "F<00>}" }, lines);
        }

        [Fact]
        public void ToDisplayLines_DecodesUtf8InsideDataOnly()
        {
            // 数据区内的 C3 85 是 'Å';帧尾的 C3 不是数据,按字节显示。
            var raw = new byte[] { (byte)'{', (byte)'(', 0xC3, 0x85, (byte)'/', (byte)'s', (byte)')', 0xC3, (byte)'}' };

            var lines = FrameDisplayFormatter.ToDisplayLines(raw, new[] { new ByteRange(1, 6) }, 100);

            Assert.Equal("{(Å/s)<C3>}", Assert.Single(lines));
        }

        [Fact]
        public void ToDisplayLines_WrapsLongLines()
        {
            var raw = Encoding.ASCII.GetBytes("{" + new string('x', 25) + "}");

            var lines = FrameDisplayFormatter.ToDisplayLines(raw, new[] { new ByteRange(1, 25) }, 10);

            Assert.Equal(new[] { "{xxxxxxxxx", "xxxxxxxxxx", "xxxxxx}" }, lines);
        }

        [Fact]
        public void ToDisplayString_EscapesNonPrintableBytes()
        {
            var text = FrameDisplayFormatter.ToDisplayString(new byte[] { (byte)'{', 0x00, 0x03, (byte)'H', 0xAA, (byte)'}' });

            Assert.Equal("{<00><03>H<AA>}", text);
        }
    }
}
