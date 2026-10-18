using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Zevision.Commands;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;

namespace Zevision.ViewModels.Editor
{
    /// <summary>
    /// 修改窗口的确认、结果和错误提示。只拼文字,不访问设备和界面,便于单独测试。
    /// <c>fileName</c> 参数为 null 表示实机数据 (不保存文件)。
    /// </summary>
    internal static class RecipeEditorMessages
    {
        /// <summary>提示中最多列出的项数。</summary>
        public const int MaxListed = 15;

        public static string Title => Lang.T("修改仪器配置", "Edit instrument configuration");

        public static string LogSource => Lang.T("修改配置", "Edit configuration");

        public static string WritePhase => Lang.T("写入设备", "Writing to the device");

        public static string PageMayDiffer => Lang.T("页面可能与设备不一致,请关闭窗口后重新打开。", "The page may not match the device; close the window and open it again.");

        /// <summary>材料、膜系、膜层、工艺页顶部的说明。</summary>
        public static string ObjectChangeHint(bool toFile) => toFile
            ? Lang.T("新建/删除同时作用于设备和配置文件 (文件立即保存)", "New/Delete act on the device and the configuration file (saved immediately)")
            : Lang.T("新建/删除直接作用于设备,完成后重新读取本页", "New/Delete act on the device directly; this page is read again afterwards");

        public static string NoFreeNumber(string kind) => Lang.T($"没有空闲的{kind}编号。", $"No free {kind.ToLowerInvariant()} number.");

        public static string ConfirmCreate(string kind, string? id, string? fileName) => fileName != null
            ? Lang.T(
                $"在设备上新建{kind},并添加到配置文件 {fileName}?{Environment.NewLine}编号取设备和文件中都未使用的第一个;新{kind}使用设备的默认值,文件立即保存。",
                $"Create a {kind.ToLowerInvariant()} on the device and add it to the configuration file {fileName}?{Environment.NewLine}It gets the first number unused on both the device and in the file, and the device defaults; the file is saved immediately.")
            : Lang.T(
                $"在设备上新建{kind} {id}?{Environment.NewLine}新{kind}使用设备的默认值;新建后重新读取{kind}列表,它的全部参数会勾选,可以直接修改。",
                $"Create {kind.ToLowerInvariant()} {id} on the device?{Environment.NewLine}It gets the device defaults; the {kind.ToLowerInvariant()} list is read again afterwards and all its settings are checked so you can edit them.");

        /// <summary>新建膜系/膜层/工艺时程序设置的默认值的说明 (确认框中);<paramref name="first"/> 为第一个材料/膜系/膜层,没有时为 null。</summary>
        public static string NewObjectDefaults(char group, string? first) => group switch
        {
            'F' => first == null
                ? Lang.T("没有材料,材料 ID 保持设备默认值。膜系的全部参数会勾选,可以直接修改。", "There is no material, so the material ID keeps the device default. All the film's settings are checked so you can edit them.")
                : Lang.T($"材料 ID 设为第一个材料 {first};膜系的全部参数会勾选,可以直接修改。", $"The material ID is set to the first material {first}; all the film's settings are checked so you can edit them."),
            'L' => first == null
                ? Lang.T("没有膜系,膜层不添加膜系 (之后可以用\"添加速率/厚度设置\"添加)。", "There is no film, so no film is added to the layer (add one later with \"Add rate/thickness\").")
                : Lang.T($"同时为膜层添加一项膜系 (第一个膜系 {first}),膜层的全部参数 (名称、膜系、速率、最终厚度) 会勾选,可以直接修改。", $"A film entry (the first film {first}) is added to the layer, and all its settings (name, film, rate, final thickness) are checked so you can edit them."),
            'P' => first == null
                ? Lang.T("没有膜层,工艺不添加膜层 (之后可以在工艺标题栏添加)。", "There is no layer, so no layer is added to the process (add one later in the process header).")
                : Lang.T($"同时为工艺添加第一个膜层 {first},工艺的全部参数会勾选,可以直接修改。", $"The first layer {first} is added to the process, and all its settings are checked so you can edit them."),
            _ => string.Empty,
        };

