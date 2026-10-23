using System;
using System.Collections.Generic;

namespace Zevision.Xtc3
{
    /// <summary>
    /// 从字节流中解出的一个包:消息 (长度和校验和正确),或包错误 (应回答),或因字符间隔超时丢弃的字节 (不回答)。
    /// </summary>
    public sealed class Xtc3Packet
    {
        private Xtc3Packet(byte[] message, Xtc3PacketError? error, byte[] discarded)
        {
            Message = message;
            Error = error;
            Discarded = discarded;
        }

        /// <summary>消息 (不含长度和校验和);有错误或被丢弃时为空。</summary>
        public byte[] Message { get; }

        /// <summary>包错误 (长度或校验和错误);应回答该包错误码。</summary>
        public Xtc3PacketError? Error { get; }

        /// <summary>因字符间隔超过 <see cref="Xtc3Protocol.InterCharacterTimeout"/> 丢弃的不完整的包;不回答。</summary>
        public byte[] Discarded { get; }

        /// <summary>是否为完整、正确的消息。</summary>
        public bool IsMessage => Error == null && Discarded.Length == 0;

        internal static Xtc3Packet ForMessage(byte[] message) => new Xtc3Packet(message, null, Array.Empty<byte>());

        internal static Xtc3Packet ForError(Xtc3PacketError error) => new Xtc3Packet(Array.Empty<byte>(), error, Array.Empty<byte>());

        internal static Xtc3Packet ForDiscarded(byte[] bytes) => new Xtc3Packet(Array.Empty<byte>(), null, bytes);
    }

    /// <summary>
    /// XTC/3 包的字节流解码器:<c>&lt;Length 低字节&gt;&lt;Length 高字节&gt;&lt;Message&gt;&lt;Checksum&gt;</c>。
    /// 命令包和响应包的分帧相同,两个方向都可用。一次读到的字节可能是半个包,也可能是多个包。不是线程安全的。
    /// </summary>
    /// <remarks>
    /// 字符间隔超过 <see cref="Xtc3Protocol.InterCharacterTimeout"/> 时丢弃已收到的部分 (XTC/3 不回答,清空缓冲区)。
    /// 长度为 0 或超过 <see cref="Xtc3Protocol.MaxLength"/> 时报告 <see cref="Xtc3PacketError.LengthError"/>,
    /// 并丢弃同一次收到的其余字节 (无法确定下一个包的起点)。
    /// </remarks>
    public sealed class Xtc3PacketDecoder
    {
        private readonly List<byte> _buffer = new List<byte>();
        private int _length = -1;
        private TimeSpan _lastByteTime;

        /// <summary>当前缓冲中的字节数 (不完整的包)。</summary>
        public int PendingCount => _buffer.Count;

        /// <summary>
        /// 加入收到的字节。
        /// </summary>
        /// <param name="data">收到的数据。</param>
        /// <param name="count">有效字节数 (从 0 起)。</param>
        /// <param name="now">收到的时刻 (单调时钟,如 <see cref="System.Diagnostics.Stopwatch"/> 的经过时间),用于字符间隔超时。</param>
        /// <returns>解出的包,按顺序。</returns>
        public IReadOnlyList<Xtc3Packet> Push(byte[] data, int count, TimeSpan now)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (count < 0 || count > data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var packets = new List<Xtc3Packet>();
            if (count == 0)
            {
                return packets;
            }

            if (_buffer.Count > 0 && now - _lastByteTime > Xtc3Protocol.InterCharacterTimeout)
            {
                packets.Add(Xtc3Packet.ForDiscarded(_buffer.ToArray()));
                Reset();
            }

            _lastByteTime = now;
            for (var i = 0; i < count; i++)
            {
                _buffer.Add(data[i]);
                if (_length < 0)
                {
                    if (_buffer.Count < 2)
                    {
                        continue;
                    }

                    _length = _buffer[0] | (_buffer[1] << 8);
                    if (_length == 0 || _length > Xtc3Protocol.MaxLength)
                    {
                        packets.Add(Xtc3Packet.ForError(Xtc3PacketError.LengthError));
                        Reset();
                        return packets;
                    }

                    continue;
                }

                if (_buffer.Count < _length + 3)
                {
                    continue;
                }

                var checksum = Xtc3Protocol.Checksum(_buffer, 2, _length);
                packets.Add(checksum == _buffer[_buffer.Count - 1]
                    ? Xtc3Packet.ForMessage(_buffer.GetRange(2, _length).ToArray())
                    : Xtc3Packet.ForError(Xtc3PacketError.InvalidChecksum));
                Reset();
            }

            return packets;
        }

        /// <summary>丢弃缓冲中不完整的包。</summary>
        public void Reset()
        {
            _buffer.Clear();
            _length = -1;
        }
    }
}
