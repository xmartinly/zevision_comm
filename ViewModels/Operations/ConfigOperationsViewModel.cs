using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;
using Zevision.ViewModels.Display;
using Zevision.ViewModels.Editor;
using Zevision.ViewModels.Main;

namespace Zevision.ViewModels.Operations
{
    /// <summary>修改配置的数据来源。</summary>
    public enum EditSource
    {
        /// <summary>实机数据:读取设备当前配置 (约 10 秒),修改后只写入设备。</summary>
        Device,

        /// <summary>保存的配置:选择配置文件,修改后写入设备并保存到该文件。</summary>
        File,
    }

    /// <summary>
    /// 仪器配置的保存、恢复、修改流程 (左侧"仪器配置"区域),以及操作进行中的进度、取消与分阶段时限。
    /// </summary>
    public sealed class ConfigOperationsViewModel : ViewModelBase
    {
        private static string ReadPhase => Lang.T("读取设备配置", "Reading the device configuration");
        private static string ExecutePhase => Lang.T("执行恢复", "Restoring");

        /// <summary>配置文件对话框的筛选器。</summary>
        private static string RecipeDialogFilter => Lang.T("Zevision 仪器配置 (*.json)|*.json|所有文件 (*.*)|*.*", "Zevision instrument configuration (*.json)|*.json|All files (*.*)|*.*");

        private readonly IInstrumentConfigService _config;
        private readonly IRecipeStore _recipes;
        private readonly IDialogService _dialogs;
        private readonly IErrorLog _errorLog;
        private readonly LogViewModel _log;
        private readonly Func<bool> _isConnected;
        private Task _idle = Task.CompletedTask;
        private bool _quiet;
        private EditSource _editSource = EditSource.Device;

        /// <summary>
        /// 创建配置操作。
        /// </summary>
        /// <param name="config">配置服务。</param>
        /// <param name="recipes">配置文件的存储。</param>
        /// <param name="dialogs">对话框。</param>
        /// <param name="errorLog">错误日志。</param>
        /// <param name="log">数据显示区。</param>
        /// <param name="isConnected">是否可以开始操作:设备已连接且没有在重复发送 (否则命令不可用)。</param>
        public ConfigOperationsViewModel(
            IInstrumentConfigService config,
            IRecipeStore recipes,
            IDialogService dialogs,
            IErrorLog errorLog,
            LogViewModel log,
            Func<bool> isConnected)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _errorLog = errorLog ?? throw new ArgumentNullException(nameof(errorLog));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _isConnected = isConnected ?? throw new ArgumentNullException(nameof(isConnected));

