using System;

namespace Zevision.Communication
{
    /// <summary>
    /// 请求的优先级。协议同一时间只允许一个请求在途:正在等待响应的请求不会被打断,
    /// 排队等待发送的请求按优先级 (同级按先后) 发送。
    /// </summary>
    public enum SendPriority
    {
        /// <summary>低:后台的周期性请求,如状态轮询。</summary>
        Low,

        /// <summary>普通 (默认)。</summary>
        Normal,

        /// <summary>高:需要尽快发送的指令,如停止、复位。</summary>
        High,
    }

    /// <summary>
    /// 一次请求的选项 (<see cref="ZevisionClient.SendAsync(string, RequestOptions, System.Threading.CancellationToken)"/>)。
    /// </summary>
    public sealed class RequestOptions
    {
        /// <summary>默认选项:连接设置的超时,普通优先级,允许重试。</summary>
        public static readonly RequestOptions Default = new RequestOptions();

        /// <summary>
        /// 本次请求的最低空闲超时;<see cref="ConnectionOptions.ResponseTimeout"/> 更长时用它。默认为零。
        /// 用于已知响应前或分包之间会停顿的指令,如 <c>?</c> (完整帮助约 116 KB,首字节约 0.4 s,包间约 0.35 s,建议至少 2 s)。
        /// </summary>
        public TimeSpan MinimumResponseTimeout { get; init; }

        /// <summary>排队等待发送时的优先级。默认为 <see cref="SendPriority.Normal"/>。</summary>
        public SendPriority Priority { get; init; } = SendPriority.Normal;

        /// <summary>
        /// 是否允许按 <see cref="ConnectionOptions.RetryCount"/> 重试。默认为 true;
        /// 探测帧格式等需要看到第一次结果的请求设为 false。
        /// </summary>
        public bool AllowRetry { get; init; } = true;
    }
}
