using System.Linq;
using Xunit;
using Zevision.Communication;

namespace Zevision.Tests
{
    public class HelpTableParserTests
    {
        // 摘自实测 IMC300 固件 1.8.11 的 ? 响应 (定宽 30 列)。
        private const string Sample =
            "\n[CMD ID]                      [VALUE]                       [DESCRIPTION]\n"
            + "C                                                           Process Control Info\n"
            + "C01                           0                             Stop/Start Process\n"
            + "C09                           Ready                         Current State\n"
            + "F01030101                     0:59:59                       Start Ramp/Rise time\n"
            + "F0A01                         Film 10                       Name\n"
            + "G08                           1.8.11.202606101229           System Software Version\n"
            + "G0A                           10-15-2026 15:38:05           System Time\n"
            + "M0103                         5.890                         Density (g/cm^3)\n"
            + "L020201060101                 0.0000                        Start Thickness\n";

        [Fact]
        public void Parse_ReadsFixedWidthColumns()
        {
            var entries = HelpTableParser.Parse(Sample);

            Assert.Equal(new[] { "C", "C01", "C09", "F01030101", "F0A01", "G08", "G0A", "M0103", "L020201060101" }, entries.Select(e => e.Id));
            Assert.Equal("Ready", entries[2].Value);
            Assert.Equal("Current State", entries[2].Description);
        }

        [Fact]
        public void Parse_KeepsSpacesAndColonsInsideValues()
        {
            var entries = HelpTableParser.Parse(Sample);

            Assert.Equal("Film 10", entries.Single(e => e.Id == "F0A01").Value);
            Assert.Equal("0:59:59", entries.Single(e => e.Id == "F01030101").Value);
            Assert.Equal("10-15-2026 15:38:05", entries.Single(e => e.Id == "G0A").Value);
        }

        [Fact]
        public void Parse_TitleRowHasEmptyValue()
        {
            var title = HelpTableParser.Parse(Sample).First();

            Assert.Equal(string.Empty, title.Value);
            Assert.Equal("Process Control Info", title.Description);
        }

        [Fact]
        public void Parse_UsesHeaderColumnPositions()
        {
            var text = "[CMD ID]  [VALUE]   [DESCRIPTION]\nM0101     Al        Name\n";

            var entry = Assert.Single(HelpTableParser.Parse(text));

            Assert.Equal("M0101", entry.Id);
            Assert.Equal("Al", entry.Value);
            Assert.Equal("Name", entry.Description);
        }

        [Fact]
        public void Parse_IgnoresUnrecognizedLines()
        {
            var entries = HelpTableParser.Parse("\r\nnot a row\r\n\r\nM01" + new string(' ', 57) + "Material M01\r\n");

            Assert.Equal("M01", Assert.Single(entries).Id);
        }
    }
}
