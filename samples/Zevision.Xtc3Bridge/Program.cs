using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Bridge;
using Zevision.Bridge.Hosting;
using Zevision.Xtc3;

namespace Zevision.BridgeHost
{
    /// <summary>
    /// XTC/3S 中间层的控制台程序 (调试和联调用):
    /// <list type="bullet">
    /// <item><c>bridge</c>:运行中间层,主机侧为虚拟串口对的一端,设备侧为 IMC300 (TCP 或 RS232)。Ctrl+C 停止。
    /// 设置取自程序旁的 <see cref="Xtc3BridgeSettings.DefaultFileName"/> (或 <c>--settings</c>),命令行参数优先;不带参数运行时与 <c>bridge</c> 相同。</item>
    /// <item><c>init</c>:按命令行参数生成设置文件。</item>
    /// <item><c>client</c>:从虚拟串口对的另一端按 XTC/3 Standard Protocol 发送命令并显示响应 (代替主机软件)。</item>
    /// </list>
    /// </summary>
    internal static class Program
    {
        private const string Usage = @"Zevision XTC/3S bridge (console)

  bridge [--settings <file>] [--host <COMx>] [--host-baud 115200] [--device <IP | COMy[:baud]>] [--create-shadow] [--base B0] [--state <file>] [--quiet]
      Runs the bridge: answers XTC/3 Standard Protocol on the host port, talks Zevision to the IMC300.
      Settings come from xtc3bridge.settings.json next to the program (or --settings); options override them.
      Without any arguments the program runs the bridge with that file.
      --create-shadow  create missing shadow objects (Film 1-9 -> objects <base>+1 .. <base>+9) on the IMC300
      --state          local parameters file (default xtc3bridge.json next to the settings file)
      --quiet          do not print every host packet

  init --host <COMx> --device <IP | COMy[:baud]> [other bridge options]
      Writes xtc3bridge.settings.json next to the program (or --settings <file>).

  client --port <COMx> [--baud 115200] [--repeat N] [--interval ms] <command> [<command> ...]
      Sends all commands in one packet (like the host software) and prints the response.
      Commands: H1 H2 H3 | E:text | S<id> | S0[:tick] | R<id>[:value] | QF<id>:<film> | UF<id>:<film>:<value> | QG<id> | UG<id>:<value>
      Example: client --port COM1 H1 S4 QF8:1 QG0 S0

Examples:
  Zevision.Xtc3Bridge.exe init --host COM2 --device 192.168.5.88
  Zevision.Xtc3Bridge.exe
  Zevision.Xtc3Bridge.exe bridge --host COM2 --device 192.168.5.88
  Zevision.Xtc3Bridge.exe client --port COM1 H1 S2 S3 S4";

        private static readonly object ConsoleLock = new object();

        private static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                var options = Arguments.Parse(args.Skip(1));
                return args.FirstOrDefault() switch
                {
                    null when File.Exists(DefaultSettingsPath) => await RunBridgeAsync(options).ConfigureAwait(false),
                    "bridge" => await RunBridgeAsync(options).ConfigureAwait(false),
                    "init" => Init(options),
                    "client" => RunClient(options),
                    _ => ShowUsage(),
                };
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ShowUsage();
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static int ShowUsage()
        {
            Console.WriteLine(Usage);
            return 2;
        }

        // ---------------------------------------------------------------- bridge

        private static string DefaultSettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Xtc3BridgeSettings.DefaultFileName);

        private static async Task<int> RunBridgeAsync(Arguments arguments)
        {
            var settings = Settings(arguments);
            var stopped = new TaskCompletionSource<bool>();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stopped.TrySetResult(true);
            };

            using var host = Xtc3BridgeHost.Start(settings, (_, e) => Print(
                e.Level == BridgeLogLevel.Info ? ConsoleColor.Gray : e.Level == BridgeLogLevel.Warning ? ConsoleColor.Yellow : ConsoleColor.Red,
                e.Level + "  " + e.Message));
            if (!arguments.Flag("--quiet"))
            {
                host.Bridge.HostExchange += (_, e) => Print(ConsoleColor.Cyan, Describe(e));
            }

