using System;
using System.Linq;
using Xunit;
using Zevision.Communication;
using Zevision.Models;
using Zevision.Services;
using Zevision.ViewModels;

namespace Zevision.Tests
{
    public class RestoreMessagesTests
    {
        private static RestoreSkip Skip(int i) =>
            new RestoreSkip(new RecipeEntry { Id = "F0101" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), Value = "1", Description = "Rate" }, "子列表条目数不同");

        [Fact]
        public void BuildSuccess_ListsSkippedItemsWithValueAndReason()
        {
            var report = new RestoreReport { CreatedCount = 2, WrittenCount = 5 };
            report.AdditionalSkipped.Add(Skip(1));

            var text = RestoreMessages.BuildSuccess(report);

            Assert.StartsWith("恢复完成:创建 2 个对象/条目,写入并校验 5 项。", text);
            Assert.Contains("F010101 Rate = 1  (子列表条目数不同)", text);
        }

        [Fact]
        public void BuildSuccess_ManySkips_IsTruncated()
        {
            var report = new RestoreReport { WrittenCount = 1 };
            report.AdditionalSkipped.AddRange(Enumerable.Range(0, RestoreMessages.MaxListedSkips + 3).Select(Skip));

            var text = RestoreMessages.BuildSuccess(report);

            Assert.Contains("… 另有 3 项,见数据显示区", text);
            Assert.DoesNotContain(Skip(RestoreMessages.MaxListedSkips).Id, text);
        }

        [Fact]
        public void BuildSummary_NoCreates_OmitsCreateCount()
        {
            Assert.Equal("恢复完成:写入并校验 3 项。", RestoreMessages.BuildSummary(new RestoreReport { WrittenCount = 3 }));
        }

        [Fact]
        public void BuildFailure_Success_ReturnsNull()
        {
            Assert.Null(RestoreMessages.BuildFailure(new RestoreReport { WrittenCount = 3 }, "已取消。"));
        }

        [Fact]
        public void BuildFailure_Cancelled_UsesReasonAndProgress()
        {
            var report = new RestoreReport { Cancelled = true, WrittenCount = 2, PlannedWriteCount = 5 };

            var text = RestoreMessages.BuildFailure(report, "超时");

            Assert.StartsWith("超时" + Environment.NewLine, text);
            Assert.Contains("已写入 2/5 项", text);
        }

        [Fact]
        public void BuildFailure_WriteFailed_NamesItem()
        {
            var report = new RestoreReport { FailedId = "F0302", Error = "B 非法参数值" };

            Assert.StartsWith("写入 F0302 失败,已停止: B 非法参数值", RestoreMessages.BuildFailure(report, "已取消。"));
        }

        [Fact]
        public void BuildConfirmation_ListsCreatesWritesAndSkips()
        {
            var recipe = new InstrumentRecipe { DeviceName = "IMC300", FirmwareVersion = "1.0", SerialNumber = "123" };
            var snapshot = new InstrumentSnapshot(Array.Empty<HelpTableEntry>());
            var plan = new RestorePlan(
                new[] { new RestoreCreate("M03", "材料", "AM03") },
                new[] { new RestoreWrite("M0301", "Gold", "Aluminum", "Name") },
                7,
                new[] { Skip(1) },
                Array.Empty<string>());

            var text = RestoreMessages.BuildConfirmation(@"C:\recipe\a.json", recipe, snapshot, plan);

            Assert.Contains("文件: a.json", text);
            Assert.Contains("材料 M03 (AM03)", text);
            Assert.Contains("M0301 Name: Aluminum → Gold", text);
            Assert.Contains("以下项不会写入:", text);
            Assert.EndsWith("是否继续?", text);
        }
    }
}
