using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Zevision.Storage;

namespace Zevision.Tests
{
    public sealed class FileErrorLogTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "zevision_errlog_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Fact]
        public void Write_AppendsAndCreatesDirectory()
        {
            var path = Path.Combine(_directory, "logs", "error.txt");
            var log = new FileErrorLog(path);

            log.Write("运行", "第一条");
            log.Write("运行", "第二条");

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} \[运行\] 第一条$", lines[0]);
            Assert.EndsWith("[运行] 第二条", lines[1]);
        }

        [Fact]
        public void Write_SessionHeader_WrittenOnceBeforeFirstEntry()
        {
            var path = Path.Combine(_directory, "error.txt");
            var log = new FileErrorLog(path, "Zevision 0.9.0");

            log.Write("运行", "第一条");
            log.Write("运行", "第二条");

            var lines = File.ReadAllLines(path);
            Assert.Equal(3, lines.Length);
            Assert.Equal("==== Zevision 0.9.0 ====", lines[0]);
            Assert.EndsWith("第一条", lines[1]);
        }

        [Fact]
        public void Write_KeepsExistingContent()
        {
            var path = Path.Combine(_directory, "error.txt");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(path, "旧内容" + Environment.NewLine);

            new FileErrorLog(path).Write("运行", "新内容");

            var lines = File.ReadAllLines(path);
            Assert.Equal("旧内容", lines[0]);
            Assert.EndsWith("新内容", lines[1]);
        }

        [Fact]
        public void Write_WithException_IncludesTypeAndStackTrace()
        {
            var path = Path.Combine(_directory, "error.txt");
            Exception captured;
            try
            {
                throw new InvalidOperationException("测试异常");
            }
            catch (InvalidOperationException ex)
            {
                captured = ex;
            }

            new FileErrorLog(path).Write("操作异常", captured.Message, captured);

            var text = File.ReadAllText(path);
            Assert.Contains("System.InvalidOperationException: 测试异常", text);
            Assert.Contains(nameof(Write_WithException_IncludesTypeAndStackTrace), text); // 堆栈
        }

        [Fact]
        public void Write_LongMultilineMessage_IsTruncatedToOneLine()
        {
            var path = Path.Combine(_directory, "error.txt");

            new FileErrorLog(path).Write("运行", "a\r\nb\n" + new string('x', 2000));

            var line = Assert.Single(File.ReadAllLines(path));
            Assert.Contains("a b ", line);
            Assert.EndsWith("…(已截断)", line);
            Assert.True(line.Length < FileErrorLog.MaxMessageLength + 60);
        }

        [Fact]
        public void Write_OverSizeLimit_RotatesToNumberedFile()
        {
            var path = Path.Combine(_directory, "error.txt");
            var log = new FileErrorLog(path, maxFileBytes: 100);

            log.Write("运行", new string('a', 120));
            log.Write("运行", "新文件中的第一条");

            Assert.Contains(new string('a', 120), File.ReadAllText(Path.Combine(_directory, "error.1.txt")));
            Assert.EndsWith("新文件中的第一条", Assert.Single(File.ReadAllLines(path)));
        }

        [Fact]
        public void Write_RotationFails_StillAppends()
        {
            // error.1.txt 被其他程序独占 (如编辑器) 时无法轮转,但之后的错误不能丢。
            var path = Path.Combine(_directory, "error.txt");
            var log = new FileErrorLog(path, maxFileBytes: 100);
            log.Write("运行", new string('a', 120));

            using (new FileStream(Path.Combine(_directory, "error.1.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                log.Write("运行", "轮转失败后的条目");
            }

            Assert.Contains("轮转失败后的条目", File.ReadAllText(path));
        }

        [Fact]
        public void Write_ConcurrentWriters_NoLostLines()
        {
            var path = Path.Combine(_directory, "error.txt");
            var log = new FileErrorLog(path);

            Parallel.For(0, 200, i => log.Write("并发", "条目 " + i));

            var lines = File.ReadAllLines(path);
            Assert.Equal(200, lines.Length);
            Assert.Equal(200, lines.Select(l => l.Substring(l.IndexOf("条目", StringComparison.Ordinal))).Distinct().Count());
        }

        [Fact]
        public void Write_UnwritablePath_DoesNotThrow()
        {
            Directory.CreateDirectory(_directory);
            var blocker = Path.Combine(_directory, "blocker");
            File.WriteAllText(blocker, string.Empty);

            // 路径中的"目录"实际是文件,无法创建:写入应静默失败。
            new FileErrorLog(Path.Combine(blocker, "error.txt")).Write("运行", "不会写入");
        }
    }
}
