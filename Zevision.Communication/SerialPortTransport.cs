using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace Zevision.Communication
{
    /// <summary>
    /// 基于 <see cref="SerialPort"/> 的串口实现。
    /// </summary>
    /// <remarks>
    /// .NET Framework 的 <c>SerialPort.BaseStream.ReadAsync</c> 不响应 <see cref="CancellationToken"/>,
    /// 挂起的读取只能通过 <see cref="Close"/> 结束。
    /// </remarks>
    public sealed class SerialPortTransport : IDeviceTransport
    {
        private const int WriteTimeoutMilliseconds = 2000;

        private SerialPort? _port;

        /// <inheritdoc />
        public bool IsOpen => _port?.IsOpen == true;

        /// <inheritdoc />
        public Task OpenAsync(ConnectionEndpoint endpoint, CancellationToken cancellationToken)
        {
            if (!(endpoint is SerialSettings settings))
            {
                throw new ArgumentException("A serial connection needs SerialSettings.", nameof(endpoint));
            }

            // SerialPort.Open 是同步调用,部分 USB 转串口驱动会阻塞较久,放到线程池执行。
            return Task.Run(() => Open(settings), cancellationToken);
        }

        private void Open(SerialSettings settings)
        {
            if (_port != null)
            {
                throw new InvalidOperationException("The port is already open.");
            }

            var port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
            {
                Handshake = Handshake.None,
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = WriteTimeoutMilliseconds,
            };

            try
            {
                port.Open();
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
            }
            catch
            {
                port.Dispose();
                throw;
            }

            _port = port;
        }

        /// <inheritdoc />
        public void Close()
        {
            var port = _port;
            _port = null;
            if (port == null)
            {
                return;
            }

            // 关闭会中止挂起的 BaseStream.ReadAsync,读取方收到 IOException (ERROR_OPERATION_ABORTED),
            // 这是 .NET Framework 下结束读取的唯一方式,由读取循环按"主动断开"处理。
            // 设备已被拔出时 Close/Dispose 本身也可能抛出 IOException:端口反正要丢弃,忽略即可,
            // 否则异常会从 Dispose 一路传到 App.OnExit,导致退出时崩溃。
            try
            {
                port.Close();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
            }

            try
            {
                port.Dispose();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
            }
        }

        /// <inheritdoc />
        public Task WriteAsync(byte[] data, CancellationToken cancellationToken)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return GetOpenPort().BaseStream.WriteAsync(data, 0, data.Length, cancellationToken);
        }

        /// <inheritdoc />
        public Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            return GetOpenPort().BaseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose() => Close();

        private SerialPort GetOpenPort()
        {
            var port = _port;
            if (port == null || !port.IsOpen)
            {
                throw new InvalidOperationException("The port is not open.");
            }

            return port;
        }
    }
}
