using System;
using System.Collections.Generic;
using System.Globalization;
using Zevision.Commands;
using Zevision.Communication;

namespace Zevision.Core.Configuration
{
    /// <summary>新建对象的结果。</summary>
    /// <param name="Error">新建失败的原因;成功时为 null。</param>
    /// <param name="DefaultsError">对象已新建,但默认引用没有设置成功的原因;成功或不需要设置时为 null。</param>
    public sealed record ObjectCreation(string? Error, string? DefaultsError);

    /// <summary>批量查询中一项的结果。</summary>
    /// <param name="Id">命令 ID。</param>
    /// <param name="Value">当前值;查询失败时为 null。</param>
    /// <param name="Error">设备拒绝的原因;成功时为 null。</param>
    public sealed record QueryValue(string Id, string? Value, string? Error);

    /// <summary>恢复计划中的一次写入。</summary>
    /// <param name="Id">命令 ID。</param>
    /// <param name="Value">要写入的值 (来自文件)。</param>
    /// <param name="CurrentValue">设备当前值。</param>
    /// <param name="Description">说明。</param>
    public sealed record RestoreWrite(string Id, string Value, string CurrentValue, string Description)
    {
        /// <summary>写入指令,如 <c>UF0101:L1</c> (由 <see cref="ZevisionCommandSet.Update"/> 生成)。</summary>
        public string Command => ZevisionCommandSet.Update(Id, Value);
    }

    /// <summary>恢复时需要重新创建的列表对象或子列表条目。</summary>
    /// <param name="Id">创建后应出现的对象 ID,如 <c>M03</c> 或 <c>F010302</c>。</param>
    /// <param name="Kind">对象类型,如"材料"。</param>
    /// <param name="Command">创建指令,如 <c>AM03</c> (指定编号) 或 <c>AF010300</c> (追加到子列表末尾)。</param>
    public sealed record RestoreCreate(string Id, string Kind, string Command);

    /// <summary>文件中被跳过 (无法写入) 的一项、它在文件中的值及原因。</summary>
    public sealed class RestoreSkip
    {
        /// <summary>由文件中的配置项创建跳过项。</summary>
        public RestoreSkip(RecipeEntry entry, string reason)
        {
            if (entry is null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            Id = entry.Id;
            Value = entry.Value;
            Description = entry.Description;
            Reason = reason;
        }

        /// <summary>命令 ID。</summary>
        public string Id { get; }

        /// <summary>文件中的值 (未能写入设备的数据)。</summary>
        public string Value { get; }

        /// <summary>说明。</summary>
        public string Description { get; }

        /// <summary>原因。</summary>
        public string Reason { get; }

        /// <summary>单行显示,如 <c>F01030301 Start Ramp/Rise time = 0:00:30  (原因)</c>。</summary>
        public override string ToString() =>
            string.IsNullOrEmpty(Description)
                ? $"{Id} = {Value}  ({Reason})"
                : $"{Id} {Description} = {Value}  ({Reason})";
    }

    /// <summary>
    /// 恢复计划:只包含与设备当前值不同的项,按依赖顺序排列。生成计划不会与设备通讯。
    /// </summary>
    /// <param name="Creates">
    /// 文件中有、设备上已被删除的对象,恢复时先按依赖顺序创建。
    /// 它们的设置项要等创建后重新读取设备才能确定写入哪些,因此不在 <paramref name="Writes"/> 中。
    /// </param>
    /// <param name="Writes">需要写入的项。</param>
    /// <param name="UnchangedCount">与设备当前值相同、无需写入的项数。</param>
    /// <param name="Skipped">跳过的项。</param>
    /// <param name="Warnings">需要用户注意但不阻止恢复的问题,如固件版本不同。</param>
    public sealed record RestorePlan(
        IReadOnlyList<RestoreCreate> Creates,
        IReadOnlyList<RestoreWrite> Writes,
        int UnchangedCount,
        IReadOnlyList<RestoreSkip> Skipped,
        IReadOnlyList<string> Warnings);

    /// <summary>恢复进度。</summary>
    /// <param name="Stage">阶段,如"写入"、"校验"。</param>
    /// <param name="Completed">已完成数。</param>
    /// <param name="Total">总数;为 0 表示进度未知。</param>
    public sealed record RestoreProgress(string Stage, int Completed, int Total);

    /// <summary>恢复结果。</summary>
    public sealed class RestoreReport
    {
        /// <summary>成功创建的对象数。</summary>
        public int CreatedCount { get; internal set; }

        /// <summary>成功写入的项数。</summary>
        public int WrittenCount { get; internal set; }

        /// <summary>计划写入的项数 (创建对象后重新计划的结果)。</summary>
        public int PlannedWriteCount { get; internal set; }

        /// <summary>创建或写入失败而停止时的对象/项 ID。</summary>
        public string? FailedId { get; internal set; }

        /// <summary>失败原因。</summary>
        public string? Error { get; internal set; }

        /// <summary>是否被用户取消。</summary>
        public bool Cancelled { get; internal set; }

        /// <summary>失败发生在创建对象 (及其后的重新读取、重新计划) 阶段,而不是写入阶段。</summary>
        public bool FailedDuringCreate { get; internal set; }

        /// <summary>读回校验时与期望值不一致的项 (描述文本)。</summary>
        public List<string> VerifyMismatches { get; } = new List<string>();

        /// <summary>创建对象后重新计划时跳过的项 (例如新对象的子列表条目数与文件不同)。</summary>
        public List<RestoreSkip> AdditionalSkipped { get; } = new List<RestoreSkip>();

        /// <summary>全部写入成功且校验一致。</summary>
        public bool IsSuccess => FailedId == null && Error == null && !Cancelled && VerifyMismatches.Count == 0;
    }
}
