using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Localization;
using Zevision.Models;
using Zevision.Services;
using Zevision.ViewModels;
using Zevision.Views;

namespace Zevision.Tests
{
    /// <summary>
    /// 中英文:语言判定、英文界面文本 (不能残留中文)、指令英文说明 (来自设备 ? 帮助表的 DESCRIPTION)。
    /// 英文测试用 <see cref="Lang.Override"/>,只作用于当前测试,不影响并行运行的中文测试。
    /// </summary>
    public class LocalizationTests
    {
        private static readonly Regex Han = new Regex("[一-鿿]");

        /// <summary>自动提取时匹配不准、由人工补充英文说明的指令 (见 ZevisionCommandSet.EnglishNames)。</summary>
        private static readonly HashSet<string> ManualEnglishNames = new HashSet<string>
        {
            "M**", "F**", "L**", "P02**", "L**02**06**", "P02**07**", "N**0B**", "N**1A**", "S**0C**", "S**0D**",
            "N**13", "G09**01", "G1203", "S**0900", "S**09**", "S**09FE", "S**1E",
            "T0101**", "T0102**", // 设备说明带编号 (Input 1: Basic Event),英文名取共同部分
        };

        // ---------- 语言判定 ----------

        [Theory]
        [InlineData("zh-CN", "zh-CN", null, UiLanguage.Chinese)]
        [InlineData("en-US", "zh-CN", null, UiLanguage.Chinese)]   // 英文系统、中国地区
        [InlineData("zh-TW", "zh-TW", null, UiLanguage.Chinese)]   // 中文系统
        [InlineData("en-US", "en-US", null, UiLanguage.English)]
        [InlineData("de-DE", "de-DE", "auto", UiLanguage.English)]
        [InlineData("zh-CN", "zh-CN", "en", UiLanguage.English)]   // 配置强制英文
        [InlineData("en-US", "en-US", "ZH", UiLanguage.Chinese)]   // 配置强制中文,不区分大小写
        [InlineData("en-US", "en-US", "xx", UiLanguage.English)]   // 无法识别的配置按系统
        public void Detect(string ui, string format, string? setting, UiLanguage expected)
        {
            Assert.Equal(expected, Lang.Detect(new CultureInfo(ui), new CultureInfo(format), setting));
        }

        [Fact]
        public void Override_OnlyWithinScope()
        {
            Assert.True(Lang.IsChinese); // 测试默认中文
            using (Lang.Override(UiLanguage.English))
            {
                Assert.False(Lang.IsChinese);
                Assert.Equal("Connect", UiText.Connect);
            }

            Assert.True(Lang.IsChinese);
            Assert.Equal("连接", UiText.Connect);
        }

        // ---------- 指令集 ----------

        [Fact]
        public void CommandSet_EnglishLabelsAndRanges()
        {
            using (Lang.Override(UiLanguage.English))
            {
                Assert.Equal("Density (g/cm^3)", ZevisionCommandSet.Find("M0103")!.Label()); // 设备说明已带单位
                Assert.Equal("Brightness (%)", ZevisionCommandSet.Find("G0E03")!.Label());
                Assert.Equal("Dim Timer (min)", ZevisionCommandSet.Find("G0E04")!.Label());
                Assert.Equal("Pre Condition 2 · Start Ramp/Rise time", ZevisionCommandSet.Find("F01030201")!.LabelFor("F01030201"));
                Assert.Equal("0 - Unassigned", ZevisionCommandSet.Find("O0103")!.Options[0].GetLabel(CommandText.Language));
                Assert.Equal("H:MM:SS (0:00:00 ~ 150:00:00)", ZevisionCommandSet.Find("F010402")!.RangeText());
                Assert.Equal("0=None, number of an existing material", ZevisionCommandSet.Find("F0102")!.RangeText());
            }

            Assert.Equal("密度 (g/cm³)", ZevisionCommandSet.Find("M0103")!.Label());
            Assert.Equal("调光定时 (分钟)", ZevisionCommandSet.Find("G0E04")!.Label());
        }

