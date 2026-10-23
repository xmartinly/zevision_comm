using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Zevision.Core;
using Zevision.Communication;
using Zevision.Storage;
using Zevision.ViewModels.Display;

namespace Zevision.Tests
{
    public class LogViewModelTests
    {
        /// <summary>按保存到文件的样子 (含换行) 为 <paramref name="bytes"/> 字节的一条记录。</summary>
        private static LogEntry EntryOfSize(int bytes, string tag) =>
            new LogEntry(DateTime.Now, LogKind.Rx, (tag + " " + new string('x', bytes)).Substring(0, bytes - 18 - Environment.NewLine.Length));

        private sealed class LineRecorder : ICommunicationRecorder
        {
            public List<string> Lines { get; } = new List<string>();

            public string Path => "test";

            public void Append(IReadOnlyList<string> lines) => Lines.AddRange(lines);

            public Task CloseAsync() => Task.CompletedTask;

            public void Dispose()
            {
            }
        }

        [Fact]
        public void Add_Error_WritesErrorLogOnce()
        {
            var errorLog = new FakeErrorLog();
            var log = new LogViewModel(errorLog);

            log.Add(LogKind.Error, "失败");
            log.Add(new LogEntry(DateTime.Now, LogKind.Error, "后续行", isContinuation: true));
            log.ReportException(new InvalidOperationException("意外"));
            log.Add(LogKind.Info, "信息");

            Assert.Equal(new[] { "失败", "意外" }, errorLog.Messages); // 意外异常只记一次 (带堆栈)
            Assert.Equal(4, log.Entries.Count);
            Assert.Equal("操作失败: 意外", log.Entries[2].Text);
        }

        [Fact]
        public void Add_OverLimit_TrimsOldestBatchWithSingleReset()
        {
            var log = new LogViewModel(new FakeErrorLog());
            for (var i = 0; i < LogViewModel.MaxEntries; i++)
            {
                log.Add(LogKind.Info, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            var resets = 0;
            log.Entries.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    resets++;
                }
            };

            log.Add(LogKind.Info, "最新");

            Assert.Equal(1, resets);
            Assert.Equal(LogViewModel.MaxEntries + 1 - LogViewModel.TrimBatch, log.Entries.Count);
            Assert.Equal(LogViewModel.TrimBatch.ToString(System.Globalization.CultureInfo.InvariantCulture), log.Entries[0].Text);
            Assert.Equal("最新", log.Entries.Last().Text);
        }

        [Theory]
        [InlineData(19000, 1528)]  // 超出一次
        [InlineData(19999, 2001)]  // 正好需要删除两批
        [InlineData(15000, 7283)]  // 二进制格式的 ? 响应
        [InlineData(0, 45000)]
        [InlineData(100, 51)]      // 未超出
        [InlineData(100, 5)]       // 少量行:逐行添加
        [InlineData(19998, 3)]     // 少量行,超出上限
        public void AddRange_SameEntriesAsAddingOneByOne_WithFewNotifications(int existing, int added)
        {
            // 多行一次添加 (一个 Reset,超出上限时再一个 Reset),结果与逐行添加、每次超出时删除一批完全相同。
            // 不超过 IncrementalAddLimit 行时逐行添加 (每行一个 Add,不重新生成列表的行)。
            var time = DateTime.Now;
            var batch = new LogViewModel(new FakeErrorLog());
            var oneByOne = new LogViewModel(new FakeErrorLog());
            batch.AddRange(Enumerable.Range(0, existing).Select(i => new LogEntry(time, LogKind.Rx, "old " + i)));
            oneByOne.AddRange(Enumerable.Range(0, existing).Select(i => new LogEntry(time, LogKind.Rx, "old " + i)));
            var lines = Enumerable.Range(0, added).Select(i => new LogEntry(time, LogKind.Rx, "new " + i)).ToList();
            var notifications = new List<NotifyCollectionChangedAction>();
            batch.Entries.CollectionChanged += (s, e) => notifications.Add(e.Action);

            batch.AddRange(lines);
            foreach (var line in lines)
            {
                oneByOne.AddRange(new[] { line });
            }

            Assert.Equal(oneByOne.Entries.Select(e => e.Text), batch.Entries.Select(e => e.Text));
            var small = added <= LogViewModel.IncrementalAddLimit;
            var expected = Enumerable.Repeat(small ? NotifyCollectionChangedAction.Add : NotifyCollectionChangedAction.Reset, small ? added : 1)
                .Concat(existing + added > LogViewModel.MaxEntries ? new[] { NotifyCollectionChangedAction.Reset } : Array.Empty<NotifyCollectionChangedAction>());
            Assert.Equal(expected, notifications);
        }

