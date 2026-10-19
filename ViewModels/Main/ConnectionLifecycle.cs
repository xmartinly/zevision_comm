using System;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Communication;
using Zevision.Core;
using Zevision.Core.Devices;
using Zevision.Core.Localization;
using Zevision.ViewModels.Display;

namespace Zevision.ViewModels.Main
{
    /// <summary>连接的状态。</summary>
    public enum ConnectionState
    {
        /// <summary>未连接。</summary>
        Disconnected,

        /// <summary>正在打开通道 (串口或 TCP)。</summary>
        Connecting,

        /// <summary>通道已打开,正在握手。</summary>
        Handshaking,

        /// <summary>已连接 (握手已完成,或没有启用握手)。</summary>
        Connected,

        /// <summary>连接中断后,等待下一次自动重连。</summary>
        Reconnecting,
    }

    /// <summary>
    /// 连接的生命周期:打开通道 → 握手 (可关闭) → 已连接;意外中断后按设置自动重连 (<see cref="ReconnectPolicy"/>);
    /// 设备的帧格式设置被修改后重新握手。每次连接成功时打开通讯数据文件 (设置了保存通讯数据时),断开或中断时关闭。
    /// </summary>
    /// <remarks>
    /// 只在 UI 线程上调用 (设备事件由 <see cref="MainViewModel"/> 转到 UI 线程后交给 <see cref="OnConnectionLost"/>)。
    /// 自动重连不恢复被中断的保存/恢复/修改和重复发送。
    /// </remarks>
    internal sealed class ConnectionLifecycle
    {
        private readonly IDeviceService _device;
        private readonly ICommunicationRecorderFactory _recorders;
        private readonly LogViewModel _log;
        private readonly ConnectionSettingsViewModel _settings;
        private ICommunicationRecorder? _recorder;
        private ConnectionOptions? _options;
        private Task _connecting = Task.CompletedTask;
        private CancellationTokenSource? _reconnect;
        private Task _reconnectLoop = Task.CompletedTask;

        public ConnectionLifecycle(IDeviceService device, ICommunicationRecorderFactory recorders, LogViewModel log, ConnectionSettingsViewModel settings)
        {
            _device = device;
            _recorders = recorders;
            _log = log;
            _settings = settings;
        }

        /// <summary><see cref="State"/> 或重连信息改变 (UI 线程)。</summary>
        public event EventHandler? StateChanged;

        public ConnectionState State { get; private set; }

        /// <summary>自动重连进行中 (等待或正在重连);此时可以用 <see cref="DisconnectAsync"/> 停止。</summary>
        public bool IsReconnecting => _reconnect != null;

        /// <summary>最近一次连接的目标,如 <c>COM3, 115200, 8N1</c>。</summary>
        public string Endpoint { get; private set; } = string.Empty;

        /// <summary>最近一次成功连接的参数;从未连接成功时为 null。</summary>
        public ConnectionOptions? Options => _options;

        /// <summary>第几次重连 (<see cref="ConnectionState.Reconnecting"/>)。</summary>
        public int ReconnectAttempt { get; private set; }

        /// <summary>这次重连前的等待。</summary>
        public TimeSpan ReconnectDelay { get; private set; }

        /// <summary>重连前的等待;测试替换为不等待。</summary>
        internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

        /// <summary>按当前设置连接 (未连接时)。</summary>
        public Task ConnectAsync()
        {
            var created = _settings.CreateOptions();
            if (!created.IsSuccess)
            {
                _log.Add(LogKind.Error, created.Error);
                return Task.CompletedTask;
            }

            var connecting = OpenAsync(created.Value, reconnectAttempt: 0, CancellationToken.None);
            _connecting = connecting;
            return connecting;
        }

        /// <summary>等待正在进行的连接 (不是自动重连) 结束。</summary>
        public Task WaitForConnectAsync() => _connecting;

        /// <summary>断开,或停止自动重连。之后为 <see cref="ConnectionState.Disconnected"/>,通讯数据文件已关闭。</summary>
        public async Task DisconnectAsync()
        {
            var wasReconnecting = await StopReconnectAsync();
            await _device.DisconnectAsync();
            if (State == ConnectionState.Connected)
            {
                _log.Add(LogKind.Info, Lang.T("已断开。", "Disconnected."));
            }
            else if (wasReconnecting)
            {
                _log.Add(LogKind.Info, Lang.T("已停止自动重连。", "Automatic reconnection stopped."));
            }

            SetState(ConnectionState.Disconnected);
            await CloseRecorderAsync();
        }

        /// <summary>程序退出时的兜底:取消等待中的自动重连 (不等待;正常退出时窗口关闭前已经断开)。</summary>
        public void CancelReconnect() => _reconnect?.Cancel();

