using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Zevision.Communication;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Devices;
using Zevision.Core.Localization;
using Zevision.Core.Settings;
using Zevision.ViewModels.Common;
using Zevision.ViewModels.Display;
using Zevision.ViewModels.Operations;

namespace Zevision.ViewModels.Main
{
    /// <summary>
    /// 主窗口视图模型:连接/断开、发送指令、显示收发的帧、保存和恢复界面设置。
    /// 通讯配置见 <see cref="Connection"/>,仪器配置的保存/恢复/修改见 <see cref="Config"/>,
    /// 数据显示区见 <see cref="Log"/>,显示格式和重复发送见 <see cref="SendOptions"/>。
    /// 连接、握手、自动重连和通讯数据文件由 <see cref="ConnectionLifecycle"/> 管理。
    /// </summary>
    public sealed class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly IDeviceService _device;
        private readonly IInstrumentConfigService _config;
        private readonly IPreferencesStore _preferences;
        private readonly IDialogService _dialogs;
        private readonly SynchronizationContext _uiContext;
        private readonly ConnectionLifecycle _link;
        private string _languageSetting = ConnectionPreferences.DefaultLanguage;
        private string _commandText = string.Empty;

        /// <summary>上次从输入框发送的指令,退出时保存,下次启动时填入输入框。</summary>
        private string _lastSentCommand = string.Empty;
        private int _txCount;
        private int _rxCount;
        private int _errorCount;

        /// <summary>
        /// 创建视图模型,扫描可用串口并恢复上次保存的通讯配置。必须在 UI 线程上创建,以便捕获其同步上下文。
        /// </summary>
        /// <param name="device">设备通讯。</param>
        /// <param name="preferences">界面设置的存储。</param>
        /// <param name="config">仪器配置的读写。</param>
        /// <param name="recipes">配置文件的存储。</param>
        /// <param name="recorders">保存通讯数据时为每次连接创建记录。</param>
        /// <param name="dialogs">对话框。</param>
        /// <param name="errorLog">错误日志。</param>
        public MainViewModel(
            IDeviceService device,
            IPreferencesStore preferences,
            IInstrumentConfigService config,
            IRecipeStore recipes,
            ICommunicationRecorderFactory recorders,
            IDialogService dialogs,
            IErrorLog errorLog)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
            if (recorders is null)
            {
                throw new ArgumentNullException(nameof(recorders));
            }

            Log = new LogViewModel(errorLog ?? throw new ArgumentNullException(nameof(errorLog)));
            SendOptions = new SendOptionsViewModel();

            // 重复发送期间不开始配置操作:两者的指令会交错。
            Config = new ConfigOperationsViewModel(config, recipes, dialogs, errorLog, Log, () => IsConnected && !SendOptions.IsRepeating);

            _link = new ConnectionLifecycle(device, recorders, Log, Connection);
            _link.StateChanged += OnLinkStateChanged;

            _device.FrameSent += OnFrameSent;
            _device.FrameReceived += OnFrameReceived;
            _device.ConnectionLost += OnConnectionLost;
            _device.Retrying += OnRetrying;
            _device.CommandsFailed += OnCommandsFailed;

            RefreshPortsCommand = new RelayCommand(_ => Connection.RefreshPorts(), _ => IsDisconnected);
            ConnectCommand = new AsyncRelayCommand(_ => _link.ConnectAsync(), Log.ReportException, _ => IsDisconnected && Connection.CreateOptions().IsSuccess);

            // 连接中断后等待重连时,"断开"停止自动重连。
            DisconnectCommand = new AsyncRelayCommand(_ => DisconnectAsync(), Log.ReportException, _ => (IsConnected && !Config.IsBusy) || _link.IsReconnecting);
            SendCommand = new AsyncRelayCommand(_ => SendInputAsync(), Log.ReportException, _ => CanSend && !string.IsNullOrWhiteSpace(CommandText));
            SendQuickCommand = new AsyncRelayCommand(p => SendAsync(p as string ?? string.Empty), Log.ReportException, p => CanSend && p is string);
            ClearLogCommand = new RelayCommand(_ => ClearLog(), _ => Log.Entries.Count > 0);
            SwitchLanguageCommand = new AsyncRelayCommand(_ => SwitchLanguageAsync(), Log.ReportException, _ => !Config.IsBusy);

