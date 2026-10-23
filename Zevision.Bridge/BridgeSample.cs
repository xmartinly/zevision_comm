using System;
using Zevision.Xtc3;

namespace Zevision.Bridge
{
    /// <summary>
    /// 一次采样 (一个 tick) 的 IMC300 数据 (ZEV-FAR-01 的 5.4)。每 tick 采样的项每次都有;每 4 个 tick 采样的项
    /// 在其他 tick 沿用上一次的值。
    /// </summary>
    internal sealed class BridgeSample
    {
        /// <summary>绝对 tick (从中间层启动起,不回绕)。</summary>
        public long Tick { get; init; }

        /// <summary>采样完成的时刻 (中间层的单调时钟)。</summary>
        public TimeSpan Time { get; init; }

        /// <summary>当前功率 <c>SC11</c>,%。</summary>
        public float Power { get; init; }

        /// <summary>滤波后的速率 <c>SC12</c>,Å/s。</summary>
        public float FilteredRate { get; init; }

        /// <summary>原始速率 <c>SC13</c>,Å/s。</summary>
        public float RawRate { get; init; }

        /// <summary>当前厚度 <c>SC15</c>,kÅ。</summary>
        public float Thickness { get; init; }

        /// <summary>原始频率 <c>SC16</c>,Hz。</summary>
        public double Frequency { get; init; }

        /// <summary>当前传感器的速率偏差 <c>SNss20</c>。</summary>
        public float RateDeviation { get; init; }

        /// <summary>当前状态文本 <c>QC09</c>。</summary>
        public string StateText { get; init; } = string.Empty;

        /// <summary>对应的 XTC/3 状态;文本不认识时为 null。停止 (<c>Stopped</c>) 时为停止前的状态。</summary>
        public Xtc3State? State { get; init; }

        /// <summary>状态文本为 <c>Stopped</c>。</summary>
        public bool StoppedText { get; init; }

        /// <summary>活动晶振失效 <c>QC18</c>。</summary>
        public bool CrystalFailed { get; init; }

        /// <summary>停止原因 <c>QC1F</c>;0 为没有停止,编码与 XTC/3 S19 相同。</summary>
        public int StopCause { get; init; }

        /// <summary>当前传感器 (ss)。</summary>
        public int Sensor { get; init; }

        /// <summary>当前传感器的当前位置 <c>QNss09</c> (活动晶振,从 1 起)。</summary>
        public int ActiveCrystal { get; init; }

        /// <summary>活动晶振寿命 <c>QC17</c>,%。</summary>
        public int CrystalLife { get; init; }

        /// <summary>当前稳定性总计 <c>QC20</c> (S19 的 S Value)。</summary>
        public int StabilityTotal { get; init; }

        /// <summary>当前质量计数 <c>QC21</c> (S19 的 Q Value)。</summary>
        public int QualityCounts { get; init; }

        /// <summary>活动工艺 <c>QP01</c>;没有读到时为 0。</summary>
        public int ActiveProcess { get; init; }

        /// <summary>测试模式 <c>QC1C</c>。</summary>
        public bool TestMode { get; init; }

        /// <summary>输出状态位图 (S11):输出 n 为第 n−1 位,由 <c>QOnn05</c> 拼成。</summary>
        public int Outputs { get; init; }

        /// <summary>输入状态位图 (S12):输入 1~8 为 0x0100~0x8000,输入 9 为 0x0001,由 <c>QInn04</c> 拼成。</summary>
        public int Inputs { get; init; }

        /// <summary>是否已停止 (S18):状态文本为 <c>Stopped</c>,或停止原因不为 0。</summary>
        public bool IsStopped => StoppedText || StopCause != 0;
    }
}
