using Xunit;
using Zevision.Communication;
using Zevision.Localization;

namespace Zevision.Tests
{
    public class ProtocolTextTests
    {
        [Theory]
        [InlineData("H01")]
        [InlineData("QG01;QG1304")]
        [InlineData("UF0101:Film 10")]
        public void ValidateCommand_Valid_ReturnsNull(string command)
        {
            Assert.Null(ProtocolText.ValidateCommand(command));
            Assert.Equal(ProtocolTextError.None, ProtocolText.CheckCommand(command, out _));
        }

        [Theory]
        [InlineData("", ProtocolTextError.Empty)]
        [InlineData("{H01}", ProtocolTextError.Delimiter)]
        [InlineData("H01}", ProtocolTextError.Delimiter)]
        [InlineData("UF0101:膜", ProtocolTextError.NotPrintableAscii)]
        [InlineData("H01\r", ProtocolTextError.NotPrintableAscii)]
        public void ValidateCommand_Invalid_ReturnsError(string command, ProtocolTextError expected)
        {
            Assert.Equal(expected, ProtocolText.CheckCommand(command, out _));
            Assert.NotNull(ProtocolText.ValidateCommand(command));
            Assert.NotNull(CommunicationText.ValidateCommand(command));
        }

        [Theory]
        [InlineData("1.25")]
        [InlineData("Film 10")]
        [InlineData("0:00:30")]
        public void ValidateValue_Valid_ReturnsNull(string value)
        {
            Assert.Null(ProtocolText.ValidateValue(value));
            Assert.Null(CommunicationText.ValidateValue(value));
        }

        [Theory]
        [InlineData("", ProtocolTextError.Empty, "值不能为空", "The value must not be empty")]
        [InlineData("a;H01", ProtocolTextError.Separator, "不能包含 ; { }", "Must not contain ; { }")]
        [InlineData("a{", ProtocolTextError.Delimiter, "不能包含 ; { }", "Must not contain ; { }")]
        [InlineData("Å", ProtocolTextError.NotPrintableAscii, "只能使用英文、数字和常用符号 (可打印 ASCII)", "Only letters, digits and common symbols (printable ASCII) are allowed")]
        public void ValidateValue_Invalid_ReturnsReason(string value, ProtocolTextError expected, string chinese, string english)
        {
            Assert.Equal(expected, ProtocolText.CheckValue(value, out _));

            // 库只给英文;界面文本由应用按语言生成。
            Assert.Equal(english, ProtocolText.ValidateValue(value));
            Assert.Equal(chinese, CommunicationText.ValidateValue(value));
            using (Lang.Override(UiLanguage.English))
            {
                Assert.Equal(english, CommunicationText.ValidateValue(value));
            }
        }

        [Fact]
        public void CheckCommand_ReportsOffendingCharacter()
        {
            Assert.Equal(ProtocolTextError.NotPrintableAscii, ProtocolText.CheckCommand("H0Å1", out var c));
            Assert.Equal('Å', c);
            Assert.Equal("命令只能包含可打印 ASCII 字符,发现 U+00C5。", CommunicationText.ValidateCommand("H0Å1"));
        }
    }
}
