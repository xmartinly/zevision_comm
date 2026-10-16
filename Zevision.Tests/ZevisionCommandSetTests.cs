using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
using Zevision.Commands;
using Zevision.Communication;
using Zevision.Localization;
using Zevision.Models;
using Zevision.Services;

namespace Zevision.Tests
{
    public class ZevisionCommandSetTests
    {
        private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        // ---------- 按命令组枚举 ----------

        [Fact]
        public void GetCommands_Materials()
        {
            Assert.True(ZevisionCommandSet.TryGetGroup("M0103", out var group));
            Assert.Equal(CommandGroup.M, group);

            var patterns = ZevisionCommandSet.GetCommands(group).Select(d => d.Pattern);

            Assert.Equal(new[] { "M00", "M**", "M**01", "M**03", "M**04", "M**07", "MFE" }, patterns);
        }

        [Fact]
        public void GetCommands_EveryGroupHasCommands()
        {
            foreach (CommandGroup group in Enum.GetValues(typeof(CommandGroup)))
            {
                var commands = ZevisionCommandSet.GetCommands(group);
                Assert.True(commands.Count > 0, group.ToString());
                Assert.All(commands, d => Assert.Equal(group, d.Group));
            }
        }

        [Theory]
        [InlineData("M0103", true, CommandGroup.M)]
        [InlineData("f0102", true, CommandGroup.F)]
        [InlineData("P020101", true, CommandGroup.P)]
        [InlineData("H01", true, CommandGroup.H)]
        [InlineData("?", false, default(CommandGroup))]
        [InlineData("X01", false, default(CommandGroup))]
        [InlineData("1", false, default(CommandGroup))]
        [InlineData("", false, default(CommandGroup))]
        public void TryGetGroup(string id, bool found, CommandGroup expected)
        {
            Assert.Equal(found, ZevisionCommandSet.TryGetGroup(id, out var group));
            Assert.Equal(expected, group);
        }

        [Theory]
        [InlineData("M0103", "M**03", "密度")]
        [InlineData("MFE", "MFE", "材料数量")]
        [InlineData("M03", "M**", "添加/删除指定编号的材料")]
        [InlineData("M00", "M00", "在列表末尾添加/删除材料")]
        [InlineData("F01030102", "F**03**02", "斜坡功率")]
        [InlineData("F01050102", "F**05**02", "后条件斜坡功率")]
        [InlineData("F0103FE", "F**03FE", "预条件数量")]
        [InlineData("L020201060101", "L**02**06**01", "起始厚度")]
        [InlineData("P02010701", "P02**07**", "膜层 ID")]
        [InlineData("P020107FE", "P02**07FE", "膜层数量")]
        [InlineData("N010B03", "N**0B**", "晶振切换输出设置")]
        [InlineData("G1304", "G1304", "RS232 校验和")]
        [InlineData("C11", "C11", "当前功率")]
        [InlineData("H01", "H01", "产品名 + 软件版本")]
        public void Find_ResolvesMostSpecificPattern(string id, string pattern, string name)
        {
            var definition = ZevisionCommandSet.Find(id);

            Assert.NotNull(definition);
            Assert.Equal(pattern, definition!.Pattern);
            Assert.Equal(name, definition.Name());
        }

        [Fact]
        public void LogicBasicIo_UsesInputAndOutputFunctions()
        {
            var input = ZevisionCommandSet.Find("T010103")!;
            var output = ZevisionCommandSet.Find("T010206")!;

            Assert.Equal("T0101**", input.Pattern);
            Assert.True(input.CanUpdate);
            Assert.Equal("输入 3 · 基本事件", input.LabelFor("T010103"));
            Assert.Equal("输出 6 · 基本动作", output.LabelFor("T010206"));
            Assert.Equal("密度 (g/cm³)", ZevisionCommandSet.Find("M0303")!.LabelFor("M0303"));
            using (Lang.Override(UiLanguage.English))
            {
                Assert.Equal("Output 6: Basic Action", output.LabelFor("T010206", "Output 6: Basic Action"));
                Assert.Equal("Input 3 · Basic Event", input.LabelFor("T010103"));
            }
            Assert.Equal("1", input.Options[1].Value);
            Assert.Equal("启动", input.Options[1].GetLabel(CommandText.Language).Split(new[] { " - " }, 2, StringSplitOptions.None)[1]);
            Assert.Contains(output.Options, o => o.Value == "33" && o.GetLabel(CommandText.Language).EndsWith("晶振切换中", StringComparison.Ordinal));
            Assert.Equal(ZevisionCommandSet.Find("I0103")!.Options.Select(o => o.Value), input.Options.Select(o => o.Value));
            Assert.Equal(ZevisionCommandSet.Find("O0103")!.Options.Select(o => o.Value), output.Options.Select(o => o.Value));
        }

