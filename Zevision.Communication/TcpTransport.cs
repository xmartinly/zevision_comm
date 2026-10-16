using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Zevision.Communication
{
    /// <summary>
    /// 基于 <see cref="TcpClient"/> 的 TCP 实现 (IMC300 以太网接口)。
    /// </summary>
    /// <remarks>
    /// 读取用 <see cref="Socket.ReceiveAsync(SocketAsyncEventArgs)"/>:挂起的接收只能通过 <see cref="Close"/> 结束,
    /// 本地关闭时它以 <see cref="SocketError.OperationAborted"/> 等错误码完成,这里按正常结束返回 0,不抛异常。
    /// (用 NetworkStream.ReadAsync 时,关闭会让挂起的读取以 ObjectDisposedException 结束,退出程序时 VS 输出窗口会显示该异常;
    /// Windows 上 Shutdown 也不会让挂起的读取提前返回。) 对方关闭连接时读取同样返回 0。
    /// 网线拔出时 TCP 本身发现不了,因此打开 TCP keep-alive (约 15 秒内判定断线,见下面的常量)。
    /// </remarks>
    public sealed class TcpTransport : IDeviceTransport
    {
        /// <summary>建立连接的超时。</summary>
        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

        /// <summary>keep-alive:空闲多久后开始探测 (毫秒)。</summary>
        private const uint KeepAliveIdleMilliseconds = 5000;

        /// <summary>keep-alive:探测间隔 (毫秒)。Windows 默认探测 10 次,约 5 + 10×1 秒判定断线。</summary>
        private const uint KeepAliveIntervalMilliseconds = 1000;

        private TcpClient? _client;
        private NetworkStream? _stream;

        /// <summary>已主动关闭:之后完成的接收 (被中止) 按正常结束处理。</summary>
        private volatile bool _closed;

        /// <inheritdoc />
        public bool IsOpen => _client?.Connected == true;

        /// <inheritdoc />
        public async Task OpenAsync(ConnectionEndpoint endpoint, CancellationToken cancellationToken)
        {
            if (!(endpoint is TcpSettings settings))
            {
                throw new ArgumentException("A TCP connection needs TcpSettings.", nameof(endpoint));
            }

            if (_client != null)
            {
                throw new InvalidOperationException("The connection is already open.");
            }

            var client = new TcpClient { NoDelay = true };
            try
            {
                var connect = client.ConnectAsync(settings.Host, settings.Port);
                var completed = await Task.WhenAny(connect, Task.Delay(ConnectTimeout, cancellationToken)).ConfigureAwait(false);
                if (completed != connect)
                {
                    // 超时或取消:关闭客户端使连接尝试结束,并观察它的异常,避免 UnobservedTaskException。
                    _ = connect.ContinueWith(t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException($"Connection timed out (no response within {ConnectTimeout.TotalSeconds:0} s). Check the IP address and the network.");
                }

                await connect.ConfigureAwait(false);
                EnableKeepAlive(client.Client);
                _stream = client.GetStream();
                _closed = false;
                _client = client;
            }
            catch (SocketException ex)
            {
                client.Close();
                throw new IOException(ex.Message, ex);
            }
            catch
            {
                client.Close();
                throw;
            }
        }

        /// <inheritdoc />
        public void Close()
        {
            var client = _client;
            _closed = true;
            _client = null;
            _stream = null;
            if (client == null)
            {
                return;
            }

            // 挂起的接收随即以"已中止"完成,由 CompleteReceive 按正常结束返回 0。
            try
            {
                client.Close();
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
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

            return GetStream().WriteAsync(data, 0, data.Length, cancellationToken);
        }

        /// <inheritdoc />
        public Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            var client = _client;
            if (client == null || _closed)
            {
                return Task.FromResult(0); // 已关闭:与对方关闭一样返回 0
            }

            var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var args = new SocketAsyncEventArgs();
            args.SetBuffer(buffer, 0, buffer.Length);
            args.Completed += (sender, e) => CompleteReceive(e, completion);
            bool pending;
            try
            {
                pending = client.Client.ReceiveAsync(args);
            }
            catch (ObjectDisposedException) when (_closed)
            {
                // 与 Close 同时发生:套接字刚被释放。
                args.Dispose();
                return Task.FromResult(0);
            }

            if (!pending)
            {
                CompleteReceive(args, completion);
            }

            return completion.Task;
        }

        /// <inheritdoc />
        public void Dispose() => Close();

        private NetworkStream GetStream() => _stream ?? throw new InvalidOperationException("The connection is not open.");

        /// <summary>
        /// 接收完成:成功时返回字节数 (0 = 对方关闭);主动关闭后的中止返回 0;其他错误 (如 keep-alive 判定断线)
        /// 以 <see cref="IOException"/> 结束,由读取循环报告连接中断。
        /// </summary>
        private void CompleteReceive(SocketAsyncEventArgs args, TaskCompletionSource<int> completion)
        {
            if (args.SocketError == SocketError.Success)
            {
                completion.TrySetResult(args.BytesTransferred);
            }
            else if (_closed)
            {
                completion.TrySetResult(0);
            }
            else
            {
                completion.TrySetException(new IOException("Receive failed: " + args.SocketError, new SocketException((int)args.SocketError)));
            }

            args.Dispose();
        }

        /// <summary>打开 keep-alive 并缩短探测时间 (SIO_KEEPALIVE_VALS:开关、空闲时间、间隔,各 4 字节)。</summary>
        private static void EnableKeepAlive(Socket socket)
        {
            var values = new byte[12];
            BitConverter.GetBytes(1u).CopyTo(values, 0);
            BitConverter.GetBytes(KeepAliveIdleMilliseconds).CopyTo(values, 4);
            BitConverter.GetBytes(KeepAliveIntervalMilliseconds).CopyTo(values, 8);
            socket.IOControl(IOControlCode.KeepAliveValues, values, null);
        }
    }
}
