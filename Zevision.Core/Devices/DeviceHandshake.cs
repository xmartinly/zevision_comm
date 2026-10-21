using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;

namespace Zevision.Core.Devices
{
    /// <summary>握手需要的通讯操作,由 <see cref="ZevisionDeviceService"/> 实现。</summary>
    internal interface IHandshakeLink
    {
        /// <summary>连接目标 (决定帧格式设置项的 ID)。</summary>
        ConnectionEndpoint Endpoint { get; }

        /// <summary>当前使用的帧格式。</summary>
        FrameOptions Frame { get; }

        /// <summary>改变当前连接的帧格式。</summary>
        Task<ZevisionResult> ChangeFrameAsync(FrameOptions frame, CancellationToken cancellationToken);

        /// <summary>发送一帧,不重试 (探测时要看到第一次的结果)。</summary>
        Task<ZevisionResult<ZevisionReply>> SendAsync(string command, CancellationToken cancellationToken);

        /// <summary>取出上次取出以来收到的全部字节 (按收到的顺序;用于识别帧格式)。</summary>
        byte[] TakeReceived();
    }

    /// <summary>
    /// 连接后的握手:按设备的回答确定帧格式 (协议选项),读取型号和帧格式设置。全部成功才算连接可用。
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>用期望的帧格式发送 <c>H01</c>。回答 <c>H01+:…</c> 即帧格式正确。</item>
    /// <item>否则等设备回答完 (<see cref="Settle"/>),用 <see cref="FrameFormatDetector"/> 从收到的字节识别设备的帧格式,
    /// 改用它再发 <c>H01</c>;识别不出时依次试其余格式。一个字节也没收到说明设备不在线 (设备对任何输入都会回答),直接失败。</item>
    /// <item>一帧查询 G03、G06 (序列号,用于通讯数据文件名) 和当前接口的帧格式设置 (RS232:G1304/G1305,TCP:G1407/G1408)。
    /// 型号读不到则失败;序列号读不到时为 null;型号不是 IMC300、或设置与实际通讯的格式不一致时作为提示返回。</item>
    /// </list>
    /// </remarks>
    internal static class DeviceHandshake
    {
        /// <summary>
        /// 一次探测失败后等待的时间:设备回答格式不对的输入最慢约 230 ms (启用长度时等 200 ms 字符超时后回 <c>-T</c>),
        /// 等它收完再识别,也避免它被当成下一次探测的回答。
        /// </summary>
        internal static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

        /// <summary>失败说明中最多列出的收到的字节数。</summary>
        private const int SampleLength = 48;

        private static readonly string HelloCommand = ZevisionCommandSet.Build(CommandActions.None, "H01");

