using System;
using System.Diagnostics;
using System.Threading;

namespace Zevision.ViewModels.Common
{
    /// <summary>
    /// 长时间操作 (保存/恢复仪器配置) 的分阶段时限与取消。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>每个阶段有同样的时限:创建时开始第一个阶段,<see cref="StartPhase"/> 开始下一个阶段并重新计时。
    /// 超时后 <see cref="Token"/> 被取消,<see cref="IsTimedOut"/> 为 true,<see cref="Phase"/> 指出超时的阶段。</item>
    /// <item>用户点"取消"调用 <see cref="Cancel"/>,<see cref="IsCancelledByUser"/> 为 true,以便给出不同提示。</item>
    /// <item>等待用户操作 (如确认框) 时用 <see cref="Pause"/> 暂停计时:用户思考的时间不应计入通讯时限。</item>
    /// </list>
    /// 只应在 UI 线程上使用。
    /// </remarks>
    public sealed class OperationTimeout : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly TimeSpan _budget;
        private bool _disposed;

        /// <summary>
        /// 开始第一个阶段并计时。
        /// </summary>
        /// <param name="budget">每个阶段的时限。</param>
        /// <param name="phase">第一个阶段的名称,用于超时提示。</param>
        public OperationTimeout(TimeSpan budget, string phase)
        {
            if (budget <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(budget));
            }

            _budget = budget;
            Phase = phase ?? throw new ArgumentNullException(nameof(phase));
            _cancellation.CancelAfter(budget);
        }

        /// <summary>传给操作的取消令牌:超时或用户取消时被取消。</summary>
        public CancellationToken Token => _cancellation.Token;

        /// <summary>每个阶段的时限。</summary>
        public TimeSpan Budget => _budget;

        /// <summary>当前 (或超时时所在的) 阶段名称。</summary>
        public string Phase { get; private set; }

        /// <summary>
        /// 结束当前阶段,开始下一个阶段并重新计时。已超时或已取消时不做任何事 (取消状态不会被"复活")。
        /// 不要在 <see cref="Pause"/> 期间调用。
        /// </summary>
        public void StartPhase(string phase)
        {
            if (phase is null)
            {
                throw new ArgumentNullException(nameof(phase));
            }

            if (_disposed || _cancellation.IsCancellationRequested)
            {
                return;
            }

            Phase = phase;
            _elapsed.Restart();
            _cancellation.CancelAfter(_budget);
        }

        /// <summary>是否由用户取消。</summary>
        public bool IsCancelledByUser { get; private set; }

        /// <summary>是否因超时而取消。</summary>
        public bool IsTimedOut => _cancellation.IsCancellationRequested && !IsCancelledByUser;

        /// <summary>用户取消。</summary>
        public void Cancel()
        {
            if (_disposed || _cancellation.IsCancellationRequested)
            {
                return;
            }

            IsCancelledByUser = true;
            _cancellation.Cancel();
        }

        /// <summary>
        /// 暂停计时,直到返回的对象被释放。用于包住确认框等等待用户的步骤。
        /// </summary>
        public IDisposable Pause()
        {
            if (_disposed || _cancellation.IsCancellationRequested)
            {
                return new Resumer(null);
            }

            _cancellation.CancelAfter(Timeout.Infinite);
            _elapsed.Stop();
            return new Resumer(this);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellation.Dispose();
        }

        private void Resume()
        {
            if (_disposed || _cancellation.IsCancellationRequested)
            {
                return;
            }

            _elapsed.Start();
            var remaining = _budget - _elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                _cancellation.Cancel();
            }
            else
            {
                _cancellation.CancelAfter(remaining);
            }
        }

        private sealed class Resumer : IDisposable
        {
            private OperationTimeout? _owner;

            public Resumer(OperationTimeout? owner) => _owner = owner;

            public void Dispose()
            {
                _owner?.Resume();
                _owner = null;
            }
        }
    }
}
