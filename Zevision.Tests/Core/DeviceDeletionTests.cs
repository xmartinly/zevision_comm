using System.Collections.Generic;
using System.Linq;
using Xunit;
using Zevision.Core.Configuration;
using Zevision.Core.Design;

namespace Zevision.Tests
{
    /// <summary>
    /// 设备上一起删除的对象 (<see cref="DeviceDeletion"/>)。删除材料:同名材料、引用它们的膜系、用到这些膜系的膜层 (不论名称),
    /// 工艺用到这些膜层时不能删除。删除工艺:同名工艺和只被它们使用的膜层,其他工艺也用的膜层保留;活动工艺、链接工艺引用时不能删除。
    /// </summary>
    public class DeviceDeletionTests
    {
        private static List<RecipeEntry> Entries(params (string Id, string Value)[] items) =>
            items.Select(e => new RecipeEntry { Id = e.Id, Value = e.Value, Description = string.Empty }).ToList();

        /// <summary>材料 Al 有两个 (M01、M03);F02 名为 Al 但用 SiO2;F04 名称不同但用 M03;L03 用 F02 和 F04。</summary>
        private static List<RecipeEntry> Materials(params (string Id, string Value)[] extra) =>
            Entries(new[]
            {
                ("M0101", "Al"),
                ("M0201", "SiO2"),
                ("M0301", "Al"),
                ("F0101", "Al"), ("F0102", "1"),
                ("F0201", "Al"), ("F0202", "2"),
                ("F0301", "SiO2"), ("F0302", "2"),
                ("F0401", "Al_hot"), ("F0402", "3"),
                ("L0101", "L_Al"), ("L01020101", "1"),
                ("L0201", "L_Al"),                                         // 空的膜系列表
                ("L0301", "Mixed"), ("L03020101", "2"), ("L03020201", "4"), // 也用到 F04
                ("L0401", "L_SiO2"), ("L04020101", "3"),
                ("P01", "1"),
                ("P020101", "P1"), ("P02010701", "4"),
            }.Concat(extra).ToArray());

        /// <summary>工艺 P1 有两个 (P0201、P0203);L02 也被 P0202 使用;活动工艺是 P0202。</summary>
        private static List<RecipeEntry> Processes(params (string Id, string Value)[] extra) =>
            Entries(new[]
            {
                ("L0101", "L_Al"), ("L0201", "L_SiO2"), ("L0301", "L_Au"),
                ("P01", "2"),
                ("P020101", "P1"), ("P02010701", "1"), ("P02010702", "2"), ("P02010703", "1"),
                ("P020201", "Shared"), ("P02020701", "2"),
                ("P020301", "P1"), ("P02030701", "3"), ("P02030702", "9"), // 膜层 9 不存在
            }.Concat(extra).ToArray());

        [Fact]
        public void Material_FilmsOfAnyNameAndLayersUsingThem_LayersFirst()
        {
            var deletion = DeviceDeletion.ForMaterialNamed(" Al ", Materials());

            // 膜层、膜系、材料 (引用别人的在前);组内编号大的在前
            Assert.Equal(new[] { "L03 L Mixed", "L01 L L_Al", "F04 F Al_hot", "F01 F Al", "M03 M Al", "M01 M Al" }, deletion.Objects.Select(o => o.Id + " " + o.Group + " " + o.Name));
            Assert.All(deletion.Objects, o => Assert.False(o.IsNew));
            Assert.Empty(deletion.Kept);
            Assert.False(deletion.IsBlocked); // 一起删除的对象之间的引用不算
        }

        [Fact]
        public void Material_UsedByAProcessOrSource_IsBlocked()
        {
            var deletion = DeviceDeletion.ForMaterialNamed("Al", Materials(("P020201", "P2"), ("P02020701", "3"), ("S010901", "1")));

            Assert.True(deletion.IsBlocked);
            Assert.Equal(new[] { "工艺 P0202 P2: P02020701 = 3", "坩埚 1 · 材料 ID S010901 = 1" }, deletion.References);
        }

        [Fact]
        public void Material_ById_OnlyThatOne_NameIsCaseSensitive()
        {
            Assert.Equal(new[] { "L03", "F04", "M03" }, DeviceDeletion.ForMaterials(new[] { "M03" }, Materials()).Objects.Select(o => o.Id));
            Assert.Empty(DeviceDeletion.ForMaterialNamed("AL", Materials()).Objects);
            Assert.Empty(DeviceDeletion.ForMaterials(new[] { "M09" }, Materials()).Objects);
        }

        [Fact]
        public void Process_SameNamed_DeletesLayersOnlyTheyUse_KeepsShared()
        {
            var deletion = DeviceDeletion.ForProcessNamed("P1", Processes());

            Assert.Equal(new[] { "P0203 P P1", "P0201 P P1", "L03 L L_Au", "L01 L L_Al" }, deletion.Objects.Select(o => o.Id + " " + o.Group + " " + o.Name));
            Assert.Equal(new[] { "L02" }, deletion.Kept.Select(o => o.Id));
            Assert.False(deletion.IsBlocked);
        }

        [Fact]
        public void Process_ActiveOrLinked_IsBlocked()
        {
            var active = DeviceDeletion.ForProcessNamed("Shared", Processes());
            var linked = DeviceDeletion.ForProcesses(new[] { "P0203" }, Processes(("P02010A", "3")));
            var self = DeviceDeletion.ForProcesses(new[] { "P0203" }, Processes(("P02030A", "3")));

            Assert.Equal(new[] { "设置活动工艺 P01 = 2" }, active.References);
            Assert.Equal(new[] { "P0202" }, active.Objects.Select(o => o.Id)); // L02 也被 P0201 使用
            Assert.Equal(new[] { "工艺 P0201 P1: P02010A = 3" }, linked.References);
            Assert.False(self.IsBlocked); // 自己的设置项不算
        }

        [Fact]
        public void Process_NotOnTheDevice_NothingToDelete()
        {
            var deletion = DeviceDeletion.ForProcessNamed("P9", Processes());

            Assert.Empty(deletion.Objects);
            Assert.Empty(deletion.Kept);
            Assert.False(deletion.IsBlocked);
        }
    }
}
