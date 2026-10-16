using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Zevision.Models
{
    /// <summary>
    /// 仪器配置文件 (recipe) 的内容。以 JSON 保存,字段名固定,修改结构时必须提升 <see cref="FormatVersion"/>。
    /// </summary>
    [DataContract]
    public sealed class InstrumentRecipe
    {
        /// <summary>
        /// 当前程序写出和能读取的格式版本。
        /// 版本 2 增加了 <see cref="Md5"/>;版本 1 的文件没有校验值,无法确认未被修改,不再接受。
        /// </summary>
        public const int CurrentFormatVersion = 2;

        /// <summary>文件格式版本。</summary>
        [DataMember(Name = "formatVersion", Order = 0)]
        public int FormatVersion { get; set; } = CurrentFormatVersion;

        /// <summary>设备名称 (G03),如 <c>IMC300</c>。</summary>
        [DataMember(Name = "deviceName", Order = 1)]
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>型号 (G05)。</summary>
        [DataMember(Name = "modelNumber", Order = 2)]
        public string ModelNumber { get; set; } = string.Empty;

        /// <summary>固件版本 (G08)。</summary>
        [DataMember(Name = "firmwareVersion", Order = 3)]
        public string FirmwareVersion { get; set; } = string.Empty;

        /// <summary>序列号 (G06)。</summary>
        [DataMember(Name = "serialNumber", Order = 4)]
        public string SerialNumber { get; set; } = string.Empty;

        /// <summary>保存时间,ISO 8601 (本地时间带时区)。</summary>
        [DataMember(Name = "savedAt", Order = 5)]
        public string SavedAt { get; set; } = string.Empty;

        /// <summary>
        /// 除本字段外所有数据的 MD5 (32 位小写十六进制),保存时计算,恢复前校验。
        /// 计算方法见 <c>Services.RecipeFile.ComputeMd5</c>。
        /// </summary>
        [DataMember(Name = "md5", Order = 6)]
        public string Md5 { get; set; } = string.Empty;

        /// <summary>配置项。</summary>
        [DataMember(Name = "entries", Order = 7)]
        public List<RecipeEntry> Entries { get; set; } = new List<RecipeEntry>();
    }

    /// <summary>
    /// 一个配置项。
    /// </summary>
    [DataContract]
    public sealed class RecipeEntry
    {
        /// <summary>命令 ID (不含动作字母),如 <c>F0101</c>。</summary>
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; } = string.Empty;

        /// <summary>值,原样保留设备给出的文本,不做数值转换以免受区域设置影响。</summary>
        [DataMember(Name = "value", Order = 1)]
        public string Value { get; set; } = string.Empty;

        /// <summary>说明,仅供阅读。</summary>
        [DataMember(Name = "description", Order = 2)]
        public string Description { get; set; } = string.Empty;
    }
}
