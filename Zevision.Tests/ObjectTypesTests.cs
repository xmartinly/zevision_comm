using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;

namespace Zevision.Tests
{
    public class ObjectTypesTests
    {
        [Theory]
        [InlineData("M0301", "M03", "材料")]
        [InlineData("F0A0901", "F0A", "膜系")]
        [InlineData("L01020101", "L01", "膜层")]
        [InlineData("P020101", "P0201", "工艺")]
        public void TryGetObject_MapsSettingToOwningObject(string id, string objectId, string kind)
        {
            Assert.True(ObjectTypes.TryGetObject(id, out var type, out var owner));
            Assert.Equal(objectId, owner);
            Assert.Equal(kind, type.Kind);
        }

        [Theory]
        [InlineData("P01")]      // 选择当前工艺,不是对象
        [InlineData("N0101")]    // 传感器数量固定,不能创建
        [InlineData("M00")]
        [InlineData("MFE")]
        [InlineData("M0001")]    // 00 是增删指令,不是对象编号
        [InlineData("G0E01")]
        public void TryGetObject_NotAnObject(string id)
        {
            Assert.False(ObjectTypes.TryGetObject(id, out _, out _));
        }

        [Theory]
        [InlineData("M03", 'M')]
        [InlineData("P0201", 'P')]
        [InlineData("P01", null)]
        [InlineData("M0301", null)]
        [InlineData("N01", null)]
        public void OfObjectId(string objectId, char? group)
        {
            Assert.Equal(group, ObjectTypes.OfObjectId(objectId)?.Group);
        }

        [Fact]
        public void NextFreeId_UsesPrefix()
        {
            Assert.Equal("M02", ObjectTypes.Material.NextFreeId(new HashSet<string> { "M01", "M03" }));
            Assert.Equal("P0201", ObjectTypes.Process.NextFreeId(new HashSet<string>()));
            var all = new HashSet<string>(Enumerable.Range(1, 0xFD).Select(n => DeviceIds.Item("F", n)));
            Assert.Null(ObjectTypes.Film.NextFreeId(all));
        }

        [Fact]
        public void Kind_FollowsLanguageAtCallTime()
        {
            using (Lang.Override(UiLanguage.English))
            {
                Assert.Equal("Layer", ObjectTypes.Layer.Kind);
            }

            Assert.Equal("膜层", ObjectTypes.Layer.Kind);
        }

        [Fact]
        public void ListPattern_IsCommandSetPattern()
        {
            Assert.Equal(new[] { "F**03", "F**05" }, ObjectTypes.Film.Lists.Select(ObjectTypes.Film.ListPattern));
            Assert.Equal("P02**07", ObjectTypes.Process.ListPattern(ObjectTypes.Process.Lists.Single()));
        }

        [Fact]
        public void DeviceIds_ComposeAndParse()
        {
            Assert.Equal("F010302", DeviceIds.Item("F0103", 2));
            Assert.Equal("P0201070A", DeviceIds.Item("P020107", 10));
            Assert.Equal("F0103FE", DeviceIds.CountOf("F0103"));
            Assert.Equal("F010300", DeviceIds.AppendSlotOf("F0103"));
            Assert.Equal("M0301", DeviceIds.NameOf("M03"));
            Assert.Equal(10, DeviceIds.LastNumber("M0A"));
            Assert.False(DeviceIds.IsItemNumber("00"));
            Assert.False(DeviceIds.IsItemNumber("FE"));
            Assert.True(DeviceIds.IsItemNumber("01"));
            Assert.True(DeviceIds.TryParseCount("3", out var count) && count == 3);
            Assert.False(DeviceIds.TryParseCount("-1", out _));
            Assert.True(DeviceIds.IsWellFormed("P020107"));
            Assert.False(DeviceIds.IsWellFormed("P0201a"));
            Assert.False(DeviceIds.IsWellFormed(null));
            Assert.Throws<ArgumentNullException>(() => DeviceIds.LastNumber(null!));
        }
    }
}
