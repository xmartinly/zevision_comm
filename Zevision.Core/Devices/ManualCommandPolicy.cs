using System;
using System.Collections.Generic;
using System.Linq;
using Zevision.Commands;
using Zevision.Core.Configuration;

namespace Zevision.Core.Devices
{
    /// <summary>
    /// 手动输入指令的整理。帮助指令 <c>?</c> (含 <c>?M</c> 等分组帮助) 允许手动发送,用于调试。
    /// </summary>
    public static class ManualCommandPolicy
    {
        /// <summary>
        /// 把每条指令的命令部分 (第一个 <c>:</c> 之前) 转为大写,数据部分 (<c>:</c> 之后,到分隔符 <c>;</c>、<c>；</c> 或换行为止) 保持原样。
        /// 设备只认大写命令 (小写回复 <c>-A</c>);数据可能是名称等文本,不能改动。分隔符和换行原样保留。
        /// </summary>
        /// <example><c>qg01;um0101:Aluminium</c> → <c>QG01;UM0101:Aluminium</c></example>
        public static string ToUpperCommands(string input)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            var chars = input.ToCharArray();
            var inData = false;
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (c == ';' || c == '；' || c == '\r' || c == '\n')
                {
                    inData = false;
                }
                else if (inData)
                {
                    continue;
                }
                else if (c == ':')
                {
                    inData = true;
                }
                else
                {
                    chars[i] = char.ToUpperInvariant(c);
                }
            }

            return new string(chars);
        }

        /// <summary>
        /// 把输入拆成单条指令:按 <c>;</c> 和换行分隔 (多行输入框中每行一条;中文输入法的全角 <c>；</c> 也视为分隔符),
        /// 去掉每条首尾空白,丢弃空段。多条指令合并发送时用 <c>;</c> 重新连接。
        /// </summary>
        public static IReadOnlyList<string> Split(string input)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            return input
                .Split(new[] { ';', '；', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        /// <summary>
        /// 指令 (含用 <c>;</c> 分隔的多条) 中是否有帮助指令 <c>?</c>;有时发送使用较长的最低响应超时。
        /// </summary>
        public static bool ContainsHelpCommand(string command) =>
            Split(command).Any(segment => segment.StartsWith(ZevisionCommandSet.HelpCommand, StringComparison.Ordinal));

        /// <summary>
        /// 指令中是否有完整的帮助指令 <c>?</c> (不是 <c>?M</c> 这样的分组帮助)。完整帮助约 116 KB、10 秒,发送前提示用户。
        /// </summary>
        public static bool ContainsFullHelpCommand(string command) =>
            Split(command).Any(segment => segment == ZevisionCommandSet.HelpCommand);

        /// <summary>
        /// 发送时的最低响应超时:帮助指令的响应大、首字节前和分包之间有停顿,与保存配置读取 <c>?</c> 时一样用
        /// <see cref="InstrumentConfigReader.SlowReplyTimeout"/>;其他指令为 <see cref="TimeSpan.Zero"/> (用连接设置的超时)。
        /// </summary>
        public static TimeSpan MinimumResponseTimeout(string command) =>
            ContainsHelpCommand(command) ? InstrumentConfigReader.SlowReplyTimeout : TimeSpan.Zero;
    }
}
