using System;
using System.Collections.Generic;
using System.Linq;
using Zevision.Communication;

namespace Zevision.Core.Devices
{
    /// <summary>
    /// 从设备的回答识别它在这个接口上使用的帧格式 (长度字节、校验和)。
    /// </summary>
    /// <remarks>
    /// 设备对任何 <c>{…}</c> 都按自己的帧格式回答;格式不对时回答错误。实测 (固件 1.8.11,2026-10-19):
    /// 无长度无校验时回 <c>-A</c>/<c>-D</c> (回显中的二进制字节变成 0),启用长度时约 200 ms 后回 <c>-T</c>,
    /// 只启用校验和时回 <c>-C</c>,TCP (长度和校验和都开) 一律回 <c>-C</c>。
    /// </remarks>
    public static class FrameFormatDetector
    {
        /// <summary>
        /// 所有帧格式,按结构由强到弱排列:带长度、校验和的格式几乎不会把其他格式的数据当成有效帧,
        /// 而不带两者的格式什么都能读,所以识别时依次尝试,先成功的为准。
        /// </summary>
        public static IReadOnlyList<FrameOptions> All { get; } = new[]
        {
            new FrameOptions(checksumEnabled: true, lengthEnabled: true),
            new FrameOptions(checksumEnabled: false, lengthEnabled: true),
            new FrameOptions(checksumEnabled: true, lengthEnabled: false),
            FrameOptions.Plain,
        };

        /// <summary>
        /// 识别收到的字节 (按收到的顺序拼接) 使用的帧格式:按 <see cref="All"/> 的顺序,第一个能把它们解成
        /// 至少一个响应帧、且每一帧都有效并能按响应格式解析的格式 (起始符之前的帧外数据不计)。
        /// </summary>
        /// <param name="received">收到的字节。</param>
        /// <returns>识别出的帧格式;无法识别时为 null。</returns>
        public static FrameOptions? Detect(byte[] received)
        {
            if (received is null)
            {
                throw new ArgumentNullException(nameof(received));
            }

            foreach (var candidate in All)
            {
                var decoder = new ZevisionFrameDecoder(candidate);
                var messages = decoder.Push(received, 0, received.Length)
                    .Concat(decoder.FlushIncomplete())
                    .Where(frame => frame.Error != FrameError.UnexpectedData)
                    .ToList();
                if (messages.Count > 0 && messages.All(frame => frame.IsValid && ZevisionResponseParser.TryParse(frame.Payload, out _)))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
