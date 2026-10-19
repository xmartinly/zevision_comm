using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Zevision.Core;

namespace Zevision.Storage
{
    /// <summary>
    /// 保存通讯数据:把数据显示区的内容 (显示的样子,含当时选择的显示格式) 追加到文本文件。
    /// 内容先缓存在内存中,超过 <see cref="BufferLimit"/> 时在后台写入;断开连接 (<see cref="CloseAsync"/>)
    /// 和程序退出 (<see cref="Dispose"/>) 时写完剩余内容并关闭文件。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>缓存的内容作为一块排队,文件只在 <c>_fileLock</c> 中按排队顺序写入:后台写入和关闭时的写入不会颠倒顺序。</item>
    /// <item>只是记录用途:写入失败时记入 error.txt 并停止记录 (丢弃缓存),不影响通讯。</item>
    /// <item>程序异常终止时,缓存中尚未写入的内容 (最多约 <see cref="BufferLimit"/>) 会丢失。</item>
    /// </list>
    /// </remarks>
    internal sealed class CommunicationLogWriter : ICommunicationRecorder
    {
        /// <summary>内存中缓存的内容 (UTF-8 字节数) 达到这个大小时写入文件:50 KB。</summary>
        internal const int BufferLimit = 50 * 1024;

        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly int NewLineBytes = Utf8.GetByteCount(Environment.NewLine);

        /// <summary>保护 <see cref="_buffer"/>、<see cref="_bufferedBytes"/>、<see cref="_pending"/>、<see cref="_closed"/>、<see cref="_draining"/>。</summary>
        private readonly object _sync = new object();

        /// <summary>写文件和关闭文件时持有,保证各块按顺序写入。</summary>
        private readonly object _fileLock = new object();

        private readonly IErrorLog _errorLog;
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly Queue<string> _pending = new Queue<string>();
        private int _bufferedBytes;
        private bool _closed;
        private bool _draining;
        private StreamWriter? _writer;

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
            return new CommunicationLogWriter(path, new StreamWriter(stream, Utf8), errorLog);
        }

        /// <inheritdoc />
        public void Append(IReadOnlyList<string> lines)
        {
            if (lines is null)
            {
                throw new ArgumentNullException(nameof(lines));
            }

            lock (_sync)
            {
                if (_closed || lines.Count == 0)
                {
                    return;
                }

                foreach (var line in lines)
                {
                    _buffer.AppendLine(line);
                    _bufferedBytes += Utf8.GetByteCount(line) + NewLineBytes;
                }

                if (_bufferedBytes < BufferLimit)
                {
                    return;
                }

                _pending.Enqueue(TakeBuffer());
                if (!_draining)
                {
                    _draining = true;
                    _ = Task.Run(Drain);
                }
            }
        }

        /// <inheritdoc />
        public Task CloseAsync() => Task.Run(Close);

        /// <inheritdoc />
        public void Dispose() => Close();

        /// <summary>停止接收新的行,在调用线程上写完缓存和排队的内容,关闭文件。可以重复调用。</summary>
        private void Close()
        {
            lock (_sync)
            {
                _closed = true;
                if (_buffer.Length > 0)
                {
                    _pending.Enqueue(TakeBuffer());
                }
            }

            // 后台写入正在进行时等它写完 (它也会写完队列);之后写完剩余的块并关闭。
            lock (_fileLock)
            {
                while (TryDequeue(out var chunk))
                {
                    Write(chunk);
                }

                CloseWriter();
            }
        }

        /// <summary>后台写入:按顺序写完排队的块。</summary>
        private void Drain()
        {
            lock (_fileLock)
            {
                while (true)
                {
                    string chunk;
                    lock (_sync)
                    {
                        if (_pending.Count == 0)
                        {
                            // 与 Append 在同一把锁中判断和清除,新排队的块不会没人写。
                            _draining = false;
                            return;
                        }

                        chunk = _pending.Dequeue();
                    }

                    Write(chunk);
                }
            }
        }

        private bool TryDequeue(out string chunk)
        {
            lock (_sync)
            {
                if (_pending.Count == 0)
                {
                    chunk = string.Empty;
                    return false;
                }

                chunk = _pending.Dequeue();
                return true;
            }
        }

        /// <summary>取出缓存的内容 (调用方持有 <see cref="_sync"/>)。</summary>
        private string TakeBuffer()
        {
            var text = _buffer.ToString();
            _buffer.Clear();
            _bufferedBytes = 0;
            return text;
        }

        /// <summary>写入一块并刷新 (调用方持有 <see cref="_fileLock"/>)。失败时记录、停止记录并丢弃缓存。</summary>
        private void Write(string chunk)
        {
            if (_writer == null)
            {
                return;
            }

            try
            {
                _writer.Write(chunk);
                _writer.Flush();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _errorLog.Write(Texts.LogSourceSaveCommunicationData, Texts.CaptureWriteFailed(Path, ex.Message), ex);
                lock (_sync)
                {
                    _closed = true;
                    _pending.Clear();
                    TakeBuffer();
                }

                CloseWriter();
            }
        }

        /// <summary>关闭文件 (调用方持有 <see cref="_fileLock"/>);关闭时刷新失败 (如磁盘已满) 也只是放弃,不抛出。</summary>
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
