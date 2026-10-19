using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Core;
using Zevision.Core.Localization;
using Zevision.Core.Settings;
using Zevision.ViewModels.Common;

namespace Zevision.ViewModels.Main
{
    /// <summary>连接方式。</summary>
    public enum ConnectionType
    {
        /// <summary>RS232 串口。</summary>
        Serial,

        /// <summary>TCP (以太网接口,默认端口 2101)。</summary>
        Tcp,
    }

    /// <summary>
    /// 主窗口左侧的通讯配置:连接方式 (RS232 / TCP)、串口与波特率、IP 地址、协议选项、响应超时、是否保存通讯数据。
    /// 连接期间由主窗口锁定 (<see cref="MainViewModel.IsDisconnected"/>)。
    /// </summary>
    /// <remarks>
    /// 校验和、长度字节和保存通讯数据按连接方式各有一组 (设备上 RS232 与 TCP 的帧格式是独立的设置),
    /// 开关显示的是选中的连接方式的那一组,切换 tab 时随之切换。
    /// <para>
    /// "连接时握手"开启时 (默认),连接后按设备的实际设置确定帧格式 (校验和、长度字节开关随之更新),
    /// 并启用传输出错重发 (<see cref="HandshakeRetryCount"/>) 和心跳 (<see cref="HandshakeHeartbeatInterval"/>);
    /// 关闭时只打开通道,帧格式按开关,不重发、不发心跳 (与 1.0 版相同),用于调试。
    /// </para>
    /// </remarks>
    public sealed class ConnectionSettingsViewModel : ViewModelBase
    {
        /// <summary>启用握手时,传输出错 (超时、帧无效、设备回答 C/L/T) 后重发的次数。</summary>
        public const int HandshakeRetryCount = 2;

        /// <summary>启用握手时的心跳间隔:连接空闲这么久后发送一次不显示的 H01。</summary>
        public static readonly TimeSpan HandshakeHeartbeatInterval = TimeSpan.FromSeconds(5);

        private ConnectionType _connectionType = ConnectionType.Serial;
        private string _tcpHost = string.Empty;
        private string? _selectedPort;
        private int _selectedBaudRate = 115200;
        private int _selectedTimeout = 1000;
        private bool _isHandshakeEnabled = true;
        private bool _isAutoReconnectEnabled;
        private readonly LinkSwitches _serial = new LinkSwitches();
        private readonly LinkSwitches _tcp = new LinkSwitches();

        /// <summary>
        /// 连接方式 tab 的序号:0 = RS232,1 = TCP (与 <see cref="ConnectionType"/> 的值相同)。
        /// 连接时按选中的 tab 创建串口或 TCP 连接。
        /// </summary>
        public int ConnectionTabIndex
        {
            get => (int)_connectionType;
            set
            {
                if (Enum.IsDefined(typeof(ConnectionType), value))
                {
                    SelectedConnectionType = (ConnectionType)value;
                }
            }
        }

        /// <summary>当前连接方式。切换时校验和/长度字节对应的设备设置项随之改变。</summary>
        public ConnectionType SelectedConnectionType
        {
            get => _connectionType;
            set
            {
                if (SetProperty(ref _connectionType, value))
                {
                    OnPropertiesChanged(nameof(ConnectionTabIndex), nameof(IsSerialConnection), nameof(IsTcpConnection), nameof(ChecksumLabel), nameof(LengthLabel), nameof(QuickCommands));
                    OnSwitchesChanged();
                    RefreshCommands();
                }
            }
        }

        /// <summary>是否使用串口。</summary>
        public bool IsSerialConnection => _connectionType == ConnectionType.Serial;

        /// <summary>是否使用 TCP。</summary>
        public bool IsTcpConnection => _connectionType == ConnectionType.Tcp;

