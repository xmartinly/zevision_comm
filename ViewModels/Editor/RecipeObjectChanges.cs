using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;
using M = Zevision.ViewModels.Editor.RecipeEditorMessages;

namespace Zevision.ViewModels.Editor
{
    /// <summary>
    /// 修改配置窗口中立即作用于设备的修改:新建/删除对象 (材料、膜系、膜层、工艺),在列表末尾追加、删除列表中的一项。
    /// </summary>
    /// <remarks>
    /// 每次修改的流程相同:确认 → 发送指令 (可取消、有时限) → 重新读取该组 (设备状态以读取为准) → 核对结果 →
    /// 更新配置 (保存的配置写文件,见 <see cref="RecipeEditSession"/>) → 重新生成页面 → 在窗口底部显示结果;有错误时提示。
    /// 页面 (分组、重新生成、结果显示) 由修改配置窗口通过 <see cref="IRecipeChangePage"/> 提供。
    /// </remarks>
    internal sealed class RecipeObjectChanges
    {
        private readonly RecipeEditSession _session;
        private readonly RecipeDocument _document;
        private readonly IDialogService _dialogs;
        private readonly OperationRunner _operation;
        private readonly IRecipeChangePage _page;

        /// <summary>创建。</summary>
        /// <param name="session">修改的配置及其与设备 (和配置文件) 的同步。</param>
        /// <param name="dialogs">对话框。</param>
        /// <param name="operation">窗口的操作状态 (进行中、进度、取消),与保存共用。</param>
        /// <param name="page">修改配置窗口的页面。</param>
        public RecipeObjectChanges(RecipeEditSession session, IDialogService dialogs, OperationRunner operation, IRecipeChangePage page)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _document = session.Document;
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            _page = page ?? throw new ArgumentNullException(nameof(page));
        }

        private bool IsDeviceSource => _session.FilePath == null;

        private string? FileName => _session.FilePath == null ? null : Path.GetFileName(_session.FilePath);

        /// <summary>
        /// 在设备上按第一个空编号新建对象 (如 <c>AM0A</c>) 并设置默认引用 (<see cref="ObjectType.NewDefault"/>),
        /// 然后用组帮助 (<c>?M</c>) 重新读取该组并重新生成页面,新对象的全部参数勾选。
        /// 保存的配置:编号取设备和文件中都未使用的第一个 (读取设备后才能确定),新对象加入文件并立即保存。
        /// </summary>
        /// <param name="group">组字母:M、F、L 或 P。</param>
        public async Task CreateObjectAsync(char group)
        {
            var type = ObjectTypes.Find(group)
                ?? throw new ArgumentException(Lang.T($"{group} 组的对象不能新建。", $"Objects of group {group} cannot be created."), nameof(group));
            var title = Lang.T("新建", "New ") + type.Kind;
            string? id = null;
            if (IsDeviceSource)
            {
                id = type.NextFreeId(_document.ObjectIds(group));
                if (id == null)
                {
                    _dialogs.ShowError(title, M.NoFreeNumber(type.Kind));
                    return;
                }
            }

            var message = M.ConfirmCreate(type.Kind, id, FileName);
            if (type.NewDefault != null)
            {
                message += Environment.NewLine + M.NewObjectDefaults(group, DefaultReferenceLabel(type));
            }

            if (_dialogs.Confirm(title, message))
            {
                await ChangeObjectAsync(title, CommandActions.Add, type, id);
            }
        }

