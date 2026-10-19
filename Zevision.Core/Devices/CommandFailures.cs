using System;
using System.Collections.Generic;
using System.Linq;
using Zevision.Communication;

namespace Zevision.Core.Devices
{
    /// <summary>
    /// <see cref="IDeviceService.CommandsFailed"/> 的参数:一次发送中没有得到成功响应的指令数。
    /// </summary>
    public sealed class CommandsFailedEventArgs : EventArgs
    {
        /// <summary>创建参数。</summary>
        public CommandsFailedEventArgs(string command, int count)
        {
            Command = command;
            Count = count;
        }

        /// <summary>发送的指令 (多条时用 <c>;</c> 分隔)。</summary>
        public string Command { get; }

        /// <summary>其中没有得到成功响应的指令数 (大于零)。</summary>
        public int Count { get; }
    }

    /// <summary>
    /// 一次发送的最终结果中,没有得到成功响应的指令数 (重发后成功的不计)。
    /// </summary>
    internal static class CommandFailures
    {
        /// <summary>
        /// 指令按 <c>;</c> 拆分,逐条按回显找成功的响应 (<c>+</c>);找不到的计为失败,含设备的否定响应
        /// 和不回显命令的 <c>-C</c>。发出后没有得到有效响应 (超时、写入失败、响应帧无效或无法解析) 时,帧中的指令全部计为失败。
        /// 没有发出 (未连接、指令不合法) 和等待时连接断开不计。
        /// </summary>
        /// <param name="command">发送的指令。</param>
        /// <param name="result">发送的结果。</param>
        public static int Count(string command, ZevisionResult<ZevisionReply> result)
        {
            var commands = command.Split(';').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
            if (!result.IsSuccess)
            {
                return IsUnanswered(result.Error!.Kind) ? commands.Count : 0;
            }

            var succeeded = new List<string>(result.Value.Responses.Where(r => r.IsSuccess).Select(r => r.Command));
            var failed = 0;
            foreach (var echo in commands.Select(ZevisionResponseParser.EchoOf))
            {
                var index = succeeded.FindIndex(e => string.Equals(e, echo, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    succeeded.RemoveAt(index);
                }
                else
                {
                    failed++;
                }
            }

            return failed;
        }

        private static bool IsUnanswered(ZevisionErrorKind kind) =>
            kind == ZevisionErrorKind.Timeout
            || kind == ZevisionErrorKind.SendFailed
            || kind == ZevisionErrorKind.InvalidFrame
            || kind == ZevisionErrorKind.UnparseableResponse;
    }
}
