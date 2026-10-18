using System;
using System.Collections.Generic;
using System.Text;
using Zevision.Communication;
using Zevision.Core.Localization;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 把收发的帧转换为数据显示区的记录。纯函数,可以在后台线程调用。
    /// </summary>
    public static class FrameLogFormatter
    {
        /// <summary>单行最大字符数,超出时折行,保证列表虚拟化有效。</summary>
        public const int MaxLineLength = 160;

        /// <summary>发送的帧:一行 TX 记录 (ASCII 格式)。</summary>
        public static LogEntry FromSent(byte[] raw, DateTime timestamp) =>
            new LogEntry(timestamp, LogKind.Tx, FrameDisplayFormatter.ToDisplayString(raw));

        /// <summary>
        /// 发送的帧:ASCII 格式为一行 TX 记录 (与 <see cref="FromSent(byte[], DateTime)"/> 相同);
        /// HEX、Binary 按每行固定字节数拆成多行,第二行起为后续行。
        /// </summary>
        public static IReadOnlyList<LogEntry> FromSent(byte[] raw, DateTime timestamp, DisplayFormat format)
        {
            if (format == DisplayFormat.Ascii)
            {
                return new[] { FromSent(raw, timestamp) };
            }

            var lines = FrameDisplayFormatter.ToDisplayLines(raw, Array.Empty<ByteRange>(), MaxLineLength, format);
            var entries = new List<LogEntry>(lines.Count);
            for (var i = 0; i < lines.Count; i++)
            {
                entries.Add(new LogEntry(timestamp, LogKind.Tx, lines[i], isContinuation: i > 0));
            }

            return entries;
        }

        /// <summary>
        /// 接收的帧:第一行带"(未请求)"或错误说明,数据中的换行拆成后续行;
        /// 多行、分包或出错时附一行统计 (字节数、行数、包数、长度字段与实收数据)。
        /// </summary>
        /// <param name="frame">收到的帧。</param>
        /// <param name="isSolicited">是否是当前请求的响应。</param>
        /// <param name="timestamp">时间。</param>
        /// <param name="format">显示格式;默认 ASCII。</param>
        public static IReadOnlyList<LogEntry> FromReceived(ZevisionFrame frame, bool isSolicited, DateTime timestamp, DisplayFormat format = DisplayFormat.Ascii)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            var lines = FrameDisplayFormatter.ToDisplayLines(frame.Raw, frame.DataRanges, MaxLineLength, format);
            var header = frame.IsValid
                ? (isSolicited ? string.Empty : Lang.T("(未请求) ", "(unsolicited) "))
                : frame.Error.Localized() + ": ";
            var kind = frame.IsValid ? LogKind.Rx : LogKind.Error;

            var entries = new List<LogEntry>(lines.Count + 1) { new LogEntry(timestamp, kind, header + lines[0]) };
            for (var i = 1; i < lines.Count; i++)
            {
                entries.Add(new LogEntry(timestamp, kind, lines[i], isContinuation: true));
            }

            if (lines.Count > 1 || frame.PacketCount > 1 || !frame.IsValid)
            {
                entries.Add(new LogEntry(timestamp, LogKind.Info, BuildFooter(frame, lines.Count), isContinuation: true));
            }

            return entries;
        }

        private static string BuildFooter(ZevisionFrame frame, int lineCount)
        {
            var sb = new StringBuilder();
            sb.Append(Lang.T("[共 ", "[")).Append(frame.Raw.Length).Append(Lang.T(" 字节, ", " bytes, ")).Append(lineCount).Append(Lang.T(" 行", " lines"));
            if (frame.PacketCount > 1)
            {
                sb.Append(", ").Append(frame.PacketCount).Append(Lang.T(" 包", " packets"));
            }

            // 出错或分包时列出每个包的长度字段和实际收到的数据长度,用于诊断分包格式。
            if (frame.DeclaredLengths.Count > 0 && (frame.PacketCount > 1 || !frame.IsValid))
            {
                sb.Append(Lang.T("; 长度字段 ", "; length fields ")).Append(string.Join("/", frame.DeclaredLengths));
                sb.Append(Lang.T("; 实收数据 ", "; data received "));
                for (var i = 0; i < frame.DataRanges.Count; i++)
                {
                    sb.Append(i == 0 ? string.Empty : "/").Append(frame.DataRanges[i].Length);
                }
            }

            return sb.Append(']').ToString();
        }
    }
}
