using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Zevision.Communication;

namespace Zevision.Bridge.Hosting
{
    /// <summary>
    /// 中间层的设置 (只有简单类型),可保存为 JSON 文件 (<see cref="DefaultFileName"/>)。由 <see cref="Xtc3BridgeHost"/> 转为
    /// <see cref="Xtc3BridgeOptions"/> 并组装中间层。
    /// </summary>
    [DataContract]
    public sealed class Xtc3BridgeSettings
    {
        /// <summary>独立程序旁的设置文件名。</summary>
        public const string DefaultFileName = "xtc3bridge.settings.json";

        /// <summary>主机侧串口:虚拟串口对中中间层的一端 (主机软件打开另一端)。</summary>
        [DataMember(Name = "hostPort", Order = 0)]
        public string HostPort { get; set; } = "COM2";

        /// <summary>主机侧串口的波特率 (主机经 UG14 修改后以保存的为准)。</summary>
        [DataMember(Name = "hostBaudRate", Order = 1)]
        public int HostBaudRate { get; set; } = 115200;

        /// <summary>IMC300:IP 地址 (TCP 端口 2101),或串口 <c>COM3</c>、<c>COM3:115200</c>。</summary>
        [DataMember(Name = "device", Order = 2)]
        public string Device { get; set; } = string.Empty;

        /// <summary>连接后影子对象不存在时是否在 IMC300 上创建 (会修改设备配置)。</summary>
        [DataMember(Name = "createShadowObjects", Order = 3)]
        public bool CreateShadowObjects { get; set; }

        /// <summary>影子对象的编号基数,两位十六进制 (默认 <c>B0</c>:Film 1~9 对应 B1~B9)。</summary>
        [DataMember(Name = "shadowBase", Order = 4)]
        public string ShadowBase { get; set; } = "B0";

        /// <summary>本地参数文件;相对路径相对于 <see cref="BaseFolder"/>。</summary>
        [DataMember(Name = "stateFile", Order = 5)]
        public string StateFile { get; set; } = "xtc3bridge.json";

        /// <summary>错误日志的文件夹;相对路径相对于 <see cref="BaseFolder"/>。</summary>
        [DataMember(Name = "logFolder", Order = 6)]
        public string LogFolder { get; set; } = "logs";

        /// <summary>H1、H3 回答的版本号。</summary>
        [DataMember(Name = "version", Order = 7)]
        public string Version { get; set; } = "1.00";

        /// <summary>
        /// IMC300 的消息 (连接、握手失败的原因) 的语言:<c>en</c> 或 <c>zh</c>;为空时不改变 (进程中 <c>Lang</c> 的设置,未设置时为中文)。
        /// </summary>
        [DataMember(Name = "language", Order = 8)]
        public string Language { get; set; } = string.Empty;

        /// <summary>相对路径的基准:读取设置文件时为它所在的文件夹,否则为程序的文件夹。不保存。</summary>
        public string BaseFolder { get; set; } = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// 读取设置文件 (UTF-8,可带 BOM);缺少的字段取初值,<see cref="BaseFolder"/> 为文件所在的文件夹。
        /// </summary>
        /// <exception cref="IOException">文件不存在、无法读取或内容无效 (消息含路径)。</exception>
        public static Xtc3BridgeSettings Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A file path is required.", nameof(path));
            }

            var fullPath = Path.GetFullPath(path);
            try
            {
                var bytes = File.ReadAllBytes(fullPath);
                var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                using var stream = new MemoryStream(bytes, start, bytes.Length - start);
                var settings = (Xtc3BridgeSettings?)new DataContractJsonSerializer(typeof(Xtc3BridgeSettings)).ReadObject(stream)
                    ?? throw new IOException(fullPath + ": the file is empty.");
                settings.BaseFolder = Path.GetDirectoryName(fullPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                return settings;
            }
            catch (Exception ex) when (ex is SerializationException || ex is System.Xml.XmlException || ex is InvalidCastException || ex is UnauthorizedAccessException)
            {
                throw new IOException(fullPath + ": " + ex.Message, ex);
            }
        }

        /// <summary>保存为缩进的 JSON (UTF-8,无 BOM)。</summary>
        /// <exception cref="IOException">写入失败。</exception>
        public void Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A file path is required.", nameof(path));
            }

            using var memory = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(memory, new UTF8Encoding(false), ownsStream: false, indent: true))
            {
                new DataContractJsonSerializer(typeof(Xtc3BridgeSettings)).WriteObject(writer, this);
            }

            File.WriteAllBytes(path, memory.ToArray());
        }

        /// <summary>
        /// 转为中间层的设置:IMC300 经 TCP 或串口,帧格式由握手确定 (先试长度和校验和),传输出错重发 2 次。
        /// </summary>
        /// <exception cref="ArgumentException">某项无效 (消息说明哪一项)。</exception>
        public Xtc3BridgeOptions ToOptions()
        {
            if (string.IsNullOrWhiteSpace(HostPort))
            {
                throw new ArgumentException("hostPort is required (the bridge's end of the virtual serial pair, such as COM2).");
            }

            if (HostBaudRate <= 0)
            {
                throw new ArgumentException("hostBaudRate must be positive.");
            }

            if (!int.TryParse(ShadowBase, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var numberBase) || !ShadowFilm.IsValidBase(numberBase))
            {
                throw new ArgumentException("shadowBase must be a hex number from 00 to " + (ShadowFilm.MaxObjectNumber - Xtc3.Xtc3Protocol.FilmCount).ToString("X2", CultureInfo.InvariantCulture) + ".");
            }

            var connection = new ConnectionOptions(ParseDevice(Device), new FrameOptions(true, true)) { RetryCount = 2 };
            var options = new Xtc3BridgeOptions(connection)
            {
                HostPort = new SerialSettings(HostPort.Trim().ToUpperInvariant(), HostBaudRate),
                ShadowBase = numberBase,
                CreateMissingShadowObjects = CreateShadowObjects,
                Version = Version,
            };
            options.Validate();
            return options;
        }

        /// <summary>
        /// IMC300 的连接目标:<c>COM3</c> 或 <c>COM3:115200</c> 为串口 (默认 115200 bit/s),其他为 IP 地址或主机名 (TCP 端口 2101)。
        /// </summary>
        /// <exception cref="ArgumentException">为空或波特率无效。</exception>
        public static ConnectionEndpoint ParseDevice(string text)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                throw new ArgumentException("device is required (the IMC300's IP address, or COMx[:baud]).");
            }

            if (!value.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                return new TcpSettings(value, TcpSettings.DefaultPort);
            }

            var parts = value.Split(':');
            var baud = 115200;
            if (parts.Length > 2 || (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out baud) || baud <= 0)))
            {
                throw new ArgumentException("device \"" + value + "\": use COMx or COMx:baud.");
            }

            return new SerialSettings(parts[0].ToUpperInvariant(), baud);
        }

        /// <summary>相对于 <see cref="BaseFolder"/> 的完整路径。</summary>
        public string Resolve(string path) =>
            Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(BaseFolder, path ?? string.Empty));

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            // 反序列化不运行字段初始化,缺少的字段保持初值。
            HostPort = "COM2";
            HostBaudRate = 115200;
            Device = string.Empty;
            ShadowBase = "B0";
            StateFile = "xtc3bridge.json";
            LogFolder = "logs";
            Version = "1.00";
            Language = string.Empty;
            BaseFolder = AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
