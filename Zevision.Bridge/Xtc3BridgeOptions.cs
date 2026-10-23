using System;
using System.Collections.Generic;
using Zevision.Communication;
using Zevision.Xtc3;

namespace Zevision.Bridge
{
    /// <summary>
    /// 中间层的设置。
    /// </summary>
    public sealed class Xtc3BridgeOptions
    {
        /// <summary>创建。</summary>
        /// <param name="device">与 IMC300 的连接 (TCP 端口 2101 或 RS232;帧格式由握手确定)。</param>
        public Xtc3BridgeOptions(ConnectionOptions device)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
        }

        /// <summary>与 IMC300 的连接。</summary>
        public ConnectionOptions Device { get; }

        /// <summary>
        /// 主机侧串口 (虚拟串口对中中间层的一端,如 COM2;主机软件打开另一端)。波特率为初值,
        /// 本地参数中保存的 RS232 Baud Rate (UG14) 优先。为 null 时不打开串口,命令包由 <see cref="Xtc3Bridge.ProcessPacketAsync"/> 传入。
        /// </summary>
        public SerialSettings? HostPort { get; init; }

        /// <summary>影子对象的编号基数 (ZEV-FAR-01 的 Q-03),默认 0xB0,即 Film 1~9 对应 B1~B9。</summary>
        public int ShadowBase { get; init; } = ShadowFilm.DefaultBase;

        /// <summary>连接后影子对象不存在时是否创建 (ZEV-FAR-01 的 5.2 b:经确认后创建)。默认不创建,相应 Film 的命令回答 E。</summary>
        public bool CreateMissingShadowObjects { get; init; }

        /// <summary>H1、H3 回答的版本号,H1 为 <c>XTC/3S Version 1.00</c>。</summary>
        public string Version { get; init; } = "1.00";

        /// <summary>H2 回答的 4 个 Integer (Structure、Compatibility、Range、Unit Type);取值待确认 (ZEV-FAR-01 的 8.2 b)。</summary>
        public IReadOnlyList<int> HelloNumbers { get; init; } = Array.AsReadOnly(new[] { 1, 83, 0, 0 });

        /// <summary>采样连续失败多少次判定与 IMC300 的连接中断 (ZEV-FAR-01 的 5.7 b)。</summary>
        public int SampleMissLimit { get; init; } = 3;

        /// <summary>采样数据的最大年龄;最近的采样更旧时,由采样得到的状态回答 E。</summary>
        public TimeSpan MaxSampleAge { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>检查设置 (<see cref="Xtc3Bridge"/> 创建时也检查)。</summary>
        /// <exception cref="ArgumentException">某项无效。</exception>
        public void Validate()
        {
            if (!ShadowFilm.IsValidBase(ShadowBase))
            {
                throw new ArgumentException("ShadowBase must be 0 to " + (ShadowFilm.MaxObjectNumber - Xtc3Protocol.FilmCount) + ".", nameof(ShadowBase));
            }

            if (HelloNumbers == null || HelloNumbers.Count != 4)
            {
                throw new ArgumentException("HelloNumbers must hold 4 integers.", nameof(HelloNumbers));
            }

            if (string.IsNullOrWhiteSpace(Version) || !float.TryParse(Version, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                throw new ArgumentException("Version must be a number such as 1.00.", nameof(Version));
            }

            if (SampleMissLimit < 1)
            {
                throw new ArgumentException("SampleMissLimit must be at least 1.", nameof(SampleMissLimit));
            }
        }
    }

    /// <summary>记录的级别。</summary>
    public enum BridgeLogLevel
    {
        /// <summary>运行情况 (连接、影子对象)。</summary>
        Info,

        /// <summary>可恢复的问题 (采样失败、未知的状态文本)。</summary>
        Warning,

        /// <summary>错误 (连接失败、影子对象被占用)。同时写入错误日志。</summary>
        Error,
    }

    /// <summary><see cref="Xtc3Bridge.Log"/> 的参数。</summary>
    public sealed class BridgeLogEventArgs : EventArgs
    {
        /// <summary>创建。</summary>
        public BridgeLogEventArgs(BridgeLogLevel level, string message, Exception? exception = null)
        {
            Level = level;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        /// <summary>级别。</summary>
        public BridgeLogLevel Level { get; }

        /// <summary>内容 (英文)。</summary>
        public string Message { get; }

        /// <summary>相关的异常。</summary>
        public Exception? Exception { get; }

        /// <inheritdoc/>
        public override string ToString() => Level + ": " + Message;
    }

    /// <summary><see cref="Xtc3Bridge.HostExchange"/> 的参数:主机发来的一个包和中间层的响应。</summary>
    public sealed class HostExchangeEventArgs : EventArgs
    {
        /// <summary>创建。</summary>
        public HostExchangeEventArgs(byte[] request, byte[]? response, IReadOnlyList<Xtc3Command> commands, IReadOnlyList<Xtc3CommandResult> results, TimeSpan duration)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Response = response;
            Commands = commands ?? throw new ArgumentNullException(nameof(commands));
            Results = results ?? throw new ArgumentNullException(nameof(results));
            Duration = duration;
        }

        /// <summary>收到的包 (消息,或长度/校验和错误时的原始字节,或超时丢弃的字节)。</summary>
        public byte[] Request { get; }

        /// <summary>发出的响应包;不回答时 (超时丢弃) 为 null。</summary>
        public byte[]? Response { get; }

        /// <summary>解析出的命令;包错误时为空。</summary>
        public IReadOnlyList<Xtc3Command> Commands { get; }

        /// <summary>各命令的结果,与 <see cref="Commands"/> 对应。</summary>
        public IReadOnlyList<Xtc3CommandResult> Results { get; }

        /// <summary>处理用时。</summary>
        public TimeSpan Duration { get; }
    }
}
