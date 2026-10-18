using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Zevision.Core.Localization
{
    /// <summary>
    /// 业务层和存储层的中英文文本,来自嵌入的翻译文件 <c>Localization/Texts.json</c>:
    /// <c>{ "键": { "zh": "中文", "en": "English" } }</c>。按 <see cref="Lang.Current"/> 取当前语言。
    /// </summary>
    /// <remarks>
    /// 有参数的文本按 <see cref="string.Format(IFormatProvider, string, object[])"/> 格式化 (<c>{0}</c>、<c>{0:0}</c>,
    /// 字面的花括号写作 <c>{{ }}</c>),与插值字符串一样使用当前区域格式;没有参数的文本原样使用。
    /// 调用方通过各层的 <c>Texts</c> 类取文本,键就是其成员名;键与参数个数由单元测试与本文件核对,
    /// 因此运行时不会缺键。万一缺键或格式不对,返回键名或未格式化的文本,不抛出异常。
    /// </remarks>
    public static class TextCatalog
    {
        /// <summary>嵌入资源的名称。</summary>
        internal const string ResourceName = "Zevision.Core.Texts.json";

        private static readonly Lazy<IReadOnlyDictionary<string, TextEntry>> Entries =
            new Lazy<IReadOnlyDictionary<string, TextEntry>>(Load);

        /// <summary>全部键。</summary>
        public static IEnumerable<string> Keys => Entries.Value.Keys;

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
            Entries.Value.TryGetValue(key ?? throw new ArgumentNullException(nameof(key)), out var entry)
                ? (language == UiLanguage.Chinese ? entry.Chinese : entry.English)
                : null;

        private static IReadOnlyDictionary<string, TextEntry> Load()
        {
            using (var stream = typeof(TextCatalog).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException("The embedded resource " + ResourceName + " is missing."))
            {
                var serializer = new DataContractJsonSerializer(
                    typeof(Dictionary<string, TextEntry>),
                    new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                return (Dictionary<string, TextEntry>)serializer.ReadObject(stream);
            }
        }

        /// <summary>一条文本的中英文。</summary>
        [DataContract]
        internal sealed class TextEntry
        {
            [DataMember(Name = "zh", IsRequired = true)]
            public string Chinese { get; set; } = string.Empty;

            [DataMember(Name = "en", IsRequired = true)]
            public string English { get; set; } = string.Empty;
        }
    }
}
