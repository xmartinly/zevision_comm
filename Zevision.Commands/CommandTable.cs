using System;
using System.Collections.Generic;
using System.Linq;

namespace Zevision.Commands
{
    /// <summary>
    /// 指令表:全部指令的定义 (数据,按协议文档的顺序) 和按组字母的索引。用法见 <see cref="ZevisionCommandSet"/>。
    /// </summary>
    /// <remarks>
    /// 定义的文字在 <see cref="CommandTexts"/> 中。本类中 <see cref="All"/> 必须写在 <see cref="ByLetter"/>、<see cref="Groups"/> 之前
    /// (静态字段按书写顺序初始化)。
    /// </remarks>
    internal static class CommandTable
    {
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

        /// <summary>全部指令 (只读),按协议文档的顺序。</summary>
        internal static readonly IReadOnlyList<CommandDefinition> All = Array.AsReadOnly(new[]
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
            new CommandDefinition("G16", R, "触发蜂鸣器", CommandValueKind.Enum, options: CommandTexts.ParseOptions("1=User Tone, 2=Error Tone, 3=Keypress Tone")),
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
        });

        /// <summary>按组字母 (A~Z) 的命令组;不是命令组的字母为 null。</summary>
        internal static readonly CommandGroup?[] Groups = BuildGroups();

        /// <summary>按组字母 (A~Z) 的指令 (只读);不是命令组的字母为 null。</summary>
        internal static readonly IReadOnlyList<CommandDefinition>?[] ByLetter = BuildIndex();

        private static CommandGroup?[] BuildGroups()
        {
            var groups = new CommandGroup?[26];
            foreach (CommandGroup group in Enum.GetValues(typeof(CommandGroup)))
            {
                groups[group.ToString()[0] - 'A'] = group;
            }

            return groups;
        }

        private static IReadOnlyList<CommandDefinition>?[] BuildIndex() =>
            Groups.Select(group => group == null ? null : (IReadOnlyList<CommandDefinition>)All.Where(d => d.Group == group).ToList().AsReadOnly()).ToArray();

        /// <summary>不带动作字母的指令,如 H01。</summary>
        private static CommandDefinition Plain(string pattern, string name) =>
            new CommandDefinition(pattern, CommandActions.None, name);

        /// <summary>只读 (Q)。</summary>
        private static CommandDefinition Info(string pattern, string name, string unit = "") =>
            new CommandDefinition(pattern, Q, name, CommandValueKind.Text, unit);

        /// <summary>只读 (Q),值为固定选项。</summary>
        private static CommandDefinition InfoEnum(string pattern, string name, string options) =>
            new CommandDefinition(pattern, Q, name, CommandValueKind.Enum, options: CommandTexts.ParseOptions(options));

        /// <summary>运行时状态 (S)。</summary>
        private static CommandDefinition Status(string pattern, string name, string unit = "", string? options = null) =>
            new CommandDefinition(pattern, S, name, options == null ? CommandValueKind.Number : CommandValueKind.Enum, unit,
                options: options == null ? null : CommandTexts.ParseOptions(options));

        /// <summary>远程动作 (R),不带数据。</summary>
        private static CommandDefinition Act(string pattern, string name) =>
            new CommandDefinition(pattern, R, name);

        /// <summary>列表末尾的添加/删除 (A/D),如 M00。</summary>
        private static CommandDefinition ListEnds(string pattern, string name, string remark = "") =>
            new CommandDefinition(pattern, AD, name, remark: remark);

        /// <summary>按指定编号创建 (A) 或删除 (D) 对象,如 AM03、DM03。</summary>
        private static CommandDefinition CreateOrDelete(string pattern, string name, string remark) =>
            new CommandDefinition(pattern, AD, name, remark: remark);

        private static CommandDefinition Text(string pattern, string name, string remark = "") =>
            new CommandDefinition(pattern, QU, name, CommandValueKind.Text, remark: remark);

        private static CommandDefinition Choice(string pattern, string name, string options) =>
            new CommandDefinition(pattern, QU, name, CommandValueKind.Enum, options: CommandTexts.ParseOptions(options));

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
