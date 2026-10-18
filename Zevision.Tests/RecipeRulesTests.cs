using Xunit;
using Zevision.Core.Configuration;

namespace Zevision.Tests
{
    public class RecipeRulesTests
    {
        [Theory]
        [InlineData("F0101", "L1")]
        [InlineData("F01030102", "8.00")]
        [InlineData("M0103", "5.890")]
        [InlineData("L010201", "1")]
        [InlineData("P020101", "P1")]
        [InlineData("N0101", "Sensor")]
        [InlineData("S0101", "Source")]
        [InlineData("T010101", "0")]
        [InlineData("I0101", "Input1")]
        [InlineData("O0102", "0")]
        [InlineData("G0E03", "78")]
        public void IsRestorable_ConfigurationItems(string id, string value)
        {
            Assert.True(RecipeRules.IsRestorable(id, value, out var reason), reason);
        }

        [Theory]
        [InlineData("C01", "0")]          // 启停工艺
        [InlineData("C09", "Ready")]      // 当前状态
        [InlineData("G1303", "115200")]   // 通讯波特率
        [InlineData("G1402", "192.168.1.88")]
        [InlineData("G0A", "10-15-2026")] // 系统时间
        [InlineData("H01", "x")]
        [InlineData("F0103FE", "2")]      // 列表计数
        [InlineData("MFE", "12")]
        [InlineData("N0112", "0.0000")]   // 当前 QCM 频率
        [InlineData("S0106", "0")]        // 开关挡板
        [InlineData("S0108", "1")]        // 切换坩埚
        [InlineData("O0105", "0")]        // 输出状态 (写入会动作继电器)
        [InlineData("I0105", "1")]
        [InlineData("P020106", "143")]    // 运行次数
        [InlineData("F0101", "")]         // 无值
        public void IsRestorable_ExcludesStatusActionsAndCommunication(string id, string value)
        {
            Assert.False(RecipeRules.IsRestorable(id, value, out var reason));
            Assert.NotEmpty(reason);
        }

        [Theory]
        [InlineData("N0108", "1")] // 位置数量
        [InlineData("N011F", "0")] // XIU 电缆长度
        public void IsRestorable_ExcludesQueryOnlyItemsOfCommandSet(string id, string value)
        {
            Assert.False(RecipeRules.IsRestorable(id, value, out var reason));
            Assert.Contains("只读项", reason);
        }

        [Theory]
        [InlineData("F0302", "3", "M03")]
        [InlineData("F2002", "16", "M10")]       // 十进制 16 → 材料 M10
        [InlineData("L01020101", "1", "F01")]
        public void TryGetReference_DecimalValueToHexObjectId(string id, string value, string target)
        {
            Assert.True(RecipeRules.TryGetReference(id, value, out var targetId, out _));
            Assert.Equal(target, targetId);
        }

        [Theory]
        [InlineData("F0302", "0")]    // 0 = 未选择
        [InlineData("F0301", "3")]    // 名称,不是引用
        [InlineData("F0302", "abc")]
        [InlineData("P02420701", "31")] // 工艺中的"层"不是层编号 (实机 31 对应不到 L1F)
        public void TryGetReference_None(string id, string value)
        {
            Assert.False(RecipeRules.TryGetReference(id, value, out _, out _));
        }

        [Fact]
        public void GroupRank_SystemThenMaterialFilmLayerProcess()
        {
            // 系统设置 (传感器、源、输入输出、逻辑、系统偏好) 全部在材料之前。
            foreach (var system in new[] { "N0101", "S0101", "I0101", "O0101", "T010101", "G0E01" })
            {
                Assert.True(RecipeRules.GroupRank(system) < RecipeRules.GroupRank("M0101"), system);
            }

            Assert.True(RecipeRules.GroupRank("N0101") < RecipeRules.GroupRank("S0101"));
            Assert.True(RecipeRules.GroupRank("M0101") < RecipeRules.GroupRank("F0101"));
            Assert.True(RecipeRules.GroupRank("F0101") < RecipeRules.GroupRank("L0101"));
            Assert.True(RecipeRules.GroupRank("L0101") < RecipeRules.GroupRank("P020101"));
        }

        [Theory]
        [InlineData("N0101", true)]
        [InlineData("S010A", true)]
        [InlineData("I0101", true)]
        [InlineData("O0102", true)]
        [InlineData("T010101", true)]
        [InlineData("G0E03", true)]
        [InlineData("M0101", false)]
        [InlineData("F0101", false)]
        [InlineData("L010201", false)]
        [InlineData("P020101", false)]
        public void IsSystemSetting(string id, bool expected)
        {
            Assert.Equal(expected, RecipeRules.IsSystemSetting(id));
        }
    }
}
