using System;
using System.Threading.Tasks;
using Zevision.Localization;
using Zevision.Services;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 一次可取消、分阶段计时 (每阶段 <see cref="OperationLimits.PhaseTimeLimit"/>) 的长时间设备操作的状态,供界面绑定:
    /// 是否在进行、说明文字和进度。主窗口的配置操作和修改窗口共用,保证结束时状态一定复位。
    /// </summary>
    public sealed class OperationRunner : ViewModelBase
    {
        private bool _isBusy;
        private string _text = string.Empty;
        private double _progress;
        private bool _isIndeterminate;

        /// <summary>是否有操作在进行。变化时刷新命令的可执行状态。</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RefreshCommands();
                }
            }
        }

        /// <summary>当前步骤的说明。</summary>
        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value ?? string.Empty);
        }

        /// <summary>进度 (0~100)。</summary>
        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        /// <summary>进度是否未知 (例如等待 <c>?</c> 的响应)。</summary>
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            private set => SetProperty(ref _isIndeterminate, value);
        }

        /// <summary>正在进行的操作的时限和取消;没有操作时为 null。</summary>
        public OperationTimeout? Current { get; private set; }

        /// <summary>是否可以取消:有操作在进行且尚未取消。</summary>
        public bool CanCancel => IsBusy && Current?.Token.IsCancellationRequested == false;

        /// <summary>取消正在进行的操作 (没有时什么也不做)。</summary>
        public void Cancel() => Current?.Cancel();

        /// <summary>
        /// 执行操作:期间 <see cref="IsBusy"/> 为 true,结束 (成功、失败或取消) 后状态复位。
        /// </summary>
        /// <param name="phase">第一阶段的名称,用于超时提示。</param>
        /// <param name="text">开始时显示的说明。</param>
        /// <param name="operation">操作本体,用 <see cref="OperationTimeout.Token"/> 取消;等待用户时用 <see cref="OperationTimeout.Pause"/> 暂停计时。</param>
        /// <param name="onCancelled">
        /// 操作因超时或用户取消抛出 <see cref="OperationCanceledException"/> 时调用 (参数可区分超时与取消);
        /// 为 null 时异常照常抛出。
        /// </param>
        public async Task RunAsync(string phase, string text, Func<OperationTimeout, Task> operation, Action<OperationTimeout>? onCancelled = null)
        {
            if (operation is null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            using (var timeout = new OperationTimeout(OperationLimits.PhaseTimeLimit, phase))
            {
                Current = timeout;
                Text = text;
                Progress = 0;
                IsIndeterminate = true;
                IsBusy = true;
                try
                {
                    await operation(timeout);
                }
                catch (OperationCanceledException) when (onCancelled != null)
                {
                    onCancelled(timeout);
                }
                finally
                {
                    Current = null;
                    IsBusy = false;
                    Text = string.Empty;
                    Progress = 0;
                }
            }
        }

        /// <summary>
        /// 把服务报告的进度显示出来。<see cref="RestoreProgress.Total"/> 为 0 表示进度未知,说明后加 <paramref name="unknownSuffix"/>。
        /// </summary>
        public IProgress<RestoreProgress> CreateProgress(string unknownSuffix) => new Progress<RestoreProgress>(p =>
        {
            IsIndeterminate = p.Total == 0;
            Text = p.Total == 0 ? p.Stage + unknownSuffix : $"{p.Stage} {p.Completed}/{p.Total}";
            Progress = p.Total == 0 ? 0 : 100.0 * p.Completed / p.Total;
        });

        /// <summary>取消的原因:超时提示或"已取消。"。</summary>
        public static string DescribeCancel(OperationTimeout timeout) =>
            timeout.IsTimedOut ? OperationLimits.BuildTimeoutMessage(timeout) : Lang.T("已取消。", "Cancelled.");
    }
}
