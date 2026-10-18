using System;
using System.Globalization;
using System.IO;
using System.Text;
using Zevision.Core;
using Zevision.Core.Localization;

namespace Zevision.Services
{
    /// <summary>
    /// 以追加方式把错误写入文本文件 (UTF-8)。线程安全;写入失败时静默放弃,不影响程序运行。
    /// </summary>
    /// <remarks>
    /// 文件超过 <see cref="MaxFileBytes"/> 时转存为 <c>error.1.txt</c> (覆盖上一次的转存),再从空文件开始,防止无限增长。
    /// 每条记录格式:<c>yyyy-MM-dd HH:mm:ss.fff [来源] 消息</c>,有异常时随后附上 <see cref="Exception.ToString"/> (含堆栈)。
    /// </remarks>
    public sealed class FileErrorLog : IErrorLog
    {
        /// <summary>单个文件的大小上限 (字节)。</summary>
        public const long MaxFileBytes = 5 * 1024 * 1024;

        /// <summary>单条消息的最大长度,超出部分截断 (完整数据见收发记录)。</summary>
        public const int MaxMessageLength = 500;

        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly object _sync = new object();
        private readonly string _path;
        private readonly string? _sessionHeader;
        private readonly long _maxFileBytes;
        private bool _headerWritten;

        /// <summary>
        /// 创建日志。文件和目录在第一次写入时创建。
        /// </summary>
        /// <param name="path">日志文件路径,如 <c>logs\error.txt</c>。</param>
        /// <param name="sessionHeader">本次运行第一条错误之前写入的一行 (如版本与运行环境);没有错误时不写。</param>
        /// <param name="maxFileBytes">大小上限;仅测试时需要修改。</param>
        public FileErrorLog(string path, string? sessionHeader = null, long maxFileBytes = MaxFileBytes)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException(Lang.T("路径不能为空。", "The path must not be empty."), nameof(path));
            }

            _path = path;
            _sessionHeader = sessionHeader;
            _maxFileBytes = maxFileBytes;
        }

        /// <inheritdoc />
        public void Write(string source, string message, Exception? exception = null)
        {
            var text = message ?? string.Empty;
            if (text.Length > MaxMessageLength)
            {
                text = text.Substring(0, MaxMessageLength) + Lang.T("…(已截断)", "…(truncated)");
            }

            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
              .Append(" [").Append(source).Append("] ")
              .Append(text.Replace("\r\n", " ").Replace('\n', ' '))
              .AppendLine();
            if (exception != null)
            {
                sb.AppendLine(exception.ToString());
            }

            lock (_sync)
            {
                try
                {
                    var directory = System.IO.Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // 轮转失败 (如 error.1.txt 被编辑器占用) 时照常追加,不能因此丢失之后的所有错误。
                    TryRotateIfTooLarge();
                    if (_sessionHeader != null && !_headerWritten)
                    {
                        sb.Insert(0, "==== " + _sessionHeader + " ====" + Environment.NewLine);
                    }

                    File.AppendAllText(_path, sb.ToString(), Utf8);
                    _headerWritten = true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException)
                {
                    // 错误日志本身失败时只能放弃:不能因为记录错误而让程序出错。
                }
            }
        }

        private void TryRotateIfTooLarge()
        {
            try
            {
                var info = new FileInfo(_path);
                if (!info.Exists || info.Length < _maxFileBytes)
                {
                    return;
                }

                var archive = System.IO.Path.Combine(
                    info.DirectoryName ?? string.Empty,
                    System.IO.Path.GetFileNameWithoutExtension(_path) + ".1" + System.IO.Path.GetExtension(_path));
                File.Delete(archive);
                File.Move(_path, archive);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 下次写入时再试。
            }
        }
    }
}
