using System;
using System.Collections.Generic;

namespace Zevision.Communication
{
    /// <summary>
    /// 从串口字节流中拆出 Zevision 消息。一次读取可能只有半帧,也可能包含多帧,解码器会跨调用累积。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 多包消息 (实测 IMC300 固件 1.8.11 对 <c>?</c> 的约 116 KB 响应):
    /// <c>{</c>[长度]数据[校验和]<c>&gt;</c> <c>&lt;</c>[长度]数据[校验和]<c>&gt;</c> … <c>&lt;</c>[长度]数据[校验和]<c>}</c>。
    /// 每个包不超过 57000 字节,长度和校验和只覆盖本包数据。<c>&gt;</c> 表示还有续包,<c>}</c> 表示消息结束。
    /// 实测 (原始字节,两次录制一致):多包消息的每个包都以 <c>cs &gt;</c> 结尾,整条消息最后再跟一个 <c>}</c>;
    /// 且最后一个续包的长度字段多算了包尾 (见 <see cref="TryEndOvercountedLastPacket"/>)。
    /// </para>
    /// <para>
    /// 未启用长度时只能扫描分隔符:<c>&gt;</c> 只有紧跟 <c>&lt;</c> 才视为包结束,否则当作数据。
    /// 若同时启用校验和、且校验和恰好等于 <c>}</c>/<c>&gt;</c>,包会被提前截断并报告错误。
    /// 这是协议本身的歧义,启用长度字节可以避免。
    /// </para>
    /// 非线程安全,应只由一个读取循环调用。
    /// </remarks>
    public sealed class ZevisionFrameDecoder
    {
        /// <summary>一条消息 (所有包合计) 的命令数据上限,防止异常数据无限累积。</summary>
        public const int MaxReceiveLength = 1024 * 1024;

        /// <summary>包结束、后面还有续包。</summary>
        public const byte ContinueByte = (byte)'>';

        /// <summary>续包开始。</summary>
        public const byte ContinuationStartByte = (byte)'<';

        private enum State
        {
            WaitStart,
            LengthHigh,
            LengthLow,
            Data,
            Checksum,
            Stop,

            /// <summary>收到 <c>&gt;</c>,等待续包起始符 <c>&lt;</c>。</summary>
            WaitContinuation,

            /// <summary>未启用长度时收到 <c>&gt;</c>,看下一个字节是否为 <c>&lt;</c> 再决定它是分隔符还是数据。</summary>
            ScanAfterContinue,
        }

        private readonly FrameOptions _options;
        private readonly List<byte> _raw = new List<byte>();
        private readonly List<byte> _data = new List<byte>();
        private readonly List<byte> _garbage = new List<byte>();
        private readonly List<ByteRange> _ranges = new List<ByteRange>();
        private readonly List<int> _declaredLengths = new List<int>();
        private State _state = State.WaitStart;
        private int _expectedLength;
        private byte _receivedChecksum;
        private int _packetDataStart;
        private int _packetRawStart;
        private bool _packetOpen;
        private int _packetCount;
        private FrameError _messageError;

