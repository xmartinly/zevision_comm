using System;

namespace Zevision.Communication
{
    /// <summary>
    /// <see cref="ZevisionClient"/> 操作失败的类型。调用方按类型分支或生成自己的提示文本。
    /// </summary>
    public enum ZevisionErrorKind
    {
        /// <summary>已经连接。</summary>
        AlreadyConnected,

        /// <summary>无法打开连接 (端口不存在或被占用、连接被拒绝、连接超时)。见 <see cref="ZevisionError.Exception"/>。</summary>
        ConnectFailed,

        /// <summary>未连接。</summary>
        NotConnected,

        /// <summary>命令不合法 (为空、含非法字符或超长),没有发送。</summary>
        InvalidCommand,

        /// <summary>写入失败。见 <see cref="ZevisionError.Exception"/>。</summary>
        SendFailed,

        /// <summary>在 <see cref="ZevisionError.Timeout"/> 内没有收到数据 (空闲超时)。</summary>
        Timeout,

        /// <summary>等待响应时连接已断开 (主动断开或连接中断)。</summary>
        Disconnected,

        /// <summary>响应帧无效 (校验和、长度等)。见 <see cref="ZevisionError.FrameError"/>。</summary>
        InvalidFrame,

        /// <summary>响应帧有效,但内容无法按 Zevision 响应格式解析。</summary>
        UnparseableResponse,
    }

    /// <summary>
    /// <see cref="ZevisionClient"/> 操作失败的详细信息。<see cref="Message"/> 是英文说明;
    /// 其他属性给出生成本地化文本所需的数据。
    /// </summary>
    public sealed class ZevisionError
    {
        /// <summary>创建错误。</summary>
        /// <param name="kind">类型。</param>
        /// <param name="message">英文说明。</param>
        public ZevisionError(ZevisionErrorKind kind, string message)
        {
            Kind = kind;
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        /// <summary>类型。</summary>
        public ZevisionErrorKind Kind { get; }

        /// <summary>英文说明。</summary>
        public string Message { get; }

        /// <summary>引起错误的异常 (<see cref="ZevisionErrorKind.ConnectFailed"/>、<see cref="ZevisionErrorKind.SendFailed"/>、
        /// <see cref="ZevisionErrorKind.InvalidCommand"/>);没有时为 null。</summary>
        public Exception? Exception { get; init; }

        /// <summary>连接目标 (<see cref="ZevisionErrorKind.ConnectFailed"/>)。</summary>
        public ConnectionEndpoint? Endpoint { get; init; }

        /// <summary>本次请求使用的空闲超时 (<see cref="ZevisionErrorKind.Timeout"/>)。</summary>
        public TimeSpan Timeout { get; init; }

        /// <summary>超时前是否收到了不完整的数据;这些数据已通过 <see cref="ZevisionClient.FrameReceived"/> 报告。</summary>
        public bool ReceivedIncompleteData { get; init; }

        /// <summary>帧错误 (<see cref="ZevisionErrorKind.InvalidFrame"/>)。</summary>
        public FrameError FrameError { get; init; }

        /// <summary>响应数据的字节数 (<see cref="ZevisionErrorKind.UnparseableResponse"/>)。</summary>
        public int DataLength { get; init; }

        /// <inheritdoc />
        public override string ToString() => Kind + ": " + Message;
    }

    /// <summary>
    /// 不带返回值的操作结果。预期内的失败 (端口被占用、超时等) 不抛异常,而是返回失败结果。
    /// </summary>
    public readonly struct ZevisionResult
    {
        private ZevisionResult(ZevisionError? error)
        {
            Error = error;
        }

        /// <summary>是否成功。</summary>
        public bool IsSuccess => Error is null;

        /// <summary>失败原因;成功时为 null。</summary>
        public ZevisionError? Error { get; }

        /// <summary>成功结果。</summary>
        public static ZevisionResult Ok() => default;

        /// <summary>失败结果。</summary>
        public static ZevisionResult Fail(ZevisionError error) => new ZevisionResult(error ?? throw new ArgumentNullException(nameof(error)));

        /// <inheritdoc />
        public override string ToString() => IsSuccess ? "OK" : Error!.ToString();
    }

    /// <summary>
    /// 带返回值的操作结果。
    /// </summary>
    /// <typeparam name="T">成功时的返回值类型。</typeparam>
    public readonly struct ZevisionResult<T>
    {
        private readonly T _value;

        private ZevisionResult(T value, ZevisionError? error)
        {
            _value = value;
            Error = error;
        }

        /// <summary>是否成功。</summary>
        public bool IsSuccess => Error is null;

        /// <summary>失败原因;成功时为 null。</summary>
        public ZevisionError? Error { get; }

        /// <summary>返回值。</summary>
        /// <exception cref="InvalidOperationException">结果是失败的。</exception>
        public T Value => IsSuccess ? _value : throw new InvalidOperationException("A failed result has no value: " + Error!.Message);

        /// <summary>成功结果。</summary>
        public static ZevisionResult<T> Ok(T value) => new ZevisionResult<T>(value, null);

        /// <summary>失败结果。</summary>
        public static ZevisionResult<T> Fail(ZevisionError error) =>
            new ZevisionResult<T>(default!, error ?? throw new ArgumentNullException(nameof(error)));

        /// <inheritdoc />
        public override string ToString() => IsSuccess ? "OK: " + _value : Error!.ToString();
    }
}
