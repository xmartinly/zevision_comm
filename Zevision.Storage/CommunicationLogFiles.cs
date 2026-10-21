using System;
using System.Globalization;
using System.IO;
using Zevision.Core;

namespace Zevision.Storage
{
    /// <summary>
    /// 为每次连接在日志目录中创建一个通讯数据文件 (<see cref="CommunicationLogWriter"/>):设备序列号、日期、时间 (到分钟),
    /// 如 <c>XXXXXXXX_20261021_1337.txt</c>;序列号未知时为 <see cref="UnknownSerialNumber"/>。
    /// 同一分钟内再次连接 (含自动重连) 时追加到同一文件。超过 30 MB 时续写到 <c>XXXXXXXX_20261021_1337_1.txt</c>、<c>_2</c>…。
    /// 文件永久保留,不自动清理。
    /// </summary>
    public sealed class CommunicationLogFiles : ICommunicationRecorderFactory
    {
        /// <summary>设备序列号未知 (未启用握手、设备未回答 G06) 时文件名中代替它的文字。</summary>
        public const string UnknownSerialNumber = "unknown";

        /// <summary>文件名中序列号的最大长度,超出部分截去。</summary>
        private const int MaxSerialNumberLength = 32;

        private readonly IErrorLog _errorLog;
        private readonly Func<string> _folder;

        /// <summary>创建工厂。</summary>
        /// <param name="errorLog">写入失败时记录到这里。</param>
        /// <param name="folder">取得保存目录;为 null 时用 <see cref="AppFolders.GetLogsFolder"/> (每次创建时确定)。</param>
        public CommunicationLogFiles(IErrorLog errorLog, Func<string>? folder = null)
        {
            _errorLog = errorLog ?? throw new ArgumentNullException(nameof(errorLog));
            _folder = folder ?? AppFolders.GetLogsFolder;
        }

        /// <inheritdoc />
        public Result<ICommunicationRecorder> Create(string? deviceSerialNumber)
        {
            var path = Path.Combine(_folder(), FileName(deviceSerialNumber, DateTime.Now));
            try
            {
                return Result<ICommunicationRecorder>.Ok(CommunicationLogWriter.Create(path, _errorLog));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return Result<ICommunicationRecorder>.Fail(Texts.CannotCreateCaptureFile(path, ex.Message));
            }
        }

        /// <summary>文件名:<c>序列号_yyyyMMdd_HHmm.txt</c>。</summary>
        internal static string FileName(string? deviceSerialNumber, DateTime time) =>
            SerialNumberPart(deviceSerialNumber) + "_" + time.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".txt";

        /// <summary>序列号中不能用于文件名的字符和空白换成 <c>_</c>,过长时截去;为空时为 <see cref="UnknownSerialNumber"/>。</summary>
        private static string SerialNumberPart(string? deviceSerialNumber)
        {
            var text = deviceSerialNumber?.Trim();
            if (text is null || text.Length == 0)
            {
                return UnknownSerialNumber;
            }

            var chars = (text.Length > MaxSerialNumberLength ? text.Substring(0, MaxSerialNumberLength) : text).ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (var i = 0; i < chars.Length; i++)
            {
                if (char.IsWhiteSpace(chars[i]) || Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }
    }
}
