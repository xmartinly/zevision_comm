using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using Zevision.Communication;

namespace Zevision.Tests
{
    public class ZevisionFrameDecoderTests
    {
        private static readonly FrameOptions ChecksumOnly = new FrameOptions(checksumEnabled: true, lengthEnabled: false);
        private static readonly FrameOptions LengthOnly = new FrameOptions(checksumEnabled: false, lengthEnabled: true);
        private static readonly FrameOptions LengthAndChecksum = new FrameOptions(checksumEnabled: true, lengthEnabled: true);

        [Fact]
        public void Push_CompleteFrame_ReturnsPayload()
        {
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);

            var frames = Push(decoder, "{H01+:IMC300 1.0}");

            var frame = Assert.Single(frames);
            Assert.True(frame.IsValid);
            Assert.Equal("H01+:IMC300 1.0", frame.Payload);
        }

        [Fact]
        public void Push_FrameSplitAcrossReads_IsReassembled()
        {
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);

            Assert.Empty(Push(decoder, "{H0"));
            Assert.Empty(Push(decoder, "1+:IMC"));
            var frame = Assert.Single(Push(decoder, "300}"));

            Assert.Equal("H01+:IMC300", frame.Payload);
        }

        [Fact]
        public void Push_TwoFramesInOneRead_ReturnsBothInOrder()
        {
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);

            var frames = Push(decoder, "{H01+}{H02+:1,0}");

            Assert.Equal(new[] { "H01+", "H02+:1,0" }, frames.Select(f => f.Payload));
        }

        [Fact]
        public void Push_BytesBeforeStart_ReportedAsUnexpectedData()
        {
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);

            var frames = Push(decoder, "xx{H01+}");

            Assert.Equal(2, frames.Count);
            Assert.Equal(FrameError.UnexpectedData, frames[0].Error);
            Assert.Equal("xx", Encoding.ASCII.GetString(frames[0].Raw));
            Assert.True(frames[1].IsValid);
        }

        [Fact]
        public void Push_NewStartBeforeStop_ReportsTruncatedAndKeepsNewFrame()
        {
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);

            var frames = Push(decoder, "{H0{H01+}");

            Assert.Equal(FrameError.Truncated, frames[0].Error);
            Assert.Equal("H01+", frames[1].Payload);
        }

        [Fact]
        public void Push_ValidChecksum_StripsChecksum()
        {
            var decoder = new ZevisionFrameDecoder(ChecksumOnly);
            var encoded = ZevisionFrameCodec.Encode("H02+", ChecksumOnly);

            var frame = Assert.Single(decoder.Push(encoded, 0, encoded.Length));

            Assert.True(frame.IsValid);
            Assert.Equal("H02+", frame.Payload);
        }

        [Fact]
        public void Push_WrongChecksum_ReportsMismatch()
        {
            var decoder = new ZevisionFrameDecoder(ChecksumOnly);
            var bytes = new byte[] { (byte)'{', (byte)'H', (byte)'0', (byte)'2', 0x00, (byte)'}' };

            var frame = Assert.Single(decoder.Push(bytes, 0, bytes.Length));

            Assert.Equal(FrameError.ChecksumMismatch, frame.Error);
        }

        [Fact]
        public void Push_ChecksumWithoutData_ReportsTooShort()
        {
            var decoder = new ZevisionFrameDecoder(ChecksumOnly);

            var frame = Assert.Single(Push(decoder, "{A}"));

            Assert.Equal(FrameError.TooShort, frame.Error);
        }

        [Fact]
        public void Push_LengthMode_TreatsBracesInsideDataAsData()
        {
            var decoder = new ZevisionFrameDecoder(LengthOnly);
            var bytes = new byte[] { (byte)'{', 0x00, 0x03, (byte)'a', (byte)'}', (byte)'b', (byte)'}' };

            var frame = Assert.Single(decoder.Push(bytes, 0, bytes.Length));

            Assert.True(frame.IsValid);
            Assert.Equal("a}b", frame.Payload);
        }

        [Fact]
        public void Push_LengthByteIsStopChar_StillDecodes()
        {
            // 长度 0x007D 的低字节恰好是 '}',长度模式下不能把它当作停止符。
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);
            var command = new string('A', 0x7D);
            var encoded = ZevisionFrameCodec.Encode(command, LengthAndChecksum);

            var frame = Assert.Single(decoder.Push(encoded, 0, encoded.Length));

            Assert.True(frame.IsValid);
            Assert.Equal(command, frame.Payload);
        }

        [Fact]
        public void Push_LengthMode_MissingStop_ReportsErrorAndRecovers()
        {
            var decoder = new ZevisionFrameDecoder(LengthOnly);
            var bytes = new byte[] { (byte)'{', 0x00, 0x01, (byte)'a', (byte)'{', 0x00, 0x01, (byte)'b', (byte)'}' };

            var frames = decoder.Push(bytes, 0, bytes.Length);

            Assert.Equal(FrameError.MissingStopByte, frames[0].Error);
            Assert.Equal("b", frames[1].Payload);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Push_LengthMode_PlainHelpReply_IsReadWithoutLength(bool checksum)
        {
            // 实测 IMC300 1.8.11 经 RS232 (G1304 = G1305 = 1):? 的约 97 KB 响应不带长度和校验和,也不分包。
            // 开头的 "?+" 会被当作长度 0x3F2B (16171),读完后找不到结束符;应按无长度读出整条消息,之后的帧照常解码。
            var options = new FrameOptions(checksumEnabled: checksum, lengthEnabled: true);
            var decoder = new ZevisionFrameDecoder(options);
            var payload = "?+:\n" + Encoding.ASCII.GetString(MakeText(40000));
            var bytes = Encoding.ASCII.GetBytes("{" + payload + "}").Concat(ZevisionFrameCodec.Encode("H01+:IMC300", options)).ToArray();

            var frames = PushInChunks(decoder, bytes, 4096);

            Assert.Equal(2, frames.Count);
            Assert.True(frames[0].IsValid, frames[0].Error.ToString());
            Assert.Equal(payload, frames[0].Payload);
            Assert.Empty(frames[0].DeclaredLengths);
            Assert.True(frames[1].IsValid);
            Assert.Equal("H01+:IMC300", frames[1].Payload);
        }

        [Fact]
        public void Push_LengthMode_MissingStopWithBinaryData_IsStillReported()
        {
            // 长度高字节像回显 (?),但数据中有控制字符:不是无长度的文本消息,仍按缺少结束符报告。
            var decoder = new ZevisionFrameDecoder(LengthOnly);
            var data = MakeText(0x3F00);
            data[100] = 0x01;
            var bytes = new[] { (byte)'{', (byte)'?', (byte)0x00 }.Concat(data).Concat(new[] { (byte)'x' }).ToArray();

            var frames = decoder.Push(bytes, 0, bytes.Length);

            Assert.Equal(FrameError.MissingStopByte, frames[0].Error);
        }

        [Fact]
        public void Push_ZeroLength_ReportsInvalidLength()
        {
            var decoder = new ZevisionFrameDecoder(LengthOnly);
            var bytes = new byte[] { (byte)'{', 0x00, 0x00 };

            var frame = Assert.Single(decoder.Push(bytes, 0, bytes.Length));

            Assert.Equal(FrameError.InvalidLength, frame.Error);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void Push_MultiPacketMessage_IsReassembled(bool lengthEnabled, bool checksumEnabled)
        {
            // 实测 IMC300 对 '?' 的响应:{包1> <包2> <包3},长度 56931 / 56957 / 2604。
            var options = new FrameOptions(checksumEnabled, lengthEnabled);
            var data = MakeText(56931 + 56957 + 2604);
            var bytes = BuildMessage(data, new[] { 56931, 56957, 2604 }, options);
            var decoder = new ZevisionFrameDecoder(options);

            var frame = Assert.Single(PushInChunks(decoder, bytes, 4096));

            Assert.True(frame.IsValid, frame.Error.ToString());
            Assert.Equal(3, frame.PacketCount);
            Assert.Equal(data, frame.Data);
        }

        [Fact]
        public void Push_MultiPacket_DataRangesExcludeFramingBytes()
        {
            var bytes = BuildMessage(Encoding.ASCII.GetBytes("abcdef"), new[] { 3, 3 }, LengthAndChecksum);
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);

            var frame = Assert.Single(decoder.Push(bytes, 0, bytes.Length));

            // { L L a b c CS > < L L d e f CS }
            Assert.Equal(new[] { 3, 11 }, frame.DataRanges.Select(r => r.Offset));
            Assert.All(frame.DataRanges, r => Assert.Equal(3, r.Length));
        }

        [Fact]
        public void Push_MultiPacket_BadChecksumInMiddlePacket_ReportsMismatch()
        {
            var bytes = BuildMessage(Encoding.ASCII.GetBytes("abcdef"), new[] { 3, 3 }, LengthAndChecksum);
            bytes[6] ^= 0xFF; // 第 1 包的校验和
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);

            var frame = Assert.Single(decoder.Push(bytes, 0, bytes.Length));

            Assert.Equal(FrameError.ChecksumMismatch, frame.Error);
            Assert.Equal("abcdef", frame.Payload);
        }

        [Fact]
        public void Push_LastPacketEndingWithContinueThenStop_IsAccepted()
        {
            // 实测结尾为 ...tion<C8>>} :最后一个包也以 '>' 结尾,随后是 '}'。
            var bytes = BuildMessage(Encoding.ASCII.GetBytes("abcdef"), new[] { 3, 3 }, LengthAndChecksum).ToList();
            bytes.Insert(bytes.Count - 1, (byte)'>');
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);

            var frame = Assert.Single(decoder.Push(bytes.ToArray(), 0, bytes.Count));

            Assert.True(frame.IsValid, frame.Error.ToString());
            Assert.Equal("abcdef", frame.Payload);
            Assert.Equal(new[] { 3, 3 }, frame.DeclaredLengths);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Push_NoLength_LastPacketEndingWithContinueThenStop_IsAccepted(bool checksumEnabled)
        {
            // 实机的分包结尾 "[cs] > }" 在未启用长度时也要识别,否则 '>' 被当成数据或校验和。
            var options = new FrameOptions(checksumEnabled, lengthEnabled: false);
            var bytes = BuildMessage(Encoding.ASCII.GetBytes("abcdef"), new[] { 3, 3 }, options).ToList();
            bytes.Insert(bytes.Count - 1, (byte)'>');
            var decoder = new ZevisionFrameDecoder(options);

            var frame = Assert.Single(decoder.Push(bytes.ToArray(), 0, bytes.Count));

            Assert.True(frame.IsValid, frame.Error.ToString());
            Assert.Equal("abcdef", frame.Payload);
            Assert.Equal(2, frame.PacketCount);
        }

        [Fact]
        public void Push_NoLength_SinglePacketDataEndingWithContinueByte_KeepsIt()
        {
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);
            var bytes = Encoding.ASCII.GetBytes("{E+:a>}");

            var frame = Assert.Single(decoder.Push(bytes, 0, bytes.Length));

            Assert.True(frame.IsValid, frame.Error.ToString());
            Assert.Equal("E+:a>", frame.Payload);
        }

        [Fact]
        public void Push_LastPacketLengthOvercountsTrailer_WithChecksum_IsAccepted()
        {
            // 固件缺陷:最后一个续包的长度字段 = 数据 + 2 (cs 与 '>')。
            byte cs1 = (byte)(('a' + 'b' + 'c') & 0xFF), cs2 = (byte)(('d' + 'e' + 'f') & 0xFF);
            var bytes = new byte[]
            {
                (byte)'{', 0x00, 0x03, (byte)'a', (byte)'b', (byte)'c', cs1, (byte)'>',
                (byte)'<', 0x00, 0x05, (byte)'d', (byte)'e', (byte)'f', cs2, (byte)'>', (byte)'}',
            };
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);

            var frame = Assert.Single(decoder.Push(bytes, 0, bytes.Length));

            Assert.True(frame.IsValid, frame.Error.ToString());
            Assert.Equal("abcdef", frame.Payload);
            Assert.Equal(new[] { 3, 5 }, frame.DeclaredLengths);
            Assert.Equal(new[] { 3, 3 }, frame.DataRanges.Select(r => r.Length));
        }

        [Fact]
        public void Push_LastPacketLengthOvercountsTrailer_WithoutChecksum_IsAccepted()
        {
            var bytes = new byte[]
            {
                (byte)'{', 0x00, 0x03, (byte)'a', (byte)'b', (byte)'c', (byte)'>',
                (byte)'<', 0x00, 0x04, (byte)'d', (byte)'e', (byte)'f', (byte)'>', (byte)'}',
            };
            var decoder = new ZevisionFrameDecoder(LengthOnly);

            var frame = Assert.Single(decoder.Push(bytes, 0, bytes.Length));

            Assert.True(frame.IsValid, frame.Error.ToString());
            Assert.Equal("abcdef", frame.Payload);
        }

        [Fact]
        public void Push_DataEndingWithGreaterThan_WrongChecksum_IsNotTreatedAsTrailer()
        {
            // 数据末尾恰好是 "x>",但 x 不是正确的校验和:不能误判为包尾。
            byte cs1 = (byte)(('a' + 'b' + 'c') & 0xFF);
            var bytes = new byte[]
            {
                (byte)'{', 0x00, 0x03, (byte)'a', (byte)'b', (byte)'c', cs1, (byte)'>',
                (byte)'<', 0x00, 0x03, (byte)'d', (byte)'x', (byte)'>', (byte)'}',
            };
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);

            Assert.Empty(decoder.Push(bytes, 0, bytes.Length));
            Assert.Equal(FrameError.Incomplete, Assert.Single(decoder.FlushIncomplete()).Error);
        }

        [Fact]
        public void FlushIncomplete_MidPacket_EmitsWhatWasReceived()
        {
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);
            var bytes = new byte[] { (byte)'{', 0x00, 0x0A, (byte)'a', (byte)'b', (byte)'c' };
            Assert.Empty(decoder.Push(bytes, 0, bytes.Length));

            var frame = Assert.Single(decoder.FlushIncomplete());

            Assert.Equal(FrameError.Incomplete, frame.Error);
            Assert.Equal("abc", frame.Payload);
            Assert.Equal(new[] { 10 }, frame.DeclaredLengths);
            Assert.Equal(3, Assert.Single(frame.DataRanges).Length);

            // 之后应能正常解码新帧。
            var next = ZevisionFrameCodec.Encode("H01+", LengthAndChecksum);
            Assert.True(Assert.Single(decoder.Push(next, 0, next.Length)).IsValid);
        }

        [Fact]
        public void FlushIncomplete_Idle_ReturnsNothing()
        {
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);

            Assert.Empty(decoder.FlushIncomplete());
        }

        [Fact]
        public void Push_ContinueWithoutContinuationStart_ReportsMissingContinuation()
        {
            var decoder = new ZevisionFrameDecoder(LengthOnly);
            var bytes = new byte[] { (byte)'{', 0x00, 0x01, (byte)'a', (byte)'>', (byte)'{', 0x00, 0x01, (byte)'b', (byte)'}' };

            var frames = decoder.Push(bytes, 0, bytes.Length);

            Assert.Equal(FrameError.MissingContinuation, frames[0].Error);
            Assert.Equal("b", frames[1].Payload);
        }

        [Fact]
        public void Push_PlainMode_GreaterThanNotFollowedByLessThan_IsData()
        {
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);

            var frame = Assert.Single(Push(decoder, "{G1202+:a > b}"));

            Assert.True(frame.IsValid);
            Assert.Equal(1, frame.PacketCount);
            Assert.Equal("G1202+:a > b", frame.Payload);
        }

        [Fact]
        public void Payload_DecodesUtf8()
        {
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);
            var bytes = new byte[] { (byte)'{', (byte)'A', 0xC3, 0x85, (byte)'}' };

            var frame = Assert.Single(decoder.Push(bytes, 0, bytes.Length));

            Assert.Equal("AÅ", frame.Payload);
        }

        [Fact]
        public void Push_PlainMode_AcceptsFramesLargerThanProtocolLimit()
        {
            var data = MakeText(100000);
            var bytes = BuildFrame(data, declaredLength: null, checksum: false);
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);

            var frame = Assert.Single(PushInChunks(decoder, bytes, 4096));

            Assert.True(frame.IsValid);
            Assert.Equal(100000, frame.Data.Length);
        }

        [Fact]
        public void Push_TrailingGarbage_IsReportedImmediately()
        {
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);

            var frames = Push(decoder, "{H01+}junk");

            Assert.Equal(2, frames.Count);
            Assert.Equal(FrameError.UnexpectedData, frames[1].Error);
            Assert.Equal("junk", Encoding.ASCII.GetString(frames[1].Raw));
        }

        [Fact]
        public void Reset_DiscardsPartialFrame()
        {
            var decoder = new ZevisionFrameDecoder(FrameOptions.Plain);
            Push(decoder, "{H0");

            decoder.Reset();
            var frame = Assert.Single(Push(decoder, "{H01+}"));

            Assert.True(frame.IsValid);
        }

        private static IReadOnlyList<ZevisionFrame> Push(ZevisionFrameDecoder decoder, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            return decoder.Push(bytes, 0, bytes.Length);
        }

        private static List<ZevisionFrame> PushInChunks(ZevisionFrameDecoder decoder, byte[] bytes, int chunkSize)
        {
            var frames = new List<ZevisionFrame>();
            for (var offset = 0; offset < bytes.Length; offset += chunkSize)
            {
                frames.AddRange(decoder.Push(bytes, offset, System.Math.Min(chunkSize, bytes.Length - offset)));
            }

            return frames;
        }

        /// <summary>生成类似帮助文本的多行 ASCII 数据 (不含起止符)。</summary>
        internal static byte[] MakeText(int length)
        {
            const string line = "F0A0901                       1                             Source Setting\r\n";
            var bytes = new byte[length];
            for (var i = 0; i < length; i++)
            {
                bytes[i] = (byte)line[i % line.Length];
            }

            return bytes;
        }

        /// <summary>
        /// 按设备的多包格式组消息:{包1&gt; &lt;包2&gt; … &lt;包N},每包带各自的长度和校验和。
        /// </summary>
        internal static byte[] BuildMessage(byte[] data, int[] packetLengths, FrameOptions options)
        {
            var message = new List<byte>();
            var offset = 0;
            for (var p = 0; p < packetLengths.Length; p++)
            {
                var length = packetLengths[p];
                message.Add(p == 0 ? (byte)'{' : (byte)'<');
                if (options.LengthEnabled)
                {
                    message.Add((byte)(length >> 8));
                    message.Add((byte)(length & 0xFF));
                }

                message.AddRange(data.Skip(offset).Take(length));
                if (options.ChecksumEnabled)
                {
                    message.Add(ZevisionFrameCodec.ComputeChecksum(data, offset, length));
                }

                message.Add(p == packetLengths.Length - 1 ? (byte)'}' : (byte)'>');
                offset += length;
            }

            return message.ToArray();
        }

        /// <summary>手工组帧,用于构造超过 57000 字节、编码器拒绝的帧。</summary>
        internal static byte[] BuildFrame(byte[] data, int? declaredLength, bool checksum)
        {
            var frame = new List<byte> { (byte)'{' };
            if (declaredLength.HasValue)
            {
                frame.Add((byte)(declaredLength.Value >> 8));
                frame.Add((byte)(declaredLength.Value & 0xFF));
            }

            frame.AddRange(data);
            if (checksum)
            {
                frame.Add(ZevisionFrameCodec.ComputeChecksum(data, 0, data.Length));
            }

            frame.Add((byte)'}');
            return frame.ToArray();
        }
    }
}
