using System;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Communication;

namespace Zevision.Core.Devices
{
    /// <summary>
    /// 与 IMC300 设备通讯的服务:连接管理、发送命令并等待响应。失败说明按界面语言给出。
    /// </summary>
    /// <remarks>
    /// 所有事件都在后台线程上触发,订阅方需要自行切换到 UI 线程。
    /// </remarks>
    public interface IDeviceService : IDisposable
    {
        /// <summary>帧发送前触发。</summary>
        event EventHandler<FrameSentEventArgs>? FrameSent;

        /// <summary>收到一帧 (含错误帧和帧外数据) 时触发。</summary>
        event EventHandler<FrameReceivedEventArgs>? FrameReceived;

        /// <summary>连接因传输错误 (串口拔出、TCP 断开等) 或心跳无应答意外中断时触发。主动断开不触发。</summary>
        event EventHandler<ConnectionLostEventArgs>? ConnectionLost;

        /// <summary>一次发送传输出错、即将重发时触发 (见 <see cref="ConnectionOptions.RetryCount"/>;不显示的发送不触发)。</summary>
        event EventHandler<RetryEventArgs>? Retrying;

        /// <summary>
        /// 一次发送结束,其中有指令没有得到成功响应时触发 (发送方的线程)。设备的否定响应 (含不回显的 <c>-C</c>) 计该指令;
        /// 发出后没有有效响应 (超时、写入失败、响应帧无效或无法解析) 计帧中全部指令;重发后成功的、未发出的、
        /// 等待时连接断开的不计。不显示的发送和握手不触发。
        /// </summary>
        event EventHandler<CommandsFailedEventArgs>? CommandsFailed;

        /// <summary>是否已连接 (通道已打开;是否握手由调用方决定)。</summary>
        bool IsConnected { get; }

        /// <summary>
        /// 打开连接 (串口或 TCP) 并开始接收。连接参数 (连接目标、帧格式、响应超时、重试、心跳) 在连接期间不变,
        /// 帧格式可由 <see cref="HandshakeAsync"/> 改为设备的实际设置。
        /// </summary>
        Task<Result> ConnectAsync(ConnectionOptions options, CancellationToken cancellationToken);

        /// <summary>
        /// 握手:按设备的回答确定它在这个接口上使用的帧格式并改用它 (先试 <paramref name="preferred"/>),
        /// 再读取型号 (G03) 和帧格式设置。期间的收发不显示。连接后,以及设备的帧格式设置被修改后调用。
        /// </summary>
        /// <param name="preferred">先尝试的帧格式 (界面上的设置,或刚修改成的格式)。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>设备信息;设备无应答、帧格式无法识别或读不到型号时失败,原因可直接显示。</returns>
        Task<Result<DeviceIdentity>> HandshakeAsync(FrameOptions preferred, CancellationToken cancellationToken);

        /// <summary>
        /// 关闭连接。未连接时直接返回。
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// 发送命令并等待响应。同一时间只有一个请求在等待响应,并发调用会排队。
        /// 响应超时 (空闲超时) 至少为 <paramref name="minimumResponseTimeout"/>,连接设置的超时更长时用连接设置;
        /// 用于已知响应前或分包之间会停顿的指令,如 <c>?</c>。其他指令传 <see cref="TimeSpan.Zero"/>。
        /// </summary>
        /// <param name="command">命令数据,不含起止符,如 <c>H01</c>。</param>
        /// <param name="minimumResponseTimeout">本次请求的最低空闲超时。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        Task<Result<ZevisionReply>> SendAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken);

        /// <summary>
        /// 与 <see cref="SendAsync"/> 相同,但不在数据显示区显示 (因此也不保存到通讯数据文件、不计数):
        /// 不触发 <see cref="FrameSent"/>;这次的响应、超时时报告的不完整数据,以及之后收到的回显相同的帧
        /// (如取消后设备仍在发送的剩余数据) 都不触发 <see cref="FrameReceived"/>,直到再次用 <see cref="SendAsync"/> 发送同一指令。
        /// 用于程序内部读取设备配置的帮助指令 (<c>?</c>、<c>?M</c>):它们只在指令输入框中发送时显示。
        /// </summary>
        /// <param name="command">命令数据,不含起止符,如 <c>?</c>。</param>
        /// <param name="minimumResponseTimeout">本次请求的最低空闲超时。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        Task<Result<ZevisionReply>> SendHiddenAsync(string command, TimeSpan minimumResponseTimeout, CancellationToken cancellationToken);
    }

    /// <summary><see cref="IDeviceService"/> 的便捷方法。</summary>
    public static class DeviceServiceExtensions
    {
        /// <summary>发送命令并等待响应,使用连接设置的响应超时。</summary>
        public static Task<Result<ZevisionReply>> SendAsync(this IDeviceService device, string command, CancellationToken cancellationToken) =>
            (device ?? throw new ArgumentNullException(nameof(device))).SendAsync(command, TimeSpan.Zero, cancellationToken);
    }
}
