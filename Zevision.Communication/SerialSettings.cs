using System;
using System.IO.Ports;

namespace Zevision.Communication
{
    /// <summary>
    /// 串口参数。IMC300 只支持配置波特率 (G1303),帧格式固定为 8 位数据位、无校验、1 位停止位 (8N1)。
    /// </summary>
    public sealed class SerialSettings : ConnectionEndpoint
    {
        /// <summary>
        /// 创建串口参数。
        /// </summary>
        public SerialSettings(string portName, int baudRate)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new ArgumentException("The port name must not be empty.", nameof(portName));
            }

            if (baudRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baudRate));
            }

            PortName = portName;
            BaudRate = baudRate;
        }

        /// <summary>端口名,如 COM3。</summary>
        public string PortName { get; }

        /// <summary>波特率。设备默认 115200 (G1303)。</summary>
        public int BaudRate { get; }

        /// <summary>数据位,固定为 8。</summary>
        public int DataBits => 8;

        /// <summary>校验位,固定为无。</summary>
        public Parity Parity => Parity.None;

        /// <summary>停止位,固定为 1。</summary>
        public StopBits StopBits => StopBits.One;

        /// <inheritdoc />
        public override string ToString() => $"{PortName}, {BaudRate}, 8N1";
    }
}
