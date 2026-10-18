using System;
using System.Collections.Generic;
using System.Globalization;

namespace Zevision.Core.Localization
{
    /// <summary>一条文本的中英文。</summary>
    /// <param name="Chinese">中文。</param>
    /// <param name="English">英文。</param>
    public sealed record LocalizedText(string Chinese, string English);

    /// <summary>
    /// 业务层和存储层的中英文文本,来自程序 (exe) 旁边的翻译文件 <see cref="FileName"/>:
    /// <c>{ "键": { "zh": "中文", "en": "English" } }</c>。按 <see cref="Lang.Current"/> 取当前语言。
    /// </summary>
    /// <remarks>
    /// 文件由存储层读取,程序启动时 (在任何文本被使用之前) 用 <see cref="Initialize"/> 交给本类;本类不读写文件。
    /// 有参数的文本按 <see cref="string.Format(IFormatProvider, string, object[])"/> 格式化 (<c>{0}</c>、<c>{0:0}</c>,
    /// 字面的花括号写作 <c>{{ }}</c>),与插值字符串一样使用当前区域格式;没有参数的文本原样使用。
    /// 调用方通过各层的 <c>Texts</c> 类取文本,键就是其成员名;发布的文件与代码的一致性由单元测试核对。
    /// 缺键 (如文件被改动) 或格式不对时返回键名或未格式化的文本,不抛出异常。
    /// </remarks>
    public static class TextCatalog
    {
        /// <summary>翻译文件名,放在程序 (exe) 所在的文件夹。</summary>
        public const string FileName = "Texts.json";

        private static volatile IReadOnlyDictionary<string, LocalizedText> _entries = new Dictionary<string, LocalizedText>();

        /// <summary>全部键。</summary>
        public static IEnumerable<string> Keys => _entries.Keys;

        /// <summary>设置文本 (程序启动时调用一次,在任何文本被使用之前)。</summary>
        /// <param name="entries">键 → 中英文。</param>
        public static void Initialize(IReadOnlyDictionary<string, LocalizedText> entries) =>
            _entries = entries ?? throw new ArgumentNullException(nameof(entries));

        /// <summary>当前语言的文本 (没有参数)。</summary>
        /// <param name="key">键。</param>
        public static string Get(string key) => Template(key, Lang.Current) ?? key;

        /// <summary>当前语言的文本,用 <paramref name="args"/> 替换其中的 <c>{0}</c>、<c>{1}</c>…</summary>
        /// <param name="key">键。</param>
        /// <param name="args">参数。</param>
        public static string Format(string key, params object?[] args)
        {
            var template = Template(key, Lang.Current);
            if (template == null)
            {
                return key;
            }

            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        /// <summary>某种语言的文本模板;没有该键时为 null。</summary>
        /// <param name="key">键。</param>
        /// <param name="language">语言。</param>
        public static string? Template(string key, UiLanguage language) =>
            _entries.TryGetValue(key ?? throw new ArgumentNullException(nameof(key)), out var entry)
                ? (language == UiLanguage.Chinese ? entry.Chinese : entry.English)
                : null;
    }
}
