using System;
using System.Text;

namespace Zevision.Communication
{
    /// <summary>
    /// Zevision 帧的编码与校验和计算。显示格式化由调用方负责。
    /// </summary>
    /// <remarks>
    /// 帧格式:<c>{</c> [长度高 长度低] 命令数据 [校验和] <c>}</c>。
    /// 长度和校验和只计算命令数据,不含起止符、长度和校验和字节。
    /// </remarks>
    public static class ZevisionFrameCodec
    {
        /// <summary>起始符 <c>{</c>。</summary>
        public const byte StartByte = (byte)'{';

        /// <summary>停止符 <c>}</c>。</summary>
        public const byte StopByte = (byte)'}';

        /// <summary>命令数据的最大长度 (字节)。</summary>
        public const int MaxDataLength = 57000;

        /// <summary>
        /// 附加到命令末尾、用来改变校验和的无副作用指令 (Hello:只读,总是成功)。
        /// </summary>
        public const string FillerCommand = "H01";

        /// <summary>
        /// 将命令数据编码为完整帧。
        /// </summary>
        /// <param name="commandData">命令数据,如 <c>H01</c> 或 <c>SC03;SC04</c>,不含起止符。</param>
        /// <param name="options">帧格式选项。</param>
        /// <exception cref="ArgumentException">命令不合法 (见 <see cref="ProtocolText.ValidateCommand"/>) 或超长。</exception>
        public static byte[] Encode(string commandData, FrameOptions options)
        {
            if (commandData is null)
            {
                throw new ArgumentNullException(nameof(commandData));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var error = ProtocolText.ValidateCommand(commandData);
            if (error != null)
            {
                throw new ArgumentException(error, nameof(commandData));
            }

            if (commandData.Length > MaxDataLength)
            {
                throw new ArgumentException($"The command is longer than {MaxDataLength} bytes.", nameof(commandData));
            }

            var data = Encoding.ASCII.GetBytes(commandData);
            var frame = new byte[data.Length + 2 + (options.LengthEnabled ? 2 : 0) + (options.ChecksumEnabled ? 1 : 0)];
            var index = 0;

            frame[index++] = StartByte;
            if (options.LengthEnabled)
            {
                frame[index++] = (byte)(data.Length >> 8);
                frame[index++] = (byte)(data.Length & 0xFF);
            }

            Buffer.BlockCopy(data, 0, frame, index, data.Length);
            index += data.Length;

            if (options.ChecksumEnabled)
            {
                frame[index++] = ComputeChecksum(data, 0, data.Length);
            }

            frame[index] = StopByte;
            return frame;
        }

        /// <summary>
        /// 编码命令;若帧头/帧尾的二进制字节 (长度、校验和) 恰好等于帧分隔符,则在命令后追加 <c>;H01</c> 后重新编码。
        /// </summary>
        /// <remarks>
        /// 实测 IMC300 固件 1.8.11:即使启用了长度字节,设备收到校验和为 0x7D (<c>}</c>) 的帧时,
        /// 仍把它当作停止符,回复 <c>-C</c> (校验和错误)。例:<c>AL0702010600</c> 的校验和正好是 0x7D。
        /// 已确认的只有 <c>}</c>;<c>{</c>、<c>&lt;</c>、<c>&gt;</c> 是协议的其他分隔符,作为预防一并避开。
        /// 追加的 H01 会让设备多回一条 H01 响应,调用方取第一条响应即可。
        /// </remarks>
        /// <param name="command">命令数据。</param>
        /// <param name="options">帧格式选项。</param>
        /// <param name="sentCommand">实际编码的命令 (可能带 <c>;H01</c>)。</param>
        /// <exception cref="ArgumentException">命令不合法 (见 <see cref="Encode"/>)。</exception>
        public static byte[] EncodeAvoidingDelimiters(string command, FrameOptions options, out string sentCommand)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            sentCommand = command;
            var frame = Encode(sentCommand, options);
            for (var attempt = 0; attempt < 3 && HasDelimiterInBinaryFields(frame, options); attempt++)
            {
                sentCommand += ";" + FillerCommand;
                frame = Encode(sentCommand, options);
            }

            return frame;
        }

        /// <summary>
        /// 帧中的长度字节或校验和是否等于帧分隔符 <c>{ } &lt; &gt;</c>。
        /// </summary>
        public static bool HasDelimiterInBinaryFields(byte[] frame, FrameOptions options)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.LengthEnabled && (IsDelimiter(frame[1]) || IsDelimiter(frame[2])))
            {
                return true;
            }

            return options.ChecksumEnabled && IsDelimiter(frame[frame.Length - 2]);
        }

        /// <summary>
        /// 计算校验和:对命令数据逐字节求和,取模 256。
        /// </summary>
        public static byte ComputeChecksum(byte[] data, int offset, int count)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var sum = 0;
            for (var i = offset; i < offset + count; i++)
            {
                sum += data[i];
            }

            return (byte)(sum & 0xFF);
        }

        private static bool IsDelimiter(byte b) => b == StartByte || b == StopByte || b == (byte)'<' || b == (byte)'>';
    }
}