        /// <summary>
        /// 创建解码器。
        /// </summary>
        public ZevisionFrameDecoder(FrameOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// 送入新收到的字节,返回其中已完整的消息 (按接收顺序)。
        /// </summary>
        public IReadOnlyList<ZevisionFrame> Push(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var frames = new List<ZevisionFrame>();
            for (var i = offset; i < offset + count; i++)
            {
                Process(buffer[i], frames);
            }

            // 帧外数据不必等到下一个起始符才报告,否则设备只发杂乱数据时永远看不到。
            FlushGarbage(frames);
            return frames;
        }

        /// <summary>
        /// 把尚未完成的消息 (以 <see cref="FrameError.Incomplete"/>) 和帧外数据立即输出,并回到等待起始符状态。
        /// 用于请求超时后显示已收到的内容,便于诊断。
        /// </summary>
        public IReadOnlyList<ZevisionFrame> FlushIncomplete()
        {
            var frames = new List<ZevisionFrame>();
            FlushGarbage(frames);
            if (_state != State.WaitStart)
            {
                Emit(frames, FrameError.Incomplete);
            }

            return frames;
        }

        /// <summary>
        /// 丢弃尚未完成的消息和帧外数据。
        /// </summary>
        public void Reset()
        {
            ClearMessage();
            _garbage.Clear();
            _state = State.WaitStart;
        }

        private void Process(byte b, List<ZevisionFrame> frames)
        {
            switch (_state)
            {
                case State.WaitStart:
                    if (b == ZevisionFrameCodec.StartByte)
                    {
                        FlushGarbage(frames);
                        BeginMessage();
                    }
                    else
                    {
                        _garbage.Add(b);
                    }

                    break;

                case State.LengthHigh:
                    _raw.Add(b);
                    _expectedLength = b << 8;
                    _state = State.LengthLow;
                    break;

                case State.LengthLow:
                    _raw.Add(b);
                    _expectedLength |= b;
                    _declaredLengths.Add(_expectedLength);
                    if (_expectedLength == 0
                        || _expectedLength > ZevisionFrameCodec.MaxDataLength
                        || _data.Count + _expectedLength > MaxReceiveLength)
                    {
                        Emit(frames, FrameError.InvalidLength);
                    }
                    else
                    {
                        _state = State.Data;
                    }

                    break;

                case State.Data:
                    if (_options.LengthEnabled)
                    {
                        // 长度已知:按字节数读取,数据中的 { } < > 都不作为分隔符。
                        _raw.Add(b);
                        _data.Add(b);
                        if (_data.Count - _packetDataStart == _expectedLength)
                        {
                            _state = _options.ChecksumEnabled ? State.Checksum : State.Stop;
                        }
                    }
                    else
                    {
                        ScanData(b, frames);
                    }

                    break;

                case State.Checksum:
                    if (b == ZevisionFrameCodec.StopByte && TryEndOvercountedLastPacket(trailerLength: 2))
                    {
                        _raw.Add(b);
                        Emit(frames, _messageError);
                        break;
                    }

                    _raw.Add(b);
                    _receivedChecksum = b;
                    _state = State.Stop;
                    break;

                case State.Stop:
                    if (!_options.ChecksumEnabled && b == ZevisionFrameCodec.StopByte && TryEndOvercountedLastPacket(trailerLength: 1))
                    {
                        _raw.Add(b);
                        Emit(frames, _messageError);
                    }
                    else if (b == ZevisionFrameCodec.StopByte)
                    {
                        _raw.Add(b);
                        EndPacket();
                        Emit(frames, _messageError);
                    }
                    else if (b == ContinueByte)
                    {
                        _raw.Add(b);
                        EndPacket();
                        _state = State.WaitContinuation;
                    }
                    else
                    {
                        Emit(frames, FrameError.MissingStopByte);

                        // 这个字节可能是下一帧的起始符,重新处理。
                        Process(b, frames);
                    }

                    break;

                case State.WaitContinuation:
                    if (b == ContinuationStartByte)
                    {
                        _raw.Add(b);
                        BeginPacket();
                    }
                    else if (b == ZevisionFrameCodec.StopByte)
                    {
                        // 实测最后一个包也可能以 '>' 结尾,随后才是 '}'。
                        _raw.Add(b);
                        Emit(frames, _messageError);
                    }
                    else
                    {
                        Emit(frames, FrameError.MissingContinuation);
                        Process(b, frames);
                    }

                    break;

                case State.ScanAfterContinue:
                    if (b == ZevisionFrameCodec.StopByte && _packetCount > 0)
                    {
                        // 多包消息的最后一个续包同样以 "[cs] >" 结尾,随后才是 '}' (与长度模式的 WaitContinuation 相同)。
                        // 只对续包这样处理:单包消息的数据可能恰好以 '>' 结尾。
                        if (!TryTakeScannedChecksum(frames))
                        {
                            return;
                        }

                        EndPacket();
                        _raw.Add(b);
                        Emit(frames, _messageError);
                    }
                    else if (b == ContinuationStartByte)
                    {
                        // 上一个 '>' 确实是包分隔符。
                        if (!TryTakeScannedChecksum(frames))
                        {
                            return;
                        }

                        EndPacket();
                        _raw.Add(b);
                        BeginPacket();
                    }
                    else
                    {
                        // 上一个 '>' 只是普通数据 (它已经在 _raw 中的正确位置)。
                        _data.Add(ContinueByte);
                        _state = State.Data;
                        Process(b, frames);
                    }

                    break;
            }
        }

        /// <summary>
        /// 处理设备固件的长度缺陷:多包消息的最后一个续包,长度字段把包尾的 [校验和] <c>&gt;</c> 也算了进去
        /// (实测 IMC300 1.8.11:实际数据 2602 字节,长度字段 2604)。
        /// 按声明长度读完后,数据末尾其实是 <c>… 数据 [cs] &gt;</c>,下一个字节是消息结束符 <c>}</c>。
        /// </summary>
        /// <param name="trailerLength">被多算的字节数:启用校验和时为 2 (cs 和 <c>&gt;</c>),否则为 1。</param>
        /// <returns>符合该模式 (启用校验和时还要求校验和正确) 并已结束当前包时返回 true。</returns>
        private bool TryEndOvercountedLastPacket(int trailerLength)
        {
            // 只对续包生效:单包消息 (如 H01) 的长度是正确的。
            var length = _data.Count - _packetDataStart;
            if (_packetCount == 0 || length <= trailerLength || _data[_data.Count - 1] != ContinueByte)
            {
                return false;
            }

            var dataEnd = _data.Count - trailerLength;
            if (_options.ChecksumEnabled)
            {
                var checksum = _data[_data.Count - 2];
                var sum = 0;
                for (var i = _packetDataStart; i < dataEnd; i++)
                {
                    sum += _data[i];
                }

                // 用校验和确认数据边界,避免把数据中恰好出现的 "x>" 误判为包尾。
                if ((byte)(sum & 0xFF) != checksum)
                {
                    return false;
                }

                _receivedChecksum = checksum;
            }

            _data.RemoveRange(dataEnd, trailerLength);
            EndPacket();
            return true;
        }

        /// <summary>
        /// 未启用长度时,逐字节扫描分隔符。
        /// </summary>
        private void ScanData(byte b, List<ZevisionFrame> frames)
        {
            if (b == ZevisionFrameCodec.StartByte)
            {
                Emit(frames, FrameError.Truncated);
                BeginMessage();
                return;
            }

            _raw.Add(b);
            if (b == ContinueByte)
            {
                _state = State.ScanAfterContinue;
                return;
            }

            if (b != ZevisionFrameCodec.StopByte)
            {
                _data.Add(b);
                if (_data.Count > MaxReceiveLength + 1)
                {
                    Emit(frames, FrameError.InvalidLength);
                }

                return;
            }

            if (!TryTakeScannedChecksum(frames))
            {
                return;
            }

            EndPacket();
            Emit(frames, _messageError);
        }

        /// <summary>
        /// 未启用长度、启用校验和时,包的最后一个数据字节其实是校验和,把它取出来。
        /// </summary>
        private bool TryTakeScannedChecksum(List<ZevisionFrame> frames)
        {
            if (!_options.ChecksumEnabled)
            {
                return true;
            }

            if (_data.Count - _packetDataStart < 2)
            {
                Emit(frames, FrameError.TooShort);
                return false;
            }

            _receivedChecksum = _data[_data.Count - 1];
            _data.RemoveAt(_data.Count - 1);
            return true;
        }

        private void BeginMessage()
        {
            ClearMessage();
            _raw.Add(ZevisionFrameCodec.StartByte);
            BeginPacket();
        }

        private void BeginPacket()
        {
            _packetDataStart = _data.Count;
            _packetRawStart = _raw.Count + (_options.LengthEnabled ? 2 : 0);
            _packetOpen = true;
            _expectedLength = 0;
            _state = _options.LengthEnabled ? State.LengthHigh : State.Data;
        }

        /// <summary>
        /// 结束当前包:记录数据区间,校验本包的校验和。
        /// </summary>
        private void EndPacket()
        {
            var length = _data.Count - _packetDataStart;
            _ranges.Add(new ByteRange(_packetRawStart, length));
            _packetOpen = false;
            _packetCount++;

            if (_options.ChecksumEnabled && _messageError == FrameError.None)
            {
                var sum = 0;
                for (var i = _packetDataStart; i < _data.Count; i++)
                {
                    sum += _data[i];
                }

                if ((byte)(sum & 0xFF) != _receivedChecksum)
                {
                    _messageError = FrameError.ChecksumMismatch;
                }
            }
        }

        private void Emit(List<ZevisionFrame> frames, FrameError error)
        {
            if (_packetOpen)
            {
                // 出错时当前包还没结束,也把已收到的数据区间记下来,便于显示。
                var length = Math.Max(0, Math.Min(_data.Count - _packetDataStart, _raw.Count - _packetRawStart));
                _ranges.Add(new ByteRange(Math.Min(_packetRawStart, _raw.Count), length));
            }

            frames.Add(new ZevisionFrame(_raw.ToArray(), _data.ToArray(), _ranges.ToArray(), _packetCount, _declaredLengths.ToArray(), error));
            ClearMessage();
            _state = State.WaitStart;
        }

        private void ClearMessage()
        {
            _raw.Clear();
            _data.Clear();
            _ranges.Clear();
            _declaredLengths.Clear();
            _packetOpen = false;
            _packetCount = 0;
            _packetDataStart = 0;
            _packetRawStart = 0;
            _expectedLength = 0;
            _messageError = FrameError.None;
        }

        private void FlushGarbage(List<ZevisionFrame> frames)
        {
            if (_garbage.Count == 0)
            {
                return;
            }

            frames.Add(new ZevisionFrame(_garbage.ToArray(), Array.Empty<byte>(), Array.Empty<ByteRange>(), 0, Array.Empty<int>(), FrameError.UnexpectedData));
            _garbage.Clear();
        }
    }
}
