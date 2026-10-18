using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;
using Zevision.Commands;
using Zevision.Communication;

namespace Zevision.Tests
{
    /// <summary>
    /// 各层程序集的边界。依赖方向只能向下:界面 (Zevision.exe) → 业务层 (Zevision.Core) → 协议与通讯
    /// (Zevision.Commands → Zevision.Communication);存储层 (Zevision.Storage) 实现业务层定义的接口,只依赖业务层。
    /// 链接库只依赖 .NET Framework,不引用 WPF 和应用;通讯库只输出英文,业务层不读写文件。
    /// </summary>
    public class LibraryBoundaryTests
    {
        private static readonly Assembly CommunicationAssembly = typeof(ZevisionClient).Assembly;
        private static readonly Assembly CommandsAssembly = typeof(ZevisionCommandSet).Assembly;
        private static readonly Assembly CoreAssembly = typeof(Zevision.Core.Result).Assembly;
        private static readonly Assembly StorageAssembly = typeof(Zevision.Storage.RecipeFile).Assembly;
        private static readonly string[] FileTypes = { "File", "FileInfo", "FileStream", "StreamWriter", "Directory", "DirectoryInfo" };

        [Fact]
        public void Communication_ReferencesOnlyFramework()
        {
            Assert.Empty(References(CommunicationAssembly).Except(new[] { "mscorlib", "System", "System.Core" }));
        }

        [Fact]
        public void Commands_ReferencesOnlyFrameworkAndCommunication()
        {
            Assert.Empty(References(CommandsAssembly).Except(new[] { "mscorlib", "System", "System.Core", "Zevision.Communication" }));
            Assert.Contains("Zevision.Communication", References(CommandsAssembly));
        }

        [Fact]
        public void Communication_HasNoChineseStrings()
        {
            var chinese = UserStrings(CommunicationAssembly).Where(ContainsCjk).ToList();

            Assert.True(chinese.Count == 0, "Chinese strings in Zevision.Communication: " + string.Join(" | ", chinese));
        }

        [Fact]
        public void Core_ReferencesOnlyFrameworkAndProtocolLibraries()
        {
            // 不引用 WPF (PresentationFramework、WindowsBase、System.Xaml)、存储层和应用。
            Assert.Empty(References(CoreAssembly).Except(new[]
            {
                "mscorlib", "System", "System.Core", "System.Runtime.Serialization", "System.Xml", "Zevision.Commands", "Zevision.Communication",
            }));
        }

        [Fact]
        public void Storage_ReferencesOnlyFrameworkAndCore()
        {
            Assert.Empty(References(StorageAssembly).Except(new[] { "mscorlib", "System", "System.Core", "System.Runtime.Serialization", "System.Xml", "Zevision.Core" }));
            Assert.Contains("Zevision.Core", References(StorageAssembly));
        }

        [Fact]
        public void Core_DoesNotUseFiles()
        {
            // 文件读写在存储层,业务层只通过接口 (IRecipeStore、IPreferencesStore、IErrorLog、ICommunicationRecorder) 使用。
            var names = new HashSet<string>(MetadataStrings(CoreAssembly), StringComparer.Ordinal);

            var used = FileTypes.Where(names.Contains).ToList();
            Assert.True(used.Count == 0, "Zevision.Core uses: " + string.Join(", ", used));
        }

        [Fact]
        public void UserInterface_UsesStorageOnlyInCompositionRoot()
        {
            // 视图和视图模型只依赖业务层的接口;存储层的实现只在 App (组装根) 中创建。
            // 源码目录取自编译时本文件的路径,与输出目录无关 (程序运行中时会输出到其他目录构建)。
            var root = Path.GetDirectoryName(Path.GetDirectoryName(SourceFilePath()))!;
            Assert.True(File.Exists(Path.Combine(root, "Zevision.sln")), root);
            var offending = new[] { "ViewModels", "Views" }
                .SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.cs"))
                .Where(file => File.ReadAllText(file).Contains("Zevision.Storage"))
                .Select(Path.GetFileName)
                .ToList();
            Assert.True(offending.Count == 0, "Zevision.Storage used in: " + string.Join(", ", offending));
        }

        [Fact]
        public void CoreAndStorage_HaveNoChineseStrings()
        {
            // 业务层和存储层的中文提示都在翻译文件 Texts.json 中,代码里只有键。
            foreach (var assembly in new[] { typeof(Zevision.Core.Result).Assembly, typeof(Zevision.Storage.RecipeFile).Assembly })
            {
                var chinese = UserStrings(assembly).Where(ContainsCjk).ToList();
                Assert.True(chinese.Count == 0, "Chinese strings in " + assembly.GetName().Name + ": " + string.Join(" | ", chinese));
            }
        }

        [Fact]
        public void Communication_DoesNotUseFiles()
        {
            var names = new HashSet<string>(MetadataStrings(CommunicationAssembly), StringComparer.Ordinal);
            var fileTypes = FileTypes.Concat(new[] { "Trace", "Debug", "EventLog" });

            var used = fileTypes.Where(names.Contains).ToList();
            Assert.True(used.Count == 0, "Zevision.Communication uses: " + string.Join(", ", used));
        }

