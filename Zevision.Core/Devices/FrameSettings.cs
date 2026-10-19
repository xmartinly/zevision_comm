using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Zevision.Communication;

namespace Zevision.Core.Devices
{
    /// <summary>
    /// 设备上决定帧格式的设置项:RS232 为 G1304 (校验和)、G1305 (长度字节),TCP 为 G1407、G1408 (两个接口各自独立)。
    /// </summary>
    /// <remarks>
    /// 实测 (固件 1.8.11,2026-10-19):修改这些设置的 <c>U</c> 命令,其响应仍按旧格式发送,从下一帧起设备只认新格式。
    /// </remarks>
    public static class FrameSettings
    {
        /// <summary>设置项的 ID:TCP 为 G1407 (校验和)、G1408 (长度),RS232 为 G1304、G1305。</summary>
        /// <param name="endpoint">连接目标。</param>
        public static (string Checksum, string Length) Ids(ConnectionEndpoint endpoint) =>
            endpoint is TcpSettings ? ("G1407", "G1408") : ("G1304", "G1305");

        /// <summary>
        /// 一帧指令修改了当前接口的帧格式设置 (如 <c>UG1304:1</c>) 并被设备接受时,修改后的帧格式;没有改变时为 null。
        /// </summary>
        /// <param name="endpoint">连接目标,决定设置项的 ID。</param>
        /// <param name="current">当前的帧格式。</param>
        /// <param name="commands">帧中的各条指令 (命令部分已为大写)。</param>
        /// <param name="responses">设备的响应,按回显与指令对应。</param>
        public static FrameOptions? AfterUpdates(ConnectionEndpoint endpoint, FrameOptions current, IEnumerable<string> commands, IReadOnlyList<ZevisionResponse> responses)
        {
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (commands is null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            if (responses is null)
            {
                throw new ArgumentNullException(nameof(responses));
            }

            var (checksumId, lengthId) = Ids(endpoint);
            var checksum = current.ChecksumEnabled;
            var length = current.LengthEnabled;
            foreach (var command in commands)
            {
                var separator = command.IndexOf(':');
                if (separator < 2 || command[0] != 'U'
                    || !int.TryParse(command.Substring(separator + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    continue;
                }

                var id = command.Substring(1, separator - 1).Trim();
                var echo = ZevisionResponseParser.EchoOf(command);
                var accepted = responses.Any(r => r.IsSuccess && string.Equals(r.Command, echo, StringComparison.OrdinalIgnoreCase));
                if (!accepted)
                {
                    continue;
                }

                if (string.Equals(id, checksumId, StringComparison.OrdinalIgnoreCase))
                {
                    checksum = value != 0;
                }
                else if (string.Equals(id, lengthId, StringComparison.OrdinalIgnoreCase))
                {
                    length = value != 0;
                }
            }

            var next = new FrameOptions(checksum, length);
            return next.Equals(current) ? null : next;
        }
    }
}
