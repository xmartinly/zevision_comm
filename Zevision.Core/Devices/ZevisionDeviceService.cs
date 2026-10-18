using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <remarks>
    /// 不显示的收发 (<see cref="SendHiddenAsync"/>):通讯库在发送的异步流中触发 <c>FrameSent</c> (以及超时时报告的不完整数据),
    /// 用 <see cref="AsyncLocal{T}"/> 标记这个流即可识别;响应在读取循环中收到,按回显识别。
    /// 隐藏的回显保留到同一指令再次正常发送为止,因此取消后设备仍在发送的剩余数据也不显示。
    /// <para>
    /// 帧格式不一致:每个收到的帧用 <see cref="FramingCheck"/> 与当前的帧格式比较。一次发送失败 (超时、帧无效、无法解析),
    /// 而发送期间收到过格式不一致的帧时,错误说明后附上检查设置的提示 (<see cref="CommunicationText.FramingHint"/>)。
    /// </para>
    /// </remarks>
    public sealed class ZevisionDeviceService : IDeviceService
    {
        private static readonly AsyncLocal<bool> Hidden = new AsyncLocal<bool>();

        private readonly Func<ConnectionEndpoint, IDeviceTransport> _transportFactory;
        private readonly IErrorLog? _errorLog;
        private readonly object _sync = new object();
        private readonly HashSet<string> _hiddenEchoes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private ZevisionClient? _client;
        private ConnectionOptions? _options;
        private bool _connecting;

        /// <summary>收到格式不一致的帧的次数 (发送前后比较) 和最近一次的情况。用 <see cref="_sync"/> 保护。</summary>
        private int _mismatchCount;
        private FramingMismatch _lastMismatch;

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
                    return Result.Fail(Texts.AlreadyConnected);
                }

                _connecting = true;
                previous = _client;
                _hiddenEchoes.Clear();
                _options = options;
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
        public Task<Result<ZevisionReply>> SendAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken) =>
            SendCoreAsync(command, minimumResponseTimeout, hidden: false, cancellationToken);

        /// <inheritdoc />
        public Task<Result<ZevisionReply>> SendHiddenAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken) =>
            SendCoreAsync(command, minimumResponseTimeout, hidden: true, cancellationToken);

        private async Task<Result<ZevisionReply>> SendCoreAsync(string command, TimeSpan minimumResponseTimeout, bool hidden, CancellationToken cancellationToken)
        {
            var client = Current;
            if (client == null)
            {
                return Result<ZevisionReply>.Fail(Texts.NotConnected);
            }

            // 只作用于这次发送的异步流 (方法返回后恢复):通讯库在这个流中触发 FrameSent 和超时时的不完整数据。
            Hidden.Value = hidden;
            var mismatches = MismatchCount;
            var result = await client.SendAsync(command, minimumResponseTimeout, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                return Result<ZevisionReply>.Ok(result.Value);
            }

            var message = CommunicationText.Describe(result.Error!, command);
            var hint = FramingHintSince(mismatches, result.Error!.Kind);
            return Result<ZevisionReply>.Fail(hint == null ? message : message + (Lang.IsChinese ? string.Empty : " ") + hint);
        }

        private int MismatchCount
        {
            get
            {
                lock (_sync)
                {
                    return _mismatchCount;
                }
            }
        }

        /// <summary>
        /// 发送期间 (计数变了) 收到过格式不一致的帧,且失败是格式不一致会造成的 (超时、帧无效、无法解析) 时,检查设置的提示;否则为 null。
        /// </summary>
        private string? FramingHintSince(int mismatchesBefore, ZevisionErrorKind kind)
        {
            if (kind != ZevisionErrorKind.Timeout && kind != ZevisionErrorKind.InvalidFrame && kind != ZevisionErrorKind.UnparseableResponse)
            {
                return null;
            }

            lock (_sync)
            {
                return _mismatchCount == mismatchesBefore || _options == null ? null : CommunicationText.FramingHint(_lastMismatch, _options.Endpoint);
            }
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

        private void OnFrameSent(object? sender, FrameSentEventArgs e)
        {
            // 在发送的时刻 (取得发送锁之后) 记下回显:不显示的指令之后收到的同回显帧也不显示,正常发送同一指令后恢复显示。
            var echo = ZevisionResponseParser.EchoOf(e.Command);
            lock (_sync)
            {
                if (Hidden.Value)
                {
                    _hiddenEchoes.Add(echo);
                }
                else
                {
                    _hiddenEchoes.Remove(echo);
                }
            }

            if (!Hidden.Value)
            {
                FrameSent?.Invoke(this, e);
            }
        }

        private void OnFrameReceived(object? sender, FrameReceivedEventArgs e)
        {
            ConnectionOptions? options;
            lock (_sync)
            {
                options = _options;
            }

            var mismatch = options == null ? FramingMismatch.None : FramingCheck.Check(e.Frame, options.Frame);
            if (mismatch != FramingMismatch.None)
            {
                lock (_sync)
                {
                    _lastMismatch = mismatch;
                    _mismatchCount++;
                }
            }

            if (!Hidden.Value && !IsHiddenReply(e.Frame))
            {
                FrameReceived?.Invoke(this, e);
            }
        }

        /// <summary>帧的数据是否以隐藏的回显开头,如 <c>?+:</c>、<c>?M+:</c> (含未收完、校验失败的帧)。</summary>
        private bool IsHiddenReply(ZevisionFrame frame)
        {
            string[] echoes;
            lock (_sync)
            {
                if (_hiddenEchoes.Count == 0)
                {
                    return false;
                }

                echoes = _hiddenEchoes.ToArray();
            }

            var payload = frame.Payload;
            return echoes.Any(echo => payload.Length > echo.Length
                && payload.StartsWith(echo, StringComparison.OrdinalIgnoreCase)
                && (payload[echo.Length] == '+' || payload[echo.Length] == '-'));
        }

        private void OnConnectionLost(object? sender, ConnectionLostEventArgs e)
        {
            var ex = e.Exception;
            if (!(ex is IOException || ex is InvalidOperationException || ex is ObjectDisposedException || ex is UnauthorizedAccessException))
            {
                // 拔线、对端关闭是预期内的;其他异常说明读取时出了意外错误,记下来以便诊断。
                _errorLog?.Write(Texts.LogSourceReadLoop, Texts.ReadLoopEnded + ex.Message, ex);
            }

            ConnectionLost?.Invoke(this, e);
        }

        private void OnHandlerFailed(object? sender, HandlerFailedEventArgs e) =>
            _errorLog?.Write(Texts.LogSourceEventHandler, Texts.HandlerFailed(e.EventName, e.Exception.Message), e.Exception);

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