        public static string ConfirmDelete(string kind, string display, string? fileName) =>
            (fileName != null
                ? Lang.T($"从设备和配置文件 {fileName} 中删除{kind} {display}?文件立即保存。", $"Delete {kind.ToLowerInvariant()} {display} from the device and the configuration file {fileName}? The file is saved immediately.")
                : Lang.T($"从设备上删除{kind} {display}?", $"Delete {kind.ToLowerInvariant()} {display} from the device?"))
            + Environment.NewLine + Lang.T("删除不能撤销。", "This cannot be undone.");

        /// <summary>
        /// 删除被引用对象时的提示:列出引用它的设置项 (由 <paramref name="describe"/> 给出所属对象、名称、ID 和值),请用户先改掉。
        /// <paramref name="allowNone"/>:引用可以改为 0 (未选择);工艺的引用 (活动工艺、链接工艺) 不行。
        /// </summary>
        public static string ReferencedObject(string kind, string display, IReadOnlyList<RecipeEntry> references, Func<RecipeEntry, string> describe, bool allowNone)
        {
            var none = allowNone ? (Chinese: " 或 0 (未选择)", English: " or 0 (none)") : (Chinese: string.Empty, English: string.Empty);
            var sb = new StringBuilder(Lang.T(
                $"不能删除{kind} {display}:以下设置引用了它。请先把这些设置改为其他{kind}{none.Chinese}并保存,再删除。",
                $"Cannot delete {kind.ToLowerInvariant()} {display}: the settings below reference it. Change them to another {kind.ToLowerInvariant()}{none.English} and save first, then delete it."));
            foreach (var entry in references.Take(MaxListed))
            {
                sb.AppendLine().Append("  ").Append(describe(entry));
            }

            if (references.Count > MaxListed)
            {
                sb.AppendLine().Append(Lang.T($"  … 共 {references.Count} 项", $"  … {references.Count} in total"));
            }

            return sb.ToString();
        }

        public static string ObjectChangeMismatch(string objectId, IReadOnlyList<string> missing, IReadOnlyList<string> extra)
        {
            string List(IReadOnlyList<string> ids, string empty) => ids.Count == 0 ? empty : string.Join(", ", ids);
            return Lang.T(
                $"设备上的结果与预期不同 ({objectId}):{Environment.NewLine}应有但没有: {List(missing, "无")}{Environment.NewLine}多出: {List(extra, "无")}{Environment.NewLine}页面已按设备当前数据重新生成。",
                $"The result on the device differs from what was expected ({objectId}):{Environment.NewLine}Missing: {List(missing, "none")}{Environment.NewLine}Unexpected: {List(extra, "none")}{Environment.NewLine}The page was rebuilt from the current device data.");
        }

        public static string ObjectChanged(CommandActions action, string kind, string objectId, bool onDevice) =>
            action == CommandActions.Add
                ? Lang.T($"已新建{kind} {objectId}", $"Created {kind.ToLowerInvariant()} {objectId}")
                : onDevice
                    ? Lang.T($"已删除{kind} {objectId}", $"Deleted {kind.ToLowerInvariant()} {objectId}")
                    : Lang.T($"已从文件中删除{kind} {objectId} (设备上没有该对象)", $"Deleted {kind.ToLowerInvariant()} {objectId} from the file (not on the device)");

        /// <summary>操作完成的提示:保存的配置说明文件已保存,实机数据说明已重新读取。</summary>
        public static string Done(string done, bool savedFile) =>
            done + (savedFile ? Lang.T(",配置文件已保存。", "; the configuration file was saved.") : Lang.T(",已重新读取。", "; the list was read again."));

        public static string FileSaveFailed(string done, string error) =>
            done + Lang.T(",但保存文件失败: ", ", but saving the file failed: ") + error;

        public static string DefaultsFailed(string done, string error) =>
            done + Lang.T(",但设置默认值失败: ", ", but setting the defaults failed: ") + error;

