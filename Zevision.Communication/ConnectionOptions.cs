using System;

namespace Zevision.Communication
{
    /// <summary>
    /// 一次连接的全部参数,连接时一次性传入,连接期间不变 (帧格式可以在连接后用
    /// <see cref="ZevisionClient.ChangeFrameOptionsAsync"/> 改为设备的实际设置)。
    /// </summary>
    public sealed class ConnectionOptions
    {
        /// <summary>默认响应空闲超时。</summary>
        public static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(1);

        /// <summary><see cref="RetryCount"/> 的上限。</summary>
        public const int MaxRetryCount = 5;

        /// <summary><see cref="HeartbeatMissLimit"/> 的默认值。</summary>
        public const int DefaultHeartbeatMissLimit = 3;

        private readonly int _retryCount;
        private readonly TimeSpan _heartbeatInterval;
        private readonly int _heartbeatMissLimit = DefaultHeartbeatMissLimit;

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

        /// <summary>连接时使用的帧格式。</summary>
        public FrameOptions Frame { get; }

        /// <summary>
        /// 等待响应的空闲超时:发送后或最近一次收到字节后,超过该时间仍无数据即判定超时。
        /// </summary>
        public TimeSpan ResponseTimeout { get; }

        /// <summary>
        /// 传输出错时自动重发的次数,0 到 <see cref="MaxRetryCount"/>;默认 0 (不重试)。
        /// 传输出错指超时、响应帧无效或无法解析,或设备回答 C (校验和)、L (长度)、T (超时) 错误;
        /// 设备拒绝命令本身 (A、B、D、F) 不重试。只重试重复执行结果不变的命令 (动作 Q、S、U、H、E、<c>?</c>):
        /// 帧中有 A、D、R 等命令时整帧不重试,因为超时并不说明设备没有执行。每次重发前触发 <see cref="ZevisionClient.Retrying"/>。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">超出 0 到 <see cref="MaxRetryCount"/>。</exception>
        public int RetryCount
        {
            get => _retryCount;
            init => _retryCount = value >= 0 && value <= MaxRetryCount
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "RetryCount must be between 0 and " + MaxRetryCount + ".");
        }

        /// <summary>
        /// 心跳间隔:连接空闲 (没有收发) 这么久后发送一次 <c>H01</c> (不触发收发事件),
        /// 连续 <see cref="HeartbeatMissLimit"/> 次无应答即判定连接中断 (<see cref="ZevisionClient.ConnectionLost"/>)。
        /// 为零 (默认) 时不发送心跳。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">小于零。</exception>
        public TimeSpan HeartbeatInterval
        {
            get => _heartbeatInterval;
            init => _heartbeatInterval = value >= TimeSpan.Zero
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "HeartbeatInterval must not be negative.");
        }

        /// <summary>连续多少次心跳无应答 (超时) 判定连接中断;默认 <see cref="DefaultHeartbeatMissLimit"/>。</summary>
        /// <exception cref="ArgumentOutOfRangeException">小于 1。</exception>
        public int HeartbeatMissLimit
        {
            get => _heartbeatMissLimit;
            init => _heartbeatMissLimit = value >= 1
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "HeartbeatMissLimit must be at least 1.");
        }
    }
}
