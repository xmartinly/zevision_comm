using System;

namespace Zevision.Core.Devices
{
    /// <summary>
    /// 连接中断后自动重连的等待时间:指数退避 1 → 2 → 4 → … 秒,上限 <see cref="MaxDelay"/>。
    /// </summary>
    public static class ReconnectPolicy
    {
        /// <summary>两次重连之间最长的等待。</summary>
        public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

        /// <summary>第 <paramref name="attempt"/> 次重连前的等待:1、2、4、8、16、32 秒,之后都是 60 秒。</summary>
        /// <param name="attempt">第几次重连,从 1 开始。</param>
        public static TimeSpan DelayBefore(int attempt)
        {
            if (attempt < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempt));
            }

            // 2^6 = 64 秒已超过上限,更大的次数不再计算 (避免移位溢出)。
            var seconds = attempt > 6 ? MaxDelay.TotalSeconds : Math.Min(1 << (attempt - 1), MaxDelay.TotalSeconds);
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
