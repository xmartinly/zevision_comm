namespace Zevision.Communication
{
    /// <summary>
    /// <see cref="ProtocolText"/> 的检查结果。
    /// </summary>
    public enum ProtocolTextError
    {
        /// <summary>合法。</summary>
        None,

        /// <summary>为空。</summary>
        Empty,

        /// <summary>含有可打印 ASCII 以外的字符。</summary>
        NotPrintableAscii,

        /// <summary>含有起止符 <c>{</c> 或 <c>}</c>。</summary>
        Delimiter,

        /// <summary>设置值中含有命令分隔符 <c>;</c>。</summary>
        Separator,
    }

    /// <summary>
    /// 可以放进 Zevision 帧的文本规则。命令和设置值的校验都以此为准,避免各处规则不一致。
    /// </summary>
    public static class ProtocolText
    {
        /// <summary>
        /// 检查一条或多条 (<c>;</c> 分隔) 命令:不能为空,只能是可打印 ASCII,不能含起止符 <c>{ }</c>。
        /// </summary>
        /// <param name="command">命令数据。</param>
        /// <param name="offending">第一个不允许的字符;合法或为空时为 <c>'\0'</c>。</param>
        public static ProtocolTextError CheckCommand(string? command, out char offending) => Check(command, allowSeparator: true, out offending);

        /// <summary>
        /// 检查一个设置值 (<c>U&lt;id&gt;:&lt;值&gt;</c> 中的值):不能为空,只能是可打印 ASCII,
        /// 不能含命令分隔符 <c>;</c> 和起止符 <c>{ }</c>。
        /// </summary>
        /// <param name="value">设置值。</param>
        /// <param name="offending">第一个不允许的字符;合法或为空时为 <c>'\0'</c>。</param>
        public static ProtocolTextError CheckValue(string? value, out char offending) => Check(value, allowSeparator: false, out offending);

        /// <summary>
        /// 校验命令 (规则见 <see cref="CheckCommand"/>)。
        /// </summary>
        /// <returns>英文错误说明;合法时为 null。</returns>
        public static string? ValidateCommand(string command)
        {
            switch (CheckCommand(command, out var c))
            {
                case ProtocolTextError.None:
                    return null;
                case ProtocolTextError.Empty:
                    return "The command must not be empty.";
                case ProtocolTextError.NotPrintableAscii:
                    return $"Commands may only contain printable ASCII characters; found U+{(int)c:X4}.";
                default:
                    return "Commands must not contain the delimiters { or }; they are added automatically.";
            }
        }

        /// <summary>
        /// 校验设置值 (规则见 <see cref="CheckValue"/>)。
        /// </summary>
        /// <returns>英文错误说明;合法时为 null。</returns>
        public static string? ValidateValue(string value)
        {
            switch (CheckValue(value, out _))
            {
                case ProtocolTextError.None:
                    return null;
                case ProtocolTextError.Empty:
                    return "The value must not be empty";
                case ProtocolTextError.NotPrintableAscii:
                    return "Only letters, digits and common symbols (printable ASCII) are allowed";
                default:
                    return "Must not contain ; { }";
            }
        }

        private static ProtocolTextError Check(string? text, bool allowSeparator, out char offending)
        {
            offending = '\0';
            if (string.IsNullOrEmpty(text))
            {
                return ProtocolTextError.Empty;
            }

            foreach (var c in text!)
            {
                var error = c < 0x20 || c > 0x7E ? ProtocolTextError.NotPrintableAscii
                    : c == '{' || c == '}' ? ProtocolTextError.Delimiter
                    : c == ';' && !allowSeparator ? ProtocolTextError.Separator
                    : ProtocolTextError.None;
                if (error != ProtocolTextError.None)
                {
                    offending = c;
                    return error;
                }
            }

            return ProtocolTextError.None;
        }
    }
}