        /// <summary>
        /// 在设备上删除对象 (如 <c>DM03</c>),然后只重新读取该组 (当前页) 并重新生成页面。
        /// 对象被其他设置引用 (<see cref="RecipeRules.FindReferencesTo"/>) 时不删除,提示先修改这些设置。
        /// </summary>
        /// <param name="objectId">对象 ID,如 <c>M03</c>、<c>P0202</c>。</param>
        public async Task DeleteObjectAsync(string objectId)
        {
            var type = ObjectTypes.OfObjectId(objectId ?? throw new ArgumentNullException(nameof(objectId)))
                ?? throw new ArgumentException(Lang.T($"{objectId} 不是可删除的对象。", $"{objectId} is not an object that can be deleted."), nameof(objectId));
            var title = Lang.T("删除", "Delete ") + type.Kind;
            var name = _document.ObjectName(objectId);
            var display = objectId + (string.IsNullOrEmpty(name) ? string.Empty : " (" + name + ")");

            // 被引用时不删除:设备会把引用改掉 (实测删除材料后膜系的材料变为 0),先让用户把这些设置改成别的对象。
            var references = RecipeRules.FindReferencesTo(_document.Entries, objectId);
            if (references.Count > 0)
            {
                _dialogs.ShowError(title, M.ReferencedObject(type.Kind, display, references, DescribeReference, allowNone: type != ObjectTypes.Process));
                return;
            }

            if (_dialogs.Confirm(title, M.ConfirmDelete(type.Kind, display, FileName)))
            {
                await ChangeObjectAsync(title, CommandActions.Delete, type, objectId);
            }
        }

        /// <summary>
        /// 在设备上把一项追加到列表末尾 (如预熔 <c>AF010300</c>、膜层的膜系 <c>AL010200</c>、工艺的膜层 <c>AP02010700</c> 再写入),
        /// 然后重新读取该组 (如 <c>?F</c>) 并展开所属对象,新的一项出现后可以修改。不确认 (用户要求,2026-10-18):追加可以用"−"删除。
        /// </summary>
        /// <param name="listId">列表 ID,如 <c>F0103</c>、<c>L0102</c>、<c>P020107</c>。</param>
        /// <param name="listName">列表名称,如"预熔"。</param>
        /// <param name="value">要写入新条目的值 (如工艺添加的膜层编号);为 null 时使用设备默认值。</param>
        public Task AppendListEntryAsync(string listId, string listName, string? value)
        {
            if (listId is null)
            {
                throw new ArgumentNullException(nameof(listId));
            }

            var owner = ListOwner(listId);
            var title = Lang.T("新增", "Add ") + listName;
            return RunChangeAsync(title, appendInterrupted: true, async operation =>
            {
                if (!await CheckListCountAsync(listId, operation.Token))
                {
                    return null;
                }

                var appended = await _session.AppendListItemAsync(listId, value, operation.Token);
                var done = M.ListAppended(owner, listName, appended.IsSuccess ? appended.Value : string.Empty);
                return new DeviceChange(listId[0], appended.Error, done, M.ListChangedInfo(done, savedFile: !IsDeviceSource))
                {
                    ExpandKey = ObjectTypes.GroupKey(listId),
                    Apply = deviceGroup => _session.ApplyListAppend(listId, appended.Value, deviceGroup),
                };
            });
        }

        /// <summary>
        /// 在设备上删除列表的一项。设备只能删除最后一项 (如 <c>DF010300</c>):删除中间的第 <paramref name="number"/> 项时,
        /// 先把后面的项依次前移 (写入并校验,只用于每项只有一个值的列表,如工艺的膜层),再删除最后一项。
        /// 然后重新读取该组并展开所属对象。
        /// </summary>
        /// <param name="listId">列表 ID,如 <c>F0103</c>、<c>P020107</c>。</param>
        /// <param name="listName">列表名称,如"预熔"。</param>
        /// <param name="number">要删除的序号 (从 1 开始);为 null 时删除最后一项。</param>
        public async Task RemoveListEntryAsync(string listId, string listName, int? number)
        {
            if (listId is null)
            {
                throw new ArgumentNullException(nameof(listId));
            }

            var count = _document.ListItemCount(listId);
            if (count == 0)
            {
                return;
            }

            var owner = ListOwner(listId);
            var title = Lang.T("删除", "Remove ") + listName;
            var removing = DeviceIds.Item(listId, number ?? count);
            var shifted = new List<RecipeEntry>();
            for (var i = number ?? count; i < count; i++)
            {
                var nextId = DeviceIds.Item(listId, i + 1);
                var next = _document.Entries.FirstOrDefault(e => e.Id == nextId);
                if (next == null)
                {
                    _dialogs.ShowError(title, M.CannotShift(nextId));
                    return;
                }

                shifted.Add(new RecipeEntry { Id = DeviceIds.Item(listId, i), Value = next.Value, Description = next.Description });
            }

            var display = _page.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Id == removing)?.DisplayValue ?? string.Empty;
            if (!_dialogs.Confirm(title, M.ConfirmListRemove(owner, listName, removing, display, shifted.Count, toFile: !IsDeviceSource)))
            {
                return;
            }