        [Theory]
        [InlineData("N010B03", 'O')] // 晶振切换输出:实机值为输出编号,0 = 未分配
        [InlineData("N010E", 'O')]
        [InlineData("N010F", 'O')]
        [InlineData("N011A02", 'I')]
        [InlineData("S0105", 'O')]
        [InlineData("S010C01", 'O')]
        [InlineData("S010D01", 'I')]
        [InlineData("S010901", 'M')] // 坩埚材料
        public void RelaySettings_AreReferences(string id, char group)
        {
            var definition = ZevisionCommandSet.Find(id)!;

            Assert.Equal(CommandValueKind.Reference, definition.Kind);
            Assert.Equal(group, definition.ReferenceGroup);
            Assert.True(definition.AllowNone);
        }

        [Fact]
        public void LayerRateRamp_FromManual()
        {
            Assert.Equal(CommandValueKind.Time, ZevisionCommandSet.Find("L010201060102")!.Kind);
            Assert.Equal("0.000 ~ 999.999 Å/s", ZevisionCommandSet.Find("L010201060103")!.RangeText());
            Assert.False(ZevisionCommandSet.Find("L01020106FE")!.CanUpdate);
            Assert.False(ZevisionCommandSet.Find("L0102FE")!.CanUpdate);
        }

        [Fact]
        public void FunctionTables_FromManual()
        {
            var outputs = ZevisionCommandSet.Find("O0103")!.Options;
            var inputs = ZevisionCommandSet.Find("I0103")!.Options;

            Assert.Equal(60, outputs.Count); // 0~58 和 60
            Assert.Equal("36 - 计算机控制", outputs.Single(o => o.Value == "36").GetLabel(CommandText.Language));
            Assert.Equal("37 - 输出源 1 坩埚选择 (二进制)", outputs.Single(o => o.Value == "37").GetLabel(CommandText.Language));
            Assert.Equal("38 - 输出源 1 坩埚选择 (二进制) 第 2 位", outputs.Single(o => o.Value == "38").GetLabel(CommandText.Language));
            using (Lang.Override(UiLanguage.English))
            {
                Assert.Equal("36 - Computer Control", outputs.Single(o => o.Value == "36").GetLabel(CommandText.Language)); // 英文界面不变
            }
            Assert.DoesNotContain(outputs, o => o.Value == "59");
            Assert.Equal("24 - 选择工艺 1-99 (7 位)", inputs.Single(o => o.Value == "24").GetLabel(CommandText.Language));
            Assert.Equal("25 - 选择工艺 1-99 第 2 位", inputs.Single(o => o.Value == "25").GetLabel(CommandText.Language));
            using (Lang.Override(UiLanguage.English))
            {
                Assert.Equal("24 - Select Process 1-99 (7 bits)", inputs.Single(o => o.Value == "24").GetLabel(CommandText.Language));
            }
            Assert.DoesNotContain(inputs, o => o.Value == "36");
        }

        [Fact]
        public void MaxPowerOption_IsDropdown()
        {
            var definition = ZevisionCommandSet.Find("F010907")!;

            Assert.Equal(CommandValueKind.Enum, definition.Kind);
            Assert.Equal(new[] { "0 - 继续", "1 - 沉积后处理", "2 - 结束工艺", "3 - 暂停工艺" }, definition.Options.Select(o => o.GetLabel(CommandText.Language)));
            using (Lang.Override(UiLanguage.English))
            {
                Assert.Equal(new[] { "0 - Continue", "1 - Post deposit", "2 - End the process", "3 - Suspend process" }, definition.Options.Select(o => o.GetLabel(CommandText.Language)));
            }
        }

