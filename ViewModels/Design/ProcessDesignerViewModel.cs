using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Design;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;
using Zevision.ViewModels.Main;

namespace Zevision.ViewModels.Design
{
    /// <summary>
    /// 工艺编辑窗口:材料页和工艺页左边是本地的列表 (标出设备上是否已有同名的对象),右边编辑选中的一项;"设备"页列出设备上的工艺和材料。
    /// "保存"检查全部内容并保存到本地文件;"上传"先保存,再把选中的工艺上传到设备 (读取设备配置、确认后新建材料/膜系/膜层/工艺、写入并回读校验)。
    /// 上传不修改设备上已有的对象,不设活动工艺;上传失败时删除本次新建的对象。删除工艺时一起删除设备上的同名工艺和只被它使用的膜层。
    /// </summary>
    /// <remarks>
    /// 提示方式:设备和文件都没有改变的问题 (检查不通过、读取设备失败、被引用不能删除) 以红字显示在窗口底部,不弹窗;
    /// 改变了设备或文件的操作失败 (上传、删除、写文件) 弹出错误框。错误和异常写入 error.txt。
    /// </remarks>
    public sealed class ProcessDesignerViewModel : ViewModelBase, IRequestClose, IDisposable
    {
        /// <summary>删除材料时读取的组:材料、膜系、膜层,引用膜层的工艺,引用材料的输出源坩埚。</summary>
        private static readonly IReadOnlyList<char> MaterialGroups = new[] { 'M', 'F', 'L', 'P', 'S' };

        /// <summary>删除工艺时读取的组:工艺和膜层。</summary>
        private static readonly IReadOnlyList<char> ProcessGroups = new[] { 'P', 'L' };

        private static string ReadPhase => Lang.T("读取设备配置", "Reading the device configuration");
        private static string UploadPhase => Lang.T("上传工艺", "Uploading the process");
        private static string DeletePhase => Lang.T("删除设备上的对象", "Deleting objects on the device");
        private static string RollbackPhase => Lang.T("删除本次上传新建的对象", "Deleting the objects this upload created");

        /// <summary>撤销上传时的删除顺序:引用别人的在前 (工艺、膜层、膜系、材料)。</summary>
        private const string RollbackOrder = "PLFM";
        private static string ReadingText => Lang.T("读取设备配置 (约 10 秒)…", "Reading the device configuration (about 10 s)…");

        private readonly DesignDocument _document;
        private readonly IReadOnlyList<MaterialLibraryEntry> _library;
        private readonly IReadOnlyList<DeviceObjectInfo> _sources;
        private readonly IDesignStore _store;
        private readonly IInstrumentConfigService _config;
        private readonly IDialogService _dialogs;
        private readonly IErrorLog _errorLog;
        private readonly string _deviceName;
        private RecipeDocument _device;
        private DeviceContents _deviceContents = DeviceContents.Empty;
        private MaterialDesignViewModel? _selectedMaterial;
        private ProcessDesignViewModel? _selectedProcess;
        private int _selectedTabIndex;
        private bool _isDirty;
        private string? _resultText;
        private bool _resultIsError;

        /// <summary>
        /// 创建窗口的视图模型。
        /// </summary>
        /// <param name="document">读取的材料和工艺 (直接修改,保存时写入文件)。</param>
        /// <param name="library">材料库。</param>
        /// <param name="device">打开窗口时读取的设备配置 (<c>?</c>):传感器、输出源 (含坩埚数量) 和"设备"页的内容。</param>
        /// <param name="store">保存材料和工艺。</param>
        /// <param name="config">读取设备配置、上传、删除。</param>
        /// <param name="dialogs">确认框和错误框。</param>
        /// <param name="errorLog">错误和异常写入 error.txt。</param>
        public ProcessDesignerViewModel(
            DesignDocument document,
            IReadOnlyList<MaterialLibraryEntry> library,
            InstrumentSnapshot device,
            IDesignStore store,
            IInstrumentConfigService config,
            IDialogService dialogs,
            IErrorLog errorLog)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _errorLog = errorLog ?? throw new ArgumentNullException(nameof(errorLog));
            _deviceName = (device ?? throw new ArgumentNullException(nameof(device))).DeviceName;
            var entries = DesignDevice.EntriesOf(device);
            _sources = DesignDevice.Objects(entries, DesignDevice.SourceGroup);
            Sensors = Choices(DesignDevice.Objects(entries, DesignDevice.SensorGroup));
            Sources = Choices(_sources);
            Library = library.Select(LibraryChoice).ToList();

            Materials = new ObservableCollection<MaterialDesignViewModel>(document.Materials.Select(m => new MaterialDesignViewModel(m, this)));
            Processes = new ObservableCollection<ProcessDesignViewModel>(document.Processes.Select(p => new ProcessDesignViewModel(p, this)));
            _selectedMaterial = Materials.FirstOrDefault();
            _selectedProcess = Processes.FirstOrDefault();
            Device = new DevicePageViewModel(this);
            _device = new RecipeDocument(new InstrumentRecipe { Entries = entries });
            UpdateDevice();

