using System;
using System.Globalization;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 数据显示区中一条记录的类型。
    /// </summary>
    public enum LogKind
    {
        /// <summary>发送到设备的数据。</summary>
        Tx,

        /// <summary>从设备接收的数据。</summary>
        Rx,

        /// <summary>程序状态信息。</summary>
        Info,

        /// <summary>错误信息。</summary>
        Error,
    }

    /// <summary>
    /// 数据显示区中的一条记录。
    /// </summary>
    public sealed class LogEntry
    {
        /// <summary>
        /// 创建记录。
        /// </summary>
        public LogEntry(DateTime timestamp, LogKind kind, string text, bool isContinuation = false)
        {
            Timestamp = timestamp;
            Kind = kind;
            Text = text;
            IsContinuation = isContinuation;
        }

        /// <summary>是否是上一条记录的后续行 (多行响应的第二行起),显示时不重复时间和类型。</summary>
        public bool IsContinuation { get; }

        /// <summary>记录时间。</summary>
        public DateTime Timestamp { get; }

        /// <summary>记录类型。</summary>
        public LogKind Kind { get; }

        /// <summary>记录内容。</summary>
        public string Text { get; }

        /// <summary>
        /// 一行文本 (复制和保存通讯数据用):<c>HH:mm:ss.fff 类型 内容</c>,类型占 5 列;
        /// 后续行用空格代替时间和类型,与上一行的内容对齐。
        /// </summary>
        public string ToDisplayLine() =>
            IsContinuation
                ? new string(' ', 18) + Text
                : Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + Kind.ToString().ToUpperInvariant().PadRight(5) + Text;
    }
}
