using System;
using System.Globalization;
using System.Text;
using Zevision.ViewModels.Common;

namespace Zevision.ViewModels.Display
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
    public sealed class LogEntry : ImmutableBindingSource
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
                ? ContinuationPrefix + Text
                : Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + KindPrefix(Kind) + Text;

        /// <summary><see cref="ToDisplayLine"/> 的 UTF-8 字节数 (不生成这一行):前缀是 ASCII,再加内容的字节数。</summary>
        public int DisplayLineByteCount => PrefixLength + Encoding.UTF8.GetByteCount(Text);

        /// <summary><see cref="ToDisplayLine"/> 中内容之前的字符数:时间 12、空格 1、类型 5。</summary>
        private const int PrefixLength = 18;

        /// <summary>接续行的前缀 (与时间和类型同宽的空格)。</summary>
        private static readonly string ContinuationPrefix = new string(' ', PrefixLength);

        /// <summary>类型的前缀 (大写,补足 5 个字符),如 <c>"TX   "</c>:保存通讯数据时每行都要用,不每次生成。</summary>
        private static string KindPrefix(LogKind kind)
        {
            switch (kind)
            {
                case LogKind.Tx: return "TX   ";
                case LogKind.Rx: return "RX   ";
                case LogKind.Info: return "INFO ";
                case LogKind.Error: return "ERROR";
                default: return kind.ToString().ToUpperInvariant().PadRight(5);
            }
        }
    }
}