        /// <summary>设备 IP 地址或主机名 (对应设备 G1402)。</summary>
        public string TcpHost
        {
            get => _tcpHost;
            set
            {
                if (SetProperty(ref _tcpHost, value ?? string.Empty))
                {
                    RefreshCommands();
                }
            }
        }

        /// <summary>
        /// TCP 端口。设备固定为 2101,界面不提供设置;只有测试会改它 (连接本机回环地址上的随机端口)。
        /// </summary>
        internal int TcpPort { get; set; } = TcpSettings.DefaultPort;

        /// <summary>校验和开关的标签:串口对应 G1304,TCP 对应 G1407。</summary>
        public string ChecksumLabel => IsTcpConnection ? Lang.T("校验和 (G1407)", "Checksum (G1407)") : Lang.T("校验和 (G1304)", "Checksum (G1304)");

        /// <summary>长度字节开关的标签:串口对应 G1305,TCP 对应 G1408。</summary>
        public string LengthLabel => IsTcpConnection ? Lang.T("长度字节 (G1408)", "Length bytes (G1408)") : Lang.T("长度字节 (G1305)", "Length bytes (G1305)");

        /// <summary>常用测试指令,来自协议文档。校验和/长度检查查询当前连接方式对应的设置项。</summary>
        public IReadOnlyList<string> QuickCommands => new[]
        {
            ZevisionCommandSet.Build(CommandActions.None, "H01"),
            ZevisionCommandSet.Build(CommandActions.None, "H02"),
            ZevisionCommandSet.Query("G01"),
            ZevisionCommandSet.Query(IsTcpConnection ? "G1407" : "G1304"),
            ZevisionCommandSet.Query(IsTcpConnection ? "G1408" : "G1305"),
        };

        /// <summary>系统中可用的串口名称。</summary>
        public ObservableCollection<string> AvailablePorts { get; } = new ObservableCollection<string>();

        /// <summary>可选波特率。设备默认 115200 (G1303)。</summary>
        public IReadOnlyList<int> BaudRates { get; } = new[] { 9600, 19200, 38400, 57600, 115200 };

        /// <summary>可选响应超时 (毫秒)。</summary>
        public IReadOnlyList<int> TimeoutOptions { get; } = new[] { 500, 1000, 2000, 5000 };

        /// <summary>当前选中的串口。</summary>
        public string? SelectedPort
        {
            get => _selectedPort;
            set => SetProperty(ref _selectedPort, value);
        }

        /// <summary>当前选中的波特率。</summary>
        public int SelectedBaudRate
        {
            get => _selectedBaudRate;
            set => SetProperty(ref _selectedBaudRate, value);
        }

        /// <summary>当前选中的响应超时 (毫秒)。</summary>
        public int SelectedTimeout
        {
            get => _selectedTimeout;
            set => SetProperty(ref _selectedTimeout, value);
        }

        /// <summary>选中的连接方式是否在帧中附加校验和 (对应设备 G1304,TCP 为 G1407)。</summary>
        public bool IsChecksumEnabled
        {
            get => Switches.Checksum;
            set => SetProperty(ref Switches.Checksum, value);
        }

        /// <summary>选中的连接方式是否在帧中附加长度字节 (对应设备 G1305,TCP 为 G1408)。</summary>
        public bool IsLengthEnabled
        {
            get => Switches.Length;
            set => SetProperty(ref Switches.Length, value);
        }

        /// <summary>
        /// 选中的连接方式是否保存通讯数据:连接期间把数据显示区的内容 (按当时的显示格式) 追加到 logs\capture_*.txt。
        /// 在连接时生效,连接期间不能修改。
        /// </summary>
        public bool IsSaveCommunicationDataEnabled
        {
            get => Switches.SaveCommunicationData;
            set => SetProperty(ref Switches.SaveCommunicationData, value);
        }

