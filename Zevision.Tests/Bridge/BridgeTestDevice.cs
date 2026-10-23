using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Bridge;
using Zevision.Communication;
using Zevision.Xtc3;

namespace Zevision.Tests
{
    /// <summary>
    /// 中间层测试用的 IMC300:<see cref="FakeDeviceService"/> 加上系统项、两个传感器和输出源、6 个输出、8 个输入,
    /// 以及指定 Film 的完整影子对象 (结构与实机按编号创建后相同,值的格式按实测帮助表)。
    /// </summary>
    internal static class BridgeTestDevice
    {
        public static FakeDeviceService Create(params int[] films)
        {
            var device = new FakeDeviceService()
                .With("G03", "IMC300", "Device Name")
                .With("G05", "IMC", "Model Number")
                .With("G06", "XXXXXXXX", "System Serial Number")
                .With("G08", "1.8.11.202606101229", "System Software Version")
                .With("G0C", "243", "Power On Count")
                .With("G0E01", "1", "Display Off/On")
                .With("G0E02", "0", "Front Panel Lock")
                .With("G0E04", "0", "Dim Timer")
                .With("G0F01", "1", "Keystroke Off/On")
                .With("G1402", "192.168.5.88", "IP Address")
                .With("G1403", "255.255.255.0", "Subnet Mask")
                .With("C01", "0", "Stop/Start Process")
                .With("C02", "1", "Stop on Alarm")
                .With("C06", "0", "Layer Control")
                .With("C07", "0.00", "Manual Power Setpoint")
                .With("C09", "Ready", "Current State")
                .With("C11", "0.00", "Current Power")
                .With("C12", "0.0000", "Current Rate")
                .With("C13", "0.0000", "Raw Rate")
                .With("C15", "0.0000", "Current Thickness")
                .With("C16", "5980000.0000", "Raw Frequency")
                .With("C17", "12", "Crystal Life")
                .With("C18", "0", "Active Crystal Failed")
                .With("C1C", "0", "Test Mode")
                .With("C1F", "0", "Stop Reason")
                .With("C20", "0", "Stability Total")
                .With("C21", "0", "Quality Counts")
                .With("M", string.Empty, "Material Settings")
                .With("M00", string.Empty, "Add or Delete material from the end of the list")
                .With("M01", string.Empty, "Material M01")
                .With("M0101", "Al", "Name")
                .With("F", string.Empty, "Film Settings")
                .With("F01", string.Empty, "Film F01")
                .With("F0101", "Al", "Name")
                .With("L", string.Empty, "Layer Settings")
                .With("L01", string.Empty, "Layer L01")
                .With("L0101", "Al", "Layer Name")
                .With("P01", "1", "Set Active Process")
                .With("P02", string.Empty, "Process List")
                .With("P0201", string.Empty, "Process")
                .With("P020101", "Process 1", "Name");
            for (var sensor = 1; sensor <= 2; sensor++)
            {
                var n = "N" + Hex(sensor);
                device.With(n, string.Empty, "Sensor " + n)
                    .With(n + "01", "Sensor", "Name")
                    .With(n + "02", "0", "Sensor Type")
                    .With(n + "09", "1", "Current Position")
                    .With(n + "10FE", "1", "Number of QCMs")
                    .With(n + "100102", "100", "QCM Health")
                    .With(n + "11", "0", "Active QCM ID")
                    .With(n + "20", "0.0000000000", "Rate Deviation");
            }

            for (var source = 1; source <= 2; source++)
            {
                var s = "S" + Hex(source);
                device.With(s, string.Empty, "Source " + s)
                    .With(s + "01", "Source", "Name")
                    .With(s + "07", "4", "Number of Crucibles")
                    .With(s + "0A", "10.00", "Max Output");
            }

            for (var output = 1; output <= 6; output++)
            {
                device.With("O" + Hex(output) + "05", "0", "Raw State");
            }

            for (var input = 1; input <= 8; input++)
            {
                device.With("I" + Hex(input) + "04", "0", "Active State");
            }

            device.With("NFE", "2", "Number of Sensors").With("SFE", "2", "Number Of Sources").With("OFE", "6", "Number of Outputs").With("IFE", "8", "Number of Inputs");
            foreach (var number in films)
            {
                AddShadowFilm(device, new ShadowFilm(number, ShadowFilm.DefaultBase));
            }

            return device;
        }

