using System;

namespace Zevision.Communication
{
    /// <summary>
    /// 帧格式选项,必须与设备的 G1304 (校验和) 和 G1305 (长度字节) 设置一致 (TCP 为 G1407、G1408)。
    /// 两个选项相同的实例相等。
    /// </summary>
    public sealed class FrameOptions : IEquatable<FrameOptions>
    {
        /// <summary>不带长度和校验和的基本帧格式 (设备默认)。</summary>
        public static readonly FrameOptions Plain = new FrameOptions(checksumEnabled: false, lengthEnabled: false);

        /// <summary>
        /// 创建帧格式选项。
        /// </summary>
        public FrameOptions(bool checksumEnabled, bool lengthEnabled)
        {
            ChecksumEnabled = checksumEnabled;
            LengthEnabled = lengthEnabled;
        }

        /// <summary>帧尾 (停止符之前) 是否带 1 字节校验和。</summary>
        public bool ChecksumEnabled { get; }

        /// <summary>起始符之后是否带 2 字节长度 (高字节在前)。</summary>
        public bool LengthEnabled { get; }

        /// <inheritdoc />
        public bool Equals(FrameOptions? other) =>
            other != null && other.ChecksumEnabled == ChecksumEnabled && other.LengthEnabled == LengthEnabled;

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as FrameOptions);

        /// <inheritdoc />
        public override int GetHashCode() => (ChecksumEnabled ? 1 : 0) | (LengthEnabled ? 2 : 0);

        /// <inheritdoc />
        public override string ToString() => $"checksum {(ChecksumEnabled ? "on" : "off")}, length {(LengthEnabled ? "on" : "off")}";
    }
}
