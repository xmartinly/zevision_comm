using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Zevision.Core.Configuration;
using Zevision.Core.Design;
using Zevision.Core.Localization;

namespace Zevision.ViewModels.Design
{
    /// <summary>工艺编辑窗口的提示文本 (纯函数,便于测试)。</summary>
    public static class DesignMessages
    {
        /// <summary>错误框最多列出的条数。</summary>
        public const int MaxListed = 20;

        /// <summary>写入 error.txt 时的来源。</summary>
        public static string LogSource => Lang.T("工艺编辑", "Process editor");

        /// <summary>检查不通过,不保存。</summary>
        public static string ValidationFailed(IReadOnlyList<string> errors) =>
            Lang.T("有以下问题,没有保存:", "Not saved; please correct the following:") + Environment.NewLine + List(errors);

        /// <summary>上传前的检查不通过 (设备上没有所选的传感器、输出源等)。</summary>
        public static string UploadChecksFailed(string? errors) =>
            Lang.T("已保存到本地,但不能上传:", "Saved locally, but cannot upload:") + Environment.NewLine + errors;

        /// <summary>上传内容中有不能写入的项 (正常情况下不会出现)。</summary>
        public static string CannotWrite(IReadOnlyList<RestoreSkip> skipped) =>
            Lang.T("已保存到本地,但以下项不能写入设备,没有上传:", "Saved locally, but these settings cannot be written to the device; nothing was uploaded:")
            + Environment.NewLine + List(skipped.Select(s => s.ToString()).ToList());

        /// <summary>已保存到本地 ("保存")。</summary>
        public static string Saved(string path) => Lang.T($"已保存到 {path}。", $"Saved to {path}.");

        /// <summary>保存了,没有上传 (取消、失败)。</summary>
        public static string SavedNotUploaded(string path) =>
            Lang.T($"已保存到 {path},未上传到设备。", $"Saved to {path}; not uploaded to the device.");

