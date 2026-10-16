using System;
using System.Globalization;
using System.Threading;

namespace Zevision.Localization
{
    /// <summary>界面语言。</summary>
    public enum UiLanguage
    {
        /// <summary>简体中文。</summary>
        Chinese,

        /// <summary>English.</summary>
        English,
    }

    /// <summary>
    /// 中英文切换:启动时按系统确定一次语言 (中国地区或中文系统用中文,其他用英文),之后不再改变。
    /// 文本在调用处成对给出 (<see cref="T"/>),不依赖 resx 代码生成。
    /// </summary>
    /// <remarks>
    /// 未调用 <see cref="Initialize"/> 时为中文 (单元测试依赖这一点)。测试英文时用 <see cref="Override"/>,
    /// 它只作用于当前异步流,不影响并行运行的其他测试。
    /// </remarks>
    public static class Lang
    {
        private static readonly AsyncLocal<UiLanguage?> OverrideLanguage = new AsyncLocal<UiLanguage?>();
        private static UiLanguage _current = UiLanguage.Chinese;

        /// <summary>当前语言。</summary>
        public static UiLanguage Current => OverrideLanguage.Value ?? _current;

        /// <summary>是否为中文。</summary>
        public static bool IsChinese => Current == UiLanguage.Chinese;

        /// <summary>按当前语言选择文本。</summary>
        public static string T(string chinese, string english) => IsChinese ? chinese : english;

        /// <summary>设置程序的语言 (启动时调用一次,在创建任何窗口之前)。</summary>
        public static void Initialize(UiLanguage language) => _current = language;

        /// <summary>
        /// 确定语言:<paramref name="setting"/> 为 <c>zh</c>/<c>en</c> 时按设置;否则 (<c>auto</c>、空、无法识别)
        /// 系统显示语言是中文,或区域格式是中国 (CN) 时用中文,其他用英文。
        /// </summary>
        /// <param name="uiCulture">系统显示语言 (<see cref="CultureInfo.CurrentUICulture"/>)。</param>
        /// <param name="formatCulture">区域格式 (<see cref="CultureInfo.CurrentCulture"/>)。</param>
        /// <param name="setting">配置文件中的 <c>language</c>。</param>
        public static UiLanguage Detect(CultureInfo uiCulture, CultureInfo formatCulture, string? setting)
        {
            if (uiCulture is null)
            {
                throw new ArgumentNullException(nameof(uiCulture));
            }

            if (formatCulture is null)
            {
                throw new ArgumentNullException(nameof(formatCulture));
            }

            switch ((setting ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "zh":
                    return UiLanguage.Chinese;
                case "en":
                    return UiLanguage.English;
            }

            if (uiCulture.TwoLetterISOLanguageName == "zh" || IsChinaRegion(formatCulture))
            {
                return UiLanguage.Chinese;
            }

            return UiLanguage.English;
        }

        /// <summary>
        /// 在当前异步流中临时使用指定语言 (用于测试),Dispose 后恢复。
        /// </summary>
        public static IDisposable Override(UiLanguage language)
        {
            var previous = OverrideLanguage.Value;
            OverrideLanguage.Value = language;
            return new Restore(previous);
        }

        private static bool IsChinaRegion(CultureInfo culture)
        {
            if (culture.IsNeutralCulture || culture.Equals(CultureInfo.InvariantCulture))
            {
                return false;
            }

            try
            {
                return new RegionInfo(culture.Name).TwoLetterISORegionName == "CN";
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private sealed class Restore : IDisposable
        {
            private readonly UiLanguage? _previous;

            public Restore(UiLanguage? previous) => _previous = previous;

            public void Dispose() => OverrideLanguage.Value = _previous;
        }
    }
}
