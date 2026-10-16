using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Zevision.Communication;

namespace Zevision.Commands
{
    /// <summary>
    /// 命令组:命令 ID 的第一个字符 (协议文档 1.5)。
    /// </summary>
    public enum CommandGroup
    {
        /// <summary>Process Control,工艺控制。</summary>
        C,

        /// <summary>Echo,回显。</summary>
        E,

        /// <summary>Film,膜。</summary>
        F,

        /// <summary>General,通用。</summary>
        G,

        /// <summary>Hello,握手/版本。</summary>
        H,

        /// <summary>Inputs,输入。</summary>
        I,

        /// <summary>Layer,层。</summary>
        L,

        /// <summary>Materials,材料。</summary>
        M,

        /// <summary>Sensors,传感器 (QCM)。</summary>
        N,

        /// <summary>Outputs,输出。</summary>
        O,

        /// <summary>Process,工艺。</summary>
        P,

        /// <summary>Sources,源。</summary>
        S,

        /// <summary>Logic,逻辑 (协议文档未列出具体指令)。</summary>
        T,
    }

    /// <summary>
    /// 指令支持的动作 (可组合)。<see cref="None"/> 表示指令本身不带动作字母,如 <c>H01</c>、<c>E:data</c>。
    /// </summary>
    [Flags]
    public enum CommandActions
    {
        /// <summary>无动作字母,直接发送命令 ID。</summary>
        None = 0,

        /// <summary>Q,查询。</summary>
        Query = 1,

        /// <summary>U,更新。</summary>
        Update = 2,

        /// <summary>A,添加列表项。</summary>
        Add = 4,

        /// <summary>D,删除列表项。</summary>
        Delete = 8,

        /// <summary>S,读取运行时状态 (带同步计数)。</summary>
        Status = 16,

        /// <summary>R,远程执行动作。</summary>
        Remote = 32,
    }

    /// <summary>指令数据 (值) 的类型。</summary>
    public enum CommandValueKind
    {
        /// <summary>没有值 (动作命令、列表节点)。</summary>
        None,

        /// <summary>字符串。</summary>
        Text,

        /// <summary>数字,可带范围。</summary>
        Number,

        /// <summary>固定选项,如 <c>0=Off, 1=On</c>。</summary>
        Enum,

        /// <summary>时间 <c>H:MM:SS</c>,小时最大 8760。</summary>
        Time,

        /// <summary>引用另一个对象的编号 (十进制),如膜的材料。</summary>
        Reference,
    }

    /// <summary>固定选项中的一项。</summary>
    public sealed class CommandOption
    {
        /// <summary>创建选项 (中英文相同的含义,如 <c>Off</c>)。</summary>
        public CommandOption(string value, string text)
            : this(value, BilingualText.Same(text))
        {
        }

        /// <summary>创建选项。</summary>
        public CommandOption(string value, BilingualText text)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Text = text ?? BilingualText.Empty;
        }

        /// <summary>发送给设备的值。</summary>
        public string Value { get; }

        /// <summary>含义,如 <c>Off</c> / <c>关</c>;没有时为空。</summary>
        public BilingualText Text { get; }

        /// <summary>显示文本,如 <c>0 - Off</c>;没有含义时只有值。</summary>
        public string GetLabel(TextLanguage language)
        {
            var text = Text.Get(language);
            return string.IsNullOrEmpty(text) ? Value : Value + " - " + text;
        }

