using System;
using System.Collections.Generic;
using System.Linq;
using Zevision.Communication;

namespace Zevision.Commands
{
    /// <summary>
    /// Zevision 指令集:每条指令的命令模式、动作、说明、单位和取值范围。用法:
    /// <code>
    /// ZevisionCommandSet.TryGetGroup("M0103", out var group);           // group = CommandGroup.M
    /// var commands = ZevisionCommandSet.GetCommands(group);              // 材料组的全部指令
    /// var density = ZevisionCommandSet.Find("M0103");                    // M**03 密度
    /// density.Build(CommandActions.Update, "2.700", 1);                  // "UM0103:2.700"
    /// ZevisionCommandSet.Update("M0103", "2.700");                        // 已有完整 ID 时:"UM0103:2.700"
    /// </code>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 来源:<c>docs/INFICON_IMC300_Zevision通讯协议说明.md</c> 第 4 章 (动作、说明、范围) 和
    /// <c>docs/INFICON_IMC300_Zevision_U类型指令汇总.xlsx</c> (U 指令的说明、范围,由 <c>ZevisionCommandSetTests</c> 读取 xlsx 核对)。
    /// 与文档不同、按实机 (IMC300 固件 1.8.11) 补充或调整的地方都在对应行注明。
    /// </para>
    /// <para>
    /// <c>**</c> 是一级编号 (两位十六进制):第一级是对象编号,如 <c>M**03</c> 中的材料编号、<c>F**02</c> 中的膜系编号;
    /// 之后的是子列表中的序号。逻辑组 (T) 在文档中没有具体指令,只收录实机帮助表中的基本 IO 逻辑 (T0101** / T0102**),选项取自文档的输入/输出分配范围。帮助 <c>?</c> 不属于任何组,不在表中。
    /// </para>
    /// <para>
    /// 结构:本类是公开的接口 (查找、生成命令);指令定义在内部的 <c>CommandTable</c>,文字在 <c>CommandTexts</c>。
    /// 全部数据在第一次使用时生成,之后不变 (只读列表),可以在多个线程中同时使用。
    /// </para>
    /// </remarks>
    public static class ZevisionCommandSet
    {
        /// <summary>帮助指令:返回全部设置项及当前值的帮助表 (约 116 KB)。不属于任何命令组。</summary>
        public const string HelpCommand = "?";

        /// <summary>全部指令 (只读),按协议文档的顺序。</summary>
        public static IReadOnlyList<CommandDefinition> All => CommandTable.All;

        /// <summary>
        /// 取一个命令组的全部指令 (只读),如 <see cref="CommandGroup.M"/> → 材料的全部指令 (M00、M**、M**01、M**03…)。
        /// </summary>
        public static IReadOnlyList<CommandDefinition> GetCommands(CommandGroup group) =>
            CommandsOf(group.ToString()[0]) ?? Array.Empty<CommandDefinition>();

        /// <summary>
        /// 由命令 ID 的第一个字符得到命令组,如 <c>M0103</c> → <see cref="CommandGroup.M"/>。不区分大小写。
        /// </summary>
        /// <returns>第一个字符是已知的命令组时返回 true。</returns>
        public static bool TryGetGroup(string id, out CommandGroup group)
        {
            group = default;
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            var letter = char.ToUpperInvariant(id[0]);
            if (letter < 'A' || letter > 'Z' || !(CommandTable.Groups[letter - 'A'] is CommandGroup found))
            {
                return false;
            }

            group = found;
            return true;
        }

        /// <summary>
        /// 查找命令 ID 对应的指令,如 <c>M0103</c> → <c>M**03</c> (密度)。只在该 ID 所属命令组中查找;
        /// 匹配多个模式时取固定字符最多的 (如 <c>MFE</c> 是材料数量而不是 <c>M**</c>)。ID 不带动作字母,区分大小写 (设备只认大写)。
        /// </summary>
        /// <returns>找不到时返回 null。</returns>
        public static CommandDefinition? Find(string id)
        {
            var commands = string.IsNullOrEmpty(id) ? null : CommandsOf(id[0]);
            if (commands == null)
            {
                return null;
            }

            CommandDefinition? best = null;
            for (var i = 0; i < commands.Count; i++)
            {
                var definition = commands[i];
                if (definition.IsMatch(id) && (best == null || definition.Specificity > best.Specificity))
                {
                    best = definition;
                }
            }

            return best;
        }

