using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Zevision.Communication;

namespace Zevision.Commands
{
    /// <summary>
    /// Zevision 指令集:每条指令的命令模式、动作、说明、单位和取值范围。用法:
    /// <code>
    /// ZevisionCommandSet.TryGetGroup("M0103", out var group);           // group = CommandGroup.M
    /// var commands = ZevisionCommandSet.GetCommands(group);              // 材料组的全部指令
    /// var density = ZevisionCommandSet.Find("M0103");                    // M**03 密度
    /// density.Build(CommandActions.Update, "2.700", 1);                  // "UM0103:2.700"
    /// ZevisionCommandSet.Update("M0103", "2.700");                        // 已有完整 ID 时:"UM0103:2.700"
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 来源:<c>docs/INFICON_IMC300_Zevision通讯协议说明.md</c> 第 4 章 (动作、说明、范围) 和
    /// <c>docs/INFICON_IMC300_Zevision_U类型指令汇总.xlsx</c> (U 指令的说明、范围,由 <c>ZevisionCommandSetTests</c> 读取 xlsx 核对)。
    /// 与文档不同、按实机 (IMC300 固件 1.8.11) 补充或调整的地方都在对应行注明。
    /// </para>
    /// <para>
    /// <c>**</c> 是一级编号 (两位十六进制):第一级是对象编号,如 <c>M**03</c> 中的材料编号、<c>F**02</c> 中的膜编号;
    /// 之后的是子列表中的序号。逻辑组 (T) 在文档中没有具体指令,只收录实机帮助表中的基本 IO 逻辑 (T0101** / T0102**),选项取自文档的输入/输出分配范围。帮助 <c>?</c> 不属于任何组,不在表中。
    /// </para>
    /// </remarks>
    public static class ZevisionCommandSet
    {
        /// <summary>帮助指令:返回全部设置项及当前值的帮助表 (约 116 KB)。不属于任何命令组。</summary>
        public const string HelpCommand = "?";

        /// <summary>命令 ID 的格式:组字母 + 若干两位十六进制 (如 <c>M0103</c>、<c>P02FE</c>、<c>E</c>)。</summary>
        private static readonly Regex IdFormat = new Regex("^[A-Z](?:[0-9A-F]{2})*$", RegexOptions.CultureInvariant);

        private const CommandActions Q = CommandActions.Query;
        private const CommandActions QU = CommandActions.Query | CommandActions.Update;
        private const CommandActions AD = CommandActions.Add | CommandActions.Delete;
        private const CommandActions S = CommandActions.Status;
        private const CommandActions R = CommandActions.Remote;

        private const string OffOn = "0=Off, 1=On";
        private const string NoYes = "0=No, 1=Yes";
        private const string DisabledEnabled = "0=Disabled, 1=Enabled";
        private const string DisableEnable = "0=Disable, 1=Enable";
        private const string ControlType = "0=Manual, 1=Direct, 2=Individual, 3=BCD";
        private const string DriveType = "0=Up, 1=Down";
        private const string StepType = "0=Single, 1=Double";
        private const string CloseOpen = "0=Close, 1=Open";
        private const string ProtocolType = "0=Zevision, 1=XTC3";

        /// <summary>输出分配功能 (O**03)。</summary>
        private const string OutputFunctions =
            "0=未分配, 1=Source Shutter 1, 2=Source Shutter 2, 3=Sensor Shutter 1, 4=Sensor Shutter 2, 5=Stop, "
            + "6=End of process, 7=Thickness setpoint, 8=Crystal fail, 9=Alarms, 10=Source in use, 11=Final Thickness, "
            + "12=End of Film, 13=In layer, 14=Ion Assisted Deposition, 15=Crystal Switcher 1, 16=Crystal Switcher 2, "
            + "17=Ready, 18=Crucible Switching, 19=Rise 1, 20=Soak 1, 21=Rise 2, 22=Soak 2, 23=Shutter Delay, 24=Deposit, "
            + "25=Rate Ramp, 26=Manual, 27=Time Power, 28=Idle Ramp, 29=Idle, 30=Max Power, 31=Rate Deviation Error, "
            + "32=Crystal Switcher Fail, 33=Crystal Switching, 34=Crucible Switcher Fail, 35=Shutter Delay Error, "
            + "36=Computer Control, 37=Crucible Select Source 1 Binary, 38=Crucible Select Source 1 Binary bit 2, "
            + "39=Crucible Select Source 1 Binary bit 3, 40=Crucible Select Source 1 Binary bit 4, "
            + "41=Crucible Select Source 1 Binary bit 5, 42=Crucible Select Source 1 Binary bit 6, "
            + "43=Crucible Select Source 1 Binary bit 7, 44=Crucible Select Source 1 Binary bit 8, "
            + "45=Crucible Select Source 1 BCD, 46=Crucible Select Source 1 BCD bit 2, 47=Crucible Select Source 1 BCD bit 3, "
            + "48=Crucible Select Source 2 Binary, 49=Crucible Select Source 2 Binary bit 2, "
            + "50=Crucible Select Source 2 Binary bit 3, 51=Crucible Select Source 2 Binary bit 4, "
            + "52=Crucible Select Source 2 Binary bit 5, 53=Crucible Select Source 2 Binary bit 6, "
            + "54=Crucible Select Source 2 Binary bit 7, 55=Crucible Select Source 2 Binary bit 8, "
            + "56=Crucible Select Source 2 BCD, 57=Crucible Select Source 2 BCD bit 2, 58=Crucible Select Source 2 BCD bit 3, "
            + "60=Not implemented";

        /// <summary>输入分配功能 (I**03),按操作手册 I**3 Ranges。12~30 为二进制选择工艺的各个位。</summary>
        private const string InputFunctions =
            "0=未分配, 1=Start, 2=Stop, 3=End Deposit, 4=Rate Watcher Hold Initiate, 5=Rate Watcher Hold Inhibit, "
            + "6=Crystal Fail Inhibit, 7=Zero Thickness, 8=Soak 2 Hold, 9=Crucible 1 Valid, 10=Crucible 2 Valid, 11=Reset, "
            + "12=Select Process 1-4 (2 bits), 13=Select Process 1-4 bit 2, "
            + "14=Select Process 1-16 (4 bits), 15=Select Process 1-16 bit 2, 16=Select Process 1-16 bit 3, 17=Select Process 1-16 bit 4, "
            + "18=Select Process 1-64 (6 bits), 19=Select Process 1-64 bit 2, 20=Select Process 1-64 bit 3, "
            + "21=Select Process 1-64 bit 4, 22=Select Process 1-64 bit 5, 23=Select Process 1-64 bit 6, "
            + "24=Select Process 1-99 (7 bits), 25=Select Process 1-99 bit 2, 26=Select Process 1-99 bit 3, "
            + "27=Select Process 1-99 bit 4, 28=Select Process 1-99 bit 5, 29=Select Process 1-99 bit 6, 30=Select Process 1-99 bit 7, "
            + "31=Switch Crystal, 32=Non-Deposit Hold, 33=Zero Film Time, 34=Start Inhibit, 35=Soak 1 Hold, 37=Not implemented";

        /// <summary>最大功率选项 (F**0907):达到最大功率时的处理。</summary>
        private const string MaxPowerOptions = "0=Continue, 1=Post deposit, 2=End the process, 3=Suspend process";

        private const string StopReasons =
            "0=No stop, 1=Keyboard, 5=Crystal Fail, 6=Max Power, 7=Hand Controller, 8=Communications, 9=Digital Input, "
            + "10=Power Loss, 11=Rate Dev Error, 12=Crystal Switcher fail";

