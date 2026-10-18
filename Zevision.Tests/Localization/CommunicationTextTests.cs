using System;
using System.IO;
using Xunit;
using Zevision.Communication;
using Zevision.Core.Localization;

namespace Zevision.Tests
{
    /// <summary>
    /// 通讯库只给英文说明和错误类型;界面文本 (与拆分前完全相同) 由 <see cref="CommunicationText"/> 生成。
    /// </summary>
    public class CommunicationTextTests
    {
        private static readonly SerialSettings Serial = new SerialSettings("COM3", 115200);
        private static readonly TcpSettings Tcp = new TcpSettings("192.168.0.10", TcpSettings.DefaultPort);

        public static TheoryData<ZevisionError, string?, string, string> Cases => new TheoryData<ZevisionError, string?, string, string>
        {
            { new ZevisionError(ZevisionErrorKind.AlreadyConnected, "x"), null, "已经连接。", "Already connected." },
            { new ZevisionError(ZevisionErrorKind.NotConnected, "x"), null, "未连接。", "Not connected." },
            { new ZevisionError(ZevisionErrorKind.Disconnected, "x"), null, "连接已断开。", "Disconnected." },
            {
                new ZevisionError(ZevisionErrorKind.ConnectFailed, "x") { Exception = new UnauthorizedAccessException("拒绝访问。"), Endpoint = Serial },
                null, "无法连接 COM3, 115200, 8N1: 拒绝访问。", "Cannot connect to COM3, 115200, 8N1: 拒绝访问。"
            },
            {
                new ZevisionError(ZevisionErrorKind.ConnectFailed, "x") { Exception = new TimeoutException("Connection timed out"), Endpoint = Tcp },
                null, "无法连接 192.168.0.10:2101: 连接超时 (5 秒内无响应)。请检查 IP 地址、端口和网络。",
                "Cannot connect to 192.168.0.10:2101: Connection timed out (no response within 5 s). Check the IP address and the network."
            },
            {
                new ZevisionError(ZevisionErrorKind.SendFailed, "x") { Exception = new IOException("端口已关闭") },
                null, "发送失败: 端口已关闭", "Sending failed: 端口已关闭"
            },
            {
                new ZevisionError(ZevisionErrorKind.Timeout, "x") { Timeout = TimeSpan.FromMilliseconds(1000) },
                null, "等待响应超时 (1000 ms 内未收到数据)。", "Timed out waiting for a response (no data within 1000 ms)."
            },
            {
                new ZevisionError(ZevisionErrorKind.Timeout, "x") { Timeout = TimeSpan.FromSeconds(2), ReceivedIncompleteData = true },
                null, "等待响应超时 (2000 ms 内未收到数据,已收到的不完整数据见上方)。",
                "Timed out waiting for a response (no data within 2000 ms; the incomplete data received is shown above)."
            },
            {
                new ZevisionError(ZevisionErrorKind.InvalidFrame, "x") { FrameError = FrameError.ChecksumMismatch },
                null, "响应帧无效: 校验和不匹配", "Invalid response frame: checksum mismatch"
            },
            {
                new ZevisionError(ZevisionErrorKind.UnparseableResponse, "x") { DataLength = 12 },
                null, "无法解析响应 (12 字节)。", "Cannot parse the response (12 bytes)."
            },
            { new ZevisionError(ZevisionErrorKind.InvalidCommand, "x"), "  ", "命令不能为空。", "The command must not be empty." },
            {
                new ZevisionError(ZevisionErrorKind.InvalidCommand, "x"), "H{01",
                "命令中不能包含起止符 { 或 },它们由程序自动添加。", "Commands must not contain the delimiters { or }; they are added automatically."
            },
            {
                new ZevisionError(ZevisionErrorKind.InvalidCommand, "x"), new string('Q', ZevisionFrameCodec.MaxDataLength + 1),
                "命令长度超过 57000 字节。", "The command is longer than 57000 bytes."
            },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Describe_GivesUiText(ZevisionError error, string? command, string chinese, string english)
        {
            Assert.Equal(chinese, CommunicationText.Describe(error, command));
            using (Lang.Override(UiLanguage.English))
            {
                Assert.Equal(english, CommunicationText.Describe(error, command));
            }
        }

        [Fact]
        public void ErrorCode_LocalizedAndEnglish()
        {
            Assert.Equal("非法参数值", ZevisionErrorCode.IllegalArgument.Localized());
            Assert.Equal("Illegal parameter value", ZevisionErrorCode.IllegalArgument.Describe());
            using (Lang.Override(UiLanguage.English))
            {
                Assert.Equal("Illegal parameter value", ZevisionErrorCode.IllegalArgument.Localized());
            }

            foreach (ZevisionErrorCode code in Enum.GetValues(typeof(ZevisionErrorCode)))
            {
                using (Lang.Override(UiLanguage.English))
                {
                    Assert.Equal(code.Describe(), code.Localized());
                }
            }
        }
    }
}
