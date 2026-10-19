using System;
using System.Linq;
using Zevision.Communication;

namespace Zevision.Core.Devices
{
    /// <summary>收到的帧与程序的帧格式设置 (长度字节) 不一致的情况。</summary>
    public enum FramingMismatch
    {
        /// <summary>一致,或无法判断。</summary>
        None,

        /// <summary>程序未启用长度,设备的响应带长度字节。</summary>
        ReplyHasLength,

        /// <summary>程序启用了长度,设备的响应不带长度字节。</summary>
        ReplyWithoutLength,
    }

    /// <summary>
    /// 判断收到的帧是否说明设备的帧格式 (长度字节) 与程序的设置不同,通讯出错时据此提示检查设置。
    /// </summary>
    /// <remarks>
    /// 设备的响应总以回显 (大写字母、<c>?</c>) 或 <c>-</c> (无回显的错误,如 <c>-C</c>) 开头。
    /// <list type="bullet">
    /// <item>程序未启用长度:<c>{</c> 之后不是这些字符,而前两个字节作为长度又放得下后面的数据,就是带长度的响应。
    /// 实测 (2026-10-18,TCP,设备的 G1407/G1408 开着):<c>{H01}</c> 的回答是 <c>{&lt;00&gt;&lt;02&gt;-Cp}</c>。</item>
    /// <item>程序启用长度:不带长度的短响应 (如 <c>{H01+:…}</c>,<c>H0</c> 被当作长度 18480) 等不到这么多数据,
    /// 超时后作为不完整数据报告;其内容正好是一个完整的文本帧时就是不带长度的响应。
    /// 不带长度的长响应解码器已按无长度读出,不在此列。</item>
    /// </list>
    /// </remarks>
    public static class FramingCheck
    {
        /// <summary>检查收到的帧。</summary>
        /// <param name="frame">收到的帧 (含不完整、无效的帧)。</param>
        /// <param name="options">程序当前的帧格式。</param>
        public static FramingMismatch Check(ZevisionFrame frame, FrameOptions options)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var raw = frame.Raw;
            if (raw.Length < 4 || raw[0] != ZevisionFrameCodec.StartByte)
            {
                return FramingMismatch.None;
            }

            if (!options.LengthEnabled)
            {
                var length = (raw[1] << 8) | raw[2];
                return !IsResponseStart(raw[1]) && length > 0 && 3 + length < raw.Length
                    ? FramingMismatch.ReplyHasLength
                    : FramingMismatch.None;
            }

            return frame.Error == FrameError.Incomplete
                && raw[raw.Length - 1] == ZevisionFrameCodec.StopByte
                && IsResponseStart(raw[1])
                && raw.Skip(1).Take(raw.Length - 2).All(IsText)
                ? FramingMismatch.ReplyWithoutLength
                : FramingMismatch.None;
        }

        private static bool IsResponseStart(byte b) => b == (byte)'?' || b == (byte)'-' || (b >= (byte)'A' && b <= (byte)'Z');

        private static bool IsText(byte b) => b == (byte)'\t' || b == (byte)'\n' || b == (byte)'\r' || (b >= 0x20 && b != 0x7F);
    }
}
