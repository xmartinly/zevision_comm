using System;
using System.Threading;
using System.Threading.Tasks;

namespace Zevision.Communication
{
    /// <summary>
    /// 与设备之间字节流的最小抽象 (串口或 TCP),便于用假实现测试协议与服务层。
    /// </summary>
    public interface IDeviceTransport : IDisposable
    {
        /// <summary>连接是否已打开。</summary>
        bool IsOpen { get; }

        /// <summary>
        /// 打开连接。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="endpoint"/> 不是该实现支持的类型。</exception>
        /// <exception cref="System.IO.IOException">端口不存在或连接被拒绝。</exception>
        /// <exception cref="TimeoutException">TCP 连接在 <see cref="TcpTransport.ConnectTimeout"/> 内没有建立。</exception>
        /// <exception cref="UnauthorizedAccessException">串口被其他程序占用。</exception>
        Task OpenAsync(ConnectionEndpoint endpoint, CancellationToken cancellationToken);

        /// <summary>
        /// 关闭连接。会使挂起的 <see cref="ReadAsync"/> 以异常结束或返回 0。重复调用无副作用。
        /// </summary>
        void Close();

        /// <summary>
        /// 写入全部字节。
        /// </summary>
        Task WriteAsync(byte[] data, CancellationToken cancellationToken);

        /// <summary>
        /// 读取至少 1 个字节,返回读取的字节数。返回 0 表示连接已关闭。
        /// </summary>
        Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 按连接目标创建传输:<see cref="SerialSettings"/> → 串口,<see cref="TcpSettings"/> → TCP。
    /// </summary>
    public static class DeviceTransports
    {
        /// <summary>创建与 <paramref name="endpoint"/> 对应的传输 (尚未打开)。</summary>
        /// <exception cref="ArgumentException">不支持的连接类型。</exception>
        public static IDeviceTransport Create(ConnectionEndpoint endpoint)
        {
            switch (endpoint)
            {
                case SerialSettings _:
                    return new SerialPortTransport();
                case TcpSettings _:
                    return new TcpTransport();
                default:
                    throw new ArgumentException("Unsupported connection type: " + endpoint?.GetType().Name, nameof(endpoint));
            }
        }
    }
}
