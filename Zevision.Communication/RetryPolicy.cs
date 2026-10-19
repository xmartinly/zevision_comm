namespace Zevision.Communication
{
    /// <summary>
    /// 自动重试的规则 (<see cref="ConnectionOptions.RetryCount"/>):只重试传输出错,且只重试重复执行结果不变的命令。
    /// </summary>
    internal static class RetryPolicy
    {
        /// <summary>
        /// 重复执行结果不变的动作:Q 查询、S 状态、U 写入同一个值、H 问候、E 回显、<c>?</c> 帮助。
        /// A/D (追加、删除列表项) 和 R (执行动作) 执行两次结果就不同;超时并不说明设备没有执行,所以不重试。
        /// </summary>
        private const string RepeatableActions = "QSUHE?";

        /// <summary>帧中每条命令 (<c>;</c> 分隔) 都可以重复执行。</summary>
        public static bool IsRepeatable(string command)
        {
            foreach (var segment in command.Split(';'))
            {
                var text = segment.Trim();
                if (text.Length > 0 && RepeatableActions.IndexOf(text[0]) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 是否是传输出错 (重发可能成功):超时、帧无效、无法解析,或设备回答 C (校验和)、L (长度)、T (超时) 错误。
        /// 设备拒绝命令本身 (A 非法命令、B 非法参数、D 格式、F 状态) 和连接问题不算。
        /// </summary>
        /// <param name="result">一次发送的结果。</param>
        /// <param name="deviceError">设备回答的传输错误代码;不是设备回答的错误时为 <see cref="ZevisionErrorCode.None"/>。</param>
        public static bool IsTransmissionFailure(ZevisionResult<ZevisionReply> result, out ZevisionErrorCode deviceError)
        {
            deviceError = ZevisionErrorCode.None;
            if (!result.IsSuccess)
            {
                var kind = result.Error!.Kind;
                return kind == ZevisionErrorKind.Timeout || kind == ZevisionErrorKind.InvalidFrame || kind == ZevisionErrorKind.UnparseableResponse;
            }

            foreach (var response in result.Value.Responses)
            {
                if (response.ErrorCode == ZevisionErrorCode.ChecksumError
                    || response.ErrorCode == ZevisionErrorCode.LengthError
                    || response.ErrorCode == ZevisionErrorCode.Timeout)
                {
                    deviceError = response.ErrorCode;
                    return true;
                }
            }

            return false;
        }
    }
}
