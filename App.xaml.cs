using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Zevision.Communication;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Devices;
using Zevision.Core.Localization;
using Zevision.Storage;
using Zevision.ViewModels.Main;
using Zevision.Views;

namespace Zevision
{
    /// <summary>
    /// 应用入口,同时作为依赖组装的唯一位置 (composition root)。
    /// </summary>
    public partial class App : Application
    {
        private SingleInstance? _singleInstance;
        private IErrorLog? _errorLog;
        private ZevisionDeviceService? _deviceService;
        private MainViewModel? _mainViewModel;

        /// <inheritdoc />
        protected override void OnStartup(StartupEventArgs e)
        {
            // 只允许一个实例:已有实例时把它切到前台后退出 (不创建日志、不读写配置)。
            _singleInstance = SingleInstance.Acquire();
            if (!_singleInstance.IsFirstInstance)
            {
                SingleInstance.ActivateExistingInstance();
                Shutdown();
                return;
            }

            // 业务层和存储层的文本 (程序旁边的 Texts.json) 在任何文本被使用之前读入;读不到时稍后提示并退出。
            var texts = TextFile.Load(TextFile.DefaultPath);
            if (texts.IsSuccess)
            {
                TextCatalog.Initialize(texts.Value);
            }

            // 先确定界面语言:日志的会话标题和所有界面文本都依赖它。
            // 中国地区或中文系统用中文,其他用英文;settings.json 的 language (zh/en) 可以强制指定,配置文件读取失败时按系统选择。
            var preferences = new JsonFilePreferencesStore(AppFolders.GetSettingsFilePath());
            var saved = preferences.Load();
            Lang.Initialize(Lang.Detect(CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture, saved.IsSuccess ? saved.Value.Language : null));

            // 最先创建错误日志并挂接全局异常处理,保证启动过程中的异常也能被记录。
            _errorLog = new FileErrorLog(Path.Combine(AppFolders.GetLogsFolder(), "error.txt"), AppInfo.Description);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            base.OnStartup(e);

            if (!texts.IsSuccess)
            {
                // 没有文本时提示和错误信息都无法显示 (只会显示键名),不能继续运行。
                var message = Lang.T(
                    $"程序无法启动:无法读取文本文件 {TextFile.DefaultPath}。请重新安装程序,或把发布包中的 {TextCatalog.FileName} 放回程序所在的文件夹。{Environment.NewLine}{texts.Error}",
                    $"The program cannot start: the text file {TextFile.DefaultPath} cannot be read. Reinstall the program, or put {TextCatalog.FileName} from the release package back into the program folder.{Environment.NewLine}{texts.Error}");
                _errorLog.Write(Lang.T("启动", "Startup"), message);
                MessageBox.Show(message, "Zevision", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            try
            {
                _deviceService = new ZevisionDeviceService(DeviceTransports.Create, _errorLog);
                _mainViewModel = new MainViewModel(
                    _deviceService,
                    preferences,
                    new InstrumentConfigService(_deviceService),
                    new RecipeFileStore(),
                    new CommunicationLogFiles(_errorLog),
                    new DialogService(),
                    _errorLog);

                _mainViewModel.RestartRequested += OnRestartRequested;

                var window = new MainWindow
                {
                    DataContext = _mainViewModel,
                };
                MainWindow = window;
                window.Show();
            }
#pragma warning disable CA1031 // 启动失败:没有窗口时进程不会自行退出,还会占着单实例锁,让以后的启动静默退出。必须记录、提示并退出。
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _errorLog.Write(Lang.T("启动", "Startup"), ex.Message, ex);
                MessageBox.Show(
                    Lang.T($"程序无法启动,已记录到 logs\\error.txt:{Environment.NewLine}{ex.Message}", $"The program cannot start; the error was recorded in logs\\error.txt:{Environment.NewLine}{ex.Message}"),
                    "Zevision",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        /// <inheritdoc />
        protected override void OnExit(ExitEventArgs e)
        {
            // 退出时逐项清理:任何一步失败都只记录,不能妨碍后面的步骤 (尤其是保存配置和关闭串口)。
            RunOnExit(Lang.T("保存通讯配置", "Save communication settings"), () => _mainViewModel?.SavePreferences());
            RunOnExit(Lang.T("释放视图模型", "Release view model"), () => _mainViewModel?.Dispose());
            RunOnExit(Lang.T("关闭串口", "Close connection"), () => _deviceService?.Dispose());
            RunOnExit(Lang.T("释放单实例锁", "Release single-instance lock"), () => _singleInstance?.Dispose());
            base.OnExit(e);
        }

        /// <summary>
        /// 切换语言后重新启动:先保存配置 (新进程启动时会读取其中的语言),再释放单实例锁
        /// (否则新进程会把本窗口切到前台后直接退出),然后启动新进程并退出本进程 (OnExit 断开连接)。
        /// </summary>
        private void OnRestartRequested(object? sender, EventArgs e)
        {
            _mainViewModel?.SavePreferences();
            var exe = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(exe))
            {
                ShowRestartFailed(Lang.T("找不到程序文件", "The program file was not found"));
                return;
            }

            _singleInstance?.Dispose();
            _singleInstance = null;
            try
            {
                using (Process.Start(exe))
                {
                }
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
            {
                // 启动失败:重新取得单实例锁,继续使用当前程序;新语言下次启动时生效。
                _singleInstance = SingleInstance.Acquire();
                _errorLog?.Write(Lang.T("切换语言", "Switch language"), ex.Message, ex);
                ShowRestartFailed(ex.Message);
                return;
            }

            Shutdown();
        }

        private static void ShowRestartFailed(string reason) =>
            MessageBox.Show(
                Lang.T("无法重新启动程序,新语言将在下次启动时生效:", "Cannot restart the program; the new language applies at the next start: ") + reason,
                "Zevision",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

        private void RunOnExit(string step, Action action)
        {
            try
            {
                action();
            }
#pragma warning disable CA1031 // 退出清理是尽力而为:异常已记录到 error.txt,继续后面的清理步骤。
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _errorLog?.Write(Lang.T("退出", "Exit"), step + Lang.T("失败: ", " failed: ") + ex.Message, ex);
            }
        }

        /// <summary>
        /// UI 线程上未处理的异常:记录并提示后继续运行。
        /// 这是一个调试/通讯工具,继续运行让用户可以保存日志或重试,比直接退出更有用。
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _errorLog?.Write(Lang.T("未处理异常 (UI)", "Unhandled exception (UI)"), e.Exception.Message, e.Exception);
            var message = Lang.T($"程序发生意外错误,已记录到 logs\\error.txt:{Environment.NewLine}{e.Exception.Message}", $"An unexpected error occurred and was recorded in logs\\error.txt:{Environment.NewLine}{e.Exception.Message}");
            if (MainWindow != null && MainWindow.IsLoaded)
            {
                MessageBox.Show(MainWindow, message, "Zevision", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show(message, "Zevision", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            e.Handled = true;
        }

        /// <summary>后台线程上未处理的异常:进程即将终止,只能记录。</summary>
        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            _errorLog?.Write(
                e.IsTerminating ? Lang.T("未处理异常 (后台,程序终止)", "Unhandled exception (background, terminating)") : Lang.T("未处理异常 (后台)", "Unhandled exception (background)"),
                exception?.Message ?? e.ExceptionObject?.ToString() ?? Lang.T("(未知)", "(unknown)"),
                exception);
        }

        /// <summary>没有被 await 的 Task 中的异常:记录并标记为已观察,避免被静默丢弃。</summary>
        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            _errorLog?.Write(Lang.T("未观察的任务异常", "Unobserved task exception"), e.Exception.GetBaseException().Message, e.Exception);
            e.SetObserved();
        }
    }
}