        [Theory]
        [InlineData("F010101")] // 膜下没有 01 子项
        [InlineData("T020101")] // 逻辑组只收录基本 IO 逻辑
        [InlineData("M0199")]
        [InlineData("")]
        public void Find_Unknown_ReturnsNull(string id)
        {
            Assert.Null(ZevisionCommandSet.Find(id));
        }

        // ---------- 生成指令 ----------

        [Fact]
        public void Build_FillsObjectNumberAsHex()
        {
            var density = ZevisionCommandSet.Find("M0103")!;

            Assert.Equal("UM0103:2.700", density.Build(CommandActions.Update, "2.700", 1));
            Assert.Equal("QM0103", density.Build(CommandActions.Query, 1));
            Assert.Equal("QM0A03", density.Build(CommandActions.Query, 10));
            Assert.Equal("QMC803", density.Build(CommandActions.Query, 200));
        }

        [Fact]
        public void Build_AllActionKinds()
        {
            Assert.Equal("UF01030202:50.00", ZevisionCommandSet.Find("F01030202")!.Build(CommandActions.Update, "50.00", 1, 2));
            Assert.Equal("AM03", ZevisionCommandSet.Find("M03")!.Build(CommandActions.Add, 3));
            Assert.Equal("DM00", ZevisionCommandSet.Find("M00")!.Build(CommandActions.Delete));
            Assert.Equal("AF010300", ZevisionCommandSet.Find("F010300")!.Build(CommandActions.Add, 1));
            Assert.Equal("RC03", ZevisionCommandSet.Find("C03")!.Build(CommandActions.Remote));
            Assert.Equal("RG16:1", ZevisionCommandSet.Find("G16")!.Build(CommandActions.Remote, "1"));
            Assert.Equal("SC11", ZevisionCommandSet.Find("C11")!.Build(CommandActions.Status));
            Assert.Equal("H01", ZevisionCommandSet.Find("H01")!.Build(CommandActions.None));
            Assert.Equal("E:abc", ZevisionCommandSet.Find("E")!.Build(CommandActions.None, "abc"));
        }

        [Fact]
        public void Build_InvalidUse_Throws()
        {
            var density = ZevisionCommandSet.Find("M0103")!;

            Assert.Throws<ArgumentException>(() => density.Build(CommandActions.Update, 1));                        // 更新需要数据
            Assert.Throws<ArgumentException>(() => density.Build(CommandActions.Add, 1));                           // 不支持的动作
            Assert.Throws<ArgumentException>(() => density.Build(CommandActions.Query | CommandActions.Update, 1)); // 多个动作
            Assert.Throws<ArgumentException>(() => density.Build(CommandActions.Query, "1", 1));                    // 查询不带数据
            Assert.Throws<ArgumentException>(() => density.Build(CommandActions.Update, "1;2", 1));                 // 非法字符
            Assert.Throws<ArgumentException>(() => density.Build(CommandActions.Query));                            // 缺编号
            Assert.Throws<ArgumentException>(() => density.Build(CommandActions.Query, 1, 2));                      // 多编号
            Assert.Throws<ArgumentOutOfRangeException>(() => density.Build(CommandActions.Query, 0));               // 00 是增删命令
            Assert.Throws<ArgumentOutOfRangeException>(() => density.Build(CommandActions.Query, 0xFE));            // FE 是数量
            Assert.Throws<ArgumentException>(() => ZevisionCommandSet.Find("MFE")!.Build(CommandActions.Update, "3"));
        }

