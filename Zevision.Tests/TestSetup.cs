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
    /// 测试程序集加载时执行一次:设置线程池的最少线程数;与程序启动时一样读入输出目录中的翻译文件 Texts.json (由 Zevision.Core 项目复制过来)。
    /// </summary>
    internal static class TestSetup
    {
        /// <summary>
        /// 线程池的最少线程数。测试类并行运行,其中有的测试故意阻塞线程池线程 (如事件处理程序 Thread.Sleep 400 ms),
        /// 线程池按需缓慢增加线程时,其他测试的读取循环可能晚于 150 ms 的响应超时才运行。
        /// </summary>
        private const int MinThreads = 32;

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Initialize()
        {
            System.Threading.ThreadPool.GetMinThreads(out var workers, out var completionPorts);
            System.Threading.ThreadPool.SetMinThreads(Math.Max(workers, MinThreads), Math.Max(completionPorts, MinThreads));

            var texts = TextFile.Load(TextFile.DefaultPath);
            if (!texts.IsSuccess)
            {
                throw new InvalidOperationException(TextFile.DefaultPath + ": " + texts.Error);
            }

            TextCatalog.Initialize(texts.Value);
        }
    }
}