        /// <summary>
        /// 英文说明:IMC300 (固件 1.8.11) <c>?</c> 帮助表中各指令的 DESCRIPTION,由实机抓包提取
        /// (<c>ZevisionCommandSetTests</c> 核对)。帮助表中没有或自动匹配不准的少数项 (按编号创建、列表节点等) 为人工补充。
        /// </summary>
        private static readonly Dictionary<string, string> EnglishNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["H01"] = "String containing the Product Name and Software Version number",
            ["H02"] = "String containing the Software Version number, with each of the values separated by commas",
            ["E"] = "Echo, Echo Received Data to the Output",
            ["G01"] = "Communication API Version",
            ["G02"] = "Device Type",
            ["G03"] = "Device Name",
            ["G04"] = "Vendor",
            ["G05"] = "Model Number",
            ["G06"] = "System Serial Number",
            ["G07"] = "System Hardware Version",
            ["G08"] = "System Software Version",
            ["G09**01"] = "Option Card Type/Name",
            ["G09FE"] = "Number of option cards in the system",
            ["G0A"] = "System Time",
            ["G0B"] = "Time Since Power On",
            ["G0C"] = "Power On Count",
            ["G0D"] = "Reset Command, resets the unit",
            ["G0E01"] = "Turn On/Off Display",
            ["G0E02"] = "Lock GUI Updates",
            ["G0E03"] = "Brightness",
            ["G0E04"] = "Dim Timer",
            ["G0E05"] = "Off Timer",
            ["G0F01"] = "Keystroke Off/On",
            ["G0F02"] = "Notification Off/On",
            ["G11"] = "Lock Command",
            ["G1201"] = "Rate Filter (seconds)",
            ["G1202"] = "Dev Graph Limits (Å/s)",
            ["G1203"] = "Mix Max Power Line",
            ["G1204"] = "Crystal Life Warning",
            ["G1205"] = "Crystal Life Error",
            ["G1301"] = "RS232 Protocol Type",
            ["G1302"] = "RS232 Protocol Version",
            ["G1303"] = "RS232 Baud Rate (bps)",
            ["G1304"] = "RS232 Enable/Disable Checksum",
            ["G1305"] = "RS232 Enable/Disable Length Checking",
            ["G1306"] = "RS232 Enable/Disable File Transfer",
            ["G1401"] = "Ethernet DHCP",
            ["G1402"] = "Ethernet IP Address",
            ["G1403"] = "Ethernet Net Mask",
            ["G1404"] = "Ethernet Gateway",
            ["G1405"] = "Ethernet MAC Address",
            ["G1406"] = "Ethernet Protocol Type",
            ["G1407"] = "Ethernet Enable/Disable Checksum",
            ["G1408"] = "Ethernet Enable/Disable Length Checking",
            ["G140A"] = "Ethernet Link Status",
            ["G16"] = "Trigger Beeper",
            ["G180101"] = "Board Serial Number",
            ["G180102"] = "Board Part Number",
            ["C01"] = "Stop/Start Process",
            ["C02"] = "Stop on Alarm",
            ["C03"] = "Zero Thickness",
            ["C04"] = "Reset Process",
            ["C05"] = "Clear S and Q Counts",
            ["C06"] = "Layer Control",
            ["C07"] = "Manual Power Setpoint",
            ["C08"] = "Run Number",
            ["C09"] = "Current State",
            ["C0A"] = "Active Process",
            ["C0B"] = "Active Layer",
            ["C0C"] = "Active Film",
            ["C0D"] = "Active Sensor",
            ["C0E"] = "Active Power Source",
            ["C0F"] = "Active Power (V)",
            ["C10"] = "Max Power",
            ["C11"] = "Active Power (%)",
            ["C12"] = "Active Rate",
            ["C13"] = "Raw Rate",
            ["C14"] = "Rate Rolling Average",
            ["C15"] = "Current Thickness",
            ["C16"] = "Raw Frequency",
            ["C17"] = "Crystal Life",
            ["C18"] = "Crystal Fail",
            ["C19"] = "Alarm State",
            ["C1A"] = "Current Process Time",
            ["C1B"] = "Last Process Error",
            ["C1C"] = "Enable/Disable Test Mode",
            ["C1D"] = "Test Mode Rate (A/S)",
            ["C1E"] = "Trigger Final Thickness",
            ["C1F"] = "Stop Reason",
            ["C20"] = "Current Stability Total",
            ["C21"] = "Current Quality Count",
            ["M00"] = "Add or Delete material from the end of the list",
            ["M**"] = "Add or delete material with the given number",
            ["M**01"] = "Name",
            ["M**03"] = "Density (g/cm^3)",
            ["M**04"] = "Z Ratio",
            ["M**07"] = "Tooling",
            ["MFE"] = "Number Of Materials",
            ["F00"] = "Add or Delete film from the end of the list",
            ["F**"] = "Add or delete film with the given number",
            ["F**01"] = "Name",
            ["F**02"] = "Material",
            ["F**0300"] = "Add or Delete Pre Condition from the end of the list",
            ["F**03**01"] = "Start Ramp/Rise time",
            ["F**03**02"] = "Ramp Power",
            ["F**03**03"] = "Hold Time",
            ["F**03FE"] = "Number of Pre Conditions",
            ["F**0401"] = "Delay Option",
            ["F**0402"] = "Control Delay Time",
            ["F**0403"] = "Shutter Delay Accuracy",
            ["F**0404"] = "Shutter Delay Timeout",
            ["F**0405"] = "Transfer Sensor",
            ["F**0406"] = "Transfer Tooling",
            ["F**0500"] = "Add or Delete Post Condition from the end of the list",
            ["F**05**01"] = "Ramp/Rise time",
            ["F**05**02"] = "Ramp Power",
            ["F**05**03"] = "Hold Time",
            ["F**05FE"] = "Number of Post Conditions",
            ["F**0601"] = "Rate Watcher Enabled",
            ["F**0602"] = "Rate Watcher Time",
            ["F**0603"] = "Rate Watcher Accuracy",
            ["F**0604"] = "Ion Assisted Deposit",
            ["F**0700"] = "Add or Delete Sensor from the end of the list",
            ["F**07**01"] = "Sensor Number",
            ["F**07**02"] = "Crystal Position",
            ["F**07**03"] = "Tooling",
            ["F**07**04"] = "Crystal Position Last",
            ["F**07**05"] = "Failure Action",
            ["F**07**06"] = "Failure Trigger",
            ["F**07FE"] = "Number of Sensors",
            ["F**0801"] = "Mode",
            ["F**0805"] = "Backup Sensor",
            ["F**0806"] = "Backup Tooling",
            ["F**0807"] = "Quality Percent",
            ["F**0808"] = "Quality Counts",
            ["F**0809"] = "Stability Single",
            ["F**080A"] = "Stability Total",
            ["F**0901"] = "Source Setting",
            ["F**0902"] = "Crucible",
            ["F**0903"] = "P Term",
            ["F**0904"] = "I Term",
            ["F**0905"] = "D Term",
            ["F**0906"] = "Maximum Power",
            ["F**0907"] = "Max Power Option",
            ["F**0908"] = "Min Power",
            ["F**0909"] = "Advanced Mode",
            ["F**0802"] = "Master Tooling",
            ["F**0803"] = "Multipoint",
            ["F**0804"] = "Aggregate Multiply",
            ["F**07**07"] = "Weight",
            ["F**07**08"] = "Cal Thickness",
            ["G1409"] = "Ethernet Enable/Disable File Transfer",
            ["N**0D"] = "Shutter Delay",
            ["P02**03"] = "Source DAC Required",
            ["P02**05"] = "Active Layer Output",
            ["FFE"] = "Number Of Films",
            ["L00"] = "Add or Delete layer from the end of the list",
            ["L**"] = "Add or delete layer with the given number",
            ["L**01"] = "Layer Name",
            ["L**0200"] = "Add or Delete Film from the end of the list",
            ["L**02**01"] = "Film",
            ["L**02**02"] = "Rate",
            ["L**02**03"] = "Final Thickness",
            ["L**02**0600"] = "Add or Delete Rate Ramp from the end of the list",
            ["L**02**06**"] = "Rate Ramp",
            ["L**02**06**01"] = "Start Thickness",
            ["L**02**06**02"] = "Ramp Time",
            ["L**02**06**03"] = "New Rate",
            ["L**02**06FE"] = "Number of Rate Ramps",
            ["L**02FE"] = "Number of Layer Films",
            ["LFE"] = "Number Of Layers",
            ["P01"] = "Set Active Process",
            ["P0200"] = "Add or Delete process from the end of the list",
            ["P02**"] = "Add or delete process with the given number",
            ["P02**01"] = "Name",
            ["P02**02"] = "Active Layer",
            ["P02**04"] = "Auto Start Next",
            ["P02**06"] = "Run Number",
            ["P02**0700"] = "Add or Delete Layer from the end of the list",
            ["P02**07**"] = "Layer",
            ["P02**07FE"] = "Number Of Layers",
            ["P02**08"] = "Deposit/Etch Mode",
            ["P02**09"] = "Display Negative Rates",
            ["P02**0A"] = "Linked Process ID",
            ["P02**0B"] = "Start without Backup XTAL",
            ["P02FE"] = "Number Of Processes",
            ["N**01"] = "Name",
            ["N**02"] = "Sensor Type",
            ["N**03"] = "Control Type",
            ["N**04"] = "Drive Type",
            ["N**05"] = "Step",
            ["N**06"] = "Feedback Type",
            ["N**07"] = "Indexer Delay",
            ["N**08"] = "Number of Positions",
            ["N**09"] = "Current Position",
            ["N**0A"] = "Crystal Tooling",
            ["N**0B**"] = "Crystal Switch Output Relay",
            ["N**0C"] = "Shutter Control",
            ["N**0E"] = "Shutter Relay",
            ["N**0F"] = "Crystal Fail Relay",
            ["N**10**02"] = "Health",
            ["N**10FE"] = "Number Of QCMs",
            ["N**11"] = "Active QCM ID",
            ["N**12"] = "Active QCM Frequency (Hz)",
            ["N**13"] = "Active QCM Rate (Å/s)",
            ["N**14"] = "Active QCM Thickness",
            ["N**15"] = "Active QCM Status",
            ["N**16"] = "Active QCM Life",
            ["N**17"] = "Active QCM Activity",
            ["N**18"] = "Switch Crystal",
            ["N**19"] = "Rotate Carousel",
            ["N**1A**"] = "Crystal Switch Feedback Input",
            ["N**1B"] = "Clear All Crystals",
            ["N**1C"] = "XIU Connection Status",
            ["N**1D"] = "XIU Type",
            ["N**1E"] = "XIU Serial Number",
            ["N**1F"] = "XIU Cable Length (ft)",
            ["N**20"] = "Rate Deviation (Å)",
            ["NFE"] = "Number Of Sensors",
            ["S**01"] = "Name",
            ["S**02"] = "Source Output Configuration",
            ["S**03"] = "Current Voltage",
            ["S**04"] = "Current Percent",
            ["S**05"] = "Shutter Output",
            ["S**06"] = "Open/Close Shutter",
            ["S**07"] = "Number of Crucibles",
            ["S**08"] = "Current Crucible Number",
            ["S**0900"] = "Add or Delete Crucible Material from the end of the list",
            ["S**09**"] = "Crucible Material",
            ["S**09FE"] = "Number of Crucible Materials",
            ["S**0A"] = "Max Output",
            ["S**0B"] = "Control Type",
            ["S**0C**"] = "Crucible Output Relay",
            ["S**0D**"] = "Crucible Feedback Input",
            ["S**0E"] = "Drive Type",
            ["S**0F"] = "Step",
            ["S**10"] = "Feedback Type",
            ["S**11"] = "Indexer Delay",
            ["S**12"] = "Recorder Output",
            ["S**13"] = "Recorder Function",
            ["S**14"] = "Rate 0 Percent Output (Å/s)",
            ["S**15"] = "Rate 100 Percent Output (Å/s)",
            ["S**16"] = "Thickness 0 Percent Output (kÅ)",
            ["S**17"] = "Thickness 100 Percent Output (kÅ)",
            ["S**18"] = "Rate Deviation 0 Percent Output",
            ["S**19"] = "Rate Deviation 100 Percent Output",
            ["S**1A"] = "Power 0 Percent Output (%)",
            ["S**1B"] = "Power 100 Percent Output (%)",
            ["S**1C"] = "Frequency 0 Percent Output (Hz)",
            ["S**1D"] = "Frequency 100 Percent Output (Hz)",
            ["S**1E"] = "Switch Crucible",
            ["T0101**"] = "Basic Event",
            ["T0102**"] = "Basic Action",
            ["SFE"] = "Number Of Sources",
            ["I**01"] = "Name",
            ["I**02"] = "Active High/Low",
            ["I**03"] = "Assigned",
            ["I**04"] = "Active State",
            ["I**05"] = "Raw State",
            ["IFE"] = "Number Of Inputs",
            ["O**01"] = "Name",
            ["O**02"] = "Normal (Inactive) State",
            ["O**03"] = "Assigned",
            ["O**04"] = "Active State",
            ["O**05"] = "Raw State",
            ["OFE"] = "Number Of Outputs",
        };

