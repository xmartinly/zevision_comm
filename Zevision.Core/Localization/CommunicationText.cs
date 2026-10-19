using System;
using Zevision.Communication;
using Zevision.Core.Devices;

namespace Zevision.Core.Localization
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
                    return Texts.AlreadyConnected;
                case ZevisionErrorKind.ConnectFailed:
                    var endpoint = error.Endpoint?.ToString() ?? string.Empty;
                    var detail = error.Exception is TimeoutException && error.Endpoint is TcpSettings
                        ? Texts.ConnectTimeout(TcpTransport.ConnectTimeout.TotalSeconds)
                        : error.Exception?.Message ?? error.Message;
                    return Texts.CannotConnect(endpoint, detail);
                case ZevisionErrorKind.NotConnected:
                    return Texts.NotConnected;
                case ZevisionErrorKind.InvalidCommand:
                    return DescribeInvalidCommand(command);
                case ZevisionErrorKind.SendFailed:
                    var reason = error.Exception?.Message ?? error.Message;
                    return Texts.SendFailed(reason);
                case ZevisionErrorKind.Timeout:
                    var partial = error.ReceivedIncompleteData ? Texts.PartialDataShown : string.Empty;
                    return Texts.ResponseTimeout(error.Timeout.TotalMilliseconds, partial);
                case ZevisionErrorKind.Disconnected:
                    return Texts.Disconnected;
                case ZevisionErrorKind.InvalidFrame:
                    return Texts.InvalidFrame + error.FrameError.Localized();
                case ZevisionErrorKind.UnparseableResponse:
                    return Texts.UnparseableResponse(error.DataLength);
                default:
                    return error.Message;
            }
        }

        /// <summary>
        /// 帧格式不一致的提示 (<see cref="FramingCheck"/>),附在通讯错误之后,如"设备的响应带长度字节,而程序未启用长度…"。
        /// </summary>
        /// <param name="mismatch">不一致的情况;<see cref="FramingMismatch.None"/> 时返回 null。</param>
        /// <param name="endpoint">连接目标,决定设置项的 ID (RS232:G1304/G1305,TCP:G1407/G1408)。</param>
        public static string? FramingHint(FramingMismatch mismatch, ConnectionEndpoint endpoint)
        {
            var (checksum, length) = FrameSettings.Ids(endpoint ?? throw new ArgumentNullException(nameof(endpoint)));
            switch (mismatch)
            {
                case FramingMismatch.ReplyHasLength:
                    return Texts.FramingReplyHasLength(length, checksum);
                case FramingMismatch.ReplyWithoutLength:
                    return Texts.FramingReplyWithoutLength(length, checksum);
                default:
                    return null;
            }
        }

        /// <summary>
        /// 重发的说明,如"第 1/2 次重发 QG01: 等待响应超时 …"。
        /// </summary>
        /// <param name="e">重发事件的参数。</param>
        public static string DescribeRetry(RetryEventArgs e)
        {
            if (e is null)
            {
                throw new ArgumentNullException(nameof(e));
            }

            var reason = e.Error != null
                ? Describe(e.Error, e.Command)
                : Texts.DeviceAnswered(DeviceErrorLetter(e.DeviceError), e.DeviceError.Localized());
            return Texts.Retrying(e.Attempt, e.MaxRetries, e.Command, reason);
        }

        /// <summary>
        /// 连接中断的原因:心跳无应答时为"设备连续 n 次没有应答心跳",否则为异常的说明 (如系统给出的串口错误)。
        /// </summary>
        /// <param name="e">连接中断事件的参数。</param>
        /// <param name="heartbeatMissLimit">判定中断的心跳次数 (<see cref="ConnectionOptions.HeartbeatMissLimit"/>)。</param>
        public static string DescribeConnectionLost(ConnectionLostEventArgs e, int heartbeatMissLimit)
        {
            if (e is null)
            {
                throw new ArgumentNullException(nameof(e));
            }

            return e.Reason == ConnectionLostReason.HeartbeatTimeout
                ? Texts.HeartbeatLost(heartbeatMissLimit)
                : e.Exception.Message;
        }

        /// <summary>设备错误代码的说明,如 <c>B</c> → 非法参数值。</summary>
        public static string Localized(this ZevisionErrorCode code)
        {
            switch (code)
            {
                case ZevisionErrorCode.None: return Texts.ErrorCodeNone;
                case ZevisionErrorCode.IllegalCommand: return Texts.ErrorCodeIllegalCommand;
                case ZevisionErrorCode.IllegalArgument: return Texts.ErrorCodeIllegalArgument;
                case ZevisionErrorCode.ChecksumError: return Texts.ErrorCodeChecksum;
                case ZevisionErrorCode.IllegalFormat: return Texts.ErrorCodeIllegalFormat;
                case ZevisionErrorCode.StateError: return Texts.ErrorCodeState;
                case ZevisionErrorCode.LengthError: return Texts.ErrorCodeLength;
                case ZevisionErrorCode.Timeout: return Texts.ErrorCodeTimeout;
                default: return Texts.ErrorCodeUnknown;
            }
        }

        /// <summary>帧解码错误的说明,如 <see cref="FrameError.ChecksumMismatch"/> → 校验和不匹配。</summary>
        public static string Localized(this FrameError error)
        {
            switch (error)
            {
                case FrameError.ChecksumMismatch: return Texts.FrameChecksumMismatch;
                case FrameError.InvalidLength: return Texts.FrameInvalidLength;
                case FrameError.MissingStopByte: return Texts.FrameMissingStopByte;
                case FrameError.Truncated: return Texts.FrameTruncated;
                case FrameError.UnexpectedData: return Texts.FrameUnexpectedData;
                case FrameError.TooShort: return Texts.FrameTooShort;
                case FrameError.MissingContinuation: return Texts.FrameMissingContinuation;
                case FrameError.Incomplete: return Texts.FrameIncomplete;
                default: return Texts.FrameError;
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
                    return Texts.CommandEmpty;
                case ProtocolTextError.NotPrintableAscii:
                    return Texts.CommandNotAscii((int)c);
                default:
                    return Texts.CommandHasDelimiters;
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
                    return Texts.ValueEmpty;
                case ProtocolTextError.NotPrintableAscii:
                    return Texts.ValueNotAscii;
                default:
                    return Texts.ValueHasDelimiters;
            }
        }

        /// <summary>重发原因中的设备错误代码字母 (重发只针对 C、L、T)。</summary>
        private static string DeviceErrorLetter(ZevisionErrorCode code)
        {
            switch (code)
            {
                case ZevisionErrorCode.ChecksumError: return "C";
                case ZevisionErrorCode.LengthError: return "L";
                case ZevisionErrorCode.Timeout: return "T";
                default: return string.Empty;
            }
        }

        private static string DescribeInvalidCommand(string? command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return Texts.CommandEmpty;
            }

            return ValidateCommand(command)
                ?? Texts.CommandTooLong(ZevisionFrameCodec.MaxDataLength);
        }
    }
}
