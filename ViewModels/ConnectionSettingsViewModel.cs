using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Localization;
using Zevision.Models;
using Zevision.Services;

namespace Zevision.ViewModels
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
    public sealed class ConnectionSettingsViewModel : ViewModelBase
    {
        private ConnectionType _connectionType = ConnectionType.Serial;
        private string _tcpHost = string.Empty;
        private string? _selectedPort;
        private int _selectedBaudRate = 115200;
        private int _selectedTimeout = 1000;
        private bool _isChecksumEnabled;
        private bool _isLengthEnabled;
        private bool _isSaveCommunicationDataEnabled;

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

        /// <summary>是否在帧中附加校验和 (对应设备 G1304,TCP 为 G1407)。</summary>
        public bool IsChecksumEnabled
        {
            get => _isChecksumEnabled;
            set => SetProperty(ref _isChecksumEnabled, value);
        }

        /// <summary>是否在帧中附加长度字节 (对应设备 G1305,TCP 为 G1408)。</summary>
        public bool IsLengthEnabled
        {
            get => _isLengthEnabled;
            set => SetProperty(ref _isLengthEnabled, value);
        }

        /// <summary>
        /// 是否保存通讯数据:连接期间把数据显示区的内容 (按当时的显示格式) 追加到 logs\capture_*.txt。
        /// 在连接时生效,连接期间不能修改。
        /// </summary>
        public bool IsSaveCommunicationDataEnabled
        {
            get => _isSaveCommunicationDataEnabled;
            set => SetProperty(ref _isSaveCommunicationDataEnabled, value);
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

            return Result<ConnectionOptions>.Ok(new ConnectionOptions(
                endpoint,
                new FrameOptions(IsChecksumEnabled, IsLengthEnabled),
                TimeSpan.FromMilliseconds(SelectedTimeout)));
        }

        /// <summary>连接成功后的说明,如"已连接 COM3 …;校验和 (G1304) 开,长度字节 (G1305) 关,超时 1000 ms。"。</summary>
        public string DescribeConnected(ConnectionOptions options)
        {
            var on = Lang.T("开", "on");
            var off = Lang.T("关", "off");
            return Lang.T(
                $"已连接 {options.Endpoint};{ChecksumLabel} {(IsChecksumEnabled ? on : off)},{LengthLabel} {(IsLengthEnabled ? on : off)},超时 {SelectedTimeout} ms。",
                $"Connected to {options.Endpoint}; {ChecksumLabel} {(IsChecksumEnabled ? on : off)}, {LengthLabel} {(IsLengthEnabled ? on : off)}, timeout {SelectedTimeout} ms.");
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
            IsChecksumEnabled = saved.ChecksumEnabled;
            IsLengthEnabled = saved.LengthEnabled;
            IsSaveCommunicationDataEnabled = saved.SaveCommunicationData;
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
            preferences.ChecksumEnabled = IsChecksumEnabled;
            preferences.LengthEnabled = IsLengthEnabled;
            preferences.SaveCommunicationData = IsSaveCommunicationDataEnabled;
            preferences.ConnectionType = SelectedConnectionType.ToString();
            preferences.TcpHost = TcpHost.Trim();
        }

        /// <summary>只接受枚举中已定义的名称 (不接受数字)。</summary>
        internal static bool TryParseName<TEnum>(string? text, out TEnum value)
            where TEnum : struct =>
            Enum.TryParse(text, out value) && Enum.IsDefined(typeof(TEnum), value) && !int.TryParse(text, out _);
    }
}
