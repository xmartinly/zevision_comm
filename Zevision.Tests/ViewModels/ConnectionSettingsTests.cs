using System.Collections.Generic;
using Xunit;
using Zevision.Core.Settings;
using Zevision.ViewModels.Main;

namespace Zevision.Tests
{
    public class ConnectionSettingsTests
    {
        [Fact]
        public void Switches_AreKeptPerConnectionType()
        {
            // 设备上 RS232 (G1304/G1305) 与 TCP (G1407/G1408) 是独立的设置:切换 tab 时开关显示各自的值。
            var settings = new ConnectionSettingsViewModel();
            settings.IsChecksumEnabled = true;
            settings.IsLengthEnabled = true;
            settings.IsSaveCommunicationDataEnabled = true;

            settings.SelectedConnectionType = ConnectionType.Tcp;
            Assert.False(settings.IsChecksumEnabled || settings.IsLengthEnabled || settings.IsSaveCommunicationDataEnabled);
            settings.IsLengthEnabled = true;

            settings.SelectedConnectionType = ConnectionType.Serial;
            Assert.True(settings.IsChecksumEnabled && settings.IsLengthEnabled && settings.IsSaveCommunicationDataEnabled);
            settings.SelectedConnectionType = ConnectionType.Tcp;
            Assert.False(settings.IsChecksumEnabled);
            Assert.True(settings.IsLengthEnabled);
        }

        [Fact]
        public void SwitchingTab_RaisesSwitchChanges()
        {
            var settings = new ConnectionSettingsViewModel();
            var changed = new List<string?>();
            settings.PropertyChanged += (s, e) => changed.Add(e.PropertyName);

            settings.ConnectionTabIndex = 1;

            Assert.Contains(nameof(ConnectionSettingsViewModel.IsChecksumEnabled), changed);
            Assert.Contains(nameof(ConnectionSettingsViewModel.IsLengthEnabled), changed);
            Assert.Contains(nameof(ConnectionSettingsViewModel.IsSaveCommunicationDataEnabled), changed);
        }

        [Fact]
        public void CreateOptions_UsesTheSelectedConnectionTypeSwitches()
        {
            var settings = new ConnectionSettingsViewModel();
            settings.Apply(new ConnectionPreferences { SerialChecksumEnabled = true, SerialLengthEnabled = true, TcpChecksumEnabled = false, TcpLengthEnabled = true, ConnectionType = "Tcp", TcpHost = "10.0.0.5" });

            var tcp = settings.CreateOptions().Value.Frame;

            Assert.False(tcp.ChecksumEnabled);
            Assert.True(tcp.LengthEnabled);
        }

        [Fact]
        public void ApplyThenWriteTo_KeepsBothSets()
        {
            var saved = new ConnectionPreferences
            {
                SerialChecksumEnabled = true,
                SerialLengthEnabled = false,
                SerialSaveCommunicationData = true,
                TcpChecksumEnabled = false,
                TcpLengthEnabled = true,
                TcpSaveCommunicationData = false,
            };
            var settings = new ConnectionSettingsViewModel();
            settings.Apply(saved);
            var written = new ConnectionPreferences();

            settings.WriteTo(written);

            Assert.Equal((true, false, true), (written.SerialChecksumEnabled, written.SerialLengthEnabled, written.SerialSaveCommunicationData));
            Assert.Equal((false, true, false), (written.TcpChecksumEnabled, written.TcpLengthEnabled, written.TcpSaveCommunicationData));
            Assert.True(settings.IsChecksumEnabled && !settings.IsLengthEnabled); // 显示的是 RS232 的一组
        }
    }
}