            AddMaterialCommand = new RelayCommand(_ => AddMaterial(), _ => IsEditable);
            DeleteMaterialCommand = new AsyncRelayCommand(_ => DeleteMaterialAsync(), ReportException, _ => IsEditable && SelectedMaterial != null);
            AddProcessCommand = new RelayCommand(_ => AddProcess(), _ => IsEditable);
            DeleteProcessCommand = new AsyncRelayCommand(_ => DeleteProcessAsync(), ReportException, _ => IsEditable && SelectedProcess != null);
            SaveCommand = new RelayCommand(_ => Save(), _ => IsEditable);
            UploadCommand = new AsyncRelayCommand(_ => UploadAsync(), ReportException, _ => IsEditable && SelectedProcess != null);
            CancelCommand = new RelayCommand(_ => CancelOrClose());
            Operation.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OperationRunner.IsBusy))
                {
                    OnBusyChanged();
                }
            };
        }

        /// <inheritdoc />
        public event EventHandler? CloseRequested;

        /// <summary>窗口标题。</summary>
        public string Title => Lang.T("工艺编辑", "Process editor");

        /// <summary>材料和工艺的保存位置。</summary>
        public string PathText => Lang.T("保存到 ", "Saved to ") + _store.Path;

        /// <summary>材料。</summary>
        public ObservableCollection<MaterialDesignViewModel> Materials { get; }

        /// <summary>工艺。</summary>
        public ObservableCollection<ProcessDesignViewModel> Processes { get; }

        /// <summary>"设备"页:设备上的工艺和材料。</summary>
        public DevicePageViewModel Device { get; }

        /// <summary>选中的材料。</summary>
        public MaterialDesignViewModel? SelectedMaterial
        {
            get => _selectedMaterial;
            set
            {
                if (SetProperty(ref _selectedMaterial, value))
                {
                    OnPropertyChanged(nameof(HasSelectedMaterial));
                    RefreshCommands();
                }
            }
        }

        /// <summary>是否选中了材料 (显示编辑区)。</summary>
        public bool HasSelectedMaterial => SelectedMaterial != null;

        /// <summary>选中的工艺 ("上传"上传这个工艺)。</summary>
        public ProcessDesignViewModel? SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                if (SetProperty(ref _selectedProcess, value))
                {
                    OnPropertyChanged(nameof(HasSelectedProcess));
                    RefreshCommands();
                }
            }
        }

        /// <summary>是否选中了工艺 (显示编辑区)。</summary>
        public bool HasSelectedProcess => SelectedProcess != null;

        /// <summary>选中的页:0 = 材料,1 = 工艺,2 = 设备。</summary>
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        /// <summary>设备上的传感器 (材料的下拉框)。</summary>
        public IReadOnlyList<Choice<int>> Sensors { get; }

        /// <summary>设备上的输出源。</summary>
        public IReadOnlyList<Choice<int>> Sources { get; }

        /// <summary>材料库 (选择后填入名称、中文名、密度、Z 比),如"Al - 铝";输入化学式的开头可以跳到该项。</summary>
        public IReadOnlyList<Choice<MaterialLibraryEntry>> Library { get; }

        /// <summary>材料库是否有内容。</summary>
        public bool HasLibrary => Library.Count > 0;

        /// <summary>新建材料 (两段预熔、一段后处理、PID 10/0/0)。</summary>
        public ICommand AddMaterialCommand { get; }

        /// <summary>删除选中的材料 (被工艺使用时不能删除);可选同时删除设备上对应的材料、膜系和膜层。</summary>
        public ICommand DeleteMaterialCommand { get; }

        /// <summary>新建工艺。</summary>
        public ICommand AddProcessCommand { get; }

        /// <summary>删除选中的工艺,同时删除设备上的同名工艺和只被它使用的膜层 (确认后)。</summary>
        public ICommand DeleteProcessCommand { get; }

        /// <summary>检查并保存到本地文件 (不访问设备)。</summary>
        public ICommand SaveCommand { get; }

        /// <summary>保存,再把选中的工艺上传到设备 (确认后)。</summary>
        public ICommand UploadCommand { get; }

        /// <summary>上传期间取消上传,否则关闭窗口。</summary>
        public ICommand CancelCommand { get; }

        /// <summary>上传的状态 (说明、进度)。</summary>
        public OperationRunner Operation { get; } = new OperationRunner();

        /// <summary>是否正在读取设备或上传。</summary>
        public bool IsBusy => Operation.IsBusy;

        /// <summary>是否可以编辑 (上传期间不可以)。</summary>
        public bool IsEditable => !IsBusy;

        /// <summary>是否有未保存的修改。</summary>
        public bool IsDirty
        {
            get => _isDirty;
            private set => SetProperty(ref _isDirty, value);
        }

        /// <summary>上一次操作的结果或问题 (代替提示框),显示在窗口底部;改变了设备或文件的失败另外弹出错误框。</summary>
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

        /// <summary><see cref="ResultText"/> 是问题 (红字),不是成功的结果。</summary>
        public bool ResultIsError
        {
            get => _resultIsError;
            private set => SetProperty(ref _resultIsError, value);
        }

        /// <summary>是否有结果文本。</summary>
        public bool HasResultText => !string.IsNullOrEmpty(ResultText);

        /// <summary>窗口关闭后写入数据显示区的内容 (上传了哪些工艺、在设备上删除了什么,包括没有完成的);没有时为 null。</summary>
        public string? CompletionMessage { get; private set; }

        /// <inheritdoc />
        public bool CanClose => !IsBusy;

        /// <inheritdoc />
        public bool ConfirmClose()
        {
            if (IsBusy)
            {
                return false;
            }

            return !IsDirty || _dialogs.Confirm(Title, Lang.T("有未保存的修改,关闭后会丢失。关闭吗?", "There are unsaved changes; they are lost when the window closes. Close?"));
        }

        /// <inheritdoc />
        public void Dispose() => Operation.Cancel();

        /// <summary>
        /// 保存到本地 (<see cref="SaveCommand"/> 的实现):检查全部内容,有问题时在窗口底部列出,不保存;不访问设备。
        /// </summary>
        /// <returns>是否保存了。</returns>
        internal bool Save()
        {
            var errors = DesignRules.Validate(_document);
            if (errors.Count > 0)
            {
                ShowProblem(DesignMessages.ValidationFailed(errors));
                return false;
            }

            var saved = _store.Save(_document);
            if (!saved.IsSuccess)
            {
                ShowResult(null);
                ShowError(saved.Error);
                return false;
            }

            IsDirty = false;
            ShowResult(DesignMessages.Saved(_store.Path));
            return true;
        }

        /// <summary>
        /// 上传选中的工艺 (<see cref="UploadCommand"/> 的实现,测试中直接调用以便等待完成):先保存 (同 <see cref="Save"/>),
        /// 再读取设备配置、生成上传内容,确认后上传。上传过程中每个阶段 (新建、重新读取、写入、校验) 各有时限。
        /// 上传失败 (写入被拒绝、校验不一致、新建后仍不能写入、取消、超时) 时删除本次在设备上新建的对象,再弹出错误框。
        /// </summary>
        internal async Task UploadAsync()
        {
            var process = SelectedProcess;
            if (process == null || !Save())
            {
                return;
            }

            var attempt = new UploadAttempt();
            await Operation.RunAsync(ReadPhase, ReadingText, timeout => UploadAsync(process, timeout, attempt), timeout =>
            {
                var reason = OperationRunner.DescribeCancel(timeout);
                if (attempt.Created == null)
                {
                    // 还没有改动设备:只在窗口底部说明。
                    ShowResult(DesignMessages.SavedNotUploaded(_store.Path) + Environment.NewLine + reason, timeout.IsTimedOut);
                    return;
                }

                attempt.Failure = reason;
            });

            if (attempt.Created != null && attempt.Failure != null)
            {
                await RollBackAsync(process.Model, attempt.Created, attempt.Failure);
            }
        }

        /// <summary>重新读取设备配置 (<c>?</c>),更新"设备"页和本地项的设备状态 ("设备"页"刷新"的实现)。</summary>
        internal async Task RefreshDeviceAsync()
        {
            ShowResult(null);
            await Operation.RunAsync(ReadPhase, ReadingText, async timeout =>
            {
                var snapshot = await _config.ReadSnapshotAsync(timeout.Token);
                if (!snapshot.IsSuccess)
                {
                    ShowFailure(snapshot.Error);
                    return;
                }

                ShowDevice(snapshot.Value);
            }, timeout =>
            {
                if (timeout.IsTimedOut)
                {
                    ShowFailure(OperationLimits.BuildTimeoutMessage(timeout));
                }
            });
        }

        /// <summary>
        /// 按关键字查找材料库 (化学式、英文名、中文名包含关键字,不区分大小写);没有关键字时为全部。
        /// 化学式与关键字相同的排在最前,其次是化学式以关键字开头的,其余按材料表的顺序。
        /// </summary>
        internal IReadOnlyList<Choice<MaterialLibraryEntry>> SearchLibrary(string? keyword)
        {
            var text = keyword?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                return Library;
            }

            bool Has(string value) => value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            int Rank(MaterialLibraryEntry e) =>
                string.Equals(e.Formula, text, StringComparison.OrdinalIgnoreCase) ? 0
                : e.Formula.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 1
                : 2;
            return Library
                .Where(c => Has(c.Value.Formula) || Has(c.Value.Name) || Has(c.Value.ChineseName))
                .OrderBy(c => Rank(c.Value))
                .ToList();
        }

        /// <summary>
        /// 化学式为 <paramref name="name"/> 的材料库项 (区分大小写:<c>Co</c> 与 <c>CO</c> 不同);没有,或有两项 (如 <c>C</c>) 时为 null。
        /// </summary>
        internal MaterialLibraryEntry? FindLibraryEntry(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var matches = _library.Where(e => string.Equals(e.Formula, name!.Trim(), StringComparison.Ordinal)).Take(2).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        /// <summary>输出源的坩埚数量;未知时为 0。</summary>
        internal int CrucibleCount(int source) => _sources.FirstOrDefault(s => s.Number == source)?.CrucibleCount ?? 0;

        /// <summary>
        /// 材料在设备上的状态 (按名称):如"设备上: M10, F0B" (同名的材料和引用它们的同名膜系),或"设备上没有";没有名称时为空。
        /// </summary>
        internal string DeviceStatus(MaterialDesign material)
        {
            var name = material.Name.Trim();
            if (name.Length == 0)
            {
                return string.Empty;
            }

            var materials = _deviceContents.MaterialsNamed(name);
            var films = materials.SelectMany(_deviceContents.FilmsOf).Where(f => string.Equals(f.Name, name, StringComparison.Ordinal));
            return DesignMessages.DeviceStatus(materials.Select(m => m.Id).Concat(films.Select(f => f.Id)).ToList());
        }

        /// <summary>工艺在设备上的状态 (按名称):如"设备上: P0209",或"设备上没有";没有名称时为空。</summary>
        internal string DeviceStatus(ProcessDesign process) =>
            process.Name.Trim().Length == 0 ? string.Empty : DesignMessages.DeviceStatus(_deviceContents.ProcessesNamed(process.Name).Select(p => p.Id).ToList());

        internal void MarkDirty()
        {
            IsDirty = true;
            ShowResult(null);
        }

        internal void ReportException(Exception exception)
        {
            if (exception is null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            _errorLog.Write(DesignMessages.LogSource, exception.Message, exception);
            _dialogs.ShowError(Title, Lang.T("操作失败: ", "Operation failed: ") + exception.Message);
        }

        /// <summary>
        /// 删除选中的材料 (<see cref="DeleteMaterialCommand"/> 的实现,测试中直接调用以便等待完成)。被本地的工艺使用时不能删除。
        /// 询问是否同时删除设备上的数据 (按最近读取的设备内容列出将删除的对象):是 = 重新读取设备,删除同名的材料、引用它们的膜系和
        /// 用到这些膜系的膜层 (设备上的工艺用到这些膜层时不删除;对象与询问时列出的不同时再确认一次),成功后删除本地;
        /// 否 = 只删除本地;取消 = 不删除。本地的删除在保存后写入文件。
        /// </summary>
        internal async Task DeleteMaterialAsync()
        {
            var material = SelectedMaterial;
            if (material == null)
            {
                return;
            }

            var users = _document.Processes.Where(p => p.Steps.Any(s => s.MaterialId == material.Id)).Select(p => DesignRules.DisplayName(p.Name)).ToList();
            if (users.Count > 0)
            {
                ShowProblem(DesignMessages.MaterialInUse(material.DisplayName, users));
                return;
            }

            var preview = DeviceDeletion.ForMaterialNamed(material.Name, _device.Entries);
            var choice = _dialogs.AskYesNoCancel(Title, DesignMessages.DeleteMaterialQuestion(material.Model, preview));
            if (choice == null)
            {
                return;
            }

            DeviceDeletion? deletion = null;
            if (choice == true)
            {
                var outcome = await DeleteOnDeviceAsync(
                    MaterialGroups,
                    entries => DeviceDeletion.ForMaterialNamed(material.Name, entries),
                    new DeleteTexts(
                        DesignMessages.MaterialSubject(material.Model),
                        d => d.Objects.Count == 0 || SameObjects(d, preview) ? null : DesignMessages.DeleteDeviceConfirmation(d, changed: true),
                        DesignMessages.LocalMaterialKept));
                if (outcome.ReadError != null)
                {
                    ShowFailure(DesignMessages.DeviceNotRead(outcome.ReadError, DesignMessages.LocalMaterialKept));
                    return;
                }

                deletion = outcome.Deletion;
                if (deletion == null)
                {
                    return;
                }
            }

            var index = Materials.IndexOf(material);
            Materials.Remove(material);
            _document.Materials.Remove(material.Model);
            SelectedMaterial = Materials.Count == 0 ? null : Materials[Math.Min(index, Materials.Count - 1)];
            MarkDirty();
            if (deletion != null)
            {
                var result = deletion.Objects.Count > 0 ? DesignMessages.DeletedFromDevice(material.Model, deletion) : DesignMessages.NothingOnDevice(material.Model);
                ShowResult(result);
                if (deletion.Objects.Count > 0)
                {
                    AddCompletion(result);
                }
            }
        }

        /// <summary>
        /// 删除选中的工艺 (<see cref="DeleteProcessCommand"/> 的实现,测试中直接调用以便等待完成),同时删除设备上的同名工艺和只被它使用的膜层:
        /// 读取设备上的工艺和膜层 (<c>?P</c>、<c>?L</c>),确认后先删除设备上的,确认已删除后再删除本地 (保存后写入文件)。
        /// 活动工艺或其他工艺的链接工艺是它时不删除。没有名称的工艺只删除本地;读取设备失败时询问是否只删除本地。
        /// </summary>
        internal async Task DeleteProcessAsync()
        {
            var process = SelectedProcess;
            if (process == null)
            {
                return;
            }

            if (process.Name.Trim().Length == 0)
            {
                // 没有名称:设备上不会有同名的工艺,不读取设备。
                if (_dialogs.Confirm(Title, DesignMessages.DeleteLocalProcessQuestion(process.Model)))
                {
                    RemoveProcess(process);
                    ShowResult(DesignMessages.ProcessDeletedLocally(process.Model));
                }

                return;
            }

            var outcome = await DeleteOnDeviceAsync(
                ProcessGroups,
                entries => DeviceDeletion.ForProcessNamed(process.Name, entries),
                new DeleteTexts(
                    DesignMessages.ProcessSubject(process.Model),
                    d => DesignMessages.DeleteProcessConfirmation(process.Model, d),
                    DesignMessages.LocalProcessKept));
            if (outcome.ReadError != null)
            {
                // 读取设备失败 (如连接已断开):可以只删除本地,例如误建的空工艺。
                _errorLog.Write(DesignMessages.LogSource, outcome.ReadError);
                if (_dialogs.Confirm(Title, DesignMessages.DeleteProcessLocallyQuestion(process.Model, outcome.ReadError)))
                {
                    RemoveProcess(process);
                    ShowResult(DesignMessages.ProcessDeletedLocally(process.Model));
                }

                return;
            }

            var deletion = outcome.Deletion;
            if (deletion == null)
            {
                return;
            }

            RemoveProcess(process);
            var result = DesignMessages.ProcessDeleted(process.Model, deletion) + ActiveProcessNote(deletion);
            ShowResult(result);
            if (deletion.Objects.Count > 0)
            {
                AddCompletion(result);
            }
        }

        /// <summary>删除设备上的一个工艺和只被它使用的膜层 ("设备"页;确认后)。不改动本地的工艺。</summary>
        internal async Task DeleteDeviceProcessAsync(DeviceObjectViewModel item) =>
            await DeleteDeviceObjectAsync(item, ProcessGroups, (id, entries) => DeviceDeletion.ForProcesses(new[] { id }, entries));

        /// <summary>删除设备上的一个材料、引用它的膜系和用到这些膜系的膜层 ("设备"页;确认后,有工艺使用这些膜层时不删除)。不改动本地的材料。</summary>
        internal async Task DeleteDeviceMaterialAsync(DeviceObjectViewModel item) =>
            await DeleteDeviceObjectAsync(item, MaterialGroups, (id, entries) => DeviceDeletion.ForMaterials(new[] { id }, entries));

        private static IReadOnlyList<Choice<int>> Choices(IReadOnlyList<DeviceObjectInfo> objects) =>
            objects.Select(o => new Choice<int>(o.Number, o.Number.ToString(CultureInfo.InvariantCulture) + " - " + o.Name, string.Empty)).ToList();

        /// <summary>
        /// 材料库的选项:"化学式 - 名称" (中文界面用中文名,没有时用英文名),如"Al - 铝";说明为两种名称、密度和 Z 比 (Z 比未确定时注明)。
        /// </summary>
        private static Choice<MaterialLibraryEntry> LibraryChoice(MaterialLibraryEntry entry)
        {
            var name = Lang.IsChinese && entry.ChineseName.Length > 0 ? entry.ChineseName : entry.Name;
            var text = name.Length > 0 ? entry.Formula + " - " + name : entry.Formula;
            var names = string.Join(" / ", new[] { entry.ChineseName, entry.Name }.Where(n => n.Length > 0));
            var description = (names.Length > 0 ? names + Lang.T(",", ", ") : string.Empty)
                + Lang.T($"密度 {entry.Density} g/cm³,Z 比 {entry.ZRatio}", $"density {entry.Density} g/cm³, Z ratio {entry.ZRatio}")
                + (entry.ZRatioEstablished ? string.Empty : Lang.T(" (Z 比未确定,按 1.000)", " (Z ratio not established; 1.000 used)"));
            return new Choice<MaterialLibraryEntry>(entry, text, description);
        }

        /// <summary>两次查找的对象相同 (询问时按最近读取的内容列出的,与重新读取后的)。</summary>
        private static bool SameObjects(DeviceDeletion a, DeviceDeletion b) =>
            a.Objects.Select(o => o.Id).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(b.Objects.Select(o => o.Id).OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal);

        private async Task DeleteDeviceObjectAsync(DeviceObjectViewModel item, IReadOnlyList<char> groups, Func<string, IReadOnlyList<RecipeEntry>, DeviceDeletion> find)
        {
            if (item is null)
            {
                return;
            }

            ShowResult(null);
            var outcome = await DeleteOnDeviceAsync(
                groups,
                entries => find(item.Id, entries),
                new DeleteTexts(DesignMessages.DeviceObjectSubject(item.Id, item.Name), d => d.Objects.Count == 0 ? null : DesignMessages.DeleteDeviceConfirmation(d), null));
            if (outcome.ReadError != null)
            {
                ShowFailure(DesignMessages.DeviceNotRead(outcome.ReadError, null));
                return;
            }

            if (outcome.Deletion is DeviceDeletion deletion)
            {
                var result = deletion.Objects.Count == 0 ? DesignMessages.AlreadyGone(item.Id) : DesignMessages.DeletedOnDevice(deletion) + ActiveProcessNote(deletion);
                ShowResult(result);
                if (deletion.Objects.Count > 0)
                {
                    AddCompletion(result);
                }
            }
        }

        /// <summary>
        /// 删除设备上的对象:用分组帮助重新读取 <paramref name="groups"/> ("设备"页随之更新),找出要删除的对象;有外部引用时在窗口底部列出,不删除。
        /// 需要时确认,再按顺序删除,最后重新读取这些对象的组,确认已经不在 (删除按编号的指令协议文档没有规定)。
        /// 删除中途失败、取消或超时:弹出错误框 (设备已改变),已删除的对象写入 <see cref="CompletionMessage"/>。
        /// </summary>
        /// <returns>
        /// <see cref="DeleteOutcome.Deletion"/>:删除的对象 (设备上没有时为空);被引用、取消或失败时为 null (已提示)。
        /// <see cref="DeleteOutcome.ReadError"/>:开始时读取设备失败 (还没有改动设备,由调用方提示)。
        /// </returns>
        private async Task<DeleteOutcome> DeleteOnDeviceAsync(IReadOnlyList<char> groups, Func<IReadOnlyList<RecipeEntry>, DeviceDeletion> find, DeleteTexts texts)
        {
            DeviceDeletion? result = null;
            string? readError = null;
            var done = new List<DesignObject>();
            await Operation.RunAsync(ReadPhase, Lang.T("读取设备配置…", "Reading the device configuration…"), async timeout =>
            {
                readError = await RereadGroupsAsync(groups, timeout.Token);
                if (readError != null)
                {
                    return;
                }

                var deletion = find(_device.Entries);
                if (deletion.IsBlocked)
                {
                    ShowProblem(DesignMessages.DeleteBlocked(texts.Subject, deletion, texts.LocalKept));
                    return;
                }

                var question = texts.Confirmation(deletion);
                if (question != null)
                {
                    bool confirmed;
                    using (timeout.Pause())
                    {
                        // 用户阅读确认框的时间不计入操作时限。
                        confirmed = _dialogs.Confirm(Title, question);
                    }

                    if (!confirmed)
                    {
                        return;
                    }
                }

                if (deletion.Objects.Count == 0)
                {
                    result = deletion;
                    return;
                }

                timeout.StartPhase(DeletePhase);
                string? failure = null;
                foreach (var target in deletion.Objects)
                {
                    Operation.Text = Lang.T($"删除 {target.Id}…", $"Deleting {target.Id}…");
                    var deleted = await _config.DeleteObjectAsync(target.Id, _deviceName, timeout.Token);
                    if (!deleted.IsSuccess)
                    {
                        failure = DesignMessages.DeleteFailed(target, deleted.Error, done, texts.LocalKept);
                        break;
                    }

                    done.Add(target);
                }

                // 删除按编号 (DM03 等) 的指令协议文档没有规定:重新读取这几组,确认对象已经不在。失败时也读取,"设备"页显示实际的内容。
                var reread = await RereadGroupsAsync(deletion.Objects.Select(o => o.Group).Distinct().ToList(), timeout.Token);
                if (failure != null || reread != null)
                {
                    if (reread != null)
                    {
                        Device.MarkStale();
                    }

                    AddPartialDeletion(done);
                    ShowError(failure ?? reread);
                    return;
                }

                var remaining = deletion.Objects.Where(o => _device.ObjectIds(o.Group).Contains(o.Id)).ToList();
                if (remaining.Count > 0)
                {
                    AddPartialDeletion(deletion.Objects.Except(remaining).ToList());
                    ShowError(DesignMessages.StillOnDevice(remaining, texts.LocalKept));
                    return;
                }

                result = deletion;
            }, timeout =>
            {
                var reason = timeout.IsTimedOut ? OperationLimits.BuildTimeoutMessage(timeout) : Lang.T("已取消。", "Cancelled.");
                if (timeout.Phase != DeletePhase)
                {
                    // 还没有删除:读取超时由调用方提示,取消不提示。
                    readError = timeout.IsTimedOut ? reason : null;
                    return;
                }

                Device.MarkStale();
                if (done.Count > 0)
                {
                    AddCompletion(DesignMessages.DeletedPartially(done));
                }

                if (timeout.IsTimedOut)
                {
                    ShowError(reason + Environment.NewLine + DesignMessages.DeletedSoFar(done));
                }
                else
                {
                    ShowResult(reason + " " + DesignMessages.DeletedSoFar(done), isError: true);
                }
            });

            return new DeleteOutcome(result, readError);
        }

        /// <summary>删除了设备的活动工艺时,删除后重新读取的 P01 (设备改为 0);其他情况为空。</summary>
        private string ActiveProcessNote(DeviceDeletion deletion) =>
            deletion.ActiveProcess == null || deletion.Objects.Count == 0
                ? string.Empty
                : " " + DesignMessages.ActiveProcessAfterDelete(_device.Entries.FirstOrDefault(e => e.Id == DeviceDeletion.ActiveProcessSetting)?.Value);

        /// <summary>删除没有全部完成:已删除的对象写入 <see cref="CompletionMessage"/> (数据显示区留下记录)。</summary>
        private void AddPartialDeletion(IReadOnlyList<DesignObject> deleted)
        {
            if (deleted.Count > 0)
            {
                AddCompletion(DesignMessages.DeletedPartially(deleted));
            }
        }

        /// <summary>用分组帮助 (如 <c>?P</c>) 重新读取这几组,替换"设备"页中这几组的内容。</summary>
        /// <returns>失败时为错误信息。</returns>
        private async Task<string?> RereadGroupsAsync(IReadOnlyList<char> groups, CancellationToken cancellationToken)
        {
            foreach (var group in groups)
            {
                var entries = await _config.ReadGroupAsync(group, cancellationToken);
                if (!entries.IsSuccess)
                {
                    return entries.Error ?? Lang.T("未知错误。", "Unknown error.");
                }

                _device.ReplaceGroup(group, entries.Value);
            }

            UpdateDevice();
            return null;
        }

        private async Task UploadAsync(ProcessDesignViewModel process, OperationTimeout timeout, UploadAttempt attempt)
        {
            var snapshot = await _config.ReadSnapshotAsync(timeout.Token);
            if (!snapshot.IsSuccess)
            {
                ShowFailure(DesignMessages.SavedNotUploaded(_store.Path) + Environment.NewLine + snapshot.Error);
                return;
            }

            ShowDevice(snapshot.Value);
            var build = DesignBuilder.Build(_document, process.Model, snapshot.Value);
            if (!build.IsSuccess)
            {
                ShowProblem(DesignMessages.UploadChecksFailed(build.Error));
                return;
            }

            var planned = RestorePlanner.Plan(build.Value.Recipe, snapshot.Value);
            if (!planned.IsSuccess)
            {
                ShowProblem(DesignMessages.SavedNotUploaded(_store.Path) + Environment.NewLine + planned.Error);
                return;
            }

            if (planned.Value.Skipped.Count > 0)
            {
                ShowProblem(DesignMessages.CannotWrite(planned.Value.Skipped));
                return;
            }

            bool confirmed;
            var sameNamed = _deviceContents.ProcessesNamed(process.Name).Select(p => p.Id).ToList();
            using (timeout.Pause())
            {
                // 用户阅读确认框的时间不计入操作时限。
                confirmed = _dialogs.Confirm(Title, DesignMessages.BuildConfirmation(process.Model, build.Value, sameNamed));
            }

            if (!confirmed)
            {
                ShowResult(DesignMessages.SavedNotUploaded(_store.Path));
                return;
            }

            // 新建、重新读取 (每轮约 10 秒)、写入、校验:每个阶段重新计时,工艺的膜层和速率斜坡多时也不会因总时间超时。
            attempt.Created = build.Value.Objects.Where(o => o.IsNew).ToList();
            timeout.StartPhase(UploadPhase);
            var bar = Operation.CreateProgress(Lang.T(" (约 10 秒)…", " (about 10 s)…"));
            string? stage = null;
            var progress = new Progress<RestoreProgress>(p =>
            {
                if (p.Stage != stage)
                {
                    stage = p.Stage;
                    timeout.StartPhase(UploadPhase + " · " + p.Stage);
                }

                bar.Report(p);
            });
            var report = await _config.ExecuteRestoreAsync(build.Value.Recipe, planned.Value, progress, timeout.Token);
            if (!report.IsSuccess || report.AdditionalSkipped.Count > 0)
            {
                // 设备已部分改变:由调用方删除本次新建的对象并提示;不记上传记录。
                attempt.Failure = report.IsSuccess ? DesignMessages.UploadSkipped(report.AdditionalSkipped) : DesignMessages.UploadFailed(report, OperationRunner.DescribeCancel(timeout));
                return;
            }

            // 写入的值都已读回校验:新建的对象按写入的内容加入"设备"页,不再读取一次。
            foreach (var created in build.Value.Objects.Where(o => o.IsNew))
            {
                _device.AddObject(created.Id, build.Value.Recipe.Entries);
            }

            UpdateDevice();
            process.SetLastUpload(DesignMessages.UploadRecord(build.Value, DateTime.Now));
            var message = DesignMessages.Uploaded(process.Model, build.Value);
            AddCompletion(message);
            ShowResult(message);
            var resaved = _store.Save(_document);
            if (!resaved.IsSuccess)
            {
                // 上传记录没有写入文件:关闭时提醒保存。
                IsDirty = true;
                ShowError(resaved.Error);
            }
        }

        /// <summary>
        /// 撤销没有成功的上传 (用户要求 2026-10-23:"在保存失败时, 提示报错信息, 删除本次上传的实机数据"):重新读取这几组,
        /// 删除仍在设备上的本次新建的对象 (它们的编号在上传开始时是空闲的,所以是这次新建的),按工艺、膜层、膜系、材料的顺序,
        /// 再读取确认。复用的对象不动。最后弹出错误框,说明上传失败的原因和删除的结果。
        /// </summary>
        private async Task RollBackAsync(ProcessDesign process, IReadOnlyList<DesignObject> created, string failure)
        {
            var groups = created.Select(o => o.Group).Distinct().ToList();
            var removed = new List<DesignObject>();
            IReadOnlyList<DesignObject>? remaining = null;
            string? error = null;
            await Operation.RunAsync(RollbackPhase, Lang.T("删除本次上传新建的对象…", "Deleting the objects this upload created…"), async timeout =>
            {
                error = await RereadGroupsAsync(groups, timeout.Token);
                if (error != null)
                {
                    return;
                }

                var present = created.Where(o => _device.ObjectIds(o.Group).Contains(o.Id))
                    .OrderBy(o => RollbackOrder.IndexOf(o.Group))
                    .ThenByDescending(o => o.Id, StringComparer.Ordinal)
                    .ToList();
                foreach (var target in present)
                {
                    Operation.Text = Lang.T($"删除 {target.Id}…", $"Deleting {target.Id}…");
                    var deleted = await _config.DeleteObjectAsync(target.Id, _deviceName, timeout.Token);
                    if (!deleted.IsSuccess)
                    {
                        error = DesignMessages.DeleteObjectFailed(target, deleted.Error);
                        break;
                    }

                    removed.Add(target);
                }

                var reread = await RereadGroupsAsync(groups, timeout.Token);
                error ??= reread;
                if (reread == null)
                {
                    remaining = created.Where(o => _device.ObjectIds(o.Group).Contains(o.Id)).ToList();
                }
            }, timeout => error = OperationRunner.DescribeCancel(timeout));

            var clean = error == null && remaining != null && remaining.Count == 0;
            if (!clean)
            {
                Device.MarkStale();
            }

            var summary = DesignMessages.UploadRolledBack(process, clean);
            AddCompletion(summary);
            ShowResult(summary, isError: true);
            ShowError(DesignMessages.UploadFailedRolledBack(failure, created, removed, error, remaining));
        }

        private void ShowDevice(InstrumentSnapshot snapshot)
        {
            _device = new RecipeDocument(new InstrumentRecipe { Entries = DesignDevice.EntriesOf(snapshot) });
            UpdateDevice();
        }

        /// <summary>按 <see cref="_device"/> 更新"设备"页和本地材料、工艺的设备状态。</summary>
        private void UpdateDevice()
        {
            _deviceContents = DeviceContents.From(_device.Entries);
            Device.Show(_deviceContents, _deviceName, DateTime.Now);
            foreach (var material in Materials)
            {
                material.RefreshDeviceStatus();
            }

            foreach (var process in Processes)
            {
                process.RefreshDeviceStatus();
            }
        }

        private void AddCompletion(string message) =>
            CompletionMessage = CompletionMessage == null ? message : CompletionMessage + Environment.NewLine + message;

        /// <summary>窗口底部的结果;<paramref name="isError"/> 时为红字。</summary>
        private void ShowResult(string? text, bool isError = false)
        {
            ResultText = text;
            ResultIsError = isError && text != null;
        }

        /// <summary>用户输入方面的问题 (检查不通过、被引用不能删除等):红字,不弹窗,不写 error.txt。</summary>
        private void ShowProblem(string text) => ShowResult(text, isError: true);

        /// <summary>设备和文件都没有改变的失败 (如读取设备失败):红字,不弹窗,写入 error.txt。</summary>
        private void ShowFailure(string? error)
        {
            var text = error ?? Lang.T("未知错误。", "Unknown error.");
            _errorLog.Write(DesignMessages.LogSource, text);
            ShowResult(text, isError: true);
        }

        /// <summary>改变了设备或文件的操作失败:弹出错误框,写入 error.txt。</summary>
        private void ShowError(string? error)
        {
            var text = error ?? Lang.T("未知错误。", "Unknown error.");
            _errorLog.Write(DesignMessages.LogSource, text);
            _dialogs.ShowError(Title, text);
        }

        private void RemoveProcess(ProcessDesignViewModel process)
        {
            var index = Processes.IndexOf(process);
            Processes.Remove(process);
            _document.Processes.Remove(process.Model);
            SelectedProcess = Processes.Count == 0 ? null : Processes[Math.Min(index, Processes.Count - 1)];
            MarkDirty();
        }

        private void AddMaterial()
        {
            var material = new MaterialDesignViewModel(new MaterialDesign(), this);
            _document.Materials.Add(material.Model);
            Materials.Add(material);
            SelectedMaterial = material;
            SelectedTabIndex = 0;
            MarkDirty();
        }

        private void AddProcess()
        {
            var process = new ProcessDesignViewModel(new ProcessDesign(), this);
            _document.Processes.Add(process.Model);
            Processes.Add(process);
            SelectedProcess = process;
            SelectedTabIndex = 1;
            MarkDirty();
        }

        private void CancelOrClose()
        {
            if (IsBusy)
            {
                Operation.Cancel();
            }
            else
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnBusyChanged()
        {
            OnPropertiesChanged(nameof(IsBusy), nameof(IsEditable), nameof(CanClose));
            foreach (var material in Materials)
            {
                material.RefreshEditable();
            }

            foreach (var process in Processes)
            {
                process.Refresh();
            }

            Device.RefreshEditable();
            RefreshCommands();
        }

        /// <summary>一次删除的提示:删除的是什么、确认文本 (返回 null 时不确认)、失败时关于本地的说明 (没有本地对象时为 null)。</summary>
        private sealed record DeleteTexts(string Subject, Func<DeviceDeletion, string?> Confirmation, string? LocalKept);

        /// <summary>设备上删除的结果:删除的对象 (被引用、取消或失败时为 null),开始时读取设备失败的原因。</summary>
        private sealed record DeleteOutcome(DeviceDeletion? Deletion, string? ReadError);

        /// <summary>一次上传:开始改动设备时记下要新建的对象 (之前为 null);失败的原因 (成功时为 null)。</summary>
        private sealed class UploadAttempt
        {
            public IReadOnlyList<DesignObject>? Created { get; set; }

            public string? Failure { get; set; }
        }
    }
}
