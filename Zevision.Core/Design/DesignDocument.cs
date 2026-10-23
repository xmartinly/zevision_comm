using System;
using System.Collections.Generic;
using System.Linq;

namespace Zevision.Core.Design
{
    /// <summary>
    /// 工艺编辑的内容:用户定义的材料和工艺。上传一个工艺时,由 <see cref="DesignBuilder"/> 转换为设备上的
    /// 材料 (M)、膜系 (F)、膜层 (L) 和工艺 (P02)。数值都是设备的文本格式 (如 <c>2.700</c>、<c>0:01:00</c>)。
    /// 保存的位置和格式由 <see cref="IDesignStore"/> 决定 (按对象分目录的文件)。
    /// </summary>
    public sealed class DesignDocument
    {
        /// <summary>材料 (每个对应设备上的一个材料和一个膜系)。</summary>
        public List<MaterialDesign> Materials { get; set; } = new List<MaterialDesign>();

        /// <summary>工艺。</summary>
        public List<ProcessDesign> Processes { get; set; } = new List<ProcessDesign>();

        /// <summary>按 <see cref="MaterialDesign.Id"/> 找材料;没有时为 null。</summary>
        public MaterialDesign? FindMaterial(string? id) => Materials.FirstOrDefault(m => m.Id == id);
    }

    /// <summary>
    /// 一种材料:设备上的材料加膜系,两者同名。
    /// </summary>
    public sealed class MaterialDesign
    {
        /// <summary>标识 (工艺步骤按它引用材料,改名不影响引用)。</summary>
        public string Id { get; set; } = NewId();

        /// <summary>名称 (材料 <c>M**01</c> 和膜系 <c>F**01</c>);新建的膜层名为 <c>名称-工艺名称-步骤号</c>。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>中文名 (从材料库填入),只在本地显示 (设备的名称只能是 ASCII,不上传)。</summary>
        public string ChineseName { get; set; } = string.Empty;

        /// <summary>密度,g/cm³ (<c>M**03</c>)。</summary>
        public string Density { get; set; } = string.Empty;

        /// <summary>Z 比 (<c>M**04</c>)。</summary>
        public string ZRatio { get; set; } = string.Empty;

        /// <summary>材料的工具因子,% (<c>M**07</c>)。</summary>
        public string Tooling { get; set; } = "100.00";

        /// <summary>传感器编号 (膜系传感器列表第 1 项 <c>F**070101</c>)。</summary>
        public int Sensor { get; set; } = 1;

        /// <summary>晶振位置 (<c>F**070102</c>)。</summary>
        public string CrystalPosition { get; set; } = "0";

        /// <summary>输出源编号 (<c>F**0901</c>)。</summary>
        public int Source { get; set; } = 1;

        /// <summary>坩埚:0 = 任意,1~输出源的坩埚数量 (<c>F**0902</c>)。</summary>
        public int Crucible { get; set; }

        /// <summary>预熔 (<c>F**03</c>),默认两段。</summary>
        public List<ConditionStep> PreConditions { get; set; } = new List<ConditionStep> { new ConditionStep(), new ConditionStep() };

        /// <summary>后处理 (<c>F**05</c>),默认一段。</summary>
        public List<ConditionStep> PostConditions { get; set; } = new List<ConditionStep> { new ConditionStep() };

        /// <summary>PID 的 P 项 (<c>F**0903</c>),默认 10。</summary>
        public string PTerm { get; set; } = "10";

        /// <summary>PID 的 I 项,秒 (<c>F**0904</c>),默认 0。</summary>
        public string ITerm { get; set; } = "0";

        /// <summary>PID 的 D 项,秒 (<c>F**0905</c>),默认 0。</summary>
        public string DTerm { get; set; } = "0";

        /// <summary>最大功率,% (<c>F**0906</c>)。</summary>
        public string MaxPower { get; set; } = "100.00";

        /// <summary>达到最大功率时的处理:0=Continue, 1=Post deposit, 2=End the process, 3=Suspend process (<c>F**0907</c>)。</summary>
        public string MaxPowerOption { get; set; } = "0";

        /// <summary>延迟选项:0=None, 1=Shutter, 2=Control, 3=Both (<c>F**0401</c>)。不为 0 时写入下面的延迟设置。</summary>
        public string DelayOption { get; set; } = "0";

        /// <summary>控制延迟时间,H:MM:SS (<c>F**0402</c>)。</summary>
        public string ControlDelayTime { get; set; } = "0:00:00";

        /// <summary>挡板延迟精度,% (<c>F**0403</c>)。</summary>
        public string ShutterDelayAccuracy { get; set; } = "5.00";

        /// <summary>挡板延迟超时,H:MM:SS (<c>F**0404</c>)。</summary>
        public string ShutterDelayTimeout { get; set; } = "0:01:00";

