using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;

namespace Zevision.ViewModels.Operations
{
    /// <summary>
    /// 恢复配置的确认框与结果提示文本。纯函数,便于测试。
    /// </summary>
    public static class RestoreMessages
    {
        /// <summary>确认框中最多列出的创建/修改项数。</summary>
        public const int MaxListedChanges = 15;

        /// <summary>提示框中最多直接列出的无法写入项数,其余见数据显示区。</summary>
        public const int MaxListedSkips = 20;

        /// <summary>
        /// 恢复前的确认框:来源与当前设备、将创建的对象、将修改的项、不会写入的项。
        /// </summary>
        public static string BuildConfirmation(string path, InstrumentRecipe recipe, InstrumentSnapshot snapshot, RestorePlan plan)
        {
            if (recipe is null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (plan is null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var sb = new StringBuilder();
            sb.AppendLine(Lang.T("文件: ", "File: ") + Path.GetFileName(path));
            sb.AppendLine(Lang.T($"来源: {recipe.DeviceName} {recipe.FirmwareVersion}  序列号 {recipe.SerialNumber}  保存于 {recipe.SavedAt}", $"Source: {recipe.DeviceName} {recipe.FirmwareVersion}  Serial {recipe.SerialNumber}  Saved at {recipe.SavedAt}"));
            sb.AppendLine(Lang.T($"当前设备: {snapshot.DeviceName} {snapshot.FirmwareVersion}  序列号 {snapshot.SerialNumber}", $"Connected device: {snapshot.DeviceName} {snapshot.FirmwareVersion}  Serial {snapshot.SerialNumber}"));
            sb.AppendLine();
            if (plan.Creates.Count > 0)
            {
                sb.AppendLine(Lang.T($"设备上缺少、将重新创建 {plan.Creates.Count} 个对象/条目:", $"{plan.Creates.Count} objects/entries are missing on the device and will be re-created:"));
                AppendLimited(sb, plan.Creates.Select(c => $"{c.Kind} {c.Id} ({c.Command})").ToList(), MaxListedChanges, Lang.T("个", ""));
                sb.AppendLine(Lang.T("创建后会重新读取设备 (每轮约 10 秒);新对象的子列表 (如预熔步骤) 会在下一轮补齐,然后再写入设置。", "After creating, the device is read again (about 10 s per round); sub-lists of new objects (such as pre-condition steps) are completed in the next round before the settings are written."));
                sb.AppendLine();
            }

            sb.AppendLine(Lang.T($"将修改已有对象的 {plan.Writes.Count} 项,{plan.UnchangedCount} 项与设备相同,跳过 {plan.Skipped.Count} 项。", $"Will change {plan.Writes.Count} settings of existing objects; {plan.UnchangedCount} already match; {plan.Skipped.Count} skipped."));
            foreach (var warning in plan.Warnings)
            {
                sb.AppendLine(Lang.T("注意: ", "Note: ") + warning);
            }

            sb.AppendLine();
            AppendLimited(sb, plan.Writes.Select(w => $"{w.Id} {w.Description}: {w.CurrentValue} → {w.Value}").ToList(), MaxListedChanges, Lang.T("项", ""));
            sb.AppendLine();
            if (plan.Skipped.Count > 0)
            {
                AppendSkips(sb, Lang.T("以下项不会写入:", "The following settings will not be written:"), plan.Skipped);
                sb.AppendLine();
            }

            sb.Append(Lang.T("恢复期间请勿操作仪器。遇到错误会立即停止,已写入的项不会回滚。是否继续?", "Do not operate the instrument while restoring. Restoring stops at the first error; settings already written are not rolled back. Continue?"));
            return sb.ToString();
        }

        /// <summary>
        /// 恢复完成但有项无法写入时的提示:概要,以及创建对象后仍无法写入的项 (ID、说明、文件中的值、原因)。
        /// 全部写入时不弹窗,只记录 <see cref="BuildSummary"/>。
        /// </summary>
        public static string BuildSuccess(RestoreReport report)
        {
            if (report is null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var sb = new StringBuilder(BuildSummary(report)).AppendLine();
            if (report.AdditionalSkipped.Count > 0)
            {
                sb.AppendLine();
                AppendSkips(sb, Lang.T($"以下 {report.AdditionalSkipped.Count} 项无法写入 (ID 说明 = 文件中的值  (原因)):", $"The following {report.AdditionalSkipped.Count} settings could not be written (ID description = value in the file  (reason)):"), report.AdditionalSkipped);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>恢复成功的一行概要。</summary>
        public static string BuildSummary(RestoreReport report)
        {
            if (report is null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            return report.CreatedCount > 0
                ? Lang.T($"恢复完成:创建 {report.CreatedCount} 个对象/条目,写入并校验 {report.WrittenCount} 项。", $"Restore complete: created {report.CreatedCount} objects/entries, wrote and verified {report.WrittenCount} settings.")
                : Lang.T($"恢复完成:写入并校验 {report.WrittenCount} 项。", $"Restore complete: wrote and verified {report.WrittenCount} settings.");
        }

        /// <summary>
        /// 恢复失败时的提示;成功时返回 null。
        /// </summary>
        /// <param name="report">执行结果。</param>
        /// <param name="cancelReason">被取消时的原因 (用户取消或超时说明)。</param>
        public static string? BuildFailure(RestoreReport report, string cancelReason)
        {
            if (report is null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var done = Lang.T($"已创建 {report.CreatedCount} 个对象/条目,已写入 {report.WrittenCount}/{report.PlannedWriteCount} 项,已完成的修改不会回滚。", $"Created {report.CreatedCount} objects/entries, wrote {report.WrittenCount}/{report.PlannedWriteCount} settings; completed changes are not rolled back.");
            if (report.Cancelled)
            {
                return cancelReason + Environment.NewLine + done;
            }

            if (report.FailedDuringCreate)
            {
                return report.Error + Environment.NewLine + done;
            }

            if (report.FailedId != null)
            {
                return Lang.T($"写入 {report.FailedId} 失败,已停止: {report.Error}{Environment.NewLine}{done}", $"Writing {report.FailedId} failed, stopped: {report.Error}{Environment.NewLine}{done}");
            }

            if (report.VerifyMismatches.Count > 0)
            {
                return Lang.T($"已写入 {report.WrittenCount} 项,但有 {report.VerifyMismatches.Count} 项读回值与文件不一致,详见数据显示区。", $"Wrote {report.WrittenCount} settings, but {report.VerifyMismatches.Count} read back differently from the file; see the data display.");
            }

            return report.Error;
        }

        private static void AppendSkips(StringBuilder sb, string heading, IReadOnlyList<RestoreSkip> skips)
        {
            sb.AppendLine(heading);
            AppendLimited(sb, skips.Select(s => s.ToString()).ToList(), MaxListedSkips, Lang.T("项,见数据显示区", ", see the data display"));
        }

        private static void AppendLimited(StringBuilder sb, IReadOnlyList<string> lines, int max, string unit)
        {
            foreach (var line in lines.Take(max))
            {
                sb.AppendLine("  " + line);
            }

            if (lines.Count > max)
            {
                sb.AppendLine(Lang.T($"  … 另有 {lines.Count - max} {unit}", $"  … {lines.Count - max} more{unit}"));
            }
        }
    }
}
