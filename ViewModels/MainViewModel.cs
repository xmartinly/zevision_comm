using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Zevision.Communication;
using Zevision.Localization;
using Zevision.Models;
using Zevision.Services;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 主窗口视图模型:连接/断开、发送指令、显示收发的帧、保存和恢复界面设置。
    /// 通讯配置见 <see cref="Connection"/>,仪器配置的保存/恢复/修改见 <see cref="Config"/>,
    /// 数据显示区见 <see cref="Log"/>,显示格式和重复发送见 <see cref="SendOptions"/>。
    /// </summary>
    public sealed class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly IDeviceService _device;
        private readonly IPreferencesStore _preferences;
        private readonly IErrorLog _errorLog;
        private readonly IDialogService _dialogs;
        private readonly SynchronizationContext _uiContext;
        private CommunicationLogWriter? _recorder;
        private Task? _connecting;
        private string _languageSetting = ConnectionPreferences.DefaultLanguage;
        private string _connectedTo = string.Empty;
        private bool _isConnected;
        private string _commandText = string.Empty;
        private int _txCount;
        private int _rxCount;

        /// <summary>
        /// 创建视图模型,扫描可用串口并恢复上次保存的通讯配置。必须在 UI 线程上创建,以便捕获其同步上下文。
        /// </summary>
        public MainViewModel(IDeviceService device, IPreferencesStore preferences, IInstrumentConfigService config, IDialogService dialogs, IErrorLog errorLog)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
            _errorLog = errorLog ?? throw new ArgumentNullException(nameof(errorLog));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

            Log = new LogViewModel(errorLog);
            SendOptions = new SendOptionsViewModel();

            // 重复发送期间不开始配置操作:两者的指令会交错。
            Config = new ConfigOperationsViewModel(config, dialogs, errorLog, Log, () => IsConnected && !SendOptions.IsRepeating);

            _device.FrameSent += OnFrameSent;
            _device.FrameReceived += OnFrameReceived;
            _device.ConnectionLost += OnConnectionLost;

            RefreshPortsCommand = new RelayCommand(_ => Connection.RefreshPorts(), _ => !IsConnected);
            ConnectCommand = new AsyncRelayCommand(_ => TrackConnectAsync(), Log.ReportException, _ => !IsConnected && Connection.CreateOptions().IsSuccess);
            DisconnectCommand = new AsyncRelayCommand(_ => DisconnectAsync(), Log.ReportException, _ => IsConnected && !Config.IsBusy);
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

        /// <summary>是否已连接。</summary>
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    OnPropertiesChanged(nameof(IsDisconnected), nameof(StatusText));
                    RefreshCommands();
                }
            }
        }

        /// <summary>是否未连接。用于在连接期间锁定配置控件。</summary>
        public bool IsDisconnected => !IsConnected;

        /// <summary>是否已连接或正在连接 (关闭窗口前需要 <see cref="CloseConnectionAsync"/>)。</summary>
        public bool HasConnection => IsConnected || _connecting != null;

        /// <summary>状态栏文本。</summary>
        public string StatusText => IsConnected ? Lang.T("已连接 ", "Connected to ") + _connectedTo : Lang.T("未连接", "Not connected");

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
        public string SwitchLanguageToolTip => Lang.T("切换到 English (需要重新启动程序)", "Switch to 中文 (restarts the program)");

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
            var connecting = _connecting;
            if (connecting != null)
            {
                // 连接中途关闭:等它结束 (TCP 最多 5 秒),否则连接成功后打开的通讯数据文件不会被关闭。异常已由命令处理。
                await Task.WhenAny(connecting);
            }

            if (!IsConnected)
            {
                return;
            }

            await Config.CancelAndWaitAsync();
            await DisconnectAsync();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _device.FrameSent -= OnFrameSent;
            _device.FrameReceived -= OnFrameReceived;
            _device.ConnectionLost -= OnConnectionLost;
            SendOptions.Stop();
            _ = CloseRecorderAsync(); // 正常退出时窗口关闭前已断开并关闭文件;这里只是兜底
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

        private async Task TrackConnectAsync()
        {
            var connecting = ConnectAsync();
            _connecting = connecting;
            try
            {
                await connecting;
            }
            finally
            {
                _connecting = null;
            }
        }

        private async Task ConnectAsync()
        {
            var created = Connection.CreateOptions();
            if (!created.IsSuccess)
            {
                Log.Add(LogKind.Error, created.Error!); // 失败的 Result 总有原因
                return;
            }

            var options = created.Value;
            CommunicationLogWriter? recorder = null;
            if (Connection.IsSaveCommunicationDataEnabled)
            {
                var path = Path.Combine(AppFolders.GetLogsFolder(), $"capture_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.txt");
                try
                {
                    recorder = CommunicationLogWriter.Create(path, _errorLog);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                {
                    Log.Add(LogKind.Error, Lang.T($"无法创建通讯数据文件 {path}: {ex.Message}", $"Cannot create the communication data file {path}: {ex.Message}"));
                    return;
                }
            }

            var result = await _device.ConnectAsync(options, CancellationToken.None);
            if (!result.IsSuccess)
            {
                if (recorder != null)
                {
                    await recorder.CloseAsync();
                }

                Log.Add(LogKind.Error, result.Error ?? Lang.T("连接失败。", "Connection failed."));
                return;
            }

            // 从"已连接"这一行开始,显示的内容同时追加到文件。
            _recorder = recorder;
            Log.Recorder = recorder;

            _connectedTo = options.Endpoint.ToString();
            IsConnected = true;
            Log.Add(LogKind.Info, Connection.DescribeConnected(options));
            if (recorder != null)
            {
                Log.Add(LogKind.Info, Lang.T("通讯数据保存到: ", "Communication data is saved to: ") + recorder.Path);
            }
        }

        private async Task DisconnectAsync()
        {
            // 正在进行的一次收发会先完成 (发送锁),之后重复发送不再继续。
            SendOptions.Stop();
            await _device.DisconnectAsync();
            if (IsConnected)
            {
                IsConnected = false;
                Log.Add(LogKind.Info, Lang.T("已断开。", "Disconnected."));
            }

            await CloseRecorderAsync();
        }

        /// <summary>停止保存通讯数据:之后的显示不再写入,已排队的写完后关闭文件。</summary>
        private Task CloseRecorderAsync()
        {
            var recorder = _recorder;
            _recorder = null;
            Log.Recorder = null;
            return recorder?.CloseAsync() ?? Task.CompletedTask;
        }

        /// <summary>
        /// 发送输入框中的指令;勾选了重复发送时按间隔重复,直到停止、取消勾选或断开。
        /// 发送后保留输入框内容,便于修改后再次发送;重复发送使用点击时的内容,之后的修改不影响本次重复。
        /// </summary>
        private Task SendInputAsync()
        {
            // 命令部分转为大写 (数据部分不变),输入框同时显示转换后的内容,即实际发送的指令。
            var command = ManualCommandPolicy.ToUpperCommands(CommandText);
            CommandText = command;
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

            // 帮助指令的响应大、首字节前和分包之间有停顿,与保存配置读取 ? 时一样使用较长的最低超时。
            var minimumTimeout = TimeSpan.Zero;
            if (ManualCommandPolicy.ContainsHelpCommand(text))
            {
                minimumTimeout = InstrumentConfigReader.SlowReplyTimeout;
                if (ManualCommandPolicy.ContainsFullHelpCommand(text))
                {
                    Log.Add(LogKind.Info, ManualCommandPolicy.HelpCommandHint);
                }
            }

            var result = await _device.SendAsync(text, minimumTimeout, CancellationToken.None);
            if (!result.IsSuccess)
            {
                Log.Add(LogKind.Error, $"{text}: {result.Error}");
                return;
            }

            Log.AddRange(ResponseLogFormatter.Format(segments, result.Value.Responses, DateTime.Now));
        }

        private void ClearLog()
        {
            Log.Clear();
            TxCount = 0;
            RxCount = 0;
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

        private void OnConnectionLost(object? sender, ConnectionLostEventArgs e)
        {
            var message = e.Exception.Message;
            Post(() =>
            {
                IsConnected = false;
                SendOptions.Stop();
                Log.Add(LogKind.Error, Lang.T("连接中断: ", "Connection lost: ") + message);
                // 不需要等待:CloseAsync 不会失败,文件在已排队的内容写完后关闭。
                _ = CloseRecorderAsync();
            });
        }

        private void Post(Action action) => _uiContext.Post(_ => action(), null);
    }
}
