using System;
using System.Text;
using Xunit;
using Zevision.Communication;

namespace Zevision.Tests
{
    public class ZevisionFrameCodecTests
    {
        [Fact]
        public void Encode_Plain_WrapsWithBraces()
        {
            var frame = ZevisionFrameCodec.Encode("H01", FrameOptions.Plain);

            Assert.Equal("{H01}", Encoding.ASCII.GetString(frame));
        }

        [Fact]
        public void ComputeChecksum_H02_Is170()
        {
            // 'H'(72) + '0'(48) + '2'(50) = 170 = 0xAA。协议文档中写成 "y" 是错误的。
            var data = Encoding.ASCII.GetBytes("H02");

            Assert.Equal(0xAA, ZevisionFrameCodec.ComputeChecksum(data, 0, data.Length));
        }

        [Fact]
        public void ComputeChecksum_WrapsModulo256()
        {
            var data = new byte[] { 0xFF, 0x02 };

            Assert.Equal(0x01, ZevisionFrameCodec.ComputeChecksum(data, 0, data.Length));
        }

        [Fact]
        public void Encode_WithChecksum_AppendsChecksumBeforeStop()
        {
            var frame = ZevisionFrameCodec.Encode("H02", new FrameOptions(checksumEnabled: true, lengthEnabled: false));

            Assert.Equal(new byte[] { (byte)'{', (byte)'H', (byte)'0', (byte)'2', 0xAA, (byte)'}' }, frame);
        }

        [Fact]
        public void Encode_WithLength_PrependsBigEndianLength()
        {
            var frame = ZevisionFrameCodec.Encode("H01", new FrameOptions(checksumEnabled: false, lengthEnabled: true));

            Assert.Equal(new byte[] { (byte)'{', 0x00, 0x03, (byte)'H', (byte)'0', (byte)'1', (byte)'}' }, frame);
        }

        [Fact]
        public void Encode_WithLengthAndChecksum_HasBoth()
        {
            var frame = ZevisionFrameCodec.Encode("H02", new FrameOptions(checksumEnabled: true, lengthEnabled: true));

            Assert.Equal(new byte[] { (byte)'{', 0x00, 0x03, (byte)'H', (byte)'0', (byte)'2', 0xAA, (byte)'}' }, frame);
        }

        [Theory]
        [InlineData("")]
        [InlineData("H{01")]
        [InlineData("H01}")]
        [InlineData("名称")]
        [InlineData("H01\r")]
        public void Encode_InvalidCommand_Throws(string command)
        {
            Assert.Throws<ArgumentException>(() => ZevisionFrameCodec.Encode(command, FrameOptions.Plain));
        }

        [Fact]
        public void Encode_TooLong_Throws()
        {
            var command = new string('A', ZevisionFrameCodec.MaxDataLength + 1);

            Assert.Throws<ArgumentException>(() => ZevisionFrameCodec.Encode(command, FrameOptions.Plain));
        }

        [Fact]
        public void EncodeAvoidingDelimiters_ChecksumEqualToStopByte_AppendsFiller()
        {
            // 实机失败的指令:AL0702010600 的校验和正好是 0x7D ('}'),设备回复 -C。
            var options = new FrameOptions(checksumEnabled: true, lengthEnabled: true);
            var plain = ZevisionFrameCodec.Encode("AL0702010600", options);
            Assert.Equal(0x7D, plain[plain.Length - 2]);

            var frame = ZevisionFrameCodec.EncodeAvoidingDelimiters("AL0702010600", options, out var sent);

            Assert.Equal("AL0702010600;H01", sent);
            Assert.False(ZevisionFrameCodec.HasDelimiterInBinaryFields(frame, options));
            Assert.Equal(ZevisionFrameCodec.Encode(sent, options), frame);
        }

        [Theory]
        [InlineData("H01")]
        [InlineData("AL0702010601")]
        [InlineData("UF0302:3")]
        public void EncodeAvoidingDelimiters_NoConflict_LeavesCommandUnchanged(string command)
        {
            var options = new FrameOptions(checksumEnabled: true, lengthEnabled: true);

            var frame = ZevisionFrameCodec.EncodeAvoidingDelimiters(command, options, out var sent);

            Assert.Equal(command, sent);
            Assert.Equal(ZevisionFrameCodec.Encode(command, options), frame);
        }

        [Fact]
        public void HasDelimiterInBinaryFields_LengthByte_IsDetected()
        {
            // 长度 125 = 0x007D:低字节等于 '}'。
            var options = new FrameOptions(checksumEnabled: false, lengthEnabled: true);
            var frame = ZevisionFrameCodec.Encode("UF0101:" + new string('a', 118), options);

            Assert.True(ZevisionFrameCodec.HasDelimiterInBinaryFields(frame, options));
            ZevisionFrameCodec.EncodeAvoidingDelimiters("UF0101:" + new string('a', 118), options, out var sent);
            Assert.EndsWith(";H01", sent);
        }

        [Fact]
        public void HasDelimiterInBinaryFields_PlainFrame_NeverConflicts()
        {
            // 不带长度和校验和时,帧中没有二进制字段。
            var frame = ZevisionFrameCodec.Encode("AL0702010600", FrameOptions.Plain);

            Assert.False(ZevisionFrameCodec.HasDelimiterInBinaryFields(frame, FrameOptions.Plain));
        }
    }
}
