using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Zevision.Xtc3;

namespace Zevision.Tests
{
    public class Xtc3ProtocolTests
    {
        [Fact]
        public void Frame_LengthLowByteFirst_ChecksumWithoutLength()
        {
            var packet = Xtc3HostCodec.EncodeCommands(new[] { Xtc3Command.Hello(1) });

            Assert.Equal(new byte[] { 0x02, 0x00, (byte)'H', 0x01, 0x49 }, packet);
        }

        [Fact]
        public void Value_IntegerAndFloat_LowByteFirst()
        {
            var buffer = new List<byte>();
            Xtc3Value.FromInteger(0x01020304).WriteTo(buffer);
            Xtc3Value.FromFloat(1.0f).WriteTo(buffer);
            Xtc3Value.FromWord(0x0102).WriteTo(buffer);
            Xtc3Value.FromString("ab").WriteTo(buffer);

            Assert.Equal(new byte[] { 4, 3, 2, 1, 0x00, 0x00, 0x80, 0x3F, 2, 1, (byte)'a', (byte)'b', 0 }, buffer);
        }

        [Fact]
        public void Decoder_PacketSplitOverReads_AndTwoPacketsInOneRead()
        {
            var decoder = new Xtc3PacketDecoder();
            var first = Xtc3HostCodec.EncodeCommands(new[] { Xtc3Command.Status(4) });
            var second = Xtc3HostCodec.EncodeCommands(new[] { Xtc3Command.Hello(1), Xtc3Command.Status(2) });

            Assert.Empty(decoder.Push(first.Take(3).ToArray(), 3, TimeSpan.Zero));
            var both = first.Skip(3).Concat(second).ToArray();
            var packets = decoder.Push(both, both.Length, TimeSpan.FromMilliseconds(10));

            Assert.Equal(2, packets.Count);
            Assert.Equal(new byte[] { (byte)'S', 4 }, packets[0].Message);
            Assert.Equal(new byte[] { (byte)'H', 1, (byte)'S', 2 }, packets[1].Message);
            Assert.Equal(0, decoder.PendingCount);
        }

        [Fact]
        public void Decoder_BadChecksum_PacketErrorC()
        {
            var packet = Xtc3HostCodec.EncodeCommands(new[] { Xtc3Command.Status(4) });
            packet[packet.Length - 1]++;

            var result = Assert.Single(new Xtc3PacketDecoder().Push(packet, packet.Length, TimeSpan.Zero));

            Assert.Equal(Xtc3PacketError.InvalidChecksum, result.Error);
        }

        [Theory]
        [InlineData(0x00, 0x00)]
        [InlineData(0xC9, 0xE1)] // 57801
        public void Decoder_LengthZeroOrTooLong_PacketErrorL_RestDiscarded(byte low, byte high)
        {
            var decoder = new Xtc3PacketDecoder();
            var bytes = new byte[] { low, high, (byte)'S', 4, 0x57 };

            var result = Assert.Single(decoder.Push(bytes, bytes.Length, TimeSpan.Zero));

            Assert.Equal(Xtc3PacketError.LengthError, result.Error);
            Assert.Equal(0, decoder.PendingCount);
        }

        [Fact]
        public void Decoder_MoreThanThreeSecondsBetweenCharacters_DiscardsPartialPacket()
        {
            var decoder = new Xtc3PacketDecoder();
            var packet = Xtc3HostCodec.EncodeCommands(new[] { Xtc3Command.Status(4) });
            decoder.Push(packet, 3, TimeSpan.FromSeconds(1));

            var packets = decoder.Push(packet, packet.Length, TimeSpan.FromSeconds(4.1));

            Assert.Equal(2, packets.Count);
            Assert.Equal(3, packets[0].Discarded.Length);
            Assert.True(packets[1].IsMessage);
        }

        [Fact]
        public void Parser_AllCommandKinds()
        {
            var commands = new[]
            {
                Xtc3Command.Echo("hi"),
                Xtc3Command.Hello(2),
                Xtc3Command.ProcessInformation(200),
                Xtc3Command.Status(35),
                Xtc3Command.Remote(11, Xtc3Value.FromFloat(42.5f)),
                Xtc3Command.Remote(17, Xtc3Value.FromByte(3)),
                Xtc3Command.Remote(0),
                Xtc3Command.QueryFilm(8, 1),
                Xtc3Command.UpdateFilm(8, 9, Xtc3Value.FromFloat(2.5f)),
                Xtc3Command.UpdateFilm(0, 2, Xtc3Value.FromInteger(30)),
                Xtc3Command.QueryGeneral(255),
                Xtc3Command.UpdateGeneral(13, 5),
            };

            var parsed = Parse(commands);

            Assert.Null(parsed.Error);
            Assert.Equal(commands.Select(c => c.ToString()), parsed.Commands.Select(c => c.ToString()));
            Assert.Equal(9, parsed.Commands[8].FilmNumber);
            Assert.All(parsed.Commands, c => Assert.Null(c.ParseError));
        }