        [Theory]
        [InlineData(CommandActions.Query, "M0103", null, "QM0103")]
        [InlineData(CommandActions.Update, "M0103", "2.700", "UM0103:2.700")]
        [InlineData(CommandActions.Add, "M03", null, "AM03")]
        [InlineData(CommandActions.Add, "F010300", null, "AF010300")]
        [InlineData(CommandActions.Update, "T020101", "1", "UT020101:1")] // 指令集未定义的 ID:只检查格式
        [InlineData(CommandActions.Update, "F01070107", "100.00", "UF01070107:100.00")]
        [InlineData(CommandActions.None, "H01", null, "H01")]
        public void BuildById(CommandActions action, string id, string? value, string expected)
        {
            Assert.Equal(expected, ZevisionCommandSet.Build(action, id, value));
        }

        [Theory]
        [InlineData(CommandActions.Update, "MFE", "3")]     // 只读
        [InlineData(CommandActions.Query, "M03", null)]     // M** 只能添加
        [InlineData(CommandActions.Query, "m0103", null)]   // 小写
        [InlineData(CommandActions.Query, "M013", null)]    // 编号不是两位
        [InlineData(CommandActions.Query, "?", null)]
        [InlineData(CommandActions.Query, "QM0103", null)]  // 已带动作字母
        [InlineData(CommandActions.Update, "M0101", null)]  // 缺数据
        [InlineData(CommandActions.Update, "M0101", "a;b")] // 非法字符
        public void BuildById_Invalid_Throws(CommandActions action, string id, string? value)
        {
            Assert.Throws<ArgumentException>(() => ZevisionCommandSet.Build(action, id, value));
        }

        [Fact]
        public void IndexNamesAndParseIndices()
        {
            var ramp = ZevisionCommandSet.Find("F01030201")!;

            Assert.Equal(2, ramp.IndexCount);
            Assert.Equal(new[] { "膜系", "预条件" }, ramp.IndexNames());
            Assert.Equal(new[] { 1, 2 }, ramp.ParseIndices("F01030201"));
            Assert.Throws<ArgumentException>(() => ramp.ParseIndices("M0103"));
        }

        [Fact]
        public void EveryIndexLevelIsNamed()
        {
            var unnamed = ZevisionCommandSet.All.Where(d => d.IndexNames().Contains("#")).Select(d => d.Pattern).ToList();

            Assert.Empty(unnamed);
        }

