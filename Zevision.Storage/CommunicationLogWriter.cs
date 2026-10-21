using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// 写入一块会使文件超过 <see cref="FileSizeLimit"/> 时,先换到下一个文件:原文件名后加 <c>_1</c>、<c>_2</c>…
    /// (<c>XXXXXXXX_20261021_1337.txt</c> → <c>XXXXXXXX_20261021_1337_1.txt</c>)。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>缓存的内容作为一块排队,文件只在 <c>_fileLock</c> 中按排队顺序写入:后台写入和关闭时的写入不会颠倒顺序。</item>
    /// <item>只是记录用途:写入失败时记入 error.txt 并停止记录 (丢弃缓存),不影响通讯。</item>
    /// <item>一块 (完整的若干批显示行) 不拆到两个文件中;单独一块超过上限时写入空文件,该文件超过上限。</item>
    /// <item>程序异常终止时,缓存中尚未写入的内容 (最多约 <see cref="BufferLimit"/>) 会丢失。</item>
    /// </list>
    /// </remarks>
    internal sealed class CommunicationLogWriter : ICommunicationRecorder
    {
        /// <summary>内存中缓存的内容 (UTF-8 字节数) 达到这个大小时写入文件:50 KB。</summary>
        internal const int BufferLimit = 50 * 1024;

        /// <summary>一个通讯数据文件的大小上限 (字节):30 MB。</summary>
        internal const long FileSizeLimit = 30L * 1024 * 1024;

        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly int NewLineBytes = Utf8.GetByteCount(Environment.NewLine);

        /// <summary>保护 <see cref="_buffer"/>、<see cref="_bufferedBytes"/>、<see cref="_pending"/>、<see cref="_closed"/>、<see cref="_draining"/>。</summary>
        private readonly object _sync = new object();

        /// <summary>写文件和关闭文件时持有,保证各块按顺序写入。</summary>
        private readonly object _fileLock = new object();

        private readonly IErrorLog _errorLog;
        private readonly string _firstPath;
        private readonly long _fileSizeLimit;
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly Queue<string> _pending = new Queue<string>();
        private int _bufferedBytes;
        private bool _closed;
        private bool _draining;
        private StreamWriter? _writer;

        /// <summary>当前文件的大小 (字节;调用方持有 <c>_fileLock</c>)。</summary>
        private long _fileBytes;

        /// <summary>当前文件的序号:0 = 第一个文件,n = 文件名后加 <c>_n</c>。</summary>
        private int _fileNumber;

        /// <summary>当前文件;换文件时在后台线程上改变,界面线程读取。</summary>
        private volatile string _path;

        private CommunicationLogWriter(string path, StreamWriter writer, IErrorLog errorLog, long fileSizeLimit)
        {
            _firstPath = path;
            _path = path;
            _writer = writer;
            _fileBytes = writer.BaseStream.Length;
            _errorLog = errorLog;
            _fileSizeLimit = fileSizeLimit;
        }

        /// <inheritdoc />
        /// <remarks>超过 <see cref="FileSizeLimit"/> 换文件后为新文件。</remarks>
        public string Path => _path;

        /// <summary>
        /// 打开文件 (已存在时追加),必要时创建目录。
        /// </summary>
        /// <param name="path">第一个文件;后续文件在它的文件名后加 <c>_1</c>、<c>_2</c>…</param>
        /// <param name="errorLog">写入失败时记录到这里。</param>
        /// <param name="fileSizeLimit">一个文件的大小上限 (字节),默认 <see cref="FileSizeLimit"/>;测试用较小的值。</param>
        /// <exception cref="IOException">无法创建文件。</exception>
        /// <exception cref="UnauthorizedAccessException">没有写权限。</exception>
        public static CommunicationLogWriter Create(string path, IErrorLog errorLog, long fileSizeLimit = FileSizeLimit)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException(Texts.PathEmpty, nameof(path));
            }

            if (errorLog is null)
            {
                throw new ArgumentNullException(nameof(errorLog));
            }

            if (fileSizeLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fileSizeLimit));
            }

            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return new CommunicationLogWriter(path, Open(path), errorLog, fileSizeLimit);
        }

        /// <summary>第 <paramref name="number"/> 个后续文件:<c>x.txt</c> → <c>x_1.txt</c>。</summary>
        internal static string NumberedPath(string path, int number) =>
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(path) ?? string.Empty,
                System.IO.Path.GetFileNameWithoutExtension(path) + "_" + number.ToString(CultureInfo.InvariantCulture) + System.IO.Path.GetExtension(path));

        private static StreamWriter Open(string path) =>
            new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), Utf8);

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

        /// <summary>
        /// 写入一块并刷新 (调用方持有 <see cref="_fileLock"/>);写入后会超过上限且当前文件不空时先换到下一个文件。
        /// 失败时记录、停止记录并丢弃缓存。
        /// </summary>
        private void Write(string chunk)
        {
            var writer = _writer;
            if (writer == null)
            {
                return;
            }

            try
            {
                var bytes = Utf8.GetByteCount(chunk);

                // 下一个文件已存在 (同一秒内重新连接) 且也放不下时继续换,直到空文件或放得下的文件。
                while (_fileBytes > 0 && _fileBytes + bytes > _fileSizeLimit)
                {
                    writer = OpenNextFile();
                }

                writer.Write(chunk);
                writer.Flush();
                _fileBytes += bytes;
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

        /// <summary>关闭当前文件,打开下一个 (调用方持有 <see cref="_fileLock"/>)。</summary>
        /// <exception cref="IOException">无法创建文件。</exception>
        /// <exception cref="UnauthorizedAccessException">没有写权限。</exception>
        private StreamWriter OpenNextFile()
        {
            CloseWriter();
            _fileNumber++;
            _path = NumberedPath(_firstPath, _fileNumber);
            var writer = Open(_path);
            _writer = writer;
            _fileBytes = writer.BaseStream.Length;
            return writer;
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
