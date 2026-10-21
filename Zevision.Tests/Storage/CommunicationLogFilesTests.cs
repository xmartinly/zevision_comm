using System;
using System.IO;
using Xunit;
using Zevision.Storage;

namespace Zevision.Tests
{
    /// <summary>
    /// 通讯数据文件的命名:设备序列号 (G06)、日期、时间到分钟,如 <c>XXXXXXXX_20261021_1337.txt</c>。
    /// </summary>
    public sealed class CommunicationLogFilesTests : IDisposable
    {
        private static readonly DateTime Time = new DateTime(2026, 10, 21, 13, 37, 45);

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "zevision_capture_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }

        [Theory]
        [InlineData("XXXXXXXX", "XXXXXXXX_20261021_1337.txt")]
        [InlineData(" 12345 ", "12345_20261021_1337.txt")]
        [InlineData("AB/12:3 4", "AB_12_3_4_20261021_1337.txt")]
        [InlineData(null, "unknown_20261021_1337.txt")]
        [InlineData("", "unknown_20261021_1337.txt")]
        [InlineData("   ", "unknown_20261021_1337.txt")]
        public void FileName_SerialNumberDateAndMinute(string? serialNumber, string expected)
        {
            Assert.Equal(expected, CommunicationLogFiles.FileName(serialNumber, Time));
        }

        [Fact]
        public void FileName_LongSerialNumber_IsCut()
        {
            Assert.Equal(new string('A', 32) + "_20261021_1337.txt", CommunicationLogFiles.FileName(new string('A', 40), Time));
        }

        [Fact]
        public void Create_CreatesFileNamedAfterSerialNumberInFolder()
        {
            var opened = new CommunicationLogFiles(new FakeErrorLog(), () => _dir).Create("XXXXXXXX");

            Assert.True(opened.IsSuccess, opened.Error);
            using (var recorder = opened.Value)
            {
                Assert.Equal(_dir, Path.GetDirectoryName(recorder.Path));
                Assert.Matches(@"^XXXXXXXX_\d{8}_\d{4}\.txt$", Path.GetFileName(recorder.Path));
                Assert.True(File.Exists(recorder.Path));
            }
        }
    }
}
