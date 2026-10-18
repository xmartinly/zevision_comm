using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Zevision.Communication;

namespace Zevision.ViewModels.Display
{
    /// <summary>
    /// 把帧字节格式化为便于阅读的文本,用于数据显示区。
    /// 可打印 ASCII 原样显示,其余字节显示为 <c>&lt;XX&gt;</c>。
    /// </summary>
    public static class FrameDisplayFormatter
    {
        /// <summary>
        /// 单行显示整帧。
        /// </summary>
        public static string ToDisplayString(byte[] bytes)
        {
            if (bytes is null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
            {
                AppendByte(sb, b);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 把帧拆成多行显示文本。
        /// <list type="bullet">
        /// <item>命令数据区内的 CR、LF、CRLF 作为换行,合法的 UTF-8 多字节序列解码显示 (如 <c>Å</c>)。</item>
        /// <item>数据区之外的帧结构字节 (长度、校验和、包分隔符) 一律按字节显示,避免误换行或误解码。</item>
        /// <item>超过 <paramref name="maxLineLength"/> 的行会被折成多行,以便列表虚拟化。</item>
        /// </list>
        /// </summary>
        /// <param name="raw">完整帧字节。</param>
        /// <param name="dataRanges">命令数据在 <paramref name="raw"/> 中的区间 (多包消息有多个),按位置升序。</param>
        /// <param name="maxLineLength">每行最大字符数。</param>
        public static IReadOnlyList<string> ToDisplayLines(byte[] raw, IReadOnlyList<ByteRange> dataRanges, int maxLineLength)
        {
            if (raw is null)
            {
                throw new ArgumentNullException(nameof(raw));
            }

            if (dataRanges is null)
            {
                throw new ArgumentNullException(nameof(dataRanges));
            }

            if (maxLineLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLineLength));
            }

            var lines = new List<string>();
            var sb = new StringBuilder();
            var rangeIndex = 0;
            for (var i = 0; i < raw.Length; i++)
            {
                while (rangeIndex < dataRanges.Count && i >= dataRanges[rangeIndex].Offset + dataRanges[rangeIndex].Length)
                {
                    rangeIndex++;
                }

                var dataEnd = rangeIndex < dataRanges.Count && i >= dataRanges[rangeIndex].Offset
                    ? dataRanges[rangeIndex].Offset + dataRanges[rangeIndex].Length
                    : -1;

                var b = raw[i];
                if (dataEnd > i && (b == '\r' || b == '\n'))
                {
                    lines.Add(sb.ToString());
                    sb.Clear();

                    // CRLF 只算一次换行。
                    if (b == '\r' && i + 1 < dataEnd && raw[i + 1] == '\n')
                    {
                        i++;
                    }

                    continue;
                }

                var utf8Length = dataEnd > i ? GetUtf8SequenceLength(raw, i, dataEnd) : 0;
                if (utf8Length > 1)
                {
                    sb.Append(Encoding.UTF8.GetString(raw, i, utf8Length));
                    i += utf8Length - 1;
                }
                else
                {
                    AppendByte(sb, b);
                }

                if (sb.Length >= maxLineLength)
                {
                    lines.Add(sb.ToString());
                    sb.Clear();
                }
            }

            if (sb.Length > 0 || lines.Count == 0)
            {
                lines.Add(sb.ToString());
            }

            return lines;
        }

        /// <summary>HEX 格式每行的字节数。</summary>
        public const int HexBytesPerLine = 32;

        /// <summary>Binary 格式每行的字节数。</summary>
        public const int BinaryBytesPerLine = 16;

        /// <summary>每个字节值的 HEX 文本 (如 <c>3F</c>),预先生成:逐字节格式化时,一个 ? 响应 (116 KB) 要分配十几万个临时字符串。</summary>
        private static readonly string[] HexTexts = Enumerable.Range(0, 256).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)).ToArray();

        /// <summary>每个字节值的 Binary 文本 (如 <c>00111111</c>)。</summary>
        private static readonly string[] BinaryTexts = Enumerable.Range(0, 256).Select(b => Convert.ToString(b, 2).PadLeft(8, '0')).ToArray();

        /// <summary>
        /// 按 <paramref name="format"/> 把帧拆成多行显示文本。<see cref="DisplayFormat.Ascii"/> 与
        /// <see cref="ToDisplayLines(byte[], IReadOnlyList{ByteRange}, int)"/> 相同;
        /// HEX 每行 <see cref="HexBytesPerLine"/> 字节,Binary 每行 <see cref="BinaryBytesPerLine"/> 字节,整帧 (含帧结构字节) 逐字节显示。
        /// </summary>
        public static IReadOnlyList<string> ToDisplayLines(byte[] raw, IReadOnlyList<ByteRange> dataRanges, int maxLineLength, DisplayFormat format)
        {
            switch (format)
            {
                case DisplayFormat.Hex:
                    return ToByteLines(raw, HexBytesPerLine, HexTexts);
                case DisplayFormat.Binary:
                    return ToByteLines(raw, BinaryBytesPerLine, BinaryTexts);
                default:
                    return ToDisplayLines(raw, dataRanges, maxLineLength);
            }
        }

        private static IReadOnlyList<string> ToByteLines(byte[] raw, int bytesPerLine, string[] byteTexts)
        {
            if (raw is null)
            {
                throw new ArgumentNullException(nameof(raw));
            }

            var lines = new List<string>((raw.Length / bytesPerLine) + 1);
            for (var start = 0; start < raw.Length; start += bytesPerLine)
            {
                var count = Math.Min(bytesPerLine, raw.Length - start);
                var parts = new string[count];
                for (var i = 0; i < count; i++)
                {
                    parts[i] = byteTexts[raw[start + i]];
                }

                lines.Add(string.Join(" ", parts));
            }

            if (lines.Count == 0)
            {
                lines.Add(string.Empty);
            }

            return lines;
        }

        /// <summary>
        /// 若 <paramref name="index"/> 处是一个完整、结构合法的 UTF-8 多字节序列,返回其长度 (2~4),否则返回 0。
        /// </summary>
        private static int GetUtf8SequenceLength(byte[] bytes, int index, int end)
        {
            var lead = bytes[index];
            int length;
            if (lead >= 0xC2 && lead <= 0xDF)
            {
                length = 2;
            }
            else if (lead >= 0xE0 && lead <= 0xEF)
            {
                length = 3;
            }
            else if (lead >= 0xF0 && lead <= 0xF4)
            {
                length = 4;
            }
            else
            {
                return 0;
            }

            if (index + length > end)
            {
                return 0;
            }

            for (var i = 1; i < length; i++)
            {
                if ((bytes[index + i] & 0xC0) != 0x80)
                {
                    return 0;
                }
            }

            return length;
        }

        private static void AppendByte(StringBuilder sb, byte b)
        {
            if (b >= 0x20 && b <= 0x7E)
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('<').Append(HexTexts[b]).Append('>');
            }
        }
    }
}
