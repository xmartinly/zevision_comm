using Zevision.Core.Localization;
using Zevision.ViewModels.Display;

namespace Zevision.Views
{
    /// <summary>
    /// 窗口 XAML 中的固定文本 (中英文),XAML 用 <c>{x:Static t:UiText.Xxx}</c> 引用。语言在启动时确定,之后不变。
    /// </summary>
    public static class UiText
    {
        /// <summary>IMC300 通讯测试</summary>
        public static string HeaderSubtitle => Lang.T("IMC300 通讯测试", "IMC300 Comm Test");

        /// <summary>连接</summary>
        public static string Connect => Lang.T("连接", "Connect");

        /// <summary>断开</summary>
        public static string Disconnect => Lang.T("断开", "Disconnect");

        /// <summary>仪器配置</summary>
        public static string InstrumentConfig => Lang.T("仪器配置", "Instrument configuration");

        /// <summary>读取设备配置并保存到文件</summary>
        public static string SaveConfigToolTip => Lang.T("读取设备配置并保存到文件", "Read the device configuration and save it to a file");

        /// <summary>保存</summary>
        public static string Save => Lang.T("保存", "Save");

        /// <summary>把配置文件写入设备</summary>
        public static string RestoreConfigToolTip => Lang.T("把配置文件写入设备", "Write a configuration file to the device");

        /// <summary>恢复</summary>
        public static string Restore => Lang.T("恢复", "Restore");

        /// <summary>修改仪器配置</summary>
        public static string EditConfigToolTip => Lang.T("修改仪器配置", "Edit the instrument configuration");

        /// <summary>修改</summary>
        public static string Edit => Lang.T("修改", "Edit");

        /// <summary>修改的数据来源</summary>
        public static string EditSourceToolTip => Lang.T("修改的数据来源", "Data source to edit");

        /// <summary>取消</summary>
        public static string Cancel => Lang.T("取消", "Cancel");

        /// <summary>连接设置</summary>
        public static string ConnectionSettings => Lang.T("连接设置", "Connection");

        /// <summary>串口线连接</summary>
        public static string SerialTabToolTip => Lang.T("串口线连接", "Serial cable");

        /// <summary>网线连接,端口 2101</summary>
        public static string TcpTabToolTip => Lang.T("网线连接,端口 2101", "Ethernet, port 2101");

        /// <summary>端口</summary>
        public static string Port => Lang.T("端口", "Port");

        /// <summary>设备连接的串口</summary>
        public static string PortToolTip => Lang.T("设备连接的串口", "Serial port of the device");

        /// <summary>重新扫描串口</summary>
        public static string RescanPorts => Lang.T("重新扫描串口", "Rescan serial ports");

        /// <summary>波特率</summary>
        public static string BaudRate => Lang.T("波特率", "Baud rate");

        /// <summary>与设备 G1303 一致,格式 8N1</summary>
        public static string BaudRateToolTip => Lang.T("与设备 G1303 一致,格式 8N1", "Must match device G1303; format 8N1");

        /// <summary>IP 地址</summary>
        public static string IpAddress => Lang.T("IP 地址", "IP address");

        /// <summary>设备 IP 地址 (G1402)</summary>
        public static string IpAddressToolTip => Lang.T("设备 IP 地址 (G1402)", "Device IP address (G1402)");

        /// <summary>协议选项</summary>
        public static string ProtocolOptions => Lang.T("协议选项", "Protocol options");

        /// <summary>需与设备设置一致;握手时自动调整</summary>
        public static string ProtocolOptionsToolTip => Lang.T("需与设备设置一致;握手时自动调整", "Must match the device; the handshake adjusts them");

        /// <summary>连接时握手</summary>
        public static string Handshake => Lang.T("连接时握手", "Handshake on connect");

        /// <summary>连接后确认设备并采用其帧格式,启用重发和心跳</summary>
        public static string HandshakeToolTip => Lang.T("连接后确认设备并采用其帧格式,启用重发和心跳", "Check the device after connecting and use its frame format; enables resends and heartbeat");

        /// <summary>自动重连</summary>
        public static string AutoReconnect => Lang.T("自动重连", "Reconnect automatically");

        /// <summary>连接中断后自动重连</summary>
        public static string AutoReconnectToolTip => Lang.T("连接中断后自动重连", "Reconnect after the connection is lost");

        /// <summary>响应超时 (ms)</summary>
        public static string ResponseTimeout => Lang.T("响应超时 (ms)", "Response timeout (ms)");

        /// <summary>超过该时间没有收到数据即超时</summary>
        public static string ResponseTimeoutToolTip => Lang.T("超过该时间没有收到数据即超时", "Timeout when no data arrives within this time");

        /// <summary>帧尾校验和 (RS232: G1304,TCP: G1407)</summary>
        public static string ChecksumToolTip => Lang.T("帧尾校验和 (RS232: G1304,TCP: G1407)", "Checksum byte (RS232: G1304, TCP: G1407)");

        /// <summary>帧头长度字节 (RS232: G1305,TCP: G1408)</summary>
        public static string LengthToolTip => Lang.T("帧头长度字节 (RS232: G1305,TCP: G1408)", "Length bytes (RS232: G1305, TCP: G1408)");

        /// <summary>把显示的数据保存到 logs\capture_*.txt</summary>
        public static string SaveCommunicationDataToolTip => Lang.T("把显示的数据保存到 logs\\capture_*.txt", "Save the displayed data to logs\\capture_*.txt");

        /// <summary>保存通讯数据</summary>
        public static string SaveCommunicationData => Lang.T("保存通讯数据", "Save communication data");

        /// <summary>显示格式</summary>
        public static string DisplayFormat => Lang.T("显示格式", "Display format");

