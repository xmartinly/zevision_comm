using System;
using System.Linq;
using System.Text;
using Xunit;
using Zevision.Communication;
using Zevision.Core.Devices;

namespace Zevision.Tests
{
    /// <summary>
    /// 帧格式的识别 (<see cref="FrameFormatDetector"/>)、帧格式设置的修改 (<see cref="FrameSettings"/>)
    /// 和自动重连的等待 (<see cref="ReconnectPolicy"/>)。识别用的字节是 2026-10-19 实机 (IMC300 1.8.11) 的原始回答。
    /// </summary>
    public class FrameFormatTests
    {
        private static readonly FrameOptions LengthAndChecksum = new FrameOptions(checksumEnabled: true, lengthEnabled: true);
        private static readonly FrameOptions LengthOnly = new FrameOptions(checksumEnabled: false, lengthEnabled: true);
        private static readonly FrameOptions ChecksumOnly = new FrameOptions(checksumEnabled: true, lengthEnabled: false);

        public static TheoryData<string, byte[], FrameOptions> RealReplies => new TheoryData<string, byte[], FrameOptions>
        {
            // RS232,设备无长度无校验
            { "plain H01", Ascii("{H01+:Zevision IMC300 1.8.11.202606101229}"), FrameOptions.Plain },
            { "plain, request had length+checksum", Bytes(0x7B, 0, 0, 0, 0, 0, 0, 0x2D, 0x41, 0x7D), FrameOptions.Plain },
            { "plain, request had length", Bytes(0x7B, 0, 0, 0, 0, 0, 0x2D, 0x41, 0x7D), FrameOptions.Plain },
            { "plain, request had checksum", Bytes(0x7B, 0x48, 0x30, 0x31, 0xA9, 0x2D, 0x44, 0x7D), FrameOptions.Plain },

            // RS232,设备只开长度 (G1305 = 1)
            { "length H01", Concat(Bytes(0x7B, 0x00, 0x28), Ascii("H01+:Zevision IMC300 1.8.11.202606101229}")), LengthOnly },
            { "length, plain request", Bytes(0x7B, 0x00, 0x02, 0x2D, 0x54, 0x7D), LengthOnly },

            // RS232,设备只开校验和 (G1304 = 1)
            { "checksum H01", Concat(Ascii("{H01+:Zevision IMC300 1.8.11.202606101229"), Bytes(0xC5, 0x7D)), ChecksumOnly },
            { "checksum, plain request", Ascii("{-Cp}"), ChecksumOnly },

            // TCP,G1407 = G1408 = 1
            { "length+checksum H01", Concat(Bytes(0x7B, 0x00, 0x28), Ascii("H01+:Zevision IMC300 1.8.11.202606101229"), Bytes(0xC5, 0x7D)), LengthAndChecksum },
            { "length+checksum, wrong request", Bytes(0x7B, 0x00, 0x02, 0x2D, 0x43, 0x70, 0x7D), LengthAndChecksum },
            { "length+checksum status", Concat(Bytes(0x7B, 0x00, 0x1D), Ascii("SC11+:B8,0.00;SC12+:B7,0.0000"), Bytes(0x1D, 0x7D)), LengthAndChecksum },
        };

        [Theory]
        [MemberData(nameof(RealReplies))]
        public void Detect_RealDeviceReplies(string name, byte[] received, FrameOptions expected)
        {
            Assert.True(expected.Equals(FrameFormatDetector.Detect(received)), name);
        }

        [Fact]
        public void Detect_SeveralRepliesAndLeadingNoise()
        {
            var reply = Bytes(0x7B, 0x00, 0x02, 0x2D, 0x43, 0x70, 0x7D);
            var received = Concat(Ascii("\r\n"), reply, reply);

            Assert.Equal(LengthAndChecksum, FrameFormatDetector.Detect(received));
        }

        [Theory]
        [InlineData("")]
        [InlineData("xyz")]
        [InlineData("{garbage}")]
        public void Detect_Unrecognisable_ReturnsNull(string received) =>
            Assert.Null(FrameFormatDetector.Detect(Ascii(received)));

        [Fact]
        public void Detect_AllFormatsAreCandidates() =>
            Assert.Equal(4, FrameFormatDetector.All.Distinct().Count());

        // ---------- 帧格式设置 ----------

        [Fact]
        public void Ids_PerInterface()
        {
            Assert.Equal(("G1304", "G1305"), FrameSettings.Ids(new SerialSettings("COM1", 115200)));
            Assert.Equal(("G1407", "G1408"), FrameSettings.Ids(new TcpSettings("10.0.0.1", TcpSettings.DefaultPort)));
        }

        [Fact]
        public void AfterUpdates_AcceptedChange_ReturnsNewFormat()
        {
            var changed = FrameSettings.AfterUpdates(new SerialSettings("COM1", 115200), FrameOptions.Plain, new[] { "UG1304:1", "QG01" }, Responses("UG1304+;QG01+:x"));

            Assert.Equal(ChecksumOnly, changed);
        }

        [Fact]
        public void AfterUpdates_TcpUsesItsOwnSettings()
        {
            var tcp = new TcpSettings("10.0.0.1", TcpSettings.DefaultPort);

            Assert.Null(FrameSettings.AfterUpdates(tcp, LengthAndChecksum, new[] { "UG1305:0" }, Responses("UG1305+")));
            Assert.Equal(ChecksumOnly, FrameSettings.AfterUpdates(tcp, LengthAndChecksum, new[] { "UG1408:0" }, Responses("UG1408+")));
        }

        [Theory]
        [InlineData("UG1305:1", "UG1305-B")] // 设备拒绝
        [InlineData("UG1305:0", "UG1305+")] // 值没有变
        [InlineData("QG1305", "QG1305+:1")] // 只是查询
        [InlineData("UG0E03:1", "UG0E03+")] // 其他设置
        public void AfterUpdates_NoChange_ReturnsNull(string command, string reply) =>
            Assert.Null(FrameSettings.AfterUpdates(new SerialSettings("COM1", 115200), FrameOptions.Plain, new[] { command }, Responses(reply)));

        [Fact]
        public void AfterUpdates_BothSettingsInOneFrame()
        {
            var changed = FrameSettings.AfterUpdates(new SerialSettings("COM1", 115200), FrameOptions.Plain, new[] { "UG1304:1", "UG1305:1" }, Responses("UG1304+;UG1305+"));

            Assert.Equal(LengthAndChecksum, changed);
        }

        // ---------- 自动重连 ----------

        [Fact]
        public void Reconnect_ExponentialBackoffUpToOneMinute()
        {
            var delays = Enumerable.Range(1, 9).Select(attempt => ReconnectPolicy.DelayBefore(attempt).TotalSeconds);

            Assert.Equal(new double[] { 1, 2, 4, 8, 16, 32, 60, 60, 60 }, delays);
            Assert.Equal(60, ReconnectPolicy.DelayBefore(int.MaxValue).TotalSeconds);
            Assert.Throws<ArgumentOutOfRangeException>(() => ReconnectPolicy.DelayBefore(0));
        }

        private static System.Collections.Generic.IReadOnlyList<ZevisionResponse> Responses(string payload) => ZevisionResponseParser.Parse(payload);

        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        private static byte[] Bytes(params int[] values) => values.Select(v => (byte)v).ToArray();

        private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    }
}
