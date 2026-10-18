using System;
using System.Globalization;
using System.IO;
using Zevision.Core;
using Zevision.Core.Localization;

namespace Zevision.Storage
{
    /// <summary>
    /// 为每次连接在日志目录中创建一个通讯数据文件 <c>capture_yyyyMMdd_HHmmss.txt</c> (<see cref="CommunicationLogWriter"/>)。
    /// 文件永久保留,不自动清理。
    /// </summary>
    public sealed class CommunicationLogFiles : ICommunicationRecorderFactory
    {
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
        public Result<ICommunicationRecorder> Create()
        {
            var path = Path.Combine(_folder(), $"capture_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.txt");
            try
            {
                return Result<ICommunicationRecorder>.Ok(CommunicationLogWriter.Create(path, _errorLog));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return Result<ICommunicationRecorder>.Fail(Lang.T($"无法创建通讯数据文件 {path}: {ex.Message}", $"Cannot create the communication data file {path}: {ex.Message}"));
            }
        }
    }
}