        /// <summary>
        /// 命令 ID 的格式是否正确:大写组字母后跟若干两位大写十六进制编号,如 <c>M0103</c>、<c>P02FE</c>、<c>E</c>。
        /// 不检查组字母是否是已知的命令组。
        /// </summary>
        public static bool IsWellFormedId(string? id)
        {
            if (id == null || id.Length % 2 == 0 || id[0] < 'A' || id[0] > 'Z')
            {
                return false;
            }

            for (var i = 1; i < id.Length; i++)
            {
                if (!CommandDefinition.IsUpperHex(id[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 由完整的命令 ID 生成指令,如 <c>Build(Update, "M0103", "2.700")</c> → <c>UM0103:2.700</c>。
        /// ID 在指令集中有定义时检查动作是否支持;没有定义的 ID (如逻辑组、文档未列出的设置项) 只检查格式,由设备判断。
        /// </summary>
        /// <param name="action">单个动作。</param>
        /// <param name="id">命令 ID (大写,不带动作字母)。</param>
        /// <param name="value">数据;更新时必须给出,查询/添加/删除/状态不能给出。</param>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> 为 null。</exception>
        /// <exception cref="ArgumentException">ID 格式不对、动作不支持或组合了多个动作、数据缺失/多余/含非法字符。</exception>
        public static string Build(CommandActions action, string id, string? value = null)
        {
            if (id is null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (!IsWellFormedId(id) || CommandsOf(id[0]) == null)
            {
                throw new ArgumentException($"Invalid command ID: {id}", nameof(id));
            }

            CheckAction(action, value, Find(id));
            return Compose(action, id, value);
        }

        /// <summary>查询指令,如 <c>QM0103</c>。</summary>
        /// <exception cref="ArgumentException">见 <see cref="Build(CommandActions, string, string)"/>。</exception>
        public static string Query(string id) => Build(CommandActions.Query, id);

        /// <summary>更新指令,如 <c>UM0103:2.700</c>。</summary>
        /// <exception cref="ArgumentException">见 <see cref="Build(CommandActions, string, string)"/>。</exception>
        public static string Update(string id, string value) => Build(CommandActions.Update, id, value ?? throw new ArgumentNullException(nameof(value)));

        /// <summary>添加指令,如 <c>AM03</c> (按编号创建材料)、<c>AF010300</c> (在膜系 F01 的预熔列表末尾追加)。</summary>
        /// <exception cref="ArgumentException">见 <see cref="Build(CommandActions, string, string)"/>。</exception>
        public static string Add(string id) => Build(CommandActions.Add, id);

        /// <summary>动作字母:Query → <c>Q</c>,None → 空。</summary>
        /// <exception cref="ArgumentException">不是单个动作。</exception>
        public static string ActionLetter(CommandActions action)
        {
            switch (action)
            {
                case CommandActions.None: return string.Empty;
                case CommandActions.Query: return "Q";
                case CommandActions.Update: return "U";
                case CommandActions.Add: return "A";
                case CommandActions.Delete: return "D";
                case CommandActions.Status: return "S";
                case CommandActions.Remote: return "R";
                default: throw new ArgumentException("Must be a single action.", nameof(action));
            }
        }

        /// <summary>对象类型的名称,如 <c>M</c> → 材料 / Material;未知时为"对象"/Object。</summary>
        public static string ObjectKind(char group, TextLanguage language) => CommandTexts.ObjectKind(group, language);

        /// <summary>
        /// 某一级编号 (列表) 的名称,参数为该级 <c>**</c> 之前的模式,如 <c>F**03</c> → 预熔 / Pre Condition、
        /// <c>P02**07</c> → 膜层 / Layer;未收录时为 <c>#</c>。名称可能含编号占位符 <c>{0}</c> (如 <c>F**07</c>)。
        /// </summary>
        public static BilingualText IndexName(string prefixPattern) => CommandTexts.IndexName(prefixPattern);

        /// <summary>
        /// 检查动作和数据:只能是单个动作;<paramref name="definition"/> 不为 null 时动作必须受支持;
        /// 更新必须带数据,查询/添加/删除/状态不能带数据,数据不能含协议不允许的字符。
        /// </summary>
        /// <exception cref="ArgumentException">不符合上述规则。</exception>
        internal static void CheckAction(CommandActions action, string? value, CommandDefinition? definition)
        {
            if (action != CommandActions.None && (action & (action - 1)) != 0)
            {
                throw new ArgumentException("Only one action can be given.", nameof(action));
            }

            if (definition != null && !definition.Supports(action))
            {
                throw new ArgumentException($"Command {definition.Pattern} ({definition.EnglishName}) does not support {action}; supported: {definition.Actions}.", nameof(action));
            }

            if (action == CommandActions.Update && value == null)
            {
                throw new ArgumentException("An update command needs a value.", nameof(value));
            }

            if (value == null)
            {
                return;
            }

            if (action != CommandActions.Update && action != CommandActions.Remote && action != CommandActions.None)
            {
                throw new ArgumentException($"Action {action} takes no value.", nameof(value));
            }

            var error = ProtocolText.ValidateValue(value);
            if (error != null)
            {
                throw new ArgumentException(error, nameof(value));
            }
        }

        /// <summary>拼接:动作字母 + ID + [<c>:</c>数据]。调用前已由 <see cref="CheckAction"/> 检查。</summary>
        internal static string Compose(CommandActions action, string id, string? value)
        {
            var command = ActionLetter(action) + id;
            return value == null ? command : command + ":" + value;
        }

        /// <summary>组字母 (区分大小写) 的指令;不是命令组时为 null。</summary>
        private static IReadOnlyList<CommandDefinition>? CommandsOf(char letter) =>
            letter >= 'A' && letter <= 'Z' ? CommandTable.ByLetter[letter - 'A'] : null;
    }
}