        /// <summary>上传前的确认:将新建和复用的对象;设备上已有同名的工艺时说明会再新建一个。</summary>
        /// <param name="process">要上传的工艺。</param>
        /// <param name="build">上传内容。</param>
        /// <param name="sameNamed">设备上同名的工艺,如 <c>P0202</c>。</param>
        public static string BuildConfirmation(ProcessDesign process, DesignBuild build, IReadOnlyList<string> sameNamed)
        {
            if (process is null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            if (build is null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            var lines = new List<string>
            {
                Lang.T($"已保存到本地。把工艺 {process.Name.Trim()} 上传到设备:", $"Saved locally. Uploading process {process.Name.Trim()} to the device:"),
                Lang.T("新建:", "New:"),
            };
            lines.AddRange(ObjectLines(build.Objects.Where(o => o.IsNew)));
            var reused = build.Objects.Where(o => !o.IsNew).ToList();
            if (reused.Count > 0)
            {
                lines.Add(Lang.T("复用 (设备上已有,名称和参数相同):", "Reused (already on the device with the same name and parameters):"));
                lines.AddRange(ObjectLines(reused));
            }

            if (sameNamed != null && sameNamed.Count > 0)
            {
                lines.Add(Lang.T(
                    $"设备上已有同名的工艺 {string.Join(", ", sameNamed)},上传后会再新建一个 (原有的不修改)。",
                    $"The device already has processes of this name ({string.Join(", ", sameNamed)}); another one is created (the existing ones are not changed)."));
            }

            lines.Add(Lang.T(
                $"共写入 {build.Recipe.Entries.Count} 项设置。设备上已有的对象不修改,不设为活动工艺。",
                $"{build.Recipe.Entries.Count} settings are written. Existing objects are not changed and the process is not made active."));
            lines.Add(Lang.T("上传需要约 30 秒 (新建后重新读取设备配置两次)。现在上传吗?", "The upload takes about 30 s (the device configuration is read twice after creating). Upload now?"));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>上传失败的原因和失败时已完成的数量 (随后删除本次新建的对象,见 <see cref="UploadFailedRolledBack"/>)。</summary>
        public static string UploadFailed(RestoreReport report, string cancelReason)
        {
            if (report is null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var done = Lang.T(
                $"失败时已新建 {report.CreatedCount} 个对象/条目,已写入 {report.WrittenCount}/{report.PlannedWriteCount} 项。",
                $"When it failed, {report.CreatedCount} objects/entries had been created and {report.WrittenCount}/{report.PlannedWriteCount} settings written.");
            string reason;
            if (report.Cancelled)
            {
                reason = cancelReason;
            }
            else if (report.FailedId != null && !report.FailedDuringCreate)
            {
                reason = Lang.T($"写入 {report.FailedId} 失败: {report.Error}", $"Writing {report.FailedId} failed: {report.Error}");
            }
            else if (report.VerifyMismatches.Count > 0)
            {
                reason = Lang.T("以下项读回的值与输入的不同:", "These settings read back differently from what was entered:") + Environment.NewLine + List(report.VerifyMismatches);
            }
            else
            {
                reason = report.Error ?? Lang.T("未知错误。", "Unknown error.");
            }

            return reason + Environment.NewLine + done;
        }

        /// <summary>新建对象后仍有不能写入的项 (如新对象的默认列表与预期不同):上传失败。</summary>
        public static string UploadSkipped(IReadOnlyList<RestoreSkip> skipped) =>
            Lang.T("新建对象后以下项仍不能写入设备:", "These settings still cannot be written after creating the objects:")
            + Environment.NewLine + List((skipped ?? Array.Empty<RestoreSkip>()).Select(s => s.ToString()).ToList());

        /// <summary>上传失败后删除本次新建的对象:窗口底部和数据显示区的一行。</summary>
        /// <param name="process">工艺。</param>
        /// <param name="clean">本次新建的对象都已从设备删除 (或没有新建)。</param>
        public static string UploadRolledBack(ProcessDesign process, bool clean) =>
            clean
                ? Lang.T($"工艺 {DesignRules.DisplayName(process?.Name)} 上传失败,本次新建的对象已从设备删除。", $"Uploading process {DesignRules.DisplayName(process?.Name)} failed; the objects it created were deleted from the device.")
                : Lang.T($"工艺 {DesignRules.DisplayName(process?.Name)} 上传失败,本次新建的对象没有全部删除,设备上可能仍有部分对象。", $"Uploading process {DesignRules.DisplayName(process?.Name)} failed; not all objects it created could be deleted, so some may still be on the device.");

        /// <summary>上传失败的错误框:原因,以及删除本次新建的对象的结果。</summary>
        /// <param name="failure">上传失败的原因。</param>
        /// <param name="created">本次上传要新建的对象。</param>
        /// <param name="removed">已从设备删除的对象。</param>
        /// <param name="error">删除时的错误 (读取设备失败、删除被拒绝、取消、超时);没有时为 null。</param>
        /// <param name="remaining">删除后重新读取时仍在设备上的对象;没有读取到时为 null。</param>
        public static string UploadFailedRolledBack(string failure, IReadOnlyList<DesignObject> created, IReadOnlyList<DesignObject> removed, string? error, IReadOnlyList<DesignObject>? remaining)
        {
            var lines = new List<string> { Lang.T("上传失败:", "Upload failed:"), failure, string.Empty };
            if (removed != null && removed.Count > 0)
            {
                lines.Add(Lang.T("已删除本次上传在设备上新建的对象: ", "Deleted the objects this upload created on the device: ") + ObjectList(removed) + Lang.T("。", "."));
            }
            else if (error == null)
            {
                lines.Add(Lang.T("设备上没有本次上传新建的对象。", "The device has no objects created by this upload."));
            }

            if (error != null)
            {
                lines.Add(Lang.T("删除本次上传新建的对象时出错: ", "Deleting the objects this upload created failed: ") + error);
            }

            var left = remaining ?? (error != null ? (created ?? Array.Empty<DesignObject>()).Except(removed ?? Array.Empty<DesignObject>()).ToList() : Array.Empty<DesignObject>());
            if (left.Count > 0)
            {
                lines.Add(Lang.T("以下对象可能仍在设备上,可在“设备”页刷新后删除: ", "These objects may still be on the device; refresh the \"Device\" page and delete them there: ") + ObjectList(left) + Lang.T("。", "."));
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>删除一个对象被设备拒绝。</summary>
        public static string DeleteObjectFailed(DesignObject target, string? error)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return Lang.T($"删除 {CommandText.ObjectKind(target.Group)} {target.Id} 失败: {error}", $"Deleting {CommandText.ObjectKind(target.Group)} {target.Id} failed: {error}");
        }

        /// <summary>上传成功 (结果文本和数据显示区)。</summary>
        public static string Uploaded(ProcessDesign process, DesignBuild build)
        {
            if (process is null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            if (build is null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            return Lang.T($"已上传工艺 {process.Name.Trim()}:", $"Uploaded process {process.Name.Trim()}: ") + Objects(build);
        }

        /// <summary>
        /// 保存在工艺中的上传记录,如 <c>2026-10-22 15:30 P0209 (M0B, F0B, L0A, L03*)</c>:工艺和用到的对象,复用的对象带 <c>*</c>。
        /// </summary>
        public static string UploadRecord(DesignBuild build, DateTime time)
        {
            if (build is null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            var process = build.Objects.Last(o => o.Group == 'P').Id;
            var others = build.Objects.Where(o => o.Group != 'P').Select(o => o.IsNew ? o.Id : o.Id + "*");
            return time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " " + process + " (" + string.Join(", ", others) + ")";
        }

        /// <summary>材料被工艺使用,不能删除。</summary>
        public static string MaterialInUse(string material, IReadOnlyList<string> processes) =>
            Lang.T($"材料 {material} 被以下工艺使用,不能删除:", $"Material {material} is used by these processes and cannot be deleted:")
            + Environment.NewLine + string.Join(", ", processes);

        /// <summary>
        /// 删除材料:是否同时删除设备上的数据 (是/否/取消)。列出按最近读取的设备内容将删除的对象 (同名的材料、引用它的膜系、
        /// 用到这些膜系的膜层);选"是"时重新读取设备,对象相同就不再确认。
        /// </summary>
        /// <param name="material">要删除的材料。</param>
        /// <param name="preview">按最近读取的设备内容找出的对象。</param>
        public static string DeleteMaterialQuestion(MaterialDesign material, DeviceDeletion preview)
        {
            if (material is null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            if (preview is null)
            {
                throw new ArgumentNullException(nameof(preview));
            }

            var name = DesignRules.DisplayName(material.Name);
            var lines = new List<string>
            {
                Lang.T($"删除材料 {name}。同时删除设备上的对应数据吗?", $"Delete material {name}. Also delete the matching data on the device?"),
            };
            if (preview.Objects.Count == 0)
            {
                lines.Add(Lang.T("按最近读取的设备内容,设备上没有它的数据。", "According to the last read of the device, it has no data of this material."));
            }
            else
            {
                lines.Add(Lang.T("按最近读取的设备内容,设备上将删除:", "According to the last read of the device, these objects will be deleted on the device:"));
                lines.AddRange(ObjectLines(preview.Objects));
                if (preview.IsBlocked)
                {
                    lines.Add(Lang.T("但设备上的以下设置引用了它们,选“是”时不会删除:", "But these settings on the device refer to them, so \"Yes\" deletes nothing:"));
                    lines.AddRange(preview.References.Take(5).Select(r => "  " + r));
                }
            }

            lines.Add(string.Empty);
            lines.Add(Lang.T("是: 重新读取设备 (约 8 秒) 后删除设备上的数据和本地材料;设备上的内容有变化时再确认一次", "Yes: read the device again (about 8 s), then delete the device data and the local material; asks again if the device has changed"));
            lines.Add(Lang.T("否: 只删除本地材料", "No: delete the local material only"));
            lines.Add(Lang.T("取消: 不删除", "Cancel: do not delete"));
            lines.Add(string.Empty);
            lines.Add(Lang.T("本地的删除在保存后写入文件;设备上的删除不能撤销。", "The local deletion is written to the files when saving; the deletion on the device cannot be undone."));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>删除没有名称的工艺 (只在本地)。</summary>
        public static string DeleteLocalProcessQuestion(ProcessDesign process) =>
            Lang.T($"删除工艺 {DesignRules.DisplayName(process?.Name)} 吗?本地的删除在保存后写入文件。", $"Delete process {DesignRules.DisplayName(process?.Name)}? The local deletion is written to the files when saving.");

        /// <summary>读取设备失败,不能同时删除设备上的同名工艺:是否只删除本地。</summary>
        public static string DeleteProcessLocallyQuestion(ProcessDesign process, string error) =>
            Lang.T(
                $"不能读取设备,无法同时删除设备上的同名工艺:{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}只删除本地的工艺 {DesignRules.DisplayName(process?.Name)} 吗?设备上的同名工艺不受影响。",
                $"The device cannot be read, so processes of the same name on the device cannot be deleted:{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}Delete only the local process {DesignRules.DisplayName(process?.Name)}? Processes of this name on the device are not affected.");

        /// <summary>只删除了本地的工艺 (没有读取设备)。</summary>
        public static string ProcessDeletedLocally(ProcessDesign process) =>
            Lang.T($"已删除本地的工艺 {DesignRules.DisplayName(process?.Name)},设备上的同名工艺不受影响。本地的删除在保存后写入文件。", $"Deleted the local process {DesignRules.DisplayName(process?.Name)}; processes of this name on the device are not affected. The local deletion is written to the files when saving.");

        /// <summary>开始删除前读取设备失败 (设备没有改变)。</summary>
        /// <param name="error">原因。</param>
        /// <param name="localKept">有本地对象时的说明 (也未删除);没有时为 null。</param>
        public static string DeviceNotRead(string error, string? localKept) =>
            Lang.T("不能读取设备,没有删除:", "The device cannot be read; nothing was deleted: ") + error + (localKept == null ? string.Empty : " " + localKept);

        /// <summary>删除的本地材料,用于提示,如"材料 Al"。</summary>
        public static string MaterialSubject(MaterialDesign material) =>
            Lang.T("材料 ", "material ") + DesignRules.DisplayName(material?.Name);

        /// <summary>删除的本地工艺,用于提示,如"工艺 P1"。</summary>
        public static string ProcessSubject(ProcessDesign process) =>
            Lang.T("工艺 ", "process ") + DesignRules.DisplayName(process?.Name);

        /// <summary>"设备"页中删除的对象,用于提示,如"工艺 P0203 P3"。</summary>
        public static string DeviceObjectSubject(string id, string name) =>
            CommandText.ObjectKind((id ?? throw new ArgumentNullException(nameof(id)))[0]).ToLowerInvariant() + " " + id + " " + name;

        /// <summary>设备上的删除没有完成时,本地材料保留。</summary>
        public static string LocalMaterialKept => Lang.T("本地材料未删除。", "The local material was not deleted.");

        /// <summary>设备上的删除没有完成时,本地工艺保留。</summary>
        public static string LocalProcessKept => Lang.T("本地工艺未删除。", "The local process was not deleted.");

        /// <summary>本地材料或工艺在设备上的状态:同名对象的 ID,如"设备上: M10, F0B";没有时为"设备上没有"。</summary>
        public static string DeviceStatus(IReadOnlyList<string> ids) =>
            ids is null || ids.Count == 0 ? Lang.T("设备上没有", "Not on the device") : Lang.T("设备上: ", "On the device: ") + string.Join(", ", ids);

        /// <summary>设备上的其他设置引用了要删除的对象:不删除。</summary>
        /// <param name="subject">删除的是什么,如"材料 Al"。</param>
        /// <param name="deletion">要删除的对象和引用。</param>
        /// <param name="localKept">有本地对象时的说明 (也未删除);没有时为 null。</param>
        public static string DeleteBlocked(string subject, DeviceDeletion deletion, string? localKept)
        {
            if (deletion is null)
            {
                throw new ArgumentNullException(nameof(deletion));
            }

            var local = localKept == null ? string.Empty : Lang.T(" (本地也未删除)", " (not locally either)");
            return Lang.T(
                $"设备上的以下设置引用了{subject} 的数据,没有删除{local}。先在“修改”中删除或修改这些设置:",
                $"These settings on the device refer to the data of {subject}; nothing was deleted{local}. Delete or change them in \"Edit\" first:")
                + Environment.NewLine + List(deletion.References);
        }

        /// <summary>设备上没有这种材料的数据。</summary>
        public static string NothingOnDevice(MaterialDesign material) =>
            Lang.T(
                $"设备上没有材料 {DesignRules.DisplayName(material?.Name)} 的数据。本地材料在保存后删除。",
                $"The device has no data of material {DesignRules.DisplayName(material?.Name)}. The local material is deleted when saving.");

        /// <summary>删除设备上的对象前的确认:要删除的,以及删除工艺时保留的膜层。</summary>
        /// <param name="deletion">要删除的对象。</param>
        /// <param name="changed">设备上的内容与询问时列出的不同 (删除材料时的第二次确认)。</param>
        public static string DeleteDeviceConfirmation(DeviceDeletion deletion, bool changed = false)
        {
            if (deletion is null)
            {
                throw new ArgumentNullException(nameof(deletion));
            }

            var lines = new List<string>
            {
                changed
                    ? Lang.T("重新读取后设备上的内容有变化。将在设备上删除:", "The device has changed since it was last read. These objects will be deleted on the device:")
                    : Lang.T("将在设备上删除:", "These objects will be deleted on the device:"),
            };
            lines.AddRange(DeletionLines(deletion));
            lines.Add(Lang.T("设备上的删除不能撤销。继续吗?", "Deleting on the device cannot be undone. Continue?"));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>删除本地工艺前的确认:设备上一起删除的工艺和膜层 (设备上没有同名的工艺时只确认本地的删除)。</summary>
        public static string DeleteProcessConfirmation(ProcessDesign process, DeviceDeletion deletion)
        {
            if (process is null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            if (deletion is null)
            {
                throw new ArgumentNullException(nameof(deletion));
            }

            var name = DesignRules.DisplayName(process.Name);
            if (deletion.Objects.Count == 0)
            {
                return Lang.T(
                    $"删除工艺 {name} 吗?设备上没有同名的工艺。本地的删除在保存后写入文件。",
                    $"Delete process {name}? The device has no process of this name. The local deletion is written to the files when saving.");
            }

            var lines = new List<string>
            {
                Lang.T(
                    $"删除工艺 {name}。设备上的同名工艺和只被它使用的膜层一起删除:",
                    $"Delete process {name}. The processes of this name on the device and the layers only they use are deleted too:"),
            };
            lines.AddRange(DeletionLines(deletion));
            lines.Add(Lang.T(
                "设备上的删除不能撤销,本地的删除在保存后写入文件。继续吗?",
                "Deleting on the device cannot be undone; the local deletion is written to the files when saving. Continue?"));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>删除设备上的一个对象失败。</summary>
        /// <param name="target">删除失败的对象。</param>
        /// <param name="error">原因。</param>
        /// <param name="done">已经删除的对象。</param>
        /// <param name="localKept">有本地对象时的说明;没有时为 null。</param>
        public static string DeleteFailed(DesignObject target, string? error, IReadOnlyList<DesignObject> done, string? localKept)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var text = Lang.T(
                $"删除 {CommandText.ObjectKind(target.Group)} {target.Id} 失败: {error}",
                $"Deleting {CommandText.ObjectKind(target.Group)} {target.Id} failed: {error}");
            var deleted = done is null || done.Count == 0
                ? Lang.T("设备上没有删除任何对象。", "Nothing was deleted on the device.")
                : Lang.T("已删除: ", "Deleted: ") + string.Join(", ", done.Select(o => o.Id)) + Lang.T("。", ".");
            return text + Environment.NewLine + deleted + (localKept == null ? string.Empty : Lang.T(string.Empty, " ") + localKept);
        }

        /// <summary>删除指令成功,但重新读取后对象仍在。</summary>
        public static string StillOnDevice(IReadOnlyList<DesignObject> objects, string? localKept) =>
            Lang.T("设备回答删除成功,但重新读取后以下对象仍在设备上:", "The device accepted the deletion, but these objects are still on the device after reading it again:")
            + Environment.NewLine + ObjectList(objects ?? Array.Empty<DesignObject>())
            + (localKept == null ? string.Empty : Environment.NewLine + localKept);

        /// <summary>已从设备删除材料的数据 (本地材料随后删除)。</summary>
        public static string DeletedFromDevice(MaterialDesign material, DeviceDeletion deletion) =>
            Lang.T(
                $"已从设备删除材料 {DesignRules.DisplayName(material?.Name)} 的数据: ",
                $"Deleted the data of material {DesignRules.DisplayName(material?.Name)} from the device: ")
            + ObjectList(deletion?.Objects ?? Array.Empty<DesignObject>())
            + Lang.T("。本地材料在保存后删除。", ". The local material is deleted when saving.");

        /// <summary>已删除本地工艺 (和设备上的同名工艺、只被它使用的膜层)。</summary>
        public static string ProcessDeleted(ProcessDesign process, DeviceDeletion deletion)
        {
            var name = DesignRules.DisplayName(process?.Name);
            if (deletion is null || deletion.Objects.Count == 0)
            {
                return Lang.T(
                    $"已删除工艺 {name} (设备上没有同名的工艺)。本地的删除在保存后写入文件。",
                    $"Deleted process {name} (the device has no process of this name). The local deletion is written to the files when saving.");
            }

            return Lang.T($"已删除工艺 {name},设备上删除了: ", $"Deleted process {name}; deleted on the device: ")
                + ObjectList(deletion.Objects)
                + Lang.T("。本地的删除在保存后写入文件。", ". The local deletion is written to the files when saving.");
        }

        /// <summary>删除没有全部完成时已经删除的对象 (数据显示区)。</summary>
        public static string DeletedPartially(IReadOnlyList<DesignObject> done) =>
            Lang.T("删除没有完成,设备上已删除: ", "Deletion incomplete; deleted on the device: ") + ObjectList(done ?? Array.Empty<DesignObject>()) + Lang.T("。", ".");

        /// <summary>取消或超时时已经删除的对象。</summary>
        public static string DeletedSoFar(IReadOnlyList<DesignObject> done) =>
            done is null || done.Count == 0
                ? Lang.T("设备上没有删除任何对象。", "Nothing was deleted on the device.")
                : Lang.T("设备上已删除: ", "Deleted on the device: ") + ObjectList(done) + Lang.T("。", ".");

        /// <summary>"设备"页:已从设备删除。</summary>
        public static string DeletedOnDevice(DeviceDeletion deletion) =>
            Lang.T("已从设备删除: ", "Deleted from the device: ") + ObjectList(deletion?.Objects ?? Array.Empty<DesignObject>()) + Lang.T("。", ".");

        /// <summary>"设备"页:要删除的对象重新读取后已经不在。</summary>
        public static string AlreadyGone(string id) => Lang.T($"设备上已经没有 {id}。", $"{id} is no longer on the device.");

        /// <summary>新建和复用的对象,如 <c>新建 材料 M0B Al, 膜系 F0B Al;复用 膜层 L03 Al-AR-1</c>。</summary>
        private static string Objects(DesignBuild build)
        {
            string Join(IEnumerable<DesignObject> objects) =>
                string.Join(", ", objects.Select(o => CommandText.ObjectKind(o.Group) + " " + o.Id + " " + o.Name));

            var text = Lang.T("新建 ", "new ") + Join(build.Objects.Where(o => o.IsNew));
            var reused = build.Objects.Where(o => !o.IsNew).ToList();
            return reused.Count == 0 ? text : text + Lang.T(";复用 ", "; reused ") + Join(reused);
        }

        /// <summary>要删除的对象按类型分行;删除工艺时还有保留的膜层。</summary>
        private static IEnumerable<string> DeletionLines(DeviceDeletion deletion)
        {
            foreach (var line in ObjectLines(deletion.Objects))
            {
                yield return line;
            }

            if (deletion.Kept.Count > 0)
            {
                yield return Lang.T("保留 (其他工艺也在用):", "Kept (other processes use them):");
                foreach (var line in ObjectLines(deletion.Kept))
                {
                    yield return line;
                }
            }
        }

        /// <summary>对象的类型和 ID,如 <c>膜层 L02, 材料 M02</c>。</summary>
        private static string ObjectList(IEnumerable<DesignObject> objects) =>
            string.Join(", ", objects.Select(o => CommandText.ObjectKind(o.Group) + " " + o.Id));

        /// <summary>按类型分行列出对象,如 <c>  膜层: L0A Al-AR-1, L0B SiO2-AR-2</c>。</summary>
        private static IEnumerable<string> ObjectLines(IEnumerable<DesignObject> objects) =>
            objects.GroupBy(o => o.Group).Select(g => "  " + CommandText.ObjectKind(g.Key) + ": " + string.Join(", ", g.Select(o => o.Id + " " + o.Name)));

        private static string List(IReadOnlyList<string> items)
        {
            var lines = items.Take(MaxListed).ToList();
            if (items.Count > MaxListed)
            {
                lines.Add(Lang.T($"…共 {items.Count} 条", $"… {items.Count} in total"));
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}
