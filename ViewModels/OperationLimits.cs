using System;
using Zevision.Localization;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 长时间设备操作的时限,保存/恢复/修改配置共用。
    /// </summary>
    public static class OperationLimits
    {
        /// <summary>
        /// 每个阶段的时限 (不含等待用户确认的时间)。一次 ? 读取约 10 秒:
        /// <list type="bullet">
        /// <item>保存:"读取设备配置"一个阶段,1 次读取。</item>
        /// <item>恢复:"读取设备配置" (1 次读取) 和"执行恢复" (每轮创建对象后各 1 次读取,加写入与校验) 两个阶段。
        /// 重建已删除膜层及其预熔步骤 (2 轮) 的执行阶段约 25 秒。</item>
        /// <item>修改:"写入设备"一个阶段,只有单条查询、写入与校验,不读取帮助表。</item>
        /// </list>
        /// </summary>
        public static readonly TimeSpan PhaseTimeLimit = TimeSpan.FromSeconds(60);

        /// <summary>
        /// 生成超时提示。
        /// </summary>
        public static string BuildTimeoutMessage(OperationTimeout timeout)
        {
            if (timeout is null)
            {
                throw new ArgumentNullException(nameof(timeout));
            }

            return Lang.T(
                $"操作超时:\"{timeout.Phase}\"阶段超过 {timeout.Budget.TotalSeconds:0} 秒仍未完成,已停止。已完成的修改不会回滚;设备可能仍在发送剩余数据。",
                $"Operation timed out: the \"{timeout.Phase}\" phase did not finish within {timeout.Budget.TotalSeconds:0} s and was stopped. Completed changes are not rolled back; the device may still send the rest of its data.");
        }
    }
}
