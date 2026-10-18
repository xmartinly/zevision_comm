using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Core;
using Zevision.Core.Localization;
using Zevision.Models;

namespace Zevision.Services
{
    /// <summary>接收数据显示区的记录并保存。</summary>
    public interface ICommunicationRecorder
    {
        /// <summary>追加一批记录 (一次通讯显示的全部行)。不阻塞调用方,不抛出 I/O 异常。</summary>
        void Append(IReadOnlyList<LogEntry> entries);
    }

    /// <summary>
    /// 保存通讯数据:把数据显示区的内容按显示的样子 (<see cref="LogEntry.ToDisplayLine"/>,含当时选择的显示格式)
    /// 追加到文本文件,每批写完立即刷新。写入在后台按顺序进行,不占用 UI 线程。
    /// </summary>
    /// <remarks>
    /// 只是记录用途:写入失败时记入 error.txt 并停止记录,不影响通讯。
    /// </remarks>
    public sealed class CommunicationLogWriter : ICommunicationRecorder
    {
        private readonly object _sync = new object();
        private readonly IErrorLog _errorLog;
        private StreamWriter? _writer;
        private Task _tail = Task.CompletedTask;

        private CommunicationLogWriter(string path, StreamWriter writer, IErrorLog errorLog)
        {
            Path = path;
            _writer = writer;
            _errorLog = errorLog;
        }

        /// <summary>文件路径。</summary>
        public string Path { get; }

        /// <summary>
        /// 打开文件 (已存在时追加),必要时创建目录。
        /// </summary>
        /// <exception cref="IOException">无法创建文件。</exception>
        /// <exception cref="UnauthorizedAccessException">没有写权限。</exception>
        public static CommunicationLogWriter Create(string path, IErrorLog errorLog)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException(Lang.T("路径不能为空。", "The path must not be empty."), nameof(path));
            }

            if (errorLog is null)
            {
                throw new ArgumentNullException(nameof(errorLog));
            }

            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            return new CommunicationLogWriter(path, new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)), errorLog);
        }

        /// <inheritdoc />
        public void Append(IReadOnlyList<LogEntry> entries)
        {
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (entries.Count == 0)
            {
                return;
            }

            // 记录是不可变对象,复制列表后交给后台格式化和写入。
            var batch = entries.ToArray();
            Enqueue(() => Write(batch));
        }

        /// <summary>
        /// 写完已排队的记录后关闭文件。之后的 <see cref="Append"/> 被忽略。
        /// </summary>
        /// <returns>文件关闭后完成;不会失败。</returns>
        public Task CloseAsync() => Enqueue(CloseWriter);

        private Task Enqueue(Action action)
        {
            lock (_sync)
            {
                _tail = _tail.ContinueWith(_ => action(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                return _tail;
            }
        }

        /// <summary>只在后台写入链中调用,按顺序执行,不需要加锁。</summary>
        private void Write(IReadOnlyList<LogEntry> batch)
        {
            if (_writer == null)
            {
                return;
            }

            try
            {
                foreach (var entry in batch)
                {
                    _writer.WriteLine(entry.ToDisplayLine());
                }

                _writer.Flush();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _errorLog.Write(Lang.T("保存通讯数据", "Save communication data"), Lang.T($"写入 {Path} 失败,停止保存: {ex.Message}", $"Writing {Path} failed; saving stopped: {ex.Message}"), ex);
                CloseWriter();
            }
        }

        /// <summary>关闭文件;关闭时刷新失败 (如磁盘已满) 也只是放弃,不抛出。</summary>
        private void CloseWriter()
        {
            try
            {
                _writer?.Dispose();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _errorLog.Write(Lang.T("保存通讯数据", "Save communication data"), Lang.T($"关闭 {Path} 失败: {ex.Message}", $"Closing {Path} failed: {ex.Message}"), ex);
            }

            _writer = null;
        }
    }
}