        /// <summary>
        /// 连接时是否握手:确认设备应答,按设备的实际设置确定帧格式 (开关随之更新),读取型号;并启用重发和心跳。
        /// 关闭时只打开通道,用于调试帧格式不一致等情况。两种连接方式共用。
        /// </summary>
        public bool IsHandshakeEnabled
        {
            get => _isHandshakeEnabled;
            set => SetProperty(ref _isHandshakeEnabled, value);
        }

        /// <summary>连接意外中断后是否自动重连 (1、2、4 … 秒后,最长 60 秒一次,直到成功或断开)。两种连接方式共用。</summary>
        public bool IsAutoReconnectEnabled
        {
            get => _isAutoReconnectEnabled;
            set => SetProperty(ref _isAutoReconnectEnabled, value);
        }

        /// <summary>选中的连接方式的开关对应的帧格式。</summary>
        public FrameOptions Frame => new FrameOptions(IsChecksumEnabled, IsLengthEnabled);

        /// <summary>选中的连接方式的开关。</summary>
        private LinkSwitches Switches => IsTcpConnection ? _tcp : _serial;

        /// <summary>把选中的连接方式的校验和、长度字节开关改为设备实际使用的帧格式 (握手后)。</summary>
        public void ApplyFrame(FrameOptions frame)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            Switches.Checksum = frame.ChecksumEnabled;
            Switches.Length = frame.LengthEnabled;
            OnSwitchesChanged();
        }

