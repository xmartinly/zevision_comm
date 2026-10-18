using Zevision.Core.Localization;

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

        /// <summary>保存配置:读取设备当前配置 (约 10 秒) 并保存到文件</summary>
        public static string SaveConfigToolTip => Lang.T("保存配置:读取设备当前配置 (约 10 秒) 并保存到文件", "Save configuration: read the current device configuration (about 10 s) and save it to a file");

        /// <summary>保存</summary>
        public static string Save => Lang.T("保存", "Save");

        /// <summary>恢复配置:选择配置文件,对比后写入设备</summary>
        public static string RestoreConfigToolTip => Lang.T("恢复配置:选择配置文件,对比后写入设备", "Restore configuration: choose a configuration file, compare it with the device and write the differences");

        /// <summary>恢复</summary>
        public static string Restore => Lang.T("恢复", "Restore");

        /// <summary>修改配置:实机数据 — 读取设备后修改并写入设备;保存的配置 — 选择文件后修改,写入设备并保存文件</summary>
        public static string EditConfigToolTip => Lang.T("修改配置:实机数据 — 读取设备后修改并写入设备;保存的配置 — 选择文件后修改,写入设备并保存文件", "Edit configuration: Device data — read the device, edit and write to the device; Saved configuration — choose a file, edit, write to the device and save the file");

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

        /// <summary>网线连接设备以太网接口 (端口固定 2101,设备 G1406 协议类型须为 Zevision)</summary>
        public static string TcpTabToolTip => Lang.T("网线连接设备以太网接口 (端口固定 2101,设备 G1406 协议类型须为 Zevision)", "Network cable to the device Ethernet port (port 2101; device G1406 protocol type must be Zevision)");

        /// <summary>端口</summary>
        public static string Port => Lang.T("端口", "Port");

        /// <summary>设备所连接的串口。插拔 USB 转串口线后点右侧按钮重新扫描</summary>
        public static string PortToolTip => Lang.T("设备所连接的串口。插拔 USB 转串口线后点右侧按钮重新扫描", "The serial port the device is connected to. After plugging a USB serial adapter in or out, click the button on the right to rescan");

        /// <summary>重新扫描串口</summary>
        public static string RescanPorts => Lang.T("重新扫描串口", "Rescan serial ports");

        /// <summary>波特率</summary>
        public static string BaudRate => Lang.T("波特率", "Baud rate");

        /// <summary>需与设备 RS232 波特率 (G1303) 一致,出厂默认 115200。数据格式固定为 8N1 (8 位数据位、无校验、1 位停止位)</summary>
        public static string BaudRateToolTip => Lang.T("需与设备 RS232 波特率 (G1303) 一致,出厂默认 115200。数据格式固定为 8N1 (8 位数据位、无校验、1 位停止位)", "Must match the device RS232 baud rate (G1303), factory default 115200. The format is fixed at 8N1 (8 data bits, no parity, 1 stop bit)");

        /// <summary>IP 地址</summary>
        public static string IpAddress => Lang.T("IP 地址", "IP address");

        /// <summary>设备的 IP 地址 (设备 G1402),如 192.168.1.88。端口固定为 2101</summary>
        public static string IpAddressToolTip => Lang.T("设备的 IP 地址 (设备 G1402),如 192.168.1.88。端口固定为 2101", "The device IP address (device G1402), e.g. 192.168.1.88. The port is fixed at 2101");

        /// <summary>协议选项</summary>
        public static string ProtocolOptions => Lang.T("协议选项", "Protocol options");

        /// <summary>校验和、长度字节需与设备的设置一致 (RS232: G1304 / G1305,TCP: G1407 / G1408),否则设备无法识别指令</summary>
        public static string ProtocolOptionsToolTip => Lang.T("校验和、长度字节需与设备的设置一致 (RS232: G1304 / G1305,TCP: G1407 / G1408),否则设备无法识别指令", "Checksum and length bytes must match the device settings (RS232: G1304 / G1305, TCP: G1407 / G1408), otherwise the device cannot recognize commands");

        /// <summary>响应超时 (ms)</summary>
        public static string ResponseTimeout => Lang.T("响应超时 (ms)", "Response timeout (ms)");

        /// <summary>空闲超时:发送后或最近一次收到数据后,超过该时间仍没有新数据即判定超时。大响应持续传输时不会超时</summary>
        public static string ResponseTimeoutToolTip => Lang.T("空闲超时:发送后或最近一次收到数据后,超过该时间仍没有新数据即判定超时。大响应持续传输时不会超时", "Idle timeout: a timeout occurs when no new data arrives within this time after sending or after the last received data. Large replies that keep arriving do not time out");

        /// <summary>帧尾附加 1 字节校验和。需与设备的设置一致 (RS232: G1304,TCP: G1407)</summary>
        public static string ChecksumToolTip => Lang.T("帧尾附加 1 字节校验和。需与设备的设置一致 (RS232: G1304,TCP: G1407)", "Appends a 1-byte checksum to each frame. Must match the device setting (RS232: G1304, TCP: G1407)");

        /// <summary>起始符后附加 2 字节数据长度。需与设备的设置一致 (RS232: G1305,TCP: G1408)</summary>
        public static string LengthToolTip => Lang.T("起始符后附加 2 字节数据长度。需与设备的设置一致 (RS232: G1305,TCP: G1408)", "Adds a 2-byte data length after the start character. Must match the device setting (RS232: G1305, TCP: G1408)");

        /// <summary>连接时在软件目录的 logs 文件夹下新建 capture_日期_时间.txt,连接期间把数据显示区的内容 (按当时的显示格式) 在每次通讯后追加进去</summary>
        public static string SaveCommunicationDataToolTip => Lang.T("连接时在软件目录的 logs 文件夹下新建 capture_日期_时间.txt,连接期间把数据显示区的内容 (按当时的显示格式) 在每次通讯后追加进去", "On connect, creates capture_date_time.txt in the logs folder of the program directory; while connected, the data display content (in the display format selected at the time) is appended after each exchange");

        /// <summary>保存通讯数据</summary>
        public static string SaveCommunicationData => Lang.T("保存通讯数据", "Save communication data");

        /// <summary>显示格式</summary>
        public static string DisplayFormat => Lang.T("显示格式", "Display format");

        /// <summary>收发数据的显示格式;只影响之后的数据</summary>
        public static string DisplayFormatToolTip => Lang.T("收发数据的显示格式;只影响之后的数据,已显示的不变", "Display format of the data sent and received; applies to new data only");

        /// <summary>重复发送</summary>
        public static string RepeatSend => Lang.T("重复发送", "Repeat send");

        /// <summary>勾选后,点击发送会按间隔重复发送输入框中的指令,直到点击停止、取消勾选或断开</summary>
        public static string RepeatSendToolTip => Lang.T("勾选后,点击发送会按间隔重复发送输入框中的指令,直到点击停止、取消勾选或断开。快捷指令不重复", "When checked, Send repeats the command in the input box at the interval until Stop is clicked, the box is unchecked or the connection closes. Quick commands are not repeated");

        /// <summary>重复间隔 (ms)</summary>
        public static string RepeatInterval => Lang.T("重复间隔 (ms)", "Interval (ms)");

        /// <summary>一次收发完成后等待多久再发送下一次</summary>
        public static string RepeatIntervalToolTip => Lang.T("一次收发完成后等待多久再发送下一次;重复期间修改,下一次即生效", "Wait after each exchange before the next send; a change applies to the next wait");

        /// <summary>停止</summary>
        public static string Stop => Lang.T("停止", "Stop");

        /// <summary>停止重复发送 (正在进行的一次收发会先完成)</summary>
        public static string StopToolTip => Lang.T("停止重复发送 (正在进行的一次收发会先完成)", "Stop repeating (the exchange in progress completes first)");

        /// <summary>清空数据显示</summary>
        public static string ClearLogToolTip => Lang.T("清空数据显示", "Clear the data display");

        /// <summary>清空</summary>
        public static string Clear => Lang.T("清空", "Clear");

        /// <summary>数据显示</summary>
        public static string DataDisplay => Lang.T("数据显示", "Data display");

        /// <summary>Ctrl+A 全选,Ctrl+C 复制选中行</summary>
        public static string LogListToolTip => Lang.T("Ctrl+A 全选,Ctrl+C 复制选中行", "Ctrl+A selects all, Ctrl+C copies the selected lines");

        /// <summary>快捷指令</summary>
        public static string QuickCommands => Lang.T("快捷指令", "Quick commands");

        /// <summary>输入指令,如 H01;多条用 ; 分隔或每行一条,合并为一帧发送。Enter 发送,Shift+Enter 换行。起止符 { } 由程序添加</summary>
        public static string CommandInputHint => Lang.T("输入指令,如 H01;多条用 ; 分隔或每行一条 (Shift+Enter 换行),合并为一帧发送,如 QG01;QG1304;QG1305。Enter 发送,起止符 { } 由程序添加", "Enter a command, e.g. H01; separate several with ; or put one per line (Shift+Enter) to send them in one frame, e.g. QG01;QG1304;QG1305. Enter sends; the delimiters { } are added automatically");

        /// <summary>发送</summary>
        public static string Send => Lang.T("发送", "Send");

        /// <summary>在设备上按第一个空编号新建对象</summary>
        public static string NewObjectToolTip => Lang.T("在设备上按第一个空编号新建 (使用设备默认值),完成后重新读取本页。有勾选的项时不可用:请先保存或取消勾选", "Creates one on the device with the first free number (device defaults), then reads this page again. Not available while settings are checked: save or uncheck them first");

        /// <summary>新增 (预熔/后处理)</summary>
        public static string AddListEntry => Lang.T("添加", "Add");

        /// <summary>工艺标题中膜层下拉框前的标签</summary>
        public static string LayerLabel => Lang.T("膜层", "Layer");

        /// <summary>工艺膜层行末的删除按钮提示</summary>
        public static string RemoveListEntryToolTip => Lang.T(
            "从设备上删除这个膜层 (需确认,不能撤销):后面的膜层依次前移,再删除最后一项,完成后重新读取。有勾选的项时不可用",
            "Removes this layer on the device (asks first; cannot be undone): the layers after it move up, then the last item is removed and the tab is read again. Not available while settings are checked");

        /// <summary>删除</summary>
        public static string DeleteObject => Lang.T("删除", "Delete");

        /// <summary>从设备上删除这个对象</summary>
        public static string DeleteObjectToolTip => Lang.T("从设备上删除这个对象 (需确认,不能撤销),完成后重新读取。有勾选的项时不可用:请先保存或取消勾选", "Deletes this object from the device (asks first; cannot be undone), then reads again. Not available while settings are checked: save or uncheck them first");

        /// <summary>勾选后可修改,保存时写入设备</summary>
        public static string CheckToEditToolTip => Lang.T("勾选 (或直接点击值) 后可修改,保存时写入设备", "Check (or click the value) to edit; written to the device on Save");

        /// <summary>关闭</summary>
        public static string Close => Lang.T("关闭", "Close");

        /// <summary>取消写入</summary>
        public static string CancelWriting => Lang.T("取消写入", "Cancel writing");

        /// <summary>把勾选的项写入设备并读回校验,全部成功后保存到 JSON 文件</summary>
        public static string SaveEditsToolTip => Lang.T("把勾选的项写入设备并读回校验 (Ctrl+S);来自配置文件时全部成功后同时保存文件", "Writes the checked settings to the device and verifies them (Ctrl+S); when editing a configuration file it is saved too if all succeed");

        /// <summary>信息 (只读项)</summary>
        public static string ReadOnlyTab => Lang.T("信息", "Information");

        /// <summary>重新从设备读取 (Q 指令)</summary>
        public static string RefreshToolTip => Lang.T("重新从设备读取 (Q 指令)", "Read again from the device (Q commands)");

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

        /// <summary>勾选本组所有 (可见的) 项</summary>
        public static string CheckAllToolTip => Lang.T("勾选本组所有 (可见的) 项", "Check all (visible) settings of this group");

        /// <summary>清除</summary>
        public static string ClearChecks => Lang.T("清除", "None");

        /// <summary>取消勾选本组所有项,值还原为文件中的值</summary>
        public static string ClearChecksToolTip => Lang.T("取消勾选本组所有项,值还原为文件中的值", "Uncheck all settings of this group and restore their values");
    }
}
