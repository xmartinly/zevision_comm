using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Zevision.Communication;

namespace Zevision.Tests
{
    /// <summary>
    /// 按实机的帧格式行为应答的模拟设备 (IMC300 固件 1.8.11,2026-10-19 实测),用作 <see cref="FakeSerialTransport.Responder"/>:
    /// 只认自己当前的帧格式;格式不对的输入按实测回答错误:无长度无校验时回显中的二进制字节为 0 并回 <c>-A</c>
    /// (首字节可打印时原样回显并回 <c>-D</c>),只启用长度时回 <c>-T</c>,启用校验和时回 <c>-C</c>。
    /// 修改帧格式设置的 <c>U</c> 命令按旧格式回答,下一帧起使用新格式。
    /// </summary>
    internal sealed class SimulatedImc300
    {
        private FrameOptions? _pendingFrame;

        public SimulatedImc300(FrameOptions frame, bool tcp = false)
        {
            Frame = frame;
            Tcp = tcp;
        }

        /// <summary>设备在这个接口上使用的帧格式。</summary>
        public FrameOptions Frame { get; private set; }

        /// <summary>是否模拟以太网接口 (帧格式设置为 G1407/G1408,否则为 G1304/G1305)。</summary>
        public bool Tcp { get; }

        public string Model { get; set; } = "IMC300";

        /// <summary>系统序列号 (G06);为 null 时设备对 <c>QG06</c> 回答 <c>-A</c>。实机为 <c>XXXXXXXX</c>。</summary>
        public string? SerialNumber { get; set; } = "XXXXXXXX";

        /// <summary>不应答任何输入 (设备关机、接错端口)。</summary>
        public bool Silent { get; set; }

        /// <summary>对任何输入都回送这些字节 (如波特率不对时的乱码)。</summary>
        public byte[]? Garbage { get; set; }

        /// <summary>查询帧格式设置时报告的值 (校验和、长度);为 null 时报告实际使用的格式。</summary>
        public (string Checksum, string Length)? ReportedSettings { get; set; }

        /// <summary>按当前帧格式正确收到的命令帧。</summary>
        public List<string> Commands { get; } = new List<string>();

        /// <summary>收到的帧格式错误的请求个数。</summary>
        public int RejectedFrames { get; private set; }

        private string ChecksumId => Tcp ? "G1407" : "G1304";

        private string LengthId => Tcp ? "G1408" : "G1305";

        public byte[]? Respond(byte[] request)
        {
            if (Silent)
            {
                return null;
            }

            if (Garbage != null)
            {
                return Garbage;
            }

            if (!TryRead(request, out var command))
            {
                RejectedFrames++;
                return Reject(request);
            }

            Commands.Add(command);
            var payload = string.Join(";", command.Split(';').Select(Execute));
            var reply = ZevisionFrameCodec.Encode(payload, Frame);
            if (_pendingFrame != null)
            {
                Frame = _pendingFrame;
                _pendingFrame = null;
            }

            return reply;
        }

        private string Execute(string command)
        {
            switch (command)
            {
                case "H01":
                    return "H01+:Zevision " + Model + " 1.8.11.202606101229";
                case "QG03":
                    return "QG03+:" + Model;
                case "QG06":
                    return SerialNumber == null ? "QG06-A" : "QG06+:" + SerialNumber;
            }

            if (command == "Q" + ChecksumId)
            {
                return command + "+:" + (ReportedSettings?.Checksum ?? Flag(Frame.ChecksumEnabled));
            }

            if (command == "Q" + LengthId)
            {
                return command + "+:" + (ReportedSettings?.Length ?? Flag(Frame.LengthEnabled));
            }

            if (command.StartsWith("U" + ChecksumId + ":", StringComparison.Ordinal))
            {
                _pendingFrame = new FrameOptions(command.EndsWith("1", StringComparison.Ordinal), (_pendingFrame ?? Frame).LengthEnabled);
                return "U" + ChecksumId + "+";
            }

            if (command.StartsWith("U" + LengthId + ":", StringComparison.Ordinal))
            {
                _pendingFrame = new FrameOptions((_pendingFrame ?? Frame).ChecksumEnabled, command.EndsWith("1", StringComparison.Ordinal));
                return "U" + LengthId + "+";
            }

            return command + "-A";
        }

        private static string Flag(bool value) => value ? "1" : "0";

        /// <summary>按设备当前的帧格式读出命令;格式不对 (长度、校验和不符,或含不可打印字节) 时返回 false。</summary>
        private bool TryRead(byte[] request, out string command)
        {
            command = string.Empty;
            if (request.Length < 2 || request[0] != ZevisionFrameCodec.StartByte || request[request.Length - 1] != ZevisionFrameCodec.StopByte)
            {
                return false;
            }

            var start = 1;
            var end = request.Length - 1; // 停止符的位置
            if (Frame.LengthEnabled)
            {
                if (request.Length < 4)
                {
                    return false;
                }

                var length = (request[1] << 8) | request[2];
                start = 3;
                if (start + length + (Frame.ChecksumEnabled ? 1 : 0) != end)
                {
                    return false;
                }
            }

            if (Frame.ChecksumEnabled)
            {
                end--;
                if (end < start || ZevisionFrameCodec.ComputeChecksum(request, start, end - start) != request[end])
                {
                    return false;
                }
            }

            var data = request.Skip(start).Take(end - start).ToArray();
            if (data.Length == 0 || data.Any(b => b < 0x20 || b >= 0x7F))
            {
                return false;
            }

            command = Encoding.ASCII.GetString(data);
            return true;
        }

        /// <summary>格式不对的输入的回答 (按设备当前的帧格式发送)。</summary>
        private byte[] Reject(byte[] request)
        {
            if (Frame.ChecksumEnabled)
            {
                return ZevisionFrameCodec.Encode("-C", Frame);
            }

            if (Frame.LengthEnabled)
            {
                return ZevisionFrameCodec.Encode("-T", Frame);
            }

            // 无长度无校验:起止符之间的内容作为命令回显;首字节不可打印时整段回显为 0。
            var inner = request.Skip(1).Take(Math.Max(request.Length - 2, 0)).ToArray();
            var printableStart = inner.Length > 0 && inner[0] >= 0x20 && inner[0] < 0x7F;
            var echo = printableStart ? inner : new byte[inner.Length];
            var code = Encoding.ASCII.GetBytes(printableStart ? "-D" : "-A");
            return new[] { ZevisionFrameCodec.StartByte }.Concat(echo).Concat(code).Concat(new[] { ZevisionFrameCodec.StopByte }).ToArray();
        }
    }
}
