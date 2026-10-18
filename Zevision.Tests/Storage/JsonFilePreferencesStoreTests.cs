using System;
using System.IO;
using Xunit;
using Zevision.Core.Settings;
using Zevision.Storage;

namespace Zevision.Tests
{
    public sealed class JsonFilePreferencesStoreTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "zevision_prefs_" + Guid.NewGuid().ToString("N"));

        private string SettingsPath => Path.Combine(_directory, "settings.json");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Fact]
        public void SaveThenLoad_RoundTrips()
        {
            var store = new JsonFilePreferencesStore(SettingsPath);
            var saved = new ConnectionPreferences
            {
                PortName = "COM3",
                BaudRate = 57600,
                ResponseTimeoutMs = 2000,
                ChecksumEnabled = true,
                LengthEnabled = true,
                SaveCommunicationData = true,
                EditSource = "File",
            };

            Assert.True(store.Save(saved).IsSuccess);
            var result = store.Load();

            Assert.True(result.IsSuccess);
            var loaded = result.Value;
            Assert.Equal("COM3", loaded.PortName);
            Assert.Equal(57600, loaded.BaudRate);
            Assert.Equal(2000, loaded.ResponseTimeoutMs);
            Assert.True(loaded.ChecksumEnabled && loaded.LengthEnabled && loaded.SaveCommunicationData);
            Assert.Equal("File", loaded.EditSource);
            Assert.Contains("\"portName\": \"COM3\"", File.ReadAllText(SettingsPath)); // 可读的缩进 JSON
            Assert.Single(Directory.GetFiles(_directory)); // 没有残留的临时文件
        }

        [Fact]
        public void Load_NoFile_ReturnsDefaults()
        {
            var result = new JsonFilePreferencesStore(SettingsPath).Load();

            Assert.True(result.IsSuccess);
            Assert.Equal(115200, result.Value.BaudRate);
            Assert.Equal(1000, result.Value.ResponseTimeoutMs);
        }

        [Fact]
        public void Load_Utf8WithBom()
        {
            // 用记事本"另存为 UTF-8"会加上 BOM;以前读取失败,设置 (包括界面语言) 全部回到默认值。
            Directory.CreateDirectory(_directory);
            File.WriteAllText(SettingsPath, "{\"portName\": \"COM3\"}", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var result = new JsonFilePreferencesStore(SettingsPath).Load();

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal("COM3", result.Value.PortName);
        }

        [Fact]
        public void Load_MissingFields_KeepDefaults()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(SettingsPath, "{\"portName\": \"COM3\"}");

            var result = new JsonFilePreferencesStore(SettingsPath).Load();

            Assert.True(result.IsSuccess);
            Assert.Equal("COM3", result.Value.PortName);
            Assert.Equal(115200, result.Value.BaudRate);
            Assert.Equal(1000, result.Value.ResponseTimeoutMs);
            Assert.Equal(ConnectionPreferences.DefaultEditSource, result.Value.EditSource); // 旧文件没有该字段
        }

        [Fact]
        public void Load_RemovedField_IsIgnored()
        {
            // 早期版本保存过 tcpPort (现在端口固定 2101):旧文件中多余的字段直接忽略。
            Directory.CreateDirectory(_directory);
            File.WriteAllText(SettingsPath, "{\"connectionType\": \"Tcp\", \"tcpHost\": \"10.0.0.5\", \"tcpPort\": 3000}");

            var result = new JsonFilePreferencesStore(SettingsPath).Load();

            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal("Tcp", result.Value.ConnectionType);
            Assert.Equal("10.0.0.5", result.Value.TcpHost);
        }

        [Fact]
        public void Load_CorruptFile_Fails()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(SettingsPath, "{ not json");

            var result = new JsonFilePreferencesStore(SettingsPath).Load();

            Assert.False(result.IsSuccess);
            Assert.Contains("默认值", result.Error);
        }

        [Fact]
        public void Save_CreatesDirectoryAndOverwrites()
        {
            var store = new JsonFilePreferencesStore(SettingsPath);
            store.Save(new ConnectionPreferences { PortName = "COM1" });

            store.Save(new ConnectionPreferences { PortName = "COM2" });

            Assert.Equal("COM2", store.Load().Value.PortName);
        }
    }
}
