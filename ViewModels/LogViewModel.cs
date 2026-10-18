using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Zevision.Core;
using Zevision.Core.Localization;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 数据显示区:保存显示记录,错误记录同时写入 error.txt,超过上限时批量删除最旧的记录。
    /// 设置了 <see cref="Recorder"/> 时,显示的记录同时追加到通讯数据文件 (多条记录作为一批)。
    /// 只应在 UI 线程上调用。绑定只用到不变的 <see cref="Entries"/>,所以是 <see cref="ImmutableBindingSource"/>。
    /// </summary>
    public sealed class LogViewModel : ImmutableBindingSource
    {
        /// <summary>最多保留的行数。</summary>
        public const int MaxEntries = 20000;

        /// <summary>超出上限时一次删除的行数。</summary>
        public const int TrimBatch = 2000;

        private readonly IErrorLog _errorLog;

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
            if (list.Count > 1)
            {
                // 一次添加,只发一次通知:逐项添加时,如果用户正向上翻看 (不在底部),列表每添加一项都要重新计算
                // 全部内容的高度,一个 ? 响应 (1500 多行) 会让界面卡住 2 秒以上,画面也会跳动。
                foreach (var entry in list)
                {
                    WriteErrorLog(entry);
                }

                Entries.AddRange(list);
                Trim();
            }
            else if (list.Count == 1)
            {
                AddCore(list[0], writeErrorLog: true);
            }

            if (list.Count > 0)
            {
                Recorder?.Append(list.Select(e => e.ToDisplayLine()).ToList());
            }
        }

        /// <summary>清空显示 (已保存的通讯数据不受影响)。</summary>
        public void Clear() => Entries.Clear();

        private void Add(LogEntry entry, bool writeErrorLog)
        {
            if (entry is null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            AddCore(entry, writeErrorLog);
            Recorder?.Append(new[] { entry.ToDisplayLine() });
        }

        private void AddCore(LogEntry entry, bool writeErrorLog)
        {
            if (writeErrorLog)
            {
                WriteErrorLog(entry);
            }

            Entries.Add(entry);
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

    /// <summary>
    /// 支持一次删除开头多项的集合:逐项删除会触发上千次界面刷新,这里只发一次 Reset 通知。
    /// </summary>
    public sealed class LogEntryCollection : ObservableCollection<LogEntry>
    {
        /// <summary>在末尾添加多项,只发一次 Reset 通知。</summary>
        public void AddRange(IReadOnlyList<LogEntry> entries)
        {
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            CheckReentrancy();
            if (entries.Count == 0)
            {
                return;
            }

            ((List<LogEntry>)Items).AddRange(entries);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        /// <summary>删除最前面的 <paramref name="count"/> 项。</summary>
        public void RemoveFirst(int count)
        {
            CheckReentrancy();
            count = Math.Min(count, Count);
            if (count <= 0)
            {
                return;
            }

            // Collection<T> 的默认底层存储是 List<T>。
            ((List<LogEntry>)Items).RemoveRange(0, count);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
