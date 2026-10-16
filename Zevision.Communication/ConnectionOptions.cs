using System;

namespace Zevision.Communication
{
    /// <summary>
    /// 一次连接的全部参数,连接时一次性传入,连接期间不变。
    /// </summary>
    public sealed class ConnectionOptions
    {
        /// <summary>默认响应空闲超时。</summary>
        public static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 创建连接参数。
        /// </summary>
        /// <param name="endpoint">连接目标:串口参数或 TCP 地址。</param>
        /// <param name="frame">帧格式 (须与设备 G1304/G1305 一致)。</param>
        /// <param name="responseTimeout">响应空闲超时;为 null 时使用 <see cref="DefaultResponseTimeout"/>。</param>
        public ConnectionOptions(ConnectionEndpoint endpoint, FrameOptions frame, TimeSpan? responseTimeout = null)
        {
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            ResponseTimeout = responseTimeout ?? DefaultResponseTimeout;
            if (ResponseTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(responseTimeout));
            }
        }

        /// <summary>连接目标 (<see cref="SerialSettings"/> 或 <see cref="TcpSettings"/>)。</summary>
        public ConnectionEndpoint Endpoint { get; }

        /// <summary>帧格式。</summary>
        public FrameOptions Frame { get; }

        /// <summary>
        /// 等待响应的空闲超时:发送后或最近一次收到字节后,超过该时间仍无数据即判定超时。
        /// </summary>
        public TimeSpan ResponseTimeout { get; }
    }
}
