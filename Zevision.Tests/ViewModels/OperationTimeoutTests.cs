using System;
using System.Threading.Tasks;
using Xunit;
using Zevision.ViewModels.Common;

namespace Zevision.Tests
{
    public class OperationTimeoutTests
    {
        [Fact]
        public async Task ExceedingBudget_CancelsAsTimeout()
        {
            using (var timeout = new OperationTimeout(TimeSpan.FromMilliseconds(100), "read"))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(5000, timeout.Token));

                Assert.True(timeout.IsTimedOut);
                Assert.False(timeout.IsCancelledByUser);
            }
        }

        [Fact]
        public void UserCancel_IsNotTimeout()
        {
            using (var timeout = new OperationTimeout(TimeSpan.FromSeconds(30), "read"))
            {
                timeout.Cancel();

                Assert.True(timeout.Token.IsCancellationRequested);
                Assert.True(timeout.IsCancelledByUser);
                Assert.False(timeout.IsTimedOut);
            }
        }

        [Fact]
        public async Task Pause_TimeDoesNotCountTowardBudget()
        {
            using (var timeout = new OperationTimeout(TimeSpan.FromMilliseconds(300), "read"))
            {
                using (timeout.Pause())
                {
                    await Task.Delay(500); // 超过整体时限,但处于暂停中
                }

                Assert.False(timeout.Token.IsCancellationRequested);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(5000, timeout.Token));
                Assert.True(timeout.IsTimedOut);
            }
        }

        [Fact]
        public async Task Resume_AfterBudgetAlreadyUsed_CancelsImmediately()
        {
            using (var timeout = new OperationTimeout(TimeSpan.FromMilliseconds(200), "read"))
            {
                // 在暂停前就已耗尽时限 (计时器还没来得及触发也一样)。
                System.Threading.Thread.Sleep(250);
                using (timeout.Pause())
                {
                }

                await Task.Delay(50);
                Assert.True(timeout.Token.IsCancellationRequested);
                Assert.True(timeout.IsTimedOut);
            }
        }

        [Fact]
        public async Task StartPhase_RestartsBudget()
        {
            // 两段各约 600 ms,合计超过单段时限 1000 ms,但每段都没超。
            // 并行运行的测试多、线程池繁忙时 Task.Delay 可能远超 600 ms:用实际耗时判断,
            // 某一段真的超过时限时结果没有意义,不做断言 (避免偶发失败)。
            var budget = TimeSpan.FromMilliseconds(1000);
            using (var timeout = new OperationTimeout(budget, "read"))
            {
                var phase = System.Diagnostics.Stopwatch.StartNew();
                await Task.Delay(600);
                if (phase.Elapsed >= budget)
                {
                    return;
                }

                timeout.StartPhase("execute");
                phase.Restart();
                await Task.Delay(600);
                if (phase.Elapsed >= budget)
                {
                    return;
                }

                Assert.False(timeout.Token.IsCancellationRequested);
                Assert.Equal("execute", timeout.Phase);
            }
        }

        [Fact]
        public async Task StartPhase_TimeoutReportsCurrentPhase()
        {
            using (var timeout = new OperationTimeout(TimeSpan.FromMilliseconds(100), "read"))
            {
                timeout.StartPhase("execute");

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(5000, timeout.Token));
                Assert.True(timeout.IsTimedOut);
                Assert.Equal("execute", timeout.Phase);
            }
        }

        [Fact]
        public async Task StartPhase_AfterTimeout_DoesNotRevive()
        {
            using (var timeout = new OperationTimeout(TimeSpan.FromMilliseconds(50), "read"))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(5000, timeout.Token));

                timeout.StartPhase("execute");

                Assert.True(timeout.IsTimedOut);
                Assert.Equal("read", timeout.Phase); // 仍报告真正超时的阶段
            }
        }

        [Fact]
        public void Pause_AfterCancel_IsHarmless()
        {
            using (var timeout = new OperationTimeout(TimeSpan.FromSeconds(30), "read"))
            {
                timeout.Cancel();
                using (timeout.Pause())
                {
                }

                Assert.True(timeout.IsCancelledByUser);
            }
        }
    }
}
