using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Zevision.Xtc3
{
    /// <summary>Film 参数 (UF/QF) 的 Command ID,docs/XTC3.md 第 7 章。</summary>
    public enum Xtc3FilmParameterId
    {
        /// <summary>Rise Time 1,秒。</summary>
        RiseTime1 = 0,

        /// <summary>Soak Power 1,%。</summary>
        SoakPower1 = 1,

        /// <summary>Soak Time 1,秒。</summary>
        SoakTime1 = 2,

        /// <summary>Rise Time 2,秒。</summary>
        RiseTime2 = 3,

        /// <summary>Soak Power 2,%。</summary>
        SoakPower2 = 4,

        /// <summary>Soak Time 2,秒。</summary>
        SoakTime2 = 5,

        /// <summary>Idle Ramp,秒。</summary>
        IdleRamp = 6,

        /// <summary>Idle Power,%。</summary>
        IdlePower = 7,

        /// <summary>Deposition Rate,Å/s。</summary>
        DepositionRate = 8,

        /// <summary>Final Thickness,kÅ。</summary>
        FinalThickness = 9,

        /// <summary>Thickness Set Point,kÅ。</summary>
        ThicknessSetPoint = 10,

        /// <summary>New Rate,Å/s。</summary>
        NewRate = 11,

        /// <summary>Rate Ramp Time,秒。</summary>
        RateRampTime = 12,

        /// <summary>Rate Watcher Accuracy,%。</summary>
        RateWatcherAccuracy = 13,

        /// <summary>RW Hold Time,秒。</summary>
        RateWatcherHoldTime = 14,

        /// <summary>Sensor,1~2。</summary>
        Sensor = 15,

        /// <summary>Tool Factor 1,%。</summary>
        ToolFactor1 = 16,

        /// <summary>Tool Factor 2,%。</summary>
        ToolFactor2 = 17,

        /// <summary>Xtal Stability Single。</summary>
        XtalStabilitySingle = 18,

        /// <summary>Xtal Stability Total。</summary>
        XtalStabilityTotal = 19,

        /// <summary>Xtal Quality Percent。</summary>
        XtalQualityPercent = 20,

        /// <summary>Xtal Quality Counts。</summary>
        XtalQualityCounts = 21,

        /// <summary>Source,1~2。</summary>
        Source = 22,

        /// <summary>Crucible,0~8。</summary>
        Crucible = 23,

        /// <summary>Control Gain,Å/s/%。</summary>
        ControlGain = 24,

        /// <summary>Control Time Constant,秒。</summary>
        ControlTimeConstant = 25,

        /// <summary>Control Dead Time,秒。</summary>
        ControlDeadTime = 26,

        /// <summary>Max Power,%。</summary>
        MaxPower = 27,

        /// <summary>Density,g/cm³。</summary>
        Density = 28,

        /// <summary>Z-Ratio。</summary>
        ZRatio = 29,

        /// <summary>Time Power,1 = Yes。</summary>
        TimePower = 30,

        /// <summary>Delay option,0 = None,1 = Shutter,2 = Control,3 = Both。</summary>
        DelayOption = 31,

        /// <summary>Transfer Sensor,1 = Yes。</summary>
        TransferSensor = 32,

        /// <summary>Transfer Tooling,%。</summary>
        TransferTooling = 33,

        /// <summary>Control Delay Time,秒。</summary>
        ControlDelayTime = 34,

        /// <summary>Ion Assisted Deposit,1 = Yes。</summary>
        IonAssistedDeposit = 35,

        /// <summary>Graph Label,0 = Rate Dev,1 = Power。</summary>
        GraphLabel = 36,

        /// <summary>Graph Scale,0 = 5,1 = 10,2 = 20,3 = 40。</summary>
        GraphScale = 37,
    }

    /// <summary>通用参数 (UG/QG) 的 Command ID,docs/XTC3.md 第 8 章 (XTC/3S)。</summary>
    public enum Xtc3GeneralParameterId
    {
        /// <summary>Film to run,1~9。</summary>
        FilmToRun = 0,

        /// <summary>Start Layer Without Backup Xtal。</summary>
        StartLayerWithoutBackupXtal = 1,

        /// <summary>Stop on Alarms。</summary>
        StopOnAlarms = 2,

        /// <summary>Stop On Max Power。</summary>
        StopOnMaxPower = 3,

        /// <summary>Dep/Etch Mode,0 = Deposit,1 = Etch。</summary>
        DepositEtchMode = 4,

        /// <summary>Test。</summary>
        Test = 5,

        /// <summary>Sensor 1 Type。</summary>
        Sensor1Type = 6,

        /// <summary>Sensor 2 Type。</summary>
        Sensor2Type = 7,

        /// <summary>Source Control Voltage。</summary>
        SourceControlVoltage = 8,

        /// <summary>Recorder Mode。</summary>
        RecorderMode = 9,

        /// <summary>Recorder Range。</summary>
        RecorderRange = 10,

        /// <summary>Recorder Filter。</summary>
        RecorderFilter = 11,

        /// <summary>Audio Feedback。</summary>
        AudioFeedback = 12,

        /// <summary>Dim LCD,分钟。</summary>
        DimLcd = 13,

        /// <summary>RS232 Baud Rate,0 = 9600 … 4 = 115200。</summary>
        BaudRate = 14,

        /// <summary>RS232 Communications Protocol,0 = Standard。</summary>
        Protocol = 15,
    }

    /// <summary>
    /// 一个 Film 参数或通用参数:类型和 XTC/3 的取值范围 (docs/XTC3.md 第 7、8 章)。
    /// </summary>
    public sealed class Xtc3Parameter
    {
        internal Xtc3Parameter(int id, string name, Xtc3ValueType type, decimal min, decimal max, int decimals = 0, string unit = "", bool zeroAllowed = false)
        {
            Id = id;
            Name = name;
            Type = type;
            Min = min;
            Max = max;
            Decimals = decimals;
            Unit = unit;
            ZeroAllowed = zeroAllowed;
        }

        /// <summary>Command ID。</summary>
        public int Id { get; }

        /// <summary>英文名称 (XTC/3 手册)。</summary>
        public string Name { get; }

        /// <summary>数据类型:<see cref="Xtc3ValueType.Integer"/> 或 <see cref="Xtc3ValueType.Float"/>。</summary>
        public Xtc3ValueType Type { get; }

        /// <summary>最小值 (<see cref="ZeroAllowed"/> 时另外允许 0)。</summary>
        public decimal Min { get; }

        /// <summary>最大值。</summary>
        public decimal Max { get; }

        /// <summary>XTC/3 显示的小数位数 (范围的写法,如 0.000~999.9 kÅ 为 3);整数为 0。</summary>
        public int Decimals { get; }

        /// <summary>单位。</summary>
        public string Unit { get; }

        /// <summary>除 <see cref="Min"/>~<see cref="Max"/> 外还允许 0 (Xtal Stability:0 或 25~9999)。</summary>
        public bool ZeroAllowed { get; }

        /// <summary>
        /// 值的类型是否正确并在范围内。浮点数按 <see cref="Decimals"/> 位小数四舍五入后比较 (主机写入的 100.0 可能是 100.00001)。
        /// </summary>
        /// <param name="value">主机写入的值。</param>
        public bool IsValid(Xtc3Value value)
        {
            if (value.Type != Type)
            {
                return false;
            }

            if (!TryToDecimal(value, Decimals, out var number))
            {
                return false;
            }

            return (ZeroAllowed && number == 0) || (number >= Min && number <= Max);
        }

        /// <summary>
        /// 数值转为 decimal:整数原样;浮点数取 7 位有效数字 (单精度的精度) 并按 <paramref name="decimals"/> 位小数四舍五入。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <param name="decimals">小数位数 (只用于浮点数)。</param>
        /// <param name="number">结果。</param>
        /// <returns>不是数值、为 NaN、无穷大或超出 ±1e9 时为 false。</returns>
        public static bool TryToDecimal(Xtc3Value value, int decimals, out decimal number)
        {
            number = 0;
            switch (value.Type)
            {
                case Xtc3ValueType.Byte:
                case Xtc3ValueType.Word:
                case Xtc3ValueType.Integer:
                    number = value.Integer;
                    return true;
                case Xtc3ValueType.Float:
                    var single = value.Float;
                    if (float.IsNaN(single) || float.IsInfinity(single) || Math.Abs(single) > 1e9f)
                    {
                        return false;
                    }

                    number = Math.Round((decimal)single, Math.Max(0, Math.Min(decimals, 9)), MidpointRounding.AwayFromZero);
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var range = Min.ToString(CultureInfo.InvariantCulture) + "~" + Max.ToString(CultureInfo.InvariantCulture);
            return Id.ToString(CultureInfo.InvariantCulture) + " " + Name + " (" + Type + ", " + (ZeroAllowed ? "0 or " : string.Empty) + range
                + (Unit.Length > 0 ? " " + Unit : string.Empty) + ")";
        }
    }

    /// <summary>Film 参数和通用参数的表。</summary>
    public static class Xtc3Parameters
    {
        /// <summary>QF/UF、QG/UG 的 ID 255:该组全部参数,按 ID 顺序。</summary>
        public const int AllId = 255;

        private const Xtc3ValueType I = Xtc3ValueType.Integer;
        private const Xtc3ValueType F = Xtc3ValueType.Float;
        private const decimal MaxSeconds = 5999; // 99:59

        /// <summary>Film 参数 0~37,下标即 ID。</summary>
        public static IReadOnlyList<Xtc3Parameter> Film { get; } = Array.AsReadOnly(new[]
        {
            new Xtc3Parameter(0, "Rise Time 1", I, 0, MaxSeconds, unit: "s"),
            new Xtc3Parameter(1, "Soak Power 1", F, 0, 100, 1, "%"),
            new Xtc3Parameter(2, "Soak Time 1", I, 0, MaxSeconds, unit: "s"),
            new Xtc3Parameter(3, "Rise Time 2", I, 0, MaxSeconds, unit: "s"),
            new Xtc3Parameter(4, "Soak Power 2", F, 0, 100, 1, "%"),
            new Xtc3Parameter(5, "Soak Time 2", I, 0, MaxSeconds, unit: "s"),
            new Xtc3Parameter(6, "Idle Ramp", I, 0, MaxSeconds, unit: "s"),
            new Xtc3Parameter(7, "Idle Power", F, 0, 100, 1, "%"),
            new Xtc3Parameter(8, "Deposition Rate", F, 0, 999.9m, 1, "A/s"),
            new Xtc3Parameter(9, "Final Thickness", F, 0, 999.9m, 3, "kA"),
            new Xtc3Parameter(10, "Thickness Set Point", F, 0, 999.9m, 3, "kA"),
            new Xtc3Parameter(11, "New Rate", F, 0, 999.9m, 1, "A/s"),
            new Xtc3Parameter(12, "Rate Ramp Time", I, 0, MaxSeconds, unit: "s"),
            new Xtc3Parameter(13, "Rate Watcher Accuracy", I, 1, 99, unit: "%"),
            new Xtc3Parameter(14, "RW Hold Time", I, 0, MaxSeconds, unit: "s"),
            new Xtc3Parameter(15, "Sensor", I, 1, 2),
            new Xtc3Parameter(16, "Tool Factor 1", F, 10, 500, 1, "%"),
            new Xtc3Parameter(17, "Tool Factor 2", F, 10, 500, 1, "%"),
            new Xtc3Parameter(18, "Xtal Stability Single", I, 25, 9999, zeroAllowed: true),
            new Xtc3Parameter(19, "Xtal Stability Total", I, 25, 9999, zeroAllowed: true),
            new Xtc3Parameter(20, "Xtal Quality Percent", I, 0, 99, unit: "%"),
            new Xtc3Parameter(21, "Xtal Quality Counts", I, 0, 99),
            new Xtc3Parameter(22, "Source", I, 1, 2),
            new Xtc3Parameter(23, "Crucible", I, 0, 8),
            new Xtc3Parameter(24, "Control Gain", F, 0.01m, 100, 2, "A/s/%"),
            new Xtc3Parameter(25, "Control Time Constant", F, 0.1m, 100, 1, "s"),
            new Xtc3Parameter(26, "Control Dead Time", F, 0.1m, 100, 1, "s"),
            new Xtc3Parameter(27, "Max Power", F, 0, 100, 1, "%"),
            new Xtc3Parameter(28, "Density", F, 0.5m, 99.99m, 2, "g/cm3"),
            new Xtc3Parameter(29, "Z-Ratio", F, 0.1m, 9.999m, 3),
            new Xtc3Parameter(30, "Time Power", I, 0, 1),
            new Xtc3Parameter(31, "Delay option", I, 0, 3),
            new Xtc3Parameter(32, "Transfer Sensor", I, 0, 1),
            new Xtc3Parameter(33, "Transfer Tooling", F, 10, 500, 1, "%"),
            new Xtc3Parameter(34, "Control Delay Time", I, 0, MaxSeconds, unit: "s"),
            new Xtc3Parameter(35, "Ion Assisted Deposit", I, 0, 1),
            new Xtc3Parameter(36, "Graph Label", I, 0, 1),
            new Xtc3Parameter(37, "Graph Scale", I, 0, 3),
        });

        /// <summary>通用参数 0~15 (XTC/3S),下标即 ID。全部为 Integer。</summary>
        public static IReadOnlyList<Xtc3Parameter> General { get; } = Array.AsReadOnly(new[]
        {
            new Xtc3Parameter(0, "Film to run", I, 1, Xtc3Protocol.FilmCount),
            new Xtc3Parameter(1, "Start Layer Without Backup Xtal", I, 0, 1),
            new Xtc3Parameter(2, "Stop on Alarms", I, 0, 1),
            new Xtc3Parameter(3, "Stop On Max Power", I, 0, 1),
            new Xtc3Parameter(4, "Dep/Etch Mode", I, 0, 1),
            new Xtc3Parameter(5, "Test", I, 0, 1),
            new Xtc3Parameter(6, "Sensor 1 Type", I, 0, 5),
            new Xtc3Parameter(7, "Sensor 2 Type", I, 0, 4),
            new Xtc3Parameter(8, "Source Control Voltage", I, 0, 5),
            new Xtc3Parameter(9, "Recorder Mode", I, 0, 3),
            new Xtc3Parameter(10, "Recorder Range", I, 0, 1),
            new Xtc3Parameter(11, "Recorder Filter", I, 0, 1),
            new Xtc3Parameter(12, "Audio Feedback", I, 0, 1),
            new Xtc3Parameter(13, "Dim LCD", I, 0, 99, unit: "min"),
            new Xtc3Parameter(14, "RS232 Baud Rate", I, 0, 4),
            new Xtc3Parameter(15, "RS232 Communications Protocol", I, 0, 3),
        });

        /// <summary>RS232 Baud Rate (通用参数 14) 的各选项对应的波特率。</summary>
        public static IReadOnlyList<int> BaudRates { get; } = Array.AsReadOnly(new[] { 9600, 19200, 38400, 57600, 115200 });

        /// <summary>按 ID 查找 Film 参数;不存在时为 null。</summary>
        public static Xtc3Parameter? FindFilm(int id) => id >= 0 && id < Film.Count ? Film[id] : null;

        /// <summary>按 ID 查找通用参数;不存在时为 null。</summary>
        public static Xtc3Parameter? FindGeneral(int id) => id >= 0 && id < General.Count ? General[id] : null;

        /// <summary>全部 Film 参数的数据类型,按 ID 顺序 (QF255、UF255)。</summary>
        internal static IReadOnlyList<Xtc3ValueType> FilmTypes { get; } = Film.Select(p => p.Type).ToList().AsReadOnly();
    }
}
