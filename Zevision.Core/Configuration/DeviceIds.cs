using System;
using System.Globalization;
using Zevision.Commands;

namespace Zevision.Core.Configuration
{
    /// <summary>
    /// 配置项 ID 的组成:组字母 + 若干级两位十六进制编号 (如 <c>F0103</c> = 膜系 F01 的预熔列表)。
    /// 列表条目编号为 01~FD;<c>00</c> 是"在末尾追加/删除最后一项"的指令项,<c>FE</c> 是条数项。
    /// 程序中拼接、拆分 ID 都用这里的方法,不手写这些编号。
    /// </summary>
    public static class DeviceIds
    {
        private const string AppendSlot = "00";
        private const string Count = "FE";

        /// <summary>两位十六进制编号,如 10 → <c>0A</c>。</summary>
        private static string Number(int number) => number.ToString("X2", CultureInfo.InvariantCulture);

        /// <summary>ID 末尾的两位十六进制编号,如 <c>M0A</c> → 10。</summary>
        public static int LastNumber(string id) =>
            int.Parse((id ?? throw new ArgumentNullException(nameof(id))).Substring(id.Length - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        /// <summary>列表的第 <paramref name="number"/> 项,如 <c>F0103</c> 的第 2 项为 <c>F010302</c>。</summary>
        public static string Item(string listId, int number) => listId + Number(number);

        /// <summary>列表的条数项,如 <c>F0103FE</c>;组的对象数如 <c>MFE</c>。</summary>
        public static string CountOf(string listId) => listId + Count;

        /// <summary>列表的追加/删除项,如 <c>F010300</c> (<c>A</c> 在末尾追加,<c>D</c> 删除最后一项)。</summary>
        public static string AppendSlotOf(string listId) => listId + AppendSlot;

        /// <summary>对象的名称项 (第 01 项),如材料 <c>M0301</c>。</summary>
        public static string NameOf(string objectId) => objectId + "01";

        /// <summary>两位编号是否是列表条目,而不是 <c>00</c> (追加/删除) 或 <c>FE</c> (条数)。</summary>
        public static bool IsItemNumber(string number) => number != AppendSlot && number != Count;

        /// <summary>是否是条数项 (以 <c>FE</c> 结尾)。</summary>
        public static bool IsCount(string id) => (id ?? throw new ArgumentNullException(nameof(id))).EndsWith(Count, StringComparison.Ordinal);

        /// <summary>解析设备返回的条数 (十进制非负整数)。</summary>
        public static bool TryParseCount(string? text, out int count) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out count);

        /// <summary>ID 格式是否正确:大写组字母后跟若干两位十六进制 (与指令集生成命令时检查的格式相同)。</summary>
        public static bool IsWellFormed(string? id) => ZevisionCommandSet.IsWellFormedId(id);
    }
}
