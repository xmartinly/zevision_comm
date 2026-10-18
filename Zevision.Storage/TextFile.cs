using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Zevision.Core;
using Zevision.Core.Localization;

namespace Zevision.Storage
{
    /// <summary>
    /// 读取翻译文件 (<see cref="TextCatalog.FileName"/>,程序旁边),交给 <see cref="TextCatalog.Initialize"/>。
    /// 文件为 UTF-8 JSON (可以带 BOM):<c>{ "键": { "zh": "中文", "en": "English" } }</c>,每条都必须有 zh 和 en。
    /// </summary>
    public static class TextFile
    {
        /// <summary>程序旁边的翻译文件路径。</summary>
        public static string DefaultPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TextCatalog.FileName);

        /// <summary>
        /// 读取并检查文件。失败时的说明是原因本身 (英文或系统语言的异常消息):文本还没有读到,调用方自己给出提示。
        /// </summary>
        /// <param name="path">文件路径。</param>
        public static Result<IReadOnlyDictionary<string, LocalizedText>> Load(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("The path must not be empty.", nameof(path));
            }

            Dictionary<string, Entry>? entries;
            try
            {
                // 记事本等编辑器保存的 UTF-8 可能带 BOM,DataContractJsonSerializer 不接受,先跳过。
                var bytes = File.ReadAllBytes(path);
                var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                using (var stream = new MemoryStream(bytes, start, bytes.Length - start))
                {
                    var serializer = new DataContractJsonSerializer(
                        typeof(Dictionary<string, Entry>),
                        new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                    entries = serializer.ReadObject(stream) as Dictionary<string, Entry>;
                }
            }
            catch (Exception ex) when (JsonFile.IsExpectedFailure(ex) || ex is ArgumentException)
            {
                // 同一个键出现两次时 DataContractJsonSerializer 抛出 ArgumentException (字典中已有该键)。
                return Result<IReadOnlyDictionary<string, LocalizedText>>.Fail(ex.Message);
            }

            if (entries == null || entries.Count == 0)
            {
                return Result<IReadOnlyDictionary<string, LocalizedText>>.Fail("The file contains no texts.");
            }

            var incomplete = entries.FirstOrDefault(e => e.Value?.Chinese == null || e.Value.English == null).Key;
            if (incomplete != null)
            {
                return Result<IReadOnlyDictionary<string, LocalizedText>>.Fail($"\"{incomplete}\" needs both \"zh\" and \"en\".");
            }

            return Result<IReadOnlyDictionary<string, LocalizedText>>.Ok(
                entries.ToDictionary(e => e.Key, e => new LocalizedText(e.Value.Chinese!, e.Value.English!), StringComparer.Ordinal));
        }

        /// <summary>文件中的一条。</summary>
        [DataContract]
        private sealed class Entry
        {
            [DataMember(Name = "zh")]
            public string? Chinese { get; set; }

            [DataMember(Name = "en")]
            public string? English { get; set; }
        }
    }
}