        /// <summary>说明、单位、选项中出现的中文 → 英文。没有收录的文本中英文相同。</summary>
        private static readonly Dictionary<string, string> Translations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 单位
            ["分钟"] = "min",
            ["秒"] = "s",

            // 选项
            ["未分配"] = "Unassigned",
            ["未选择"] = "None",
            ["允许屏幕更新"] = "Allow screen updates",
            ["禁止"] = "Locked",
            ["静态IP"] = "Static IP",
            ["停止"] = "Stop",
            ["启动/恢复"] = "Start/Resume",
            ["不停止"] = "Do not stop",
            ["链路断开"] = "Down",
            ["链路连通"] = "Up",
            ["常开"] = "Open",
            ["常闭"] = "Closed",

            // 补充说明
            ["文档范围 1~300;实机值为 0"] = "documented range 1~300; device value is 0",
            ["文档范围 25~9999;实机值为 0"] = "documented range 25~9999; device value is 0",
            ["文档范围 1~晶振数;实机值为 0"] = "documented range 1~number of crystals; device value is 0",
            ["0=禁用, 1~晶振数"] = "0=Disabled, 1~number of crystals",
            ["0=禁用"] = "0=disabled",
            ["0=无"] = "0=None",
            ["1~膜层列表中的膜层数"] = "1~number of layers in the list",
            ["工艺编号;文档写 P0201~P02C8"] = "process number; the document says P0201~P02C8",
            ["1~坩埚数量 (S**07)"] = "1~number of crucibles (S**07)",
            ["E:数据 → 原样返回数据"] = "E:data → returns the data unchanged",
            ["实机返回 MM-DD-YYYY HH:MM:SS,文档写 YYYY-MM-DD HH:MM:SS;写入格式未验证"] = "device returns MM-DD-YYYY HH:MM:SS, document says YYYY-MM-DD HH:MM:SS; write format not verified",
            ["添加实机验证:AM01~AM03;按编号删除 (D) 文档未说明"] = "adding verified on the device: AM01~AM03; deleting by number (D) is not documented",
            ["添加实机验证:AF01、AF02;按编号删除 (D) 文档未说明"] = "adding verified on the device: AF01, AF02; deleting by number (D) is not documented",
            ["按材料的规则推断,未经实机验证"] = "inferred from materials, not verified on the device",
            ["文档未列出,实机为 Name"] = "not in the document; Name on the device",
            ["文档未列出,实机帮助表有"] = "not in the document; listed by the device help",
            ["实机说明为 Active Layer"] = "device description: Active Layer",
            ["实机为没有值的列表节点,设置项是其下的起始厚度等"] = "a list node without a value on the device; the settings are its Start Thickness etc.",
        };

        /// <summary>
        /// 选项的中文:表中的选项按文档和设备写英文 (英文原样使用),中文用这里的译文。
        /// "… bit n"、"Select Process a-b" 等系列由 <see cref="ChineseOption"/> 按规则组合。没有收录的选项中英文相同。
        /// 同一个英文词在不同设置中含义不同时 (如继电器的 Open/Closed、链路的 Down/Up),表中直接写中文,英文见 <see cref="Translations"/>。
        /// </summary>
        private static readonly Dictionary<string, string> ChineseOptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Off"] = "关", ["On"] = "开",
            ["No"] = "否", ["Yes"] = "是",
            ["Disabled"] = "禁用", ["Enabled"] = "启用",
            ["Disable"] = "禁用", ["Enable"] = "启用", ["FTP Enable"] = "启用 FTP",
            ["Unlocked"] = "未锁定", ["Locked"] = "已锁定",
            ["Manual"] = "手动", ["Direct"] = "直接", ["Individual"] = "单独",
            ["Up"] = "上", ["Down"] = "下",
            ["Single"] = "单", ["Double"] = "双", ["Dual"] = "双晶振",
            ["Crystal2"] = "2 晶振", ["Crystal6"] = "6 晶振", ["Crystal12"] = "12 晶振", ["Generic"] = "通用",
            ["Close"] = "关闭", ["Open"] = "打开",
            ["Low"] = "低", ["High"] = "高",
            ["Inactive"] = "无效", ["Active"] = "有效",
            ["Open/Low"] = "断开/低", ["Closed/High"] = "闭合/高",
            ["Good"] = "正常", ["Failed"] = "失效", ["Failure"] = "失效",
            ["No Alarm"] = "无报警", ["Alarm"] = "报警",
            ["Disconnected"] = "未连接", ["Connected"] = "已连接",
            ["None"] = "无", ["Shutter"] = "挡板", ["Control"] = "控制", ["Both"] = "两者",
            ["Unused"] = "不使用", ["PostDp"] = "沉积后处理", ["Suspnd"] = "暂停", ["TimePw"] = "时间功率",
            ["On fail"] = "失效时", ["On last"] = "最后一个失效时",
            ["Simple"] = "简单", ["Advanced"] = "高级",
            ["Continue"] = "继续", ["Post deposit"] = "沉积后处理", ["End the process"] = "结束工艺", ["Suspend process"] = "暂停工艺",
            ["Source"] = "输出源", ["Recorder"] = "记录仪", ["In position"] = "到位",
            ["Rate Raw"] = "原始速率", ["Rate Filtered"] = "滤波后速率", ["Thickness"] = "厚度", ["Rate Deviation"] = "速率偏差",
            ["Power"] = "功率", ["Frequency"] = "频率",
            ["Deposit"] = "沉积", ["Etch"] = "刻蚀",
            ["User Tone"] = "用户提示音", ["Error Tone"] = "错误提示音", ["Keypress Tone"] = "按键音",

            // 停止原因 (C1F)
            ["No stop"] = "未停止", ["Keyboard"] = "键盘", ["Crystal Fail"] = "晶振失效", ["Max Power"] = "最大功率",
            ["Hand Controller"] = "手控器", ["Communications"] = "通讯", ["Digital Input"] = "数字输入", ["Power Loss"] = "断电",
            ["Rate Dev Error"] = "速率偏差错误", ["Crystal Switcher fail"] = "晶振切换器故障",

            // 输出功能 (O**03、T0102**)
            ["Source Shutter 1"] = "输出源挡板 1", ["Source Shutter 2"] = "输出源挡板 2",
            ["Sensor Shutter 1"] = "传感器挡板 1", ["Sensor Shutter 2"] = "传感器挡板 2",
            ["Stop"] = "停止", ["End of process"] = "工艺结束", ["Thickness setpoint"] = "厚度设定点", ["Crystal fail"] = "晶振失效",
            ["Alarms"] = "报警", ["Source in use"] = "输出源使用中", ["Final Thickness"] = "最终厚度", ["End of Film"] = "膜系结束",
            ["In layer"] = "膜层中", ["Ion Assisted Deposition"] = "离子辅助沉积",
            ["Crystal Switcher 1"] = "晶振切换器 1", ["Crystal Switcher 2"] = "晶振切换器 2", ["Ready"] = "就绪",
            ["Crucible Switching"] = "坩埚切换中", ["Rise 1"] = "上升 1", ["Soak 1"] = "浸润 1", ["Rise 2"] = "上升 2", ["Soak 2"] = "浸润 2",
            ["Shutter Delay"] = "挡板延迟", ["Rate Ramp"] = "速率斜坡", ["Time Power"] = "时间功率", ["Idle Ramp"] = "空闲斜坡",
            ["Idle"] = "空闲", ["Rate Deviation Error"] = "速率偏差错误", ["Crystal Switcher Fail"] = "晶振切换器故障",
            ["Crystal Switching"] = "晶振切换中", ["Crucible Switcher Fail"] = "坩埚切换器故障", ["Shutter Delay Error"] = "挡板延迟错误",
            ["Computer Control"] = "计算机控制", ["Not implemented"] = "未实现",
            ["Crucible Select Source 1 Binary"] = "输出源 1 坩埚选择 (二进制)", ["Crucible Select Source 1 BCD"] = "输出源 1 坩埚选择 (BCD)",
            ["Crucible Select Source 2 Binary"] = "输出源 2 坩埚选择 (二进制)", ["Crucible Select Source 2 BCD"] = "输出源 2 坩埚选择 (BCD)",

            // 输入功能 (I**03、T0101**)
            ["Start"] = "启动", ["End Deposit"] = "结束沉积", ["Rate Watcher Hold Initiate"] = "速率监视保持开始",
            ["Rate Watcher Hold Inhibit"] = "禁止速率监视保持", ["Crystal Fail Inhibit"] = "禁止晶振失效", ["Zero Thickness"] = "厚度清零",
            ["Soak 2 Hold"] = "浸润 2 保持", ["Crucible 1 Valid"] = "坩埚 1 有效", ["Crucible 2 Valid"] = "坩埚 2 有效", ["Reset"] = "复位",
            ["Switch Crystal"] = "切换晶振", ["Non-Deposit Hold"] = "非沉积保持", ["Zero Film Time"] = "膜系时间清零",
            ["Start Inhibit"] = "禁止启动", ["Soak 1 Hold"] = "浸润 1 保持",
        };

        private static readonly Regex OptionBit = new Regex(@"^(.+) bit (\d+)$", RegexOptions.CultureInvariant);
        private static readonly Regex OptionBits = new Regex(@"^(.+) \((\d+) bits\)$", RegexOptions.CultureInvariant);
        private static readonly Regex SelectProcess = new Regex(@"^Select Process (\d+-\d+)$", RegexOptions.CultureInvariant);

        /// <summary>各级编号的含义,键为该级 <c>**</c> 之前的模式。含 <c>{0}</c> 时编号填入其中 (如"第 1 个传感器"),否则写在名称后 (如"预熔 2")。</summary>
        private static readonly Dictionary<string, BilingualText> IndexNamesByPrefix = new Dictionary<string, BilingualText>(StringComparer.Ordinal)
        {
            ["M"] = new BilingualText("材料", "Material"),
            ["F"] = new BilingualText("膜系", "Film"),
            ["L"] = new BilingualText("膜层", "Layer"),
            ["P02"] = new BilingualText("工艺", "Process"),
            ["N"] = new BilingualText("传感器", "Sensor"),
            ["S"] = new BilingualText("输出源", "Source"),
            ["I"] = new BilingualText("输入", "Input"),
            ["O"] = new BilingualText("输出", "Output"),
            ["G09"] = new BilingualText("选项卡", "Option Card"),
            ["F**03"] = new BilingualText("预熔", "Pre Condition"),
            ["F**05"] = new BilingualText("后处理", "Post Condition"),
            ["F**07"] = new BilingualText("第 {0} 个传感器", "Sensor {0}"), // 膜系的传感器列表:是列表中的第几项,不是传感器编号
            ["L**02"] = new BilingualText("膜系", "Film"),
            ["L**02**06"] = new BilingualText("斜坡", "Ramp"),
            ["P02**07"] = new BilingualText("膜层", "Layer"),
            ["N**0B"] = new BilingualText("输出", "Output"),
            ["N**10"] = BilingualText.Same("QCM"),
            ["N**1A"] = new BilingualText("输入", "Input"),
            ["S**09"] = new BilingualText("坩埚", "Crucible"),
            ["S**0C"] = new BilingualText("输出", "Output"),
            ["S**0D"] = new BilingualText("输入", "Input"),
            ["T0101"] = new BilingualText("输入", "Input"),
            ["T0102"] = new BilingualText("输出", "Output"),
        };

        private static readonly Dictionary<char, BilingualText> ObjectKinds = new Dictionary<char, BilingualText>
        {
            ['M'] = new BilingualText("材料", "Material"),
            ['F'] = new BilingualText("膜系", "Film"),
            ['L'] = new BilingualText("膜层", "Layer"),
            ['P'] = new BilingualText("工艺", "Process"),
            ['N'] = new BilingualText("传感器", "Sensor"),
            ['S'] = new BilingualText("输出源", "Source"),
            ['I'] = new BilingualText("输入", "Input"),
            ['O'] = new BilingualText("输出", "Output"),
        };

        private static readonly IReadOnlyList<CommandDefinition> Definitions = new[]
        {
            // ---------- H / E ----------
            Plain("H01", "产品名 + 软件版本"),
            Plain("H02", "软件版本,逗号分隔"),
            new CommandDefinition("E", CommandActions.None, "回显测试", CommandValueKind.Text, remark: "E:数据 → 原样返回数据"),

            // ---------- G 通用 ----------
            Info("G01", "通信 API 版本"),
            Info("G02", "设备类型"),
            Info("G03", "设备名"),
            Info("G04", "厂商"),
            Info("G05", "型号"),
            Info("G06", "系统序列号"),
            Info("G07", "系统硬件版本"),
            Info("G08", "系统软件版本"),
            Info("G09**01", "选项卡类型/名称"),
            Info("G09FE", "选项卡数量"),
            Text("G0A", "系统时间", "实机返回 MM-DD-YYYY HH:MM:SS,文档写 YYYY-MM-DD HH:MM:SS;写入格式未验证"),
            Info("G0B", "开机以来时间"),
            Info("G0C", "开机次数"),
            Act("G0D", "复位命令"),
            Choice("G0E01", "显示开/关", OffOn),
            Choice("G0E02", "前面板锁定", "0=允许屏幕更新, 1=禁止"),
            Int("G0E03", "亮度", "%", 20, 100),
            Int("G0E04", "调光定时", "分钟", 0, 300, "文档范围 1~300;实机值为 0"),
            Int("G0E05", "关屏定时", "分钟", 0, 300, "文档范围 1~300;实机值为 0"),
            Choice("G0F01", "按键音", OffOn),
            Choice("G0F02", "通知音", OffOn),
            Choice("G11", "通信锁定", "0=Unlocked, 1=Locked"),
            Dec("G1201", "速率滤波", "秒", 0.1m, 30.0m),
            Int("G1202", "偏差图限制", "Å/s", 1, 15),
            Choice("G1203", "混合最大功率线", OffOn),
            Int("G1204", "晶振寿命警告", "%", 1, 99),
            Int("G1205", "晶振寿命错误", "%", 1, 99),
            InfoEnum("G1301", "RS232 协议类型", ProtocolType),
            Info("G1302", "RS232 协议版本"),
            Info("G1303", "RS232 波特率", "bps"),
            Choice("G1304", "RS232 校验和", DisableEnable),
            Choice("G1305", "RS232 长度检查", DisableEnable),
            Choice("G1306", "RS232 文件传输", "0=Disable, 1=FTP Enable"),
            Choice("G1401", "DHCP", "0=静态IP, 1=DHCP"),
            Text("G1402", "IPv4 地址"),
            Text("G1403", "IPv4 子网掩码"),
            Text("G1404", "IPv4 网关"),
            Info("G1405", "以太网 MAC 地址"),
            Choice("G1406", "以太网协议类型", ProtocolType),
            Choice("G1407", "以太网校验和", DisableEnable),
            Choice("G1408", "以太网长度检查", DisableEnable),
            InfoEnum("G140A", "以太网链路状态", "0=链路断开, 1=链路连通"),
            Text("G1409", "以太网文件传输", "文档未列出,实机帮助表有"),
            new CommandDefinition("G16", R, "触发蜂鸣器", CommandValueKind.Enum, options: ParseOptions("1=User Tone, 2=Error Tone, 3=Keypress Tone")),
            Info("G180101", "板序列号"),
            Info("G180102", "板部件号"),

            // ---------- C 工艺控制 ----------
            Choice("C01", "停止/启动工艺", "0=停止, 1=启动/恢复"),
            Choice("C02", "报警停止", "0=不停止, 1=停止"),
            Act("C03", "所有传感器厚度清零"),
            Act("C04", "复位工艺,回到就绪状态"),
            Act("C05", "清除 S 和 Q 计数"),
            Choice("C06", "膜层控制", "0=PID, 1=Manual"),
            Dec("C07", "手动功率设定", "%", 0.00m, 100.00m),
            Int("C08", "运行号", string.Empty, 0, 4294967295),
            Info("C09", "当前状态"),
            Info("C0A", "活动工艺名"),
            Info("C0B", "活动膜层名"),
            Info("C0C", "活动膜系名"),
            Info("C0D", "活动传感器名"),
            Info("C0E", "活动输出源名"),
            Status("C0F", "电源输出电压", "V"),
            Info("C10", "最大功率", "%"),
            Status("C11", "当前功率", "%"),
            Status("C12", "当前速率,滤波后", "Å/s"),
            Status("C13", "原始速率", "Å/s"),
            Status("C14", "速率滚动平均", "Å/s"),
            Status("C15", "当前厚度", "kÅ"),
            Status("C16", "原始频率", "Hz"),
            Info("C17", "活动晶振寿命", "%"),
            InfoEnum("C18", "活动晶振失败", "0=Good, 1=Failed"),
            InfoEnum("C19", "报警状态", "0=No Alarm, 1=Alarm"),
            Info("C1A", "当前工艺时间"),
            Info("C1B", "最后工艺错误"),
            Choice("C1C", "测试模式", DisabledEnabled),
            Dec("C1D", "测试模式速率", "Å/s", 0.000m, 999.9m),
            Act("C1E", "触发最终厚度"),
            InfoEnum("C1F", "停止原因", StopReasons),
            Info("C20", "当前稳定性总计"),
            Info("C21", "当前质量计数"),

            // ---------- M 材料 ----------
            ListEnds("M00", "在列表末尾添加/删除材料"),
            CreateOrDelete("M**", "添加/删除指定编号的材料", "添加实机验证:AM01~AM03;按编号删除 (D) 文档未说明"),
            Text("M**01", "材料名称"),
            Dec("M**03", "密度", "g/cm³", 0.100m, 100.000m),
            Dec("M**04", "Z 比", string.Empty, 0.100m, 15.000m),
            Dec("M**07", "工具因子", "%", 1.0m, 1000.0m),
            Info("MFE", "材料数量"),

            // ---------- F 膜 ----------
            ListEnds("F00", "在列表末尾添加/删除膜系", "文档未列出,实机帮助表有"),
            CreateOrDelete("F**", "添加/删除指定编号的膜系", "添加实机验证:AF01、AF02;按编号删除 (D) 文档未说明"),
            Text("F**01", "膜系名称", "文档未列出,实机为 Name"),
            Ref("F**02", "材料 ID", 'M', allowNone: true),
            ListEnds("F**0300", "添加/删除预熔"),
            Time("F**03**01", "起始斜坡/上升时间", 150),
            Dec("F**03**02", "斜坡功率", "%", 0.00m, 99.99m),
            Time("F**03**03", "保持时间", 150),
            Info("F**03FE", "预熔数量"),
            Choice("F**0401", "延迟选项", "0=None, 1=Shutter, 2=Control, 3=Both"),
            Time("F**0402", "控制延迟时间", 150),
            Dec("F**0403", "挡板延迟精度", "%", 1, 99),
            Time("F**0404", "挡板延迟超时", 3, 1),
            Ref("F**0405", "传输传感器 ID", 'N', allowNone: true),
            Dec("F**0406", "传输工具因子", "%", 1.0m, 10000.0m),
            ListEnds("F**0500", "添加/删除后处理"),
            Time("F**05**01", "后处理斜坡/上升时间", 150),
            Dec("F**05**02", "后处理斜坡功率", "%", 0.00m, 99.99m),
            Time("F**05**03", "后处理保持时间", 150),
            Info("F**05FE", "后处理数量"),
            Choice("F**0601", "速率监视", DisabledEnabled),
            Time("F**0602", "速率监视时间", 150),
            Dec("F**0603", "速率监视精度", "%", 1, 100),
            Choice("F**0604", "离子辅助沉积", NoYes),
            ListEnds("F**0700", "添加/删除传感器"),
            Ref("F**07**01", "传感器编号", 'N', allowNone: false),
            Int("F**07**02", "晶振位置", string.Empty, 0, null, "文档范围 1~晶振数;实机值为 0"),
            Dec("F**07**03", "传感器膜系工具因子", "%", 1.0m, 9999.0m),
            Int("F**07**04", "最后晶振位置", string.Empty, 0, null, "0=禁用, 1~晶振数"),
            Choice("F**07**05", "失败动作", "0=Unused, 1=PostDp, 2=Stop, 3=Suspnd, 4=TimePw"),
            Choice("F**07**06", "失败触发", "0=On fail, 1=On last"),
            Text("F**07**07", "权重", "文档未列出,实机帮助表有"),
            Text("F**07**08", "校准厚度", "文档未列出,实机帮助表有"),
            Info("F**07FE", "传感器数量"),
            Choice("F**0801", "传感器模式", "0=Simple, 1=Advanced"),
            Text("F**0802", "主工具因子", "文档未列出,实机帮助表有"),
            Text("F**0803", "多点", "文档未列出,实机帮助表有"),
            Text("F**0804", "聚合倍乘", "文档未列出,实机帮助表有"),
            Ref("F**0805", "备份传感器 ID", 'N', allowNone: true),
            Dec("F**0806", "备份工具因子", "%", 1.0m, 10000.0m),
            Dec("F**0807", "质量百分比", "%", 0, 99), // 文档写整数,实机值为 0.0
            Int("F**0808", "质量计数", string.Empty, 0, 99),
            Int("F**0809", "稳定性单个", string.Empty, 0, 9999, "文档范围 25~9999;实机值为 0"),
            Int("F**080A", "稳定性总计", string.Empty, 0, 9999, "文档范围 25~9999;实机值为 0"),
            Ref("F**0901", "输出源 ID", 'S', allowNone: true),
            Int("F**0902", "坩埚", string.Empty, 0, null, "0=禁用"),
            Dec("F**0903", "P 项", string.Empty, 0.010m, 10000.00m),
            Dec("F**0904", "I 项", "秒", 0.000m, 10000.000m),
            Dec("F**0905", "D 项", "秒", 0.000m, 10000.000m),
            Dec("F**0906", "最大功率", "%", 0.0m, 100.0m),
            Choice("F**0907", "最大功率选项", MaxPowerOptions),
            Dec("F**0908", "最小功率", "%", 0.0m, 100.0m),
            Text("F**0909", "高级模式", "文档未列出,实机帮助表有"),
            Info("FFE", "膜系数量"),

            // ---------- L 层 ----------
            ListEnds("L00", "添加/删除膜层"),
            CreateOrDelete("L**", "添加/删除指定编号的膜层", "按材料的规则推断,未经实机验证"),
            Text("L**01", "膜层名称"),
            ListEnds("L**0200", "添加/删除膜系到膜系列表"),
            Ref("L**02**01", "膜系 ID", 'F', allowNone: true),
            Dec("L**02**02", "速率", "Å/s", 0.000m, 999.9m),
            Dec("L**02**03", "最终厚度", "kÅ", 0.0000m, 1000.000m),
            ListEnds("L**02**0600", "添加/删除速率斜坡"),
            new CommandDefinition("L**02**06**", QU, "速率斜坡项", remark: "实机为没有值的列表节点,设置项是其下的起始厚度等"),
            Dec("L**02**06**01", "起始厚度", "kÅ", 0.0000m, 999.9999m),
            Time("L**02**06**02", "斜坡时间", 150),
            Dec("L**02**06**03", "新速率", "Å/s", 0.000m, 999.999m),
            Info("L**02**06FE", "速率斜坡数量"),
            Info("L**02FE", "膜系列表数量"),
            Info("LFE", "膜层数量"), // 文档未列出,实机帮助表有

            // ---------- P 工艺 ----------
            Int("P01", "设置活动工艺", string.Empty, 1, 250, "工艺编号;文档写 P0201~P02C8"),
            ListEnds("P0200", "添加/删除工艺"),
            CreateOrDelete("P02**", "添加/删除指定编号的工艺", "按材料的规则推断,未经实机验证"),
            Text("P02**01", "工艺名称"),
            Int("P02**02", "活动膜层数量", string.Empty, 1, null, "1~膜层列表中的膜层数"),
            Text("P02**03", "需要输出源 DAC", "文档未列出,实机帮助表有"),
            Choice("P02**04", "自动启动下一膜层", NoYes),
            Text("P02**05", "活动膜层输出", "文档未列出,实机帮助表有"),
            Int("P02**06", "运行号", string.Empty, 1, 4294967295),
            ListEnds("P02**0700", "添加/删除膜层"),

            // 工艺包含膜层:值是膜层编号,下拉框选择已有的膜层 (用户要求)。实机上有指向不存在膜层的值 (31/32/33,而只有 L01~L09),
            // 编辑时显示为"(不存在)"并保留;恢复计划不把它当作引用 (见 RecipeRules.References)。
            Ref("P02**07**", "膜层 ID", 'L', allowNone: true),
            Info("P02**07FE", "膜层数量"),
            Choice("P02**08", "沉积/刻蚀", "0=Deposit, 1=Etch"),
            Choice("P02**09", "显示负速率", NoYes),
            Int("P02**0A", "链接工艺 ID", string.Empty, 0, 250),
            Choice("P02**0B", "无备份晶振启动", NoYes),
            Info("P02FE", "工艺数量"),

            // ---------- N 传感器 ----------
            Text("N**01", "传感器名称"),
            Choice("N**02", "传感器类型", "0=Single, 1=Dual, 2=Crystal2, 3=Crystal6, 4=Crystal12, 5=Generic"),
            Choice("N**03", "控制类型", ControlType),
            Choice("N**04", "驱动类型", DriveType),
            Choice("N**05", "步进", StepType),
            Int("N**06", "反馈类型", string.Empty, null, null, "0=无"), // 文档只给出 0,不能做成选项
            Time("N**07", "索引延迟", 999),
            Info("N**08", "位置数量"),
            Int("N**09", "当前位置", string.Empty, 1, 32),
            Dec("N**0A", "晶振工具因子", "%", 1.00m, 10000.00m),
            Ref("N**0B**", "晶振切换输出设置", 'O', allowNone: true),
            Choice("N**0C", "开/关挡板", CloseOpen),
            Text("N**0D", "挡板延迟", "文档未列出,实机帮助表有"),
            Ref("N**0E", "挡板输出设置", 'O', allowNone: true),
            Ref("N**0F", "晶振失败输出设置", 'O', allowNone: true),
            Info("N**10**02", "QCM 健康", "%"),
            Info("N**10FE", "QCM 数量"),
            Info("N**11", "活动 QCM ID"),
            Status("N**12", "活动 QCM 频率", "Hz"),
            Status("N**13", "活动 QCM 速率", "Å/s"),
            Status("N**14", "活动 QCM 厚度"),
            Status("N**15", "活动 QCM 状态", string.Empty, "0=Good, 1=Failure"),
            Status("N**16", "活动 QCM 寿命", "%"),
            Status("N**17", "活动 QCM 活动"),
            Act("N**18", "切换晶振"),
            Act("N**19", "旋转转盘"),
            Ref("N**1A**", "晶振切换输入设置", 'I', allowNone: true),
            Act("N**1B", "清除晶振状态"),
            InfoEnum("N**1C", "XIU 连接", "0=Disconnected, 1=Connected"),
            Info("N**1D", "XIU 类型"),
            Info("N**1E", "XIU 序列号"),
            Info("N**1F", "XIU 电缆长度", "ft"),
            Status("N**20", "速率偏差"),
            Info("NFE", "传感器数量"),

            // ---------- S 源 ----------
            Text("S**01", "输出源名称"),
            Choice("S**02", "输出源输出配置", "0=Source, 1=Recorder"),
            Status("S**03", "当前电压", "V"),
            Status("S**04", "当前百分比功率", "%"),
            Ref("S**05", "挡板输出设置", 'O', allowNone: true),
            Choice("S**06", "开/关挡板", CloseOpen),
            Int("S**07", "坩埚数量", string.Empty, 0, 32),
            Int("S**08", "当前坩埚编号", string.Empty, 1, 32, "1~坩埚数量 (S**07)"),
            ListEnds("S**0900", "添加/删除坩埚材料"),
            Ref("S**09**", "材料 ID", 'M', allowNone: true),
            Info("S**09FE", "坩埚材料数量"),
            Dec("S**0A", "最大输出", "V", -10, 10),
            Choice("S**0B", "控制类型", ControlType),
            Ref("S**0C**", "坩埚输出继电器设置", 'O', allowNone: true),
            Ref("S**0D**", "坩埚输入继电器设置", 'I', allowNone: true),
            Choice("S**0E", "驱动类型", DriveType),
            Choice("S**0F", "步进", StepType),
            Choice("S**10", "反馈类型", "0=None, 1=Individual, 2=BCD, 4=In position"),
            Time("S**11", "索引延迟", 150, 1),
            Int("S**12", "记录仪输出选择", string.Empty, null, null),
            Choice("S**13", "记录仪功能", "0=Rate Raw, 1=Rate Filtered, 2=Thickness, 3=Rate Deviation, 4=Power, 5=Frequency"),
            Dec("S**14", "速率 0% 输出", "Å/s", 0, 100000),
            Dec("S**15", "速率 100% 输出", "Å/s", 0, 100000),
            Dec("S**16", "厚度 0% 输出", "kÅ", 0, 100), // 手册写 0~100000 Å,实机单位为 kÅ
            Dec("S**17", "厚度 100% 输出", "kÅ", 0, 100),
            Dec("S**18", "速率偏差 0% 输出", string.Empty, -100, 100),
            Dec("S**19", "速率偏差 100% 输出", string.Empty, -100, 100),
            Dec("S**1A", "功率 0% 输出", "%", 0, 100),
            Dec("S**1B", "功率 100% 输出", "%", 0, 100),
            Dec("S**1C", "频率 0% 输出", "Hz", 4000000, 6000000),
            Dec("S**1D", "频率 100% 输出", "Hz", 4000000, 6000000),
            Act("S**1E", "切换坩埚"),
            Info("SFE", "输出源数量"),

            // ---------- I 输入 ----------
            Text("I**01", "输入名称"),
            Choice("I**02", "高/低有效", "0=Low, 1=High"),
            InfoEnum("I**03", "分配功能", InputFunctions),
            InfoEnum("I**04", "活动状态", "0=Inactive, 1=Active"),
            InfoEnum("I**05", "原始状态", "0=Low, 1=High"),
            Info("IFE", "输入数量"),

            // ---------- O 输出 ----------
            Text("O**01", "输出名称"),
            Choice("O**02", "正常/非活动状态", "0=常开, 1=常闭"),
            InfoEnum("O**03", "分配功能", OutputFunctions),
            Choice("O**04", "活动状态", "0=Inactive, 1=Active"),
            InfoEnum("O**05", "原始状态", "0=Open/Low, 1=Closed/High"),
            Info("OFE", "输出数量"),

            // ---------- T 逻辑 ----------
            // 文档没有逻辑组的指令;实机帮助表有基本 IO 逻辑 (Basic IO Logic):
            // T0101** = 输入 n 触发的事件 (Input n: Basic Event),T0102** = 输出 n 的动作 (Output n: Basic Action),
            // 序号对应输入/输出编号,取值按文档 4.9 输入分配范围、4.8 输出分配范围。
            Choice("T0101**", "基本事件", InputFunctions),
            Choice("T0102**", "基本动作", OutputFunctions),
        };

        private static readonly Dictionary<CommandGroup, IReadOnlyList<CommandDefinition>> ByGroup =
            Enum.GetValues(typeof(CommandGroup)).Cast<CommandGroup>()
                .ToDictionary(g => g, g => (IReadOnlyList<CommandDefinition>)Definitions.Where(d => d.Group == g).ToList());

        /// <summary>全部指令,按协议文档的顺序。</summary>
        public static IReadOnlyList<CommandDefinition> All => Definitions;

        /// <summary>
        /// 取一个命令组的全部指令,如 <see cref="CommandGroup.M"/> → 材料的全部指令 (M00、M**、M**01、M**03…)。
        /// </summary>
        public static IReadOnlyList<CommandDefinition> GetCommands(CommandGroup group) =>
            ByGroup.TryGetValue(group, out var commands) ? commands : Array.Empty<CommandDefinition>();

        /// <summary>
        /// 由命令 ID 的第一个字符得到命令组,如 <c>M0103</c> → <see cref="CommandGroup.M"/>。不区分大小写。
        /// </summary>
        /// <returns>第一个字符是已知的命令组时返回 true。</returns>
        public static bool TryGetGroup(string id, out CommandGroup group)
        {
            group = default;
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            var letter = char.ToUpperInvariant(id[0]);
            return letter >= 'A' && letter <= 'Z' && Enum.TryParse(letter.ToString(), out group) && Enum.IsDefined(typeof(CommandGroup), group);
        }

        /// <summary>
        /// 查找命令 ID 对应的指令,如 <c>M0103</c> → <c>M**03</c> (密度)。只在该 ID 所属命令组中查找;
        /// 匹配多个模式时取固定字符最多的 (如 <c>MFE</c> 是材料数量而不是 <c>M**</c>)。ID 不带动作字母。
        /// </summary>
        /// <returns>找不到时返回 null。</returns>
        public static CommandDefinition? Find(string id)
        {
            if (!TryGetGroup(id, out var group))
            {
                return null;
            }

            CommandDefinition? best = null;
            foreach (var definition in GetCommands(group))
            {
                if (definition.IsMatch(id) && (best == null || definition.Specificity > best.Specificity))
                {
                    best = definition;
                }
            }

            return best;
        }

        /// <summary>
        /// 由完整的命令 ID 生成指令,如 <c>Build(Update, "M0103", "2.700")</c> → <c>UM0103:2.700</c>。
        /// ID 在指令集中有定义时检查动作是否支持;没有定义的 ID (如逻辑组、文档未列出的设置项) 只检查格式,由设备判断。
        /// </summary>
        /// <param name="action">单个动作。</param>
        /// <param name="id">命令 ID (大写,不带动作字母)。</param>
        /// <param name="value">数据;更新时必须给出,查询/添加/删除/状态不能给出。</param>
        /// <exception cref="ArgumentException">ID 格式不对、动作不支持或组合了多个动作、数据缺失/多余/含非法字符。</exception>
        public static string Build(CommandActions action, string id, string? value = null)
        {
            if (id is null || !IdFormat.IsMatch(id) || !TryGetGroup(id, out _))
            {
                throw new ArgumentException($"Invalid command ID: {id}", nameof(id));
            }

            CheckAction(action, value, Find(id));
            return Compose(action, id, value);
        }

        /// <summary>查询指令,如 <c>QM0103</c>。</summary>
        /// <exception cref="ArgumentException">见 <see cref="Build(CommandActions, string, string)"/>。</exception>
        public static string Query(string id) => Build(CommandActions.Query, id);

        /// <summary>更新指令,如 <c>UM0103:2.700</c>。</summary>
        /// <exception cref="ArgumentException">见 <see cref="Build(CommandActions, string, string)"/>。</exception>
        public static string Update(string id, string value) => Build(CommandActions.Update, id, value ?? throw new ArgumentNullException(nameof(value)));

        /// <summary>添加指令,如 <c>AM03</c> (按编号创建材料)、<c>AF010300</c> (在膜 F01 的预熔列表末尾追加)。</summary>
        /// <exception cref="ArgumentException">见 <see cref="Build(CommandActions, string, string)"/>。</exception>
        public static string Add(string id) => Build(CommandActions.Add, id);

        /// <summary>
        /// 检查动作和数据:只能是单个动作;<paramref name="definition"/> 不为 null 时动作必须受支持;
        /// 更新必须带数据,查询/添加/删除/状态不能带数据,数据不能含协议不允许的字符。
        /// </summary>
        /// <exception cref="ArgumentException">不符合上述规则。</exception>
        internal static void CheckAction(CommandActions action, string? value, CommandDefinition? definition)
        {
            if (action != CommandActions.None && (action & (action - 1)) != 0)
            {
                throw new ArgumentException("Only one action can be given.", nameof(action));
            }

            if (definition != null && !definition.Supports(action))
            {
                throw new ArgumentException($"Command {definition.Pattern} ({definition.EnglishName}) does not support {action}; supported: {definition.Actions}.", nameof(action));
            }

            if (action == CommandActions.Update && value == null)
            {
                throw new ArgumentException("An update command needs a value.", nameof(value));
            }

            if (value == null)
            {
                return;
            }

            if (action != CommandActions.Update && action != CommandActions.Remote && action != CommandActions.None)
            {
                throw new ArgumentException($"Action {action} takes no value.", nameof(value));
            }

            var error = ProtocolText.ValidateValue(value);
            if (error != null)
            {
                throw new ArgumentException(error, nameof(value));
            }
        }

        /// <summary>拼接:动作字母 + ID + [<c>:</c>数据]。调用前已由 <see cref="CheckAction"/> 检查。</summary>
        internal static string Compose(CommandActions action, string id, string? value)
        {
            var command = ActionLetter(action) + id;
            return value == null ? command : command + ":" + value;
        }

        /// <summary>动作字母:Query → <c>Q</c>,None → 空。</summary>
        /// <exception cref="ArgumentException">不是单个动作。</exception>
        public static string ActionLetter(CommandActions action)
        {
            switch (action)
            {
                case CommandActions.None: return string.Empty;
                case CommandActions.Query: return "Q";
                case CommandActions.Update: return "U";
                case CommandActions.Add: return "A";
                case CommandActions.Delete: return "D";
                case CommandActions.Status: return "S";
                case CommandActions.Remote: return "R";
                default: throw new ArgumentException("Must be a single action.", nameof(action));
            }
        }

        /// <summary>对象类型的名称,如 <c>M</c> → 材料 / Material;未知时为"对象"/Object。</summary>
        public static string ObjectKind(char group, TextLanguage language) =>
            ObjectKinds.TryGetValue(group, out var kind) ? kind.Get(language) : language.Pick("对象", "Object");

        /// <summary>英文说明 (设备帮助表的 DESCRIPTION);没有收录时为 null。</summary>
        internal static string? EnglishNameOf(string pattern) =>
            EnglishNames.TryGetValue(pattern, out var name) ? name : null;

        /// <summary>中文文本 → 中英文;没有收录翻译的文本中英文相同。</summary>
        internal static BilingualText Localize(string chinese) =>
            new BilingualText(chinese, Translations.TryGetValue(chinese, out var english) ? english : chinese);

        /// <summary>
        /// 某一级编号 (列表) 的名称,参数为该级 <c>**</c> 之前的模式,如 <c>F**03</c> → 预熔 / Pre Condition、
        /// <c>P02**07</c> → 膜层 / Layer;未收录时为 <c>#</c>。名称可能含编号占位符 <c>{0}</c> (如 <c>F**07</c>)。
        /// </summary>
        public static BilingualText IndexName(string prefixPattern) =>
            IndexNamesByPrefix.TryGetValue(prefixPattern, out var name) ? name : BilingualText.Same("#");

        /// <summary>解析 <c>0=Off, 1=On</c> 形式的选项;中文含义 (如"未分配") 按 <see cref="Translations"/> 给出英文。</summary>
        internal static IReadOnlyList<CommandOption> ParseOptions(string text) =>
            text.Split(',')
                .Select(part => part.Split(new[] { '=' }, 2))
                .Select(pair => new CommandOption(pair[0].Trim(), LocalizeOption(pair.Length > 1 ? pair[1].Trim() : string.Empty)))
                .ToList();

        /// <summary>选项文本:表中写中文的用 <see cref="Translations"/> 得到英文,写英文的用 <see cref="ChineseOption"/> 得到中文。</summary>
        private static BilingualText LocalizeOption(string text) =>
            Translations.ContainsKey(text) ? Localize(text) : new BilingualText(ChineseOption(text) ?? text, text);

        /// <summary>英文选项的中文;没有收录时为 null。"… bit n" 译为"… 第 n 位","… (n bits)" 译为"… (n 位)"。</summary>
        internal static string? ChineseOption(string english)
        {
            if (ChineseOptions.TryGetValue(english, out var chinese))
            {
                return chinese;
            }

            var bits = OptionBits.Match(english);
            if (bits.Success)
            {
                return (ChineseOption(bits.Groups[1].Value) ?? bits.Groups[1].Value) + " (" + bits.Groups[2].Value + " 位)";
            }

            var bit = OptionBit.Match(english);
            if (bit.Success)
            {
                return (ChineseOption(bit.Groups[1].Value) ?? bit.Groups[1].Value) + " 第 " + bit.Groups[2].Value + " 位";
            }

            var process = SelectProcess.Match(english);
            return process.Success ? "选择工艺 " + process.Groups[1].Value : null;
        }

        /// <summary>不带动作字母的指令,如 H01。</summary>
        private static CommandDefinition Plain(string pattern, string name) =>
            new CommandDefinition(pattern, CommandActions.None, name);

        /// <summary>只读 (Q)。</summary>
        private static CommandDefinition Info(string pattern, string name, string unit = "") =>
            new CommandDefinition(pattern, Q, name, CommandValueKind.Text, unit);

        /// <summary>只读 (Q),值为固定选项。</summary>
        private static CommandDefinition InfoEnum(string pattern, string name, string options) =>
            new CommandDefinition(pattern, Q, name, CommandValueKind.Enum, options: ParseOptions(options));

        /// <summary>运行时状态 (S)。</summary>
        private static CommandDefinition Status(string pattern, string name, string unit = "", string? options = null) =>
            new CommandDefinition(pattern, S, name, options == null ? CommandValueKind.Number : CommandValueKind.Enum, unit,
                options: options == null ? null : ParseOptions(options));

        /// <summary>远程动作 (R),不带数据。</summary>
        private static CommandDefinition Act(string pattern, string name) =>
            new CommandDefinition(pattern, R, name);

        /// <summary>列表末尾的添加/删除 (A/D),如 M00。</summary>
        private static CommandDefinition ListEnds(string pattern, string name, string remark = "") =>
            new CommandDefinition(pattern, AD, name, remark: remark);

        /// <summary>按指定编号创建对象 (A),如 AM03。</summary>
        private static CommandDefinition Create(string pattern, string name, string remark) =>
            new CommandDefinition(pattern, CommandActions.Add, name, remark: remark);

        /// <summary>按指定编号创建 (A) 或删除 (D) 对象,如 AM03、DM03。</summary>
        private static CommandDefinition CreateOrDelete(string pattern, string name, string remark) =>
            new CommandDefinition(pattern, AD, name, remark: remark);

        private static CommandDefinition Text(string pattern, string name, string remark = "") =>
            new CommandDefinition(pattern, QU, name, CommandValueKind.Text, remark: remark);

        private static CommandDefinition Choice(string pattern, string name, string options) =>
            new CommandDefinition(pattern, QU, name, CommandValueKind.Enum, options: ParseOptions(options));

        private static CommandDefinition Int(string pattern, string name, string unit, decimal? min, decimal? max, string remark = "") =>
            new CommandDefinition(pattern, QU, name, CommandValueKind.Number, unit, min, max, isInteger: true, remark: remark);

        private static CommandDefinition Dec(string pattern, string name, string unit, decimal? min, decimal? max, string remark = "") =>
            new CommandDefinition(pattern, QU, name, CommandValueKind.Number, unit, min, max, remark: remark);

        /// <summary>时间 H:MM:SS;范围按操作手册,<see cref="CommandDefinition.Min"/>/<see cref="CommandDefinition.Max"/> 为秒数。</summary>
        private static CommandDefinition Time(string pattern, string name, int maxHours, int minSeconds = 0) =>
            new CommandDefinition(pattern, QU, name, CommandValueKind.Time, min: minSeconds, max: maxHours * 3600m);

        private static CommandDefinition Ref(string pattern, string name, char group, bool allowNone) =>
            new CommandDefinition(pattern, QU, name, CommandValueKind.Reference, referenceGroup: group, allowNone: allowNone);
    }
}