        /// <summary>
        /// 程序退出时的兜底:在调用线程上写完缓存的通讯数据并关闭文件 (正常退出时窗口关闭前已经断开并关闭;
        /// 注销、关机时窗口不经过 Closing,后台写入可能来不及完成)。
        /// </summary>
        public void CloseRecorder()
        {
            var recorder = _recorder;
            _recorder = null;
            _log.Recorder = null;
            recorder?.Dispose();
        }

        /// <summary>停止保存通讯数据:之后的显示不再写入,缓存和已排队的内容在后台写完后关闭文件。</summary>
        public Task CloseRecorderAsync()
        {
            var recorder = _recorder;
            _recorder = null;
            _log.Recorder = null;
            return CloseAsync(recorder);
        }

        /// <summary>
        /// 连接意外中断 (UI 线程)。只处理已连接时的中断:连接或握手期间的中断由连接流程报告。
        /// 设置了自动重连时开始重连,否则为未连接。
        /// </summary>
        public void OnConnectionLost(ConnectionLostEventArgs e)
        {
            if (State != ConnectionState.Connected)
            {
                return;
            }

            var reason = CommunicationText.DescribeConnectionLost(e, _options?.HeartbeatMissLimit ?? ConnectionOptions.DefaultHeartbeatMissLimit);
            _log.Add(LogKind.Error, Lang.T("连接中断: ", "Connection lost: ") + reason);

            // 不需要等待:CloseAsync 不会失败,文件在已排队的内容写完后关闭。
            _ = CloseRecorderAsync();
            if (_settings.IsAutoReconnectEnabled)
            {
                _reconnect = new CancellationTokenSource();
                _reconnectLoop = ReconnectLoopAsync(_reconnect.Token);
            }
            else
            {
                SetState(ConnectionState.Disconnected);
            }
        }

        /// <summary>
        /// 设备接受了修改帧格式的指令 (如 <c>UG1304:1</c>):设备从下一帧起只认新格式。
        /// 启用握手时按新格式重新握手并更新界面上的开关,失败则断开;没有启用握手时只提示。
        /// </summary>
        /// <param name="expected">修改后的帧格式。</param>
        public async Task OnFrameSettingsChangedAsync(FrameOptions expected)
        {
            if (State != ConnectionState.Connected)
            {
                return;
            }

            var description = _settings.DescribeFrame(expected);
            if (!_settings.IsHandshakeEnabled)
            {
                _log.Add(LogKind.Info, Lang.T(
                    $"设备的帧格式已改为: {description}。程序仍按原来的格式通讯;请断开,修改协议选项后重新连接。",
                    $"The device's frame format is now: {description}. The program still uses the old format; disconnect, change the protocol options and connect again."));
                return;
            }

            var identity = await _device.HandshakeAsync(expected, CancellationToken.None);
            if (State != ConnectionState.Connected)
            {
                return; // 握手期间用户断开了连接
            }

            if (!identity.IsSuccess)
            {
                _log.Add(LogKind.Error, Lang.T("按设备的新帧格式重新握手失败,已断开: ", "The handshake with the device's new frame format failed; disconnected: ") + identity.Error);
                await DisconnectAsync();
                return;
            }

            _settings.ApplyFrame(identity.Value.Frame);
            _log.Add(LogKind.Info, Lang.T("已改用设备的新帧格式: ", "Now using the device's new frame format: ") + _settings.DescribeFrame(identity.Value.Frame) + Lang.T("。", "."));
            LogWarnings(identity.Value);
        }

        /// <summary>打开通道并握手 (启用时)。</summary>
        /// <param name="options">连接参数。</param>
        /// <param name="reconnectAttempt">第几次自动重连;0 表示用户发起的连接 (失败时记为错误,重连失败只作为信息)。</param>
        /// <param name="cancellationToken">停止重连时取消。</param>
        /// <returns>是否已连接。</returns>
        private async Task<bool> OpenAsync(ConnectionOptions options, int reconnectAttempt, CancellationToken cancellationToken)
        {
            ICommunicationRecorder? recorder = null;
            if (_settings.IsSaveCommunicationDataEnabled)
            {
                var file = _recorders.Create();
                if (!file.IsSuccess)
                {
                    ReportFailure(reconnectAttempt, file.Error);
                    return false;
                }

                recorder = file.Value;
            }

            Endpoint = options.Endpoint.ToString();
            string? failure = null;
            var opened = false;
            var connected = false;
            try
            {
                SetState(ConnectionState.Connecting);
                var result = await _device.ConnectAsync(options, cancellationToken);
                if (!result.IsSuccess)
                {
                    failure = result.Error;
                    return false;
                }

                opened = true;
                DeviceIdentity? identity = null;
                if (_settings.IsHandshakeEnabled)
                {
                    SetState(ConnectionState.Handshaking);
                    var handshake = await _device.HandshakeAsync(options.Frame, cancellationToken);
                    if (!handshake.IsSuccess)
                    {
                        failure = Lang.T("握手失败: ", "Handshake failed: ") + handshake.Error;
                        return false;
                    }

                    identity = handshake.Value;
                }

                connected = true;
                OnConnected(options, identity, recorder, reconnectAttempt);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false; // 停止重连:不报告。
            }
            finally
            {
                // 任何失败 (含意外异常) 都关闭通道和文件,用户发起的连接回到未连接,界面不会停在"正在连接"。
                if (!connected)
                {
                    if (opened)
                    {
                        await _device.DisconnectAsync();
                    }

                    await CloseAsync(recorder);
                    if (failure != null)
                    {
                        ReportFailure(reconnectAttempt, failure);
                    }

                    if (reconnectAttempt == 0)
                    {
                        SetState(ConnectionState.Disconnected);
                    }
                }
            }
        }

