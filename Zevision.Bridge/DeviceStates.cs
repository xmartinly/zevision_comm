using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Zevision.Xtc3;

namespace Zevision.Bridge
{
    /// <summary>
    /// IMC300 的当前状态文本 (<c>C09</c>) 与 XTC/3 状态 (S4) 的对应。
    /// </summary>
    /// <remarks>
    /// <para>实测 (2026-10-23,固件 1.8.11,测试模式,ZEV-STR-01 的 STR-2026-10-23-04) 一层的文本依次为 <c>Ready</c>、
    /// <c>Precon Rise 1</c>、<c>Precon Soak 1</c>、<c>Precon Rise 2</c>、<c>Precon Soak 2</c>、<c>Dep Rate Hold 1</c>、<c>Feed Ramp 1</c>、
    /// <c>Idle</c>;停止后为 <c>Stopped</c>,复位后为 <c>Ready</c>。预熔为 Rise/Soak (第 1 步为 1,其后为 2),<c>Dep</c> 开头为沉积,
    /// <c>Feed</c> 开头 (后处理) 为 Idle Ramp。</para>
    /// <para>XTC/3 的状态名称 (与 IMC300 输出功能 17~29 相同) 也按名称对应。比较时不区分大小写、空格和连字符。
    /// 不认识的文本由调用方记录。</para>
    /// </remarks>
    internal static class DeviceStates
    {
        private static readonly Dictionary<string, Xtc3State> ByName = new Dictionary<string, Xtc3State>(StringComparer.Ordinal)
        {
            [Key("Ready")] = Xtc3State.Ready,
            [Key("Crucible Switch")] = Xtc3State.CrucibleSwitch,
            [Key("Crucible Switching")] = Xtc3State.CrucibleSwitch,
            [Key("Rise 1")] = Xtc3State.Rise1,
            [Key("Soak 1")] = Xtc3State.Soak1,
            [Key("Rise 2")] = Xtc3State.Rise2,
            [Key("Soak 2")] = Xtc3State.Soak2,
            [Key("Shutter Delay")] = Xtc3State.ShutterDelay,
            [Key("Deposit")] = Xtc3State.Deposit,
            [Key("Rate Ramp")] = Xtc3State.RateRamp,
            [Key("Manual")] = Xtc3State.Manual,
            [Key("Time Power")] = Xtc3State.TimePower,
            [Key("Idle Ramp")] = Xtc3State.IdleRamp,
            [Key("Idle")] = Xtc3State.Idle,
        };

        /// <summary>停止后的文本 (<c>Stopped</c>):XTC/3 没有这个状态,S4 保留停止前的状态,S18 为 1。</summary>
        public static bool IsStopped(string? text) => text != null && Key(text) == "STOPPED";

        /// <summary>按状态文本找 XTC/3 状态;不认识 (含 <c>Stopped</c>) 时为 false。</summary>
        public static bool TryMap(string? text, out Xtc3State state)
        {
            state = Xtc3State.Ready;
            if (text == null)
            {
                return false;
            }

            var key = Key(text);
            if (ByName.TryGetValue(key, out state))
            {
                return true;
            }

            if (key.StartsWith("PRECON", StringComparison.Ordinal))
            {
                var first = (StepNumber(text) ?? 1) <= 1;
                if (key.Contains("RISE") || key.Contains("RAMP"))
                {
                    state = first ? Xtc3State.Rise1 : Xtc3State.Rise2;
                    return true;
                }

                if (key.Contains("SOAK") || key.Contains("HOLD"))
                {
                    state = first ? Xtc3State.Soak1 : Xtc3State.Soak2;
                    return true;
                }

                return false;
            }

            if (key.StartsWith("DEP", StringComparison.Ordinal))
            {
                state = key.Contains("MANUAL") ? Xtc3State.Manual
                    : key.Contains("TIMEP") ? Xtc3State.TimePower
                    : key.Contains("RAMP") ? Xtc3State.RateRamp
                    : Xtc3State.Deposit;
                return true;
            }

            if (key.StartsWith("FEED", StringComparison.Ordinal))
            {
                state = Xtc3State.IdleRamp;
                return true;
            }

            if (key.Contains("MANUAL"))
            {
                state = Xtc3State.Manual;
                return true;
            }

            if (key.Contains("SHUTTERDELAY") || key.Contains("CONTROLDELAY") || key.Contains("CTRLDELAY"))
            {
                state = Xtc3State.ShutterDelay;
                return true;
            }

            if (key.Contains("CRUCIBLE"))
            {
                state = Xtc3State.CrucibleSwitch;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 运行中:从坩埚切换到怠速斜坡 (1~11)。传感器、Transfer Sensor 运行中不能修改 (ZEV-FAR-01 的 5.3 e)。
        /// </summary>
        public static bool IsRunning(Xtc3State state) => state >= Xtc3State.CrucibleSwitch && state <= Xtc3State.IdleRamp;

        /// <summary>沉积中 (S0 的速率和速率偏差只在这些状态给出):沉积、速率斜坡。</summary>
        public static bool IsDepositing(Xtc3State state) => state == Xtc3State.Deposit || state == Xtc3State.RateRamp;

        private static string Key(string text) => new string(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        /// <summary>文本末尾的步骤号 (<c>Precon Rise 2</c> → 2);没有时为 null。</summary>
        private static int? StepNumber(string text)
        {
            var digits = new string(text.Trim().Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return digits.Length > 0 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : (int?)null;
        }
    }
}
