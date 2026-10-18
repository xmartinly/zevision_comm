using System;
using System.Collections.Generic;

namespace Zevision.Communication
{
    /// <summary>
    /// 解析响应帧的命令数据。
    /// </summary>
    /// <remarks>
    /// 多条响应以 <c>;</c> 分隔。协议没有为数据中的 <c>;</c> 定义转义,
    /// 因此不像响应头 (命令 + 状态符,且不含空格) 的片段会并回上一条响应的数据。
    /// 若数据中 <c>;</c> 之后恰好是形如 <c>XX+</c> 的文本,仍会被误拆。
    /// </remarks>
    public static class ZevisionResponseParser
    {
        /// <summary>
        /// 将响应帧的命令数据解析为单条响应列表。
        /// </summary>
        /// <param name="payload">帧的命令数据,如 <c>H01+:IMC300 1.0;QG99-A</c>。</param>
        /// <exception cref="FormatException">某条响应没有 <c>+</c> 或 <c>-</c> 状态符。</exception>
        public static IReadOnlyList<ZevisionResponse> Parse(string payload)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return TryParseCore(payload, out var responses, out var invalid)
                ? responses
                : throw new FormatException($"Response is missing the status + or -: \"{invalid}\"");
        }

        /// <summary>
        /// 尝试解析;格式错误时返回 false (不抛出异常)。
        /// </summary>
        public static bool TryParse(string payload, out IReadOnlyList<ZevisionResponse> responses)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (TryParseCore(payload, out var parsed, out _))
            {
                responses = parsed;
                return true;
            }

            responses = Array.Empty<ZevisionResponse>();
            return false;
        }

        private static bool TryParseCore(string payload, out IReadOnlyList<ZevisionResponse> responses, out string? invalid)
        {
            // 协议没有转义 ';'。没有状态符的片段视为上一条响应数据的一部分 (例如帮助文本中的 ';'),重新拼回去。
            var segments = new List<string>();
            foreach (var part in payload.Split(';'))
            {
                if (part.Length == 0)
                {
                    // 末尾的 ';' (协议允许) 或连续的 ';'。
                    continue;
                }

                if (segments.Count > 0 && !LooksLikeResponse(part))
                {
                    segments[segments.Count - 1] += ";" + part;
                }
                else
                {
                    segments.Add(part);
                }
            }

            var list = new List<ZevisionResponse>(segments.Count);
            foreach (var segment in segments)
            {
                var response = ParseSingle(segment);
                if (response == null)
                {
                    responses = Array.Empty<ZevisionResponse>();
                    invalid = segment;
                    return false;
                }

                list.Add(response);
            }

            responses = list;
            invalid = null;
            return true;
        }

        /// <summary>
        /// 设备对一条指令的回显:第一条命令 (<c>;</c> 之前) 中数据分隔符 <c>:</c> 之前的部分,
        /// 如 <c>UG1304:1;H01</c> → <c>UG1304</c>。
        /// </summary>
        public static string EchoOf(string command)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var end = command.IndexOf(';');
            var first = (end >= 0 ? command.Substring(0, end) : command).Trim();
            var separator = first.IndexOf(':');
            return separator >= 0 ? first.Substring(0, separator) : first;
        }

        /// <summary>
        /// 这一帧是否是对回显为 <paramref name="expectedEcho"/> 的请求的响应。
        /// 无法判断时 (帧无效、无法解析、设备未回显命令,如 <c>-C</c>) 视为是,交给调用方报告。
        /// </summary>
        public static bool IsReplyTo(ZevisionFrame frame, string expectedEcho) =>
            IsReplyTo(frame ?? throw new ArgumentNullException(nameof(frame)), expectedEcho, out _);

        /// <summary>
        /// 同 <see cref="IsReplyTo(ZevisionFrame, string)"/>,并给出解析结果 (帧无效或无法解析时为 null),
        /// 让客户端不必再解析一次 (<c>?</c> 的响应约 116 KB)。
        /// </summary>
        internal static bool IsReplyTo(ZevisionFrame frame, string expectedEcho, out IReadOnlyList<ZevisionResponse>? responses)
        {
            responses = null;
            if (!frame.IsValid || !TryParseCore(frame.Payload, out var parsed, out _))
            {
                return true;
            }

            responses = parsed;
            if (parsed.Count == 0)
            {
                return true;
            }

            var echo = parsed[0].Command;
            return echo.Length == 0 || string.Equals(echo, expectedEcho, StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeResponse(string text)
        {
            var separator = text.IndexOf(':');
            var head = separator >= 0 ? text.Substring(0, separator) : text;
            if (head.Length < 2 || head.IndexOf(' ') >= 0)
            {
                return false;
            }

            var last = head[head.Length - 1];
            return last == '+' || last == '-' || head[head.Length - 2] == '-';
        }

        /// <returns>一条响应;没有状态符 <c>+</c>/<c>-</c> 时为 null。</returns>
        private static ZevisionResponse? ParseSingle(string text)
        {
            var separator = text.IndexOf(':');
            var head = separator >= 0 ? text.Substring(0, separator) : text;
            var data = separator >= 0 ? text.Substring(separator + 1) : null;

            if (head.Length >= 2 && head[head.Length - 1] == '+')
            {
                return new ZevisionResponse(head.Substring(0, head.Length - 1), true, ZevisionErrorCode.None, null, data);
            }

            if (head.Length >= 2 && head[head.Length - 1] == '-')
            {
                return new ZevisionResponse(head.Substring(0, head.Length - 1), false, ZevisionErrorCode.Unknown, null, data);
            }

            // 设备无法解析帧时 (如校验和错误) 不回显命令,只回 "-C":命令部分为空。
            if (head.Length >= 2 && head[head.Length - 2] == '-')
            {
                var code = head[head.Length - 1];
                return new ZevisionResponse(head.Substring(0, head.Length - 2), false, MapErrorCode(code), code, data);
            }

            return null;
        }

        private static ZevisionErrorCode MapErrorCode(char code)
        {
            switch (code)
            {
                case 'A': return ZevisionErrorCode.IllegalCommand;
                case 'B': return ZevisionErrorCode.IllegalArgument;
                case 'C': return ZevisionErrorCode.ChecksumError;
                case 'D': return ZevisionErrorCode.IllegalFormat;
                case 'F': return ZevisionErrorCode.StateError;
                case 'L': return ZevisionErrorCode.LengthError;
                case 'T': return ZevisionErrorCode.Timeout;
                default: return ZevisionErrorCode.Unknown;
            }
        }
    }
}