        [Fact]
        public void Parser_UpdateFilmAll_Reads38TypedValues()
        {
            var values = Xtc3Parameters.Film.Select(p => p.Type == Xtc3ValueType.Float ? Xtc3Value.FromFloat(p.Id) : Xtc3Value.FromInteger(p.Id)).ToList();

            var parsed = Parse(new[] { Xtc3Command.UpdateFilmAll(3, values), Xtc3Command.Status(4) });

            Assert.Equal(2, parsed.Commands.Count);
            Assert.Equal(values, parsed.Commands[0].Parameters.Skip(1));
            Assert.Equal("S4", parsed.Commands[1].Name);
        }

        [Fact]
        public void Parser_IllegalIds_AnswerC_AndParsingContinues()
        {
            var message = new List<byte> { (byte)'S', 22, (byte)'R', 24, (byte)'H', 9, (byte)'Q', (byte)'F', 40, 1, (byte)'Q', (byte)'G', 16 };
            message.AddRange(new byte[] { (byte)'U', (byte)'F', 40, 1, 0, 0, 0, 0 }); // 非法 ID 的 UF 也按 4 字节值跳过
            message.AddRange(new byte[] { (byte)'U', (byte)'G', 99, 1, 0, 0, 0, (byte)'S', 4 });

            var parsed = Xtc3MessageParser.Parse(message);

            Assert.Null(parsed.Error);
            Assert.Equal(new Xtc3ErrorCode?[] { Xtc3ErrorCode.IllegalId, Xtc3ErrorCode.IllegalId, Xtc3ErrorCode.IllegalId, Xtc3ErrorCode.IllegalId, Xtc3ErrorCode.IllegalId, Xtc3ErrorCode.IllegalId, Xtc3ErrorCode.IllegalId, null },
                parsed.Commands.Select(c => c.ParseError));
        }

        [Fact]
        public void Parser_UnknownGroup_IllegalCommand_TakesTheRestOfTheMessage()
        {
            var parsed = Xtc3MessageParser.Parse(new byte[] { (byte)'S', 4, (byte)'X', 1, 2, (byte)'S', 4 });

            Assert.Equal(new Xtc3ErrorCode?[] { null, Xtc3ErrorCode.IllegalCommand }, parsed.Commands.Select(c => c.ParseError));
        }

        [Fact]
        public void Parser_Xtc3MOnlyAndBlockQueries_IllegalCommand_LengthKnown()
        {
            var parsed = Xtc3MessageParser.Parse(new byte[] { (byte)'Q', (byte)'N', 1, (byte)'Q', (byte)'B', 1, (byte)'Q', (byte)'P', 3, 5, 0, 1, (byte)'S', 4 });

            Assert.Equal(new Xtc3ErrorCode?[] { Xtc3ErrorCode.IllegalCommand, Xtc3ErrorCode.IllegalCommand, Xtc3ErrorCode.IllegalCommand, null },
                parsed.Commands.Select(c => c.ParseError));
        }

        [Theory]
        [InlineData(new byte[] { (byte)'S' })]
        [InlineData(new byte[] { (byte)'S', 0 })]
        [InlineData(new byte[] { (byte)'E', (byte)'a' })]
        [InlineData(new byte[] { (byte)'U', (byte)'F', 8, 1, 0, 0 })]
        [InlineData(new byte[] { (byte)'R', 11, 0, 0, 0 })]
        public void Parser_MissingBytes_PacketErrorF(byte[] message)
        {
            Assert.Equal(Xtc3PacketError.IllegalFormat, Xtc3MessageParser.Parse(message).Error);
        }

        [Fact]
        public void Parser_MoreThan100Commands_PacketErrorM()
        {
            Assert.Null(Parse(Enumerable.Repeat(Xtc3Command.Status(4), 100)).Error);
            Assert.Equal(Xtc3PacketError.TooManyCommands, Parse(Enumerable.Repeat(Xtc3Command.Status(4), 101)).Error);
        }

