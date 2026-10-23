using System;
using System.Collections.Generic;
using System.Globalization;
using Zevision.Core.Configuration;
using Zevision.Xtc3;

namespace Zevision.Bridge
{
    /// <summary>IMC300 设置项的值的写法。</summary>
    internal enum DeviceValueKind
    {
        /// <summary>时间 <c>H:MM:SS</c>;XTC/3 为整数秒。</summary>
        Seconds,

        /// <summary>固定小数位数的数 (<see cref="DeviceItem.Decimals"/>)。</summary>
        Number,
    }

    /// <summary>影子对象中的一个设置项:所在对象 (M、F、L)、对象下的编号路径和值的写法。</summary>
    internal sealed class DeviceItem
    {
        public DeviceItem(char objectGroup, int[] path, DeviceValueKind kind, int decimals = 0)
        {
            ObjectGroup = objectGroup;
            Path = path;
            Kind = kind;
            Decimals = decimals;
        }

        public char ObjectGroup { get; }

        public int[] Path { get; }

        public DeviceValueKind Kind { get; }

        /// <summary>设备保存的小数位数 (实测帮助表,如密度 5.890 为 3 位);整数为 0。</summary>
        public int Decimals { get; }

        public string IdFor(ShadowFilm film) => ShadowFilm.Item(
            ObjectGroup switch
            {
                'M' => film.MaterialId,
                'F' => film.FilmId,
                'L' => film.LayerId,
                _ => film.ProcessId,
            },
            Path);
    }

    /// <summary>一项写入:设置项、值,以及读回时允许的数值误差。</summary>
    internal readonly struct DeviceWrite
    {
        public DeviceWrite(string id, string value, decimal tolerance = 0)
        {
            Id = id;
            Value = value;
            Tolerance = tolerance;
        }

        public string Id { get; }

        public string Value { get; }

        public decimal Tolerance { get; }

        /// <summary>读回的值与写入的值一致:数值在误差内,时间秒数相同,其他按字符串。</summary>
        public bool Matches(string? readBack)
        {
            if (readBack == null)
            {
                return false;
            }

            if (Tolerance > 0
                && decimal.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var written)
                && decimal.TryParse(readBack, NumberStyles.Float, CultureInfo.InvariantCulture, out var actual))
            {
                return Math.Abs(written - actual) <= Tolerance;
            }

            return ParameterValue.AreEquivalent(Value, readBack.Trim());
        }

