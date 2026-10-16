using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Communication;

namespace Zevision.Sample
{
    /// <summary>
    /// 只引用 Zevision.Communication.dll 和 Zevision.Commands.dll 的最小示例:连接、握手、查询,只读不写。
    /// </summary>
    /// <remarks>
    /// 用法:
    /// <code>
    /// Zevision.Sample COM3 [波特率] [-c] [-l]
    /// Zevision.Sample 192.168.0.10 [-c] [-l]
    /// </code>
    /// <c>-c</c>/<c>-l</c>:设备开启了校验和 (G1304 / G1407) 或长度 (G1305 / G1408) 时加上,须与设备一致。
    /// </remarks>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: Zevision.Sample <COMx [baud] | IP address> [-c] [-l]");
                return 2;
            }

            // 1. 通讯参数:连接目标 + 帧格式 + 响应空闲超时。
            var target = args[0];
            var numbers = args.Skip(1).Where(a => !a.StartsWith("-", StringComparison.Ordinal)).ToList();
            ConnectionEndpoint endpoint = target.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                ? new SerialSettings(target, numbers.Count > 0 ? int.Parse(numbers[0], CultureInfo.InvariantCulture) : 115200)
                : new TcpSettings(target, TcpSettings.DefaultPort);
            var frame = new FrameOptions(checksumEnabled: args.Contains("-c"), lengthEnabled: args.Contains("-l"));
            var options = new ConnectionOptions(endpoint, frame, TimeSpan.FromSeconds(1));

            // 2. 客户端:一个实例对应一台设备。事件在后台线程触发;库不写日志,需要时在这里记录。
            using var client = new ZevisionClient(options);
            client.FrameSent += (s, e) => Console.WriteLine("TX " + e.Command);
            client.ConnectionLost += (s, e) => Console.WriteLine("Connection lost: " + e.Exception.Message);
            client.HandlerFailed += (s, e) => Console.WriteLine($"{e.EventName} handler failed: {e.Exception}");

            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var connected = await client.ConnectAsync(cancel.Token).ConfigureAwait(false);
            if (!connected.IsSuccess)
            {
                Console.WriteLine(connected.Error); // 例如 "ConnectFailed: Cannot connect to COM3, 115200, 8N1: ..."
                return 1;
            }

            // 3. 用指令集生成命令,再发送。
            await QueryAsync(client, ZevisionCommandSet.Find("H01")!.Build(CommandActions.None), cancel.Token).ConfigureAwait(false);
            await QueryAsync(client, ZevisionCommandSet.Query("G03"), cancel.Token).ConfigureAwait(false);   // 设备名
            await QueryAsync(client, ZevisionCommandSet.Query("C09"), cancel.Token).ConfigureAwait(false);   // 工艺状态
            await QueryAsync(client, ZevisionCommandSet.Query("MFE"), cancel.Token).ConfigureAwait(false);   // 材料数量

            // 一帧多条命令:响应按顺序一一对应。
            var density = ZevisionCommandSet.Find("M0103")!;
            await QueryAsync(client, density.Build(CommandActions.Query, 1) + ";" + ZevisionCommandSet.Query("M0101"), cancel.Token).ConfigureAwait(false);

            // 更新指令只演示生成,不发送 (示例不修改设备设置)。范围和选项见 Min/Max/Options。
            Console.WriteLine($"Update example (not sent): {density.Build(CommandActions.Update, "2.700", 1)}  " +
                $"{density.GetLabel(TextLanguage.English)}, range {density.GetRangeText(TextLanguage.English)}");

            await client.DisconnectAsync().ConfigureAwait(false);
            return 0;
        }

        private static async Task QueryAsync(ZevisionClient client, string command, CancellationToken cancellationToken)
        {
            var result = await client.SendAsync(command, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                Console.WriteLine($"   {result.Error!.Kind}: {result.Error.Message}");
                return;
            }

            foreach (var response in result.Value.Responses)
            {
                Console.WriteLine(response.IsSuccess
                    ? $"   {response.Command} = {response.Data}"
                    : $"   {response.Command} -{response.RawErrorCode} {response.ErrorCode.Describe()}");
            }
        }
    }
}