        /// <summary>帧格式的说明,如"校验和 (G1304) 开,长度字节 (G1305) 关"。</summary>
        public string DescribeFrame(FrameOptions frame)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            var on = Lang.T("开", "on");
            var off = Lang.T("关", "off");
            return Lang.T(
                $"{ChecksumLabel} {(frame.ChecksumEnabled ? on : off)},{LengthLabel} {(frame.LengthEnabled ? on : off)}",
                $"{ChecksumLabel} {(frame.ChecksumEnabled ? on : off)}, {LengthLabel} {(frame.LengthEnabled ? on : off)}");
        }

        /// <summary>重新扫描串口;原来选中的串口仍存在时保持选中,否则选第一个。</summary>
        public void RefreshPorts()
        {
            var previous = SelectedPort;
            AvailablePorts.Clear();
            foreach (var port in SerialPort.GetPortNames())
            {
                AvailablePorts.Add(port);
            }

            SelectedPort = previous != null && AvailablePorts.Contains(previous)
                ? previous
                : AvailablePorts.FirstOrDefault();
        }

        /// <summary>
        /// 按当前配置生成连接参数:串口需要已选端口;TCP 需要非空的 IP 地址 (端口固定 2101)。
        /// </summary>
        /// <returns>参数不完整时为失败,原因可直接显示。</returns>
        public Result<ConnectionOptions> CreateOptions()
        {
            ConnectionEndpoint endpoint;
            if (IsSerialConnection)
            {
                if (string.IsNullOrEmpty(SelectedPort))
                {
                    return Result<ConnectionOptions>.Fail(Lang.T("请选择串口。", "Select a serial port."));
                }

                endpoint = new SerialSettings(SelectedPort!, SelectedBaudRate);
            }
            else if (string.IsNullOrWhiteSpace(TcpHost))
            {
                return Result<ConnectionOptions>.Fail(Lang.T("请输入设备 IP 地址。", "Enter the device IP address."));
            }
            else
            {
                endpoint = new TcpSettings(TcpHost, TcpPort);
            }

            // 握手开启时启用重发和心跳;关闭时只打开通道 (与 1.0 版相同)。
            return Result<ConnectionOptions>.Ok(new ConnectionOptions(endpoint, Frame, TimeSpan.FromMilliseconds(SelectedTimeout))
            {
                RetryCount = IsHandshakeEnabled ? HandshakeRetryCount : 0,
                HeartbeatInterval = IsHandshakeEnabled ? HandshakeHeartbeatInterval : TimeSpan.Zero,
            });
        }

        /// <summary>连接成功后的说明,如"已连接 COM3 …;校验和 (G1304) 开,长度字节 (G1305) 关,超时 1000 ms。"。</summary>
        public string DescribeConnected(ConnectionOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return Lang.T(
                $"已连接 {options.Endpoint};{DescribeFrame(Frame)},超时 {SelectedTimeout} ms。",
                $"Connected to {options.Endpoint}; {DescribeFrame(Frame)}, timeout {SelectedTimeout} ms.");
        }

        /// <summary>
        /// 恢复保存的配置。只接受选项中存在的值 (防止手工修改的配置导致界面显示空白);连接方式只接受已定义的名称。
        /// </summary>
        public void Apply(ConnectionPreferences saved)
        {
            if (saved is null)
            {
                throw new ArgumentNullException(nameof(saved));
            }

            if (saved.PortName.Length > 0 && AvailablePorts.Contains(saved.PortName))
            {
                SelectedPort = saved.PortName;
            }

            SelectedBaudRate = BaudRates.Contains(saved.BaudRate) ? saved.BaudRate : SelectedBaudRate;
            SelectedTimeout = TimeoutOptions.Contains(saved.ResponseTimeoutMs) ? saved.ResponseTimeoutMs : SelectedTimeout;
            _serial.Checksum = saved.SerialChecksumEnabled;
            _serial.Length = saved.SerialLengthEnabled;
            _serial.SaveCommunicationData = saved.SerialSaveCommunicationData;
            _tcp.Checksum = saved.TcpChecksumEnabled;
            _tcp.Length = saved.TcpLengthEnabled;
            _tcp.SaveCommunicationData = saved.TcpSaveCommunicationData;
            OnSwitchesChanged();
            IsHandshakeEnabled = saved.HandshakeEnabled;
            IsAutoReconnectEnabled = saved.AutoReconnect;
            if (TryParseName(saved.ConnectionType, out ConnectionType connectionType))
            {
                SelectedConnectionType = connectionType;
            }

            TcpHost = saved.TcpHost;
        }

        /// <summary>把当前配置写入要保存的设置。</summary>
        public void WriteTo(ConnectionPreferences preferences)
        {
            if (preferences is null)
            {
                throw new ArgumentNullException(nameof(preferences));
            }

            preferences.PortName = SelectedPort ?? string.Empty;
            preferences.BaudRate = SelectedBaudRate;
            preferences.ResponseTimeoutMs = SelectedTimeout;
            preferences.SerialChecksumEnabled = _serial.Checksum;
            preferences.SerialLengthEnabled = _serial.Length;
            preferences.SerialSaveCommunicationData = _serial.SaveCommunicationData;
            preferences.TcpChecksumEnabled = _tcp.Checksum;
            preferences.TcpLengthEnabled = _tcp.Length;
            preferences.TcpSaveCommunicationData = _tcp.SaveCommunicationData;
            preferences.HandshakeEnabled = IsHandshakeEnabled;
            preferences.AutoReconnect = IsAutoReconnectEnabled;
            preferences.ConnectionType = SelectedConnectionType.ToString();
            preferences.TcpHost = TcpHost.Trim();
        }

        private void OnSwitchesChanged() =>
            OnPropertiesChanged(nameof(IsChecksumEnabled), nameof(IsLengthEnabled), nameof(IsSaveCommunicationDataEnabled));

        /// <summary>只接受枚举中已定义的名称 (不接受数字)。</summary>
        internal static bool TryParseName<TEnum>(string? text, out TEnum value)
            where TEnum : struct =>
            Enum.TryParse(text, out value) && Enum.IsDefined(typeof(TEnum), value) && !int.TryParse(text, out _);

        /// <summary>一种连接方式的协议选项和数据记录开关。</summary>
        private sealed class LinkSwitches
        {
            public bool Checksum;
            public bool Length;
            public bool SaveCommunicationData;
        }
    }
}
