using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Zevision.Communication;
using Zevision.Core.Configuration;

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
            var editor = new Zevision.ViewModels.RecipeEditorViewModel(snapshot.ToRecipe(DateTimeOffset.Now), null, config, new FakeDialogService(), new FakeErrorLog());

            var layer = editor.Groups.Single(g => g.Key == "L02");
            System.Console.WriteLine(string.Join("; ", layer.Items.Select(i => i.Id + " | " + i.Label)));
            Assert.Contains(layer.Items, i => i.Id == "L02020101"); // 膜系
            Assert.Contains(layer.Items, i => i.Id == "L02020102"); // 速率
            Assert.Contains(layer.Items, i => i.Id == "L02020103"); // 厚度
        }
    }
}