        [Fact]
        public void LogEntry_DisplayLineByteCount_IsUtf8LengthOfDisplayLine()
        {
            // 程序不在前台时按它计数,必须与通讯数据文件对 ToDisplayLine 的计数相同。
            var time = new DateTime(2026, 10, 22, 9, 5, 7, 89);
            foreach (var kind in Enum.GetValues(typeof(LogKind)).Cast<LogKind>())
            {
                foreach (var text in new[] { string.Empty, "QG01+:20", "中文 Å/s", "😀 \uD800 x" })
                {
                    foreach (var continuation in new[] { false, true })
                    {
                        var entry = new LogEntry(time, kind, text, continuation);
                        Assert.Equal(new UTF8Encoding(false).GetByteCount(entry.ToDisplayLine()), entry.DisplayLineByteCount);
                    }
                }
            }
        }

        [Fact]
        public void AddRange_ErrorLines_WrittenToErrorLogOnce()
        {
            var errorLog = new FakeErrorLog();
            var log = new LogViewModel(errorLog);

            log.AddRange(new[]
            {
                new LogEntry(DateTime.Now, LogKind.Error, "帧外数据"),
                new LogEntry(DateTime.Now, LogKind.Error, "续行", isContinuation: true),
                new LogEntry(DateTime.Now, LogKind.Rx, "数据"),
            });

            Assert.Equal(3, log.Entries.Count);
            Assert.Single(errorLog.Messages);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)] // 不保存通讯数据时同样按 50 KB 计数
        public void InBackground_ShowsPendingWhenSaveBufferFills_ErrorLogAndFileAtOnce(bool saving)
        {
            var errorLog = new FakeErrorLog();
            var recorder = new LineRecorder();
            var log = new LogViewModel(errorLog) { Recorder = saving ? recorder : null, IsInBackground = true };

            log.Add(EntryOfSize(LogViewModel.BackgroundShowBytes - 1000, "a"));
            log.AddRange(new[] { new LogEntry(DateTime.Now, LogKind.Error, "错误"), EntryOfSize(500, "b") });

            // 不到 50 KB:显示等到保存数据的时刻,error.txt 和通讯数据文件立即得到。
            Assert.Empty(log.Entries);
            Assert.Equal(new[] { "错误" }, errorLog.Messages);
            Assert.Equal(saving ? 3 : 0, recorder.Lines.Count);

            log.Add(EntryOfSize(474, "c")); // 刚好 50 KB:错误行 26 字节 + 500 + 474 = 1000

            Assert.Equal(new[] { "a", "错误", "b", "c" }, log.Entries.Select(e => e.Text.Split(' ')[0]));

            log.Add(LogKind.Info, "d");

            Assert.Equal(4, log.Entries.Count);
        }

