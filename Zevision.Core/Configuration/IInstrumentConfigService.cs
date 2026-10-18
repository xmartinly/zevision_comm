using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Communication;

namespace Zevision.Core.Configuration
{
    /// <summary>
    /// 读取设备配置 (只发送 <c>?</c> 和 <c>Q</c>,不修改设备)。
    /// </summary>
    public interface IInstrumentConfigReader
    {
        /// <summary>
        /// 发送 <c>?</c> 读取设备全部设置项的当前值。响应约 116 KB,115200 bps 下约需 10 秒。
        /// </summary>
        Task<Result<InstrumentSnapshot>> ReadSnapshotAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 用组帮助 (如 <c>?M</c>) 读取一个组的当前配置,按 <see cref="RecipeRules"/> 挑出可恢复的项 (与 <see cref="InstrumentSnapshot.ToRecipe"/> 相同)。
        /// 比完整的 <c>?</c> 快得多 (材料约 0.5 秒)。响应中没有该组的数量项 (如 <c>MFE</c>) 时视为解析失败。
        /// </summary>
        Task<Result<IReadOnlyList<RecipeEntry>>> ReadGroupAsync(char group, CancellationToken cancellationToken);

        /// <summary>
        /// 用 Q 指令查询多项的当前值。每 <see cref="InstrumentConfigReader.QueryBatchSize"/> 条合并为一帧
        /// (<c>QG01;QG02;…</c>),比逐条查询快得多。设备拒绝某一项 (如 <c>-A</c> 不存在) 时该项带错误说明,不影响其他项;
        /// 通讯失败时返回失败。
        /// </summary>
        Task<Result<IReadOnlyList<QueryValue>>> QueryAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 修改设备配置 (恢复、写入编辑的项、新建/删除对象、列表追加)。修改前都先确认设备型号与空闲状态。
    /// </summary>
    public interface IInstrumentConfigWriter
    {
        /// <summary>
        /// 执行恢复,顺序为:系统设置 (传感器、源等) → 创建缺失对象并重新读取、重新计划 →
        /// 材料 → 膜层 → 层 → 工艺 的设置 → 逐项读回校验。遇到第一个错误即停止。
        /// </summary>
        Task<RestoreReport> ExecuteRestoreAsync(InstrumentRecipe recipe, RestorePlan plan, IProgress<RestoreProgress>? progress, CancellationToken cancellationToken);

        /// <summary>
        /// 把编辑过的配置项写入设备 (修改配置功能):先逐项检查可写性和字符,再用 Q 指令确认设备型号 (G03) 与
        /// 空闲状态 (C09),然后按恢复顺序写入并逐项读回校验。遇到第一个错误即停止,不回滚。
        /// 不创建对象:要修改的项在设备上不存在时,设备会拒绝。
        /// </summary>
        Task<RestoreReport> ApplyEditsAsync(InstrumentRecipe recipe, IReadOnlyList<RecipeEntry> edits, IProgress<RestoreProgress>? progress, CancellationToken cancellationToken);

        /// <summary>
        /// 在设备上按编号新建对象 (如 <c>AF02</c>),<paramref name="defaultValue"/> 不为 null 时再按 <see cref="ObjectType.NewDefault"/>
        /// 设置默认引用 (如 <c>UF0202:1</c>,或先在列表末尾追加一项),写入后读回校验。
        /// 只在开始时确认一次设备型号 (G03) 与空闲状态 (C09)。结果由调用方重新读取该组来确认。
        /// </summary>
        /// <param name="type">对象类型。</param>
        /// <param name="objectId">新对象的 ID。</param>
        /// <param name="defaultValue">默认引用的值 (被引用对象的十进制编号);为 null 时不设置。</param>
        /// <param name="deviceName">配置所属的设备型号。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        Task<ObjectCreation> CreateObjectAsync(ObjectType type, string objectId, string? defaultValue, string deviceName, CancellationToken cancellationToken);

        /// <summary>
        /// 在设备上按编号删除对象 (如 <c>DM03</c>)。先确认设备型号与空闲状态。结果由调用方重新读取该组来确认。
        /// </summary>
        Task<Result> DeleteObjectAsync(string objectId, string deviceName, CancellationToken cancellationToken);

        /// <summary>
        /// 在列表末尾追加一项 (如工艺增加膜层:<c>AP020107 00</c>,读数量 <c>QP020107FE</c> 得到新条目 <c>P0201070n</c>);
        /// <paramref name="value"/> 不为 null 时再 <c>UP0201070n:值</c> 并读回校验 (膜系的预熔/后处理一项有多个设置,追加后使用设备默认值,传 null)。
        /// 先确认设备型号与空闲状态。
        /// </summary>
        /// <returns>成功时为新条目的 ID。</returns>
        Task<Result<string>> AppendListItemAsync(string listId, string? value, string deviceName, CancellationToken cancellationToken);

        /// <summary>
        /// 删除列表的最后一项 (如膜系的最后一条预熔:<c>DF010300</c>)。设备只支持从末尾删除。
        /// 先确认设备型号与空闲状态,删除前后读取数量 (<c>QF0103FE</c>) 确认少了一条。
        /// </summary>
        /// <returns>成功时为被删除的条目 ID,如 <c>F010302</c>。</returns>
        Task<Result<string>> RemoveLastListItemAsync(string listId, string deviceName, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 保存和恢复仪器配置:读取和修改设备。生成配置文件内容 (<see cref="InstrumentSnapshot.ToRecipe"/>) 和
    /// 恢复计划 (<see cref="RestorePlanner.Plan"/>) 不与设备通讯,不在这里。
    /// </summary>
    public interface IInstrumentConfigService : IInstrumentConfigReader, IInstrumentConfigWriter
    {
    }
}
