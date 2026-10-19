using System;

namespace Zevision.Communication
{
    /// <summary>
    /// <see cref="ZevisionClient.FrameSent"/> 的参数。
    /// </summary>
    public sealed class FrameSentEventArgs : EventArgs
    {
        /// <summary>创建参数。</summary>
        public FrameSentEventArgs(string command, byte[] raw)
        {
            Command = command;
            Raw = raw;
        }

        /// <summary>实际发送的命令数据 (可能追加了 <see cref="ZevisionFrameCodec.FillerCommand"/>)。</summary>
        public string Command { get; }

        /// <summary>完整帧字节。</summary>
        public byte[] Raw { get; }
    }

    /// <summary>
    /// <see cref="ZevisionClient.FrameReceived"/> 的参数。
    /// </summary>
    public sealed class FrameReceivedEventArgs : EventArgs
    {
        /// <summary>创建参数。</summary>
        public FrameReceivedEventArgs(ZevisionFrame frame, bool isSolicited)
        {
            Frame = frame;
            IsSolicited = isSolicited;
        }

        /// <summary>收到的帧。</summary>
        public ZevisionFrame Frame { get; }

        /// <summary>是否是对当前请求的响应;false 表示设备主动发送或迟到的响应。</summary>
        public bool IsSolicited { get; }
    }

    /// <summary>连接中断的原因。</summary>
    public enum ConnectionLostReason
    {
        /// <summary>读取失败:拔线、对端关闭或读取时的意外错误。</summary>
        ReadFailed,

        /// <summary>设备连续多次没有应答心跳 (<see cref="ConnectionOptions.HeartbeatInterval"/>)。</summary>
        HeartbeatTimeout,
    }

    /// <summary>
    /// <see cref="ZevisionClient.ConnectionLost"/> 的参数。
    /// </summary>
    public sealed class ConnectionLostEventArgs : EventArgs
    {
        /// <summary>创建参数 (读取失败)。</summary>
        public ConnectionLostEventArgs(Exception exception)
            : this(exception, ConnectionLostReason.ReadFailed)
        {
        }

        /// <summary>创建参数。</summary>
        public ConnectionLostEventArgs(Exception exception, ConnectionLostReason reason)
        {
            Exception = exception;
            Reason = reason;
        }

        /// <summary>
        /// 导致断开的异常。读取失败时通常是 <see cref="System.IO.IOException"/> (拔线、对端关闭),
        /// 其他类型表示读取时出现了意外错误,调用方应记录它;心跳无应答时是 <see cref="TimeoutException"/>。
        /// </summary>
        public Exception Exception { get; }

        /// <summary>中断的原因。</summary>
        public ConnectionLostReason Reason { get; }
    }

    /// <summary>
    /// <see cref="ZevisionClient.Retrying"/> 的参数:一次传输出错,即将重发。
    /// </summary>
    public sealed class RetryEventArgs : EventArgs
    {
        /// <summary>创建参数。</summary>
        public RetryEventArgs(string command, int attempt, int maxRetries, ZevisionError? error, ZevisionErrorCode deviceError)
        {
            Command = command;
            Attempt = attempt;
            MaxRetries = maxRetries;
            Error = error;
            DeviceError = deviceError;
        }

        /// <summary>要重发的命令。</summary>
        public string Command { get; }

        /// <summary>第几次重发 (从 1 开始)。</summary>
        public int Attempt { get; }

        /// <summary>最多重发几次。</summary>
        public int MaxRetries { get; }

        /// <summary>出错的原因 (超时、帧无效、无法解析);设备回答了错误代码时为 null。</summary>
        public ZevisionError? Error { get; }

        /// <summary>设备回答的错误代码 (C、L、T);<see cref="Error"/> 不为 null 时为 <see cref="ZevisionErrorCode.None"/>。</summary>
        public ZevisionErrorCode DeviceError { get; }
    }

    /// <summary>
    /// <see cref="ZevisionClient.HandlerFailed"/> 的参数。
    /// </summary>
    public sealed class HandlerFailedEventArgs : EventArgs
    {
        /// <summary>创建参数。</summary>
        public HandlerFailedEventArgs(string eventName, Exception exception)
        {
            EventName = eventName;
            Exception = exception;
        }

        /// <summary>出错的事件,如 <c>FrameReceived</c>。</summary>
        public string EventName { get; }

        /// <summary>事件处理程序抛出的异常。</summary>
        public Exception Exception { get; }
    }
}
