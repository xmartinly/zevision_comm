using Xunit;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Core.Configuration;

namespace Zevision.Tests
{
    public class ParameterValueTests
    {
        [Theory]
        [InlineData("M0103", "2.700", null)]
        [InlineData("M0103", "0.05", "超出范围")]
        [InlineData("M0103", "2,7", "应为数字")]
        [InlineData("G0E03", "50", null)]
        [InlineData("G0E03", "50.5", "应为整数")]
        [InlineData("G0E03", "101", "超出范围 20 ~ 100 %")]
        [InlineData("S010A", "-5.5", null)]
        [InlineData("F010402", "0:00:30", null)]
        [InlineData("F010402", "1:60:00", "时间格式")]
        [InlineData("F010402", "150:00:00", null)]
        [InlineData("F010402", "150:00:01", "超出范围 H:MM:SS (0:00:00 ~ 150:00:00)")]
        [InlineData("F010404", "0:00:00", "超出范围 H:MM:SS (0:00:01 ~ 3:00:00)")]
        [InlineData("F010404", "3:00:00", null)]
        [InlineData("N0107", "999:00:00", null)]
        [InlineData("L010201060102", "151:00:00", "超出范围")]
        [InlineData("S011C", "3999999", "超出范围")]
        [InlineData("S0108", "33", "超出范围")]
        [InlineData("N010E", "2", null)]
        [InlineData("G0E01", "1", null)]
        [InlineData("G0E01", "2", "只能选择: 0=关, 1=开")]
        [InlineData("F0102", "3", null)]
        [InlineData("F0102", "x", "对象编号")]
        [InlineData("M0101", "Al;Au", "; { }")]
        public void Validate(string id, string value, string? expected)
        {
            var error = ParameterValue.Validate(ZevisionCommandSet.Find(id), value);

            if (expected == null)
            {
                Assert.Null(error);
            }
            else
            {
                Assert.Contains(expected, error);
            }
        }

        [Fact]
        public void Validate_UnknownDefinition_ChecksOnlyProtocolCharacters()
        {
            Assert.Null(ParameterValue.Validate(null, "anything 1.2"));
            Assert.NotNull(ParameterValue.Validate(null, "a}b"));
        }

        [Theory]
        [InlineData("M0103", "2.7", "5.890", "2.700")]
        [InlineData("M0103", " 2.7004 ", "5.890", "2.700")]
        [InlineData("F010403", "7", "5.00", "7.00")]
        [InlineData("G0E03", "50", "78", "50")]
        [InlineData("F010402", "1:5", "0:00:00", "0:01:05")]
        [InlineData("F010402", "02:03:04", "0:00:00", "2:03:04")]
        [InlineData("M0101", " Gold ", "Al", "Gold")]
        [InlineData("M0103", "abc", "5.890", "abc")]
        public void Normalize(string id, string value, string original, string expected)
        {
            Assert.Equal(expected, ParameterValue.Normalize(ZevisionCommandSet.Find(id), value, original));
        }

        [Theory]
        [InlineData("5.890", "5.890", true)]
        [InlineData("5.89", "5.890", true)]
        [InlineData("-1", "-1.0", true)]
        [InlineData("0:00:05", "00:00:05", true)]
        [InlineData("0:05", "0:00:05", true)]
        [InlineData("5.89", "5.891", false)]
        [InlineData("Al", "al", false)]
        [InlineData("1", null, false)]
        public void AreEquivalent(string expected, string? actual, bool equal)
        {
            Assert.Equal(equal, ParameterValue.AreEquivalent(expected, actual));
        }

        [Theory]
        [InlineData("0:00:01", 1)]
        [InlineData("8760:00:00", 8760 * 3600)]
        [InlineData("2:30", 150)]
        public void TryParseTime_Valid(string text, int seconds)
        {
            Assert.True(ParameterValue.TryParseTime(text, out var actual));
            Assert.Equal(seconds, actual);
        }

        [Theory]
        [InlineData("8761:00:00")]
        [InlineData("0:60")]
        [InlineData("10")]
        [InlineData("1:2:3:4")]
        [InlineData("-1:00")]
        [InlineData("1::00")]
        public void TryParseTime_Invalid(string text)
        {
            Assert.False(ParameterValue.TryParseTime(text, out _));
        }
    }
}
