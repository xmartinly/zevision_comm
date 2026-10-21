using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Zevision.Core;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;

namespace Zevision.ViewModels.Display
{
    /// <summary>
    /// 数据显示区:保存显示记录,错误记录同时写入 error.txt,超过上限时批量删除最旧的记录。
    /// 设置了 <see cref="Recorder"/> 时,显示的记录同时追加到通讯数据文件 (多条记录作为一批)。
    /// 程序不在前台时 (<see cref="IsInBackground"/>) 降低显示的刷新率。
    /// 只应在 UI 线程上调用。绑定只用到不变的 <see cref="Entries"/>,所以是 <see cref="ImmutableBindingSource"/>。
    /// </summary>
    public sealed class LogViewModel : ImmutableBindingSource
    {
        /// <summary>最多保留的行数。</summary>
        public const int MaxEntries = 20000;

        /// <summary>超出上限时一次删除的行数。</summary>
        public const int TrimBatch = 2000;

        /// <summary>程序不在前台时刷新显示的间隔:1 s。</summary>
        public static readonly TimeSpan BackgroundRefreshInterval = TimeSpan.FromSeconds(1);

        private readonly IErrorLog _errorLog;

        /// <summary>程序不在前台时暂存、尚未显示的记录。</summary>
        private readonly List<LogEntry> _pending = new List<LogEntry>();

        private bool _isInBackground;
        private bool _refreshScheduled;

        /// <summary>
        /// 创建数据显示区。
        /// </summary>
        public LogViewModel(IErrorLog errorLog)
        {
            _errorLog = errorLog ?? throw new ArgumentNullException(nameof(errorLog));
        }

        /// <summary>显示记录。</summary>
        public LogEntryCollection Entries { get; } = new LogEntryCollection();

        /// <summary>保存通讯数据:显示的记录按显示的样子 (<see cref="LogEntry.ToDisplayLine"/>) 同时交给它追加;为 null 时不保存。</summary>
        public ICommunicationRecorder? Recorder { get; set; }

        /// <summary>
        /// 程序是否不在前台 (其他程序在前台或主窗口最小化;由窗口设置)。为 true 时新记录先暂存,
        /// 每 <see cref="BackgroundRefreshInterval"/> 一次添加到 <see cref="Entries"/> (一次通知),减少界面刷新;
        /// 改为 false 时立即显示暂存的记录。error.txt 和通讯数据文件不受影响,记录添加时立即交给它们。
        /// </summary>
        public bool IsInBackground
        {
            get => _isInBackground;
            set
            {
                _isInBackground = value;
                if (!value)
                {
                    ShowPending();
                }
            }
        }

        /// <summary>等待下一次刷新;测试替换为手动完成的任务。await 之后回到调用方 (UI) 线程。</summary>
        internal Func<TimeSpan, Task> Delay { get; set; } = Task.Delay;

        /// <summary>添加一条记录。</summary>
        public void Add(LogKind kind, string text) => Add(new LogEntry(DateTime.Now, kind, text));

        /// <summary>
        /// 添加一条记录。错误记录 (不含多行响应的后续行) 同时追加到 error.txt。
        /// </summary>
        public void Add(LogEntry entry) => Add(entry, writeErrorLog: true);

        /// <summary>
        /// 命令执行中的意外异常:error.txt 记录完整堆栈,数据显示区只显示消息。
        /// </summary>
        public void ReportException(Exception exception)
        {
            if (exception is null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            _errorLog.Write(Lang.T("操作异常", "Operation error"), exception.Message, exception);
            Add(new LogEntry(DateTime.Now, LogKind.Error, Lang.T("操作失败: ", "Operation failed: ") + exception.Message), writeErrorLog: false);
        }

        /// <summary>按顺序添加多条记录 (保存通讯数据时作为一批追加)。</summary>
        public void AddRange(IEnumerable<LogEntry> entries)
        {
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var list = entries.ToList();
            if (list.Count == 0)
            {
                return;
            }

            foreach (var entry in list)
            {
                WriteErrorLog(entry);
            }

            Show(list);
            Recorder?.Append(list.Select(e => e.ToDisplayLine()).ToList());
        }

        /// <summary>清空显示,包括暂存的记录 (已保存的通讯数据不受影响)。</summary>
        public void Clear()
        {
            _pending.Clear();
            Entries.Clear();
        }

        private void Add(LogEntry entry, bool writeErrorLog)
        {
            if (entry is null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (writeErrorLog)
            {
                WriteErrorLog(entry);
            }

            Show(new[] { entry });
            Recorder?.Append(new[] { entry.ToDisplayLine() });
        }

        /// <summary>
        /// 显示记录。在前台时立即添加:一条逐项添加,多条一次添加,只发一次通知 (逐项添加时,如果用户正向上翻看,
        /// 列表每添加一项都要重新计算全部内容的高度,一个 ? 响应 (1500 多行) 会让界面卡住 2 秒以上,画面也会跳动)。
        /// 不在前台时暂存,由 <see cref="ShowPendingLaterAsync"/> 稍后一起添加。
        /// </summary>
        private void Show(IReadOnlyList<LogEntry> entries)
        {
            if (_isInBackground)
            {
                _pending.AddRange(entries);
                if (!_refreshScheduled)
                {
                    _ = ShowPendingLaterAsync();
                }

                return;
            }

            // 回到前台时已显示暂存的记录 (IsInBackground),这里不会有暂存的记录。
            if (entries.Count == 1)
            {
                Entries.Add(entries[0]);
            }
            else
            {
                Entries.AddRange(entries);
            }

            Trim();
        }

        /// <summary>过 <see cref="BackgroundRefreshInterval"/> 后显示暂存的记录 (期间暂存的记录一起显示)。</summary>
        private async Task ShowPendingLaterAsync()
        {
            _refreshScheduled = true;
            try
            {
                await Delay(BackgroundRefreshInterval);
            }
            finally
            {
                _refreshScheduled = false;
            }

            ShowPending();
        }

        private void ShowPending()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            var entries = _pending.ToArray();
            _pending.Clear();
            Entries.AddRange(entries);
            Trim();
        }

        private void WriteErrorLog(LogEntry entry)
        {
            if (entry.Kind == LogKind.Error && !entry.IsContinuation)
            {
                _errorLog.Write(Lang.T("运行", "Runtime"), entry.Text);
            }
        }

        /// <summary>
        /// 超过 <see cref="MaxEntries"/> 时按 <see cref="TrimBatch"/> 的整数倍删除最旧的记录,一次完成;
        /// 结果与逐项添加、每次超出时删除 <see cref="TrimBatch"/> 条相同。
        /// </summary>
        private void Trim()
        {
            var excess = Entries.Count - MaxEntries;
            if (excess > 0)
            {
                Entries.RemoveFirst((excess + TrimBatch - 1) / TrimBatch * TrimBatch);
            }
        }
    }

    /// <summary>数据显示区的记录 (一次添加多行、删除开头多行时只发一次通知,见 <see cref="BulkObservableCollection{T}"/>)。</summary>
    public sealed class LogEntryCollection : BulkObservableCollection<LogEntry>
    {
    }
}
