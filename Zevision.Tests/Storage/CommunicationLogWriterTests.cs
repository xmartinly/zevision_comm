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
    /// 通讯数据文件 (<c>logs\capture_*.txt</c>):内容先缓存在内存中,超过 50 KB、断开连接 (<c>CloseAsync</c>)
    /// 或程序退出 (<c>Dispose</c>) 时写入,顺序不变。
    /// </summary>
    public sealed class CommunicationLogWriterTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "zevision_capture_" + Guid.NewGuid().ToString("N") + ".txt");

        public void Dispose() => File.Delete(_path);

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
