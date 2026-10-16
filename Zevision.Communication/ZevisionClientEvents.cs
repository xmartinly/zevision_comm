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

    /// <summary>
    /// <see cref="ZevisionClient.ConnectionLost"/> 的参数。
    /// </summary>
    public sealed class ConnectionLostEventArgs : EventArgs
    {
        /// <summary>创建参数。</summary>
        public ConnectionLostEventArgs(Exception exception)
        {
            Exception = exception;
        }

        /// <summary>
        /// 导致断开的异常。通常是 <see cref="System.IO.IOException"/> (拔线、对端关闭);
        /// 其他类型表示读取时出现了意外错误,调用方应记录它。
        /// </summary>
        public Exception Exception { get; }
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