            await RunChangeAsync(title, appendInterrupted: false, async operation =>
            {
                if (!await CheckListCountAsync(listId, operation.Token))
                {
                    return null;
                }

                // 删除中间一项:先把后面的项前移 (写入并校验),再删除最后一项。
                var report = shifted.Count == 0 ? null : await _session.ApplyEditsAsync(shifted, _operation.CreateProgress("…"), operation.Token);
                _operation.Text = title + "…";
                var removed = report != null && !report.IsSuccess
                    ? Result<string>.Fail(M.WriteFailed(report, OperationRunner.DescribeCancel(operation), toFile: !IsDeviceSource))
                    : await _session.RemoveLastListItemAsync(listId, operation.Token);

                var values = shifted.ToDictionary(e => e.Id, e => e.Value, StringComparer.Ordinal);
                var done = M.ListRemoved(owner, listName, removing);
                return new DeviceChange(listId[0], removed.Error, done, M.ListRemovedInfo(done, savedFile: !IsDeviceSource))
                {
                    ExpandKey = ObjectTypes.GroupKey(listId),
                    Apply = _ => _session.ApplyListRemove(removed.Value, values),
                };
            });
        }

        /// <summary>
        /// 新建/删除:(保存的配置先读取设备上的该组,确定编号) → 发送指令 (新建时同时设置默认引用) → 由 <see cref="FinishChangeAsync"/>
        /// 重新读取并核对结果 (新建的对象出现、删除的对象消失,且没有其他对象变化)。
        /// </summary>
        private Task ChangeObjectAsync(string title, CommandActions action, ObjectType type, string? objectId) =>
            RunChangeAsync(title, appendInterrupted: false, async operation =>
            {
                var token = operation.Token;
                if (!IsDeviceSource)
                {
                    _operation.Text = Lang.T($"读取设备上的{type.Kind}…", $"Reading {type.Kind.ToLowerInvariant()}s on the device…");
                }

                var current = await _session.ReadDeviceGroupAsync(type.Group, token);
                if (!current.IsSuccess)
                {
                    _page.ReportFailure(current.Error!); // 失败的 Result 总有原因
                    return null;
                }

                var before = RecipeDocument.ObjectIdsIn(current.Value, type.Group);
                var id = objectId ?? type.NextFreeId(_session.UsedObjectIds(type.Group, before));
                if (id == null)
                {
                    _page.ReportFailure(M.NoFreeNumber(type.Kind));
                    return null;
                }

                // 保存的配置中删除设备上没有的对象:只从文件中删除,不发指令,也不必重新读取。
                var onDevice = action == CommandActions.Add || before.Contains(id);
                string? error = null;
                string? defaultsError = null;
                if (onDevice && action == CommandActions.Add)
                {
                    // 新建时同时设置默认引用,重新读取的结果 (保存的配置写进文件的内容) 就是设好的。
                    _operation.Text = title + " " + id + "…";
                    var created = await _session.CreateObjectAsync(type, id, token);
                    error = created.Error;
                    defaultsError = created.DefaultsError;
                }
                else if (onDevice)
                {
                    _operation.Text = title + " " + id + "…";
                    error = (await _session.DeleteObjectAsync(id, token)).Error;
                }

                var done = M.ObjectChanged(action, type.Kind, id, onDevice);
                return new DeviceChange(type.Group, error, done, M.Done(done, savedFile: !IsDeviceSource))
                {
                    ExpandKey = action == CommandActions.Add ? id : null,
                    DeviceGroup = onDevice ? null : current.Value,
                    Verify = after =>
                    {
                        var (missing, extra) = RecipeDocument.CompareObjectChange(action, id, before, RecipeDocument.ObjectIdsIn(after, type.Group));
                        return missing.Count + extra.Count > 0 ? M.ObjectChangeMismatch(id, missing, extra) : null;
                    },
                    Apply = after => _session.ApplyObjectChange(action, id, after),
                    Warning = defaultsError == null ? null : M.DefaultsFailed(done, defaultsError),
                    AfterRebuild = action == CommandActions.Add ? () => CheckNewObject(id) : null,
                };
            });

        /// <summary>
        /// 执行一次设备修改 (可取消,有时限):<paramref name="execute"/> 发送指令并给出结果 (失败已提示时为 null),
        /// 之后由 <see cref="FinishChangeAsync"/> 收尾。取消或超时时提示设备上可能已经执行。
        /// </summary>
        private Task RunChangeAsync(string title, bool appendInterrupted, Func<OperationTimeout, Task<DeviceChange?>> execute)
        {
            _page.ShowResult(null);
            return _operation.RunAsync(M.WritePhase, title + "…", async operation =>
            {
                var change = await execute(operation);
                if (change != null)
                {
                    await FinishChangeAsync(change, operation.Token);
                }
            }, operation => _page.ReportFailure(M.Interrupted(OperationRunner.DescribeCancel(operation), appendInterrupted)));
        }

        /// <summary>
        /// 设备修改之后:重新读取所属的组 (无论指令是否成功,设备状态以读取为准) → 核对结果 → 更新配置 (保存的配置写进文件) →
        /// 重新生成页面 → 在窗口底部显示结果。有任何错误时提示错误。
        /// </summary>
        private async Task FinishChangeAsync(DeviceChange change, CancellationToken cancellationToken)
        {
            var deviceGroup = change.DeviceGroup;
            if (deviceGroup == null)
            {
                var kind = CommandText.ObjectKind(change.Group);
                _operation.Text = Lang.T($"重新读取{kind}…", $"Reading {kind.ToLowerInvariant()}s again…");
                var read = await _session.RereadGroupAsync(change.Group, cancellationToken);
                if (!read.IsSuccess)
                {
                    _page.Rebuild(expandKey: null);
                    _page.ReportFailure((change.Error == null ? string.Empty : change.Error + Environment.NewLine) + read.Error + Environment.NewLine + M.PageMayDiffer);
                    return;
                }

                deviceGroup = read.Value;
            }

            var problem = change.Error ?? change.Verify?.Invoke(deviceGroup);
            if (problem != null)
            {
                _page.Rebuild(expandKey: null);
                _page.ReportFailure(problem);
                return;
            }

            var fileError = change.Apply(deviceGroup);
            _page.Rebuild(change.ExpandKey);
            change.AfterRebuild?.Invoke();
            _page.RecordChange(change.Done);
            if (fileError != null)
            {
                _page.ReportFailure(M.FileSaveFailed(change.Done, fileError));
            }
            else if (change.Warning != null)
            {
                _page.ReportFailure(change.Warning);
            }
            else
            {
                _page.ShowResult(change.Info);
            }
        }

        /// <summary>保存的配置:设备与文件中列表的条数必须相同 (见 <see cref="RecipeEditSession.CheckListCountAsync"/>)。</summary>
        private async Task<bool> CheckListCountAsync(string listId, CancellationToken cancellationToken)
        {
            var refused = await _session.CheckListCountAsync(listId, cancellationToken);
            if (refused != null)
            {
                _page.ReportFailure(refused);
            }

            return refused == null;
        }

        /// <summary>列表所属的对象,如"膜系 F01"、"工艺 P0201"。</summary>
        private static string ListOwner(string listId) => CommandText.ObjectKind(listId[0]) + " " + ObjectTypes.GroupKey(listId);

        /// <summary>
        /// 新对象的默认引用 (<see cref="RecipeEditSession.DefaultReferenceId"/>) 的显示,如 <c>1 - Al</c>;没有时为 null。
        /// </summary>
        private string? DefaultReferenceLabel(ObjectType type)
        {
            var first = _session.DefaultReferenceId(type);
            return first == null
                ? null
                : new ParameterOption(DeviceIds.LastNumber(first).ToString(CultureInfo.InvariantCulture), _document.ObjectName(first) ?? string.Empty).Label;
        }

        /// <summary>新建对象 (材料、膜系、膜层、工艺) 后勾选它的全部参数,可以直接修改后保存。</summary>
        private void CheckNewObject(string objectId)
        {
            foreach (var item in _page.Groups.FirstOrDefault(g => g.Key == objectId)?.Items ?? Array.Empty<RecipeItemViewModel>())
            {
                item.IsChecked = true;
            }
        }

        /// <summary>删除被引用对象的提示中的一行:所属对象 · 名称 (ID = 值)。</summary>
        private string DescribeReference(RecipeEntry entry)
        {
            var key = ObjectTypes.GroupKey(entry.Id);
            var owner = _page.Groups.FirstOrDefault(g => g.Key == key)?.Title ?? key;
            var label = ZevisionCommandSet.Find(entry.Id)?.LabelFor(entry.Id, entry.Description) ?? entry.Description;
            return $"{owner} · {label} ({entry.Id} = {entry.Value})";
        }

        /// <summary>
        /// 一次设备修改 (新建/删除对象、列表追加/删除) 的结果,交给 <see cref="FinishChangeAsync"/> 重新读取、
        /// 更新配置、重新生成页面并显示结果。
        /// </summary>
        /// <param name="Group">要重新读取的组。</param>
        /// <param name="Error">设备指令失败的原因;成功时为 null。</param>
        /// <param name="Done">完成的修改,如"已新建材料 M03";记入窗口关闭后的结果。</param>
        /// <param name="Info">成功时在窗口底部显示的结果。</param>
        private sealed record DeviceChange(char Group, string? Error, string Done, string Info)
        {
            /// <summary>重新生成页面后展开的分组。</summary>
            public string? ExpandKey { get; init; }

            /// <summary>已经读取的设备组 (没有向设备发指令时不再读取);为 null 时重新读取。</summary>
            public IReadOnlyList<RecipeEntry>? DeviceGroup { get; init; }

            /// <summary>核对重新读取的设备组,返回不符合预期的说明;符合时为 null。</summary>
            public Func<IReadOnlyList<RecipeEntry>, string?>? Verify { get; init; }

            /// <summary>把修改写进配置 (保存的配置写文件),返回保存文件失败的原因。</summary>
            public Func<IReadOnlyList<RecipeEntry>, string?> Apply { get; init; } = _ => null;

            /// <summary>修改已完成但有问题时的提示 (如默认值没有设置成功)。</summary>
            public string? Warning { get; init; }

            /// <summary>重新生成页面后执行 (如勾选新对象的全部参数)。</summary>
            public Action? AfterRebuild { get; init; }
        }
    }

    /// <summary>修改配置窗口为 <see cref="RecipeObjectChanges"/> 提供的页面操作。</summary>
    internal interface IRecipeChangePage
    {
        /// <summary>当前的全部分组。</summary>
        IReadOnlyList<RecipeGroupViewModel> Groups { get; }

        /// <summary>按当前配置重新生成页面;<paramref name="expandKey"/> 指定的分组展开。</summary>
        void Rebuild(string? expandKey);

        /// <summary>记下完成的修改 (窗口关闭后记入主窗口的数据显示区)。</summary>
        void RecordChange(string done);

        /// <summary>在窗口底部显示结果;null 时清除。</summary>
        void ShowResult(string? text);

        /// <summary>提示错误 (同时写入错误日志)。</summary>
        void ReportFailure(string message);
    }
}