        /// <summary>连接成功:开始保存通讯数据,按设备更新帧格式开关,记录连接和设备信息。</summary>
        private void OnConnected(ConnectionOptions options, DeviceIdentity? identity, ICommunicationRecorder? recorder, int reconnectAttempt)
        {
            // 从下面这几行开始,显示的内容同时追加到文件。
            _recorder = recorder;
            _log.Recorder = recorder;
            _options = options;
            if (identity != null && !identity.Frame.Equals(options.Frame))
            {
                _settings.ApplyFrame(identity.Frame);
                _log.Add(LogKind.Info, Lang.T("设备的帧格式与协议选项不同,已改用设备的设置。", "The device's frame format differs from the protocol options; now using the device's settings."));
            }

            EndReconnect();
            SetState(ConnectionState.Connected);
            if (reconnectAttempt > 0)
            {
                _log.Add(LogKind.Info, Lang.T("已重新连接。", "Reconnected."));
            }

            _log.Add(LogKind.Info, _settings.DescribeConnected(options));
            if (identity != null)
            {
                _log.Add(LogKind.Info, Lang.T("设备: ", "Device: ") + identity.Hello);
                LogWarnings(identity);
            }

            if (recorder != null)
            {
                _log.Add(LogKind.Info, Lang.T("通讯数据保存到: ", "Communication data is saved to: ") + recorder.Path);
            }
        }

        /// <summary>自动重连:按 <see cref="ReconnectPolicy"/> 等待后用当前设置连接 (连接期间设置已锁定),直到成功或停止。</summary>
        private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
        {
            for (var attempt = 1; !cancellationToken.IsCancellationRequested; attempt++)
            {
                ReconnectAttempt = attempt;
                ReconnectDelay = ReconnectPolicy.DelayBefore(attempt);
                SetState(ConnectionState.Reconnecting);
                try
                {
                    await Delay(ReconnectDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var created = _settings.CreateOptions();
                if (!created.IsSuccess)
                {
                    ReportFailure(attempt, created.Error);
                    continue;
                }

                try
                {
                    if (await OpenAsync(created.Value, attempt, cancellationToken))
                    {
                        return;
                    }
                }
#pragma warning disable CA1031 // 重连循环在后台运行,没有调用方接收异常:记入错误日志后停止重连,回到未连接 (OpenAsync 已关闭通道和文件)。
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _log.ReportException(ex);
                    EndReconnect();
                    SetState(ConnectionState.Disconnected);
                    return;
                }
            }
        }

        /// <summary>停止自动重连并等它结束。</summary>
        /// <returns>是否有正在进行的重连。</returns>
        private async Task<bool> StopReconnectAsync()
        {
            var reconnect = _reconnect;
            if (reconnect == null)
            {
                return false;
            }

            reconnect.Cancel();
            await _reconnectLoop;
            reconnect.Dispose();
            if (_reconnect == reconnect)
            {
                _reconnect = null;
            }

            return true;
        }

        /// <summary>连接成功:不再是重连中 (重连循环随后结束)。</summary>
        private void EndReconnect()
        {
            var reconnect = _reconnect;
            _reconnect = null;
            reconnect?.Dispose();
        }

        private void ReportFailure(int reconnectAttempt, string? error)
        {
            if (reconnectAttempt == 0)
            {
                _log.Add(LogKind.Error, error ?? Lang.T("连接失败。", "Connection failed."));
            }
            else
            {
                // 重连失败只作为信息:每分钟一次的失败不写入 error.txt。
                _log.Add(LogKind.Info, Lang.T($"第 {reconnectAttempt} 次重连失败: ", $"Reconnection attempt {reconnectAttempt} failed: ") + error);
            }
        }

        private void LogWarnings(DeviceIdentity identity)
        {
            foreach (var warning in identity.Warnings)
            {
                _log.Add(LogKind.Info, warning);
            }
        }

        private static Task CloseAsync(ICommunicationRecorder? recorder) => recorder?.CloseAsync() ?? Task.CompletedTask;

        private void SetState(ConnectionState state)
        {
            State = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