        /// <summary>收发数据的显示格式</summary>
        public static string DisplayFormatToolTip => Lang.T("收发数据的显示格式", "Display format of the data");

        /// <summary>重复发送</summary>
        public static string RepeatSend => Lang.T("重复发送", "Repeat send");

        /// <summary>按间隔重复发送输入框中的指令</summary>
        public static string RepeatSendToolTip => Lang.T("按间隔重复发送输入框中的指令", "Repeat the command in the input box at the interval");

        /// <summary>重复间隔 (ms)</summary>
        public static string RepeatInterval => Lang.T("重复间隔 (ms)", "Interval (ms)");

        /// <summary>两次发送之间的等待</summary>
        public static string RepeatIntervalToolTip => Lang.T("两次发送之间的等待", "Wait between sends");

        /// <summary>停止</summary>
        public static string Stop => Lang.T("停止", "Stop");

        /// <summary>停止重复发送</summary>
        public static string StopToolTip => Lang.T("停止重复发送", "Stop repeating");

        /// <summary>没有得到成功响应的指令数</summary>
        public static string ErrorCountToolTip => Lang.T("没有得到成功响应的指令数", "Commands without a successful response");

        /// <summary>清空数据显示</summary>
        public static string ClearLogToolTip => Lang.T("清空数据显示", "Clear the data display");

        /// <summary>清空</summary>
        public static string Clear => Lang.T("清空", "Clear");

        /// <summary>数据显示</summary>
        public static string DataDisplay => Lang.T("数据显示", "Data display");

        /// <summary>Ctrl+C 复制选中行</summary>
        public static string LogListToolTip => Lang.T("Ctrl+C 复制选中行", "Ctrl+C copies the selected lines");

        /// <summary>快捷指令</summary>
        public static string QuickCommands => Lang.T("快捷指令", "Quick commands");

        /// <summary>输入指令,如 H01;多条用 ; 分隔或每行一条,合并为一帧发送。Enter 发送,Shift+Enter 换行。起止符 { } 由程序添加</summary>
        public static string CommandInputHint => Lang.T("输入指令,如 H01;多条用 ; 分隔或每行一条 (Shift+Enter 换行),合并为一帧发送,如 QG01;QG1304;QG1305。Enter 发送,起止符 { } 由程序添加", "Enter a command, e.g. H01; separate several with ; or put one per line (Shift+Enter) to send them in one frame, e.g. QG01;QG1304;QG1305. Enter sends; the delimiters { } are added automatically");

        /// <summary>发送</summary>
        public static string Send => Lang.T("发送", "Send");

        /// <summary>在设备上新建;有勾选项时不可用</summary>
        public static string NewObjectToolTip => Lang.T("在设备上新建;有勾选项时不可用", "Create on the device; unavailable while settings are checked");

        /// <summary>新增 (预熔/后处理)</summary>
        public static string AddListEntry => Lang.T("添加", "Add");

        /// <summary>工艺标题中膜层下拉框前的标签</summary>
        public static string LayerLabel => Lang.T("膜层", "Layer");

        /// <summary>从设备上删除这个膜层;有勾选项时不可用</summary>
        public static string RemoveListEntryToolTip => Lang.T("从设备上删除这个膜层;有勾选项时不可用", "Remove this layer from the device; unavailable while settings are checked");

        /// <summary>删除</summary>
        public static string DeleteObject => Lang.T("删除", "Delete");

        /// <summary>从设备上删除;有勾选项时不可用</summary>
        public static string DeleteObjectToolTip => Lang.T("从设备上删除;有勾选项时不可用", "Delete from the device; unavailable while settings are checked");

        /// <summary>勾选后修改</summary>
        public static string CheckToEditToolTip => Lang.T("勾选后修改", "Check to edit");

        /// <summary>关闭</summary>
        public static string Close => Lang.T("关闭", "Close");

        /// <summary>取消写入</summary>
        public static string CancelWriting => Lang.T("取消写入", "Cancel writing");

        /// <summary>写入设备并校验 (Ctrl+S)</summary>
        public static string SaveEditsToolTip => Lang.T("写入设备并校验 (Ctrl+S)", "Write to the device and verify (Ctrl+S)");

        /// <summary>信息 (只读项)</summary>
        public static string ReadOnlyTab => Lang.T("信息", "Information");

        /// <summary>重新读取</summary>
        public static string RefreshToolTip => Lang.T("重新读取", "Read again");

        /// <summary>刷新</summary>
        public static string Refresh => Lang.T("刷新", "Refresh");

        /// <summary>设置 (可写项)</summary>
        public static string WritableTab => Lang.T("设置", "Settings");

        /// <summary>全部展开</summary>
        public static string ExpandAll => Lang.T("全部展开", "Expand all");

        /// <summary>全部收起</summary>
        public static string CollapseAll => Lang.T("全部收起", "Collapse all");

        /// <summary>筛选:ID、名称或值,如 F01、密度、Density</summary>
        public static string FilterHint => Lang.T("筛选 (Ctrl+F):ID、名称或值,如 F01、密度、Density", "Filter (Ctrl+F): ID, name or value, e.g. F01, Density");

        /// <summary>全选</summary>
        public static string CheckAll => Lang.T("全选", "All");

        /// <summary>勾选本组所有项</summary>
        public static string CheckAllToolTip => Lang.T("勾选本组所有项", "Check all in this group");

        /// <summary>清除</summary>
        public static string ClearChecks => Lang.T("清除", "None");

        /// <summary>取消勾选,还原值</summary>
        public static string ClearChecksToolTip => Lang.T("取消勾选,还原值", "Uncheck all and restore the values");
    }
}
