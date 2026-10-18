using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Zevision.Commands
{
    /// <summary>
    /// 指令表的文字:设备帮助表的英文说明、中文 → 英文的翻译、选项的中文、列表和对象的名称。
    /// </summary>
    /// <remarks>
    /// 与指令表 (<see cref="CommandTable"/>) 分开:<see cref="CommandDefinition"/> 在指令表初始化时读取这里的文字,
    /// 分在两个类中,本类第一次被用到时已完整初始化,与字段的书写顺序无关。
    /// </remarks>
    internal static class CommandTexts
    {
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
            ["0=任意, 1~所选输出源的坩埚数量 (S**07)"] = "0=Any, 1~number of crucibles of the selected source (S**07)",
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

        /// <summary>解析过的选项表,键为选项文本。</summary>
        private static readonly Dictionary<string, IReadOnlyList<CommandOption>> ParsedOptions = new Dictionary<string, IReadOnlyList<CommandOption>>(StringComparer.Ordinal);

        /// <summary>英文说明 (设备帮助表的 DESCRIPTION);没有收录时为 null。</summary>
        internal static string? EnglishNameOf(string pattern) =>
            EnglishNames.TryGetValue(pattern, out var name) ? name : null;

        /// <summary>中文文本 → 中英文;没有收录翻译的文本中英文相同。</summary>
        internal static BilingualText Localize(string chinese) =>
            new BilingualText(chinese, Translations.TryGetValue(chinese, out var english) ? english : chinese);

        /// <summary>某一级编号 (列表) 的名称 (见 <see cref="ZevisionCommandSet.IndexName"/>)。</summary>
        internal static BilingualText IndexName(string prefixPattern) =>
            IndexNamesByPrefix.TryGetValue(prefixPattern, out var name) ? name : BilingualText.Same("#");

        /// <summary>
        /// 解析 <c>0=Off, 1=On</c> 形式的选项;中文含义 (如"未分配") 按 <see cref="Translations"/> 给出英文。
        /// 同一段文本只解析一次,各指令共用同一个只读列表 (输出分配功能等长列表被多条指令使用)。
        /// </summary>
        internal static IReadOnlyList<CommandOption> ParseOptions(string text)
        {
            lock (ParsedOptions)
            {
                if (!ParsedOptions.TryGetValue(text, out var options))
                {
                    options = text.Split(',')
                        .Select(part => part.Split(new[] { '=' }, 2))
                        .Select(pair => new CommandOption(pair[0].Trim(), LocalizeOption(pair.Length > 1 ? pair[1].Trim() : string.Empty)))
                        .ToList()
                        .AsReadOnly();
                    ParsedOptions.Add(text, options);
                }

                return options;
            }
        }

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

        /// <summary>对象类型的名称 (见 <see cref="ZevisionCommandSet.ObjectKind"/>)。</summary>
        internal static string ObjectKind(char group, TextLanguage language) =>
            ObjectKinds.TryGetValue(group, out var kind) ? kind.Get(language) : language.Pick("对象", "Object");
    }
}