        /// <summary>完整的影子对象 (与 <c>ShadowObjects</c> 核对的结构相同)。</summary>
        public static void AddShadowFilm(FakeDeviceService device, ShadowFilm film)
        {
            var number = film.ObjectNumber.ToString(CultureInfo.InvariantCulture);
            var m = film.MaterialId;
            device.With(m, string.Empty, "Material " + m).With(m + "01", film.Name, "Name").With(m + "03", "1.000", "Density (g/cm^3)")
                .With(m + "04", "1.000", "Z Ratio").With(m + "07", "100.00", "Tooling");

            var f = film.FilmId;
            device.With(f, string.Empty, "Film " + f).With(f + "01", film.Name, "Name").With(f + "02", number, "Material")
                .With(f + "03FE", "2", "Number of Pre Conditions");
            foreach (var step in new[] { "030101", "030201", "050101" })
            {
                var item = f + step.Substring(0, 4);
                device.With(item + "01", "0:00:00", "Start Ramp/Rise time").With(item + "02", "0.00", "Ramp Power").With(item + "03", "0:00:00", "Hold Time");
            }

            device.With(f + "0401", "0", "Delay Option").With(f + "0402", "0:00:00", "Control Delay Time").With(f + "0403", "5.00", "Shutter Delay Accuracy")
                .With(f + "0404", "0:01:00", "Shutter Delay Timeout").With(f + "0405", "0", "Transfer Sensor").With(f + "0406", "100.00", "Transfer Tooling")
                .With(f + "05FE", "1", "Number of Post Conditions")
                .With(f + "0601", "0", "Rate Watcher Enabled").With(f + "0602", "0:00:00", "Rate Watcher Time").With(f + "0603", "5", "Rate Watcher Accuracy")
                .With(f + "0604", "0", "Ion Assisted Deposit")
                .With(f + "07FE", "1", "Number of Sensors").With(f + "070101", "1", "Sensor Number").With(f + "070102", "0", "Crystal Position")
                .With(f + "070103", "100.00", "Tooling").With(f + "070105", "3", "Failure Action")
                .With(f + "0807", "0.0", "Quality Percent").With(f + "0808", "0", "Quality Counts").With(f + "0809", "0", "Stability Single").With(f + "080A", "0", "Stability Total")
                .With(f + "0901", "1", "Source Setting").With(f + "0902", "0", "Crucible").With(f + "0903", "5.000", "P Term").With(f + "0904", "0.000", "I Term")
                .With(f + "0905", "0.000", "D Term").With(f + "0906", "80.00", "Maximum Power").With(f + "0907", "0", "Max Power Option");

            var l = film.LayerId;
            device.With(l, string.Empty, "Layer " + l).With(l + "01", film.Name, "Layer Name").With(l + "02FE", "1", "Number of Layer Films")
                .With(l + "020101", number, "Film").With(l + "020102", "0.000", "Rate").With(l + "020103", "0.000", "Final Thickness")
                .With(l + "020106FE", "1", "Number of Rate Ramps").With(l + "0201060101", "0.0000", "Start Thickness")
                .With(l + "0201060102", "0:00:00", "Ramp Time").With(l + "0201060103", "0.000", "New Rate");

            var p = film.ProcessId;
            device.With(p, string.Empty, "Process").With(p + "01", film.Name, "Name").With(p + "02", "1", "Active Layer").With(p + "04", "0", "Auto Start Next")
                .With(p + "07FE", "1", "Number Of Layers").With(p + "0701", number, "Layer").With(p + "08", "0", "Deposit/Etch Mode").With(p + "0B", "0", "Start without Backup XTAL");
        }

        public static string Hex(int number) => number.ToString("X2", CultureInfo.InvariantCulture);
    }

    /// <summary>直接驱动 <see cref="BridgeEngine"/> 的测试环境:手动时钟、收集的记录、内存中的本地参数。</summary>
    internal sealed class BridgeHarness
    {
        public BridgeHarness(FakeDeviceService device, Xtc3BridgeOptions? options = null, MemoryBridgeStateStore? store = null)
        {
            Device = device;
            Store = store ?? new MemoryBridgeStateStore();
            Options = options ?? new Xtc3BridgeOptions(new ConnectionOptions(new TcpSettings("127.0.0.1", TcpSettings.DefaultPort), new FrameOptions(true, true)));
            Engine = new BridgeEngine(Options, device, Store, () => Now, (level, message, _) => Logs.Add(level + ": " + message));
        }

        public FakeDeviceService Device { get; }

        public MemoryBridgeStateStore Store { get; }

        public Xtc3BridgeOptions Options { get; }

        public BridgeEngine Engine { get; }

        public TimeSpan Now { get; set; } = TimeSpan.FromSeconds(100);

        public List<string> Logs { get; } = new List<string>();

        public static async Task<BridgeHarness> ReadyAsync(FakeDeviceService device, Xtc3BridgeOptions? options = null, MemoryBridgeStateStore? store = null)
        {
            var harness = new BridgeHarness(device, options, store);
            var prepared = await harness.Engine.PrepareAsync(CancellationToken.None);
            if (!prepared.IsSuccess)
            {
                throw new InvalidOperationException(prepared.Error);
            }

            harness.Engine.IsOnline = true;
            return harness;
        }

        /// <summary>执行命令,返回各结果的文本 (<c>ACK [Float 2.5]</c>、<c>B</c>)。</summary>
        public async Task<IReadOnlyList<Xtc3CommandResult>> RunAsync(params Xtc3Command[] commands) =>
            await Engine.ExecuteAsync(commands, CancellationToken.None);

        public async Task<Xtc3CommandResult> RunOneAsync(Xtc3Command command) => (await RunAsync(command))[0];

        /// <summary>采样一个 tick (时钟前进 250 ms)。</summary>
        public async Task SampleAsync()
        {
            Now += Xtc3Protocol.TickInterval;
            if (!await Engine.SampleAsync(CancellationToken.None))
            {
                throw new InvalidOperationException("Sampling failed.");
            }
        }

        public List<string> Sent(string prefix) => Device.Commands.Where(c => c.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }
}
