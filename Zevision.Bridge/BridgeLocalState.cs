using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Zevision.Xtc3;

namespace Zevision.Bridge
{
    /// <summary>
    /// 只存在于中间层的参数 (ZEV-FAR-01 附录 A 中类别为 L 的项和标志):主机写入后读回的值不变,中间层重启后保留。
    /// </summary>
    [DataContract]
    public sealed class BridgeLocalState
    {
        /// <summary>文件格式版本。</summary>
        public const int CurrentFormatVersion = 1;

        /// <summary>Tool Factor 2 的初值 (%)。</summary>
        public const float DefaultToolFactor = 100f;

        /// <summary>文件格式版本。</summary>
        [DataMember(Name = "formatVersion", Order = 0)]
        public int FormatVersion { get; set; } = CurrentFormatVersion;

        /// <summary>各 Film 的 Graph Label (QF/UF 36),下标 0 为 Film 1。</summary>
        [DataMember(Name = "graphLabels", Order = 1)]
        public int[] GraphLabels { get; set; } = new int[Xtc3Protocol.FilmCount];

        /// <summary>各 Film 的 Graph Scale (QF/UF 37)。</summary>
        [DataMember(Name = "graphScales", Order = 2)]
        public int[] GraphScales { get; set; } = new int[Xtc3Protocol.FilmCount];

        /// <summary>各 Film 的 Tool Factor 2 (QF/UF 17,%)。IMC300 没有按晶振设定的工具因子,只保存 (ZEV-FAR-01 的 Q-01)。</summary>
        [DataMember(Name = "toolFactor2", Order = 3)]
        public float[] ToolFactor2 { get; set; } = Enumerable.Repeat(DefaultToolFactor, Xtc3Protocol.FilmCount).ToArray();

        /// <summary>Recorder Mode (QG/UG 9)。</summary>
        [DataMember(Name = "recorderMode", Order = 4)]
        public int RecorderMode { get; set; }

        /// <summary>Recorder Range (QG/UG 10)。</summary>
        [DataMember(Name = "recorderRange", Order = 5)]
        public int RecorderRange { get; set; }

        /// <summary>Recorder Filter (QG/UG 11)。</summary>
        [DataMember(Name = "recorderFilter", Order = 6)]
        public int RecorderFilter { get; set; } = 1;

        /// <summary>RS232 Baud Rate (QG/UG 14):主机经 UG14 设定的主机侧串口波特率选项;未设定时为 null (用中间层设置中的波特率)。</summary>
        [DataMember(Name = "baudRate", Order = 7)]
        public int? BaudRate { get; set; }

        /// <summary>远程锁定 (R3 置位、R4 清除;S33 的 Remote Lock)。</summary>
        [DataMember(Name = "remoteLock", Order = 8)]
        public bool RemoteLock { get; set; }

        /// <summary>S21 的上电错误标志 (R22 清除)。</summary>
        [DataMember(Name = "powerUpErrors", Order = 9)]
        public int PowerUpErrors { get; set; }

        /// <summary>上次连接时设备的开机次数 (<c>G0C</c>);增加时置 S21 的 bit2。</summary>
        [DataMember(Name = "powerOnCount", Order = 10)]
        public long? PowerOnCount { get; set; }

        /// <summary>补齐缺少或长度不对的数组 (旧文件、手工修改的文件),把超出范围的值改为初值。</summary>
        public void Normalize()
        {
            GraphLabels = Fit(GraphLabels, 0, v => v >= 0 && v <= 1);
            GraphScales = Fit(GraphScales, 0, v => v >= 0 && v <= 3);
            ToolFactor2 = Fit(ToolFactor2, DefaultToolFactor, v => v >= 10f && v <= 500f);
            RecorderMode = RecorderMode >= 0 && RecorderMode <= 3 ? RecorderMode : 0;
            RecorderRange = RecorderRange >= 0 && RecorderRange <= 1 ? RecorderRange : 0;
            RecorderFilter = RecorderFilter >= 0 && RecorderFilter <= 1 ? RecorderFilter : 1;
            BaudRate = BaudRate >= 0 && BaudRate < Xtc3Parameters.BaudRates.Count ? BaudRate : null;
            PowerUpErrors &= 0x07;
        }

