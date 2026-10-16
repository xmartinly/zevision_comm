using System;
using System.Collections.Generic;

namespace Zevision.Communication
{
    /// <summary>
    /// 设备否定响应 (<c>-</c>) 附带的错误代码。
    /// </summary>
    public enum ZevisionErrorCode
    {
        /// <summary>无错误 (肯定响应)。</summary>
        None,

        /// <summary>A:非法命令,不支持该命令。</summary>
        IllegalCommand,

        /// <summary>B:非法参数值。</summary>
        IllegalArgument,

        /// <summary>C:校验和错误 (仅启用校验和时)。</summary>
        ChecksumError,

        /// <summary>D:非法格式。</summary>
        IllegalFormat,

        /// <summary>F:状态错误,系统处于错误状态。</summary>
        StateError,

        /// <summary>L:长度错误 (仅启用长度时)。</summary>
        LengthError,

        /// <summary>T:超时,收到起始符后 200 ms 内未收到停止符。</summary>
        Timeout,

        /// <summary>否定响应,但没有或无法识别错误代码。</summary>
        Unknown,
    }

    /// <summary>
    /// 一帧响应中单条命令的响应,格式为:命令回显 + 状态(+/-) [+ 错误代码] [: 数据]。
    /// </summary>
    public sealed class ZevisionResponse
    {
        /// <summary>
        /// 创建响应。
        /// </summary>
        public ZevisionResponse(string command, bool isSuccess, ZevisionErrorCode errorCode, char? rawErrorCode, string? data)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            IsSuccess = isSuccess;
            ErrorCode = errorCode;
            RawErrorCode = rawErrorCode;
            Data = data;
        }

        /// <summary>设备回显的命令 (数据分隔符之前的部分)。</summary>
        public string Command { get; }

        /// <summary>是否为肯定响应 (<c>+</c>)。</summary>
        public bool IsSuccess { get; }

        /// <summary>错误代码;肯定响应时为 <see cref="ZevisionErrorCode.None"/>。</summary>
        public ZevisionErrorCode ErrorCode { get; }

        /// <summary>原始错误代码字符;没有时为 null。</summary>
        public char? RawErrorCode { get; }

        /// <summary>查询返回的数据 (<c>:</c> 之后的部分);没有时为 null。</summary>
        public string? Data { get; }
    }

    /// <summary>
    /// 一次请求的完整回复:原始帧及其中每条命令的响应。
    /// </summary>
    public sealed class ZevisionReply
    {
        /// <summary>
        /// 创建回复。
        /// </summary>
        public ZevisionReply(ZevisionFrame frame, IReadOnlyList<ZevisionResponse> responses)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Responses = responses ?? throw new ArgumentNullException(nameof(responses));
        }

        /// <summary>收到的响应帧。</summary>
        public ZevisionFrame Frame { get; }

        /// <summary>按命令顺序排列的响应。</summary>
        public IReadOnlyList<ZevisionResponse> Responses { get; }
    }

    /// <summary>
    /// <see cref="ZevisionErrorCode"/> 的辅助方法。
    /// </summary>
    public static class ZevisionErrorCodeExtensions
    {
        /// <summary>
        /// 返回错误代码的英文说明,如 <c>Illegal parameter value</c>。
        /// </summary>
        public static string Describe(this ZevisionErrorCode code)
        {
            switch (code)
            {
                case ZevisionErrorCode.None: return "No error";
                case ZevisionErrorCode.IllegalCommand: return "Illegal command, not supported";
                case ZevisionErrorCode.IllegalArgument: return "Illegal parameter value";
                case ZevisionErrorCode.ChecksumError: return "Checksum error";
                case ZevisionErrorCode.IllegalFormat: return "Illegal format";
                case ZevisionErrorCode.StateError: return "State error, the system is in the wrong state";
                case ZevisionErrorCode.LengthError: return "Length error";
                case ZevisionErrorCode.Timeout: return "Timeout, no stop character received";
                default: return "Unknown error";
            }
        }
    }
}
