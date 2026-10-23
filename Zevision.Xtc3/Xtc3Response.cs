using System;
using System.Collections.Generic;
using System.Linq;

namespace Zevision.Xtc3
{
    /// <summary>
    /// 一条命令的执行结果:成功 (ACK 加响应数据) 或错误码。
    /// </summary>
    public sealed class Xtc3CommandResult
    {
        private static readonly Xtc3CommandResult EmptyOk = new Xtc3CommandResult(null, Array.Empty<Xtc3Value>());

        private Xtc3CommandResult(Xtc3ErrorCode? error, IReadOnlyList<Xtc3Value> values)
        {
            Error = error;
            Values = values;
        }

        /// <summary>是否成功。</summary>
        public bool IsSuccess => Error == null;

        /// <summary>错误码;成功时为 null。</summary>
        public Xtc3ErrorCode? Error { get; }

        /// <summary>响应数据,按顺序;失败时为空。</summary>
        public IReadOnlyList<Xtc3Value> Values { get; }

        /// <summary>成功,带响应数据。</summary>
        public static Xtc3CommandResult Ok(params Xtc3Value[] values) =>
            values == null || values.Length == 0 ? EmptyOk : new Xtc3CommandResult(null, (Xtc3Value[])values.Clone());

        /// <summary>成功,带响应数据。</summary>
        public static Xtc3CommandResult Ok(IEnumerable<Xtc3Value> values) => Ok((values ?? throw new ArgumentNullException(nameof(values))).ToArray());

        /// <summary>失败。</summary>
        public static Xtc3CommandResult Fail(Xtc3ErrorCode error) => new Xtc3CommandResult(error, Array.Empty<Xtc3Value>());

        /// <inheritdoc/>
        public override string ToString() => IsSuccess
            ? "ACK" + (Values.Count > 0 ? " [" + string.Join(", ", Values) + "]" : string.Empty)
            : ((char)Error!.Value).ToString();
    }

    /// <summary>
    /// 响应包的编码:<c>&lt;Length&gt;&lt;CCB&gt;&lt;Timer&gt;&lt;Response Message&gt;&lt;Checksum&gt;</c>。
    /// </summary>
    /// <remarks>
    /// 命令包有效时 CCB 为 0,响应消息为各命令的响应,末尾不加包错误码 (手册未写明,ZEV-FAR-01 的 8.2 c);
    /// 包错误时 CCB 为 <see cref="Xtc3Protocol.PacketErrorFlag"/>,响应消息只有包错误码。
    /// </remarks>
    public static class Xtc3ResponseEncoder
    {
        /// <summary>
        /// 编码各命令的结果。响应超过 <see cref="Xtc3Protocol.MaxLength"/> 时,放不下的命令改为
        /// <see cref="Xtc3ErrorCode.NoRoomForResponse"/>,其后的命令改为 <see cref="Xtc3ErrorCode.PriorCommandFailed"/>。
        /// </summary>
        /// <param name="timer">当前 tick。</param>
        /// <param name="results">各命令的结果,与命令一一对应。</param>
        public static byte[] Encode(byte timer, IReadOnlyList<Xtc3CommandResult> results)
        {
            if (results is null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            var message = new List<byte> { 0, timer };
            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                if (!result.IsSuccess)
                {
                    message.Add((byte)result.Error!.Value);
                    continue;
                }

                // 加上本条和其后每条至少 1 字节 (错误码) 后不超过上限。
                var size = 1 + result.Values.Sum(v => v.Size);
                if (message.Count + size + (results.Count - i - 1) > Xtc3Protocol.MaxLength)
                {
                    message.Add((byte)Xtc3ErrorCode.NoRoomForResponse);
                    message.AddRange(Enumerable.Repeat((byte)Xtc3ErrorCode.PriorCommandFailed, results.Count - i - 1));
                    break;
                }

                message.Add(Xtc3Protocol.Ack);
                foreach (var value in result.Values)
                {
                    value.WriteTo(message);
                }
            }

            return Xtc3Protocol.Frame(message);
        }