        [Fact]
        public async Task InBackground_ShowsPendingWhenCommunicationDataFileIsWritten()
        {
            var dir = Path.Combine(Path.GetTempPath(), "zevision_log_" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, "XXXXXXXX_20261022_0900.txt");
            try
            {
                var writer = CommunicationLogWriter.Create(path, new FakeErrorLog());
                var log = new LogViewModel(new FakeErrorLog()) { Recorder = writer };
                log.Add(LogKind.Info, "已连接"); // 在前台时显示的内容同样计入文件的缓存
                log.IsInBackground = true;

                // 内容含中文:按 UTF-8 字节计数,不是字符数。写入文件之前不显示。
                var line = 0;
                while (log.Entries.Count == 1)
                {
                    Assert.Equal(0, new FileInfo(path).Length);
                    log.Add(new LogEntry(DateTime.Now, LogKind.Rx, "第 " + line++ + " 行 Å/s " + new string('x', 700)));
                }

                // 显示的这一批同时使文件写入缓存:文件的内容就是显示的全部内容。
                var shown = log.Entries.Select(e => e.ToDisplayLine()).ToList();
                await WaitUntil(() => ReadLines(path).Count == shown.Count);
                Assert.Equal(shown, ReadLines(path));
                Assert.True(line > 50);

                await writer.CloseAsync();
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [Fact]
        public void LogViewModel_BackgroundShowBytes_IsCommunicationDataBufferLimit()
        {
            Assert.Equal(CommunicationLogWriter.BufferLimit, LogViewModel.BackgroundShowBytes);
        }

        [Fact]
        public void RecorderRemoved_InBackground_ShowsPending()
        {
            // 断开连接时去掉记录,文件写入剩余的内容:这也是保存数据的时刻。
            var log = new LogViewModel(new FakeErrorLog()) { Recorder = new LineRecorder(), IsInBackground = true };
            log.Add(LogKind.Info, "a");
            log.Add(LogKind.Info, "已断开。");

            Assert.Empty(log.Entries);

            log.Recorder = null;

            Assert.Equal(new[] { "a", "已断开。" }, log.Entries.Select(e => e.Text));
        }

        [Fact]
        public void NewRecorder_InBackground_CountsFromEmptyBuffer()
        {
            var log = new LogViewModel(new FakeErrorLog()) { IsInBackground = true };
            log.Add(EntryOfSize(LogViewModel.BackgroundShowBytes - 20, "a"));

            log.Recorder = new LineRecorder(); // 新文件:缓存从空开始,不是保存的时刻
            log.Add(EntryOfSize(20, string.Empty));

            Assert.Empty(log.Entries);

            log.Add(EntryOfSize(LogViewModel.BackgroundShowBytes - 20, "c"));

            Assert.Equal(3, log.Entries.Count);
        }

        [Fact]
        public void Refreshed_RaisedWhenPendingShownOrBackToForeground()
        {
            var log = new LogViewModel(new FakeErrorLog()) { IsInBackground = true };
            var refreshed = 0;
            log.Refreshed += (s, e) => refreshed++;

            log.Add(LogKind.Info, "a");
            Assert.Equal(0, refreshed);

            log.Add(EntryOfSize(LogViewModel.BackgroundShowBytes, "b")); // 保存数据的时刻
            Assert.Equal(1, refreshed);

            log.Recorder = new LineRecorder(); // 开始保存:不是保存的时刻
            log.Add(LogKind.Info, "c");
            Assert.Equal(1, refreshed);

            log.Recorder = null; // 断开连接:文件写完剩余的内容
            Assert.Equal(2, refreshed);

            log.IsInBackground = false;
            Assert.Equal(3, refreshed);
            Assert.Equal(3, log.Entries.Count);
        }

        [Fact]
        public void BackToForeground_ShowsPendingAtOnce_ThenAddsDirectly()
        {
            var log = new LogViewModel(new FakeErrorLog()) { IsInBackground = true };
            log.Add(LogKind.Info, "a");
            log.Add(LogKind.Info, "b");

            log.IsInBackground = false;

            Assert.Equal(new[] { "a", "b" }, log.Entries.Select(e => e.Text));

            log.Add(LogKind.Info, "c");

            Assert.Equal(new[] { "a", "b", "c" }, log.Entries.Select(e => e.Text));
        }

        [Fact]
        public void Clear_InBackground_DropsPendingEntries_KeepsSaveCount()
        {
            var log = new LogViewModel(new FakeErrorLog()) { IsInBackground = true };
            log.Add(EntryOfSize(LogViewModel.BackgroundShowBytes - 20, "a"));

            log.Clear();

            Assert.Empty(log.Entries);

            // 清空不影响保存的数据:缓存照常计数,下一条使它达到 50 KB。
            log.Add(EntryOfSize(20, string.Empty));

            Assert.Single(log.Entries);
        }

        private static List<string> ReadLines(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var lines = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                }

                return lines;
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

        [Fact]
        public void RemoveFirst_MoreThanCount_RemovesAll()
        {
            var entries = new LogEntryCollection { new LogEntry(DateTime.Now, LogKind.Info, "a") };

            entries.RemoveFirst(10);

            Assert.Empty(entries);
        }

        [Fact]
        public void FrameLogFormatter_MultiLineSolicited_HasContinuationLinesAndFooter()
        {
            var raw = Encoding.ASCII.GetBytes("{?+:a\nb}");
            var frame = new ZevisionFrame(raw, Encoding.ASCII.GetBytes("?+:a\nb"), new[] { new ByteRange(1, 6) }, 1, Array.Empty<int>(), FrameError.None);

            var entries = FrameLogFormatter.FromReceived(frame, isSolicited: true, DateTime.Now);

            Assert.Equal(3, entries.Count);
            Assert.Equal(LogKind.Rx, entries[0].Kind);
            Assert.Equal("{?+:a", entries[0].Text);
            Assert.True(entries[1].IsContinuation);
            Assert.Equal("b}", entries[1].Text);
            Assert.Equal(LogKind.Info, entries[2].Kind);
            Assert.StartsWith("[共 8 字节, 2 行", entries[2].Text);
        }

        [Fact]
        public void FrameLogFormatter_UnsolicitedSingleLine_HasPrefixNoFooter()
        {
            var raw = Encoding.ASCII.GetBytes("{H01+}");
            var frame = new ZevisionFrame(raw, Encoding.ASCII.GetBytes("H01+"), new[] { new ByteRange(1, 4) }, 1, Array.Empty<int>(), FrameError.None);

            var entry = Assert.Single(FrameLogFormatter.FromReceived(frame, isSolicited: false, DateTime.Now));

            Assert.Equal("(未请求) {H01+}", entry.Text);
        }

        [Fact]
        public void FrameLogFormatter_InvalidFrame_IsErrorWithDescription()
        {
            var raw = Encoding.ASCII.GetBytes("{H01+");
            var frame = new ZevisionFrame(raw, Encoding.ASCII.GetBytes("H01+"), new[] { new ByteRange(1, 4) }, 1, Array.Empty<int>(), FrameError.MissingStopByte);

            var entries = FrameLogFormatter.FromReceived(frame, isSolicited: true, DateTime.Now);

            Assert.Equal(LogKind.Error, entries[0].Kind);
            Assert.StartsWith("缺少停止符: ", entries[0].Text);
            Assert.Equal(2, entries.Count); // 出错时附统计行
        }
    }
}
