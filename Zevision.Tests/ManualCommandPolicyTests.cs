using Xunit;
using Zevision.ViewModels;

namespace Zevision.Tests
{
    public class ManualCommandPolicyTests
    {
        [Theory]
        [InlineData("qg01", "QG01")]
        [InlineData("qg01;qg1304；h01", "QG01;QG1304；H01")]
        [InlineData("um0101:Aluminium", "UM0101:Aluminium")]               // 数据 (名称) 不变
        [InlineData("uf0101:a:b;qg01", "UF0101:a:b;QG01")]                // 数据中的 ':' 不结束数据
        [InlineData("um0101:Al\nqm0101", "UM0101:Al\nQM0101")]          // 换行开始新指令
        [InlineData("e:hello", "E:hello")]
        [InlineData("?m", "?M")]
        [InlineData("", "")]
        public void ToUpperCommands_UppercasesOnlyCommandPart(string input, string expected)
        {
            Assert.Equal(expected, ManualCommandPolicy.ToUpperCommands(input));
        }

        [Theory]
        [InlineData("?")]
        [InlineData(" ? ")]
        [InlineData("H01;?")]
        [InlineData("H01; ?")]
        [InlineData("?;H01")]
        public void ContainsHelpCommand_True(string command)
        {
            Assert.True(ManualCommandPolicy.ContainsHelpCommand(command));
        }

        [Theory]
        [InlineData("H01")]
        [InlineData("QG01;QG1304")]
        [InlineData("UF0101:What?")]
        [InlineData("")]
        public void ContainsHelpCommand_False(string command)
        {
            Assert.False(ManualCommandPolicy.ContainsHelpCommand(command));
        }

        [Theory]
        [InlineData("QG01;QG1304;QG1305", new[] { "QG01", "QG1304", "QG1305" })]
        [InlineData(" QG01 ; QG1304 ", new[] { "QG01", "QG1304" })]
        [InlineData("QG01;;QG1304;", new[] { "QG01", "QG1304" })]
        [InlineData("QG01；QG1304", new[] { "QG01", "QG1304" })] // 中文输入法的全角分号
        [InlineData("UF0101:Film 10;QF0101", new[] { "UF0101:Film 10", "QF0101" })]
        [InlineData(" ; ", new string[0])]
        public void Split_NormalizesSegments(string input, string[] expected)
        {
            Assert.Equal(expected, ManualCommandPolicy.Split(input));
        }

        [Theory]
        [InlineData("?", true)]
        [InlineData("H01; ? ", true)]
        [InlineData("?M", false)]   // 分组帮助:实机约 4.9 KB、0.45 秒
        [InlineData("?S01", false)]
        [InlineData("H01", false)]
        public void ContainsFullHelpCommand(string command, bool expected)
        {
            Assert.Equal(expected, ManualCommandPolicy.ContainsFullHelpCommand(command));
            Assert.Equal(command != "H01", ManualCommandPolicy.ContainsHelpCommand(command)); // 分组帮助同样使用较长超时
        }

        [Fact]
        public void ContainsHelpCommand_FullWidthSeparator()
        {
            Assert.True(ManualCommandPolicy.ContainsHelpCommand("H01；?"));
        }
    }
}
