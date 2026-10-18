using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Communication;
using Zevision.Core.Localization;

namespace Zevision.Core.Devices
{
    /// <summary>
    /// <see cref="IDeviceService"/> 的实现:每次连接按当时的通讯参数创建一个 <see cref="ZevisionClient"/> (通讯库),
    /// 转发它的事件,把它的英文结果转换为界面语言的说明 (<see cref="CommunicationText"/>),
    /// 并把通讯库交出的意外异常 (读取循环出错、事件处理程序出错) 写入错误日志。
    /// </summary>
    public sealed class ZevisionDeviceService : IDeviceService
    {
        private readonly Func<ConnectionEndpoint, IDeviceTransport> _transportFactory;
        private readonly IErrorLog? _errorLog;
        private readonly object _sync = new object();
        private ZevisionClient? _client;
        private bool _connecting;

        /// <summary>
        /// 创建服务。每次连接时用 <paramref name="transportFactory"/> 按连接目标 (串口或 TCP) 创建传输,
        /// 断开时关闭。
        /// </summary>
        /// <param name="transportFactory">传输工厂,通常为 <see cref="DeviceTransports.Create"/>。</param>
        /// <param name="errorLog">记录读取循环和事件订阅方的意外异常;为 null 时不记录。</param>
        public ZevisionDeviceService(Func<ConnectionEndpoint, IDeviceTransport> transportFactory, IErrorLog? errorLog = null)
        {
            _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
            _errorLog = errorLog;
        }

        /// <summary>
        /// 用固定的传输创建服务 (每次连接都使用同一个实例,用于测试)。
        /// </summary>
        /// <param name="transport">传输。</param>
        /// <param name="errorLog">记录读取循环和事件订阅方的意外异常;为 null 时不记录。</param>
        public ZevisionDeviceService(IDeviceTransport transport, IErrorLog? errorLog = null)
            : this(CheckTransport(transport), errorLog)
        {
        }

        /// <inheritdoc />
        public event EventHandler<FrameSentEventArgs>? FrameSent;

        /// <inheritdoc />
        public event EventHandler<FrameReceivedEventArgs>? FrameReceived;

        /// <inheritdoc />
        public event EventHandler<ConnectionLostEventArgs>? ConnectionLost;

        /// <inheritdoc />
        public bool IsConnected => Current?.IsConnected == true;

        private ZevisionClient? Current
        {
            get
            {
                lock (_sync)
                {
                    return _client;
                }
            }
        }

        /// <inheritdoc />
        public async Task<Result> ConnectAsync(ConnectionOptions options, CancellationToken cancellationToken)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ZevisionClient client;
            ZevisionClient? previous;
            lock (_sync)
            {
                if (_connecting || _client?.IsConnected == true)
                {
                    return Result.Fail(Lang.T("已经连接。", "Already connected."));
                }

                _connecting = true;
                previous = _client;
                client = new ZevisionClient(options, _transportFactory);
                Attach(client);
                _client = client;
            }

            try
            {
                if (previous != null)
                {
                    // 上一次的连接已断开 (主动断开或连接中断):不再转发它的事件。
                    Detach(previous);
                    previous.Dispose();
                }

                var result = await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return result.IsSuccess ? Result.Ok() : Result.Fail(CommunicationText.Describe(result.Error!));
            }
            finally
            {
                lock (_sync)
                {
                    _connecting = false;
                }
            }
        }

        /// <inheritdoc />
        public Task DisconnectAsync() => Current?.DisconnectAsync() ?? Task.CompletedTask;

        /// <inheritdoc />
        public async Task<Result<ZevisionReply>> SendAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken)
        {
            var client = Current;
            if (client == null)
            {
                return Result<ZevisionReply>.Fail(Lang.T("未连接。", "Not connected."));
            }

            var result = await client.SendAsync(command, minimumResponseTimeout, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? Result<ZevisionReply>.Ok(result.Value)
                : Result<ZevisionReply>.Fail(CommunicationText.Describe(result.Error!, command));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            ZevisionClient? client;
            lock (_sync)
            {
                client = _client;
            }

            client?.Dispose();
        }

        private void Attach(ZevisionClient client)
        {
            client.FrameSent += OnFrameSent;
            client.FrameReceived += OnFrameReceived;
            client.ConnectionLost += OnConnectionLost;
            client.HandlerFailed += OnHandlerFailed;
        }

        private void Detach(ZevisionClient client)
        {
            client.FrameSent -= OnFrameSent;
            client.FrameReceived -= OnFrameReceived;
            client.ConnectionLost -= OnConnectionLost;
            client.HandlerFailed -= OnHandlerFailed;
        }

        private void OnFrameSent(object? sender, FrameSentEventArgs e) => FrameSent?.Invoke(this, e);

        private void OnFrameReceived(object? sender, FrameReceivedEventArgs e) => FrameReceived?.Invoke(this, e);

        private void OnConnectionLost(object? sender, ConnectionLostEventArgs e)
        {
            var ex = e.Exception;
            if (!(ex is IOException || ex is InvalidOperationException || ex is ObjectDisposedException || ex is UnauthorizedAccessException))
            {
                // 拔线、对端关闭是预期内的;其他异常说明读取时出了意外错误,记下来以便诊断。
                _errorLog?.Write(Lang.T("读取循环", "Read loop"), Lang.T("读取循环意外终止: ", "The read loop ended unexpectedly: ") + ex.Message, ex);
            }

            ConnectionLost?.Invoke(this, e);
        }

        private void OnHandlerFailed(object? sender, HandlerFailedEventArgs e) =>
            _errorLog?.Write(Lang.T("事件处理", "Event handler"), Lang.T($"{e.EventName} 的处理程序出错: {e.Exception.Message}", $"A {e.EventName} handler failed: {e.Exception.Message}"), e.Exception);

        private static Func<ConnectionEndpoint, IDeviceTransport> CheckTransport(IDeviceTransport transport)
        {
            if (transport is null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            return _ => transport;
        }
    }
}
