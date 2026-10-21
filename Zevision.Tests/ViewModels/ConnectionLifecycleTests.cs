using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Communication;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Devices;
using Zevision.Core.Settings;
using Zevision.Storage;
using Zevision.ViewModels.Display;
using Zevision.ViewModels.Main;

namespace Zevision.Tests
{
    /// <summary>
    /// 主窗口的连接生命周期 (<see cref="ConnectionLifecycle"/>):握手的结果、自动重连、帧格式设置被修改后重新握手、
    /// 重发和状态错误的提示。<see cref="FakeDeviceService"/> 的操作同步完成,测试在 <see cref="InlineContext"/> 中执行。
    /// </summary>
    public class ConnectionLifecycleTests
    {
        private static readonly FrameOptions LengthAndChecksum = new FrameOptions(checksumEnabled: true, lengthEnabled: true);

        [Fact]
        public void Connect_Handshake_ConnectedWithRetryAndHeartbeat()
        {
            var device = new FakeDeviceService();
            using (Inline())
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);

                Assert.True(main.IsConnected);
                Assert.Equal(ConnectionState.Connected, main.ConnectionState);
                Assert.Single(device.Handshakes);
                var options = device.Connections.Single();
                Assert.Equal(ConnectionSettingsViewModel.HandshakeRetryCount, options.RetryCount);
                Assert.Equal(ConnectionSettingsViewModel.HandshakeHeartbeatInterval, options.HeartbeatInterval);
                Assert.Contains(Texts(main), t => t == "设备: Zevision IMC300 1.8.11");
            }
        }

        [Fact]
        public void Connect_HandshakeOff_OnlyOpensChannel()
        {
            var device = new FakeDeviceService();
            using (Inline())
            using (var main = NewMain(device))
            {
                main.Connection.IsHandshakeEnabled = false;
                main.ConnectCommand.Execute(null);

                Assert.True(main.IsConnected);
                Assert.Empty(device.Handshakes);
                var options = device.Connections.Single();
                Assert.Equal(0, options.RetryCount);
                Assert.Equal(TimeSpan.Zero, options.HeartbeatInterval);
            }
        }

        [Fact]
        public void Connect_DeviceUsesOtherFormat_SwitchesFollowDevice()
        {
            var device = new FakeDeviceService { Handshake = _ => Result<DeviceIdentity>.Ok(new DeviceIdentity("Zevision IMC300", "IMC300", null, FrameOptions.Plain, new[] { "提示" })) };
            using (Inline())
            using (var main = NewMain(device))
            {
                main.Connection.IsChecksumEnabled = true;
                main.Connection.IsLengthEnabled = true;

                main.ConnectCommand.Execute(null);

                Assert.Equal(LengthAndChecksum, device.Handshakes.Single());
                Assert.False(main.Connection.IsChecksumEnabled);
                Assert.False(main.Connection.IsLengthEnabled);
                var texts = Texts(main);
                Assert.Contains("设备的帧格式与协议选项不同,已改用设备的设置。", texts);
                Assert.Contains(texts, t => t.StartsWith("已连接 127.0.0.1:2101;校验和 (G1407) 关,长度字节 (G1408) 关", StringComparison.Ordinal));
                Assert.Contains("提示", texts);
            }
        }

        [Fact]
        public void Connect_HandshakeFails_DisconnectsAndReportsError()
        {
            var device = new FakeDeviceService { Handshake = _ => Result<DeviceIdentity>.Fail("设备没有应答 H01。") };
            using (Inline())
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);

                Assert.Equal(ConnectionState.Disconnected, main.ConnectionState);
                Assert.True(main.ConnectCommand.CanExecute(null));
                var error = main.Log.Entries.Last();
                Assert.Equal(LogKind.Error, error.Kind);
                Assert.Equal("握手失败: 设备没有应答 H01。", error.Text);
            }
        }

        [Fact]
        public void ConnectionLost_NoAutoReconnect_Disconnected()
        {
            var device = new FakeDeviceService();
            using (Inline())
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);

                device.RaiseConnectionLost(new ConnectionLostEventArgs(new IOException("设备已移除。")));

                Assert.Equal(ConnectionState.Disconnected, main.ConnectionState);
                Assert.Equal("连接中断: 设备已移除。", main.Log.Entries.Last().Text);
                Assert.Single(device.Connections);
            }
        }

        [Fact]
        public void ConnectionLost_AutoReconnect_ReconnectsAndHandshakesAgain()
        {
            var device = new FakeDeviceService();
            using (Inline())
            using (var main = NewMain(device))
            {
                var delays = new List<TimeSpan>();
                main.Link.Delay = (delay, _) => { delays.Add(delay); return Task.CompletedTask; };
                main.Connection.IsAutoReconnectEnabled = true;
                main.ConnectCommand.Execute(null);

                device.RaiseConnectionLost(new ConnectionLostEventArgs(new TimeoutException("x"), ConnectionLostReason.HeartbeatTimeout));

                Assert.True(main.IsConnected);
                Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, delays);
                Assert.Equal(2, device.Connections.Count);
                Assert.Equal(2, device.Handshakes.Count);
                var texts = Texts(main);
                Assert.Contains("连接中断: 设备连续 3 次没有应答心跳。", texts);
                Assert.Contains("已重新连接。", texts);
            }
        }

        [Fact]
        public void Reconnect_FailingAttempts_BackOffUntilStopped()
        {
            var device = new FakeDeviceService { ConnectResult = n => n == 1 ? Result.Ok() : Result.Fail("无法连接。") };
            using (Inline())
            using (var main = NewMain(device))
            {
                var delays = new List<TaskCompletionSource<bool>>();
                main.Link.Delay = (_, cancel) =>
                {
                    var delay = new TaskCompletionSource<bool>();
                    cancel.Register(() => delay.TrySetCanceled());
                    delays.Add(delay);
                    return delay.Task;
                };
                main.Connection.IsAutoReconnectEnabled = true;
                main.ConnectCommand.Execute(null);
                device.RaiseConnectionLost(new ConnectionLostEventArgs(new IOException("断开")));

                Assert.Equal(ConnectionState.Reconnecting, main.ConnectionState);
                Assert.Equal("连接中断,1 秒后第 1 次重连 …", main.StatusText);
                Assert.True(main.DisconnectCommand.CanExecute(null));
                Assert.False(main.ConnectCommand.CanExecute(null));
                Assert.False(main.IsDisconnected); // 设置保持锁定

                delays[0].SetResult(true);
                delays[1].SetResult(true);
                Assert.Equal("连接中断,4 秒后第 3 次重连 …", main.StatusText);
                Assert.Contains("第 2 次重连失败: 无法连接。", Texts(main));
                Assert.DoesNotContain(main.Log.Entries, e => e.Kind == LogKind.Error && e.Text.Contains("重连失败"));

                main.DisconnectCommand.Execute(null);

                Assert.Equal(ConnectionState.Disconnected, main.ConnectionState);
                Assert.Equal("已停止自动重连。", main.Log.Entries.Last().Text);
                Assert.Equal(3, device.Connections.Count);
            }
        }

        [Fact]
        public void CloseConnection_WhileReconnecting_StopsLoop()
        {
            var device = new FakeDeviceService();
            using (Inline())
            using (var main = NewMain(device))
            {
                main.Link.Delay = (_, cancel) => Task.Delay(Timeout.Infinite, cancel);
                main.Connection.IsAutoReconnectEnabled = true;
                main.ConnectCommand.Execute(null);
                device.RaiseConnectionLost(new ConnectionLostEventArgs(new IOException("断开")));
                Assert.True(main.HasConnection);

                var closing = main.CloseConnectionAsync();

                Assert.True(closing.IsCompleted); // 取消等待后重连循环立即结束
                Assert.False(main.HasConnection);
            }
        }

        [Fact]
        public void ManualFrameSettingChange_HandshakesWithNewFormat()
        {
            var device = new FakeDeviceService().With("G1407", "0");
            using (Inline())
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);

                main.SendQuickCommand.Execute("UG1407:1");

                Assert.Equal(new[] { FrameOptions.Plain, new FrameOptions(checksumEnabled: true, lengthEnabled: false) }, device.Handshakes);
                Assert.True(main.Connection.IsChecksumEnabled);
                Assert.Contains("已改用设备的新帧格式: 校验和 (G1407) 开,长度字节 (G1408) 关。", Texts(main));
            }
        }

        [Fact]
        public void ManualFrameSettingChange_HandshakeOff_OnlyHints()
        {
            var device = new FakeDeviceService().With("G1407", "0");
            using (Inline())
            using (var main = NewMain(device))
            {
                main.Connection.IsHandshakeEnabled = false;
                main.ConnectCommand.Execute(null);

                main.SendQuickCommand.Execute("UG1407:1");

                Assert.Empty(device.Handshakes);
                Assert.False(main.Connection.IsChecksumEnabled);
                Assert.Contains(Texts(main), t => t.StartsWith("设备的帧格式已改为: 校验和 (G1407) 开", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void ManualFrameSettingChange_HandshakeFails_Disconnects()
        {
            var device = new FakeDeviceService().With("G1407", "0");
            using (Inline())
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);
                device.Handshake = _ => Result<DeviceIdentity>.Fail("设备没有应答 H01。");

                main.SendQuickCommand.Execute("UG1407:1");

                Assert.Equal(ConnectionState.Disconnected, main.ConnectionState);
                Assert.Contains("按设备的新帧格式重新握手失败,已断开: 设备没有应答 H01。", Texts(main));
            }
        }

        [Fact]
        public void Retrying_IsShownAsInfo()
        {
            var device = new FakeDeviceService();
            using (Inline())
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);

                device.RaiseRetrying(new RetryEventArgs("QG01", 1, 2, null, ZevisionErrorCode.LengthError));

                var entry = main.Log.Entries.Last();
                Assert.Equal(LogKind.Info, entry.Kind);
                Assert.Equal("第 1/2 次重发 QG01: 设备回答 L (长度错误)", entry.Text);
            }
        }

        [Fact]
        public void StateError_ShowsCurrentState()
        {
            var device = new FakeDeviceService().With("G0E03", "20").With("C09", "Process");
            device.StateErrorWrites.Add("G0E03");
            using (Inline())
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);

                main.SendQuickCommand.Execute("UG0E03:30");

                Assert.Equal("设备当前状态 (C09): Process", main.Log.Entries.Last().Text);
            }
        }

        [Fact]
        public void ErrorCount_AddsFailedCommands_ClearResets()
        {
            var device = new FakeDeviceService();
            using (Inline())
            using (var main = NewMain(device))
            {
                main.ConnectCommand.Execute(null);
                Assert.False(main.HasErrors);

                device.RaiseCommandsFailed(new CommandsFailedEventArgs("QG01;QX", 1));
                device.RaiseCommandsFailed(new CommandsFailedEventArgs("QA;QB", 2));

                Assert.Equal(3, main.ErrorCount);
                Assert.True(main.HasErrors);

                main.ClearLogCommand.Execute(null);

                Assert.Equal(0, main.ErrorCount);
                Assert.False(main.HasErrors);
            }
        }

        [Fact]
        public void Dispose_WritesCommunicationDataSynchronously()
        {
            var device = new FakeDeviceService();
            var recorder = new TrackingRecorder();
            using (Inline())
            {
                var main = new MainViewModel(device, new MemoryPreferencesStore(new ConnectionPreferences()), new InstrumentConfigService(device), new RecipeFileStore(), new SingleRecorderFactory(recorder), new FakeDialogService(), new FakeErrorLog());
                main.Connection.SelectedConnectionType = ConnectionType.Tcp;
                main.Connection.TcpHost = "127.0.0.1";
                main.Connection.IsSaveCommunicationDataEnabled = true;
                main.ConnectCommand.Execute(null);
                Assert.True(main.IsConnected);

                // 注销、关机时窗口不经过 Closing,App.OnExit 直接释放视图模型:缓存的数据应在返回前写出。
                main.Dispose();

                Assert.True(recorder.Disposed);
                Assert.False(recorder.ClosedAsync);
            }
        }

        [Theory]
        [InlineData(true, "XXXXXXXX")]
        [InlineData(false, null)]
        public void Connect_SaveCommunicationData_NamesFileAfterHandshakeSerialNumber(bool handshake, string? expected)
        {
            var device = new FakeDeviceService();
            var factory = new SingleRecorderFactory(new TrackingRecorder());
            using (Inline())
            {
                var main = new MainViewModel(device, new MemoryPreferencesStore(new ConnectionPreferences()), new InstrumentConfigService(device), new RecipeFileStore(), factory, new FakeDialogService(), new FakeErrorLog());
                main.Connection.SelectedConnectionType = ConnectionType.Tcp;
                main.Connection.TcpHost = "127.0.0.1";
                main.Connection.IsHandshakeEnabled = handshake;
                main.Connection.IsSaveCommunicationDataEnabled = true;

                main.ConnectCommand.Execute(null);

                Assert.True(main.IsConnected);
                Assert.Equal(new[] { expected }, factory.SerialNumbers);
                main.Dispose();
            }
        }

        [Fact]
        public void Connect_HandshakeFails_CreatesNoCommunicationDataFile()
        {
            var device = new FakeDeviceService { Handshake = _ => Result<DeviceIdentity>.Fail("no reply") };
            var factory = new SingleRecorderFactory(new TrackingRecorder());
            using (Inline())
            {
                var main = new MainViewModel(device, new MemoryPreferencesStore(new ConnectionPreferences()), new InstrumentConfigService(device), new RecipeFileStore(), factory, new FakeDialogService(), new FakeErrorLog());
                main.Connection.SelectedConnectionType = ConnectionType.Tcp;
                main.Connection.TcpHost = "127.0.0.1";
                main.Connection.IsSaveCommunicationDataEnabled = true;

                main.ConnectCommand.Execute(null);

                Assert.False(main.IsConnected);
                Assert.Empty(factory.SerialNumbers);
                main.Dispose();
            }
        }

        [Fact]
        public void Preferences_HandshakeAndAutoReconnect_RoundTrip()
        {
            var store = new MemoryPreferencesStore(new ConnectionPreferences { HandshakeEnabled = false, AutoReconnect = true });
            using (Inline())
            using (var main = NewMain(new FakeDeviceService(), store))
            {
                Assert.False(main.Connection.IsHandshakeEnabled);
                Assert.True(main.Connection.IsAutoReconnectEnabled);

                main.Connection.IsHandshakeEnabled = true;
                main.SavePreferences();
            }

            Assert.True(store.Saved!.HandshakeEnabled);
            Assert.True(store.Saved.AutoReconnect);
            Assert.True(new ConnectionPreferences().HandshakeEnabled);
            Assert.False(new ConnectionPreferences().AutoReconnect);
        }

        [Fact]
        public void Preferences_LastSentCommand_SavedAndRestoredIntoInputBox()
        {
            var store = new MemoryPreferencesStore(new ConnectionPreferences { LastCommand = "QG03" });
            var device = new FakeDeviceService();
            using (Inline())
            using (var main = NewMain(device, store))
            {
                Assert.Equal("QG03", main.CommandText);
                main.Connection.SelectedConnectionType = ConnectionType.Tcp;
                main.Connection.TcpHost = "127.0.0.1";
                main.ConnectCommand.Execute(null);

                main.CommandText = "qg01\nqm0101";
                main.SendCommand.Execute(null);
                main.CommandText = "not sent"; // 只保存发送过的内容
                main.SavePreferences();
            }

            Assert.Equal("QG01\nQM0101", store.Saved!.LastCommand);
            Assert.Contains("QG01;QM0101", device.Frames); // 多行合并为一帧发送

            using (Inline())
            using (var restarted = NewMain(new FakeDeviceService(), new MemoryPreferencesStore(store.Saved)))
            {
                Assert.Equal("QG01\nQM0101", restarted.CommandText);
            }
        }

        [Fact]
        public void Preferences_NoCommandSent_KeepsPreviousLastCommand()
        {
            var store = new MemoryPreferencesStore(new ConnectionPreferences { LastCommand = "H01" });
            using (Inline())
            using (var main = NewMain(new FakeDeviceService(), store))
            {
                main.CommandText = "edited but not sent";
                main.SavePreferences();
            }

            Assert.Equal("H01", store.Saved!.LastCommand);
        }

        private static List<string> Texts(MainViewModel main) => main.Log.Entries.Select(e => e.Text).ToList();

        private static MainViewModel NewMain(FakeDeviceService device, MemoryPreferencesStore? store = null)
        {
            var main = new MainViewModel(device, store ?? new MemoryPreferencesStore(new ConnectionPreferences()), new InstrumentConfigService(device), new RecipeFileStore(), new NoCommunicationRecorders(), new FakeDialogService(), new FakeErrorLog());
            if (store == null)
            {
                main.Connection.SelectedConnectionType = ConnectionType.Tcp;
                main.Connection.TcpHost = "127.0.0.1";
            }

            return main;
        }

        /// <summary>
        /// 整个测试在 <see cref="InlineContext"/> 中执行:视图模型的 Post 和 await 之后的代码都直接在完成它的线程上执行,
        /// 假设备又是同步完成的,所以每个操作返回时已经全部做完。
        /// </summary>
        private static IDisposable Inline() => new ContextScope(new InlineContext());

        private sealed class TrackingRecorder : ICommunicationRecorder
        {
            public string Path => "memory";

            public bool Disposed { get; private set; }

            public bool ClosedAsync { get; private set; }

            public void Append(IReadOnlyList<string> lines)
            {
            }

            public Task CloseAsync()
            {
                ClosedAsync = true;
                return Task.CompletedTask;
            }

            public void Dispose() => Disposed = true;
        }

        private sealed class SingleRecorderFactory : ICommunicationRecorderFactory
        {
            private readonly ICommunicationRecorder _recorder;

            public SingleRecorderFactory(ICommunicationRecorder recorder) => _recorder = recorder;

            /// <summary>每次 <see cref="Create"/> 收到的设备序列号。</summary>
            public List<string?> SerialNumbers { get; } = new List<string?>();

            public Result<ICommunicationRecorder> Create(string? deviceSerialNumber)
            {
                SerialNumbers.Add(deviceSerialNumber);
                return Result<ICommunicationRecorder>.Ok(_recorder);
            }
        }

        private sealed class InlineContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state) => d(state);
        }

        private sealed class ContextScope : IDisposable
        {
            private readonly SynchronizationContext? _previous = SynchronizationContext.Current;

            public ContextScope(SynchronizationContext context) => SynchronizationContext.SetSynchronizationContext(context);

            public void Dispose() => SynchronizationContext.SetSynchronizationContext(_previous);
        }
    }
}
