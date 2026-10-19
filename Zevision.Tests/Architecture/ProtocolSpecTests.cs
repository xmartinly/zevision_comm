using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Zevision.Commands;
using Zevision.Communication;

namespace Zevision.Tests
{
    /// <summary>
    /// 协议规格书 (<c>docs/协议规格书.md</c>) 与代码一致 (开发规范 §6:帧格式、命令表、错误码表必须与实现一致)。
    /// 命令表由这里生成:设置环境变量 <c>ZEVISION_UPDATE_DOCS=1</c> 运行 <see cref="CommandTable_MatchesCommandSet"/> 即重新写入。
    /// </summary>
    public class ProtocolSpecTests
    {
        private const string TableStart = "<!-- 命令表开始 (由 ProtocolSpecTests 生成,不要手工修改) -->";
        private const string TableEnd = "<!-- 命令表结束 -->";

        private static readonly string SpecPath = Path.Combine(RepositoryRoot(), "docs", "协议规格书.md");

        [Fact]
        public void CommandTable_MatchesCommandSet()
        {
            var document = File.ReadAllText(SpecPath, Encoding.UTF8).Replace("\r\n", "\n");
            var start = document.IndexOf(TableStart, StringComparison.Ordinal);
            var end = document.IndexOf(TableEnd, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, "协议规格书中缺少命令表的起止标记。");

            var expected = RenderCommandTable();
            var actual = document.Substring(start + TableStart.Length, end - start - TableStart.Length);
            if (actual == expected)
            {
                return;
            }

            if (Environment.GetEnvironmentVariable("ZEVISION_UPDATE_DOCS") == "1")
            {
                File.WriteAllText(SpecPath, document.Substring(0, start + TableStart.Length) + expected + document.Substring(end), new UTF8Encoding(false));
                return;
            }

            Assert.Fail("协议规格书的命令表与 ZevisionCommandSet 不一致;设置 ZEVISION_UPDATE_DOCS=1 后运行本测试重新生成。");
        }

        [Fact]
        public void ErrorCodeTable_ListsEveryDeviceCode()
        {
            var document = File.ReadAllText(SpecPath, Encoding.UTF8);
            foreach (var code in "ABCDFLT")
            {
                var parsed = ZevisionResponseParser.Parse("QG01-" + code).Single().ErrorCode;
                Assert.NotEqual(ZevisionErrorCode.Unknown, parsed);
                Assert.Contains("| `" + code + "` |", document);
            }
        }

        [Fact]
        public void FrameConstants_MatchCode()
        {
            var document = File.ReadAllText(SpecPath, Encoding.UTF8);

            Assert.Contains("1 ~ " + ZevisionFrameCodec.MaxDataLength, document);
            Assert.Contains(ZevisionFrameDecoder.MaxReceiveLength + " 字节", document);
            Assert.Contains("`{` (0x" + ZevisionFrameCodec.StartByte.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + ")", document);
            Assert.Contains("`}` (0x" + ZevisionFrameCodec.StopByte.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + ")", document);
            Assert.Contains("追加 `;" + ZevisionFrameCodec.FillerCommand + "`", document);
        }

        private static string RenderCommandTable()
        {
            var text = new StringBuilder("\n| 指令 ID | 动作 | 名称 | English | 类型 | 范围 / 选项 |\n|---|---|---|---|---|---|\n");
            foreach (var definition in ZevisionCommandSet.All)
            {
                text.Append("| `").Append(definition.Pattern).Append("` | ")
                    .Append(Actions(definition.Actions)).Append(" | ")
                    .Append(Cell(definition.ChineseName)).Append(" | ")
                    .Append(Cell(definition.EnglishName)).Append(" | ")
                    .Append(Kind(definition)).Append(" | ")
                    .Append(Cell(definition.GetRangeText(TextLanguage.Chinese))).Append(" |\n");
            }

            return text.ToString();
        }

        private static string Actions(CommandActions actions)
        {
            var letters = new[]
            {
                (CommandActions.Query, 'Q'), (CommandActions.Update, 'U'), (CommandActions.Add, 'A'),
                (CommandActions.Delete, 'D'), (CommandActions.Status, 'S'), (CommandActions.Remote, 'R'),
            }.Where(a => (actions & a.Item1) != 0).Select(a => a.Item2).ToArray();
            return letters.Length == 0 ? "—" : new string(letters);
        }

        private static string Kind(CommandDefinition definition)
        {
            switch (definition.Kind)
            {
                case CommandValueKind.Text: return "文本";
                case CommandValueKind.Number: return definition.IsInteger ? "整数" : "数字";
                case CommandValueKind.Enum: return "选项";
                case CommandValueKind.Time: return "时间";
                case CommandValueKind.Reference: return "引用";
                default: return "—";
            }
        }

        /// <summary>表格单元:<c>|</c> 和 Markdown 强调符号转义,换行变为空格。</summary>
        private static string Cell(string? text) =>
            text is null || text.Length == 0 ? string.Empty
                : text.Replace("\r", string.Empty).Replace('\n', ' ').Replace("|", "\\|").Replace("*", "\\*").Replace("_", "\\_");

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Zevision.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new DirectoryNotFoundException("找不到 Zevision.sln 所在的目录。");
        }
    }
}
