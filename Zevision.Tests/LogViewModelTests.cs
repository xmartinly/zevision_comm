using System;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using Xunit;
using Zevision.Communication;
using Zevision.ViewModels;

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
