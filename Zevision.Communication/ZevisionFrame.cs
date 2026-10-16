using System;
using System.Collections.Generic;
using System.Text;

namespace Zevision.Communication
{
    /// <summary>
    /// 接收帧的解码错误。
    /// </summary>
    public enum FrameError
    {
        /// <summary>帧完整有效。</summary>
        None,

        /// <summary>校验和不匹配 (多包消息中任一包不匹配即报告)。</summary>
        ChecksumMismatch,

        /// <summary>长度字节为 0、超过 57000,或消息总长超过接收上限。</summary>
        InvalidLength,

        /// <summary>按长度读完数据和校验和后,下一个字节既不是 <c>}</c> 也不是 <c>&gt;</c>。</summary>
        MissingStopByte,

        /// <summary>帧未结束就收到了新的起始符,前一帧被丢弃。</summary>
        Truncated,

        /// <summary>帧外的多余字节 (起始符之前的数据)。</summary>
        UnexpectedData,

        /// <summary>启用校验和时,帧中没有足够的字节容纳命令数据和校验和。</summary>
        TooShort,

        /// <summary>包以 <c>&gt;</c> 结尾表示还有续包,但下一个字节既不是续包起始符 <c>&lt;</c> 也不是结束符 <c>}</c>。</summary>
        MissingContinuation,

        /// <summary>等待响应超时时,消息还没接收完整 (解码器仍在某个包的中间)。</summary>
        Incomplete,
    }

    /// <summary>
    /// <see cref="ZevisionFrame.Raw"/> 中的一段字节区间。
    /// </summary>
    public readonly struct ByteRange
    {
        /// <summary>创建区间。</summary>
        public ByteRange(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        /// <summary>起始位置。</summary>
        public int Offset { get; }

        /// <summary>长度。</summary>
        public int Length { get; }
    }

    /// <summary>
    /// 解码器输出的一条完整消息 (可能由多个包组成,可能带错误)。
    /// </summary>
    /// <remarks>
    /// 大响应 (如 <c>?</c>) 会被设备拆成多个包:<c>{</c>包1<c>&gt;</c> <c>&lt;</c>包2<c>&gt;</c> … <c>&lt;</c>包N<c>}</c>,
    /// 每个包有自己的长度和校验和。<see cref="Data"/> 是各包数据按顺序拼接的结果。
    /// </remarks>
    public sealed class ZevisionFrame
    {
        private string? _payload;

        /// <summary>
        /// 创建帧。
        /// </summary>
        /// <param name="raw">接收到的原始字节 (含起止符、长度、校验和、包分隔符)。</param>
        /// <param name="data">各包命令数据的拼接;出错时可能不完整。</param>
        /// <param name="dataRanges">每个包的命令数据在 <paramref name="raw"/> 中的位置。</param>
        /// <param name="packetCount">已完整接收的包数。</param>
        /// <param name="declaredLengths">各包长度字段的值 (未启用长度时为空),含未完成的包。</param>
        /// <param name="error">解码错误。</param>
        public ZevisionFrame(byte[] raw, byte[] data, IReadOnlyList<ByteRange> dataRanges, int packetCount, IReadOnlyList<int> declaredLengths, FrameError error)
        {
            Raw = raw ?? throw new ArgumentNullException(nameof(raw));
            Data = data ?? throw new ArgumentNullException(nameof(data));
            DataRanges = dataRanges ?? throw new ArgumentNullException(nameof(dataRanges));
            PacketCount = packetCount;
            DeclaredLengths = declaredLengths ?? throw new ArgumentNullException(nameof(declaredLengths));
            Error = error;
        }

        /// <summary>各包长度字段的值,按包顺序 (未启用长度时为空)。用于诊断分包问题。</summary>
        public IReadOnlyList<int> DeclaredLengths { get; }

        /// <summary>原始字节,用于显示。</summary>
        public byte[] Raw { get; }

        /// <summary>命令数据 (不含起止符、长度、校验和、包分隔符)。</summary>
        public byte[] Data { get; }

        /// <summary>每个包的命令数据在 <see cref="Raw"/> 中的位置,用于显示时区分数据和帧结构字节。</summary>
        public IReadOnlyList<ByteRange> DataRanges { get; }

        /// <summary>消息由几个包组成。</summary>
        public int PacketCount { get; }

        /// <summary>解码错误。</summary>
        public FrameError Error { get; }

        /// <summary>帧是否有效。</summary>
        public bool IsValid => Error == FrameError.None;

        /// <summary>
        /// 命令数据的文本。设备的说明文字含 UTF-8 字符 (如 <c>Å</c>),因此按 UTF-8 解码;纯 ASCII 时与 ASCII 解码相同。
        /// 首次读取时解码并缓存:大响应 (<c>?</c> 约 116 KB) 在回显匹配和响应解析中都要用,避免重复解码出两份大字符串。
        /// </summary>
        public string Payload => _payload ?? (_payload = Encoding.UTF8.GetString(Data)); // 并发首次读取最多多解码一次,结果相同
    }
}