        /// <summary>英文显示文本,如 <c>0 - Off</c>。</summary>
        public override string ToString() => GetLabel(TextLanguage.English);
    }

    /// <summary>
    /// 一条指令的定义:命令模式、支持的动作、说明、单位、取值范围或选项。
    /// </summary>
    /// <remarks>
    /// <see cref="Pattern"/> 中的 <c>**</c> 是一级编号,用两位十六进制表示,由调用方给出:
    /// 第一级是对象编号 (如 <c>M**03</c> 中的材料编号、<c>F**02</c> 中的膜编号、<c>P02**01</c> 中的工艺编号),
    /// 之后的是子列表中的序号 (如 <c>F**03**01</c> 中的预条件序号)。
    /// </remarks>
    public sealed class CommandDefinition
    {
        /// <summary>编号的最小值。00 是列表的"添加/删除"命令,不是对象编号。</summary>
        public const int MinIndex = 1;

        /// <summary>编号的最大值。FE 是列表的"数量"项,FF 保留,都不是对象编号。</summary>
        public const int MaxIndex = 0xFD;

        private readonly Regex _regex;
        private readonly IReadOnlyList<BilingualText> _indexNames;

        internal CommandDefinition(
            string pattern,
            CommandActions actions,
            string name,
            CommandValueKind kind = CommandValueKind.None,
            string unit = "",
            decimal? min = null,
            decimal? max = null,
            bool isInteger = false,
            IReadOnlyList<CommandOption>? options = null,
            char referenceGroup = '\0',
            bool allowNone = false,
            string remark = "")
        {
            Pattern = pattern;
            Group = (CommandGroup)Enum.Parse(typeof(CommandGroup), pattern.Substring(0, 1));
            Actions = actions;
            ChineseName = name;
            EnglishName = ZevisionCommandSet.EnglishNameOf(pattern) ?? name;
            Kind = kind;
            Unit = ZevisionCommandSet.Localize(unit);
            Min = min;
            Max = max;
            IsInteger = isInteger;
            Options = options ?? Array.Empty<CommandOption>();
            ReferenceGroup = referenceGroup;
            AllowNone = allowNone;
            Remark = ZevisionCommandSet.Localize(remark);
            IndexCount = (pattern.Length - pattern.Replace("**", string.Empty).Length) / 2;
            Specificity = pattern.Length - (2 * IndexCount);
            _indexNames = BuildIndexNames(pattern);
            _regex = new Regex("^" + pattern.Replace("**", "[0-9A-F]{2}") + "$", RegexOptions.CultureInvariant);
        }

        /// <summary>命令模式,如 <c>M**03</c>。</summary>
        public string Pattern { get; }

        /// <summary>命令组 (模式的第一个字符)。</summary>
        public CommandGroup Group { get; }

        /// <summary>支持的动作。</summary>
        public CommandActions Actions { get; }

        /// <summary>说明:中文为"密度",英文为设备帮助表中的 DESCRIPTION,如 <c>Density (g/cm^3)</c>。</summary>
        public string GetName(TextLanguage language) => language.Pick(ChineseName, EnglishName);

        /// <summary>中文说明 (与《U 类型指令汇总》一致)。</summary>
        public string ChineseName { get; }

        /// <summary>英文说明:设备 <c>?</c> 帮助表的 DESCRIPTION (IMC300 固件 1.8.11)。</summary>
        public string EnglishName { get; }

        /// <summary>值的类型。</summary>
        public CommandValueKind Kind { get; }

        /// <summary>单位,如 <c>g/cm³</c>、<c>秒</c>/<c>s</c>;没有时为空。</summary>
        public BilingualText Unit { get; }

        /// <summary>最小值:数字为数值,时间为秒数。</summary>
        public decimal? Min { get; }

        /// <summary>最大值:数字为数值,时间为秒数。</summary>
        public decimal? Max { get; }

        /// <summary>是否只能是整数 (仅数字)。</summary>
        public bool IsInteger { get; }

        /// <summary>固定选项 (枚举,或带参数的动作如 G16)。</summary>
        public IReadOnlyList<CommandOption> Options { get; }

        /// <summary>被引用对象的组字母,如 <c>M</c> (仅引用)。</summary>
        public char ReferenceGroup { get; }

        /// <summary>引用是否可以为 0 (未选择)。</summary>
        public bool AllowNone { get; }

        /// <summary>补充说明,如 <c>0=disabled</c>、与实机不同之处;没有时为空。</summary>
        public BilingualText Remark { get; }

        /// <summary>模式中 <c>**</c> 编号的级数,即 <see cref="Build(CommandActions, int[])"/> 需要的编号个数。</summary>
        public int IndexCount { get; }

        /// <summary>各级编号的含义,如 <c>F**03**01</c> → 膜系、预条件 / Film、Pre Condition。</summary>
        public IReadOnlyList<string> GetIndexNames(TextLanguage language) => _indexNames.Select(n => PlainIndexName(n.Get(language))).ToList();

        /// <summary>去掉编号占位符的名称,如"第 {0} 个传感器" → "传感器",用于错误说明等。</summary>
        private static string PlainIndexName(string name) =>
            name.Replace("第 {0} 个", string.Empty).Replace(" {0}", string.Empty).Trim();

        /// <summary>
        /// 第一级编号是否是对象编号:<c>**</c> 紧跟在组字母后 (<c>M**03</c>) 或是工艺 (<c>P02**</c>)。
        /// 否则是固定对象下的序号,如 <c>T0101**</c> 的输入编号。
        /// </summary>
        public bool FirstIndexIsObject => IndexCount > 0 && (Pattern.IndexOf("**", StringComparison.Ordinal) == 1 || Pattern.StartsWith("P02**", StringComparison.Ordinal));

        /// <summary>模式中的固定字符数;一个 ID 匹配多个模式时取最具体的。</summary>
        internal int Specificity { get; }

        /// <summary>是否可以更新 (U)。</summary>
        public bool CanUpdate => (Actions & CommandActions.Update) != 0;

        /// <summary>
        /// 带单位的名称,如 <c>密度 (g/cm³)</c>。英文的设备说明多已带单位 (如 <c>Density (g/cm^3)</c>),
        /// 已含括号时不再追加单位。
        /// </summary>
        public string GetLabel(TextLanguage language) => WithUnit(GetName(language), language);

        /// <summary>范围或选项说明,如 <c>0.100 ~ 100.000 g/cm³</c>、<c>0=Off, 1=On</c>;没有时为说明或空。</summary>
        public string GetRangeText(TextLanguage language)
        {
            var unit = Unit.Get(language);
            var remark = Remark.Get(language);
            switch (Kind)
            {
                case CommandValueKind.Number when Min.HasValue || Max.HasValue:
                    var range = $"{Format(Min)} ~ {Format(Max)}" + (string.IsNullOrEmpty(unit) ? string.Empty : " " + unit);
                    return string.IsNullOrEmpty(remark) ? range : range + ", " + remark;
                case CommandValueKind.Enum:
                    return string.Join(", ", Options.Select(o => o.Value + "=" + o.Text.Get(language)));
                case CommandValueKind.Time when Max.HasValue:
                    return $"H:MM:SS ({FormatTime(Min ?? 0)} ~ {FormatTime(Max.Value)})";
                case CommandValueKind.Time:
                    return language.Pick("H:MM:SS (小时 0 ~ 8760)", "H:MM:SS (hours 0 ~ 8760)");
                case CommandValueKind.Reference:
                    var kind = ZevisionCommandSet.ObjectKind(ReferenceGroup, language);
                    return language.IsChinese()
                        ? (AllowNone ? "0=未选择, " : string.Empty) + "已有的" + kind + "编号"
                        : (AllowNone ? "0=None, " : string.Empty) + "number of an existing " + kind.ToLowerInvariant();
                default:
                    return remark;
            }
        }

        /// <summary>命令 ID 是否属于这条指令,如 <c>M0103</c> 属于 <c>M**03</c>。</summary>
        public bool IsMatch(string id) => id != null && _regex.IsMatch(id);

        /// <summary>
        /// 某一项的显示名称:子列表中的项在前面加上列表名和序号,以区分同名的项,
        /// 如 <c>F01030201</c> → <c>预条件 2 · 起始斜坡/上升时间</c>。第一级编号是对象本身时不显示
        /// (<c>T010103</c> 的第一级是输入编号:<c>输入 3 · 基本事件</c>)。
        /// </summary>
        /// <param name="id">命令 ID。</param>
        /// <param name="language">语言。</param>
        /// <param name="deviceDescription">
        /// 设备帮助表中该项的 DESCRIPTION。英文优先用它 (就是设备自己的说明),为空时用 <see cref="EnglishName"/>;
        /// 中文用中文名称。
        /// </param>
        public string LabelFor(string id, TextLanguage language, string? deviceDescription = null)
        {
            var useDeviceDescription = !language.IsChinese() && !string.IsNullOrEmpty(deviceDescription);
            var label = useDeviceDescription ? WithUnit(deviceDescription!, language) : GetLabel(language);
            if (!IsMatch(id))
            {
                return label;
            }

            // 第一级通常是对象本身 (M**、P02**),由分组标题显示;T0101** 这类固定对象下的第一级是序号 (输入 n),要显示。
            // 设备的英文说明已带这一级编号 (Input 1: Basic Event),不再重复。
            var numbers = ParseIndices(id);
            var parts = new List<string>();
            for (var level = FirstIndexIsObject || useDeviceDescription ? 1 : 0; level < numbers.Count; level++)
            {
                var name = _indexNames[level].Get(language); // 可能含编号占位符 {0}
                var number = numbers[level].ToString(CultureInfo.InvariantCulture);
                parts.Add(name.Contains("{0}") ? string.Format(CultureInfo.InvariantCulture, name, number) : name + " " + number);
            }

            return parts.Count == 0 ? label : string.Join(" ", parts) + " · " + label;
        }

        /// <summary>名称后加单位;英文的设备说明多已带单位 (如 <c>Density (g/cm^3)</c>),已含括号时不再追加。</summary>
        private string WithUnit(string name, TextLanguage language)
        {
            var unit = Unit.Get(language);
            if (string.IsNullOrEmpty(unit) || (!language.IsChinese() && name.IndexOf('(') >= 0))
            {
                return name;
            }

            return $"{name} ({unit})";
        }

        /// <summary>
        /// 从命令 ID 中取出各级编号 (十进制),如 <c>M**03</c> 的 <c>M0A03</c> → [10]。
        /// </summary>
        /// <exception cref="ArgumentException">ID 不属于这条指令。</exception>
        public IReadOnlyList<int> ParseIndices(string id)
        {
            if (!IsMatch(id))
            {
                throw new ArgumentException($"{id} does not belong to command {Pattern}.", nameof(id));
            }

            var numbers = new List<int>(IndexCount);
            for (var i = Pattern.IndexOf("**", StringComparison.Ordinal); i >= 0; i = Pattern.IndexOf("**", i + 2, StringComparison.Ordinal))
            {
                numbers.Add(int.Parse(id.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }

            return numbers;
        }

        /// <summary>
        /// 生成命令 ID:按顺序把编号 (十进制,如材料 10) 以两位十六进制填入 <c>**</c>,
        /// 如 <c>M**03</c> 与 10 → <c>M0A03</c>。
        /// </summary>
        /// <param name="indices">各级编号,个数必须等于 <see cref="IndexCount"/>,每个在 1~253 (01~FD) 之间。</param>
        /// <exception cref="ArgumentException">编号个数不对。</exception>
        /// <exception cref="ArgumentOutOfRangeException">编号超出范围。</exception>
        public string FormatId(params int[] indices)
        {
            indices = indices ?? Array.Empty<int>();
            if (indices.Length != IndexCount)
            {
                var names = IndexCount == 0
                    ? "takes no numbers"
                    : $"needs {IndexCount} number(s) ({string.Join(", ", GetIndexNames(TextLanguage.English))})";
                throw new ArgumentException($"Command {Pattern} {names}, got {indices.Length}.", nameof(indices));
            }

            var sb = new StringBuilder(Pattern.Length);
            var next = 0;
            for (var i = 0; i < Pattern.Length; i++)
            {
                if (Pattern[i] == '*' && i + 1 < Pattern.Length && Pattern[i + 1] == '*')
                {
                    var number = indices[next];
                    if (number < MinIndex || number > MaxIndex)
                    {
                        throw new ArgumentOutOfRangeException(nameof(indices), number, $"{GetIndexNames(TextLanguage.English)[next]} number must be {MinIndex}~{MaxIndex}.");
                    }

                    sb.Append(number.ToString("X2", CultureInfo.InvariantCulture));
                    next++;
                    i++;
                }
                else
                {
                    sb.Append(Pattern[i]);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 生成不带数据的指令,如 <c>Build(Query, 1)</c> → <c>QM0103</c>、<c>Build(Remote)</c> → <c>RC03</c>、
        /// <c>Build(None)</c> → <c>H01</c>。
        /// </summary>
        /// <exception cref="ArgumentException">动作不支持、需要数据 (更新),或编号个数不对。</exception>
        /// <exception cref="ArgumentOutOfRangeException">编号超出范围。</exception>
        public string Build(CommandActions action, params int[] indices) => BuildCore(action, null, indices);

        /// <summary>
        /// 生成带数据的指令,如 <c>Build(Update, "2.700", 1)</c> → <c>UM0103:2.700</c>、<c>Build(None, "abc")</c> → <c>E:abc</c>。
        /// 只检查数据中没有协议不允许的字符 (<see cref="ProtocolText.ValidateValue"/>),不检查范围。
        /// </summary>
        /// <exception cref="ArgumentException">动作不支持或不带数据、数据含非法字符,或编号个数不对。</exception>
        /// <exception cref="ArgumentOutOfRangeException">编号超出范围。</exception>
        public string Build(CommandActions action, string value, params int[] indices)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return BuildCore(action, value, indices);
        }

        /// <inheritdoc />
        public override string ToString() => $"{Pattern} {EnglishName}";

        /// <summary>是否支持该动作。<see cref="CommandActions.None"/> 只适用于不带动作字母的指令 (H01、E)。</summary>
        public bool Supports(CommandActions action) =>
            action == CommandActions.None ? Actions == CommandActions.None : (Actions & action) == action;

        private string BuildCore(CommandActions action, string? value, int[] indices)
        {
            ZevisionCommandSet.CheckAction(action, value, this);
            return ZevisionCommandSet.Compose(action, FormatId(indices), value);
        }

        private static IReadOnlyList<BilingualText> BuildIndexNames(string pattern)
        {
            var names = new List<BilingualText>();
            for (var i = pattern.IndexOf("**", StringComparison.Ordinal); i >= 0; i = pattern.IndexOf("**", i + 2, StringComparison.Ordinal))
            {
                names.Add(ZevisionCommandSet.IndexName(pattern.Substring(0, i)));
            }

            return names;
        }

        private static string FormatTime(decimal seconds)
        {
            var total = (long)seconds;
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}:{2:D2}", total / 3600, total / 60 % 60, total % 60);
        }

        private static string Format(decimal? value) =>
            value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }
}
