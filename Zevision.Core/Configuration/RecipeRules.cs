using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Zevision.Commands;
using Zevision.Communication;

namespace Zevision.Core.Configuration
{
    /// <summary>
    /// 哪些配置项可以保存到 recipe 并恢复到设备。所有规则集中在这里维护。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 帮助表不区分只读和可写,还混有动作命令 (如 C03 厚度清零、G0D 复位仪器)。
    /// 盲目写回会触发动作或改坏通讯参数,因此采用"组白名单 + 排除规则"。
    /// </para>
    /// <para>
    /// 规则依据 IMC300 固件 1.8.11 的帮助表推断,尚未逐项在实机上验证。
    /// 恢复时遇到设备拒绝 (例如对只读项写入) 会立即停止并报告 ID,据此调整本类即可。
    /// </para>
    /// </remarks>
    public static class RecipeRules
    {
        /// <summary>
        /// 恢复顺序:先系统设置 (传感器 N、源 S、输入 I、输出 O、逻辑 T、系统偏好 G),
        /// 再材料 M → 膜层 F → 层 L → 工艺 P (被引用的对象在前)。未列出的组排在最后。
        /// </summary>
        private static readonly string GroupOrder = "NSIOTGMFLP";

        /// <summary>第一个对象组 (材料) 在 <see cref="GroupOrder"/> 中的位置;在它之前的都是系统设置。</summary>
        private static readonly int FirstObjectGroupRank = GroupOrder.IndexOf('M');

        /// <summary>
        /// 是否是系统设置 (传感器、源、输入输出、逻辑、系统偏好)。系统设置不需要创建对象,恢复时最先写入。
        /// </summary>
        public static bool IsSystemSetting(string id) => GroupRank(id) < FirstObjectGroupRank;

        private static readonly IReadOnlyList<Rule> Exclusions = new[]
        {
            // 列表计数 (Number of ...) 由列表条目决定,不能直接写。
            new Rule("^[A-Z].*FE$", () => Texts.ExcludedListCount),

            // 传感器的运行状态。
            new Rule("^N[0-9A-F]{2}09$", () => Texts.ExcludedSensorPosition),
            new Rule("^N[0-9A-F]{2}10", () => Texts.ExcludedQcmRuntime),
            new Rule("^N[0-9A-F]{2}1[1-7]$", () => Texts.ExcludedQcmState),
            new Rule("^N[0-9A-F]{2}1[C-E]$", () => Texts.ExcludedXiu),
            new Rule("^N[0-9A-F]{2}20$", () => Texts.ExcludedRateDeviation),

            // 源:当前输出值是状态;开关挡板、切换坩埚是物理动作。
            new Rule("^S[0-9A-F]{2}0[34]$", () => Texts.ExcludedSourceOutput),
            new Rule("^S[0-9A-F]{2}06$", () => Texts.ExcludedShutter),
            new Rule("^S[0-9A-F]{2}08$", () => Texts.ExcludedCrucible),

            // 输入输出:分配情况和当前状态是状态;写输出状态会直接动作继电器。
            new Rule("^I[0-9A-F]{2}0[345]$", () => Texts.ExcludedInputState),
            new Rule("^O[0-9A-F]{2}0[345]$", () => Texts.ExcludedOutputState),

            // 计数器和历史值。
            new Rule("^P02[0-9A-F]{2}06$", () => Texts.ExcludedProcessRunNumber),
            new Rule("^F[0-9A-F]{2}07[0-9A-F]{2}04$", () => Texts.ExcludedLastCrystal),
        };

