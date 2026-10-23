using System;
using System.Globalization;
using System.Linq;
using Zevision.Commands;
using Zevision.Core.Localization;

namespace Zevision.Core.Configuration
{
    /// <summary>
    /// 按 <see cref="CommandDefinition"/> 校验、规整和比较配置值。数字一律按 InvariantCulture (小数点为 <c>.</c>)。
    /// </summary>
    public static class ParameterValue
    {
        /// <summary>时间的最大小时数 (协议文档:小时最大 8760)。</summary>
        public const int MaxHours = 8760;

        private const NumberStyles NumberStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

        /// <summary>
        /// 校验值。先检查协议字符 (<see cref="CommunicationText.ValidateValue"/>),再按类型检查格式和范围;
        /// <paramref name="definition"/> 为 null (文档未列出的项) 时只检查协议字符。
        /// 引用类型的选项由调用方的下拉框保证,这里只检查是非负整数。
        /// </summary>
        /// <returns>错误说明;合法时为 null。</returns>
        public static string? Validate(CommandDefinition? definition, string value)
        {
            var error = CommunicationText.ValidateValue(value);
            if (error != null || definition == null)
            {
                return error;
            }

            var text = value.Trim();
            switch (definition.Kind)
            {
                case CommandValueKind.Number:
                    return ValidateNumber(definition, text);

                case CommandValueKind.Time:
                    if (!TryParseTime(text, out var seconds))
                    {
                        return Texts.InvalidTime(MaxHours);
                    }

                    return (definition.Min.HasValue && seconds < definition.Min.Value) || (definition.Max.HasValue && seconds > definition.Max.Value)
                        ? Texts.OutOfRange + definition.RangeText()
                        : null;

                case CommandValueKind.Enum:
                    return definition.Options.Any(o => o.Value == text) ? null : Texts.MustBeOneOf + definition.RangeText();

                case CommandValueKind.Reference:
                    return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? null : Texts.MustBeObjectNumber;

                default:
                    return null;
            }
        }

        /// <summary>
        /// 写入前规整值:去除首尾空白;数字按 <paramref name="original"/> 的小数位数格式化 (设备按固定位数保存,
        /// 如 <c>5.89</c> → <c>5.890</c>,读回校验才能一致);时间格式化为设备的 <c>H:MM:SS</c>。
        /// 值不合法时只去除空白。
        /// </summary>
        public static string Normalize(CommandDefinition? definition, string value, string original)
        {
            var text = (value ?? string.Empty).Trim();
            if (definition == null)
            {
                return text;
            }

            switch (definition.Kind)
            {
                case CommandValueKind.Number:
                    if (decimal.TryParse(text, NumberStyle, CultureInfo.InvariantCulture, out var number)
                        && decimal.TryParse(original, NumberStyle, CultureInfo.InvariantCulture, out _))
                    {
                        var decimals = Math.Min(DecimalPlaces(original), 28); // decimal 最多 28 位小数,Math.Round 超过会抛出
                        return Math.Round(number, decimals, MidpointRounding.AwayFromZero)
                            .ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    }

                    return text;

                case CommandValueKind.Time:
                    return TryParseTime(text, out var seconds) ? FormatTime(seconds) : text;

                default:
                    return text;
            }
        }

        /// <summary>
        /// 读回的值是否与写入的值一致:字符串相同,或者都是数字且数值相等 (<c>5.890</c> = <c>5.89</c>),
        /// 或者都是时间且秒数相等 (<c>0:00:05</c> = <c>00:00:05</c>)。
        /// </summary>
        public static bool AreEquivalent(string? expected, string? actual)
        {
            if (string.Equals(expected, actual, StringComparison.Ordinal))
            {
                return true;
            }

            if (expected == null || actual == null)
            {
                return false;
            }

            if (decimal.TryParse(expected, NumberStyle, CultureInfo.InvariantCulture, out var a)
                && decimal.TryParse(actual, NumberStyle, CultureInfo.InvariantCulture, out var b))
            {
                return a == b;
            }

            return expected.IndexOf(':') >= 0
                && TryParseTime(expected, out var s1)
                && TryParseTime(actual, out var s2)
                && s1 == s2;
        }

        /// <summary>
        /// 解析时间 <c>H:MM:SS</c> 或 <c>M:SS</c> (小时为 0 时可省略)。小时 0~8760,分、秒 0~59。
        /// </summary>
        /// <param name="text">时间文本。</param>
        /// <param name="totalSeconds">总秒数。</param>
        public static bool TryParseTime(string text, out int totalSeconds)
        {
            totalSeconds = 0;
            var parts = (text ?? string.Empty).Trim().Split(':');
            if (parts.Length < 2 || parts.Length > 3)
            {
                return false;
            }

            var numbers = new int[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0
                    || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                {
                    return false;
                }
            }

            var hours = parts.Length == 3 ? numbers[0] : 0;
            var minutes = numbers[parts.Length - 2];
            var seconds = numbers[parts.Length - 1];
            if (hours > MaxHours || minutes > 59 || seconds > 59)
            {
                return false;
            }

            totalSeconds = (hours * 3600) + (minutes * 60) + seconds;
            return true;
        }

        /// <summary>按设备格式显示时间,如 <c>0:00:05</c>。</summary>
        public static string FormatTime(int totalSeconds) =>
            string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}:{2:D2}", totalSeconds / 3600, totalSeconds / 60 % 60, totalSeconds % 60);

        private static string? ValidateNumber(CommandDefinition definition, string text)
        {
            if (!decimal.TryParse(text, NumberStyle, CultureInfo.InvariantCulture, out var number))
            {
                return definition.IsInteger ? Texts.MustBeInteger : Texts.MustBeNumber;
            }

            if (definition.IsInteger && text.IndexOf('.') >= 0)
            {
                return Texts.MustBeInteger;
            }

            if ((definition.Min.HasValue && number < definition.Min.Value)
                || (definition.Max.HasValue && number > definition.Max.Value))
            {
                return Texts.OutOfRange + definition.RangeText();
            }

            return null;
        }

        /// <summary>数字的小数位数 (小数点后的字符数),如 <c>5.600</c> 为 3;没有小数点时为 0。</summary>
        internal static int DecimalPlaces(string number)
        {
            var dot = number.Trim().IndexOf('.');
            return dot < 0 ? 0 : number.Trim().Length - dot - 1;
        }
    }
}