        public static async Task<Result<DeviceIdentity>> RunAsync(IHandshakeLink link, FrameOptions preferred, CancellationToken cancellationToken)
        {
            var hello = await DetectFrameAsync(link, preferred, cancellationToken).ConfigureAwait(false);
            if (!hello.IsSuccess)
            {
                return Result<DeviceIdentity>.Fail(hello.Error);
            }

            var (checksumId, lengthId) = FrameSettings.Ids(link.Endpoint);
            var ids = new[] { InstrumentSnapshot.DeviceNameId, InstrumentSnapshot.SerialNumberId, checksumId, lengthId };
            var command = string.Join(";", ids.Select(ZevisionCommandSet.Query));
            var reply = await link.SendAsync(command, cancellationToken).ConfigureAwait(false);
            if (!reply.IsSuccess)
            {
                return Result<DeviceIdentity>.Fail(Texts.HandshakeQueryFailed(CommunicationText.Describe(reply.Error!, command)));
            }

            var responses = reply.Value.Responses;
            if (!(ValueOf(responses, ids[0]) is { Length: > 0 } model))
            {
                return Result<DeviceIdentity>.Fail(Texts.HandshakeNoModel);
            }

            var frame = link.Frame;
            var warnings = new List<string>();
            if (!string.Equals(model, DeviceIdentity.ExpectedModel, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(Texts.UnexpectedModel(model, DeviceIdentity.ExpectedModel));
            }

            if (ValueOf(responses, checksumId) is string checksum && ValueOf(responses, lengthId) is string length
                && IsFlag(checksum, out var checksumOn) && IsFlag(length, out var lengthOn)
                && !new FrameOptions(checksumOn, lengthOn).Equals(frame))
            {
                warnings.Add(Texts.FrameSettingsDiffer(checksumId, checksum, lengthId, length));
            }

            var serialNumber = ValueOf(responses, InstrumentSnapshot.SerialNumberId) is { Length: > 0 } serial ? serial : null;
            return Result<DeviceIdentity>.Ok(new DeviceIdentity(hello.Value, model, serialNumber, frame, warnings));
        }

        /// <summary>找到设备回答 <c>H01</c> 的帧格式,并改用它。</summary>
        /// <returns><c>H01</c> 回答的数据 (如 <c>Zevision IMC300 1.8.11…</c>)。</returns>
        private static async Task<Result<string>> DetectFrameAsync(IHandshakeLink link, FrameOptions preferred, CancellationToken cancellationToken)
        {
            var tried = new List<FrameOptions>();
            var received = Array.Empty<byte>();
            var candidate = preferred;
            while (candidate != null)
            {
                tried.Add(candidate);
                var changed = await link.ChangeFrameAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (!changed.IsSuccess)
                {
                    return Result<string>.Fail(CommunicationText.Describe(changed.Error!));
                }

                link.TakeReceived();
                var reply = await link.SendAsync(HelloCommand, cancellationToken).ConfigureAwait(false);
                var response = reply.IsSuccess ? reply.Value.Responses.FirstOrDefault() : null;
                if (response != null && string.Equals(response.Command, HelloCommand, StringComparison.OrdinalIgnoreCase))
                {
                    // 回显正确说明帧格式正确;设备仍可能拒绝 (不太可能,但不能当成功)。
                    return response.IsSuccess
                        ? Result<string>.Ok(response.Data?.Trim() ?? string.Empty)
                        : Result<string>.Fail(Texts.HandshakeRejected(response.RawErrorCode?.ToString() ?? string.Empty, response.ErrorCode.Localized()));
                }

                if (!reply.IsSuccess && IsConnectionFailure(reply.Error!.Kind))
                {
                    return Result<string>.Fail(CommunicationText.Describe(reply.Error, HelloCommand));
                }

                await Task.Delay(Settle, cancellationToken).ConfigureAwait(false);
                var bytes = link.TakeReceived();
                if (bytes.Length == 0 && tried.Count == 1)
                {
                    var reason = reply.IsSuccess ? Texts.NoResponse : CommunicationText.Describe(reply.Error!, HelloCommand);
                    return Result<string>.Fail(Texts.HandshakeNoReply(reason));
                }

                if (bytes.Length > 0)
                {
                    received = bytes;
                }

                var detected = bytes.Length > 0 ? FrameFormatDetector.Detect(bytes) : null;
                candidate = detected != null && !tried.Contains(detected)
                    ? detected
                    : FrameFormatDetector.All.FirstOrDefault(options => !tried.Contains(options));
            }

            return Result<string>.Fail(Texts.HandshakeFrameUnknown(Describe(received)));
        }

        private static bool IsConnectionFailure(ZevisionErrorKind kind) =>
            kind == ZevisionErrorKind.NotConnected || kind == ZevisionErrorKind.Disconnected || kind == ZevisionErrorKind.SendFailed;

        /// <summary>查询响应中某一项的值 (按回显对应);没有或被拒绝时为 null。</summary>
        private static string? ValueOf(IReadOnlyList<ZevisionResponse> responses, string id)
        {
            var echo = ZevisionResponseParser.EchoOf(ZevisionCommandSet.Query(id));
            var response = responses.FirstOrDefault(r => string.Equals(r.Command, echo, StringComparison.OrdinalIgnoreCase));
            return response != null && response.IsSuccess ? response.Data?.Trim() : null;
        }

        private static bool IsFlag(string value, out bool enabled)
        {
            var parsed = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number);
            enabled = number != 0;
            return parsed;
        }

        /// <summary>收到的字节的可读形式:可打印 ASCII 原样,其他字节写作 <c>&lt;XX&gt;</c>;过长时截断。</summary>
        private static string Describe(byte[] bytes)
        {
            var text = new StringBuilder();
            foreach (var b in bytes.Take(SampleLength))
            {
                if (b >= 0x20 && b < 0x7F)
                {
                    text.Append((char)b);
                }
                else
                {
                    text.Append('<').Append(b.ToString("X2", CultureInfo.InvariantCulture)).Append('>');
                }
            }

            return bytes.Length > SampleLength ? text.Append("...").ToString() : text.ToString();
        }
    }
}