        [Fact]
        public void CommandSet_EnglishTextsContainNoChinese()
        {
            var bad = new List<string>();
            using (Lang.Override(UiLanguage.English))
            {
                foreach (var d in ZevisionCommandSet.All)
                {
                    var texts = new[] { d.Name(), d.Label(), d.UnitText(), d.RemarkText(), d.RangeText() }
                        .Concat(d.IndexNames())
                        .Concat(d.Options.Select(o => o.GetLabel(CommandText.Language)));
                    bad.AddRange(texts.Where(t => Han.IsMatch(t)).Select(t => d.Pattern + ": " + t));
                }
            }

            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        }

        /// <summary>英文说明就是设备 ? 帮助表中的 DESCRIPTION (人工补充的少数项除外)。</summary>
        [Fact]
        public void CommandSet_EnglishNamesComeFromDeviceHelp()
        {
            var help = RealHelpTable();
            var mismatches = new List<string>();
            foreach (var d in ZevisionCommandSet.All.Where(d => !ManualEnglishNames.Contains(d.Pattern)))
            {
                var entry = help.FirstOrDefault(e => d.IsMatch(e.Id));
                if (entry == null)
                {
                    mismatches.Add(d.Pattern + ": not in the help table");
                }
                else if (entry.Description != d.EnglishName)
                {
                    mismatches.Add($"{d.Pattern}: \"{d.EnglishName}\" != \"{entry.Description}\"");
                }
            }

            Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
        }

        // ---------- 界面文本 ----------

        [Fact]
        public void UiText_BothLanguagesPresent_EnglishHasNoChinese()
        {
            var properties = typeof(UiText).GetProperties(BindingFlags.Public | BindingFlags.Static);
            Assert.True(properties.Length > 40);

            var bad = new List<string>();
            foreach (var property in properties)
            {
                var chinese = (string)property.GetValue(null)!;
                string english;
                using (Lang.Override(UiLanguage.English))
                {
                    english = (string)property.GetValue(null)!;
                }

                if (string.IsNullOrEmpty(chinese) || string.IsNullOrEmpty(english) || Han.IsMatch(english))
                {
                    bad.Add($"{property.Name}: \"{chinese}\" / \"{english}\"");
                }
            }

            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        }

        [Fact]
        public void EditorRow_English_UsesDeviceDescription()
        {
            using (Lang.Override(UiLanguage.English))
            {
                var density = RecipeItemViewModel.Create(new RecipeEntry { Id = "M0103", Value = "5.890", Description = "Density (g/cm^3)" }, new ParameterOptions(Array.Empty<RecipeEntry>()));
                var ramp = RecipeItemViewModel.Create(new RecipeEntry { Id = "F01030201", Value = "0:00:00", Description = "Start Ramp/Rise time" }, new ParameterOptions(Array.Empty<RecipeEntry>()));

                Assert.Equal("Density (g/cm^3)", density.Label);
                Assert.Equal("Pre Condition 2 · Start Ramp/Rise time", ramp.Label);
                Assert.StartsWith("M0103  Density (g/cm^3)", density.ToolTip);
                Assert.Contains("Range: 0.100 ~ 100.000 g/cm³", density.ToolTip);
                Assert.False(Han.IsMatch(density.ToolTip), density.ToolTip);
            }
        }

        [Fact]
        public void Messages_English()
        {
            using (Lang.Override(UiLanguage.English))
            {
                Assert.Contains("Out of range", ParameterValue.Validate(ZevisionCommandSet.Find("G0E03"), "101"));
                Assert.Equal("Checksum error", ZevisionErrorCode.ChecksumError.Describe());
                using (var main = new MainViewModel(new FakeDeviceService(), new MemoryPreferencesStore(new ConnectionPreferences()), new InstrumentConfigService(new FakeDeviceService()), new FakeDialogService(), new FakeErrorLog()))
                {
                    Assert.StartsWith("IMC300 Communication Test - Zevision ", main.Title);
                }
            }
        }

        [Fact]
        public void LanguageSetting_IsPreserved()
        {
            var store = new MemoryPreferencesStore(new ConnectionPreferences { Language = "en" });
            using (var main = new MainViewModel(new FakeDeviceService(), store, new InstrumentConfigService(new FakeDeviceService()), new FakeDialogService(), new FakeErrorLog()))
            {
                main.SavePreferences();
            }

            Assert.Equal("en", store.Saved!.Language);
        }

