using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Communication;
using Zevision.Core;
using Zevision.Core.Devices;
using Zevision.Xtc3;

namespace Zevision.Bridge
{
    /// <summary>
    /// XTC/3S 协议兼容中间层 (ZEV-FAR-01):在主机侧串口 (虚拟串口对的一端) 按 XTC/3 Standard Protocol 应答主机软件,
    /// 经 Zevision 协议访问 IMC300。
    /// </summary>
    /// <remarks>
    /// <para><see cref="StartAsync"/> 后在后台:连接 IMC300、握手、核对影子对象、每 250 ms 采样;连接中断后按
    /// <see cref="ReconnectPolicy"/> 自动重连。主机侧串口打开失败或被关闭时每 2 s 重试。</para>
    /// <para>与 IMC300 未连接 (或尚未核对完) 时,需要设备数据的命令回答 E;E、H 和本地参数照常回答 (ZEV-FAR-01 的 P-06)。</para>
    /// <para>事件在后台线程上触发。</para>
    /// </remarks>
    public sealed class Xtc3Bridge : IDisposable
    {
        /// <summary>主机侧串口打开失败后重试的间隔。</summary>
        public static readonly TimeSpan HostRetryInterval = TimeSpan.FromSeconds(2);

        private readonly Xtc3BridgeOptions _options;
        private readonly IDeviceService _device;
        private readonly IErrorLog? _errorLog;
        private readonly Func<ConnectionEndpoint, IDeviceTransport> _hostTransportFactory;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly SemaphoreSlim _packetLock = new SemaphoreSlim(1, 1);
        private readonly object _runLock = new object();
        private readonly BridgeEngine _engine;
        private CancellationTokenSource? _run;
        private Task _deviceLoop = Task.CompletedTask;
        private Task _hostLoop = Task.CompletedTask;
        private IDeviceTransport? _hostTransport;
        private TaskCompletionSource<bool> _lost = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _disposed;

        /// <summary>
        /// 创建中间层。
        /// </summary>
        /// <param name="options">设置。</param>
        /// <param name="device">与 IMC300 的连接 (<see cref="ZevisionDeviceService"/>);由中间层连接和断开,中间层不释放它。</param>
        /// <param name="state">本地参数的存储。</param>
        /// <param name="errorLog">错误日志 (<see cref="BridgeLogLevel.Error"/> 的记录写入);可为 null。</param>
        /// <param name="hostTransportFactory">主机侧串口的传输 (测试用);默认 <see cref="DeviceTransports.Create"/>。</param>
        public Xtc3Bridge(Xtc3BridgeOptions options, IDeviceService device, IBridgeStateStore state, IErrorLog? errorLog = null, Func<ConnectionEndpoint, IDeviceTransport>? hostTransportFactory = null)
            : this(options, device, state, errorLog, hostTransportFactory, null)
        {
        }

        /// <summary>测试用:注入时钟 (经过的时间)。</summary>
        internal Xtc3Bridge(
            Xtc3BridgeOptions options, IDeviceService device, IBridgeStateStore state, IErrorLog? errorLog, Func<ConnectionEndpoint, IDeviceTransport>? hostTransportFactory, Func<TimeSpan>? clock)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            options.Validate();
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _errorLog = errorLog;
            _hostTransportFactory = hostTransportFactory ?? DeviceTransports.Create;
            _engine = new BridgeEngine(options, device, state ?? throw new ArgumentNullException(nameof(state)), clock ?? (() => _clock.Elapsed), Write);
            _device.ConnectionLost += OnConnectionLost;
        }

        /// <summary>运行情况和错误的记录。</summary>
        public event EventHandler<BridgeLogEventArgs>? Log;

        /// <summary>处理完主机的一个包 (含包错误和超时丢弃的字节)。</summary>
        public event EventHandler<HostExchangeEventArgs>? HostExchange;

        /// <summary>与 IMC300 的连接已就绪 (握手、影子对象核对完成)。</summary>
        public bool IsDeviceOnline => _engine.IsOnline;

