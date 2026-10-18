using System.Collections.Generic;
using Xunit;
using Zevision.Core.Configuration;

namespace Zevision.Tests
{
    public class CrucibleRuleTests
    {
        /// <summary>实机的输出源:S01 有 6 个坩埚,S02 有 8 个。</summary>
        private static readonly Dictionary<string, string> Device = new Dictionary<string, string>
        {
            ["S0107"] = "6",
            ["S0207"] = "8",
            ["F010901"] = "1",
            ["F020901"] = "2",
            ["F030901"] = "0",   // 未选择输出源
            ["F040901"] = "3",   // 输出源不存在
        };

        private static string? ValueOf(string id) => Device.TryGetValue(id, out var value) ? value : null;

        [Theory]
        [InlineData("F010902", true)]
        [InlineData("F200902", true)]
        [InlineData("F010901", false)]   // 输出源
        [InlineData("F0109", false)]     // 分组
        [InlineData("F01090201", false)]
        [InlineData("S010902", false)]
        [InlineData("F000902", false)]   // 00 不是对象编号
        public void IsCrucible(string id, bool expected) => Assert.Equal(expected, CrucibleRule.IsCrucible(id));

        [Theory]
        [InlineData("F010902", 6)]
        [InlineData("F020902", 8)]
        [InlineData("F030902", CrucibleRule.DefaultCount)]   // 未选择输出源
        [InlineData("F040902", CrucibleRule.DefaultCount)]   // 输出源不存在
        [InlineData("F050902", CrucibleRule.DefaultCount)]   // 膜系没有输出源项
        public void MaxFor_IsTheSourceCrucibleCount(string id, int expected) => Assert.Equal(expected, CrucibleRule.MaxFor(id, ValueOf));

        [Fact]
        public void MaxFor_SourceWithoutCrucibles_IsOne()
        {
            var values = new Dictionary<string, string> { ["F010901"] = "1", ["S0107"] = "0" };

            Assert.Equal(1, CrucibleRule.MaxFor("F010902", id => values.TryGetValue(id, out var v) ? v : null));
        }

        [Theory]
        [InlineData("F010902", "0")]     // 任意
        [InlineData("F010902", "1")]
        [InlineData("F010902", "6")]
        [InlineData("F020902", "8")]
        [InlineData("F030902", "9")]     // 未选择输出源:无法判断,交给设备
        [InlineData("F040902", "9")]     // 输出源不存在:无法判断
        [InlineData("F010902", "x")]     // 不是数字:由值的校验处理
        [InlineData("F0102", "9")]       // 不是坩埚号项
        public void Check_WithinCountOrUndecidable_IsNull(string id, string value) => Assert.Null(CrucibleRule.Check(id, value, ValueOf));

        [Fact]
        public void Check_AboveSourceCount_NamesTheSourceAndCount()
        {
            Assert.Equal("坩埚 7 超出输出源 S01 的坩埚数量 (6)", CrucibleRule.Check("F010902", "7", ValueOf));
            Assert.NotNull(CrucibleRule.Check("F020902", "9", ValueOf));
        }
    }
}
