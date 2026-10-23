using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Zevision.Communication;
using Zevision.Core.Devices;
using Zevision.Core.Localization;
using Zevision.Storage;

namespace Zevision.Bridge.Hosting
{
    /// <summary>
    /// 一步组装并启动中间层:客户软件 (.NET) 在自己的进程中加载中间层时调用 <see cref="Start(Xtc3BridgeSettings, EventHandler{BridgeLogEventArgs})"/>,
    /// 独立程序 <c>Zevision.Xtc3Bridge.exe</c> 也用它。
    /// </summary>
    /// <remarks>
    /// <para>组装内容:读取程序旁的 <c>Texts.json</c> (IMC300 的连接、握手消息;已读取过时不再读取),错误日志
    /// (<see cref="Xtc3BridgeSettings.LogFolder"/> 中的 <see cref="ErrorLogFileName"/>),<see cref="ZevisionDeviceService"/>,
    /// 本地参数文件,<see cref="Xtc3Bridge"/>。</para>
    /// <para>部署:程序文件夹中应有 <c>Zevision.Communication.dll</c>、<c>Zevision.Commands.dll</c>、<c>Zevision.Core.dll</c>、
    /// <c>Zevision.Storage.dll</c>、<c>Zevision.Xtc3.dll</c>、<c>Zevision.Bridge.dll</c>、<c>Zevision.Bridge.Hosting.dll</c> 和 <c>Texts.json</c>。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using var host = Xtc3BridgeHost.Start(new Xtc3BridgeSettings { HostPort = "COM2", Device = "192.168.5.88" });
    /// // 主机侧的 XTC/3 通信经 COM1 进行;退出时 Dispose 停止中间层。
    /// </code>
    /// </example>
    public sealed class Xtc3BridgeHost : IDisposable
    {
        /// <summary>错误日志的文件名。</summary>
        public const string ErrorLogFileName = "xtc3bridge-error.txt";

        private readonly ZevisionDeviceService _service;
        private bool _disposed;

        private Xtc3BridgeHost(Xtc3Bridge bridge, ZevisionDeviceService service, string errorLogPath, string stateFilePath)
        {
            Bridge = bridge;
            _service = service;
            ErrorLogPath = errorLogPath;
            StateFilePath = stateFilePath;
        }

        /// <summary>中间层 (状态、影子对象、事件)。</summary>
        public Xtc3Bridge Bridge { get; }

        /// <summary>错误日志的完整路径。</summary>
        public string ErrorLogPath { get; }

        /// <summary>本地参数文件的完整路径。</summary>
        public string StateFilePath { get; }

        /// <summary>
        /// 组装并启动中间层 (立即返回;与 IMC300 的连接和主机侧串口在后台打开,结果见 <paramref name="log"/>)。
        /// </summary>
        /// <param name="settings">设置。</param>
        /// <param name="log">记录 (后台线程上调用);可为 null。</param>
        /// <exception cref="ArgumentException">设置无效。</exception>
        /// <exception cref="IOException">无法创建日志文件夹。</exception>
        public static Xtc3BridgeHost Start(Xtc3BridgeSettings settings, EventHandler<BridgeLogEventArgs>? log = null) =>
            Start(settings, log, DeviceTransports.Create, DeviceTransports.Create);

        /// <summary>测试用:指定 IMC300 和主机侧串口的传输。</summary>
        internal static Xtc3BridgeHost Start(
            Xtc3BridgeSettings settings,
            EventHandler<BridgeLogEventArgs>? log,
            Func<ConnectionEndpoint, IDeviceTransport> deviceTransports,
            Func<ConnectionEndpoint, IDeviceTransport> hostTransports)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var options = settings.ToOptions();
            if (string.Equals(settings.Language, "zh", StringComparison.OrdinalIgnoreCase) || string.Equals(settings.Language, "en", StringComparison.OrdinalIgnoreCase))
            {
                Lang.Initialize(string.Equals(settings.Language, "zh", StringComparison.OrdinalIgnoreCase) ? UiLanguage.Chinese : UiLanguage.English);
            }

            string? textsProblem = null;
            if (!TextCatalog.Keys.Any())
            {
                var texts = TextFile.Load(TextFile.DefaultPath);
                if (texts.IsSuccess)
                {
                    TextCatalog.Initialize(texts.Value);
                }
                else
                {
                    textsProblem = TextFile.DefaultPath + ": " + texts.Error + " IMC300 messages show their keys.";
                }
            }

            var logFolder = settings.Resolve(settings.LogFolder);
            Directory.CreateDirectory(logFolder);
            var errorLogPath = Path.Combine(logFolder, ErrorLogFileName);
            var errorLog = new FileErrorLog(errorLogPath, "==== Zevision.Bridge " + typeof(Xtc3Bridge).Assembly.GetName().Version + " ====");
            var statePath = settings.Resolve(settings.StateFile);
            var service = new ZevisionDeviceService(deviceTransports, errorLog);
            Xtc3Bridge bridge;
            try
            {
                bridge = new Xtc3Bridge(options, service, new JsonFileBridgeStateStore(statePath), errorLog, hostTransports);
            }
            catch
            {
                service.Dispose();
                throw;
            }

            if (log != null)
            {
                bridge.Log += log;
            }

            if (textsProblem != null)
            {
                errorLog.Write("XTC3 bridge", textsProblem);
                log?.Invoke(bridge, new BridgeLogEventArgs(BridgeLogLevel.Warning, textsProblem));
            }

            _ = bridge.StartAsync(); // 同步完成:连接和串口在后台运行
            return new Xtc3BridgeHost(bridge, service, errorLogPath, statePath);
        }

        /// <summary>停止中间层 (关闭主机侧串口,断开 IMC300)。</summary>
        public Task StopAsync() => Bridge.StopAsync();

        /// <summary>停止中间层并释放与 IMC300 的连接。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Bridge.Dispose();
            _service.Dispose();
        }
    }
}