        /// <summary>传输传感器编号,0 = 无 (<c>F**0405</c>)。</summary>
        public int TransferSensor { get; set; }

        /// <summary>传输工具因子,% (<c>F**0406</c>)。</summary>
        public string TransferTooling { get; set; } = "100.00";

        /// <summary>是否写入延迟设置 (延迟选项不为 0)。</summary>
        public bool HasDelaySettings => DelayOption.Trim().Length > 0 && DelayOption.Trim() != "0";

        /// <summary>新的标识。</summary>
        public static string NewId() => Guid.NewGuid().ToString("N");
    }

    /// <summary>一段预熔或后处理:斜坡/上升时间、斜坡功率、保持时间。</summary>
    public sealed class ConditionStep
    {
        /// <summary>斜坡/上升时间,H:MM:SS (<c>F**03**01</c>、<c>F**05**01</c>)。</summary>
        public string RampTime { get; set; } = "0:00:00";

        /// <summary>斜坡功率,% (<c>F**03**02</c>、<c>F**05**02</c>)。</summary>
        public string RampPower { get; set; } = "0.00";

        /// <summary>保持时间,H:MM:SS (<c>F**03**03</c>、<c>F**05**03</c>)。</summary>
        public string HoldTime { get; set; } = "0:00:00";
    }

    /// <summary>一个工艺:名称、是否自动启动下一膜层、按顺序的步骤 (每一步在设备上是一个膜层)。</summary>
    public sealed class ProcessDesign
    {
        /// <summary>标识。</summary>
        public string Id { get; set; } = MaterialDesign.NewId();

        /// <summary>名称 (<c>P02**01</c>)。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>自动启动下一膜层 (<c>P02**04</c>)。</summary>
        public bool AutoStartNext { get; set; } = true;

        /// <summary>步骤。</summary>
        public List<ProcessStepDesign> Steps { get; set; } = new List<ProcessStepDesign>();

        /// <summary>上次上传到设备的记录 (时间和用到的对象),只用于显示;没有上传过时为空。</summary>
        public string LastUpload { get; set; } = string.Empty;
    }

    /// <summary>工艺的一步:一种材料按速率镀到最终厚度,可带速率斜坡。在设备上是一个膜层 (膜系列表只有这一个膜系)。</summary>
    public sealed class ProcessStepDesign
    {
        /// <summary>材料的 <see cref="MaterialDesign.Id"/>。</summary>
        public string MaterialId { get; set; } = string.Empty;

        /// <summary>速率,Å/s (<c>L**020102</c>)。</summary>
        public string Rate { get; set; } = string.Empty;

        /// <summary>最终厚度,kÅ (<c>L**020103</c>)。</summary>
        public string Thickness { get; set; } = string.Empty;

        /// <summary>速率斜坡 (<c>L**020106</c>)。</summary>
        public List<RateRampDesign> RateRamps { get; set; } = new List<RateRampDesign>();
    }

    /// <summary>速率斜坡:到达起始厚度后,在斜坡时间内把速率改为新速率。</summary>
    public sealed class RateRampDesign
    {
        /// <summary>起始厚度,kÅ (<c>L**020106**01</c>)。</summary>
        public string StartThickness { get; set; } = string.Empty;

        /// <summary>斜坡时间,H:MM:SS (<c>L**020106**02</c>)。</summary>
        public string RampTime { get; set; } = "0:00:00";

        /// <summary>新速率,Å/s (<c>L**020106**03</c>)。</summary>
        public string NewRate { get; set; } = string.Empty;
    }

    /// <summary>
    /// 材料库中的一项 (IMC300 操作手册附录 A 的材料表,源文件 <c>docs\materials.json</c>)。
    /// 选择后把化学式填为材料名称,并填入密度、Z 比和中文名。
    /// </summary>
    /// <param name="Formula">化学式,如 <c>Al</c> (材料表按它排序;少数化学式有两项,如石墨和金刚石的 <c>C</c>)。</param>
    /// <param name="Name">材料的英文名称,如 <c>aluminum</c>。</param>
    /// <param name="ChineseName">材料的中文名称,如 <c>铝</c>;材料表中没有时为空。</param>
    /// <param name="Density">密度,g/cm³,设备的文本格式 (三位小数),如 <c>2.700</c>。</param>
    /// <param name="ZRatio">Z 比,设备的文本格式 (三位小数)。</param>
    /// <param name="ZRatioEstablished">Z 比是否已确定;手册中标 * 的材料为 false,Z 比按 1.000。</param>
    public sealed record MaterialLibraryEntry(string Formula, string Name, string ChineseName, string Density, string ZRatio, bool ZRatioEstablished);
}
