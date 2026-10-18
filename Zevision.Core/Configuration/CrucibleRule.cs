using System;
using Zevision.Commands;

namespace Zevision.Core.Configuration
{
    /// <summary>
    /// 膜系的坩埚号 (<c>F**0902</c>):0 = 任意 (Any),1 ~ 膜系所选输出源 (<c>F**0901</c>) 的坩埚数量 (<c>S**07</c>)。
    /// 输出源未选择、不存在,或其坩埚数量未知或为 0 时,最大坩埚号按 <see cref="DefaultCount"/> 计。
    /// </summary>
    /// <remarks>
    /// 设备上坩埚号的选项为 Any、1 ~ 最大数量 (用户确认,2026-10-18);协议文档写的 "0 = disabled" 与设备不符。
    /// 修改窗口的下拉框、恢复计划和写入前的检查都用这里的规则。
    /// </remarks>
    public static class CrucibleRule
    {
        /// <summary>"任意坩埚"的值。</summary>
        public const string Any = "0";

        /// <summary>输出源的坩埚数量未知或为 0 时的最大坩埚号。</summary>
        public const int DefaultCount = 1;

        private const string CrucibleSuffix = "0902";
        private const string SourceSuffix = "0901";
        private const string SourcePrefix = "S";
        private const string CountSuffix = "07";

        /// <summary>是否是膜系的坩埚号项,如 <c>F010902</c>。</summary>
        public static bool IsCrucible(string id) =>
            ObjectTypes.Film.TryGetObjectId(id, out var film) && id.Length == film.Length + CrucibleSuffix.Length && id.EndsWith(CrucibleSuffix, StringComparison.Ordinal);

        /// <summary>
        /// 坩埚号的最大值:膜系所选输出源的坩埚数量;未知或为 0 时为 <see cref="DefaultCount"/>。
        /// </summary>
        /// <param name="crucibleId">坩埚号项,如 <c>F010902</c>。</param>
        /// <param name="valueOf">按 ID 取值 (配置、设备快照,或修改后的值);没有该项时返回 null。</param>
        public static int MaxFor(string crucibleId, Func<string, string?> valueOf) =>
            TryGetCount(crucibleId, valueOf, out _, out var count) ? Math.Max(count, DefaultCount) : DefaultCount;

        /// <summary>
        /// 检查坩埚号是否超出所选输出源的坩埚数量。只在坩埚数量已知时检查;0 (任意) 和非数字的值不检查。
        /// </summary>
        /// <param name="crucibleId">配置项 ID;不是坩埚号项时不检查。</param>
        /// <param name="value">要写入的坩埚号。</param>
        /// <param name="valueOf">按 ID 取写入时的值 (输出源 <c>F**0901</c>、坩埚数量 <c>S**07</c>);没有该项时返回 null。</param>
        /// <returns>超出时的原因,如"坩埚 7 超出输出源 S01 的坩埚数量 (6)";可以或无法判断时为 null。</returns>
        public static string? Check(string crucibleId, string? value, Func<string, string?> valueOf)
        {
            if (!IsCrucible(crucibleId) || !DeviceIds.TryParseCount(value, out var number)
                || !TryGetCount(crucibleId, valueOf, out var source, out var count))
            {
                return null;
            }

            return number <= Math.Max(count, DefaultCount) ? null : Texts.CrucibleExceedsCount(number, source, count);
        }

        /// <summary>膜系所选的输出源 (如 <c>S01</c>) 及其坩埚数量;输出源未选择 (0)、不是编号,或数量查不到时返回 false。</summary>
        private static bool TryGetCount(string crucibleId, Func<string, string?> valueOf, out string source, out int count)
        {
            if (valueOf is null)
            {
                throw new ArgumentNullException(nameof(valueOf));
            }

            source = string.Empty;
            count = 0;
            var film = crucibleId.Substring(0, crucibleId.Length - CrucibleSuffix.Length);
            if (!DeviceIds.TryParseCount(valueOf(film + SourceSuffix), out var number)
                || number < CommandDefinition.MinIndex || number > CommandDefinition.MaxIndex)
            {
                return false;
            }

            source = DeviceIds.Item(SourcePrefix, number);
            return DeviceIds.TryParseCount(valueOf(source + CountSuffix), out count);
        }
    }
}