        /// <summary>
        /// 判断帮助表中的一项是否保存到 recipe。
        /// </summary>
        /// <param name="id">命令 ID。</param>
        /// <param name="value">当前值。</param>
        /// <param name="reason">不保存时的原因。</param>
        public static bool IsRestorable(string id, string value, out string reason)
        {
            if (id is null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (string.IsNullOrEmpty(value))
            {
                reason = Texts.NotRestorableNoValue;
                return false;
            }

            if (!IsIncludedGroup(id))
            {
                reason = Texts.NotRestorableGroup;
                return false;
            }

            // 帮助表的 ID 由 HelpTableParser 读出,不检查每级两位:这种 ID 生成不了写入指令,保存的配置文件也读不回来。
            if (!DeviceIds.IsWellFormed(id))
            {
                reason = Texts.NotRestorableMalformedId;
                return false;
            }

            foreach (var rule in Exclusions)
            {
                if (rule.Pattern.IsMatch(id))
                {
                    reason = rule.Reason;
                    return false;
                }
            }

            // 指令集中有定义但不支持 U 的项 (如 N**08 位置数量、N**1F XIU 电缆长度) 写入会被设备拒绝。
            var definition = ZevisionCommandSet.Find(id);
            if (definition != null && !definition.CanUpdate)
            {
                reason = Texts.NotRestorableReadOnly;
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// 从帮助表中挑出保存到 recipe 的项 (<see cref="IsRestorable"/>);ID 重复时只取第一次出现的。
        /// </summary>
        public static List<RecipeEntry> SelectRestorable(IEnumerable<HelpTableEntry> entries)
        {
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            return entries
                .Where(e => seen.Add(e.Id) && IsRestorable(e.Id, e.Value, out _))
                .Select(e => new RecipeEntry { Id = e.Id, Value = e.Value, Description = e.Description })
                .ToList();
        }

        /// <summary>
        /// 值是对其他对象编号 (十进制) 引用的设置项,值 0 表示"未选择"。
        /// 只收录与实机数据一致的规则:F??02 → M (F2002=16 → M10、F0902=10 → M0A,删除 M03 后 F0302 变 0),
        /// L??02??01 → F (L03020101=3 → F03)。
        /// 工艺中的层 (P02??07??) 不是层编号:实机 P0242 引用 31/32/33,而设备只有 L01~L09,因此不收录。
        /// </summary>
        private static readonly IReadOnlyList<ReferenceRule> References = new[]
        {
            new ReferenceRule("^F[0-9A-F]{2}02$", ObjectTypes.Material),          // 膜系的材料
            new ReferenceRule("^L[0-9A-F]{2}02[0-9A-F]{2}01$", ObjectTypes.Film), // 膜层中的膜系
        };

        /// <summary>
        /// 删除对象前的引用检查额外使用的规则 (只用于删除检查,不参与恢复计划和下拉框):
        /// <list type="bullet">
        /// <item>工艺包含膜层:工艺的膜层列表 <c>P02??07??</c> 的值是膜层编号 (00 是增删指令,FE 是数量,不算)。
        /// 实机上有指向不存在膜层的值 (31/32/33,而只有 L01~L09),所以不能用于恢复计划 (会跳过这些写入),
        /// 但删除检查只看是否等于要删除的膜层,不受影响。</item>
        /// <item>工艺被"设置活动工艺" (<c>P01</c>) 和"链接工艺 ID" (<c>P02??0A</c>) 引用,值是工艺编号 (实机 P01 = 1 对应 P0201)。</item>
        /// <item>输出源的坩埚材料 <c>S??09??</c> 的值是材料编号 (指令集中定义为引用)。恢复计划暂不使用:它属于系统设置,
        /// 在创建材料之前写入,是否据此跳过需要实机确认。</item>
        /// </list>
        /// </summary>
        private static readonly IReadOnlyList<ReferenceRule> DeleteCheckReferences = new[]
        {
            new ReferenceRule("^P02[0-9A-F]{2}07(?!00$|FE$)[0-9A-F]{2}$", ObjectTypes.Layer),
            new ReferenceRule("^P01$", ObjectTypes.Process),
            new ReferenceRule("^P02[0-9A-F]{2}0A$", ObjectTypes.Process),
            new ReferenceRule("^S[0-9A-F]{2}09(?!00$|FE$)[0-9A-F]{2}$", ObjectTypes.Material),
        };

        /// <summary>删除检查用的全部规则。</summary>
        private static readonly IReadOnlyList<ReferenceRule> AllReferences = References.Concat(DeleteCheckReferences).ToList();

        /// <summary>
        /// 找出引用了 <paramref name="objectId"/> 的设置项 (删除前检查)。层级关系:工艺包含膜层,膜层包含膜系,膜系包含材料,
        /// 即膜系的材料 (F??02)、膜层中的膜系 (L??02??01)、工艺中的膜层 (P02??07??),以及活动工艺 (P01) 和链接工艺 (P02??0A)。
        /// 值为 0 (未选择) 的不算。
        /// </summary>
        public static IReadOnlyList<RecipeEntry> FindReferencesTo(IEnumerable<RecipeEntry> entries, string objectId)
        {
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (string.IsNullOrEmpty(objectId))
            {
                throw new ArgumentException(Texts.ObjectIdEmpty, nameof(objectId));
            }

            return entries.Where(e => AllReferences.Any(rule => rule.Refers(e.Id, e.Value, objectId))).ToList();
        }

        /// <summary>
        /// 设置项的值是否引用了另一个对象,如 <c>F0302 = 3</c> → <c>M03</c>。
        /// </summary>
        /// <returns>值引用了对象时返回 true (值为 0 或不是数字时返回 false)。</returns>
        public static bool TryGetReference(string id, string value, out string targetId, out string kind)
        {
            foreach (var rule in References)
            {
                if (rule.TryGetTarget(id, value, out targetId))
                {
                    kind = rule.Kind;
                    return true;
                }
            }

            targetId = string.Empty;
            kind = string.Empty;
            return false;
        }

        /// <summary>
        /// 恢复时的排序键:先按组顺序,组内保持设备帮助表中的原始顺序 (由调用方稳定排序保证)。
        /// </summary>
        public static int GroupRank(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return int.MaxValue;
            }

            var index = GroupOrder.IndexOf(id[0]);
            return index >= 0 ? index : GroupOrder.Length;
        }

        private static bool IsIncludedGroup(string id)
        {
            switch (id[0])
            {
                case 'F': // 膜层
                case 'L': // 层
                case 'M': // 材料
                case 'N': // 传感器
                case 'P': // 工艺
                case 'S': // 源
                case 'T': // 逻辑
                case 'I': // 输入 (名称、极性)
                case 'O': // 输出 (名称、常态)
                    return true;

                case 'G':
                    // 系统组只保存显示 (G0E)、声音 (G0F)、图表 (G12) 偏好;
                    // 通讯 (G13/G14)、时间 (G0A)、复位 (G0D)、锁定 (G11) 等一律排除。
                    return id.StartsWith("G0E", StringComparison.Ordinal)
                        || id.StartsWith("G0F", StringComparison.Ordinal)
                        || id.StartsWith("G12", StringComparison.Ordinal);

                default:
                    return false;
            }
        }

        private sealed class ReferenceRule
        {
            private readonly Regex _pattern;
            private readonly ObjectType _target;

            public ReferenceRule(string pattern, ObjectType target)
            {
                _pattern = new Regex(pattern, RegexOptions.CultureInvariant);
                _target = target;
            }

            /// <summary>被引用对象的名称 (句中小写),如"材料" / "material"。</summary>
            public string Kind => _target.Kind.ToLowerInvariant();

            /// <summary>值是 1~255 的对象编号时,给出被引用对象的 ID (0 = 未选择,不是引用)。</summary>
            public bool TryGetTarget(string? id, string? value, out string targetId)
            {
                if (_pattern.IsMatch(id ?? string.Empty)
                    && DeviceIds.TryParseCount(value, out var number)
                    && number > 0
                    && number <= 0xFF)
                {
                    targetId = DeviceIds.Item(_target.Prefix, number);
                    return true;
                }

                targetId = string.Empty;
                return false;
            }

            public bool Refers(string id, string value, string objectId) =>
                TryGetTarget(id, value, out var target) && string.Equals(target, objectId, StringComparison.Ordinal);
        }

        /// <summary>排除规则:ID 模式与原因 (调用时按界面语言取)。</summary>
        private sealed class Rule
        {
            private readonly Func<string> _reason;

            public Rule(string pattern, Func<string> reason)
            {
                Pattern = new Regex(pattern, RegexOptions.CultureInvariant);
                _reason = reason;
            }

            public Regex Pattern { get; }

            public string Reason => _reason();
        }
    }
}
