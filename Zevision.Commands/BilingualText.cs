using System;

namespace Zevision.Commands
{
    /// <summary>
    /// 文本的语言。指令集中的名称、单位、选项等同时有中英文,由调用方选择。
    /// </summary>
    public enum TextLanguage
    {
        /// <summary>英文 (设备帮助表的说明)。</summary>
        English,

        /// <summary>中文。</summary>
        Chinese,
    }

    /// <summary>一段中英文文本。</summary>
    public sealed class BilingualText
    {
        /// <summary>空文本。</summary>
        public static readonly BilingualText Empty = new BilingualText(string.Empty, string.Empty);

        /// <summary>创建文本。</summary>
        public BilingualText(string chinese, string english)
        {
            Chinese = chinese ?? string.Empty;
            English = english ?? string.Empty;
        }

        /// <summary>中文。</summary>
        public string Chinese { get; }

        /// <summary>英文。</summary>
        public string English { get; }

        /// <summary>中英文相同的文本 (如 <c>Z-ratio</c>、<c>Off</c>)。</summary>
        public static BilingualText Same(string text) => new BilingualText(text, text);

        /// <summary>取指定语言的文本。</summary>
        public string Get(TextLanguage language) => language == TextLanguage.Chinese ? Chinese : English;

        /// <summary>英文。</summary>
        public override string ToString() => English;
    }

    /// <summary><see cref="TextLanguage"/> 的辅助方法。</summary>
    internal static class TextLanguageExtensions
    {
        public static bool IsChinese(this TextLanguage language) => language == TextLanguage.Chinese;

        public static string Pick(this TextLanguage language, string chinese, string english) =>
            language == TextLanguage.Chinese ? chinese : english;
    }
}