            Connection.RefreshPorts();
            ApplyPreferences();
            Log.Add(LogKind.Info, Lang.T("就绪。请选择连接方式并连接。", "Ready. Choose a connection type and connect."));
        }

        /// <summary>
        /// 用户确认切换语言后触发:配置中的语言已更新,由 App 保存配置并重新启动程序。
        /// </summary>
        public event EventHandler? RestartRequested;

        /// <summary>窗口标题 (含版本号)。</summary>
        public string Title { get; } = Lang.T($"IMC300通讯测试 - {AppInfo.ProductName} {AppInfo.Version}", $"IMC300 Communication Test - {AppInfo.ProductName} {AppInfo.Version}");

        /// <summary>通讯配置 (连接方式、串口、IP、协议选项、超时)。</summary>
        public ConnectionSettingsViewModel Connection { get; } = new ConnectionSettingsViewModel();

        /// <summary>发送选项:显示格式、重复发送。</summary>
        public SendOptionsViewModel SendOptions { get; }

        /// <summary>数据显示区。</summary>
        public LogViewModel Log { get; }

        /// <summary>仪器配置的保存、恢复、修改。</summary>
        public ConfigOperationsViewModel Config { get; }

        /// <summary>连接状态。</summary>
        public ConnectionState ConnectionState => _link.State;

        /// <summary>是否已连接 (握手已完成,或没有启用握手)。</summary>
        public bool IsConnected => _link.State == ConnectionState.Connected;

        /// <summary>是否未连接 (也不在连接、握手或等待重连)。用于在连接期间锁定配置控件。</summary>
        public bool IsDisconnected => _link.State == ConnectionState.Disconnected;

        /// <summary>是否正在连接、握手或等待自动重连。</summary>
        public bool IsConnecting => !IsConnected && !IsDisconnected;

        /// <summary>是否已连接、正在连接或等待重连 (关闭窗口前需要 <see cref="CloseConnectionAsync"/>)。</summary>
        public bool HasConnection => !IsDisconnected;

        /// <summary>状态栏文本。</summary>
        public string StatusText
        {
            get
            {
                switch (_link.State)
                {
                    case ConnectionState.Connected:
                        return Lang.T("已连接 ", "Connected to ") + _link.Endpoint;
                    case ConnectionState.Connecting:
                        return Lang.T("正在连接 ", "Connecting to ") + _link.Endpoint + " …";
                    case ConnectionState.Handshaking:
                        return Lang.T("正在握手 …", "Handshaking …");
                    case ConnectionState.Reconnecting:
                        var seconds = _link.ReconnectDelay.TotalSeconds;
                        return Lang.T($"连接中断,{seconds:0} 秒后第 {_link.ReconnectAttempt} 次重连 …", $"Connection lost; reconnection attempt {_link.ReconnectAttempt} in {seconds:0} s …");
                    default:
                        return Lang.T("未连接", "Not connected");
                }
            }
        }

        /// <summary>连接的生命周期 (测试用来替换重连前的等待)。</summary>
        internal ConnectionLifecycle Link => _link;

        /// <summary>指令输入框内容。</summary>
        public string CommandText
        {
            get => _commandText;
            set => SetProperty(ref _commandText, value);
        }

        /// <summary>已发送帧数。</summary>
        public int TxCount
        {
            get => _txCount;
            private set => SetProperty(ref _txCount, value);
        }

        /// <summary>已接收帧数。</summary>
        public int RxCount
        {
            get => _rxCount;
            private set => SetProperty(ref _rxCount, value);
        }

        /// <summary>
        /// 错误指令数:显示的发送中没有得到成功响应的指令 (设备的否定响应,或发出后没有有效响应;重发后成功的不计)。
        /// </summary>
        public int ErrorCount
        {
            get => _errorCount;
            private set
            {
                if (SetProperty(ref _errorCount, value))
                {
                    OnPropertiesChanged(nameof(HasErrors));
                }
            }
        }

        /// <summary>是否有错误指令 (计数醒目显示)。</summary>
        public bool HasErrors => ErrorCount > 0;

        /// <summary>重新扫描串口。</summary>
        public ICommand RefreshPortsCommand { get; }

        /// <summary>按选中的连接方式 (RS232 / TCP) 连接。</summary>
        public ICommand ConnectCommand { get; }

        /// <summary>断开连接。</summary>
        public ICommand DisconnectCommand { get; }

        /// <summary>发送输入框中的指令 (发送后保留内容);勾选重复发送时按间隔重复。</summary>
        public ICommand SendCommand { get; }

        /// <summary>发送快捷指令,命令参数为指令文本。</summary>
        public ICommand SendQuickCommand { get; }

        /// <summary>清空数据显示区。</summary>
        public ICommand ClearLogCommand { get; }

        /// <summary>
        /// 切换中英文:确认后把另一种语言写入配置并请求重启 (<see cref="RestartRequested"/>)。
        /// 界面文本在启动时确定,切换语言需要重新启动程序。配置操作进行中不可用。
        /// </summary>
        public ICommand SwitchLanguageCommand { get; }

        /// <summary>切换语言按钮上显示的目标语言:中文界面显示 <c>EN</c>,英文界面显示 <c>中</c>。</summary>
        public string SwitchLanguageText => Lang.IsChinese ? "EN" : "中";

        /// <summary>切换语言按钮的提示。</summary>
        public string SwitchLanguageToolTip => Lang.T("切换到 English (重启程序)", "Switch to 中文 (restarts)");

        /// <summary>手动发送完整帮助指令 <c>?</c> 时显示的提示 (分组帮助如 <c>?M</c> 很快,不提示)。</summary>
        internal static string HelpCommandHint => Lang.T(
            "完整的 ? 响应约 116 KB,接收约需 10 秒,期间不能发送其他指令,请稍候。只看一组可用 ?M、?F 等分组帮助。",
            "The full ? reply is about 116 KB and takes about 10 s to receive; no other commands can be sent meanwhile. For one group use group help such as ?M or ?F.");

        /// <summary>可以发送指令:已连接,没有配置操作,也没有在重复发送。</summary>
        private bool CanSend => IsConnected && !Config.IsBusy && !SendOptions.IsRepeating;

        /// <summary>
        /// 保存当前通讯配置,在程序退出时调用。
        /// </summary>
        public void SavePreferences()
        {
            var preferences = new ConnectionPreferences
            {
                DisplayFormat = SendOptions.DisplayFormat.ToString(),
                RepeatSend = SendOptions.IsRepeatEnabled,
                RepeatIntervalMs = SendOptions.RepeatInterval,
                EditSource = Config.EditSource.ToString(),
                Language = _languageSetting, // 界面语言在启动时由 App 读取;这里原样保留
                LastCommand = _lastSentCommand,
            };
            Connection.WriteTo(preferences);

            var saved = _preferences.Save(preferences);
            if (!saved.IsSuccess)
            {
                Log.Add(LogKind.Error, saved.Error ?? Lang.T("保存通讯配置失败。", "Saving the settings failed."));
            }
        }

        /// <summary>
        /// 退出或重启前结束连接:等待正在进行的连接完成,取消并等待正在进行的配置操作 (期间不弹窗),
        /// 停止重复发送,断开并写完、关闭通讯数据文件。没有连接时直接返回。不抛出通讯异常。
        /// </summary>
        public async Task CloseConnectionAsync()
        {
            // 连接中途关闭:等它结束 (TCP 最多 5 秒,再加握手),否则连接成功后打开的通讯数据文件不会被关闭。异常已由命令处理。
            await Task.WhenAny(_link.WaitForConnectAsync());
            if (IsDisconnected)
            {
                return;
            }

            if (IsConnected)
            {
                await Config.CancelAndWaitAsync();
            }

            await DisconnectAsync();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _device.FrameSent -= OnFrameSent;
            _device.FrameReceived -= OnFrameReceived;
            _device.ConnectionLost -= OnConnectionLost;
            _device.Retrying -= OnRetrying;
            _device.CommandsFailed -= OnCommandsFailed;
            _link.StateChanged -= OnLinkStateChanged;
            SendOptions.Stop();
            _link.CancelReconnect();
            _link.CloseRecorder(); // 正常退出时窗口关闭前已断开并关闭文件;这里是注销、关机等情况的兜底
        }

        private void ApplyPreferences()
        {
            var loaded = _preferences.Load();
            if (!loaded.IsSuccess)
            {
                // 配置文件损坏时使用默认值,下次退出时会覆盖。
                Log.Add(LogKind.Error, loaded.Error ?? Lang.T("读取通讯配置失败。", "Reading the settings failed."));
                return;
            }

            var saved = loaded.Value;
            Connection.Apply(saved);

            // 只接受已定义的名称 (不接受数字等),否则保持默认 (实机数据 / ASCII)。
            if (ConnectionSettingsViewModel.TryParseName(saved.EditSource, out EditSource source))
            {
                Config.EditSource = source;
            }

            if (ConnectionSettingsViewModel.TryParseName(saved.DisplayFormat, out DisplayFormat format))
            {
                SendOptions.DisplayFormat = format;
            }

            SendOptions.IsRepeatEnabled = saved.RepeatSend;
            SendOptions.RepeatInterval = saved.RepeatIntervalMs; // 不在选项中的值被忽略
            _languageSetting = string.IsNullOrWhiteSpace(saved.Language) ? ConnectionPreferences.DefaultLanguage : saved.Language;

            // JSON 中为 null 时 DataContract 会写入 null。
            _lastSentCommand = saved.LastCommand ?? string.Empty;
            CommandText = _lastSentCommand;
        }

        private async Task SwitchLanguageAsync()
        {
            var target = Lang.IsChinese ? "en" : "zh";
            var message = Lang.T("切换到 English 需要重新启动程序。", "Switching to 中文 restarts the program.");
            if (IsConnected)
            {
                message += Environment.NewLine + Lang.T("当前连接会断开,重启后需要重新连接。", "The current connection is closed; connect again after the restart.");
            }

            if (!_dialogs.Confirm(Lang.T("切换语言", "Switch language"), message + Environment.NewLine + Lang.T("现在重启吗?", "Restart now?")))
            {
                return;
            }

            _languageSetting = target;

            // 先断开并写完通讯数据文件:重启由 Application.Shutdown 完成,不会经过窗口的 Closing。
            await CloseConnectionAsync();
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }

        private Task DisconnectAsync()
        {
            // 正在进行的一次收发会先完成 (发送锁),之后重复发送不再继续。
            SendOptions.Stop();
            return _link.DisconnectAsync();
        }

        private void OnLinkStateChanged(object? sender, EventArgs e)
        {
            if (!IsConnected)
            {
                SendOptions.Stop();
            }

            OnPropertiesChanged(nameof(ConnectionState), nameof(IsConnected), nameof(IsDisconnected), nameof(IsConnecting), nameof(HasConnection), nameof(StatusText));
            RefreshCommands();
        }

        /// <summary>
        /// 发送输入框中的指令;勾选了重复发送时按间隔重复,直到停止、取消勾选或断开。
        /// 发送后保留输入框内容,便于修改后再次发送;重复发送使用点击时的内容,之后的修改不影响本次重复。
        /// 发送的内容退出时保存,下次启动时填入输入框。
        /// </summary>
        private Task SendInputAsync()
        {
            // 命令部分转为大写 (数据部分不变),输入框同时显示转换后的内容,即实际发送的指令。
            var command = ManualCommandPolicy.ToUpperCommands(CommandText);
            CommandText = command;
            _lastSentCommand = command;
            return SendOptions.RunAsync(() => SendAsync(command), () => IsConnected);
        }

        /// <summary>
        /// 发送一条或多条 (用 <c>;</c> 分隔) 指令。多条指令合并为一帧发送,收到响应后逐条显示解析结果。
        /// </summary>
        private async Task SendAsync(string command)
        {
            var segments = ManualCommandPolicy.Split(command);
            if (segments.Count == 0)
            {
                return;
            }

            var text = string.Join(";", segments);

            if (ManualCommandPolicy.ContainsFullHelpCommand(text))
            {
                Log.Add(LogKind.Info, HelpCommandHint);
            }

            var result = await _device.SendAsync(text, ManualCommandPolicy.MinimumResponseTimeout(text), CancellationToken.None);
            if (!result.IsSuccess)
            {
                Log.Add(LogKind.Error, $"{text}: {result.Error}");
                return;
            }

            var responses = result.Value.Responses;
            Log.AddRange(ResponseLogFormatter.Format(segments, responses, DateTime.Now));

            // 关闭握手时只做原样的收发 (调试),不额外查询。
            if (Connection.IsHandshakeEnabled && responses.Any(r => r.ErrorCode == ZevisionErrorCode.StateError))
            {
                await ShowDeviceStateAsync();
            }

            // 设备接受了修改帧格式的指令 (如 UG1304:1):从下一帧起只认新格式。
            var endpoint = _link.Options?.Endpoint;
            var changed = endpoint == null ? null : FrameSettings.AfterUpdates(endpoint, Connection.Frame, segments, responses);
            if (changed != null)
            {
                await _link.OnFrameSettingsChangedAsync(changed);
            }
        }

        /// <summary>
        /// 设备回答 F (状态不允许):读出当前状态 (C09) 一并显示。不自动切换设备状态 (如中止工艺),由操作者决定。
        /// </summary>
        private async Task ShowDeviceStateAsync()
        {
            var state = await _config.QueryAsync(new[] { InstrumentSnapshot.StateId }, CancellationToken.None);
            if (state.IsSuccess && state.Value.FirstOrDefault()?.Value is string value)
            {
                Log.Add(LogKind.Info, Lang.T("设备当前状态 (C09): ", "Current device state (C09): ") + value);
            }
        }

        private void ClearLog()
        {
            Log.Clear();
            TxCount = 0;
            RxCount = 0;
            ErrorCount = 0;
        }

        private void OnFrameSent(object? sender, FrameSentEventArgs e)
        {
            // 格式化在后台线程完成,UI 线程只负责添加记录。
            var entries = FrameLogFormatter.FromSent(e.Raw, DateTime.Now, SendOptions.DisplayFormat);
            Post(() =>
            {
                Log.AddRange(entries);
                TxCount++;
            });
        }

        private void OnFrameReceived(object? sender, FrameReceivedEventArgs e)
        {
            var entries = FrameLogFormatter.FromReceived(e.Frame, e.IsSolicited, DateTime.Now, SendOptions.DisplayFormat);
            Post(() =>
            {
                RxCount++;
                Log.AddRange(entries);
            });
        }

        private void OnConnectionLost(object? sender, ConnectionLostEventArgs e) => Post(() => _link.OnConnectionLost(e));

        private void OnCommandsFailed(object? sender, CommandsFailedEventArgs e)
        {
            var count = e.Count;
            Post(() => ErrorCount += count);
        }

        private void OnRetrying(object? sender, RetryEventArgs e)
        {
            var text = CommunicationText.DescribeRetry(e);
            Post(() => Log.Add(LogKind.Info, text));
        }

        private void Post(Action action) => _uiContext.Post(_ => action(), null);
    }
}