        [Fact]
        public void SwitchLanguage_Confirmed_SavesTargetAndRequestsRestart()
        {
            var store = new MemoryPreferencesStore(new ConnectionPreferences());
            var dialogs = new FakeDialogService();
            using (var main = new MainViewModel(new FakeDeviceService(), store, new InstrumentConfigService(new FakeDeviceService()), dialogs, new FakeErrorLog()))
            {
                var restarts = 0;
                main.RestartRequested += (s, e) => restarts++;
                Assert.Equal("EN", main.SwitchLanguageText);

                main.SwitchLanguageCommand.Execute(null);
                main.SavePreferences(); // App 在重启前保存

                Assert.Equal(1, restarts);
                Assert.Contains("重新启动", Assert.Single(dialogs.Confirms));
                Assert.Equal("en", store.Saved!.Language);
            }
        }

        [Fact]
        public void SwitchLanguage_Declined_KeepsLanguage()
        {
            var store = new MemoryPreferencesStore(new ConnectionPreferences());
            var dialogs = new FakeDialogService { ConfirmResult = false };
            using (var main = new MainViewModel(new FakeDeviceService(), store, new InstrumentConfigService(new FakeDeviceService()), dialogs, new FakeErrorLog()))
            {
                var restarts = 0;
                main.RestartRequested += (s, e) => restarts++;

                main.SwitchLanguageCommand.Execute(null);
                main.SavePreferences();

                Assert.Equal(0, restarts);
                Assert.Equal("auto", store.Saved!.Language);
            }
        }

        [Fact]
        public void SwitchLanguage_FromEnglish_TargetsChinese()
        {
            using (Lang.Override(UiLanguage.English))
            {
                var store = new MemoryPreferencesStore(new ConnectionPreferences());
                using (var main = new MainViewModel(new FakeDeviceService(), store, new InstrumentConfigService(new FakeDeviceService()), new FakeDialogService(), new FakeErrorLog()))
                {
                    Assert.Equal("中", main.SwitchLanguageText);
                    main.SwitchLanguageCommand.Execute(null);
                    main.SavePreferences();
                }

                Assert.Equal("zh", store.Saved!.Language);
            }
        }

        private static IReadOnlyList<HelpTableEntry> RealHelpTable()
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "imc300_1.8.11_h01_h02_help.bin"));
            var frames = new ZevisionFrameDecoder(new FrameOptions(checksumEnabled: true, lengthEnabled: true)).Push(bytes, 0, bytes.Length);
            return HelpTableParser.Parse(ZevisionResponseParser.Parse(frames[2].Payload)[0].Data!);
        }

        /// <summary>中文界面中可以保留英文的技术名词 (协议、接口、缩写、单位)。</summary>
        private static readonly HashSet<string> ChineseUiAllowedEnglish = new HashSet<string>(StringComparer.Ordinal)
        {
            "RS232", "DHCP", "IPv4", "IP", "API", "MAC", "FTP", "XTC3", "XIU", "DAC", "PID", "BCD", "QCM", "ID", "Zevision", "PI", "Name", "Active", "Layer",
        };

        [Fact]
        public void ChineseUi_CommandNamesAndOptions_AreTranslated()
        {
            // 用户要求 (2026-10-16):中文界面不出现未翻译的英文;快门统一为挡板。
            var problems = new List<string>();
            foreach (var definition in ZevisionCommandSet.All)
            {
                var texts = new[] { definition.Name() }.Concat(definition.Options.Select(o => o.TextValue()));
                foreach (var text in texts)
                {
                    var words = System.Text.RegularExpressions.Regex.Matches(text, "[A-Za-z][A-Za-z0-9]+").Cast<System.Text.RegularExpressions.Match>()
                        .Select(m => m.Value).Where(w => !ChineseUiAllowedEnglish.Contains(w)).ToList();
                    if (words.Count > 0 || text.Contains("快门"))
                    {
                        problems.Add($"{definition.Pattern}: {text}");
                    }
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }
    }
}
