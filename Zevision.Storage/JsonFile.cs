using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Zevision.Storage
{
    /// <summary>
    /// DataContract 对象与缩进的 UTF-8 JSON 文件之间的读写 (配置文件、<c>settings.json</c>)。
    /// 使用 .NET Framework 自带的 <see cref="DataContractJsonSerializer"/>,不引入第三方依赖。
    /// </summary>
    /// <remarks>
    /// 写入是安全覆盖:先写到同目录的临时文件,成功后再替换目标文件。
    /// 写到一半出错 (断电、磁盘满、程序被关闭) 时,原文件保持完好,不会留下半个文件。
    /// </remarks>
    internal static class JsonFile
    {
        /// <summary>读取文件。</summary>
        /// <returns>内容为 null 或不是 <typeparamref name="T"/> 时返回 null。</returns>
        /// <exception cref="IOException">读取失败。</exception>
        /// <exception cref="UnauthorizedAccessException">没有读权限。</exception>
        /// <exception cref="SerializationException">内容不是有效的 JSON。</exception>
        public static T? Read<T>(string path)
            where T : class
        {
            using (var stream = OpenUtf8(path))
            {
                return new DataContractJsonSerializer(typeof(T)).ReadObject(stream) as T;
            }
        }

        /// <summary>
        /// 读出文件内容,跳过 UTF-8 BOM:记事本等编辑器"另存为 UTF-8"时会加上,而 DataContractJsonSerializer 不接受。
        /// </summary>
        /// <exception cref="IOException">读取失败。</exception>
        /// <exception cref="UnauthorizedAccessException">没有读权限。</exception>
        public static MemoryStream OpenUtf8(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return new MemoryStream(bytes, start, bytes.Length - start, writable: false);
        }

        /// <summary>写入文件 (覆盖),必要时创建目录。失败时删除临时文件并把异常抛给调用方。</summary>
        /// <exception cref="IOException">写入失败。</exception>
        /// <exception cref="UnauthorizedAccessException">没有写权限。</exception>
        /// <exception cref="SerializationException">对象无法序列化。</exception>
        public static void Write<T>(string path, T value) => WriteBytes(CheckPath(path), Serialize(value));

        /// <summary>
        /// 写入文件,内容与现有文件完全相同时不写 (不产生临时文件,修改时间不变)。工艺编辑每次保存全部对象,多数文件没有变化。
        /// </summary>
        /// <returns>是否写入了。</returns>
        /// <exception cref="IOException">读取现有文件或写入失败。</exception>
        /// <exception cref="UnauthorizedAccessException">没有读写权限。</exception>
        /// <exception cref="SerializationException">对象无法序列化。</exception>
        public static bool WriteIfChanged<T>(string path, T value)
        {
            var bytes = Serialize(value);
            if (File.Exists(CheckPath(path)) && File.ReadAllBytes(path).SequenceEqual(bytes))
            {
                return false;
            }

            WriteBytes(path, bytes);
            return true;
        }

        private static string CheckPath(string path) =>
            string.IsNullOrEmpty(path) ? throw new ArgumentException(Texts.PathEmpty, nameof(path)) : path;

        /// <summary>序列化为缩进的 UTF-8 JSON (无 BOM)。</summary>
        private static byte[] Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), ownsStream: false, indent: true))
                {
                    new DataContractJsonSerializer(typeof(T)).WriteObject(writer, value);
                }

                return stream.ToArray();
            }
        }

        private static void WriteBytes(string path, byte[] bytes)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = path + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path))
                {
                    File.Replace(temp, path, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        /// <summary>读写文件时的预期异常:文件不存在或被占用、没有权限、内容不是有效的 JSON。</summary>
        public static bool IsExpectedFailure(Exception ex) =>
            ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException;

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 临时文件删除失败不影响结果,下次写入会覆盖。
            }
        }
    }
}
