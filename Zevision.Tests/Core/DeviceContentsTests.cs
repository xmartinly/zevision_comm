using System.Collections.Generic;
using System.Linq;
using Xunit;
using Zevision.Core.Configuration;
using Zevision.Core.Design;

namespace Zevision.Tests
{
    /// <summary>
    /// 工艺编辑"设备"页的内容 (<see cref="DeviceContents"/>):从帮助表的设置项中取出材料、膜系、膜层和工艺;
    /// 列表按条目 ID 取,不用数量项,所以分组帮助挑出的可恢复项也可以。
    /// </summary>
    public class DeviceContentsTests
    {
        private static RecipeEntry E(string id, string value) => new RecipeEntry { Id = id, Value = value, Description = string.Empty };

        [Fact]
        public void From_RealCapture_ProcessesLayersFilmsAndMaterials()
        {
            var contents = DeviceContents.From(DesignDevice.EntriesOf(DesignBuilderTests.CaptureSnapshot()));

            Assert.Equal(12, contents.Materials.Count);
            Assert.Equal(11, contents.Films.Count);
            Assert.Equal(9, contents.Layers.Count);
            Assert.Equal(new[] { "P0201", "P0202", "P0203", "P0204", "P0205", "P0206", "P0207", "P0208", "P0242", "P02FA" }, contents.Processes.Select(p => p.Id));

            var p1 = contents.Processes[0];
            Assert.Equal("P1", p1.Name);
            Assert.Equal(new[] { 1 }, p1.Layers);
            Assert.Equal(new[] { 31, 32, 32, 32, 32, 32, 33, 33, 32, 32, 32, 32 }, contents.Processes.Single(p => p.Id == "P0242").Layers);
            Assert.Null(contents.FindLayer(31)); // 实机上工艺引用不存在的膜层

            var layer = contents.FindLayer(1)!;
            Assert.Equal(("L01", "L1"), (layer.Id, layer.Name));
            Assert.Equal(new DeviceLayerFilm(1, "0.000", "1.030"), layer.Films.Single());
            Assert.Equal(new DeviceFilm("F01", "L1", 1), contents.FindFilm(1));
            Assert.Equal(new DeviceFilm("F20", "Film 32", 16), contents.FindFilm(0x20));
            Assert.Equal(0, contents.FindFilm(7)!.Material); // 没有选择材料

            var al = contents.MaterialsNamed(" Al ").Single();
            Assert.Equal(("M10", "2.700", "1.080"), (al.Id, al.Density, al.ZRatio));
            Assert.Equal(new[] { "F20" }, contents.FilmsOf(al).Select(f => f.Id));
            Assert.Empty(contents.MaterialsNamed("al")); // 区分大小写
            Assert.Equal(new[] { "P0201" }, contents.ProcessesNamed("P1").Select(p => p.Id));
            Assert.Empty(contents.ProcessesNamed(" "));
        }

        [Fact]
        public void From_GroupEntriesWithoutCounts_ListsByItemIds()
        {
            var contents = DeviceContents.From(new List<RecipeEntry>
            {
                E("L0201", "L_Al"),
                E("L02020101", "3"),
                E("L02020102", "1.0"),
                E("L02020103", "0.100"),
                E("L020201060101", "0.050"), // 速率斜坡的项,不是膜系
                E("L02020201", "4"),
                E("L02020202", "2.0"),
                E("L02020203", "0.200"),
                E("P020501", "P5"),
                E("P02050702", "2"),
                E("P02050701", "7"),
                E("P02050700", string.Empty), // 增删项
                E("P01", "5"),               // 不是对象
            });

            Assert.Equal(new[] { new DeviceLayerFilm(3, "1.0", "0.100"), new DeviceLayerFilm(4, "2.0", "0.200") }, contents.Layers.Single().Films);
            var process = contents.Processes.Single();
            Assert.Equal(("P0205", "P5"), (process.Id, process.Name));
            Assert.Equal(new[] { 7, 2 }, process.Layers);
            Assert.Empty(contents.Materials);
        }
    }
}
