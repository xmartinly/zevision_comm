using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using Zevision.Core;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;

namespace Zevision.ViewModels.Display
{
    /// <summary>
    /// 数据显示区:保存显示记录,错误记录同时写入 error.txt,超过上限时批量删除最旧的记录。
    /// 设置了 <see cref="Recorder"/> 时,显示的记录同时追加到通讯数据文件 (多条记录作为一批)。
    /// 程序不在前台时 (<see cref="IsInBackground"/>) 新记录在保存数据的时刻统一显示。
    /// 只应在 UI 线程上调用。绑定只用到不变的 <see cref="Entries"/>,所以是 <see cref="ImmutableBindingSource"/>。
    /// </summary>
    public sealed class LogViewModel : ImmutableBindingSource
    {
        /// <summary>最多保留的行数。</summary>
        public const int MaxEntries = 20000;

        /// <summary>超出上限时一次删除的行数。</summary>
        public const int TrimBatch = 2000;

        /// <summary>
        /// 逐行添加的一批记录的行数上限:不超过它的一批逐行添加 (每行一个 Add 通知),更多的一次添加 (一个 Reset 通知)。
        /// Reset 使列表丢弃并重新生成所有行的容器 (实测每次约 100 ms、8 MB),重复发送多条指令时每次收发都有一批解析结果;
        /// 逐行添加时列表复用容器。几百行以上 (如 ? 响应) 逐行添加时,用户向上翻看的情况下界面会卡住 1 秒以上,所以仍一次添加。
        /// </summary>
        public const int IncrementalAddLimit = 50;

        /// <summary>
        /// 程序不在前台时,显示的内容每积累这么多 UTF-8 字节 (按保存到通讯数据文件的样子计算,含换行) 统一显示一次:
        /// 与通讯数据文件的内存缓存相同 (50 KB),即保存通讯数据时写入文件的时刻。
        /// </summary>
        public const int BackgroundShowBytes = 50 * 1024;

        private static readonly int NewLineBytes = Encoding.UTF8.GetByteCount(Environment.NewLine);

        private readonly IErrorLog _errorLog;

        /// <summary>程序不在前台时暂存、尚未显示的记录。</summary>
        private readonly List<LogEntry> _pending = new List<LogEntry>();

        private bool _isInBackground;
        private ICommunicationRecorder? _recorder;

        /// <summary>上次保存 (写入通讯数据文件) 以来显示的内容的字节数,与通讯数据文件的内存缓存同样计数。</summary>
        private int _unsavedBytes;

        /// <summary>
        /// 创建数据显示区。
        /// </summary>
        public LogViewModel(IErrorLog errorLog)
        {
            _errorLog = errorLog ?? throw new ArgumentNullException(nameof(errorLog));
        }

        /// <summary>
        /// 程序不在前台时暂存的记录显示出来 (保存数据的时刻、回到前台时) 之后触发。
        /// 随数据显示区一起刷新的内容 (收发计数) 在此时通知界面。
        /// </summary>
        public event EventHandler? Refreshed;

        /// <summary>显示记录。</summary>
        public LogEntryCollection Entries { get; } = new LogEntryCollection();

        /// <summary>
        /// 保存通讯数据:显示的记录按显示的样子 (<see cref="LogEntry.ToDisplayLine"/>) 同时交给它追加;为 null 时不保存。
        /// 去掉 (断开连接时) 是保存的时刻 (文件写完剩余的内容),暂存的记录随即显示。
        /// </summary>
        public ICommunicationRecorder? Recorder
        {
            get => _recorder;
            set
            {
                var previous = _recorder;
                _recorder = value;

                // 新的记录从空的缓存开始。
                _unsavedBytes = 0;
                if (previous != null)
                {
                    ShowPending();
                }
            }
        }

        /// <summary>
        /// 程序是否不在前台 (其他程序在前台或主窗口最小化;由窗口设置)。为 true 时新记录先暂存,
        /// 在保存数据的时刻 (显示的内容积累到 <see cref="BackgroundShowBytes"/>,或去掉 <see cref="Recorder"/>)
        /// 一次添加到 <see cref="Entries"/>,减少界面刷新;改为 false 时立即显示暂存的记录。每次都触发 <see cref="Refreshed"/>。
        /// error.txt 和通讯数据文件不受影响,记录添加时立即交给它们。
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
        /// 显示一批记录。在前台时立即添加 (<see cref="Display"/>);不在前台时暂存,在保存数据的时刻一起添加。
        /// </summary>
        private void Show(IReadOnlyList<LogEntry> entries)
        {
            var saved = CountUnsaved(entries);
            if (!_isInBackground)
            {
                // 回到前台时已显示暂存的记录 (IsInBackground),这里不会有暂存的记录。
                Display(entries);
                return;
            }

            _pending.AddRange(entries);
            if (saved)
            {
                ShowPending();
            }
        }

        /// <summary>
        /// 计入一批显示的内容,与通讯数据文件的内存缓存同样计数 (每行的 UTF-8 字节数加换行,一批计完后比较)。
        /// </summary>
        /// <returns>这一批使内容达到 <see cref="BackgroundShowBytes"/>,即保存通讯数据时缓存写入文件的时刻。</returns>
        private bool CountUnsaved(IReadOnlyList<LogEntry> entries)
        {
            foreach (var entry in entries)
            {
                _unsavedBytes += entry.DisplayLineByteCount + NewLineBytes;
            }

            if (_unsavedBytes < BackgroundShowBytes)
            {
                return false;
            }

            _unsavedBytes = 0;
            return true;
        }

        /// <summary>显示暂存的记录 (没有时什么也不添加),然后触发 <see cref="Refreshed"/>。</summary>
        private void ShowPending()
        {
            if (_pending.Count > 0)
            {
                var entries = _pending.ToArray();
                _pending.Clear();
                Display(entries);
            }

            Refreshed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 添加到 <see cref="Entries"/>:不超过 <see cref="IncrementalAddLimit"/> 行时逐行添加,
        /// 更多时一次添加,只发一次通知 (逐项添加时,如果用户正向上翻看,列表每添加一项都要重新计算全部内容的高度,
        /// 一个 ? 响应 (1500 多行) 会让界面卡住 1 秒以上,画面也会跳动)。
        /// </summary>
        private void Display(IReadOnlyList<LogEntry> entries)
        {
            if (entries.Count <= IncrementalAddLimit)
            {
                foreach (var entry in entries)
                {
                    Entries.Add(entry);
                }
            }
            else
            {
                Entries.AddRange(entries);
            }

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