        public override string ToString() => Id + ":" + Value;
    }

    /// <summary>
    /// Film 参数 (UF/QF) 与影子对象设置项的对应 (ZEV-FAR-01 表 A.1)。特殊的换算 (Soak/Idle Power 上限、RW Hold Time、
    /// Time Power、Transfer Sensor、本地参数) 由 <see cref="BridgeEngine"/> 处理,这里只有一一对应的项。
    /// </summary>
    internal static class FilmMap
    {
        /// <summary>IMC300 预熔、后处理功率的上限 (%);XTC/3 写入 100.0 时写这个值。</summary>
        public const decimal MaxConditionPower = 99.99m;

        private static readonly Dictionary<Xtc3FilmParameterId, DeviceItem> Items = new Dictionary<Xtc3FilmParameterId, DeviceItem>
        {
            [Xtc3FilmParameterId.RiseTime1] = Time('F', 0x03, 1, 1),
            [Xtc3FilmParameterId.SoakPower1] = Number('F', 2, 0x03, 1, 2),
            [Xtc3FilmParameterId.SoakTime1] = Time('F', 0x03, 1, 3),
            [Xtc3FilmParameterId.RiseTime2] = Time('F', 0x03, 2, 1),
            [Xtc3FilmParameterId.SoakPower2] = Number('F', 2, 0x03, 2, 2),
            [Xtc3FilmParameterId.SoakTime2] = Time('F', 0x03, 2, 3),
            [Xtc3FilmParameterId.IdleRamp] = Time('F', 0x05, 1, 1),
            [Xtc3FilmParameterId.IdlePower] = Number('F', 2, 0x05, 1, 2),
            [Xtc3FilmParameterId.DepositionRate] = Number('L', 3, 0x02, 1, 2),
            [Xtc3FilmParameterId.FinalThickness] = Number('L', 3, 0x02, 1, 3),
            [Xtc3FilmParameterId.ThicknessSetPoint] = Number('L', 4, 0x02, 1, 0x06, 1, 1), // ZEV-FAR-01 的 Q-02:速率斜坡起始厚度
            [Xtc3FilmParameterId.NewRate] = Number('L', 3, 0x02, 1, 0x06, 1, 3),
            [Xtc3FilmParameterId.RateRampTime] = Time('L', 0x02, 1, 0x06, 1, 2),
            [Xtc3FilmParameterId.RateWatcherAccuracy] = Number('F', 0, 0x06, 3),
            [Xtc3FilmParameterId.Sensor] = Number('F', 0, 0x07, 1, 1),
            [Xtc3FilmParameterId.ToolFactor1] = Number('F', 2, 0x07, 1, 3),
            [Xtc3FilmParameterId.XtalStabilitySingle] = Number('F', 0, 0x08, 0x09),
            [Xtc3FilmParameterId.XtalStabilityTotal] = Number('F', 0, 0x08, 0x0A),
            [Xtc3FilmParameterId.XtalQualityPercent] = Number('F', 1, 0x08, 0x07),
            [Xtc3FilmParameterId.XtalQualityCounts] = Number('F', 0, 0x08, 0x08),
            [Xtc3FilmParameterId.Source] = Number('F', 0, 0x09, 1),
            [Xtc3FilmParameterId.Crucible] = Number('F', 0, 0x09, 2),
            [Xtc3FilmParameterId.ControlGain] = Number('F', 3, 0x09, 3),
            [Xtc3FilmParameterId.ControlTimeConstant] = Number('F', 3, 0x09, 4),
            [Xtc3FilmParameterId.ControlDeadTime] = Number('F', 3, 0x09, 5),
            [Xtc3FilmParameterId.MaxPower] = Number('F', 2, 0x09, 6),
            [Xtc3FilmParameterId.Density] = Number('M', 3, 0x03),
            [Xtc3FilmParameterId.ZRatio] = Number('M', 3, 0x04),
            [Xtc3FilmParameterId.TimePower] = Number('F', 0, 0x07, 1, 5), // 传感器的失败动作:4 = 时间功率
            [Xtc3FilmParameterId.DelayOption] = Number('F', 0, 0x04, 1),
            [Xtc3FilmParameterId.TransferSensor] = Number('F', 0, 0x04, 5),
            [Xtc3FilmParameterId.TransferTooling] = Number('F', 2, 0x04, 6),
            [Xtc3FilmParameterId.ControlDelayTime] = Time('F', 0x04, 2),
            [Xtc3FilmParameterId.IonAssistedDeposit] = Number('F', 0, 0x06, 4),
        };

        /// <summary>RW Hold Time 的启用开关 <c>Fkk0601</c>。</summary>
        public static readonly DeviceItem RateWatcherEnabled = Number('F', 0, 0x06, 1);

        /// <summary>RW Hold Time 的时间 <c>Fkk0602</c>。</summary>
        public static readonly DeviceItem RateWatcherTime = Time('F', 0x06, 2);

        /// <summary>最大功率选项 <c>Fkk0907</c> (通用参数 Stop On Max Power)。</summary>
        public static readonly DeviceItem MaxPowerOption = Number('F', 0, 0x09, 7);

        /// <summary>无备份晶振启动 <c>P02kk0B</c> (通用参数 Start Layer Without Backup Xtal)。</summary>
        public static readonly DeviceItem StartWithoutBackup = Number('P', 0, 0x0B);

        /// <summary>沉积/刻蚀 <c>P02kk08</c> (通用参数 Dep/Etch Mode)。</summary>
        public static readonly DeviceItem EtchMode = Number('P', 0, 0x08);

        /// <summary>Time Power 写入 1 时的失败动作 (TimePw)。</summary>
        public const int FailureActionTimePower = 4;

        /// <summary>Time Power 写入 0 时的失败动作 (Stop)。</summary>
        public const int FailureActionStop = 2;

        /// <summary>只由中间层保存的 Film 参数。</summary>
        public static bool IsLocal(Xtc3FilmParameterId id) =>
            id == Xtc3FilmParameterId.ToolFactor2 || id == Xtc3FilmParameterId.GraphLabel || id == Xtc3FilmParameterId.GraphScale;

        /// <summary>Film 参数对应的设置项;本地参数和 RW Hold Time 没有 (另行处理)。</summary>
        public static DeviceItem? Find(Xtc3FilmParameterId id) => Items.TryGetValue(id, out var item) ? item : null;

        /// <summary>
        /// 设备的值转为 XTC/3 的值。时间转为秒;数按参数的类型转为 Float 或取整为 Integer。
        /// </summary>
        /// <returns>值无法解析时为 null (回答 E)。</returns>
        public static Xtc3Value? ToXtc(Xtc3Parameter parameter, DeviceItem item, string? text)
        {
            if (text == null)
            {
                return null;
            }

            if (item.Kind == DeviceValueKind.Seconds)
            {
                return ParameterValue.TryParseTime(text, out var seconds) ? Xtc3Value.FromInteger(seconds) : (Xtc3Value?)null;
            }

            return ToNumber(parameter.Type, text);
        }

        /// <summary>设备上的数转为 XTC/3 的 Float 或 Integer (四舍五入)。无法解析时为 null。</summary>
        public static Xtc3Value? ToNumber(Xtc3ValueType type, string? text)
        {
            if (!decimal.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return null;
            }

            if (type == Xtc3ValueType.Float)
            {
                return Xtc3Value.FromFloat((float)number);
            }

            var rounded = Math.Round(number, 0, MidpointRounding.AwayFromZero);
            return rounded >= int.MinValue && rounded <= int.MaxValue ? Xtc3Value.FromInteger((int)rounded) : (Xtc3Value?)null;
        }

        /// <summary>
        /// XTC/3 的值 (已按 <see cref="Xtc3Parameter.IsValid"/> 检查) 转为设备的写法。读回的误差为 XTC/3 显示精度的一半。
        /// </summary>
        public static DeviceWrite ToDevice(Xtc3Parameter parameter, DeviceItem item, string id, Xtc3Value value)
        {
            if (item.Kind == DeviceValueKind.Seconds)
            {
                return new DeviceWrite(id, ParameterValue.FormatTime(value.Integer));
            }

            Xtc3Parameter.TryToDecimal(value, item.Decimals, out var number);
            return ToDevice(id, number, item.Decimals, parameter.Decimals);
        }

        /// <summary>数按 <paramref name="decimals"/> 位小数写出;误差为 <paramref name="xtcDecimals"/> 位精度的一半。</summary>
        public static DeviceWrite ToDevice(string id, decimal number, int decimals, int xtcDecimals)
        {
            var text = Math.Round(number, decimals, MidpointRounding.AwayFromZero).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return new DeviceWrite(id, text, Tolerance(Math.Min(decimals, xtcDecimals)));
        }

        /// <summary>整数写出,读回时数值相等即可 (<c>5</c> = <c>5.0</c>)。</summary>
        public static DeviceWrite Integer(string id, int value) =>
            new DeviceWrite(id, value.ToString(CultureInfo.InvariantCulture), 0.5m);

        /// <summary>按 <paramref name="decimals"/> 位精度的一半作为误差。</summary>
        public static decimal Tolerance(int decimals)
        {
            var tolerance = 0.5m;
            for (var i = 0; i < decimals; i++)
            {
                tolerance /= 10;
            }

            return tolerance;
        }

        private static DeviceItem Time(char group, params int[] path) => new DeviceItem(group, path, DeviceValueKind.Seconds);

        private static DeviceItem Number(char group, int decimals, params int[] path) => new DeviceItem(group, path, DeviceValueKind.Number, decimals);
    }
}