        /// <summary>实机帮助表中能识别的每个 ID:取出编号再生成,必须得到原 ID。</summary>
        [Fact]
        public void RealHelpTable_ParseAndFormatRoundTrip()
        {
            var checkedCount = 0;
            foreach (var id in RealHelpTableIds())
            {
                var definition = ZevisionCommandSet.Find(id);
                if (definition == null)
                {
                    continue;
                }

                var indices = definition.ParseIndices(id).ToArray();
                if (indices.All(i => i >= CommandDefinition.MinIndex && i <= CommandDefinition.MaxIndex))
                {
                    Assert.Equal(id, definition.FormatId(indices));
                    checkedCount++;
                }
            }

            Assert.True(checkedCount > 1000, checkedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // ---------- 与文档、实机核对 ----------

        /// <summary>《U 类型指令汇总》中的每个 U 指令都必须在指令集中可更新,说明一致。</summary>
        /// <summary>xlsx 名称 → 界面名称 (膜系、膜层、输出源的统一用语)。</summary>
        private static readonly Dictionary<string, string> UiNames = new Dictionary<string, string>
        {
            ["层控制"] = "膜层控制",
            ["活动层数量"] = "活动膜层数量",
            ["自动启动下一层"] = "自动启动下一膜层",
            ["层 ID"] = "膜层 ID",
            ["层名称"] = "膜层名称",
            ["膜 ID"] = "膜系 ID",
            ["传感器膜 tooling"] = "传感器膜系工具因子",
            ["源 ID"] = "输出源 ID",
            ["源名称"] = "输出源名称",
            ["源输出配置"] = "输出源输出配置",
        };

        /// <summary>
        /// 界面用语 (用户要求,2026-10-16):过程 → 工艺,快门 → 挡板;xlsx 中文名称里的英文译为中文
        /// (tooling → 工具因子、RateWatcher → 速率监视、Rate → 速率…)。
        /// </summary>
        private static string UiTerms(string name)
        {
            foreach (var (from, to) in new[]
            {
                ("过程", "工艺"), ("快门", "挡板"), (" tooling", "工具因子"), ("Tooling", "工具因子"),
                ("RateWatcher ", "速率监视"), ("RateWatcher", "速率监视"), ("Dev 图", "偏差图"), ("Ethernet ", "以太网"), ("Z-ratio", "Z 比"), ("Mix ", "混合"), ("carousel", "转盘"),
                ("Rate Deviation ", "速率偏差 "), ("Rate ", "速率 "), ("Thickness ", "厚度 "), ("Power ", "功率 "), ("Frequency ", "频率 "),
            })
            {
                name = name.Replace(from, to);
            }

            return name;
        }

        [Fact]
        public void EveryUCommandInSpreadsheet_IsUpdatableWithSameName()
        {
            var rows = ReadSpreadsheet().Skip(1).Where(r => r.Count >= 4).ToList();
            Assert.True(rows.Count > 100, "没有读到 xlsx 内容");

            var missing = new List<string>();
            foreach (var row in rows)
            {
                var pattern = NormalizePattern(row[1]);
                var definition = ZevisionCommandSet.All.FirstOrDefault(d => d.Pattern == pattern);
                // 界面用语与 xlsx 不同 (用户要求,2026-10-16):过程 → 工艺,膜 → 膜系,层 → 膜层,源 → 输出源。
                var name = UiNames.TryGetValue(row[3], out var renamed) ? renamed : UiTerms(row[3]);
                if (definition == null || !definition.CanUpdate || definition.Name() != name)
                {
                    missing.Add($"{row[1]} ({pattern}) {row[3]} → {definition?.Name() ?? "未定义"} {definition?.Actions}");
                }
            }

            Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
        }

        [Fact]
        public void Definition_LabelAndRangeIncludeUnit()
        {
            var density = ZevisionCommandSet.Find("M0103")!;

            Assert.Equal("密度 (g/cm³)", density.Label());
            Assert.Equal("0.100 ~ 100.000 g/cm³", density.RangeText());
            Assert.Equal("0=关, 1=开", ZevisionCommandSet.Find("G0E01")!.RangeText());
        }

        [Theory]
        [InlineData("M0103", "密度 (g/cm³)")]
        [InlineData("F01030201", "预条件 2 · 起始斜坡/上升时间")]
        [InlineData("F01050102", "后条件 1 · 后条件斜坡功率 (%)")]
        [InlineData("L020201060101", "膜系 1 斜坡 1 · 起始厚度 (kÅ)")]
        [InlineData("F01070201", "第 2 个传感器 · 传感器编号")]           // 膜系的传感器列表:第几项,不是传感器编号
        [InlineData("P0201070A", "膜层 10 · 膜层 ID")]
        [InlineData("N010B03", "输出 3 · 晶振切换输出设置")]
        public void LabelFor_SubListItemsShowListAndNumber(string id, string label)
        {
            Assert.Equal(label, ZevisionCommandSet.Find(id)!.LabelFor(id));
        }

        /// <summary>实机帮助表中的每个 ID 最多只能有一个"最具体"的定义,否则 Find 的结果取决于表中顺序。</summary>
        [Fact]
        public void RealHelpTable_NoAmbiguousDefinitions()
        {
            var ambiguous = RealHelpTableIds()
                .Select(id => (Id: id, Matches: ZevisionCommandSet.All.Where(d => d.IsMatch(id)).ToList()))
                .Where(x => x.Matches.Count > 1 && x.Matches.Count(d => d.Specificity == x.Matches.Max(m => m.Specificity)) > 1)
                .Select(x => x.Id)
                .ToList();

            Assert.Empty(ambiguous);
        }

        /// <summary>实机上数字和时间类可更新项的当前值都符合类型 (范围不检查,设备值可能超出文档范围)。</summary>
        [Fact]
        public void RealHelpTable_CurrentValuesMatchKind()
        {
            var bad = new List<string>();
            foreach (var entry in RealHelpTable())
            {
                var definition = ZevisionCommandSet.Find(entry.Id);
                if (definition == null || !definition.CanUpdate || string.IsNullOrEmpty(entry.Value))
                {
                    continue;
                }

                if ((definition.Kind == CommandValueKind.Number && !decimal.TryParse(entry.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                    || (definition.Kind == CommandValueKind.Time && !ParameterValue.TryParseTime(entry.Value, out _)))
                {
                    bad.Add($"{entry.Id} = {entry.Value} ({definition.Name()})");
                }
            }

            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
        }

        // ---------- 修改配置窗口的下拉选项 ----------

        [Fact]
        public void ParameterOptions_Reference_ListsExistingObjectsAfterNone()
        {
            var entries = new[]
            {
                new RecipeEntry { Id = "M0101", Value = "Al", Description = "Name" },
                new RecipeEntry { Id = "M0103", Value = "2.700", Description = "Density" },
                new RecipeEntry { Id = "M0A01", Value = "Au", Description = "Name" },
                new RecipeEntry { Id = "F0101", Value = "F1", Description = "Name" },
            };

            var options = new ParameterOptions(entries).For(ZevisionCommandSet.Find("F0102")!);

            Assert.Equal(new[] { "0 - 未选择", "1 - Al", "10 - Au" }, options.Select(o => o.Label));
        }

        [Fact]
        public void ParameterOptions_Enum_ReturnsFixedOptions()
        {
            var options = new ParameterOptions(Array.Empty<RecipeEntry>()).For(ZevisionCommandSet.Find("F010401")!);

            Assert.Equal(new[] { "0", "1", "2", "3" }, options.Select(o => o.Value));
            Assert.Equal("3 - 两者", options[3].Label);
        }

        /// <summary>
        /// 表格中的写法统一为指令集的写法:单个 <c>*</c> 也是一级两位编号 (<c>P02**07*</c> → <c>P02**07**</c>),
        /// <c>N**0B01...</c> 表示一组条目 (→ <c>N**0B**</c>)。
        /// </summary>
        private static string NormalizePattern(string command)
        {
            var pattern = command.Trim();
            if (pattern.EndsWith("01...", StringComparison.Ordinal))
            {
                pattern = pattern.Substring(0, pattern.Length - 5) + "**";
            }

            return Regex.Replace(pattern, @"(?<!\*)\*(?!\*)", "**");
        }

        private static IEnumerable<string> RealHelpTableIds() => RealHelpTable().Select(e => e.Id);

        private static IReadOnlyList<HelpTableEntry> RealHelpTable()
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "imc300_1.8.11_h01_h02_help.bin"));
            var frames = new ZevisionFrameDecoder(new FrameOptions(checksumEnabled: true, lengthEnabled: true)).Push(bytes, 0, bytes.Length);
            return HelpTableParser.Parse(ZevisionResponseParser.Parse(frames[2].Payload)[0].Data!);
        }

        /// <summary>读取 xlsx 第一个工作表 (只用 System.IO.Compression 和 XML,不依赖 Excel 库)。</summary>
        private static List<List<string>> ReadSpreadsheet()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "U_commands.xlsx");
            using (var archive = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read))
            {
                var shared = new List<string>();
                var sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
                if (sharedEntry != null)
                {
                    using (var stream = sharedEntry.Open())
                    {
                        shared.AddRange(XDocument.Load(stream).Root!.Elements(Sheet + "si")
                            .Select(si => string.Concat(si.Descendants(Sheet + "t").Select(t => t.Value))));
                    }
                }

                using (var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open())
                {
                    return XDocument.Load(stream).Descendants(Sheet + "row")
                        .Select(row => row.Elements(Sheet + "c").Select(c => CellText(c, shared)).ToList())
                        .ToList();
                }
            }
        }

        private static string CellText(XElement cell, IReadOnlyList<string> shared)
        {
            var type = (string?)cell.Attribute("t");
            if (type == "inlineStr")
            {
                return string.Concat(cell.Descendants(Sheet + "t").Select(t => t.Value));
            }

            var value = cell.Element(Sheet + "v")?.Value ?? string.Empty;
            return type == "s" ? shared[int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)] : value;
        }
    }
}