        /// <summary>删除列表中间一项时,后面某一项不在配置中,无法前移。</summary>
        public static string CannotShift(string itemId) =>
            Lang.T($"配置中没有 {itemId},无法前移。", $"{itemId} is not in the configuration, so the items cannot be moved up.");

        /// <summary>空列表在分组中的说明 (膜层的膜系列表:速率和最终厚度属于其中的每一项)。</summary>
        public static string EmptyListHint => Lang.T(
            "这个膜层还没有速率和厚度设置。设备上膜层的速率、最终厚度属于膜层中的每个膜系:点右侧按钮添加一项,然后勾选修改膜系、速率和最终厚度",
            "This layer has no rate or thickness yet. On the device, a layer's rate and final thickness belong to each film in the layer: add one with the button, then check and edit its film, rate and final thickness");

        /// <summary>空列表的添加按钮。</summary>
        public static string AddFirstListItem => Lang.T("添加速率/厚度设置", "Add rate/thickness");

        /// <summary>操作被取消或超时:设备上可能已经执行。</summary>
        public static string Interrupted(string reason, bool append) =>
            reason + Environment.NewLine + (append
                ? Lang.T("设备上可能已经追加,请关闭窗口后重新打开以确认。", "It may already have been appended on the device; close the window and open it again to check.")
                : Lang.T("设备上可能已经执行,请关闭窗口后重新打开以确认。", "It may already have been done on the device; close the window and open it again to check."));

        /// <summary>删除列表一项的确认。<paramref name="shiftCount"/> 为删除中间一项时要前移写入的项数。</summary>
        public static string ConfirmListRemove(string owner, string listName, string itemId, string display, int shiftCount, bool toFile)
        {
            var text = Lang.T($"从设备上删除{owner}的{listName} {display} ({itemId})?", $"Remove {listName.ToLowerInvariant()} {display} ({itemId}) of {owner} from the device?");
            if (shiftCount > 0)
            {
                text += Environment.NewLine + Lang.T(
                    $"设备只能删除列表的最后一项:后面的 {shiftCount} 项依次前移 (写入并校验),再删除最后一项。",
                    $"The device can only remove the last item: the {shiftCount} item(s) after it move up one place (written and verified), then the last item is removed.");
            }

            if (toFile)
            {
                text += Environment.NewLine + Lang.T("配置文件同时修改并立即保存;需要设备与文件中该列表的条数相同。", "The configuration file is changed and saved immediately; the list must have the same number of items on the device and in the file.");
            }

            return text + Environment.NewLine + Lang.T("删除不能撤销。", "This cannot be undone.");
        }

        public static string ListAppended(string owner, string listName, string itemId) =>
            Lang.T($"{owner} 新增{listName} ({itemId})", $"{owner}: added {listName.ToLowerInvariant()} ({itemId})");

        public static string ListRemoved(string owner, string listName, string itemId) =>
            Lang.T($"{owner} 删除{listName} ({itemId})", $"{owner}: removed {listName.ToLowerInvariant()} ({itemId})");

        /// <summary>列表删除一项完成的提示。</summary>
        public static string ListRemovedInfo(string done, bool savedFile) =>
            Lang.T("已", "Done: ") + done + (savedFile
                ? Lang.T(",配置文件已保存。", "; the configuration file was saved.")
                : Lang.T(",已重新读取。", "; read again."));

        /// <summary>列表追加一项完成的提示。</summary>
        public static string ListChangedInfo(string done, bool savedFile) =>
            Lang.T("已", "Done: ") + done + (savedFile
                ? Lang.T(",配置文件已保存。", "; the configuration file was saved.")
                : Lang.T(",已重新读取,可以修改它的设置。", "; the tab was read again, you can edit it now."));

