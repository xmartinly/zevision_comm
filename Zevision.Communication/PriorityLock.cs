using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Zevision.Communication
{
    /// <summary>
    /// 带优先级的异步互斥锁:同一时间只有一个持有者;释放时交给等待者中优先级最高、排队最早的一个。
    /// </summary>
    internal sealed class PriorityLock
    {
        private readonly object _sync = new object();

        /// <summary>等待者,按优先级从高到低排列,同级按排队先后。</summary>
        private readonly LinkedList<Waiter> _waiters = new LinkedList<Waiter>();
        private bool _held;

        /// <summary>锁空闲 (没有持有者) 时立即取得;否则不等待。</summary>
        public bool TryEnter()
        {
            lock (_sync)
            {
                if (_held)
                {
                    return false;
                }

                _held = true;
                return true;
            }
        }

        /// <summary>取得锁;被占用时按 <paramref name="priority"/> 排队。</summary>
        /// <exception cref="System.OperationCanceledException">排队时被取消 (此时没有取得锁)。</exception>
        public async Task EnterAsync(SendPriority priority, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Waiter waiter;
            lock (_sync)
            {
                if (!_held)
                {
                    _held = true;
                    return;
                }

                waiter = new Waiter(priority);
                var next = _waiters.First;
                while (next != null && next.Value.Priority >= priority)
                {
                    next = next.Next;
                }

                waiter.Node = next == null ? _waiters.AddLast(waiter) : _waiters.AddBefore(next, waiter);
            }

            using (cancellationToken.Register(() => Cancel(waiter, cancellationToken)))
            {
                await waiter.Granted.Task.ConfigureAwait(false);
            }
        }

        /// <summary>释放锁:有等待者时直接交给下一个。</summary>
        public void Exit()
        {
            Waiter next;
            lock (_sync)
            {
                var first = _waiters.First;
                if (first == null)
                {
                    _held = false;
                    return;
                }

                _waiters.RemoveFirst();
                next = first.Value;
            }

            next.Granted.TrySetResult(true);
        }

        /// <summary>取消排队;已经取得锁的等待者不受影响。</summary>
        private void Cancel(Waiter waiter, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (waiter.Node?.List == null)
                {
                    return;
                }

                _waiters.Remove(waiter.Node);
            }

            waiter.Granted.TrySetCanceled(cancellationToken);
        }

        private sealed class Waiter
        {
            public Waiter(SendPriority priority)
            {
                Priority = priority;
            }

            public SendPriority Priority { get; }

            /// <summary>在队列中的位置;取得锁或取消后不再属于队列 (<see cref="LinkedListNode{T}.List"/> 为 null)。</summary>
            public LinkedListNode<Waiter>? Node { get; set; }

            public TaskCompletionSource<bool> Granted { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
