using System;
using Zevision.Core.Localization;
using Zevision.Storage;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// C# 9 的模块初始化器需要的特性。.NET Framework 4.7.2 中没有,在此补上 (编译器只按名称查找它)。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

namespace Zevision.Tests
{
    /// <summary>
    /// 测试程序集加载时执行一次:与程序启动时一样读入输出目录中的翻译文件 Texts.json (由 Zevision.Core 项目复制过来)。
    /// </summary>
    internal static class TestSetup
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void LoadTexts()
        {
            var texts = TextFile.Load(TextFile.DefaultPath);
            if (!texts.IsSuccess)
            {
                throw new InvalidOperationException(TextFile.DefaultPath + ": " + texts.Error);
            }

            TextCatalog.Initialize(texts.Value);
        }
    }
}