        public static string ConfirmSave(IReadOnlyList<RecipeItemViewModel> selected, string? path)
        {
            var sb = new StringBuilder();
            var modified = selected.Count(i => i.IsModified);
            sb.AppendLine(path != null
                ? Lang.T($"将把勾选的 {selected.Count} 项写入设备 ({modified} 项已修改),全部成功后保存到:", $"The {selected.Count} checked settings ({modified} modified) will be written to the device and, if all succeed, saved to:")
                : Lang.T($"将把勾选的 {selected.Count} 项写入设备 ({modified} 项已修改),不保存文件:", $"The {selected.Count} checked settings ({modified} modified) will be written to the device; no file is saved:"));

            if (path != null)
            {
                sb.AppendLine(path);
            }

            sb.AppendLine();
            foreach (var item in selected.OrderBy(i => RecipeRules.GroupRank(i.Id)).Take(MaxListed))
            {
                sb.AppendLine(item.IsModified
                    ? $"  {item.Id} {item.Label}: {item.OriginalValue} → {item.Value}"
                    : Lang.T($"  {item.Id} {item.Label}: {item.Value} (未改动)", $"  {item.Id} {item.Label}: {item.Value} (unchanged)"));
            }

            if (selected.Count > MaxListed)
            {
                sb.AppendLine(Lang.T($"  … 另有 {selected.Count - MaxListed} 项", $"  … {selected.Count - MaxListed} more"));
            }

            sb.AppendLine();
            sb.Append(path != null
                ? Lang.T("写入期间请勿操作仪器。遇到错误会立即停止,已写入的项不会回滚,文件不会被修改。是否继续?", "Do not operate the instrument while writing. Writing stops at the first error; settings already written are not rolled back and the file is not changed. Continue?")
                : Lang.T("写入期间请勿操作仪器。遇到错误会立即停止,已写入的项不会回滚。是否继续?", "Do not operate the instrument while writing. Writing stops at the first error; settings already written are not rolled back. Continue?"));
            return sb.ToString();
        }

        /// <summary>写入设备失败;<paramref name="cancelReason"/> 用于被取消或超时的情况。</summary>
        public static string WriteFailed(RestoreReport report, string cancelReason, bool toFile)
        {
            var done = Lang.T($"已写入 {report.WrittenCount}/{report.PlannedWriteCount} 项,已写入的项不会回滚", $"Wrote {report.WrittenCount}/{report.PlannedWriteCount} settings; they are not rolled back")
                + (toFile ? Lang.T(";文件未修改。", "; the file was not changed.") : "。");
            if (report.Cancelled)
            {
                return cancelReason + Environment.NewLine + done;
            }

            if (report.VerifyMismatches.Count > 0)
            {
                return Lang.T($"写入后读回校验不一致:{Environment.NewLine}  ", $"Read-back after writing does not match:{Environment.NewLine}  ")
                    + string.Join(Environment.NewLine + "  ", report.VerifyMismatches.Take(MaxListed))
                    + Environment.NewLine + done;
            }

            var target = report.FailedId != null ? Lang.T($"写入 {report.FailedId} 失败,已停止: ", $"Writing {report.FailedId} failed, stopped: ") : string.Empty;
            return target + report.Error + Environment.NewLine + done;
        }

        /// <summary>写入的结果。</summary>
        public static string SaveSummary(int writtenCount) =>
            Lang.T($"已写入设备并校验 {writtenCount} 项。", $"Wrote and verified {writtenCount} settings on the device.");

        public static string SavedTo(string path) => Lang.T($"配置已保存到 {path}", $"The configuration was saved to {path}");

        public static string SaveFileFailed(string error) => Lang.T($"设备已修改,但保存文件失败: {error}", $"The device was changed, but saving the file failed: {error}");

        /// <summary>窗口关闭后记入主窗口数据显示区的结果。</summary>
        public static string Completion(IReadOnlyList<string> changes, int writtenCount, string? fileName)
        {
            var parts = new List<string>(changes);
            if (writtenCount > 0)
            {
                parts.Add(Lang.T($"已写入并校验 {writtenCount} 项", $"wrote and verified {writtenCount} settings"));
            }

            return (fileName != null
                ? Lang.T($"修改配置 ({fileName}):", $"Edit configuration ({fileName}): ")
                : Lang.T("修改配置 (实机):", "Edit configuration (device): ")) + string.Join(Lang.T(";", "; "), parts);
        }
    }
}
