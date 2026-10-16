using System;

namespace Zevision.Communication
{
    /// <summary>
    /// 连接目标:串口 (<see cref="SerialSettings"/>) 或 TCP (<see cref="TcpSettings"/>)。
    /// <see cref="object.ToString"/> 给出用于日志和状态栏的简短描述。
    /// </summary>
    public abstract class ConnectionEndpoint
    {
    }

    /// <summary>
    /// TCP 连接参数。IMC300 的以太网接口默认端口为 2101 (协议文档 1.1);
    /// 帧格式由设备的 G1406 (协议类型,须为 0=Zevision)、G1407 (校验和)、G1408 (长度检查) 决定。
    /// </summary>
    public sealed class TcpSettings : ConnectionEndpoint
    {
        /// <summary>IMC300 以太网接口的默认端口。</summary>
        public const int DefaultPort = 2101;

        /// <summary>
        /// 创建 TCP 连接参数。
        /// </summary>
        /// <param name="host">设备 IP 地址或主机名 (对应 G1402)。</param>
        /// <param name="port">端口,1~65535。</param>
        public TcpSettings(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("The IP address must not be empty.", nameof(host));
            }

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port), port, "The port must be 1~65535.");
            }

            Host = host.Trim();
            Port = port;
        }

        /// <summary>IP 地址或主机名。</summary>
        public string Host { get; }

        /// <summary>端口。</summary>
        public int Port { get; }

        /// <inheritdoc />
        public override string ToString() => $"{Host}:{Port}";
    }
}
