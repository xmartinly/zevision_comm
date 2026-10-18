using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Communication;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Devices;
using Zevision.Core.Localization;
using Zevision.Core.Settings;
using Zevision.Storage;
using Zevision.ViewModels.Display;
using Zevision.ViewModels.Main;

namespace Zevision.Tests
{
    /// <summary>发送选项:显示格式、重复发送、多行输入、保存通讯数据。</summary>
    public class SendOptionsTests
    {
        // ---------- 显示格式 ----------

        [Fact]
        public void DisplayLines_Hex_32BytesPerLine()
        {
            var raw = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();

            var lines = FrameDisplayFormatter.ToDisplayLines(raw, Array.Empty<ByteRange>(), 160, DisplayFormat.Hex);

            Assert.Equal(2, lines.Count);
            Assert.StartsWith("00 01 02 03", lines[0]);
            Assert.EndsWith("1F", lines[0]);
            Assert.Equal("20 21 22 23 24 25 26 27", lines[1]);
        }

        [Fact]
        public void DisplayLines_Binary_16BytesPerLine()
        {
            var raw = Encoding.ASCII.GetBytes("{H01}");

            var lines = FrameDisplayFormatter.ToDisplayLines(raw, Array.Empty<ByteRange>(), 160, DisplayFormat.Binary);

            Assert.Equal("01111011 01001000 00110000 00110001 01111101", Assert.Single(lines));
        }