        /// <summary>副本。</summary>
        public BridgeLocalState Clone()
        {
            var copy = (BridgeLocalState)MemberwiseClone();
            copy.GraphLabels = (int[])GraphLabels.Clone();
            copy.GraphScales = (int[])GraphScales.Clone();
            copy.ToolFactor2 = (float[])ToolFactor2.Clone();
            return copy;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            // 反序列化不运行字段初始化,缺少的字段保持初值。
            FormatVersion = CurrentFormatVersion;
            GraphLabels = new int[Xtc3Protocol.FilmCount];
            GraphScales = new int[Xtc3Protocol.FilmCount];
            ToolFactor2 = Enumerable.Repeat(DefaultToolFactor, Xtc3Protocol.FilmCount).ToArray();
            RecorderFilter = 1;
        }

        private static T[] Fit<T>(T[]? values, T fallback, Func<T, bool> isValid) =>
            Enumerable.Range(0, Xtc3Protocol.FilmCount)
                .Select(i => values != null && i < values.Length && isValid(values[i]) ? values[i] : fallback)
                .ToArray();
    }

    /// <summary>本地参数的存储。</summary>
    public interface IBridgeStateStore
    {
        /// <summary>读取;没有保存过时返回初值。</summary>
        /// <exception cref="IOException">读取失败或内容无效。</exception>
        BridgeLocalState Load();

        /// <summary>保存。</summary>
        /// <exception cref="IOException">写入失败。</exception>
        void Save(BridgeLocalState state);
    }

    /// <summary>保存在内存中的本地参数 (测试,或不需要保留时)。</summary>
    public sealed class MemoryBridgeStateStore : IBridgeStateStore
    {
        private BridgeLocalState _state = new BridgeLocalState();

        /// <summary>保存的次数。</summary>
        public int SaveCount { get; private set; }

        /// <inheritdoc/>
        public BridgeLocalState Load() => _state.Clone();

        /// <inheritdoc/>
        public void Save(BridgeLocalState state)
        {
            _state = (state ?? throw new ArgumentNullException(nameof(state))).Clone();
            SaveCount++;
        }
    }

    /// <summary>
    /// 保存在 JSON 文件中的本地参数 (UTF-8,缩进;先写临时文件再替换)。
    /// </summary>
    public sealed class JsonFileBridgeStateStore : IBridgeStateStore
    {
        /// <summary>创建。</summary>
        /// <param name="path">文件路径。</param>
        public JsonFileBridgeStateStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A file path is required.", nameof(path));
            }

            Path = path;
        }

        /// <summary>文件路径。</summary>
        public string Path { get; }

        /// <inheritdoc/>
        public BridgeLocalState Load()
        {
            if (!File.Exists(Path))
            {
                return new BridgeLocalState();
            }

            try
            {
                var bytes = File.ReadAllBytes(Path);
                var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0; // 跳过 UTF-8 BOM
                using var stream = new MemoryStream(bytes, start, bytes.Length - start);
                var state = (BridgeLocalState?)new DataContractJsonSerializer(typeof(BridgeLocalState)).ReadObject(stream)
                    ?? throw new IOException(Path + ": the file is empty.");
                if (state.FormatVersion > BridgeLocalState.CurrentFormatVersion)
                {
                    throw new IOException(Path + ": format version " + state.FormatVersion + " is newer than " + BridgeLocalState.CurrentFormatVersion + ".");
                }

                state.Normalize();
                return state;
            }
            catch (Exception ex) when (ex is SerializationException || ex is System.Xml.XmlException || ex is InvalidCastException)
            {
                throw new IOException(Path + ": " + ex.Message, ex);
            }
        }

        /// <inheritdoc/>
        public void Save(BridgeLocalState state)
        {
            if (state is null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var memory = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(memory, new UTF8Encoding(false), ownsStream: false, indent: true))
            {
                new DataContractJsonSerializer(typeof(BridgeLocalState)).WriteObject(writer, state);
            }

            var temp = Path + ".tmp";
            File.WriteAllBytes(temp, memory.ToArray());
            if (File.Exists(Path))
            {
                File.Replace(temp, Path, null);
            }
            else
            {
                File.Move(temp, Path);
            }
        }
    }
}