        [Fact]
        public void Response_AckWithValues_ErrorCodes_RoundTrip()
        {
            var commands = new[] { Xtc3Command.ProcessInformation(7), Xtc3Command.QueryFilm(99, 1), Xtc3Command.Status(4), Xtc3Command.Status(35) };
            var results = new[]
            {
                Xtc3CommandResult.Ok(
                    Xtc3Value.FromByte(1), Xtc3Value.FromByte(7), Xtc3Value.FromByte(1), Xtc3Value.FromByte(1),
                    Xtc3Value.FromFloat(2.5f), Xtc3Value.FromFloat(0.1f), Xtc3Value.FromFloat(30f), Xtc3Value.FromFloat(-0.2f), Xtc3Value.FromInteger(1717986918)),
                Xtc3CommandResult.Fail(Xtc3ErrorCode.IllegalId),
                Xtc3CommandResult.Fail(Xtc3ErrorCode.PriorCommandFailed),
                Xtc3CommandResult.Fail(Xtc3ErrorCode.PriorCommandFailed),
            };

            var packet = Xtc3ResponseEncoder.Encode(0x42, results);
            var message = Assert.Single(new Xtc3PacketDecoder().Push(packet, packet.Length, TimeSpan.Zero)).Message;
            var decoded = Xtc3HostCodec.DecodeResponse(message, commands);

            Assert.Equal(0, decoded.ConditionCode);
            Assert.Equal(0x42, decoded.Timer);
            Assert.Equal(results.Select(r => r.ToString()), decoded.Results.Select(r => r.ToString()));
            Assert.Equal(new byte[] { 0, 0x42, Xtc3Protocol.Ack, 1, 7 }, message.Take(5));
            Assert.Equal((byte)'C', message[message.Length - 3]);
        }

        [Fact]
        public void Response_PacketError_CcbMsbSet_SingleCode()
        {
            var packet = Xtc3ResponseEncoder.EncodePacketError(9, Xtc3PacketError.InvalidChecksum);

            Assert.Equal(new byte[] { 3, 0, 0x80, 9, (byte)'C', (byte)(0x80 + 9 + 'C') }, packet);
            var decoded = Xtc3HostCodec.DecodeResponse(packet.Skip(2).Take(3).ToArray(), Array.Empty<Xtc3Command>());
            Assert.Equal(Xtc3PacketError.InvalidChecksum, decoded.PacketError);
        }

        [Fact]
        public void Response_TooLong_NoRoomThenPrior()
        {
            var big = Xtc3CommandResult.Ok(Xtc3Value.FromBytes(new byte[30000]));
            var packet = Xtc3ResponseEncoder.Encode(0, new[] { big, big, Xtc3CommandResult.Ok() });

            var message = Assert.Single(new Xtc3PacketDecoder().Push(packet, packet.Length, TimeSpan.Zero)).Message;

            Assert.Equal(new byte[] { (byte)'O', (byte)'P' }, message.Skip(message.Length - 2));
            Assert.True(message.Length <= Xtc3Protocol.MaxLength);
        }

        [Theory]
        [InlineData(18, 0, true)]
        [InlineData(18, 10, false)]
        [InlineData(18, 25, true)]
        [InlineData(15, 3, false)]
        [InlineData(13, 0, false)]
        public void Parameter_IntegerRanges(int id, int value, bool valid)
        {
            Assert.Equal(valid, Xtc3Parameters.Film[id].IsValid(Xtc3Value.FromInteger(value)));
        }

        [Theory]
        [InlineData(1, 100.00001f, true)] // 单精度的 100.0
        [InlineData(1, 100.06f, false)]
        [InlineData(28, 0.495f, true)] // 两位小数四舍五入为 0.50
        [InlineData(28, float.NaN, false)]
        [InlineData(9, 999.9f, true)]
        public void Parameter_FloatRanges_RoundedToDisplayDecimals(int id, float value, bool valid)
        {
            Assert.Equal(valid, Xtc3Parameters.Film[id].IsValid(Xtc3Value.FromFloat(value)));
        }

        [Fact]
        public void Parameter_WrongType_Invalid()
        {
            Assert.False(Xtc3Parameters.Film[8].IsValid(Xtc3Value.FromInteger(2)));
            Assert.False(Xtc3Parameters.Film[0].IsValid(Xtc3Value.FromFloat(2)));
        }

        [Fact]
        public void Tables_IdsMatchIndexes_AndEnums()
        {
            Assert.Equal(38, Xtc3Parameters.Film.Count);
            Assert.Equal(16, Xtc3Parameters.General.Count);
            Assert.All(Xtc3Parameters.Film, p => Assert.Equal(p.Id, Xtc3Parameters.Film.ToList().IndexOf(p)));
            Assert.All(Enum.GetValues(typeof(Xtc3FilmParameterId)).Cast<int>(), id => Assert.NotNull(Xtc3Parameters.FindFilm(id)));
            Assert.All(Enum.GetValues(typeof(Xtc3GeneralParameterId)).Cast<int>(), id => Assert.NotNull(Xtc3Parameters.FindGeneral(id)));
        }

        [Fact]
        public void StatusMessages_AreDistinctSingleBits()
        {
            var bits = Enum.GetValues(typeof(Xtc3StatusMessages)).Cast<uint>().Where(b => b != 0).ToList();

            Assert.Equal(bits.Count, bits.Distinct().Count());
            Assert.All(bits, b => Assert.Equal(0u, b & (b - 1)));
        }

        private static Xtc3ParsedMessage Parse(IEnumerable<Xtc3Command> commands)
        {
            var packet = Xtc3HostCodec.EncodeCommands(commands);
            return Xtc3MessageParser.Parse(packet.Skip(2).Take(packet.Length - 3).ToArray());
        }
    }
}