            SaveCommand = new AsyncRelayCommand(_ => TrackAsync(SaveAsync), log.ReportException, _ => CanStart);
            RestoreCommand = new AsyncRelayCommand(_ => TrackAsync(RestoreAsync), log.ReportException, _ => CanStart);
            EditCommand = new AsyncRelayCommand(_ => TrackAsync(EditAsync), log.ReportException, _ => CanStart);
            CancelCommand = new RelayCommand(_ => Operation.Cancel(), _ => Operation.CanCancel);
            Operation.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(OperationRunner.IsBusy))
                {
                    OnPropertyChanged(nameof(IsBusy));
                }
            };
        }

        /// <summary>读取设备配置并保存到文件。</summary>
        public ICommand SaveCommand { get; }

        /// <summary>从文件恢复设备配置。</summary>
        public ICommand RestoreCommand { get; }

        /// <summary>
        /// 修改配置:按 <see cref="EditSource"/> 读取实机数据或选择配置文件,在弹窗中修改勾选的项并写入设备
        /// (来自文件时同时保存文件)。
        /// </summary>
        public ICommand EditCommand { get; }

        /// <summary>修改配置来源的选项。</summary>
        public IReadOnlyList<Choice<EditSource>> EditSources { get; } = new[]
        {
            new Choice<EditSource>(EditSource.Device, Lang.T("实机数据", "Device data"), Lang.T("修改设备的当前配置", "Edit the current device configuration")),
            new Choice<EditSource>(EditSource.File, Lang.T("保存的配置", "Saved configuration"), Lang.T("修改配置文件,同时写入设备", "Edit a configuration file; also writes to the device")),
        };

        /// <summary>修改配置的数据来源。</summary>
        public EditSource EditSource
        {
            get => _editSource;
            set => SetProperty(ref _editSource, value);
        }

        /// <summary>取消正在进行的保存/恢复/修改 (读取设备配置)。</summary>
        public ICommand CancelCommand { get; }

        /// <summary>正在进行的保存/恢复/修改的状态 (说明、进度),供界面绑定。</summary>
        public OperationRunner Operation { get; } = new OperationRunner();

        /// <summary>是否有保存/恢复/修改正在进行。进行期间禁止发送指令和断开。</summary>
        public bool IsBusy => Operation.IsBusy;

        private bool CanStart => _isConnected() && !IsBusy;

        /// <summary>
        /// 退出或重启前调用:取消正在进行的操作并等待它 (含其后的对话框、修改窗口) 结束。
        /// 等待期间不再显示对话框和修改窗口,结果只写入数据显示区,避免在关闭中的主窗口上弹出模态框。
        /// </summary>
        public async Task CancelAndWaitAsync()
        {
            _quiet = true;
            try
            {
                Operation.Cancel();
                await _idle;
            }
            finally
            {
                _quiet = false;
            }
        }

        /// <summary>记录正在进行的操作 (整个命令,不只是 <see cref="ReadDeviceAsync"/> 部分),供 <see cref="CancelAndWaitAsync"/> 等待。</summary>
        private async Task TrackAsync(Func<Task> operation)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _idle = done.Task;
            try
            {
                await operation();
            }
            finally
            {
                done.TrySetResult(true);
            }
        }

        /// <summary>保存配置 (<see cref="SaveCommand"/> 的实现,测试中直接调用以便等待完成)。</summary>
        internal async Task SaveAsync()
        {
            var title = Lang.T("保存仪器配置", "Save instrument configuration");
            var path = _dialogs.ShowSaveFileDialog(
                title,
                RecipeDialogFilter,
                _recipes.Folder,
                "IMC300_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + _recipes.FileExtension);
            if (path == null)
            {
                return;
            }

            await ReadDeviceAsync(title, (snapshot, cancellationToken) =>
            {
                var recipe = snapshot.ToRecipe(DateTimeOffset.Now);
                var saved = _recipes.Save(recipe, path);
                if (!saved.IsSuccess)
                {
                    ReportFailure(title, saved.Error);
                    return Task.CompletedTask;
                }

                var message = Lang.T($"已保存 {recipe.Entries.Count} 项配置 ({recipe.DeviceName} {recipe.FirmwareVersion}) 到 {path}", $"Saved {recipe.Entries.Count} settings ({recipe.DeviceName} {recipe.FirmwareVersion}) to {path}");
                _log.Add(LogKind.Info, message);
                return Task.CompletedTask;
            });
        }

        /// <summary>修改配置 (<see cref="EditCommand"/> 的实现,测试中直接调用以便等待完成)。</summary>
        internal async Task EditAsync()
        {
            var title = Lang.T("修改仪器配置", "Edit instrument configuration");
            if (EditSource == EditSource.File)
            {
                // 打开前同样校验格式与 MD5:被改动过的文件不能用来修改设备。
                if (LoadRecipe(title) is var (path, recipe))
                {
                    OpenEditor(recipe, path, null);
                }

                return;
            }

            // 实机数据:先读取设备当前配置 (可取消,有时限),读取结束后再打开窗口。
            InstrumentRecipe? read = null;
            InstrumentSnapshot? device = null;
            await ReadDeviceAsync(title, (snapshot, cancellationToken) =>
            {
                device = snapshot;
                read = snapshot.ToRecipe(DateTimeOffset.Now);
                _log.Add(LogKind.Info, Lang.T($"已读取设备配置 {read.Entries.Count} 项 ({read.DeviceName} {read.FirmwareVersion}),打开修改窗口。", $"Read {read.Entries.Count} settings from the device ({read.DeviceName} {read.FirmwareVersion}); opening the edit window."));
                return Task.CompletedTask;
            });

            if (read != null)
            {
                OpenEditor(read, null, device);
            }
        }

        /// <summary>
        /// 打开修改窗口 (模态);<paramref name="path"/> 为 null 时修改的是实机数据。
        /// <paramref name="device"/> 是刚读取的完整设备数据 (实机数据模式):"信息"页直接用它,不再重复查询;
        /// 为 null 时 (保存的配置) "信息"页在窗口打开的同时用 Q 指令读取。
        /// </summary>
        private void OpenEditor(InstrumentRecipe recipe, string? path, InstrumentSnapshot? device)
        {
            if (_quiet)
            {
                return; // 程序正在退出
            }

            using (var editor = new RecipeEditorViewModel(RecipeEditSession.Open(recipe, path, _config, _recipes), _dialogs, _errorLog))
            {
                if (device != null)
                {
                    editor.Info.ApplySnapshot(device, DateTime.Now);
                }
                else
                {
                    // 窗口关闭时 Dispose 取消未完成的读取。
                    editor.Info.RefreshCommand.Execute(null);
                }

                _dialogs.ShowRecipeEditor(editor);
                if (editor.CompletionMessage != null)
                {
                    _log.Add(LogKind.Info, editor.CompletionMessage);
                }
            }
        }

        /// <summary>恢复配置 (<see cref="RestoreCommand"/> 的实现,测试中直接调用以便等待完成)。</summary>
        internal async Task RestoreAsync()
        {
            var title = Lang.T("恢复仪器配置", "Restore instrument configuration");
            if (!(LoadRecipe(title) is var (path, recipe)))
            {
                return;
            }

            await ReadDeviceAsync(title, async (snapshot, cancellationToken) =>
            {
                var planned = RestorePlanner.Plan(recipe, snapshot);
                if (!planned.IsSuccess)
                {
                    ReportFailure(title, planned.Error);
                    return;
                }

                var plan = planned.Value;
                LogPlan(plan);
                if (plan.Writes.Count == 0 && plan.Creates.Count == 0)
                {
                    _log.Add(LogKind.Info, Lang.T($"设备当前配置与文件一致 ({plan.UnchangedCount} 项),无需修改。", $"The device already matches the file ({plan.UnchangedCount} settings); nothing to change."));
                    return;
                }

                if (_quiet)
                {
                    return; // 程序正在退出:不再询问,也不写入
                }

                bool confirmed;
                using (Operation.Current?.Pause())
                {
                    // 用户阅读确认框的时间不计入操作时限。
                    confirmed = _dialogs.Confirm(title, RestoreMessages.BuildConfirmation(path, recipe, snapshot, plan));
                }

                if (!confirmed)
                {
                    _log.Add(LogKind.Info, Lang.T("已取消恢复,设备未做任何修改。", "Restore cancelled; the device was not changed."));
                    return;
                }

                // 第二阶段:创建、重新读取、写入、校验,重新计时。
                Operation.Current?.StartPhase(ExecutePhase);
                var report = await _config.ExecuteRestoreAsync(recipe, plan, Operation.CreateProgress(Lang.T(" (约 10 秒)…", " (about 10 s)…")), cancellationToken);
                ReportRestore(title, report);
            });
        }

        /// <summary>选择并读取配置文件 (校验格式与 MD5);取消或失败 (已提示) 时为 null。</summary>
        private (string Path, InstrumentRecipe Recipe)? LoadRecipe(string title)
        {
            var path = _dialogs.ShowOpenFileDialog(title, RecipeDialogFilter, _recipes.Folder);
            if (path == null)
            {
                return null;
            }

            var loaded = _recipes.Load(path);
            if (!loaded.IsSuccess)
            {
                ReportFailure(title, loaded.Error);
                return null;
            }

            return (path, loaded.Value);
        }

        /// <summary>
        /// 在可取消、分阶段计时 (每阶段 <see cref="OperationLimits.PhaseTimeLimit"/>) 的操作中读取设备配置 (<c>?</c>,约 10 秒),
        /// 成功后执行 <paramref name="next"/>;读取失败时提示。期间 <see cref="IsBusy"/> 为 true。
        /// </summary>
        /// <param name="title">操作名称,用于提示。</param>
        /// <param name="next">读取成功后的步骤;等待用户时应用 <see cref="OperationTimeout.Pause"/> 暂停计时。</param>
        private Task ReadDeviceAsync(string title, Func<InstrumentSnapshot, CancellationToken, Task> next) =>
            Operation.RunAsync(ReadPhase, Lang.T("读取设备配置 (约 10 秒)…", "Reading the device configuration (about 10 s)…"), async timeout =>
            {
                var snapshot = await _config.ReadSnapshotAsync(timeout.Token);
                if (snapshot.IsSuccess)
                {
                    await next(snapshot.Value, timeout.Token);
                }
                else
                {
                    ReportFailure(title, snapshot.Error);
                }
            }, timeout =>
            {
                if (timeout.IsTimedOut)
                {
                    ReportFailure(title, OperationLimits.BuildTimeoutMessage(timeout));
                }
                else
                {
                    _log.Add(LogKind.Info, Lang.T("操作已取消。设备可能仍在发送剩余的配置数据,这些数据不显示。", "Operation cancelled. The device may still send the rest of the configuration data; it is not shown."));
                }
            });

        private void LogPlan(RestorePlan plan)
        {
            _log.Add(LogKind.Info, Lang.T($"恢复计划: 创建 {plan.Creates.Count} 个对象,修改 {plan.Writes.Count} 项,{plan.UnchangedCount} 项与设备相同,跳过 {plan.Skipped.Count} 项。", $"Restore plan: create {plan.Creates.Count} objects, change {plan.Writes.Count} settings, {plan.UnchangedCount} already match, skip {plan.Skipped.Count}."));
            foreach (var create in plan.Creates)
            {
                _log.Add(LogKind.Info, Lang.T($"将创建{create.Kind} {create.Id} ({create.Command})", $"Will create {create.Kind} {create.Id} ({create.Command})"));
            }

            foreach (var warning in plan.Warnings)
            {
                _log.Add(LogKind.Error, warning);
            }

            foreach (var skip in plan.Skipped)
            {
                _log.Add(LogKind.Info, Lang.T("跳过 ", "Skipped ") + skip);
            }
        }

        private void ReportRestore(string title, RestoreReport report)
        {
            // 创建对象后仍无法写入的项是用户关心的结果,用错误级别显示 (同时记入 error.txt)。
            foreach (var skip in report.AdditionalSkipped)
            {
                _log.Add(LogKind.Error, Lang.T("无法写入 ", "Cannot write ") + skip);
            }

            foreach (var mismatch in report.VerifyMismatches)
            {
                _log.Add(LogKind.Error, Lang.T("校验不一致 ", "Verification mismatch ") + mismatch);
            }

            var cancelReason = Operation.Current is { } operation ? OperationRunner.DescribeCancel(operation) : Lang.T("已取消。", "Cancelled.");
            var failure = RestoreMessages.BuildFailure(report, cancelReason);
            if (failure != null)
            {
                ReportFailure(title, failure);
                return;
            }

            // 成功只记录到数据显示区,不弹窗;有项无法写入时仍然弹窗 (各项已在上面记录为错误)。
            _log.Add(LogKind.Info, RestoreMessages.BuildSummary(report));
            if (report.AdditionalSkipped.Count > 0 && !_quiet)
            {
                _dialogs.ShowError(title, RestoreMessages.BuildSuccess(report));
            }
        }

        private void ReportFailure(string title, string? error)
        {
            var message = error ?? Lang.T("未知错误。", "Unknown error.");
            _log.Add(LogKind.Error, message);
            if (!_quiet)
            {
                _dialogs.ShowError(title, message);
            }
        }
    }
}
