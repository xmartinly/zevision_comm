using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Zevision.Communication;
using Zevision.Core.Configuration;
using Zevision.Storage;

namespace Zevision.Tests
{
    /// <summary>
    /// 用实机录下的原始字节回放解码,防止协议格式回归。
    /// </summary>
    public class RealCaptureTests
    {
        private static readonly FrameOptions LengthAndChecksum = new FrameOptions(checksumEnabled: true, lengthEnabled: true);

        private static byte[] LoadCapture() =>
            File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "imc300_1.8.11_h01_h02_help.bin"));

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(4096)]
        [InlineData(200000)]
        public void Decode_RealCapture_AllMessagesValid(int chunkSize)
        {
            var bytes = LoadCapture();
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);
            var frames = new List<ZevisionFrame>();
            for (var offset = 0; offset < bytes.Length; offset += chunkSize)
            {
                frames.AddRange(decoder.Push(bytes, offset, Math.Min(chunkSize, bytes.Length - offset)));
            }

            Assert.All(frames, f => Assert.True(f.IsValid, f.Error.ToString()));
            Assert.Equal(new[] { "H01", "H02", "?", "H01", "H02", "H01" }, frames.Select(f => f.Payload.Substring(0, f.Payload.IndexOf('+'))));

            // ? 的响应:3 包,最后一包的长度字段比实际数据多 2 (固件缺陷)。
            var help = frames[2];
            Assert.Equal(3, help.PacketCount);
            Assert.Equal(new[] { 56931, 56957, 2604 }, help.DeclaredLengths);
            Assert.Equal(new[] { 56931, 56957, 2602 }, help.DataRanges.Select(r => r.Length));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4096)]
        public void Decode_HelpReplyWithoutLengthAndChecksum_InLengthMode(int chunkSize)
        {
            // 2026-10-18 实测 RS232 (G1304 = G1305 = 1):设备把 ? 的响应作为一个不带长度和校验和的 {数据} 发送。
            // 用录下的真实帮助表按这种格式重新组帧,解码器 (长度 + 校验和模式) 应完整读出,帮助表解析结果相同。
            var decoder = new ZevisionFrameDecoder(LengthAndChecksum);
            var help = decoder.Push(LoadCapture(), 0, LoadCapture().Length)[2];
            var plain = new[] { ZevisionFrameCodec.StartByte }.Concat(help.Data).Concat(new[] { ZevisionFrameCodec.StopByte })
                .Concat(ZevisionFrameCodec.Encode("H01+:Zevision IMC300", LengthAndChecksum)).ToArray();

            var frames = new List<ZevisionFrame>();
            var replay = new ZevisionFrameDecoder(LengthAndChecksum);
            for (var offset = 0; offset < plain.Length; offset += chunkSize)
            {
                frames.AddRange(replay.Push(plain, offset, Math.Min(chunkSize, plain.Length - offset)));
            }

            Assert.Equal(2, frames.Count);
            Assert.True(frames[0].IsValid, frames[0].Error.ToString());
            Assert.Equal(help.Payload, frames[0].Payload);
            Assert.Equal(HelpTableParser.Parse(help.Payload.Substring(3)).Count, HelpTableParser.Parse(frames[0].Payload.Substring(3)).Count);
            Assert.Equal("H01+:Zevision IMC300", frames[1].Payload);
        }

        [Fact]
        public void HelpReply_ParsesIntoSnapshotAndRecipe()
        {
            var bytes = LoadCapture();
            var frames = new ZevisionFrameDecoder(LengthAndChecksum).Push(bytes, 0, bytes.Length);
            var response = Assert.Single(ZevisionResponseParser.Parse(frames[2].Payload));

            var snapshot = new InstrumentSnapshot(HelpTableParser.Parse(response.Data!));
            var recipe = snapshot.ToRecipe(DateTimeOffset.Now);

            Assert.Equal("IMC300", snapshot.DeviceName);
            Assert.Equal("1.8.11.202606101229", snapshot.FirmwareVersion);
            Assert.Equal("Ready", snapshot.CurrentState);
            Assert.Equal("Film 10", snapshot.GetValue("F0A01"));
            Assert.True(snapshot.Entries.Count > 1500, snapshot.Entries.Count.ToString());

            Assert.Contains(recipe.Entries, e => e.Id == "F0A01" && e.Value == "Film 10");
            Assert.Contains(recipe.Entries, e => e.Id == "M1001" && e.Value == "Al");
            Assert.DoesNotContain(recipe.Entries, e => e.Id.StartsWith("G13", StringComparison.Ordinal) || e.Id.StartsWith("C", StringComparison.Ordinal));
        }

        [Fact]
        public void Editor_RealCapture_LayerShowsFilmRateThickness()
        {
            var bytes = LoadCapture();
            var frames = new ZevisionFrameDecoder(LengthAndChecksum).Push(bytes, 0, bytes.Length);
            var snapshot = new InstrumentSnapshot(HelpTableParser.Parse(ZevisionResponseParser.Parse(frames[2].Payload)[0].Data!));
            var config = new InstrumentConfigService(new FakeDeviceService());
            var editor = new Zevision.ViewModels.RecipeEditorViewModel(RecipeEditSession.Open(snapshot.ToRecipe(DateTimeOffset.Now), null, config, new RecipeFileStore()), new FakeDialogService(), new FakeErrorLog());

            var layer = editor.Groups.Single(g => g.Key == "L02");
            System.Console.WriteLine(string.Join("; ", layer.Items.Select(i => i.Id + " | " + i.Label)));
            Assert.Contains(layer.Items, i => i.Id == "L02020101"); // 膜系
            Assert.Contains(layer.Items, i => i.Id == "L02020102"); // 速率
            Assert.Contains(layer.Items, i => i.Id == "L02020103"); // 厚度
        }
    }
}
