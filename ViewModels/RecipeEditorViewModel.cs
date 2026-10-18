using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Zevision.Commands;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;
using Zevision.Services;
using M = Zevision.ViewModels.RecipeEditorMessages;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 修改配置窗口,分两页:"信息"页 (<see cref="Info"/>) 显示设备当前的只读信息和运行状态;
    /// "设置"页按类别分页、页内按对象分组显示配置项 (<see cref="RecipePageBuilder"/>),勾选后可编辑。
    /// 材料、膜系、膜层、工艺可以新建/删除,它们的子列表可以增删 (<see cref="ObjectTypes"/>),这些操作立即作用于设备。
    /// 保存时把勾选的项写入设备并读回校验。
    /// </summary>
    /// <remarks>
    /// 实机数据与保存的配置的差别 (页面是设备数据还是文件内容、是否保存文件) 都在 <see cref="RecipeEditTarget"/> 中;
    /// 提示文字在 <see cref="RecipeEditorMessages"/> 中。
    /// </remarks>
    public sealed class RecipeEditorViewModel : ViewModelBase, IRequestClose, IDisposable, IRecipePageActions
    {
        /// <summary>筛选时最多自动展开的分组数;更多时只筛选,由用户展开。</summary>
        internal const int MaxAutoExpandGroups = 20;

        private readonly RecipeDocument _document;
        private readonly RecipeEditTarget _target;
        private readonly IInstrumentConfigService _config;
        private readonly IDialogService _dialogs;
        private readonly IErrorLog _errorLog;
        private readonly List<string> _changes = new List<string>();
        private string _filterText = string.Empty;
        private int _writtenCount;
        private IReadOnlyList<RecipeGroupViewModel> _groups = Array.Empty<RecipeGroupViewModel>();
        private IReadOnlyList<RecipeTabViewModel> _tabs = Array.Empty<RecipeTabViewModel>();
        private int _selectedTabIndex;
        private SelectionStats _stats;

        /// <summary>
        /// 创建编辑器。
        /// </summary>
        /// <param name="recipe">
        /// 要修改的配置:来自文件时应已通过 <see cref="RecipeFile.Load"/> 的校验;
        /// 来自实机时由 <see cref="InstrumentSnapshot.ToRecipe"/> 根据刚读取的设备数据生成。
        /// </param>
        /// <param name="path">配置文件路径;为 null 时修改的是实机数据,写入设备后不保存文件。</param>
        /// <param name="config">配置服务。</param>
        /// <param name="dialogs">对话框。</param>
        /// <param name="errorLog">错误日志。</param>
        public RecipeEditorViewModel(InstrumentRecipe recipe, string? path, IInstrumentConfigService config, IDialogService dialogs, IErrorLog errorLog)
        {
            _document = new RecipeDocument(recipe ?? throw new ArgumentNullException(nameof(recipe)));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _target = RecipeEditTarget.Create(_document, path, config);
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _errorLog = errorLog ?? throw new ArgumentNullException(nameof(errorLog));

            Info = new InfoPageViewModel(recipe.Entries, config, errorLog, OnCommandError);
            BuildView(expandKey: null);

            Operation.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OperationRunner.IsBusy))
                {
                    OnPropertiesChanged(nameof(IsBusy), nameof(CanSave), nameof(IsEditable), nameof(CanChangeObjects));
                }
            };

            SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), OnCommandError, _ => CanSave);
            CancelCommand = new RelayCommand(_ => Cancel());
            ExpandAllCommand = new RelayCommand(_ => SetExpanded(true));
            CollapseAllCommand = new RelayCommand(_ => SetExpanded(false));
        }

        /// <inheritdoc />
        public event EventHandler? CloseRequested;

        /// <summary>"信息"页。</summary>
        public InfoPageViewModel Info { get; }

        /// <summary>是否在修改实机数据 (没有配置文件,写入设备后不保存文件)。</summary>
        public bool IsDeviceSource => _target.FilePath == null;

        /// <summary>材料、膜系、膜层、工艺页顶部的说明。</summary>
        public string ObjectChangeHint => M.ObjectChangeHint(toFile: !IsDeviceSource);

        /// <summary>窗口标题。</summary>
        public string Title => Lang.T("修改仪器配置 - ", "Edit instrument configuration - ") + (FileName ?? Lang.T("实机数据", "Device data"));

        /// <summary>数据来源说明。</summary>
        public string SourceText
        {
            get
            {
                var recipe = _document.Recipe;
                return IsDeviceSource
                    ? Lang.T($"实机数据    {recipe.DeviceName} {recipe.FirmwareVersion}    序列号 {recipe.SerialNumber}    读取于 {FormatTime(recipe.SavedAt)}", $"Device data    {recipe.DeviceName} {recipe.FirmwareVersion}    Serial {recipe.SerialNumber}    Read at {FormatTime(recipe.SavedAt)}")
                    : Lang.T($"{recipe.DeviceName} {recipe.FirmwareVersion}    序列号 {recipe.SerialNumber}    保存于 {recipe.SavedAt}", $"{recipe.DeviceName} {recipe.FirmwareVersion}    Serial {recipe.SerialNumber}    Saved at {recipe.SavedAt}");
            }
        }

        /// <summary>全部可写分组 (按恢复顺序:系统设置 → 材料 → 膜系 → 膜层 → 工艺)。新建/删除对象后重新生成。</summary>
        public IReadOnlyList<RecipeGroupViewModel> Groups
        {
            get => _groups;
            private set => SetProperty(ref _groups, value);
        }

        /// <summary>"设置"页中的类别页。新建/删除对象后重新生成。</summary>
        public IReadOnlyList<RecipeTabViewModel> Tabs
        {
            get => _tabs;
            private set => SetProperty(ref _tabs, value);
        }

        /// <summary>选中的类别页;重新生成后按标题恢复。</summary>
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        /// <summary>
        /// 是否可以新建/删除对象或修改列表:没有操作在进行,且没有勾选的项 (操作后会重新生成页面,未保存的修改会丢失)。
        /// </summary>
        public bool CanChangeObjects => !IsBusy && CheckedCount == 0;

        /// <summary>筛选文本:按 ID、说明或值查找;匹配的分组不超过 <see cref="MaxAutoExpandGroups"/> 个时自动展开。</summary>
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value ?? string.Empty))
                {
                    // 先筛选,匹配的分组不多时再展开:展开的分组逐行生成控件 (不虚拟化),
                    // 像 "0" 这样的短筛选会匹配几百个分组,全部展开要好几秒。
                    var filter = _filterText.Trim();
                    var matched = Groups.Where(g => g.ApplyFilter(filter, expandMatches: false)).ToList();
                    if (filter.Length > 0 && matched.Count <= MaxAutoExpandGroups)
                    {
                        foreach (var group in matched)
                        {
                            group.IsExpanded = true;
                        }
                    }
                }
            }
        }

        /// <summary>已勾选的项数。</summary>
        public int CheckedCount => _stats.Checked;

        /// <summary>已勾选且值已修改的项数。</summary>
        public int ModifiedCount => _stats.Modified;

        /// <summary>底部状态文本。</summary>
        public string SelectionText
        {
            get
            {
                var text = Lang.T($"已勾选 {CheckedCount} 项,其中 {ModifiedCount} 项已修改", $"{CheckedCount} checked, {ModifiedCount} modified");
                return _stats.Invalid > 0 ? text + Lang.T($";{_stats.Invalid} 项的值不合法", $"; {_stats.Invalid} invalid") : text;
            }
        }

        /// <summary>是否可以保存:有勾选项、值都合法、没有操作在进行。</summary>
        public bool CanSave => !IsBusy && CheckedCount > 0 && _stats.Invalid == 0;

        /// <summary>正在进行的写入/新建/删除的状态 (说明、进度),供界面绑定。</summary>
        public OperationRunner Operation { get; } = new OperationRunner();

        /// <summary>是否正在写入设备。</summary>
        public bool IsBusy => Operation.IsBusy;

        /// <summary>写入期间禁止编辑。</summary>
        public bool IsEditable => !IsBusy;

        /// <summary>保存成功后的结果说明,供主窗口记录;未保存时为 null。</summary>
        public string? CompletionMessage { get; private set; }

        /// <inheritdoc />
        public bool CanClose => !IsBusy;

        /// <summary>把勾选的项写入设备 (保存的配置同时保存文件)。</summary>
        public ICommand SaveCommand { get; }

        /// <summary>写入期间取消写入;否则关闭窗口 (不保存)。</summary>
        public ICommand CancelCommand { get; }

        /// <summary>展开全部分组。</summary>
        public ICommand ExpandAllCommand { get; }

        /// <summary>收起全部分组。</summary>
        public ICommand CollapseAllCommand { get; }

        private string? FileName => _target.FilePath == null ? null : Path.GetFileName(_target.FilePath);

        private IEnumerable<RecipeItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

        /// <summary>
        /// 关闭窗口前调用 (关闭按钮、Esc、标题栏的 ×):操作进行中不能关闭;有已修改但未保存的项时先确认是否放弃。
        /// 只勾选而没有改动的项不算未保存。
        /// </summary>
        /// <returns>可以关闭时返回 true。</returns>
        public bool ConfirmClose() =>
            CanClose
            && (ModifiedCount == 0 || _dialogs.Confirm(M.Title, Lang.T(
                $"有 {ModifiedCount} 项已修改但未保存,关闭后这些修改会丢失。确定关闭吗?",
                $"{ModifiedCount} modified setting(s) have not been saved and will be lost. Close anyway?")));

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
                message += Environment.NewLine + M.NewObjectDefaults(group, DefaultReference(type)?.Label);
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
        /// 然后重新读取该组 (如 <c>?F</c>) 并展开所属对象,新的一项出现后可以修改。
        /// </summary>
        /// <param name="listId">列表 ID,如 <c>F0103</c>、<c>L0102</c>、<c>P020107</c>。</param>
        /// <param name="listName">列表名称,如"预熔"。</param>
        /// <param name="value">要写入新条目的值 (如工艺添加的膜层编号);为 null 时使用设备默认值。</param>
        /// <param name="valueLabel">值的显示文本,用于确认框。</param>
        public async Task AppendListEntryAsync(string listId, string listName, string? value = null, string? valueLabel = null)
        {
            if (listId is null)
            {
                throw new ArgumentNullException(nameof(listId));
            }

            var owner = ListOwner(listId);
            var title = Lang.T("新增", "Add ") + listName;
            if (!_dialogs.Confirm(title, M.ConfirmListAppend(owner, listName, value == null ? null : valueLabel ?? value, toFile: !IsDeviceSource)))
            {
                return;
            }

            await RunChangeAsync(title, appendInterrupted: true, async operation =>
            {
                if (!await CheckListCountAsync(listId, operation.Token))
                {
                    return null;
                }

                var appended = await _config.AppendListItemAsync(listId, value, _document.Recipe.DeviceName, operation.Token);
                var done = M.ListAppended(owner, listName, appended.IsSuccess ? appended.Value : string.Empty);
                return new DeviceChange(title, listId[0], appended.Error, done, M.ListChangedInfo(done, savedFile: !IsDeviceSource))
                {
                    ExpandKey = ObjectTypes.GroupKey(listId),
                    Apply = deviceGroup => _target.ApplyListAppend(listId, appended.Value, deviceGroup),
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
        public async Task RemoveListEntryAsync(string listId, string listName, int? number = null)
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

            var display = AllItems.FirstOrDefault(i => i.Id == removing)?.DisplayValue ?? string.Empty;
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
                var report = shifted.Count == 0 ? null : await _config.ApplyEditsAsync(_document.Recipe, shifted, Operation.CreateProgress("…"), operation.Token);
                Operation.Text = title + "…";
                var removed = report != null && !report.IsSuccess
                    ? Result<string>.Fail(M.WriteFailed(report, OperationRunner.DescribeCancel(operation), toFile: !IsDeviceSource))
                    : await _config.RemoveLastListItemAsync(listId, _document.Recipe.DeviceName, operation.Token);

                var values = shifted.ToDictionary(e => e.Id, e => e.Value, StringComparer.Ordinal);
                var done = M.ListRemoved(owner, listName, removing);
                return new DeviceChange(title, listId[0], removed.Error, done, M.ListRemovedInfo(done, savedFile: !IsDeviceSource))
                {
                    ExpandKey = ObjectTypes.GroupKey(listId),
                    Apply = _ => _target.ApplyListRemove(removed.Value, values),
                };
            });
        }

        /// <summary>
        /// 保存:确认 → 写入设备并读回校验。
        /// 实机数据:写入成功后把新值作为当前值,窗口保持打开。
        /// 保存的配置:写入成功后保存文件 (新的保存时间和 MD5),成功后关闭窗口;写入设备失败时不修改文件。
        /// </summary>
        public async Task SaveAsync()
        {
            var selected = AllItems.Where(i => i.IsChecked).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            // 按类型规整 (如 5.89 → 5.890、0:5 → 0:00:05),确认框中显示的就是要写入的值。
            foreach (var item in selected.Where(i => !i.HasError))
            {
                item.Value = item.ValueToWrite;
            }

            var invalid = selected.FirstOrDefault(i => i.HasError);
            if (invalid != null)
            {
                _dialogs.ShowError(M.Title, $"{invalid.Id} {invalid.Label}: {invalid.Error}");
                return;
            }

            if (!_dialogs.Confirm(M.Title, M.ConfirmSave(selected, _target.FilePath)))
            {
                return;
            }

            var close = false;
            await Operation.RunAsync(M.WritePhase, Lang.T("检查设备…", "Checking the device…"), async operation =>
            {
                var edits = selected.Select(i => new RecipeEntry { Id = i.Id, Value = i.Value, Description = i.Description }).ToList();
                var report = await _config.ApplyEditsAsync(_document.Recipe, edits, Operation.CreateProgress("…"), operation.Token);
                if (!report.IsSuccess)
                {
                    ReportFailure(M.WriteFailed(report, OperationRunner.DescribeCancel(operation), toFile: !IsDeviceSource));
                    return;
                }

                close = Commit(selected, report.WrittenCount);
            });

            // 在 IsBusy 复位之后再请求关闭:窗口的 OnClosing 在 CanClose (即 !IsBusy) 为 false 时会取消关闭。
            if (close)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>取消"信息"页正在进行的读取 (窗口关闭时调用)。</summary>
        public void Dispose() => Info.Dispose();

        /// <inheritdoc />
        ICommand IRecipePageActions.Command(Func<Task> execute, Func<bool>? canExecute) =>
            new AsyncRelayCommand(_ => execute(), OnCommandError, _ => CanChangeObjects && (canExecute?.Invoke() ?? true));

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
                    Operation.Text = Lang.T($"读取设备上的{type.Kind}…", $"Reading {type.Kind.ToLowerInvariant()}s on the device…");
                }

                var current = await _target.ReadDeviceGroupAsync(type.Group, token);
                if (!current.IsSuccess)
                {
                    ReportFailure(current.Error!); // 失败的 Result 总有原因
                    return null;
                }

                var before = RecipeDocument.ObjectIdsIn(current.Value, type.Group);
                var id = objectId ?? type.NextFreeId(_target.UsedObjectIds(type.Group, before));
                if (id == null)
                {
                    ReportFailure(M.NoFreeNumber(type.Kind));
                    return null;
                }

                // 保存的配置中删除设备上没有的对象:只从文件中删除,不发指令,也不必重新读取。
                var onDevice = action == CommandActions.Add || before.Contains(id);
                string? error = null;
                string? defaultsError = null;
                if (onDevice && action == CommandActions.Add)
                {
                    // 新建时同时设置默认引用,重新读取的结果 (保存的配置写进文件的内容) 就是设好的。
                    Operation.Text = title + " " + id + "…";
                    var created = await _config.CreateObjectAsync(type, id, DefaultReference(type)?.Value, _document.Recipe.DeviceName, token);
                    error = created.Error;
                    defaultsError = created.DefaultsError;
                }
                else if (onDevice)
                {
                    Operation.Text = title + " " + id + "…";
                    error = (await _config.DeleteObjectAsync(id, _document.Recipe.DeviceName, token)).Error;
                }

                var done = M.ObjectChanged(action, type.Kind, id, onDevice);
                return new DeviceChange(title, type.Group, error, done, M.Done(done, savedFile: !IsDeviceSource))
                {
                    ExpandKey = action == CommandActions.Add ? id : null,
                    DeviceGroup = onDevice ? null : current.Value,
                    Verify = after =>
                    {
                        var (missing, extra) = RecipeDocument.CompareObjectChange(action, id, before, RecipeDocument.ObjectIdsIn(after, type.Group));
                        return missing.Count + extra.Count > 0 ? M.ObjectChangeMismatch(id, missing, extra) : null;
                    },
                    Apply = after => _target.ApplyObjectChange(action, id, after),
                    Warning = defaultsError == null ? null : M.DefaultsFailed(done, defaultsError),
                    AfterRebuild = action == CommandActions.Add ? () => CheckNewObject(id) : null,
                };
            });

        /// <summary>
        /// 执行一次设备修改 (可取消,有时限):<paramref name="execute"/> 发送指令并给出结果 (失败已提示时为 null),
        /// 之后由 <see cref="FinishChangeAsync"/> 收尾。取消或超时时提示设备上可能已经执行。
        /// </summary>
        private Task RunChangeAsync(string title, bool appendInterrupted, Func<OperationTimeout, Task<DeviceChange?>> execute) =>
            Operation.RunAsync(M.WritePhase, title + "…", async operation =>
            {
                var change = await execute(operation);
                if (change != null)
                {
                    await FinishChangeAsync(change, operation.Token);
                }
            }, operation => ReportFailure(M.Interrupted(OperationRunner.DescribeCancel(operation), appendInterrupted)));

        /// <summary>
        /// 设备修改之后:重新读取所属的组 (无论指令是否成功,设备状态以读取为准) → 核对结果 → 更新配置 (保存的配置写进文件) →
        /// 重新生成页面 → 提示。有任何错误时提示错误。
        /// </summary>
        private async Task FinishChangeAsync(DeviceChange change, CancellationToken cancellationToken)
        {
            var deviceGroup = change.DeviceGroup;
            if (deviceGroup == null)
            {
                var kind = CommandText.ObjectKind(change.Group);
                Operation.Text = Lang.T($"重新读取{kind}…", $"Reading {kind.ToLowerInvariant()}s again…");
                var read = await _config.ReadGroupAsync(change.Group, cancellationToken);
                if (!read.IsSuccess)
                {
                    BuildView(expandKey: null);
                    ReportFailure((change.Error == null ? string.Empty : change.Error + Environment.NewLine) + read.Error + Environment.NewLine + M.PageMayDiffer);
                    return;
                }

                deviceGroup = read.Value;
                _target.OnDeviceGroupRead(change.Group, deviceGroup);
            }

            var problem = change.Error ?? change.Verify?.Invoke(deviceGroup);
            if (problem != null)
            {
                BuildView(expandKey: null);
                ReportFailure(problem);
                return;
            }

            var fileError = change.Apply(deviceGroup);
            BuildView(change.ExpandKey);
            change.AfterRebuild?.Invoke();
            AddChange(change.Done);
            if (fileError != null)
            {
                ReportFailure(M.FileSaveFailed(change.Done, fileError));
            }
            else if (change.Warning != null)
            {
                ReportFailure(change.Warning);
            }
            else
            {
                _dialogs.ShowInfo(change.Title, change.Info);
            }
        }

        /// <summary>保存的配置:设备与文件中列表的条数必须相同 (见 <see cref="RecipeEditTarget.CheckListCountAsync"/>)。</summary>
        private async Task<bool> CheckListCountAsync(string listId, CancellationToken cancellationToken)
        {
            var refused = await _target.CheckListCountAsync(listId, cancellationToken);
            if (refused != null)
            {
                ReportFailure(refused);
            }

            return refused == null;
        }

        /// <summary>列表所属的对象,如"膜系 F01"、"工艺 P0201"。</summary>
        private static string ListOwner(string listId) => CommandText.ObjectKind(listId[0]) + " " + ObjectTypes.GroupKey(listId);

        /// <summary>
        /// 新对象的默认引用:被引用组中编号最小的对象 (值为十进制编号,如 <c>1</c>;显示如 <c>1 - Al</c>);
        /// 没有默认引用或被引用的组中没有对象时为 null。
        /// </summary>
        private ParameterOption? DefaultReference(ObjectType type)
        {
            var first = type.NewDefault == null
                ? null
                : _document.ObjectIds(type.NewDefault.ReferencedGroup).OrderBy(i => i, StringComparer.Ordinal).FirstOrDefault();
            return first == null
                ? null
                : new ParameterOption(DeviceIds.LastNumber(first).ToString(CultureInfo.InvariantCulture), _document.ObjectName(first) ?? string.Empty);
        }

        /// <summary>新建对象 (材料、膜系、膜层、工艺) 后勾选它的全部参数,可以直接修改后保存。</summary>
        private void CheckNewObject(string objectId)
        {
            foreach (var item in Groups.FirstOrDefault(g => g.Key == objectId)?.Items ?? Array.Empty<RecipeItemViewModel>())
            {
                item.IsChecked = true;
            }
        }

        /// <summary>删除被引用对象的提示中的一行:所属对象 · 名称 (ID = 值)。</summary>
        private string DescribeReference(RecipeEntry entry)
        {
            var key = ObjectTypes.GroupKey(entry.Id);
            var owner = Groups.FirstOrDefault(g => g.Key == key)?.Title ?? key;
            var label = ZevisionCommandSet.Find(entry.Id)?.LabelFor(entry.Id, entry.Description) ?? entry.Description;
            return $"{owner} · {label} ({entry.Id} = {entry.Value})";
        }

        /// <summary>
        /// 设备已全部写入并校验通过:新值作为当前值 (下拉框的引用选项、再次修改都基于它)。
        /// 实机数据:窗口保持打开,可以继续修改。保存的配置:同时保存文件,成功后关闭窗口;保存失败时内存中的值不变。
        /// </summary>
        /// <returns>窗口是否应关闭 (保存的配置且文件已保存)。</returns>
        private bool Commit(IReadOnlyList<RecipeItemViewModel> selected, int writtenCount)
        {
            var summary = M.SaveSummary(writtenCount);
            var values = selected.ToDictionary(i => i.Id, i => i.Value, StringComparer.Ordinal);
            var saveError = _target.Commit(d => d.SetValues(values));
            if (saveError != null)
            {
                ReportFailure(summary + Environment.NewLine + M.SaveFileFailed(saveError));
                return false;
            }

            foreach (var item in selected)
            {
                item.Commit();
            }

            // 按新值重新生成页面 (保留选中的页、展开的分组和筛选;已保存的项不再勾选,没有未保存的修改):
            // 分组标题用新名称,引用其他对象的下拉框也用新名称,如改了膜层名称后工艺的膜层下拉框、改了膜系名称后膜层的膜系下拉框。
            if (IsDeviceSource)
            {
                BuildView(expandKey: null);
            }

            _writtenCount += writtenCount;
            UpdateCompletionMessage();
            _dialogs.ShowInfo(M.Title, IsDeviceSource ? summary : summary + Environment.NewLine + M.SavedTo(_target.FilePath!));
            return !IsDeviceSource;
        }

        /// <summary>
        /// 由配置生成分组和类别页 (<see cref="RecipePageBuilder"/>)。重新生成时保留选中的页、展开的分组和筛选;
        /// <paramref name="expandKey"/> 指定的分组 (新建的对象、修改了列表的对象) 展开。
        /// </summary>
        private void BuildView(string? expandKey)
        {
            var selectedTitle = SelectedTabIndex >= 0 && SelectedTabIndex < Tabs.Count ? Tabs[SelectedTabIndex].Title : null;
            var expanded = new HashSet<string>(Groups.Where(g => g.IsExpanded).Select(g => g.Key), StringComparer.Ordinal);
            var filter = _filterText.Trim();

            var (groups, tabs) = RecipePageBuilder.Build(_document, this);
            foreach (var group in groups)
            {
                if (expanded.Contains(group.Key) || group.Key == expandKey)
                {
                    group.IsExpanded = true;
                }

                if (filter.Length > 0)
                {
                    group.ApplyFilter(filter, expandMatches: false);
                }

                foreach (var item in group.Items)
                {
                    item.Changed += (s, e) => OnSelectionChanged();
                }
            }

            Groups = groups;
            Tabs = tabs;
            SelectedTabIndex = Math.Max(0, tabs.ToList().FindIndex(t => t.Title == selectedTitle));
            OnSelectionChanged();
        }

        private void AddChange(string change)
        {
            _changes.Add(change);
            UpdateCompletionMessage();
        }

        private void UpdateCompletionMessage() => CompletionMessage = M.Completion(_changes, _writtenCount, FileName);

        private static string FormatTime(string savedAt) =>
            DateTimeOffset.TryParse(savedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                ? time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : savedAt;

        private void ReportFailure(string message)
        {
            _errorLog.Write(M.LogSource, message);
            _dialogs.ShowError(M.Title, message);
        }

        private void OnCommandError(Exception ex)
        {
            _errorLog.Write(M.LogSource, ex.Message, ex);
            _dialogs.ShowError(M.Title, Lang.T("操作失败: ", "Operation failed: ") + ex.Message);
        }

        private void Cancel()
        {
            if (IsBusy)
            {
                Operation.Cancel();
                return;
            }

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SetExpanded(bool expanded)
        {
            foreach (var group in Groups.Where(g => g.IsVisible))
            {
                group.IsExpanded = expanded;
            }
        }

        /// <summary>
        /// 勾选或值变化:重新统计 (一次遍历) 并通知。统计结果缓存起来,因为 <see cref="CanSave"/> 和
        /// 每个新建/删除按钮的可执行判断 (<see cref="CanChangeObjects"/>) 在每次键盘、鼠标操作后都会被 WPF 重新查询,
        /// 每次都遍历上千项会让输入变慢。
        /// </summary>
        private void OnSelectionChanged()
        {
            var stats = default(SelectionStats);
            foreach (var item in AllItems.Where(i => i.IsChecked))
            {
                stats.Checked++;
                stats.Modified += item.IsModified ? 1 : 0;
                stats.Invalid += item.HasError ? 1 : 0;
            }

            _stats = stats;
            OnPropertiesChanged(nameof(CheckedCount), nameof(ModifiedCount), nameof(SelectionText), nameof(CanSave), nameof(CanChangeObjects));
        }

        /// <summary>勾选项的统计:勾选数、已修改数、值不合法数。</summary>
        private struct SelectionStats
        {
            public int Checked;
            public int Modified;
            public int Invalid;
        }

        /// <summary>
        /// 一次设备修改 (新建/删除对象、列表追加/删除) 的结果,交给 <see cref="FinishChangeAsync"/> 重新读取、
        /// 更新配置、重新生成页面并提示。
        /// </summary>
        /// <param name="Title">提示框标题。</param>
        /// <param name="Group">要重新读取的组。</param>
        /// <param name="Error">设备指令失败的原因;成功时为 null。</param>
        /// <param name="Done">完成的修改,如"已新建材料 M03";记入窗口关闭后的结果。</param>
        /// <param name="Info">成功时的提示。</param>
        private sealed record DeviceChange(string Title, char Group, string? Error, string Done, string Info)
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
}
