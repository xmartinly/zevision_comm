using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Core;

namespace Zevision.Storage
{
    /// <summary>
    /// 保存通讯数据:把数据显示区的内容 (显示的样子,含当时选择的显示格式) 追加到文本文件,每批写完立即刷新。
    /// 写入在后台按顺序进行,不占用 UI 线程。
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

        /// <inheritdoc />
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
                throw new ArgumentException(Texts.PathEmpty, nameof(path));
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
        public void Append(IReadOnlyList<string> lines)
        {
            if (lines is null)
            {
                throw new ArgumentNullException(nameof(lines));
            }

            if (lines.Count == 0)
            {
                return;
            }

            // 复制列表后交给后台写入。
            var batch = lines.ToArray();
            Enqueue(() => Write(batch));
        }

        /// <inheritdoc />
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
        private void Write(IReadOnlyList<string> batch)
        {
            if (_writer == null)
            {
                return;
            }

            try
            {
                foreach (var line in batch)
                {
                    _writer.WriteLine(line);
                }

                _writer.Flush();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _errorLog.Write(Texts.LogSourceSaveCommunicationData, Texts.CaptureWriteFailed(Path, ex.Message), ex);
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
                _errorLog.Write(Texts.LogSourceSaveCommunicationData, Texts.CaptureCloseFailed(Path, ex.Message), ex);
            }

            _writer = null;
        }
    }
}
