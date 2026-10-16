using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Zevision
{
    /// <summary>
    /// 保证同一用户会话中只运行一个实例:两个实例会争用同一个串口,并互相覆盖 settings.json 和日志。
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        /// <summary>互斥体名称。"Local\" 表示只在当前用户会话内唯一。</summary>
        private const string MutexName = @"Local\INFICON.Zevision.SingleInstance";

        private readonly Mutex? _mutex;
        private bool _owned;

        private SingleInstance(Mutex? mutex, bool owned)
        {
            _mutex = mutex;
            _owned = owned;
        }

        /// <summary>本进程是否是唯一实例。</summary>
        public bool IsFirstInstance => _owned;

        /// <summary>
        /// 尝试成为唯一实例。返回的对象须在程序退出时释放。
        /// </summary>
        public static SingleInstance Acquire()
        {
            try
            {
                var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
                return new SingleInstance(mutex, createdNew);
            }
            catch (UnauthorizedAccessException)
            {
                // 已有实例以管理员身份运行时,普通权限无法打开它的互斥体:同样说明已有实例。
                return new SingleInstance(null, owned: false);
            }
        }

        /// <summary>
        /// 把已在运行的实例的主窗口还原并切到前台。找不到时不做任何事。
        /// </summary>
        public static void ActivateExistingInstance()
        {
            using (var current = Process.GetCurrentProcess())
            {
                foreach (var process in Process.GetProcessesByName(current.ProcessName))
                {
                    using (process)
                    {
                        IntPtr window;
                        try
                        {
                            if (process.Id == current.Id || (window = process.MainWindowHandle) == IntPtr.Zero)
                            {
                                continue;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            continue; // 枚举期间该进程已退出
                        }

                        if (NativeMethods.IsIconic(window))
                        {
                            NativeMethods.ShowWindow(window, NativeMethods.SwRestore);
                        }

                        NativeMethods.SetForegroundWindow(window);
                        return;
                    }
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_owned)
            {
                _mutex!.ReleaseMutex(); // owned 只在取得互斥体时为 true
                _owned = false;
            }

            _mutex?.Dispose();
        }

        private static class NativeMethods
        {
            public const int SwRestore = 9;

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsIconic(IntPtr hWnd);
        }
    }
}
