using System;
using System.Collections.Generic;
using Zevision.Communication;
using Zevision.Core.Localization;

namespace Zevision.ViewModels.Display
{
    /// <summary>
    /// 把手动发送的指令的响应解析结果转换为数据显示区的记录。纯函数。
    /// </summary>
    public static class ResponseLogFormatter
    {
        /// <summary>
        /// 单条指令只显示错误 (原始响应已在 RX 行中);
        /// 多条指令逐条显示"指令 = 数据"、"指令 ✓"或错误,并核对响应条数与回显。
        /// </summary>
        /// <param name="segments">发送的各条指令。</param>
        /// <param name="responses">解析出的响应。</param>
        /// <param name="timestamp">记录时间。</param>
        public static IReadOnlyList<LogEntry> Format(IReadOnlyList<string> segments, IReadOnlyList<ZevisionResponse> responses, DateTime timestamp)
        {
            if (segments is null)
            {
                throw new ArgumentNullException(nameof(segments));
            }

            if (responses is null)
            {
                throw new ArgumentNullException(nameof(responses));
            }

            var entries = new List<LogEntry>();
            var multiple = segments.Count > 1;
            if (multiple)
            {
                entries.Add(new LogEntry(timestamp, LogKind.Info, Lang.T($"解析 {segments.Count} 条指令的响应:", $"Responses to {segments.Count} commands:")));
            }

            for (var i = 0; i < responses.Count; i++)
            {
                var response = responses[i];
                var expected = i < segments.Count ? ZevisionResponseParser.EchoOf(segments[i]) : null;
                var label = response.Command.Length > 0 ? response.Command : expected ?? Lang.T("(无回显)", "(no echo)");
                var echoNote = expected != null && response.Command.Length > 0 && response.Command != expected
                    ? Lang.T($" (发送的是 {expected})", $" (sent {expected})")
                    : string.Empty;

                if (!response.IsSuccess)
                {
                    var code = response.RawErrorCode.HasValue ? response.RawErrorCode.Value + " " : string.Empty;
                    entries.Add(new LogEntry(timestamp, LogKind.Error, $"{label}: {code}{response.ErrorCode.Localized()}{echoNote}"));
                }
                else if (multiple)
                {
                    var text = response.Data != null ? $"{label} = {response.Data}" : $"{label} ✓";
                    entries.Add(new LogEntry(timestamp, LogKind.Info, text + echoNote, isContinuation: true));
                }
            }

            if (multiple && responses.Count != segments.Count)
            {
                entries.Add(new LogEntry(timestamp, LogKind.Error, Lang.T($"发送了 {segments.Count} 条指令,但收到 {responses.Count} 条响应。", $"Sent {segments.Count} commands but received {responses.Count} responses.")));
            }

            return entries;
        }
    }
}
