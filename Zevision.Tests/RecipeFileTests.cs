using System;
using System.IO;
using System.Text;
using Xunit;
using Zevision.Models;
using Zevision.Services;

namespace Zevision.Tests
{
    public sealed class RecipeFileTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "zevision_recipe_" + Guid.NewGuid().ToString("N"));

        public RecipeFileTests()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }

        [Fact]
        public void SaveThenLoad_RoundTrips()
        {
            var path = Path.Combine(_directory, "a.json");

            Assert.True(RecipeFile.Save(NewRecipe(), path).IsSuccess);
            var loaded = RecipeFile.Load(path);

            Assert.True(loaded.IsSuccess, loaded.Error);
            Assert.Equal("IMC300", loaded.Value.DeviceName);
            Assert.Equal("1.8.11.202606101229", loaded.Value.FirmwareVersion);
            Assert.Equal(2, loaded.Value.Entries.Count);
            Assert.Equal("Film 10", loaded.Value.Entries[0].Value);
            Assert.False(File.Exists(path + ".tmp"));

            var json = File.ReadAllText(path);
            Assert.Contains("\"formatVersion\": 2", json); // 缩进的可读 JSON
            Assert.Matches("\"md5\": \"[0-9a-f]{32}\"", json);
        }

        [Fact]
        public void ComputeMd5_MatchesDocumentedCanonicalEncoding()
        {
            // 参考值由独立实现 (Python hashlib) 按文档中的编码计算:
            // "1:2\n6:IMC300\n0:\n0:\n0:\n0:\n1:1\n5:M0101\n2:Al\n4:Name\n"
            var recipe = new InstrumentRecipe { DeviceName = "IMC300" };
            recipe.Entries.Add(new RecipeEntry { Id = "M0101", Value = "Al", Description = "Name" });

            Assert.Equal("abacbdb4e4f5a029989895590460deb2", RecipeFile.ComputeMd5(recipe).Value);
        }

        [Fact]
        public void ComputeMd5_NonAsciiUsesUtf8()
        {
            var recipe = new InstrumentRecipe { DeviceName = "IMC300" };
            recipe.Entries.Add(new RecipeEntry { Id = "M0101", Value = "Å/s", Description = "Name" });

            // 参考值: md5("1:2\n6:IMC300\n0:\n0:\n0:\n0:\n1:1\n5:M0101\n3:Å/s\n4:Name\n" 的 UTF-8 编码)
            Assert.Equal("d20bef04e3764aa4decb7895b62f270a", RecipeFile.ComputeMd5(recipe).Value);
        }

        [Fact]
        public void ComputeMd5_IsUnambiguousAcrossFieldBoundaries()
        {
            var a = new InstrumentRecipe { DeviceName = "IMC300" };
            a.Entries.Add(new RecipeEntry { Id = "M0101", Value = "a\n1:b", Description = string.Empty });
            var b = new InstrumentRecipe { DeviceName = "IMC300" };
            b.Entries.Add(new RecipeEntry { Id = "M0101", Value = "a", Description = "b" });

            Assert.NotEqual(RecipeFile.ComputeMd5(a).Value, RecipeFile.ComputeMd5(b).Value);
        }

        [Fact]
        public void Load_ValueEditedAfterSave_FailsMd5()
        {
            var path = SaveSample();
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"5.890\"", "\"5.891\""));

            var loaded = RecipeFile.Load(path);

            Assert.False(loaded.IsSuccess);
            Assert.Contains("MD5 校验失败", loaded.Error);
        }

        [Fact]
        public void Load_EntryRemovedAfterSave_FailsMd5()
        {
            // 删除一个条目后直接写回 (保留原 md5),模拟手工删行。
            var path = SaveSample();
            var recipe = RecipeFile.Load(path).Value;
            recipe.Entries.RemoveAt(1);
            using (var stream = File.Create(path))
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(InstrumentRecipe)).WriteObject(stream, recipe);
            }

            var loaded = RecipeFile.Load(path);

            Assert.False(loaded.IsSuccess);
            Assert.Contains("MD5 校验失败", loaded.Error);
        }

        [Fact]
        public void Load_Md5Edited_Fails()
        {
            var path = SaveSample();
            var recipe = RecipeFile.Load(path).Value;
            var flipped = (recipe.Md5[0] == '0' ? '1' : '0') + recipe.Md5.Substring(1);
            File.WriteAllText(path, File.ReadAllText(path).Replace(recipe.Md5, flipped));

            Assert.Contains("MD5 校验失败", RecipeFile.Load(path).Error);
        }

        [Fact]
        public void Load_Md5Missing_Fails()
        {
            var path = SaveSample();
            var recipe = RecipeFile.Load(path).Value;
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"md5\": \"" + recipe.Md5 + "\",", string.Empty));

            Assert.Contains("缺少 MD5", RecipeFile.Load(path).Error);
        }

        [Fact]
        public void Load_ReformattedJsonWithSameData_StillPasses()
        {
            // 只改变缩进和换行 (例如被编辑器重新格式化),数据未变,不应视为修改。
            var path = SaveSample();
            var compact = new StringBuilder();
            var inString = false;
            foreach (var c in File.ReadAllText(path))
            {
                if (c == '"')
                {
                    inString = !inString;
                }

                if (inString || !char.IsWhiteSpace(c))
                {
                    compact.Append(c);
                }
            }

            File.WriteAllText(path, compact.ToString());

            var loaded = RecipeFile.Load(path);

            Assert.True(loaded.IsSuccess, loaded.Error);
        }

        [Fact]
        public void Load_Version1File_AsksToSaveAgain()
        {
            var path = Path.Combine(_directory, "old.json");
            File.WriteAllText(path, "{\"formatVersion\": 1, \"deviceName\": \"IMC300\", \"entries\": [{\"id\": \"M0101\", \"value\": \"Al\", \"description\": \"Name\"}]}");

            var loaded = RecipeFile.Load(path);

            Assert.False(loaded.IsSuccess);
            Assert.Contains("重新\"保存配置\"", loaded.Error);
        }

        [Fact]
        public void Save_OverwritesExistingFile()
        {
            var path = Path.Combine(_directory, "a.json");
            File.WriteAllText(path, "old");

            Assert.True(RecipeFile.Save(NewRecipe(), path).IsSuccess);

            Assert.True(RecipeFile.Load(path).IsSuccess);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{\"formatVersion\": 99, \"deviceName\": \"IMC300\", \"entries\": [{\"id\": \"M0101\", \"value\": \"Al\"}]}")]
        [InlineData("{\"formatVersion\": 2, \"deviceName\": \"IMC300\", \"md5\": \"zz\", \"entries\": [{\"id\": \"M0101\", \"value\": \"Al\"}]}")]
        public void Load_InvalidContent_Fails(string json)
        {
            var path = Path.Combine(_directory, "bad.json");
            File.WriteAllText(path, json);

            var loaded = RecipeFile.Load(path);

            Assert.False(loaded.IsSuccess);
            Assert.NotNull(loaded.Error);
        }

        [Fact]
        public void Load_ValidMd5ButInvalidId_Fails()
        {
            // 校验值正确 (例如由程序自身写出) 时,内容规则仍然要检查。
            var recipe = NewRecipe();
            recipe.Entries[0].Id = "UM0101";
            var path = Path.Combine(_directory, "badid.json");
            Assert.True(RecipeFile.Save(recipe, path).IsSuccess);

            Assert.Contains("ID 不合法", RecipeFile.Load(path).Error);
        }

        [Fact]
        public void Load_MissingFile_Fails()
        {
            Assert.False(RecipeFile.Load(Path.Combine(_directory, "missing.json")).IsSuccess);
        }

        private static InstrumentRecipe NewRecipe()
        {
            var recipe = new InstrumentRecipe
            {
                DeviceName = "IMC300",
                ModelNumber = "IMC",
                FirmwareVersion = "1.8.11.202606101229",
                SerialNumber = "XXXXXXXX",
                SavedAt = "2026-10-15T16:30:00.0000000+08:00",
            };
            recipe.Entries.Add(new RecipeEntry { Id = "F0A01", Value = "Film 10", Description = "Name" });
            recipe.Entries.Add(new RecipeEntry { Id = "M0103", Value = "5.890", Description = "Density (g/cm^3)" });
            return recipe;
        }

        private string SaveSample()
        {
            var path = Path.Combine(_directory, "sample.json");
            Assert.True(RecipeFile.Save(NewRecipe(), path).IsSuccess);
            return path;
        }
    }
}
