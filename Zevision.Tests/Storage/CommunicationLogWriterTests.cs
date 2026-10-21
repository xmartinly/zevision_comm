using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Zevision.Storage;

namespace Zevision.Tests
{
    /// <summary>
    /// 通讯数据文件 (<c>logs\序列号_日期_时间.txt</c>):内容先缓存在内存中,超过 50 KB、断开连接 (<c>CloseAsync</c>)
    /// 或程序退出 (<c>Dispose</c>) 时写入,顺序不变;文件超过大小上限时续写到 <c>_1</c>、<c>_2</c>… 文件。
    /// </summary>
    public sealed class CommunicationLogWriterTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _path;

        public CommunicationLogWriterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "zevision_capture_" + Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_dir, "XXXXXXXX_20261021_1337.txt");
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }

        [Fact]
        public async Task Append_BelowLimit_StaysInMemoryUntilClosed()
        {
            var writer = CommunicationLogWriter.Create(_path, new FakeErrorLog());
            writer.Append(new[] { "a", "b" });
            await Task.Delay(50);

            Assert.Equal(string.Empty, ReadShared());

            await writer.CloseAsync();
            Assert.Equal(new[] { "a", "b" }, File.ReadAllLines(_path));
        }

        [Fact]
        public async Task Append_OverLimit_WritesInBackgroundInOrder()
        {
            var writer = CommunicationLogWriter.Create(_path, new FakeErrorLog());
            var line = new string('x', 1000);
            var count = CommunicationLogWriter.BufferLimit / 1000 + 1; // 超过 50 KB
            for (var i = 0; i < count; i++)
            {
                writer.Append(new[] { i + " " + line });
            }

            await WaitUntil(() => ReadShared().Length > 0);
            writer.Append(new[] { "tail" });
            Assert.DoesNotContain("tail", ReadShared());

            await writer.CloseAsync();
            var lines = File.ReadAllLines(_path);
            Assert.Equal(count + 1, lines.Length);
            Assert.Equal(Enumerable.Range(0, count).Select(i => i + " " + line).Concat(new[] { "tail" }), lines);
        }

        [Fact]
        public void Dispose_WritesSynchronouslyAndIgnoresLaterLines()
        {
            var writer = CommunicationLogWriter.Create(_path, new FakeErrorLog());
            writer.Append(new[] { "中文 Å/s" });

            writer.Dispose();
            writer.Append(new[] { "ignored" });
            writer.Dispose();

            Assert.Equal(new[] { "中文 Å/s" }, File.ReadAllLines(_path, Encoding.UTF8));
        }

        [Fact]
        public async Task Limit_CountsUtf8Bytes()
        {
            var writer = CommunicationLogWriter.Create(_path, new FakeErrorLog());
            var chinese = new string('中', 1000); // 3000 字节
            for (var i = 0; i < CommunicationLogWriter.BufferLimit / 3000 + 1; i++)
            {
                writer.Append(new[] { chinese });
            }

            await WaitUntil(() => ReadShared().Length > 0);
            await writer.CloseAsync();
        }

        [Fact]
        public void NumberedPath_AppendsNumberToFileName()
        {
            Assert.Equal(
                Path.Combine(_dir, "XXXXXXXX_20261021_1337_2.txt"),
                CommunicationLogWriter.NumberedPath(_path, 2));
        }

        [Fact]
        public async Task Write_OverFileLimit_ContinuesInNumberedFiles()
        {
            var writer = CommunicationLogWriter.Create(_path, new FakeErrorLog(), fileSizeLimit: 100 * 1024);
            var batches = Enumerable.Range(0, 3).Select(i => i + new string('x', 60 * 1024)).ToArray();
            foreach (var batch in batches)
            {
                writer.Append(new[] { batch }); // 每批超过 50 KB:各自一块
            }

            writer.Append(new[] { "tail" });
            await writer.CloseAsync();

            Assert.Equal(new[] { batches[0] }, File.ReadAllLines(_path));
            Assert.Equal(new[] { batches[1] }, File.ReadAllLines(Numbered(1)));
            Assert.Equal(new[] { batches[2], "tail" }, File.ReadAllLines(Numbered(2)));
            Assert.False(File.Exists(Numbered(3)));
            Assert.Equal(Numbered(2), writer.Path);
        }

        [Fact]
        public void Write_ChunkLargerThanLimit_GoesIntoEmptyFile()
        {
            var writer = CommunicationLogWriter.Create(_path, new FakeErrorLog(), fileSizeLimit: 1024);
            var line = new string('x', 60 * 1024);
            writer.Append(new[] { "a" + line });
            writer.Append(new[] { "b" + line });
            writer.Dispose();

            Assert.Equal(new[] { "a" + line }, File.ReadAllLines(_path));
            Assert.Equal(new[] { "b" + line }, File.ReadAllLines(Numbered(1)));
            Assert.False(File.Exists(Numbered(2)));
        }

        [Fact]
        public void Write_ExistingFullFiles_ContinuesInNextFileWithRoom()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_path, new string('0', 100));
            File.WriteAllText(Numbered(1), new string('1', 100));

            var line = new string('n', 60); // 100 + 62 字节 > 150
            var writer = CommunicationLogWriter.Create(_path, new FakeErrorLog(), fileSizeLimit: 150);
            writer.Append(new[] { line });
            writer.Dispose();

            Assert.Equal(new string('0', 100), File.ReadAllText(_path));
            Assert.Equal(new string('1', 100), File.ReadAllText(Numbered(1)));
            Assert.Equal(new[] { line }, File.ReadAllLines(Numbered(2)));
        }

        private string Numbered(int number) => Path.Combine(_dir, "XXXXXXXX_20261021_1337_" + number + ".txt");

        /// <summary>写入方打开文件时允许读取 (FileShare.Read),这里也要允许它继续写入。</summary>
        private string ReadShared()
        {
            using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("condition not met");
                }

                await Task.Delay(10);
            }
        }
    }
}
