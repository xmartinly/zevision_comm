using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using Zevision.Core.Localization;

namespace Zevision.Tests
{
    /// <summary>
    /// 翻译文件 Texts.json 与各层的 <c>Texts</c> 类一致:键一一对应 (键 = 成员名),中英文都有,
    /// 参数个数与模板中的占位符相同,每个文本在两种语言下都能生成。
    /// </summary>
    public class TextCatalogTests
    {
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.CultureInvariant);

        private static readonly Type[] AccessorClasses =
        {
            typeof(Zevision.Core.Result).Assembly.GetType("Zevision.Core.Texts", throwOnError: true)!,
            typeof(Zevision.Storage.RecipeFile).Assembly.GetType("Zevision.Storage.Texts", throwOnError: true)!,
        };

        private static IEnumerable<MemberInfo> Accessors =>
            AccessorClasses.SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m is PropertyInfo || (m is MethodInfo method && !method.IsSpecialName));

        [Fact]
        public void Keys_MatchAccessors()
        {
            var names = Accessors.Select(m => m.Name).ToList();

            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), TextCatalog.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }

        [Fact]
        public void EveryText_HasBothLanguages_AndEnglishHasNoChinese()
        {
            foreach (var key in TextCatalog.Keys)
            {
                var chinese = TextCatalog.Template(key, UiLanguage.Chinese);
                var english = TextCatalog.Template(key, UiLanguage.English);
                Assert.False(string.IsNullOrWhiteSpace(chinese), key);
                Assert.False(string.IsNullOrWhiteSpace(english), key);
                Assert.False(english!.Any(c => c >= 0x3000 && c <= 0x9FFF), key + ": " + english);
            }
        }

        [Fact]
        public void Placeholders_MatchParameters()
        {
            foreach (var member in Accessors)
            {
                var parameters = member is MethodInfo method ? method.GetParameters().Length : 0;
                foreach (var language in new[] { UiLanguage.Chinese, UiLanguage.English })
                {
                    var template = TextCatalog.Template(member.Name, language)!;
                    IEnumerable<int> indices = parameters == 0
                        ? new int[0] // 没有参数的文本原样使用,花括号是字面字符
                        : Placeholder.Matches(template.Replace("{{", string.Empty).Replace("}}", string.Empty))
                            .Cast<Match>().Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().OrderBy(i => i);
                    Assert.True(Enumerable.Range(0, parameters).SequenceEqual(indices), $"{member.Name} ({language}): {template}");
                }
            }
        }

        [Fact]
        public void EveryAccessor_ProducesTextInBothLanguages()
        {
            foreach (var language in new[] { UiLanguage.Chinese, UiLanguage.English })
            {
                using (Lang.Override(language))
                {
                    foreach (var member in Accessors)
                    {
                        var args = member is MethodInfo method ? method.GetParameters().Select(Sample).ToArray() : new object[0];
                        var text = member is MethodInfo m ? (string)m.Invoke(null, args) : (string)((PropertyInfo)member).GetValue(null);
                        var template = TextCatalog.Template(member.Name, language);

                        Assert.NotNull(template);
                        if (args.Length == 0)
                        {
                            Assert.Equal(template, text);
                        }

                        Assert.DoesNotMatch(Placeholder, text);
                        Assert.All(args.OfType<string>(), a => Assert.Contains(a, text));
                    }
                }
            }
        }

        [Fact]
        public void Format_UsesCurrentLanguage()
        {
            using (Lang.Override(UiLanguage.English))
            {
                Assert.Equal("Not connected.", TextCatalog.Get("NotConnected"));
            }

            using (Lang.Override(UiLanguage.Chinese))
            {
                Assert.Equal("未连接。", TextCatalog.Get("NotConnected"));
                Assert.Equal("命令长度超过 12 字节。", TextCatalog.Format("CommandTooLong", 12));
            }
        }

        [Fact]
        public void MissingKey_ReturnsKeyInsteadOfThrowing()
        {
            Assert.Equal("NoSuchKey", TextCatalog.Get("NoSuchKey"));
            Assert.Equal("NoSuchKey", TextCatalog.Format("NoSuchKey", 1));
        }

        private static object Sample(ParameterInfo parameter)
        {
            var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            if (type == typeof(string))
            {
                return "<" + parameter.Name + ">";
            }

            if (type == typeof(int))
            {
                return 7;
            }

            if (type == typeof(double))
            {
                return 7.0;
            }

            throw new NotSupportedException(parameter.Member.Name + "." + parameter.Name + ": " + type);
        }
    }
}