        /// <summary>主机侧串口是否已打开。</summary>
        public bool IsHostPortOpen => _hostTransport?.IsOpen == true;

        /// <summary>各 Film 的影子对象核对结果 (下标 0 为 Film 1)。</summary>
        public IReadOnlyList<ShadowCheck> ShadowChecks => _engine.ShadowChecks;

        /// <summary>当前 tick (响应中的 Timer)。</summary>
        public byte Timer => _engine.Timer;

        internal BridgeEngine Engine => _engine;

        /// <summary>
        /// 开始运行:后台连接 IMC300,并打开主机侧串口 (<see cref="Xtc3BridgeOptions.HostPort"/> 不为 null 时)。立即返回。
        /// </summary>
        /// <exception cref="InvalidOperationException">已在运行。</exception>
        public Task StartAsync()
        {
            lock (_runLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(Xtc3Bridge));
                }

                if (_run != null)
                {
                    throw new InvalidOperationException("The bridge is already running.");
                }

                _run = new CancellationTokenSource();
                var token = _run.Token;
                _deviceLoop = Task.Run(() => RunDeviceAsync(token));
                _hostLoop = _options.HostPort is { } host ? Task.Run(() => RunHostAsync(host, token)) : Task.CompletedTask;
            }

            return Task.CompletedTask;
        }

        /// <summary>停止:关闭主机侧串口,停止采样,断开 IMC300。未运行时直接返回。</summary>
        public async Task StopAsync()
        {
            CancellationTokenSource? run;
            lock (_runLock)
            {
                run = _run;
                _run = null;
            }

            if (run == null)
            {
                return;
            }

            run.Cancel();
            _hostTransport?.Close(); // 串口的读取不响应取消,关闭才结束
            _lost.TrySetResult(true);
            await Task.WhenAll(_deviceLoop, _hostLoop).ConfigureAwait(false);
            run.Dispose();
        }

        /// <summary>
        /// 处理主机的一个包,返回响应包 (不回答时为 null)。主机侧串口和测试调用;同一时间只处理一个包。
        /// </summary>
        /// <param name="packet"><see cref="Xtc3PacketDecoder"/> 解出的包。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task<byte[]?> ProcessPacketAsync(Xtc3Packet packet, CancellationToken cancellationToken)
        {
            if (packet is null)
            {
                throw new ArgumentNullException(nameof(packet));
            }

            await _packetLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var started = _clock.Elapsed;
                IReadOnlyList<Xtc3Command> commands = Array.Empty<Xtc3Command>();
                IReadOnlyList<Xtc3CommandResult> results = Array.Empty<Xtc3CommandResult>();
                byte[]? response;
                if (packet.Discarded.Length > 0)
                {
                    response = null;
                    Write(BridgeLogLevel.Warning, "Discarded " + packet.Discarded.Length + " bytes of an incomplete packet (more than 3 s between characters).", null);
                }
                else if (packet.Error is { } packetError)
                {
                    response = Xtc3ResponseEncoder.EncodePacketError(_engine.Timer, packetError);
                }
                else
                {
                    var parsed = Xtc3MessageParser.Parse(packet.Message);
                    if (parsed.Error is { } parseError)
                    {
                        response = Xtc3ResponseEncoder.EncodePacketError(_engine.Timer, parseError);
                    }
                    else
                    {
                        commands = parsed.Commands;
                        results = await _engine.ExecuteAsync(commands, cancellationToken).ConfigureAwait(false);
                        response = Xtc3ResponseEncoder.Encode(_engine.Timer, results);
                    }
                }

                RaiseHostExchange(new HostExchangeEventArgs(
                    packet.Discarded.Length > 0 ? packet.Discarded : packet.Message, response, commands, results, _clock.Elapsed - started));
                return response;
            }
            finally
            {
                _packetLock.Release();
            }
        }

        /// <summary>
        /// 创建缺失的影子对象 (设备应处于 Ready),然后重新核对。用于 <see cref="Xtc3BridgeOptions.CreateMissingShadowObjects"/> 为 false 时,
        /// 由使用者确认后调用。
        /// </summary>
        /// <returns>未连接、读取设备配置失败或创建失败时失败。</returns>
        public async Task<Result> CreateShadowObjectsAsync(CancellationToken cancellationToken)
        {
            if (!_engine.IsOnline)
            {
                return Result.Fail("The IMC300 is not connected.");
            }

            await _packetLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _engine.CreateShadowObjectsAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _packetLock.Release();
            }
        }

        /// <summary>停止运行 (同步等待),不释放 IMC300 的连接对象。</summary>
        public void Dispose()
        {
            lock (_runLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _device.ConnectionLost -= OnConnectionLost;

            // 停止中不调用 UI 线程,同步等待不会死锁;调用方在退出时使用。
            Task.Run(StopAsync).GetAwaiter().GetResult();
            _packetLock.Dispose();
        }

        // ---------------------------------------------------------------- IMC300

        private async Task RunDeviceAsync(CancellationToken cancellationToken)
        {
            var attempt = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (await ConnectAsync(cancellationToken).ConfigureAwait(false))
                    {
                        attempt = 0;
                        await SampleUntilLostAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Write(BridgeLogLevel.Error, "IMC300 connection loop failed: " + ex.Message, ex);
                }

                _engine.IsOnline = false;
                await _device.DisconnectAsync().ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                attempt++;
                var delay = ReconnectPolicy.DelayBefore(attempt);
                Write(BridgeLogLevel.Info, "Reconnecting to the IMC300 in " + delay.TotalSeconds + " s (attempt " + attempt + ").", null);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>连接、握手、准备 (读取配置、核对影子对象)。</summary>
        private async Task<bool> ConnectAsync(CancellationToken cancellationToken)
        {
            _lost = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var connected = await _device.ConnectAsync(_options.Device, cancellationToken).ConfigureAwait(false);
            if (!connected.IsSuccess)
            {
                Write(BridgeLogLevel.Error, "Cannot connect to the IMC300 (" + _options.Device.Endpoint + "): " + connected.Error, null);
                return false;
            }

            var identity = await _device.HandshakeAsync(_options.Device.Frame, cancellationToken).ConfigureAwait(false);
            if (!identity.IsSuccess)
            {
                Write(BridgeLogLevel.Error, "IMC300 handshake failed: " + identity.Error, null);
                return false;
            }

            foreach (var warning in identity.Value.Warnings)
            {
                Write(BridgeLogLevel.Warning, warning, null);
            }

            Write(BridgeLogLevel.Info, "Connected to " + identity.Value.Hello + " at " + _options.Device.Endpoint + ", " + identity.Value.Frame + ".", null);
            var prepared = await _engine.PrepareAsync(cancellationToken).ConfigureAwait(false);
            if (!prepared.IsSuccess)
            {
                Write(BridgeLogLevel.Error, "Reading the IMC300 configuration failed: " + prepared.Error, null);
                return false;
            }

            _engine.IsOnline = true;
            var ready = _engine.ShadowChecks.Where(c => c.Status == ShadowStatus.Ready).Select(c => c.Film.Number).ToList();
            Write(BridgeLogLevel.Info, "IMC300 ready; films with shadow objects: " + (ready.Count == 0 ? "none" : string.Join(", ", ready)) + ".", null);
            return true;
        }

        /// <summary>每个 tick 采样一次,直到连接中断、连续 <see cref="Xtc3BridgeOptions.SampleMissLimit"/> 次采样失败或停止。</summary>
        private async Task SampleUntilLostAsync(CancellationToken cancellationToken)
        {
            var lost = _lost.Task;
            var misses = 0;
            var interval = Xtc3Protocol.TickInterval;
            while (!cancellationToken.IsCancellationRequested && !lost.IsCompleted)
            {
                var wait = TimeSpan.FromTicks(interval.Ticks - (_clock.Elapsed.Ticks % interval.Ticks));
                await Task.WhenAny(Task.Delay(wait, cancellationToken), lost).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested || lost.IsCompleted)
                {
                    break;
                }

                misses = await _engine.SampleAsync(cancellationToken).ConfigureAwait(false) ? 0 : misses + 1;
                if (misses >= _options.SampleMissLimit)
                {
                    Write(BridgeLogLevel.Error, "The IMC300 did not answer " + misses + " samples; reconnecting.", null);
                    break;
                }
            }

            if (lost.IsCompleted && !cancellationToken.IsCancellationRequested)
            {
                Write(BridgeLogLevel.Error, "Connection to the IMC300 lost.", null);
            }
        }

        private void OnConnectionLost(object? sender, ConnectionLostEventArgs e)
        {
            _engine.IsOnline = false;
            _lost.TrySetResult(true);
        }

        // ---------------------------------------------------------------- 主机侧串口

        private async Task RunHostAsync(SerialSettings settings, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                var baud = _engine.HostBaudRate(settings.BaudRate);
                var endpoint = new SerialSettings(settings.PortName, baud);
                var transport = _hostTransportFactory(endpoint);
                try
                {
                    await transport.OpenAsync(endpoint, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    transport.Dispose();
                    Write(BridgeLogLevel.Error, "Cannot open the host port " + endpoint + ": " + ex.Message, ex);
                    if (!await DelayAsync(HostRetryInterval, cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }

                    continue;
                }
                catch (OperationCanceledException)
                {
                    transport.Dispose();
                    break;
                }

                _hostTransport = transport;
                Write(BridgeLogLevel.Info, "Host port " + endpoint + " open.", null);
                var failed = false;
                try
                {
                    await ServeAsync(transport, buffer, baud, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException || ex is OperationCanceledException)
                {
                    failed = !cancellationToken.IsCancellationRequested;
                    if (failed)
                    {
                        Write(BridgeLogLevel.Error, "Host port " + endpoint + " failed: " + ex.Message, ex);
                    }
                }
                finally
                {
                    _hostTransport = null;
                    transport.Dispose();
                }

                // 波特率修改后立即重新打开;出错后等一会儿再打开 (端口反复出错时不空转)。
                if (cancellationToken.IsCancellationRequested || (failed && !await DelayAsync(HostRetryInterval, cancellationToken).ConfigureAwait(false)))
                {
                    break;
                }
            }
        }

        /// <summary>读取主机的字节,解包、处理、回答;主机修改了波特率 (UG14) 时在发出响应后返回,由调用方按新波特率重新打开。</summary>
        private async Task ServeAsync(IDeviceTransport transport, byte[] buffer, int baud, CancellationToken cancellationToken)
        {
            var decoder = new Xtc3PacketDecoder();
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await transport.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    throw new IOException("The port was closed.");
                }

                foreach (var packet in decoder.Push(buffer, count, _clock.Elapsed))
                {
                    var response = await ProcessPacketAsync(packet, cancellationToken).ConfigureAwait(false);
                    if (response != null)
                    {
                        await transport.WriteAsync(response, cancellationToken).ConfigureAwait(false);
                    }

                    if (_engine.TakePendingBaudRate() is { } newBaud && newBaud != baud)
                    {
                        Write(BridgeLogLevel.Info, "Host changed the baud rate to " + newBaud + " (UG14).", null);
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false); // 让响应发完再关闭端口
                        return;
                    }
                }
            }
        }

        private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        // ---------------------------------------------------------------- 记录

        private void Write(BridgeLogLevel level, string message, Exception? exception)
        {
            if (level == BridgeLogLevel.Error)
            {
                _errorLog?.Write("XTC3 bridge", message, exception);
            }

            try
            {
                Log?.Invoke(this, new BridgeLogEventArgs(level, message, exception));
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _errorLog?.Write("XTC3 bridge", "Log handler failed: " + ex.Message, ex);
            }
        }

        private void RaiseHostExchange(HostExchangeEventArgs e)
        {
            try
            {
                HostExchange?.Invoke(this, e);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _errorLog?.Write("XTC3 bridge", "HostExchange handler failed: " + ex.Message, ex);
            }
        }
    }
}