        /// <summary>编码包错误的响应:CCB 最高位置位,响应消息只有包错误码。</summary>
        /// <param name="timer">当前 tick。</param>
        /// <param name="error">包错误。</param>
        public static byte[] EncodePacketError(byte timer, Xtc3PacketError error) =>
            Xtc3Protocol.Frame(new[] { Xtc3Protocol.PacketErrorFlag, timer, (byte)error });
    }

    /// <summary>
    /// 主机侧解出的响应包。
    /// </summary>
    public sealed class Xtc3ResponsePacket
    {
        internal Xtc3ResponsePacket(byte conditionCode, byte timer, IReadOnlyList<Xtc3CommandResult> results, Xtc3PacketError? packetError)
        {
            ConditionCode = conditionCode;
            Timer = timer;
            Results = results;
            PacketError = packetError;
        }

        /// <summary>CCB。</summary>
        public byte ConditionCode { get; }

        /// <summary>Timer (tick)。</summary>
        public byte Timer { get; }

        /// <summary>各命令的结果;包错误时为空。</summary>
        public IReadOnlyList<Xtc3CommandResult> Results { get; }

        /// <summary>包错误 (CCB 最高位置位时)。</summary>
        public Xtc3PacketError? PacketError { get; }
    }

    /// <summary>
    /// 主机侧的编码和解码:把命令编成命令包,按发出的命令解析响应包的消息。用于测试和调试工具。
    /// </summary>
    public static class Xtc3HostCodec
    {
        /// <summary>把命令编成一个命令包 (长度、命令、校验和)。</summary>
        /// <param name="commands">命令,按顺序。</param>
        public static byte[] EncodeCommands(IEnumerable<Xtc3Command> commands)
        {
            if (commands is null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            var message = new List<byte>();
            foreach (var command in commands)
            {
                command.WriteTo(message);
            }

            return Xtc3Protocol.Frame(message);
        }

        /// <summary>
        /// 解析响应包的消息 (<see cref="Xtc3PacketDecoder"/> 解出的 CCB、Timer 和响应消息)。
        /// 成功的命令按 <see cref="Xtc3Syntax.ResponseLayout"/> 读取数据。
        /// </summary>
        /// <param name="message">响应包的消息。</param>
        /// <param name="commands">发出的命令,按顺序。</param>
        /// <exception cref="FormatException">消息与命令不符 (太短、多余的字节、无法识别的应答字节)。</exception>
        public static Xtc3ResponsePacket DecodeResponse(IReadOnlyList<byte> message, IReadOnlyList<Xtc3Command> commands)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (commands is null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            if (message.Count < 2)
            {
                throw new FormatException("A response holds at least the CCB and the timer.");
            }

            var ccb = message[0];
            var timer = message[1];
            if ((ccb & Xtc3Protocol.PacketErrorFlag) != 0)
            {
                if (message.Count != 3)
                {
                    throw new FormatException("A packet error response holds exactly one error code.");
                }

                return new Xtc3ResponsePacket(ccb, timer, Array.Empty<Xtc3CommandResult>(), (Xtc3PacketError)message[2]);
            }

            var results = new List<Xtc3CommandResult>();
            var position = 2;
            foreach (var command in commands)
            {
                if (position >= message.Count)
                {
                    throw new FormatException("The response ends before the response to " + command.Name + ".");
                }

                var first = message[position++];
                if (first != Xtc3Protocol.Ack)
                {
                    if (!Enum.IsDefined(typeof(Xtc3ErrorCode), first))
                    {
                        throw new FormatException("Unknown response byte 0x" + first.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + " for " + command.Name + ".");
                    }

                    results.Add(Xtc3CommandResult.Fail((Xtc3ErrorCode)first));
                    continue;
                }

                var values = new List<Xtc3Value>();
                foreach (var type in Xtc3Syntax.ResponseLayout(command))
                {
                    if (!Xtc3Value.TryRead(message, ref position, type, Xtc3Syntax.EthernetParametersLength, out var value))
                    {
                        throw new FormatException("The response to " + command.Name + " is too short.");
                    }

                    values.Add(value);
                }

                results.Add(Xtc3CommandResult.Ok(values));
            }

            if (position != message.Count)
            {
                throw new FormatException("The response has " + (message.Count - position) + " extra bytes.");
            }

            return new Xtc3ResponsePacket(ccb, timer, results, null);
        }
    }
}
