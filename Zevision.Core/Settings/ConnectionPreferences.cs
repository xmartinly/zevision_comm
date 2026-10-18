using System.Runtime.Serialization;

namespace Zevision.Core.Settings
{
    /// <summary>
    /// 需要在两次运行之间保留的通讯配置,保存为 <c>settings.json</c>。默认值与设备出厂设置一致。
    /// 校验和、长度字节和保存通讯数据按连接方式 (RS232 / TCP) 分别保存:设备上两者是独立的设置 (G1304/G1305 与 G1407/G1408)。
    /// </summary>
    /// <remarks>
    /// JSON 中缺少的字段保持默认值 (反序列化前由 <see cref="OnDeserializing"/> 设置),便于以后增加字段。
    /// </remarks>
    [DataContract]
    public sealed class ConnectionPreferences
    {
        /// <summary>创建默认配置。</summary>
        public ConnectionPreferences()
        {
            SetDefaults();
        }

        /// <summary>上次使用的端口名;为空表示没有记录。</summary>
        [DataMember(Name = "portName", Order = 0)]
        public string PortName { get; set; } = string.Empty;

        /// <summary>波特率。</summary>
        [DataMember(Name = "baudRate", Order = 1)]
        public int BaudRate { get; set; }

        /// <summary>响应空闲超时 (毫秒)。</summary>
        [DataMember(Name = "responseTimeoutMs", Order = 2)]
        public int ResponseTimeoutMs { get; set; }

        /// <summary>RS232 连接是否启用校验和 (设备 G1304)。</summary>
        [DataMember(Name = "checksumEnabled", Order = 3)]
        public bool SerialChecksumEnabled { get; set; }

        /// <summary>RS232 连接是否启用长度字节 (设备 G1305)。</summary>
        [DataMember(Name = "lengthEnabled", Order = 4)]
        public bool SerialLengthEnabled { get; set; }

        /// <summary>RS232 连接是否保存通讯数据 (数据显示区的内容) 到文本文件。JSON 键名沿用开发版本的 rawCaptureEnabled。</summary>
        [DataMember(Name = "rawCaptureEnabled", Order = 5)]
        public bool SerialSaveCommunicationData { get; set; }

        /// <summary>
        /// TCP 连接是否启用校验和 (设备 G1407)。0.9.x 的文件没有这一项 (两种连接共用 RS232 的设置),这时与 RS232 相同。
        /// </summary>
        public bool TcpChecksumEnabled
        {
            get => _tcpChecksumEnabled ?? SerialChecksumEnabled;
            set => _tcpChecksumEnabled = value;
        }

        /// <summary>TCP 连接是否启用长度字节 (设备 G1408)。文件中没有这一项时与 RS232 相同。</summary>
        public bool TcpLengthEnabled
        {
            get => _tcpLengthEnabled ?? SerialLengthEnabled;
            set => _tcpLengthEnabled = value;
        }

        /// <summary>TCP 连接是否保存通讯数据。文件中没有这一项时与 RS232 相同。</summary>
        public bool TcpSaveCommunicationData
        {
            get => _tcpSaveCommunicationData ?? SerialSaveCommunicationData;
            set => _tcpSaveCommunicationData = value;
        }

        /// <summary>修改配置的数据来源 (<c>Device</c> 实机数据 / <c>File</c> 保存的配置)。不认识的值按默认处理。</summary>
        [DataMember(Name = "editSource", Order = 6)]
        public string EditSource { get; set; } = DefaultEditSource;

        /// <summary><see cref="EditSource"/> 的默认值:实机数据。</summary>
        public const string DefaultEditSource = "Device";

        /// <summary>连接方式 (<c>Serial</c> RS232 / <c>Tcp</c>)。不认识的值按 RS232 处理。</summary>
        [DataMember(Name = "connectionType", Order = 7)]
        public string ConnectionType { get; set; } = DefaultConnectionType;

        /// <summary><see cref="ConnectionType"/> 的默认值:RS232。</summary>
        public const string DefaultConnectionType = "Serial";

        /// <summary>TCP 连接的设备 IP 地址或主机名。</summary>
        [DataMember(Name = "tcpHost", Order = 8)]
        public string TcpHost { get; set; } = string.Empty;

        /// <summary>收发数据的显示格式 (<c>Ascii</c> / <c>Hex</c> / <c>Binary</c>)。不认识的值按 ASCII 处理。</summary>
        [DataMember(Name = "displayFormat", Order = 11)]
        public string DisplayFormat { get; set; } = DefaultDisplayFormat;

        /// <summary><see cref="DisplayFormat"/> 的默认值:ASCII。</summary>
        public const string DefaultDisplayFormat = "Ascii";

        /// <summary>是否勾选重复发送。</summary>
        [DataMember(Name = "repeatSend", Order = 12)]
        public bool RepeatSend { get; set; }

        /// <summary>重复发送间隔 (毫秒)。不在选项中的值按默认处理。</summary>
        [DataMember(Name = "repeatIntervalMs", Order = 13)]
        public int RepeatIntervalMs { get; set; }

        /// <summary>界面语言:<c>auto</c> (按系统:中国地区或中文系统用中文,其他用英文)、<c>zh</c> 或 <c>en</c>。</summary>
        [DataMember(Name = "language", Order = 10)]
        public string Language { get; set; } = DefaultLanguage;

        /// <summary><see cref="Language"/> 的默认值:按系统自动选择。</summary>
        public const string DefaultLanguage = "auto";

        // TCP 的三项单独保存;为 null 表示文件中没有 (旧版本),取 RS232 的值。
        [DataMember(Name = "tcpChecksumEnabled", Order = 14, EmitDefaultValue = false)]
        private bool? _tcpChecksumEnabled;

        [DataMember(Name = "tcpLengthEnabled", Order = 15, EmitDefaultValue = false)]
        private bool? _tcpLengthEnabled;

        [DataMember(Name = "tcpSaveCommunicationData", Order = 16, EmitDefaultValue = false)]
        private bool? _tcpSaveCommunicationData;

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        private void SetDefaults()
        {
            PortName = string.Empty;
            BaudRate = 115200;
            ResponseTimeoutMs = 1000;
            EditSource = DefaultEditSource;
            ConnectionType = DefaultConnectionType;
            TcpHost = string.Empty;
            Language = DefaultLanguage;
            DisplayFormat = DefaultDisplayFormat;
            RepeatSend = false;
            RepeatIntervalMs = 1000;
            _tcpChecksumEnabled = null;
            _tcpLengthEnabled = null;
            _tcpSaveCommunicationData = null;
        }
    }
}