        [Fact]
        public void DisplayLines_HexAndBinary_AllByteValues()
        {
            var raw = Enumerable.Range(0, 256).Select(b => (byte)b).ToArray();

            var hex = FrameDisplayFormatter.ToDisplayLines(raw, Array.Empty<ByteRange>(), 160, DisplayFormat.Hex);
            var binary = FrameDisplayFormatter.ToDisplayLines(raw, Array.Empty<ByteRange>(), 160, DisplayFormat.Binary);

            Assert.Equal(string.Join(" ", raw.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))), string.Join(" ", hex));
            Assert.Equal(string.Join(" ", raw.Select(b => Convert.ToString(b, 2).PadLeft(8, '0'))), string.Join(" ", binary));
        }

        [Fact]
        public void DisplayLines_Ascii_UnchangedFromDefault()
        {
            var raw = Encoding.ASCII.GetBytes("{H01\n}");
            var ranges = new[] { new ByteRange(1, 4) };

            Assert.Equal(
                FrameDisplayFormatter.ToDisplayLines(raw, ranges, 160),
                FrameDisplayFormatter.ToDisplayLines(raw, ranges, 160, DisplayFormat.Ascii));
        }

        [Fact]
        public void FromSent_HexSplitsIntoContinuationLines()
        {
            var raw = new byte[40];

            var entries = FrameLogFormatter.FromSent(raw, DateTime.Now, DisplayFormat.Hex);

            Assert.Equal(2, entries.Count);
            Assert.All(entries, e => Assert.Equal(LogKind.Tx, e.Kind));
            Assert.False(entries[0].IsContinuation);
            Assert.True(entries[1].IsContinuation);
            Assert.Equal("{H01}", Assert.Single(FrameLogFormatter.FromSent(Encoding.ASCII.GetBytes("{H01}"), DateTime.Now, DisplayFormat.Ascii)).Text);
        }

        [Fact]
        public async Task Main_DisplayFormat_AppliesToFollowingFrames()
        {
            var transport = new FakeSerialTransport { Responder = _ => Encoding.ASCII.GetBytes("{H01+:x}") };
            using (var device = new ZevisionDeviceService(transport))
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);
                await WaitUntil(() => main.IsConnected && !main.ConnectCommand.CanExecute(null));
                main.CommandText = "H01";
                main.SendCommand.Execute(null);
                await WaitUntil(() => main.Log.Entries.Any(e => e.Kind == LogKind.Rx && e.Text == "{H01+:x}"));

                main.SendOptions.DisplayFormat = DisplayFormat.Hex;
                main.CommandText = "H01";
                await WaitUntil(() => main.SendCommand.CanExecute(null)); // 第一次发送的命令已结束
                main.SendCommand.Execute(null);

                await WaitUntil(() => main.Log.Entries.Any(e => e.Kind == LogKind.Rx && e.Text == "7B 48 30 31 2B 3A 78 7D"));
                var entries = Ui(() => main.Log.Entries.ToList());
                Assert.Contains(entries, e => e.Kind == LogKind.Tx && e.Text == "7B 48 30 31 7D");
                Assert.Contains(entries, e => e.Kind == LogKind.Tx && e.Text.StartsWith("{", StringComparison.Ordinal)); // 之前的数据仍是 ASCII
                await device.DisconnectAsync();
            }
        }

        // ---------- 重复发送 ----------

        [Fact]
        public async Task Run_NotRepeating_ExchangesOnce()
        {
            var options = new SendOptionsViewModel();
            var count = 0;

            await options.RunAsync(() => { count++; return Task.CompletedTask; }, () => true);

            Assert.Equal(1, count);
            Assert.False(options.IsRepeating);
        }

        [Fact]
        public async Task Run_Repeating_UntilStopped()
        {
            var options = new SendOptionsViewModel { IsRepeatEnabled = true, RepeatInterval = 100 };
            var count = 0;

            var run = options.RunAsync(() => { count++; return Task.CompletedTask; }, () => true);
            Assert.True(options.IsRepeating);
            await WaitUntil(() => count >= 3);
            options.StopRepeatCommand.Execute(null);
            await run;

            Assert.False(options.IsRepeating);
            var stoppedAt = count;
            await Task.Delay(250);
            Assert.Equal(stoppedAt, count);
        }

        [Fact]
        public async Task Run_Repeating_StopsWhenUnchecked()
        {
            var options = new SendOptionsViewModel { IsRepeatEnabled = true, RepeatInterval = 100 };

            var run = options.RunAsync(() => Task.CompletedTask, () => true);
            options.IsRepeatEnabled = false;
            await run;

            Assert.False(options.IsRepeating);
        }

        [Fact]
        public async Task Run_Repeating_StopsWhenCannotContinue()
        {
            var options = new SendOptionsViewModel { IsRepeatEnabled = true, RepeatInterval = 100 };
            var count = 0;

            await options.RunAsync(() => { count++; return Task.CompletedTask; }, () => count < 2);

            Assert.Equal(2, count);
        }

        [Fact]
        public void RepeatInterval_OnlyListedValues()
        {
            var options = new SendOptionsViewModel();

            options.RepeatInterval = 300;
            Assert.Equal(SendOptionsViewModel.DefaultRepeatInterval, options.RepeatInterval);
            options.RepeatInterval = 2000;
            Assert.Equal(2000, options.RepeatInterval);
        }

        [Fact]
        public async Task Main_RepeatSend_RepeatsAndBlocksOtherOperations()
        {
            var device = new FakeDeviceService().With("G01", "1.00");
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);
                await WaitUntil(() => main.IsConnected);

                main.SendOptions.IsRepeatEnabled = true;
                main.SendOptions.RepeatInterval = 100;
                main.CommandText = "QG01";
                main.SendCommand.Execute(null);
                await WaitUntil(() => device.CommandsSnapshot().Count(c => c == "QG01") >= 3);

                Assert.True(main.SendOptions.IsRepeating);
                Assert.False(main.SendQuickCommand.CanExecute("H01"));
                Assert.False(main.Config.SaveCommand.CanExecute(null)); // 重复期间不开始配置操作

                main.DisconnectCommand.Execute(null);
                await WaitUntil(() => !main.IsConnected && !main.SendOptions.IsRepeating);
                var sent = device.CommandsSnapshot().Count(c => c == "QG01");
                await Task.Delay(250);
                Assert.Equal(sent, device.CommandsSnapshot().Count(c => c == "QG01"));
            }
        }

        // ---------- 关闭窗口 / 切换语言前断开 ----------

        [Fact]
        public async Task Main_CloseConnection_StopsRepeatAndDisconnects()
        {
            var device = new FakeDeviceService().With("G01", "1.00");
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);
                await WaitUntil(() => main.IsConnected);
                main.SendOptions.IsRepeatEnabled = true;
                main.CommandText = "QG01";
                main.SendCommand.Execute(null);
                await WaitUntil(() => main.SendOptions.IsRepeating);

                await main.CloseConnectionAsync();

                Assert.False(main.IsConnected);
                await WaitUntil(() => !main.SendOptions.IsRepeating);
            }
        }

        [Fact]
        public async Task Main_SwitchLanguage_DisconnectsBeforeRestart()
        {
            var device = new FakeDeviceService();
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);
                await WaitUntil(() => main.IsConnected);
                bool? connectedAtRestart = null;
                main.RestartRequested += (s, e) => connectedAtRestart = main.IsConnected;

                main.SwitchLanguageCommand.Execute(null);
                await WaitUntil(() => connectedAtRestart != null);

                Assert.False(connectedAtRestart);
            }
        }

        [Fact]
        public async Task Main_Send_UppercasesCommandPart()
        {
            var device = new FakeDeviceService().With("M0101", "Al");
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);
                await WaitUntil(() => main.IsConnected);
                main.CommandText = "um0101:Aluminium";

                main.SendCommand.Execute(null);
                await WaitUntil(() => device.CommandsSnapshot().Count > 0);

                Assert.Equal("UM0101:Aluminium", device.CommandsSnapshot()[0]);
                Assert.Equal("UM0101:Aluminium", main.CommandText);
            }
        }

        [Fact]
        public async Task Main_Send_KeepsInputText()
        {
            var device = new FakeDeviceService().With("G01", "1.00");
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);
                await WaitUntil(() => main.IsConnected);
                main.CommandText = "QG01\nQG01";

                main.SendCommand.Execute(null);
                await WaitUntil(() => device.CommandsSnapshot().Count > 0);

                Assert.Equal("QG01\nQG01", main.CommandText);
            }
        }

        // ---------- 多行输入 ----------

        [Fact]
        public void Split_NewLinesSeparateCommands()
        {
            Assert.Equal(new[] { "QG01", "QG1304", "H01" }, ManualCommandPolicy.Split("QG01\r\nQG1304\n\n H01 "));
        }

        // ---------- 保存通讯数据 ----------

        [Fact]
        public async Task Writer_AppendsDisplayLines()
        {
            var path = Path.Combine(Path.GetTempPath(), "zevision_comm_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(path, "existing" + Environment.NewLine);
                var time = new DateTime(2026, 10, 16, 9, 42, 16, 334);
                var writer = CommunicationLogWriter.Create(path, new FakeErrorLog());
                writer.Append(new[] { new LogEntry(time, LogKind.Tx, "{H01}").ToDisplayLine() });
                writer.Append(new[] { new LogEntry(time, LogKind.Rx, "7B 48").ToDisplayLine(), new LogEntry(time, LogKind.Rx, "30 31", isContinuation: true).ToDisplayLine() });
                await writer.CloseAsync();
                writer.Append(new[] { "ignored after close" });
                await writer.CloseAsync();

                Assert.Equal(
                    new[] { "existing", "09:42:16.334 TX   {H01}", "09:42:16.334 RX   7B 48", new string(' ', 18) + "30 31" },
                    File.ReadAllLines(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void LogViewModel_SendsBatchesToRecorder()
        {
            var recorder = new ListRecorder();
            var log = new LogViewModel(new FakeErrorLog()) { Recorder = recorder };

            var time = new DateTime(2026, 10, 16, 9, 42, 16, 334);
            log.Add(new LogEntry(time, LogKind.Info, "one"));
            log.AddRange(new[] { new LogEntry(time, LogKind.Rx, "a"), new LogEntry(time, LogKind.Rx, "b", isContinuation: true) });
            log.Clear();
            log.Recorder = null;
            log.Add(LogKind.Info, "not recorded");

            // 保存的是显示的样子 (与 Ctrl+C 复制相同)。
            Assert.Equal(new[] { 1, 2 }, recorder.Batches.Select(b => b.Count));
            Assert.Equal(new[] { "09:42:16.334 INFO one", "09:42:16.334 RX   a", new string(' ', 18) + "b" }, recorder.Batches.SelectMany(b => b));
        }

        [Fact]
        public void Preferences_RoundTripSendOptions()
        {
            var store = new MemoryPreferencesStore(new ConnectionPreferences { DisplayFormat = "Hex", RepeatSend = true, RepeatIntervalMs = 200 });
            using (var main = new MainViewModel(new FakeDeviceService(), store, new InstrumentConfigService(new FakeDeviceService()), new RecipeFileStore(), new NoCommunicationRecorders(), new FakeDialogService(), new FakeErrorLog()))
            {
                Assert.Equal(DisplayFormat.Hex, main.SendOptions.DisplayFormat);
                Assert.True(main.SendOptions.IsRepeatEnabled);
                Assert.Equal(200, main.SendOptions.RepeatInterval);

                main.SendOptions.DisplayFormat = DisplayFormat.Binary;
                main.SavePreferences();
            }

            Assert.Equal("Binary", store.Saved!.DisplayFormat);
            Assert.True(store.Saved.RepeatSend);
            Assert.Equal(200, store.Saved.RepeatIntervalMs);
        }

        [Theory]
        [InlineData("1", 1000)]     // 数字不当作枚举名
        [InlineData("Octal", 1000)]
        public void Preferences_InvalidValuesKeepDefaults(string format, int interval)
        {
            var store = new MemoryPreferencesStore(new ConnectionPreferences { DisplayFormat = format, RepeatIntervalMs = 333 });
            using (var main = new MainViewModel(new FakeDeviceService(), store, new InstrumentConfigService(new FakeDeviceService()), new RecipeFileStore(), new NoCommunicationRecorders(), new FakeDialogService(), new FakeErrorLog()))
            {
                Assert.Equal(DisplayFormat.Ascii, main.SendOptions.DisplayFormat);
                Assert.Equal(interval, main.SendOptions.RepeatInterval);
            }
        }

        /// <summary>
        /// 主视图模型把设备事件 Post 到构造时的同步上下文。真实程序是 UI 线程 (串行);测试中用 <see cref="SerialContext"/>
        /// 串行执行,并且测试读取显示记录时持有同一把锁 (<see cref="WaitUntil"/>、<see cref="Ui{T}"/>),避免并发修改集合。
        /// </summary>
        private static MainViewModel NewMain(IDeviceService device)
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new SerialContext());
            try
            {
                return new MainViewModel(device, new MemoryPreferencesStore(new ConnectionPreferences()), new InstrumentConfigService(device), new RecipeFileStore(), new NoCommunicationRecorders(), new FakeDialogService(), new FakeErrorLog())
                {
                    Connection = { SelectedConnectionType = ConnectionType.Tcp, TcpHost = "127.0.0.1" },
                };
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        /// <summary>在 <see cref="SerialContext"/> 的锁内读取 (与 Post 的回调互斥)。</summary>
        private static T Ui<T>(Func<T> read)
        {
            lock (SerialContext.Gate)
            {
                return read();
            }
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!Ui(condition))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("condition not met");
                }

                await Task.Delay(10);
            }
        }

        /// <summary>按顺序、互斥地执行 Post 的回调 (模拟 UI 线程的串行执行)。</summary>
        private sealed class SerialContext : SynchronizationContext
        {
            public static readonly object Gate = new object();

            public override void Post(SendOrPostCallback d, object? state) =>
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    lock (Gate)
                    {
                        d(state);
                    }
                });

            public override SynchronizationContext CreateCopy() => this;
        }

        private sealed class ListRecorder : ICommunicationRecorder
        {
            public List<IReadOnlyList<string>> Batches { get; } = new List<IReadOnlyList<string>>();

            public string Path => "memory";

            public void Append(IReadOnlyList<string> lines) => Batches.Add(lines);

            public Task CloseAsync() => Task.CompletedTask;
        }
    }
}
