using System;
using System.Collections.Generic;

namespace Zevision.Xtc3
{
    /// <summary>
    /// XTC/3 Standard Protocol 的常量和校验和 (docs/XTC3.md 第 1 章)。
    /// </summary>
    public static class Xtc3Protocol
    {
        /// <summary>包长度字段的最大值 (命令包为消息的字节数,响应包为 CCB、Timer 和响应消息的字节数)。</summary>
        public const int MaxLength = 57800;

        /// <summary>一个命令包中命令的最大条数;超过时整包回答包错误 <see cref="Xtc3PacketError.TooManyCommands"/>。</summary>
        public const int MaxCommands = 100;

        /// <summary>命令成功时响应前的肯定应答字节 (ASCII 6)。命令失败时不发送,只发送错误码。</summary>
        public const byte Ack = 0x06;

        /// <summary>CCB 的最高位:命令包有错误,响应消息只有一个包错误码。</summary>
        public const byte PacketErrorFlag = 0x80;

        /// <summary>XTC/3S 的 Film 数量,Film 编号为 1~9。</summary>
        public const int FilmCount = 9;

        /// <summary>DDS 频率单位换算为 Hz 的系数 (S0、S13 的 Frequency 乘以它为 Hz)。</summary>
        public const double HertzPerDdsUnit = 0.0034924596;

        /// <summary>命令包字符之间的最长间隔;超过时丢弃已收到的部分,不回答,后续字符作为新包的开始。</summary>
        public static readonly TimeSpan InterCharacterTimeout = TimeSpan.FromSeconds(3);

        /// <summary>Timer (tick) 每隔这么长时间加 1,0~255 循环。</summary>
        public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

        /// <summary>校验和:各字节之和取模 256 (不含长度字节)。</summary>
        /// <param name="data">数据。</param>
        /// <param name="offset">起始位置。</param>
        /// <param name="count">字节数。</param>
        public static byte Checksum(IReadOnlyList<byte> data, int offset, int count)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (offset < 0 || count < 0 || offset + count > data.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var sum = 0;
            for (var i = offset; i < offset + count; i++)
            {
                sum += data[i];
            }

            return (byte)sum;
        }

        /// <summary>给消息加上长度 (2 字节,低字节在前) 和校验和,成为一个包。</summary>
        /// <param name="message">消息 (命令包的命令,或响应包的 CCB、Timer 和响应消息)。</param>
        /// <exception cref="ArgumentException">消息为空或超过 <see cref="MaxLength"/>。</exception>
        public static byte[] Frame(IReadOnlyList<byte> message)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (message.Count == 0 || message.Count > MaxLength)
            {
                throw new ArgumentException("A packet must hold 1 to " + MaxLength + " bytes; it holds " + message.Count + ".", nameof(message));
            }

            var packet = new byte[message.Count + 3];
            packet[0] = (byte)(message.Count & 0xFF);
            packet[1] = (byte)(message.Count >> 8);
            for (var i = 0; i < message.Count; i++)
            {
                packet[i + 2] = message[i];
            }

            packet[packet.Length - 1] = Checksum(message, 0, message.Count);
            return packet;
        }
    }

    /// <summary>
    /// 命令的错误码 (Response Error Code)。命令失败时响应中只有这个字节,没有 ACK。
    /// </summary>
    public enum Xtc3ErrorCode : byte
    {
        /// <summary>A:非法命令。</summary>
        IllegalCommand = (byte)'A',

        /// <summary>B:非法参数值。</summary>
        IllegalValue = (byte)'B',

        /// <summary>C:非法 ID。</summary>
        IllegalId = (byte)'C',

        /// <summary>D:非法格式。</summary>
        IllegalFormat = (byte)'D',

        /// <summary>E:数据不可用。</summary>
        DataNotAvailable = (byte)'E',

        /// <summary>F:当前不能执行 (状态限制)。</summary>
        CannotDoNow = (byte)'F',

        /// <summary>L:长度错误。</summary>
        LengthError = (byte)'L',

        /// <summary>O:响应空间不足。</summary>
        NoRoomForResponse = (byte)'O',

        /// <summary>P:同一包中前面的命令已失败,本命令没有执行。</summary>
        PriorCommandFailed = (byte)'P',
    }

    /// <summary>
    /// 包错误码 (Packet Error Code)。响应的 CCB 最高位置位,响应消息只有这一个字节。
    /// </summary>
    public enum Xtc3PacketError : byte
    {
        /// <summary>C:校验和错误。</summary>
        InvalidChecksum = (byte)'C',

        /// <summary>F:格式错误 (字节过多或缺少值)。</summary>
        IllegalFormat = (byte)'F',

        /// <summary>I:无效消息。</summary>
        InvalidMessage = (byte)'I',

        /// <summary>L:长度错误 (长度应大于 0 且不超过 <see cref="Xtc3Protocol.MaxLength"/>)。</summary>
        LengthError = (byte)'L',

        /// <summary>M:命令过多 (超过 <see cref="Xtc3Protocol.MaxCommands"/> 条)。</summary>
        TooManyCommands = (byte)'M',
    }
}