            Print(ConsoleColor.Gray, "Host port " + settings.HostPort + ", IMC300 " + Xtc3BridgeSettings.ParseDevice(settings.Device) + ", shadow objects base " + settings.ShadowBase
                + ", local parameters " + host.StateFilePath + ", errors " + host.ErrorLogPath + ". Ctrl+C stops.");
            await stopped.Task.ConfigureAwait(false);
            Print(ConsoleColor.Gray, "Stopping ...");
            await host.StopAsync().ConfigureAwait(false);
            return 0;
        }

        /// <summary>设置:设置文件 (<c>--settings</c>,否则程序旁的默认文件,不存在时为初值),再用命令行参数覆盖。</summary>
        private static Xtc3BridgeSettings Settings(Arguments arguments)
        {
            var path = arguments.Value("--settings") ?? DefaultSettingsPath;
            var settings = File.Exists(path) || arguments.Value("--settings") != null ? Xtc3BridgeSettings.Load(path) : new Xtc3BridgeSettings();
            settings.HostPort = arguments.Value("--host") ?? settings.HostPort;
            settings.HostBaudRate = arguments.Int("--host-baud", settings.HostBaudRate);
            settings.Device = arguments.Value("--device") ?? settings.Device;
            settings.ShadowBase = arguments.Value("--base") ?? settings.ShadowBase;
            settings.StateFile = arguments.Value("--state") ?? settings.StateFile;
            settings.CreateShadowObjects |= arguments.Flag("--create-shadow");
            settings.Language = settings.Language.Length == 0 ? "en" : settings.Language; // 控制台的输出为英文
            settings.ToOptions(); // 检查,无效时抛出 ArgumentException
            return settings;
        }

        /// <summary>按命令行参数写设置文件 (不从要写的文件读取)。</summary>
        private static int Init(Arguments arguments)
        {
            var path = arguments.Value("--settings") ?? DefaultSettingsPath;
            var settings = new Xtc3BridgeSettings
            {
                HostPort = arguments.Value("--host") ?? "COM2",
                HostBaudRate = arguments.Int("--host-baud", 115200),
                Device = arguments.Required("--device"),
                ShadowBase = arguments.Value("--base") ?? "B0",
                StateFile = arguments.Value("--state") ?? "xtc3bridge.json",
                CreateShadowObjects = arguments.Flag("--create-shadow"),
                Language = "en",
            };
            settings.ToOptions();
            settings.Save(path);
            Print(ConsoleColor.Gray, "Wrote " + Path.GetFullPath(path) + ".");
            return 0;
        }

        private static string Describe(HostExchangeEventArgs e)
        {
            if (e.Response == null)
            {
                return "HOST  (discarded " + e.Request.Length + " bytes)";
            }

            if (e.Commands.Count == 0)
            {
                return "HOST  packet error " + (char)e.Response[4] + "  [" + BitConverter.ToString(e.Request) + "]";
            }

            var pairs = e.Commands.Zip(e.Results, (c, r) => c.Name + Arguments(c) + " → " + r);
            return "HOST  " + string.Join(" | ", pairs) + "  (" + e.Duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms)";

            static string Arguments(Xtc3Command command) =>
                command.Parameters.Count == 0 ? string.Empty : "(" + string.Join(", ", command.Parameters.Select(Show)) + ")";
        }

        // ---------------------------------------------------------------- client

        private static int RunClient(Arguments arguments)
        {
            var portName = arguments.Required("--port");
            var commands = arguments.Positional.ToList();
            if (commands.Count == 0)
            {
                throw new ArgumentException("Give at least one command.");
            }

            var repeat = arguments.Int("--repeat", 1);
            var interval = arguments.Int("--interval", 500);
            using var port = new SerialPort(portName, arguments.Int("--baud", 115200), Parity.None, 8, StopBits.One) { ReadTimeout = 200 };
            port.Open();
            byte? timer = null;
            for (var i = 0; i < repeat; i++)
            {
                if (i > 0)
                {
                    Thread.Sleep(interval);
                }

                // S0 不带 tick 时用上一个响应的 Timer (第一次先发一个 E 取得)。
                if (timer == null && commands.Any(c => c.Equals("S0", StringComparison.OrdinalIgnoreCase)))
                {
                    timer = Exchange(port, new[] { Xtc3Command.Echo(string.Empty) })?.Timer;
                }

                var parsed = commands.Select(c => ParseCommand(c, timer ?? 0)).ToList();
                var started = DateTime.UtcNow;
                var response = Exchange(port, parsed);
                if (response == null)
                {
                    Print(ConsoleColor.Red, "No response in 5 s.");
                    return 1;
                }

                timer = response.Timer;
                var elapsed = (DateTime.UtcNow - started).TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture);
                if (response.PacketError is { } error)
                {
                    Print(ConsoleColor.Red, "Packet error " + (char)error + " (CCB 0x" + response.ConditionCode.ToString("X2", CultureInfo.InvariantCulture) + ")");
                    continue;
                }

                Print(ConsoleColor.Gray, "Timer " + response.Timer + ", " + elapsed + " ms");
                for (var n = 0; n < parsed.Count; n++)
                {
                    var result = response.Results[n];
                    var text = result.IsSuccess ? "ACK " + string.Join(", ", result.Values.Select(Show)) : "error " + (char)result.Error!.Value;
                    Print(result.IsSuccess ? ConsoleColor.Green : ConsoleColor.Yellow, "  " + parsed[n].Name.PadRight(6) + " " + text);
                }
            }

            return 0;
        }

        /// <summary>发送一个命令包,等待一个响应包 (最长 5 s)。</summary>
        private static Xtc3ResponsePacket? Exchange(SerialPort port, IReadOnlyList<Xtc3Command> commands)
        {
            var packet = Xtc3HostCodec.EncodeCommands(commands);
            port.DiscardInBuffer();
            port.Write(packet, 0, packet.Length);
            var decoder = new Xtc3PacketDecoder();
            var buffer = new byte[4096];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(5))
            {
                int count;
                try
                {
                    count = port.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }

                foreach (var received in decoder.Push(buffer, count, clock.Elapsed))
                {
                    if (received.IsMessage)
                    {
                        return Xtc3HostCodec.DecodeResponse(received.Message, commands);
                    }
                }
            }

            return null;
        }

        /// <summary>命令文本:<c>H1</c>、<c>E:text</c>、<c>S4</c>、<c>S0[:tick]</c>、<c>R11:50</c>、<c>QF8:1</c>、<c>UF8:1:2.5</c>、<c>QG0</c>、<c>UG0:2</c>。</summary>
        private static Xtc3Command ParseCommand(string text, byte timer)
        {
            var parts = text.Split(':');
            var head = parts[0].ToUpperInvariant();
            int Id(int skip) => int.Parse(head.Substring(skip), CultureInfo.InvariantCulture);
            string Part(int index) => index < parts.Length ? parts[index] : throw new ArgumentException(text + ": a value is missing.");
            if (head == "E")
            {
                return Xtc3Command.Echo(text.Length > 2 ? text.Substring(2) : string.Empty);
            }

            if (head.StartsWith("QF", StringComparison.Ordinal))
            {
                return Xtc3Command.QueryFilm(Id(2), int.Parse(Part(1), CultureInfo.InvariantCulture));
            }

            if (head.StartsWith("UF", StringComparison.Ordinal))
            {
                var id = Id(2);
                var type = Xtc3Parameters.FindFilm(id)?.Type ?? Xtc3ValueType.Integer;
                return Xtc3Command.UpdateFilm(id, int.Parse(Part(1), CultureInfo.InvariantCulture), Number(type, Part(2)));
            }

            if (head.StartsWith("QG", StringComparison.Ordinal))
            {
                return Xtc3Command.QueryGeneral(Id(2));
            }

            if (head.StartsWith("UG", StringComparison.Ordinal))
            {
                return Xtc3Command.UpdateGeneral(Id(2), int.Parse(Part(1), CultureInfo.InvariantCulture));
            }

            if (head.StartsWith("H", StringComparison.Ordinal))
            {
                return Xtc3Command.Hello(Id(1));
            }

            if (head.StartsWith("S", StringComparison.Ordinal))
            {
                var id = Id(1);
                return id == 0 ? Xtc3Command.ProcessInformation(parts.Length > 1 ? byte.Parse(parts[1], CultureInfo.InvariantCulture) : timer) : Xtc3Command.Status(id);
            }

            if (head.StartsWith("R", StringComparison.Ordinal))
            {
                var id = Id(1);
                return Xtc3Syntax.RemoteParameter(id) is { } type ? Xtc3Command.Remote(id, Number(type, Part(1))) : Xtc3Command.Remote(id);
            }

            throw new ArgumentException("Unknown command: " + text);
        }

        private static Xtc3Value Number(Xtc3ValueType type, string text) => type switch
        {
            Xtc3ValueType.Float => Xtc3Value.FromFloat(float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)),
            Xtc3ValueType.Byte => Xtc3Value.FromByte(byte.Parse(text, CultureInfo.InvariantCulture)),
            _ => Xtc3Value.FromInteger(int.Parse(text, CultureInfo.InvariantCulture)),
        };

        private static string Show(Xtc3Value value) => value.Type switch
        {
            Xtc3ValueType.Float => value.Float.ToString("0.####", CultureInfo.InvariantCulture),
            Xtc3ValueType.String => "\"" + value.Text + "\"",
            Xtc3ValueType.Bytes => string.Join(".", value.Bytes.Select(b => b.ToString(CultureInfo.InvariantCulture))),
            Xtc3ValueType.Word => "0x" + value.Integer.ToString("X4", CultureInfo.InvariantCulture),
            _ => value.Integer.ToString(CultureInfo.InvariantCulture),
        };

        private static void Print(ConsoleColor color, string text)
        {
            lock (ConsoleLock)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + text);
                Console.ResetColor();
            }
        }

        /// <summary>命令行参数:<c>--name value</c>、<c>--flag</c> 和其余的位置参数。</summary>
        private sealed class Arguments
        {
            private static readonly HashSet<string> Flags = new HashSet<string>(StringComparer.Ordinal) { "--create-shadow", "--quiet" };
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly HashSet<string> _flags = new HashSet<string>(StringComparer.Ordinal);

            public List<string> Positional { get; } = new List<string>();

            public static Arguments Parse(IEnumerable<string> args)
            {
                var result = new Arguments();
                var list = args.ToList();
                for (var i = 0; i < list.Count; i++)
                {
                    if (Flags.Contains(list[i]))
                    {
                        result._flags.Add(list[i]);
                    }
                    else if (list[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        result._values[list[i]] = i + 1 < list.Count ? list[++i] : throw new ArgumentException(list[i] + " needs a value.");
                    }
                    else
                    {
                        result.Positional.Add(list[i]);
                    }
                }

                return result;
            }

            public string? Value(string name) => _values.TryGetValue(name, out var value) ? value : null;

            public string Required(string name) => Value(name) ?? throw new ArgumentException(name + " is required.");

            public int Int(string name, int fallback) => Value(name) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

            public bool Flag(string name) => _flags.Contains(name);
        }
    }
}
