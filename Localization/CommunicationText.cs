using System;
using Zevision.Communication;

namespace Zevision.Localization
{
    /// <summary>
    /// 通讯库 (Zevision.Communication) 结果的界面文本。通讯库只给出英文说明和错误类型,
    /// 这里按 <see cref="Lang.Current"/> 生成中英文提示,内容与拆分前的程序一致。
    /// </summary>
    public static class CommunicationText
    {
        /// <summary>
        /// <see cref="ZevisionClient"/> 操作失败的说明。
        /// </summary>
        /// <param name="error">失败原因。</param>
        /// <param name="command">发送的命令 (用于说明命令为什么不合法);连接失败时为 null。</param>
        public static string Describe(ZevisionError error, string? command = null)
        {
            if (error is null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            switch (error.Kind)
            {
                case ZevisionErrorKind.AlreadyConnected:
                    return Lang.T("已经连接。", "Already connected.");
                case ZevisionErrorKind.ConnectFailed:
                    var endpoint = error.Endpoint?.ToString() ?? string.Empty;
                    var detail = error.Exception is TimeoutException && error.Endpoint is TcpSettings
                        ? Lang.T($"连接超时 ({TcpTransport.ConnectTimeout.TotalSeconds:0} 秒内无响应)。请检查 IP 地址、端口和网络。", $"Connection timed out (no response within {TcpTransport.ConnectTimeout.TotalSeconds:0} s). Check the IP address and the network.")
                        : error.Exception?.Message ?? error.Message;
                    return Lang.T($"无法连接 {endpoint}: {detail}", $"Cannot connect to {endpoint}: {detail}");
                case ZevisionErrorKind.NotConnected:
                    return Lang.T("未连接。", "Not connected.");
                case ZevisionErrorKind.InvalidCommand:
                    return DescribeInvalidCommand(command);
                case ZevisionErrorKind.SendFailed:
                    var reason = error.Exception?.Message ?? error.Message;
                    return Lang.T($"发送失败: {reason}", $"Sending failed: {reason}");
                case ZevisionErrorKind.Timeout:
                    var partial = error.ReceivedIncompleteData ? Lang.T(",已收到的不完整数据见上方", "; the incomplete data received is shown above") : string.Empty;
                    return Lang.T($"等待响应超时 ({error.Timeout.TotalMilliseconds:0} ms 内未收到数据{partial})。", $"Timed out waiting for a response (no data within {error.Timeout.TotalMilliseconds:0} ms{partial}).");
                case ZevisionErrorKind.Disconnected:
                    return Lang.T("连接已断开。", "Disconnected.");
                case ZevisionErrorKind.InvalidFrame:
                    return Lang.T("响应帧无效: ", "Invalid response frame: ") + error.FrameError.Localized();
                case ZevisionErrorKind.UnparseableResponse:
                    return Lang.T($"无法解析响应 ({error.DataLength} 字节)。", $"Cannot parse the response ({error.DataLength} bytes).");
                default:
                    return error.Message;
            }
        }

        /// <summary>设备错误代码的说明,如 <c>B</c> → 非法参数值。</summary>
        public static string Localized(this ZevisionErrorCode code)
        {
            switch (code)
            {
                case ZevisionErrorCode.None: return Lang.T("无错误", "No error");
                case ZevisionErrorCode.IllegalCommand: return Lang.T("非法命令,不支持该命令", "Illegal command, not supported");
                case ZevisionErrorCode.IllegalArgument: return Lang.T("非法参数值", "Illegal parameter value");
                case ZevisionErrorCode.ChecksumError: return Lang.T("校验和错误", "Checksum error");
                case ZevisionErrorCode.IllegalFormat: return Lang.T("非法格式", "Illegal format");
                case ZevisionErrorCode.StateError: return Lang.T("状态错误,系统处于错误状态", "State error, the system is in the wrong state");
                case ZevisionErrorCode.LengthError: return Lang.T("长度错误", "Length error");
                case ZevisionErrorCode.Timeout: return Lang.T("超时,未收到停止符", "Timeout, no stop character received");
                default: return Lang.T("未知错误", "Unknown error");
            }
        }

        /// <summary>帧解码错误的说明,如 <see cref="FrameError.ChecksumMismatch"/> → 校验和不匹配。</summary>
        public static string Localized(this FrameError error)
        {
            switch (error)
            {
                case FrameError.ChecksumMismatch: return Lang.T("校验和不匹配", "checksum mismatch");
                case FrameError.InvalidLength: return Lang.T("长度字节无效", "invalid length bytes");
                case FrameError.MissingStopByte: return Lang.T("缺少停止符", "missing stop character");
                case FrameError.Truncated: return Lang.T("帧不完整", "incomplete frame");
                case FrameError.UnexpectedData: return Lang.T("帧外数据", "data outside a frame");
                case FrameError.TooShort: return Lang.T("帧过短", "frame too short");
                case FrameError.MissingContinuation: return Lang.T("'>' 之后缺少续包 '<'", "missing continuation packet '<' after '>'");
                case FrameError.Incomplete: return Lang.T("消息未接收完整", "message not completely received");
                default: return Lang.T("帧错误", "frame error");
            }
        }

        /// <summary>
        /// 校验命令 (规则见 <see cref="ProtocolText.CheckCommand"/>)。
        /// </summary>
        /// <returns>错误说明;合法时为 null。</returns>
        public static string? ValidateCommand(string? command)
        {
            switch (ProtocolText.CheckCommand(command, out var c))
            {
                case ProtocolTextError.None:
                    return null;
                case ProtocolTextError.Empty:
                    return Lang.T("命令不能为空。", "The command must not be empty.");
                case ProtocolTextError.NotPrintableAscii:
                    return Lang.T($"命令只能包含可打印 ASCII 字符,发现 U+{(int)c:X4}。", $"Commands may only contain printable ASCII characters; found U+{(int)c:X4}.");
                default:
                    return Lang.T("命令中不能包含起止符 { 或 },它们由程序自动添加。", "Commands must not contain the delimiters { or }; they are added automatically.");
            }
        }

        /// <summary>
        /// 校验设置值 (规则见 <see cref="ProtocolText.CheckValue"/>)。
        /// </summary>
        /// <returns>错误说明;合法时为 null。</returns>
        public static string? ValidateValue(string? value)
        {
            switch (ProtocolText.CheckValue(value, out _))
            {
                case ProtocolTextError.None:
                    return null;
                case ProtocolTextError.Empty:
                    return Lang.T("值不能为空", "The value must not be empty");
                case ProtocolTextError.NotPrintableAscii:
                    return Lang.T("只能使用英文、数字和常用符号 (可打印 ASCII)", "Only letters, digits and common symbols (printable ASCII) are allowed");
                default:
                    return Lang.T("不能包含 ; { }", "Must not contain ; { }");
            }
        }

        private static string DescribeInvalidCommand(string? command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return Lang.T("命令不能为空。", "The command must not be empty.");
            }

            return ValidateCommand(command)
                ?? Lang.T($"命令长度超过 {ZevisionFrameCodec.MaxDataLength} 字节。", $"The command is longer than {ZevisionFrameCodec.MaxDataLength} bytes.");
        }
    }
}
