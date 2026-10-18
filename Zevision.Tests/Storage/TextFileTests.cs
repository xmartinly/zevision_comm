using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Zevision.Core.Localization;
using Zevision.Storage;

namespace Zevision.Tests
{
    /// <summary>读取程序旁边的翻译文件 Texts.json。</summary>
    public sealed class TextFileTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "zevision_texts_" + Guid.NewGuid().ToString("N") + ".json");

        public void Dispose() => File.Delete(_path);

        [Fact]
        public void ShippedFile_IsNextToTheProgram()
        {
            // Zevision.Core 项目把 Texts.json 复制到输出目录,引用它的程序 (和测试) 的输出目录中也有。
            var texts = TextFile.Load(TextFile.DefaultPath);

            Assert.True(texts.IsSuccess, texts.Error);
            Assert.Equal(texts.Value.Keys.OrderBy(k => k, StringComparer.Ordinal), TextCatalog.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }

        [Fact]
        public void Load_Utf8WithBom()
        {
            File.WriteAllText(_path, "{ \"NotConnected\": { \"zh\": \"未连接。\", \"en\": \"Not connected.\" } }", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var texts = TextFile.Load(_path);

            Assert.True(texts.IsSuccess, texts.Error);
            Assert.Equal(new LocalizedText("未连接。", "Not connected."), texts.Value["NotConnected"]);
        }

        [Fact]
        public void Load_MissingFile_Fails()
        {
            Assert.False(TextFile.Load(_path).IsSuccess);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{ }")]
        [InlineData("{ \"NotConnected\": { \"zh\": \"未连接。\" } }")]
        [InlineData("{ \"NotConnected\": null }")]
        [InlineData("{ \"NotConnected\": { \"zh\": \"a\", \"en\": \"b\" }, \"NotConnected\": { \"zh\": \"c\", \"en\": \"d\" } }")] // 重复的键
        public void Load_InvalidContent_Fails(string content)
        {
            File.WriteAllText(_path, content, new UTF8Encoding(false));

            var texts = TextFile.Load(_path);

            Assert.False(texts.IsSuccess);
            Assert.False(string.IsNullOrEmpty(texts.Error));
        }
    }
}
