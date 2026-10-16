using System.Collections.Generic;
using System.Linq;
using Xunit;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Models;
using Zevision.Services;

namespace Zevision.Tests
{
    public class RecipeDocumentTests
    {
        [Fact]
        public void ObjectIds_AndNextFree()
        {
            var document = Document(("M01", ""), ("M0101", "Al"), ("M03", ""), ("M0301", "Au"), ("MFE", "2"), ("P0201", ""), ("P020101", "Proc"));

            Assert.Equal(new[] { "M01", "M03" }, document.ObjectIds('M').OrderBy(i => i));
            Assert.Equal("M02", ObjectTypes.Material.NextFreeId(document.ObjectIds('M')));
            Assert.Equal("P0202", ObjectTypes.Process.NextFreeId(document.ObjectIds('P')));
            Assert.Equal("Au", document.ObjectName("M03"));
            Assert.Null(document.ObjectName("M02"));
        }

        [Fact]
        public void ListItemCount_IgnoresAddAndCountItems()
        {
            var document = Document(("F0103", ""), ("F010300", ""), ("F010301", ""), ("F01030101", "0:00:10"), ("F010302", ""), ("F0103FE", "2"), ("F0105FE", "0"));

            Assert.Equal(2, document.ListItemCount("F0103"));
            Assert.Equal(0, document.ListItemCount("F0105"));
        }

        [Fact]
        public void ReplaceGroup_KeepsPosition()
        {
            var document = Document(("G01", "x"), ("M01", ""), ("M0101", "Al"), ("F01", ""));

            document.ReplaceGroup('M', new[] { Entry("M02", ""), Entry("M0201", "Cu") });

            Assert.Equal(new[] { "G01", "M02", "M0201", "F01" }, document.Entries.Select(e => e.Id));
        }

        [Fact]
        public void AddObject_CopiesOnlyThatObject_AfterItsGroup()
        {
            var document = Document(("M0101", "Al"), ("F0101", "Film1"));
            var device = new[] { Entry("M0101", "Al"), Entry("M0201", "Cu"), Entry("M0401", "Material"), Entry("M0403", "1.000") };

            document.AddObject("M04", device);
            document.Entries.Single(e => e.Id == "M0401").Value = "changed";

            Assert.Equal(new[] { "M0101", "M0401", "M0403", "F0101" }, document.Entries.Select(e => e.Id));
            Assert.Equal("Material", device.Single(e => e.Id == "M0401").Value); // 复制,不共用对象
        }

        [Fact]
        public void RemoveObject_RemovesAllItsEntries()
        {
            var document = Document(("M0101", "Al"), ("M0103", "2.7"), ("M0201", "Cu"), ("MFE", "2"));

            document.RemoveObject("M01");

            Assert.Equal(new[] { "M0201", "MFE" }, document.Entries.Select(e => e.Id));
        }

        [Fact]
        public void AddListItem_InsertsAfterTheList_RemoveListItem_RemovesWithChildren()
        {
            var document = Document(("P0201", ""), ("P02010701", "1"), ("P020108", "x"), ("F0103", ""), ("F010301", ""), ("F01030101", "0:00:10"), ("F0105", ""));

            document.AddListItem("P020107", "P02010702", new[] { Entry("P02010701", "1"), Entry("P02010702", "2") });
            document.AddListItem("F0103", "F010302", new[] { Entry("F010301", ""), Entry("F010302", ""), Entry("F01030201", "0:00:00") });

            Assert.Equal(
                new[] { "P0201", "P02010701", "P02010702", "P020108", "F0103", "F010301", "F01030101", "F010302", "F01030201", "F0105" },
                document.Entries.Select(e => e.Id));
            Assert.Equal("2", document.Entries.Single(e => e.Id == "P02010702").Value);

            document.RemoveListItem("F010301");

            Assert.DoesNotContain(document.Entries, e => e.Id.StartsWith("F010301", System.StringComparison.Ordinal));
            Assert.Equal("F010302", DeviceIds.Item("F0103", 2));
            Assert.Equal("P0201070A", DeviceIds.Item("P020107", 10));
        }

        [Fact]
        public void SetValues_AndClone_AreIndependent()
        {
            var document = Document(("M0101", "Al"), ("M0103", "2.7"));
            var copy = document.Clone();

            copy.SetValues(new Dictionary<string, string> { ["M0101"] = "Gold" });

            Assert.Equal("Al", document.Entries[0].Value);
            Assert.Equal("Gold", copy.Entries[0].Value);
            Assert.Equal(document.Recipe.DeviceName, copy.Recipe.DeviceName);
        }

        [Theory]
        [InlineData(CommandActions.Add, "M03", new[] { "M01", "M03" }, "", "")]
        [InlineData(CommandActions.Delete, "M01", new string[0], "", "")]
        [InlineData(CommandActions.Add, "M03", new[] { "M01" }, "M03", "")]
        [InlineData(CommandActions.Delete, "M01", new[] { "M01", "M05" }, "", "M01,M05")]
        public void CompareObjectChange(CommandActions action, string objectId, string[] after, string missing, string extra)
        {
            var (m, e) = RecipeDocument.CompareObjectChange(action, objectId, new HashSet<string> { "M01" }, new HashSet<string>(after));

            Assert.Equal(missing, string.Join(",", m));
            Assert.Equal(extra, string.Join(",", e));
        }

        private static RecipeDocument Document(params (string Id, string Value)[] entries)
        {
            var recipe = new InstrumentRecipe { DeviceName = "IMC300" };
            recipe.Entries.AddRange(entries.Select(e => Entry(e.Id, e.Value)));
            return new RecipeDocument(recipe);
        }

        private static RecipeEntry Entry(string id, string value) =>
            new RecipeEntry { Id = id, Value = value, Description = id.StartsWith("P020107") ? "Layer 1" : id };
    }
}
