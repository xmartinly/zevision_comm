using System;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using Xunit;
using Zevision.Communication;
using Zevision.ViewModels.Display;

namespace Zevision.Tests
{
    public class LogViewModelTests
    {
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
        [InlineData(100, 5)]       // 未超出
        public void AddRange_SameEntriesAsAddingOneByOne_WithOneAddNotification(int existing, int added)
        {
            // 多行一次添加 (一个 Reset,超出上限时再一个 Reset),结果与逐行添加、每次超出时删除一批完全相同。
            var time = DateTime.Now;
            var batch = new LogViewModel(new FakeErrorLog());
            var oneByOne = new LogViewModel(new FakeErrorLog());
            batch.AddRange(Enumerable.Range(0, existing).Select(i => new LogEntry(time, LogKind.Rx, "old " + i)));
            oneByOne.AddRange(Enumerable.Range(0, existing).Select(i => new LogEntry(time, LogKind.Rx, "old " + i)));
            var lines = Enumerable.Range(0, added).Select(i => new LogEntry(time, LogKind.Rx, "new " + i)).ToList();
            var notifications = 0;
            batch.Entries.CollectionChanged += (s, e) => notifications++;

            batch.AddRange(lines);
            foreach (var line in lines)
            {
                oneByOne.AddRange(new[] { line });
            }

            Assert.Equal(oneByOne.Entries.Select(e => e.Text), batch.Entries.Select(e => e.Text));
            Assert.Equal(existing + added > LogViewModel.MaxEntries ? 2 : 1, notifications);
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
