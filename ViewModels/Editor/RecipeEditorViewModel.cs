using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;
using M = Zevision.ViewModels.Editor.RecipeEditorMessages;

namespace Zevision.ViewModels.Editor
{
    /// <summary>
    /// 修改配置窗口,分两页:"信息"页 (<see cref="Info"/>) 显示设备当前的只读信息和运行状态;
    /// "设置"页按类别分页、页内按对象分组显示配置项 (<see cref="RecipePageBuilder"/>),勾选后可编辑。
    /// 材料、膜系、膜层、工艺可以新建/删除,它们的子列表可以增删 (<see cref="ObjectTypes"/>),这些操作立即作用于设备。
    /// 保存时把勾选的项写入设备并读回校验。
    /// </summary>
    /// <remarks>
    /// 与设备、配置文件的同步 (页面是设备数据还是文件内容、是否保存文件) 都在 <see cref="RecipeEditSession"/> (业务层) 中;
    /// 新建/删除对象和列表的增删在 <see cref="RecipeObjectChanges"/> 中,本类只管页面 (分组、筛选、勾选)、保存和关闭;
    /// 提示文字在 <see cref="RecipeEditorMessages"/> 中。
    /// </remarks>
    public sealed class RecipeEditorViewModel : ViewModelBase, IRequestClose, IDisposable, IRecipePageActions, IRecipeChangePage
    {
        /// <summary>筛选时最多自动展开的分组数;更多时只筛选,由用户展开。</summary>
        internal const int MaxAutoExpandGroups = 20;

        private readonly RecipeDocument _document;
        private readonly RecipeEditSession _session;
        private readonly IDialogService _dialogs;
        private readonly IErrorLog _errorLog;
        private readonly RecipeObjectChanges _objectChanges;
        private readonly List<string> _changes = new List<string>();
        private string _filterText = string.Empty;
        private int _writtenCount;
        private IReadOnlyList<RecipeGroupViewModel> _groups = Array.Empty<RecipeGroupViewModel>();
        private IReadOnlyList<RecipeTabViewModel> _tabs = Array.Empty<RecipeTabViewModel>();
        private int _selectedTabIndex;
        private string? _resultText;
        private SelectionStats _stats;

        /// <summary>
        /// 创建编辑器。
        /// </summary>
        /// <param name="session">要修改的配置及其与设备 (和配置文件) 的同步 (<see cref="RecipeEditSession.Open"/>)。</param>
        /// <param name="dialogs">对话框。</param>
        /// <param name="errorLog">错误日志。</param>
        public RecipeEditorViewModel(RecipeEditSession session, IDialogService dialogs, IErrorLog errorLog)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _document = session.Document;
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _errorLog = errorLog ?? throw new ArgumentNullException(nameof(errorLog));
            _objectChanges = new RecipeObjectChanges(session, dialogs, Operation, this);

            Info = new InfoPageViewModel(_document.Entries, session.Reader, errorLog, OnCommandError);
            BuildView(expandKey: null);

            Operation.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OperationRunner.IsBusy))
                {
                    OnPropertiesChanged(nameof(IsBusy), nameof(CanSave), nameof(IsEditable), nameof(CanChangeObjects), nameof(HasResultText));
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
        public bool IsDeviceSource => _session.FilePath == null;

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

        /// <summary>
        /// 上一次操作成功的结果 (如"已写入设备并校验 3 项。"),显示在窗口底部,不弹窗。开始新的操作时清空;失败时用错误框提示。
        /// </summary>
        public string? ResultText
        {
            get => _resultText;
            private set
            {
                if (SetProperty(ref _resultText, value))
                {
                    OnPropertyChanged(nameof(HasResultText));
                }
            }
        }

        /// <summary>是否显示 <see cref="ResultText"/> (操作进行中不显示)。</summary>
        public bool HasResultText => !IsBusy && !string.IsNullOrEmpty(_resultText);

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

        private string? FileName => _session.FilePath == null ? null : Path.GetFileName(_session.FilePath);

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

        /// <summary>在设备上新建对象 (见 <see cref="RecipeObjectChanges.CreateObjectAsync"/>)。</summary>
        /// <param name="group">组字母:M、F、L 或 P。</param>
        public Task CreateObjectAsync(char group) => _objectChanges.CreateObjectAsync(group);

        /// <summary>在设备上删除对象 (见 <see cref="RecipeObjectChanges.DeleteObjectAsync"/>)。</summary>
        /// <param name="objectId">对象 ID,如 <c>M03</c>、<c>P0202</c>。</param>
        public Task DeleteObjectAsync(string objectId) => _objectChanges.DeleteObjectAsync(objectId);

        /// <summary>在设备上把一项追加到列表末尾 (见 <see cref="RecipeObjectChanges.AppendListEntryAsync"/>)。</summary>
        /// <param name="listId">列表 ID,如 <c>F0103</c>、<c>P020107</c>。</param>
        /// <param name="listName">列表名称,如"预熔"。</param>
        /// <param name="value">要写入新条目的值;为 null 时使用设备默认值。</param>
        public Task AppendListEntryAsync(string listId, string listName, string? value = null) => _objectChanges.AppendListEntryAsync(listId, listName, value);

        /// <summary>在设备上删除列表的一项 (见 <see cref="RecipeObjectChanges.RemoveListEntryAsync"/>)。</summary>
        /// <param name="listId">列表 ID,如 <c>F0103</c>、<c>P020107</c>。</param>
        /// <param name="listName">列表名称,如"预熔"。</param>
        /// <param name="number">要删除的序号 (从 1 开始);为 null 时删除最后一项。</param>
        public Task RemoveListEntryAsync(string listId, string listName, int? number = null) => _objectChanges.RemoveListEntryAsync(listId, listName, number);

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

            if (!_dialogs.Confirm(M.Title, M.ConfirmSave(selected, _session.FilePath)))
            {
                return;
            }

            var close = false;
            ResultText = null;
            await Operation.RunAsync(M.WritePhase, Lang.T("检查设备…", "Checking the device…"), async operation =>
            {
                var edits = selected.Select(i => new RecipeEntry { Id = i.Id, Value = i.Value, Description = i.Description }).ToList();
                var report = await _session.ApplyEditsAsync(edits, Operation.CreateProgress("…"), operation.Token);
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

        /// <inheritdoc />
        void IRecipeChangePage.Rebuild(string? expandKey) => BuildView(expandKey);

        /// <inheritdoc />
        void IRecipeChangePage.RecordChange(string done) => AddChange(done);

        /// <inheritdoc />
        void IRecipeChangePage.ShowResult(string? text) => ResultText = text;

        /// <inheritdoc />
        void IRecipeChangePage.ReportFailure(string message) => ReportFailure(message);

        /// <summary>
        /// 设备已全部写入并校验通过:新值作为当前值 (下拉框的引用选项、再次修改都基于它)。
        /// 实机数据:窗口保持打开,可以继续修改。保存的配置:同时保存文件,成功后关闭窗口;保存失败时内存中的值不变。
        /// </summary>
        /// <returns>窗口是否应关闭 (保存的配置且文件已保存)。</returns>
        private bool Commit(IReadOnlyList<RecipeItemViewModel> selected, int writtenCount)
        {
            var summary = M.SaveSummary(writtenCount);
            var values = selected.ToDictionary(i => i.Id, i => i.Value, StringComparer.Ordinal);
            var saveError = _session.Commit(d => d.SetValues(values));
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
            ResultText = IsDeviceSource ? summary : summary + " " + M.SavedTo(_session.FilePath!);
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
    }
}
