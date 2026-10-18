using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Zevision.Commands;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 下拉框中的一项 (界面语言)。由指令集的 <see cref="CommandOption"/> 或配置中的对象生成。
    /// </summary>
    public sealed class ParameterOption : ImmutableBindingSource
    {
        /// <summary>创建选项。</summary>
        /// <param name="value">发送给设备的值。</param>
        /// <param name="text">含义,如 <c>关</c>、对象名称;可以为空。</param>
        public ParameterOption(string value, string? text)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Text = text ?? string.Empty;
        }

        /// <summary>发送给设备的值。</summary>
        public string Value { get; }

        /// <summary>含义 (界面语言),如 <c>关</c>。</summary>
        public string Text { get; }

        /// <summary>显示文本,如 <c>0 - 关</c>;没有含义时只有值。</summary>
        public string Label => string.IsNullOrEmpty(Text) ? Value : Value + " - " + Text;

        /// <summary>按界面语言转换指令集的选项。</summary>
        public static ParameterOption From(CommandOption option) =>
            new ParameterOption((option ?? throw new ArgumentNullException(nameof(option))).Value, option.TextValue());

        /// <inheritdoc />
        public override string ToString() => Label;
    }

    /// <summary>
    /// 一份配置中各项的下拉选项:枚举取 <see cref="CommandDefinition.Options"/>;引用取配置中已有的对象。
    /// 被引用的对象列表按组只计算一次 (生成页面时每个引用项都要用)。
    /// </summary>
    public sealed class ParameterOptions
    {
        private readonly IReadOnlyList<RecipeEntry> _entries;
        private readonly Dictionary<(char Group, bool AllowNone), IReadOnlyList<ParameterOption>> _references =
            new Dictionary<(char, bool), IReadOnlyList<ParameterOption>>();

        /// <summary>为一份配置创建选项。</summary>
        /// <param name="entries">配置项;引用的对象从中查找 (名称取对象的 <c>01</c> 项)。</param>
        public ParameterOptions(IEnumerable<RecipeEntry> entries)
        {
            _entries = (entries ?? throw new ArgumentNullException(nameof(entries))).ToList();
        }

        /// <summary>
        /// 下拉框的选项:枚举为固定选项;引用为配置中已有的对象,允许时在最前面加 <c>0 - 未选择</c>。
        /// 其他类型返回空列表。返回的列表是共享的,不要修改。
        /// </summary>
        public IReadOnlyList<ParameterOption> For(CommandDefinition definition)
        {
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            switch (definition.Kind)
            {
                case CommandValueKind.Enum:
                    return definition.Options.Select(ParameterOption.From).ToList();
                case CommandValueKind.Reference:
                    var key = (definition.ReferenceGroup, definition.AllowNone);
                    if (!_references.TryGetValue(key, out var options))
                    {
                        options = BuildReferences(key.ReferenceGroup, key.AllowNone);
                        _references.Add(key, options);
                    }

                    return options;
                default:
                    return Array.Empty<ParameterOption>();
            }
        }

        private IReadOnlyList<ParameterOption> BuildReferences(char group, bool allowNone)
        {
            var options = new List<ParameterOption>();
            if (allowNone)
            {
                options.Add(new ParameterOption("0", Lang.T("未选择", "None")));
            }

            // 对象名称项:组字母 + 两位编号 + 01,如 M0301。
            var nameItem = new Regex("^" + group + "([0-9A-F]{2})01$", RegexOptions.CultureInvariant);
            foreach (var entry in _entries)
            {
                var match = nameItem.Match(entry.Id);
                if (match.Success)
                {
                    var number = DeviceIds.LastNumber(match.Groups[1].Value);
                    if (number >= CommandDefinition.MinIndex && number <= CommandDefinition.MaxIndex)
                    {
                        options.Add(new ParameterOption(number.ToString(CultureInfo.InvariantCulture), entry.Value));
                    }
                }
            }

            return options;
        }
    }
}