        [Fact]
        public void Commands_ErrorMessagesAreEnglish()
        {
            // 指令库保留中英文名称作为数据,但异常说明只用英文。
            var definition = ZevisionCommandSet.Find("M0103")!;
            var errors = new List<string>
            {
                Assert.Throws<ArgumentException>(() => ZevisionCommandSet.Build(CommandActions.Query, "m01")).Message,
                Assert.Throws<ArgumentException>(() => ZevisionCommandSet.Build(CommandActions.Query | CommandActions.Update, "M0103")).Message,
                Assert.Throws<ArgumentException>(() => ZevisionCommandSet.Build(CommandActions.Add, "M0103")).Message,
                Assert.Throws<ArgumentException>(() => ZevisionCommandSet.Build(CommandActions.Update, "M0103")).Message,
                Assert.Throws<ArgumentException>(() => ZevisionCommandSet.Build(CommandActions.Query, "M0103", "1")).Message,
                Assert.Throws<ArgumentException>(() => ZevisionCommandSet.Update("M0103", "a;b")).Message,
                Assert.Throws<ArgumentException>(() => definition.FormatId()).Message,
                Assert.Throws<ArgumentOutOfRangeException>(() => definition.FormatId(0)).Message,
                Assert.Throws<ArgumentException>(() => definition.ParseIndices("F0103")).Message,
                Assert.Throws<ArgumentException>(() => ZevisionCommandSet.ActionLetter(CommandActions.Query | CommandActions.Add)).Message,
            };

            // 只看第一行:.NET Framework 会按系统语言追加"参数名: id"。
            Assert.All(errors, e => Assert.False(ContainsCjk(FirstLine(e)), e));
            Assert.Contains("does not support Add", errors[2]);
        }

        [Fact]
        public void Commands_TextsInBothLanguages()
        {
            var definition = ZevisionCommandSet.Find("M0103")!;

            Assert.Equal("密度", definition.GetName(TextLanguage.Chinese));
            Assert.Equal("Density (g/cm^3)", definition.GetName(TextLanguage.English));
            Assert.Equal("密度 (g/cm³)", definition.GetLabel(TextLanguage.Chinese));
            Assert.Equal("Density (g/cm^3)", definition.GetLabel(TextLanguage.English));
            Assert.Equal("M**03 Density (g/cm^3)", definition.ToString());
            Assert.Equal("预熔 2 · 起始斜坡/上升时间", ZevisionCommandSet.Find("F01030201")!.LabelFor("F01030201", TextLanguage.Chinese));
            Assert.Equal("Pre Condition 2 · Start Ramp/Rise time", ZevisionCommandSet.Find("F01030201")!.LabelFor("F01030201", TextLanguage.English));
            Assert.Equal("材料", ZevisionCommandSet.ObjectKind('M', TextLanguage.Chinese));
            Assert.Equal("Material", ZevisionCommandSet.ObjectKind('M', TextLanguage.English));
            Assert.Equal("1 - On", ZevisionCommandSet.Find("G0E01")!.Options[1].ToString());
        }

        private static IReadOnlyList<string> References(Assembly assembly) =>
            assembly.GetReferencedAssemblies().Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        private static string SourceFilePath([CallerFilePath] string path = "") => path;

        private static string FirstLine(string text) => text.Split(new[] { '\r', '\n' }, 2)[0];

        private static bool ContainsCjk(string text) =>
            text.Any(c => (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3000 && c <= 0x303F) || (c >= 0xFF00 && c <= 0xFFEF));

        /// <summary>程序集 #US 堆中的全部字符串字面量。</summary>
        private static IEnumerable<string> UserStrings(Assembly assembly)
        {
            var (bytes, start, size) = Stream(assembly, "#US");
            var i = start + 1; // 第一项是空串
            while (i < start + size)
            {
                var length = ReadCompressed(bytes, ref i);
                if (length > 1)
                {
                    yield return Encoding.Unicode.GetString(bytes, i, length - 1);
                }

                i += length;
            }
        }

        /// <summary>程序集 #Strings 堆中的全部名称 (类型、成员、命名空间)。</summary>
        private static IEnumerable<string> MetadataStrings(Assembly assembly)
        {
            var (bytes, start, size) = Stream(assembly, "#Strings");
            return Encoding.UTF8.GetString(bytes, start, size).Split('\0').Where(s => s.Length > 0);
        }

        /// <summary>按 ECMA-335 II.24.2 找到元数据流:返回文件字节、流的起始位置和大小。</summary>
        private static (byte[] Bytes, int Start, int Size) Stream(Assembly assembly, string name)
        {
            var bytes = File.ReadAllBytes(assembly.Location);
            var root = IndexOf(bytes, Encoding.ASCII.GetBytes("BSJB"));
            Assert.True(root > 0, "metadata root not found");
            var versionLength = BitConverter.ToInt32(bytes, root + 12);
            var p = root + 16 + versionLength + 2; // 跳过 Flags
            var streams = BitConverter.ToInt16(bytes, p);
            p += 2;
            for (var s = 0; s < streams; s++)
            {
                var offset = BitConverter.ToInt32(bytes, p);
                var size = BitConverter.ToInt32(bytes, p + 4);
                var end = Array.IndexOf(bytes, (byte)0, p + 8);
                var streamName = Encoding.ASCII.GetString(bytes, p + 8, end - (p + 8));
                if (streamName == name)
                {
                    return (bytes, root + offset, size);
                }

                p = (end + 4) & ~3; // 名称连同结尾的 0 按 4 字节对齐
            }

            throw new InvalidOperationException(name + " not found");
        }

        private static int ReadCompressed(byte[] bytes, ref int i)
        {
            var b = bytes[i];
            if ((b & 0x80) == 0)
            {
                i += 1;
                return b;
            }

            if ((b & 0xC0) == 0x80)
            {
                var value = ((b & 0x3F) << 8) | bytes[i + 1];
                i += 2;
                return value;
            }

            var large = ((b & 0x1F) << 24) | (bytes[i + 1] << 16) | (bytes[i + 2] << 8) | bytes[i + 3];
            i += 4;
            return large;
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (!needle.Where((t, j) => haystack[i + j] != t).Any())
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
