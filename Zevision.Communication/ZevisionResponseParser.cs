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

            var responses = new List<ZevisionResponse>(segments.Count);
            foreach (var segment in segments)
            {
                responses.Add(ParseSingle(segment));
            }

            return responses;
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

            var first = command.Split(';')[0].Trim();
            var separator = first.IndexOf(':');
            return separator >= 0 ? first.Substring(0, separator) : first;
        }

        /// <summary>
        /// 这一帧是否是对回显为 <paramref name="expectedEcho"/> 的请求的响应。
        /// 无法判断时 (帧无效、无法解析、设备未回显命令,如 <c>-C</c>) 视为是,交给调用方报告。
        /// </summary>
        public static bool IsReplyTo(ZevisionFrame frame, string expectedEcho)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (!frame.IsValid || !TryParse(frame.Payload, out var responses) || responses.Count == 0)
            {
                return true;
            }

            var echo = responses[0].Command;
            return echo.Length == 0 || string.Equals(echo, expectedEcho, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 尝试解析;格式错误时返回 false。
        /// </summary>
        public static bool TryParse(string payload, out IReadOnlyList<ZevisionResponse> responses)
        {
            try
            {
                responses = Parse(payload);
                return true;
            }
            catch (FormatException)
            {
                responses = Array.Empty<ZevisionResponse>();
                return false;
            }
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

        private static ZevisionResponse ParseSingle(string text)
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

            throw new FormatException($"Response is missing the status + or -: \"{text}\"");
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
