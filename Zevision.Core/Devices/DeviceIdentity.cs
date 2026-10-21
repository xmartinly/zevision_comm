using System.Collections.Generic;
using Zevision.Communication;

namespace Zevision.Core.Devices
{
    /// <summary>握手 (<see cref="IDeviceService.HandshakeAsync"/>) 得到的设备信息。</summary>
    /// <param name="Hello"><c>H01</c> 的回答,如 <c>Zevision IMC300 1.8.11.202606101229</c>。</param>
    /// <param name="Model">设备型号 (G03),如 <c>IMC300</c>。</param>
    /// <param name="SerialNumber">系统序列号 (G06),如 <c>XXXXXXXX</c>;设备未回答或为空时为 null。</param>
    /// <param name="Frame">设备在这个接口上使用的帧格式,当前连接已改用它。</param>
    /// <param name="Warnings">不妨碍通讯、但应当提示的情况 (界面语言),如型号不是 IMC300。</param>
    public sealed record DeviceIdentity(string Hello, string Model, string? SerialNumber, FrameOptions Frame, IReadOnlyList<string> Warnings)
    {
        /// <summary>本程序适用的设备型号。</summary>
        public const string ExpectedModel = "IMC300";
    }
}
