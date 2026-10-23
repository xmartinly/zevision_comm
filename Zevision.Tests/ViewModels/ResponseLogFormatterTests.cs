using System;
using System.Linq;
using Xunit;
using Zevision.Communication;
using Zevision.ViewModels.Display;

namespace Zevision.Tests
{
    public class ResponseLogFormatterTests
    {
        [Fact]
        public void SingleCommand_Success_NoExtraLines()
        {
            var entries = ResponseLogFormatter.Format(new[] { "QG01" }, ZevisionResponseParser.Parse("QG01+:1"), DateTime.Now);

            Assert.Empty(entries); // 原始响应已在 RX 行中
        }

        [Fact]
        public void SingleCommand_Error_Shown()
        {
            var entry = Assert.Single(ResponseLogFormatter.Format(new[] { "QM0F01" }, ZevisionResponseParser.Parse("QM0F01-D"), DateTime.Now));

            Assert.Equal(LogKind.Error, entry.Kind);
            Assert.StartsWith("QM0F01: D ", entry.Text);
        }

        [Fact]
        public void MultipleCommands_OneLineEach()
        {
            var entries = ResponseLogFormatter.Format(new[] { "QG01", "UG0E03:78" }, ZevisionResponseParser.Parse("QG01+:1;UG0E03+"), DateTime.Now);

            Assert.Equal(new[] { "解析 2 条指令的响应:", "QG01 = 1", "UG0E03 ✓" }, entries.Select(e => e.Text));
            Assert.True(entries[1].IsContinuation);
        }

        [Fact]
        public void MultipleCommands_HelpTableInAFrame_OneShortLine()
        {
            // 与 ? 同帧时帮助表 (约 116 KB、上千行) 不放进一个条目:列表无法虚拟化;完整内容在 RX 行中。
            var table = "[CMD ID] [VALUE] [DESCRIPTION]\n" + string.Concat(Enumerable.Range(0, 2000).Select(i => "G01   1   Line " + i + "\n"));

            var entries = ResponseLogFormatter.Format(new[] { "QG01", "?" }, ZevisionResponseParser.Parse("QG01+:1;?+:" + table), DateTime.Now);

            var help = entries.Last().Text;
            Assert.StartsWith("? = [CMD ID] [VALUE] [DESCRIPTION]… (共 " + table.Length + " 个字符,见上面的 RX 行)", help, StringComparison.Ordinal);
            Assert.DoesNotContain("\n", help);
            Assert.Equal("QG01 = 1", entries[1].Text);
            Assert.Equal(new string('x', 160) + "… (共 200 个字符,见上面的 RX 行)", ResponseLogFormatter.Shorten(new string('x', 200)));
        }

        [Fact]
        public void MultipleCommands_CountMismatch_Reported()
        {
            var entries = ResponseLogFormatter.Format(new[] { "QG01", "QG02" }, ZevisionResponseParser.Parse("QG01+:1"), DateTime.Now);

            Assert.Equal(LogKind.Error, entries.Last().Kind);
            Assert.Contains("发送了 2 条指令,但收到 1 条响应", entries.Last().Text);
        }
    }
}
