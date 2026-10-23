using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zevision.Communication;
using Zevision.Core.Configuration;
using Zevision.Core.Devices;
using Zevision.Core.Settings;
using Zevision.Storage;
using Zevision.ViewModels.Main;

namespace Zevision.Tests
{
    /// <summary>
    /// TCP 连接:用本机回环地址上的 <see cref="TcpListener"/> 模拟设备的以太网接口。
    /// </summary>
    public sealed class TcpTransportTests : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);

        public TcpTransportTests()
        {
            _listener.Start();
        }

        private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public void Dispose() => _listener.Stop();

        // ---------- 连接参数 ----------

        [Theory]
        [InlineData("", 2101)]
        [InlineData(" ", 2101)]
        [InlineData("192.168.1.88", 0)]
        [InlineData("192.168.1.88", 65536)]
        public void TcpSettings_Invalid_Throws(string host, int port)
        {
            Assert.ThrowsAny<ArgumentException>(() => new TcpSettings(host, port));
        }

        [Fact]
        public void TcpSettings_ToStringIsHostAndPort()
        {
            Assert.Equal("192.168.1.88:2101", new TcpSettings(" 192.168.1.88 ", TcpSettings.DefaultPort).ToString());
        }

        [Fact]
        public void DeviceTransports_CreatesByEndpointType()
        {
            Assert.IsType<SerialPortTransport>(DeviceTransports.Create(new SerialSettings("COM1", 115200)));
            Assert.IsType<TcpTransport>(DeviceTransports.Create(new TcpSettings("127.0.0.1", 2101)));
        }

        // ---------- 传输 ----------

        [Fact]
        public async Task Open_WriteAndRead_RoundTrip()
        {
            using (var transport = new TcpTransport())
            {
                var accept = _listener.AcceptTcpClientAsync();
                await transport.OpenAsync(new TcpSettings("127.0.0.1", Port), CancellationToken.None);
                using (var server = await WithTimeout(accept))
                {
                    Assert.True(transport.IsOpen);
                    var stream = server.GetStream();

                    await transport.WriteAsync(Encoding.ASCII.GetBytes("{H01}"), CancellationToken.None);
                    var received = new byte[16];
                    var count = await WithTimeout(stream.ReadAsync(received, 0, received.Length));
                    Assert.Equal("{H01}", Encoding.ASCII.GetString(received, 0, count));

                    await stream.WriteAsync(Encoding.ASCII.GetBytes("{H01+}"), 0, 6);
                    var buffer = new byte[16];
                    count = await WithTimeout(transport.ReadAsync(buffer, CancellationToken.None));
                    Assert.Equal("{H01+}", Encoding.ASCII.GetString(buffer, 0, count));
                }
            }
        }

        [Fact]
        public async Task Open_Refused_ThrowsIOException()
        {
            var port = Port;
            _listener.Stop(); // 端口上没有监听:连接被拒绝

            using (var transport = new TcpTransport())
            {
                await Assert.ThrowsAsync<IOException>(() => transport.OpenAsync(new TcpSettings("127.0.0.1", port), CancellationToken.None));
                Assert.False(transport.IsOpen);
            }
        }

        [Fact]
        public async Task Open_WrongEndpointType_Throws()
        {
            using (var transport = new TcpTransport())
            {
                await Assert.ThrowsAsync<ArgumentException>(() => transport.OpenAsync(new SerialSettings("COM1", 115200), CancellationToken.None));
            }
        }

        [Fact]
        public async Task RemoteClose_ReadReturnsZero()
        {
            using (var transport = new TcpTransport())
            {
                var accept = _listener.AcceptTcpClientAsync();
                await transport.OpenAsync(new TcpSettings("127.0.0.1", Port), CancellationToken.None);
                var server = await WithTimeout(accept);
                var read = transport.ReadAsync(new byte[16], CancellationToken.None);

                server.Close();

                Assert.Equal(0, await WithTimeout(read));
            }
        }

        [Fact]
        public async Task Close_EndsPendingRead()
        {
            var transport = new TcpTransport();
            var accept = _listener.AcceptTcpClientAsync();
            await transport.OpenAsync(new TcpSettings("127.0.0.1", Port), CancellationToken.None);
            using (await WithTimeout(accept))
            {
                var read = transport.ReadAsync(new byte[16], CancellationToken.None);

                transport.Close();

                // .NET Framework 下关闭后挂起的读取以异常结束 (或返回 0),不会永远挂起。
                var completed = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.Same(read, completed);
                Assert.False(transport.IsOpen);
            }
        }

        // ---------- 设备服务经 TCP ----------

        [Fact]
        public async Task DeviceService_OverTcp_SendsAndReceives()
        {
            using (var service = new ZevisionDeviceService(DeviceTransports.Create))
            {
                var accept = _listener.AcceptTcpClientAsync();
                var connected = await service.ConnectAsync(
                    new ConnectionOptions(new TcpSettings("127.0.0.1", Port), FrameOptions.Plain, TimeSpan.FromSeconds(2)),
                    CancellationToken.None);
                Assert.True(connected.IsSuccess, connected.Error);

                using (var server = await WithTimeout(accept))
                {
                    var stream = server.GetStream();
                    var send = service.SendAsync("H01", CancellationToken.None);
                    var request = new byte[16];
                    var count = await WithTimeout(stream.ReadAsync(request, 0, request.Length));
                    Assert.Equal("{H01}", Encoding.ASCII.GetString(request, 0, count));

                    var reply = Encoding.ASCII.GetBytes("{H01+:Zevision IMC300}");
                    await stream.WriteAsync(reply, 0, reply.Length);
                    var result = await WithTimeout(send);

                    Assert.True(result.IsSuccess, result.Error);
                    Assert.Equal("Zevision IMC300", Assert.Single(result.Value.Responses).Data);
                }
            }
        }

        [Fact]
        public async Task DeviceService_RemoteClose_RaisesConnectionLost()
        {
            using (var service = new ZevisionDeviceService(DeviceTransports.Create))
            {
                var lost = new TaskCompletionSource<bool>();
                service.ConnectionLost += (s, e) => lost.TrySetResult(true);
                var accept = _listener.AcceptTcpClientAsync();
                await service.ConnectAsync(new ConnectionOptions(new TcpSettings("127.0.0.1", Port), FrameOptions.Plain), CancellationToken.None);

                (await WithTimeout(accept)).Close();

                Assert.True(await WithTimeout(lost.Task));
                Assert.False(service.IsConnected);
            }
        }

        [Fact]
        public async Task DeviceService_Refused_ReportsEndpoint()
        {
            var port = Port;
            _listener.Stop();
            using (var service = new ZevisionDeviceService(DeviceTransports.Create))
            {
                var result = await service.ConnectAsync(new ConnectionOptions(new TcpSettings("127.0.0.1", port), FrameOptions.Plain), CancellationToken.None);

                Assert.False(result.IsSuccess);
                Assert.Contains("无法连接 127.0.0.1:" + port, result.Error);
            }
        }

        // ---------- 主窗口:RS232 / TCP 切换 ----------

        [Fact]
        public void Main_TcpMode_LabelsQuickCommandsAndConnectAvailability()
        {
            using (var main = NewMain(new MemoryPreferencesStore(new ConnectionPreferences())))
            {
                Assert.Equal("IMC300通讯测试 - Zevision " + AppInfo.Version, main.Title);
                Assert.Equal("校验和 (G1304)", main.Connection.ChecksumLabel);
                Assert.Contains("QG1304", main.Connection.QuickCommands);

                main.Connection.SelectedConnectionType = ConnectionType.Tcp;

                Assert.True(main.Connection.IsTcpConnection);
                Assert.Equal("校验和 (G1407)", main.Connection.ChecksumLabel);
                Assert.Equal("长度字节 (G1408)", main.Connection.LengthLabel);
                Assert.Contains("QG1407", main.Connection.QuickCommands);
                Assert.Contains("QG1408", main.Connection.QuickCommands);

                main.Connection.TcpHost = string.Empty;
                Assert.False(main.ConnectCommand.CanExecute(null));
                main.Connection.TcpHost = "192.168.1.88";
                Assert.True(main.ConnectCommand.CanExecute(null));
                Assert.Equal(TcpSettings.DefaultPort, main.Connection.TcpPort); // 端口固定 2101
            }
        }

        [Fact]
        public async Task Main_TcpMode_ConnectsAndShowsEndpoint()
        {
            using (var device = new ZevisionDeviceService(DeviceTransports.Create))
            using (var main = new MainViewModel(device, new MemoryPreferencesStore(new ConnectionPreferences()), new InstrumentConfigService(device), new RecipeFileStore(), new MemoryDesignStore(), new NoCommunicationRecorders(), new FakeDialogService(), new FakeErrorLog()))
            {
                main.Connection.SelectedConnectionType = ConnectionType.Tcp;
                main.Connection.TcpHost = "127.0.0.1";
                main.Connection.TcpPort = Port; // 只有测试改端口:连接回环地址上的随机端口
                main.Connection.IsHandshakeEnabled = false; // 回环上的监听方不应答 (握手见 HandshakeTests)
                var accept = _listener.AcceptTcpClientAsync();

                main.ConnectCommand.Execute(null);
                using (await WithTimeout(accept))
                {
                    await WaitUntil(() => main.IsConnected);

                    Assert.Equal("已连接 127.0.0.1:" + main.Connection.TcpPort, main.StatusText);
                }

                await device.DisconnectAsync();
            }
        }

        [Fact]
        public void Main_ConnectionTabIndex_SelectsConnectionType()
        {
            using (var main = NewMain(new MemoryPreferencesStore(new ConnectionPreferences())))
            {
                Assert.Equal(0, main.Connection.ConnectionTabIndex);

                main.Connection.ConnectionTabIndex = 1;
                Assert.Equal(ConnectionType.Tcp, main.Connection.SelectedConnectionType);

                main.Connection.ConnectionTabIndex = 5; // 无效序号忽略
                Assert.Equal(ConnectionType.Tcp, main.Connection.SelectedConnectionType);

                main.Connection.SelectedConnectionType = ConnectionType.Serial;
                Assert.Equal(0, main.Connection.ConnectionTabIndex);
            }
        }

        [Fact]
        public async Task Close_WithPendingRead_EndsWithoutObjectDisposedException()
        {
            // 退出时的首次机会 ObjectDisposedException:先 Shutdown,挂起的读取正常返回 0。
            var transport = new TcpTransport();
            var accept = _listener.AcceptTcpClientAsync();
            await transport.OpenAsync(new TcpSettings("127.0.0.1", Port), CancellationToken.None);
            using (await WithTimeout(accept))
            {
                var read = transport.ReadAsync(new byte[16], CancellationToken.None);

                transport.Close();

                Assert.Equal(0, await WithTimeout(read));
            }
        }

        [Fact]
        public void Main_ConnectionSettings_RestoredAndSaved()
        {
            var store = new MemoryPreferencesStore(new ConnectionPreferences { ConnectionType = "Tcp", TcpHost = "10.0.0.5" });
            using (var main = NewMain(store))
            {
                Assert.Equal(ConnectionType.Tcp, main.Connection.SelectedConnectionType);
                Assert.Equal("10.0.0.5", main.Connection.TcpHost);
                Assert.Equal(TcpSettings.DefaultPort, main.Connection.TcpPort);

                main.Connection.SelectedConnectionType = ConnectionType.Serial;
                main.SavePreferences();
            }

            Assert.Equal("Serial", store.Saved!.ConnectionType);
            Assert.Equal("10.0.0.5", store.Saved.TcpHost);
        }

        [Theory]
        [InlineData("Garbage")]
        [InlineData("1")]
        public void Main_UnknownConnectionType_DefaultsToSerial(string stored)
        {
            using (var main = NewMain(new MemoryPreferencesStore(new ConnectionPreferences { ConnectionType = stored })))
            {
                Assert.Equal(ConnectionType.Serial, main.Connection.SelectedConnectionType);
            }
        }

        private static MainViewModel NewMain(IPreferencesStore store)
        {
            var device = new FakeDeviceService();
            return new MainViewModel(device, store, new InstrumentConfigService(device), new RecipeFileStore(), new MemoryDesignStore(), new NoCommunicationRecorders(), new FakeDialogService(), new FakeErrorLog());
        }

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(completed == task, "等待超时。");
            return await task;
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            for (var i = 0; i < 100 && !condition(); i++)
            {
                await Task.Delay(50);
            }

            Assert.True(condition(), "等待条件超时。");
        }
    }
}
