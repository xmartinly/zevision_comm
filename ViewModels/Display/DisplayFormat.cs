namespace Zevision.ViewModels.Display
{
    /// <summary>数据显示区中收发数据的显示格式。</summary>
    public enum DisplayFormat
    {
        /// <summary>
        /// 文本:可打印 ASCII 原样显示,其余字节显示为 <c>&lt;XX&gt;</c>,数据中的换行拆行,UTF-8 解码 (默认)。
        /// </summary>
        Ascii,

        /// <summary>十六进制:每字节两位,空格分隔,如 <c>7B 48 30 31 7D</c>。</summary>
        Hex,

        /// <summary>二进制:每字节八位,空格分隔,如 <c>01111011</c>。</summary>
        Binary,
    }
}
