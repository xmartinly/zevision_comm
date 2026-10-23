using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Zevision.Commands;
using Zevision.Core.Configuration;
using Zevision.Xtc3;

namespace Zevision.Bridge
{
    /// <summary>
    /// XTC/3S 的 Film n 在 IMC300 上的影子对象 (ZEV-FAR-01 的 5.2):材料 <c>Mkk</c>、膜系 <c>Fkk</c>、膜层 <c>Lkk</c>、工艺 <c>P02kk</c>,
    /// kk = 编号基数 + n (两位十六进制)。
    /// </summary>
    public sealed class ShadowFilm
    {
        /// <summary>默认的编号基数:Film 1~9 对应对象编号 B1~B9 (ZEV-FAR-01 的 Q-03)。</summary>
        public const int DefaultBase = 0xB0;

        /// <summary>对象编号的最大值 (指令集允许 01~FD)。</summary>
        public const int MaxObjectNumber = 0xFD;

        /// <summary>
        /// 创建 Film <paramref name="number"/> 的影子对象编号。
        /// </summary>
        /// <param name="number">Film 编号,1~9。</param>
        /// <param name="numberBase">编号基数,0~<see cref="MaxObjectNumber"/> − 9。</param>
        public ShadowFilm(int number, int numberBase)
        {
            if (number < 1 || number > Xtc3Protocol.FilmCount)
            {
                throw new ArgumentOutOfRangeException(nameof(number), number, "A film number is 1 to " + Xtc3Protocol.FilmCount + ".");
            }

            if (!IsValidBase(numberBase))
            {
                throw new ArgumentOutOfRangeException(nameof(numberBase), numberBase, "The object number base is 0 to " + (MaxObjectNumber - Xtc3Protocol.FilmCount) + ".");
            }

            Number = number;
            ObjectNumber = numberBase + number;
            Key = ObjectNumber.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>Film 编号,1~9。</summary>
        public int Number { get; }

        /// <summary>对象编号 (十进制),也是引用它的设置项的值 (<c>Fkk02</c>、<c>P01</c> 等)。</summary>
        public int ObjectNumber { get; }

        /// <summary>对象编号的两位十六进制,如 <c>B1</c>。</summary>
        public string Key { get; }

        /// <summary>影子材料,如 <c>MB1</c>。</summary>
        public string MaterialId => "M" + Key;

        /// <summary>影子膜系,如 <c>FB1</c>。</summary>
        public string FilmId => "F" + Key;

        /// <summary>影子膜层,如 <c>LB1</c>。</summary>
        public string LayerId => "L" + Key;

        /// <summary>影子工艺,如 <c>P02B1</c>。</summary>
        public string ProcessId => "P02" + Key;

        /// <summary>四个影子对象的名称:<c>XTC Film n</c>。</summary>
        public string Name => "XTC Film " + Number.ToString(CultureInfo.InvariantCulture);

        /// <summary>编号基数是否可用。</summary>
        public static bool IsValidBase(int numberBase) => numberBase >= 0 && numberBase + Xtc3Protocol.FilmCount <= MaxObjectNumber;

        /// <summary>对象下的设置项:<c>Item("FB1", 3, 1, 2)</c> → <c>FB1030102</c>。</summary>
        public static string Item(string objectId, params int[] path) => path.Aggregate(objectId, DeviceIds.Item);

        /// <inheritdoc/>
        public override string ToString() => "Film " + Number.ToString(CultureInfo.InvariantCulture) + " (" + Key + ")";
    }

    /// <summary>影子对象核对的结果。</summary>
    public enum ShadowStatus
    {
        /// <summary>尚未核对 (未连接设备)。</summary>
        Unknown,

        /// <summary>四个对象都在,名称和列表结构正确,可以使用。</summary>
        Ready,

        /// <summary>有对象不存在,可以创建 (已有的对象名称和结构正确)。</summary>
        Missing,

        /// <summary>编号已被其他对象占用 (名称不是中间层所设),不修改它。</summary>
        Occupied,

        /// <summary>对象的列表结构或引用不对 (例如在前面板上删除了预熔),中间层不修复。</summary>
        Invalid,
    }

    /// <summary>一个 Film 的影子对象核对结果和说明。</summary>
    /// <param name="Film">Film。</param>
    /// <param name="Status">结果。</param>
    /// <param name="Detail">不可用时的说明 (英文),如缺少的对象、不符的设置项;可用时为空。</param>
    /// <param name="MissingObjects">不存在的对象 ID (需要创建的)。</param>
    public sealed record ShadowCheck(ShadowFilm Film, ShadowStatus Status, string Detail, IReadOnlyList<string> MissingObjects);

    /// <summary>
    /// 影子对象的核对和创建内容 (纯函数,不访问设备)。
    /// </summary>
    internal static class ShadowObjects
    {
        // 影子对象应有的子列表条数 (ZEV-FAR-01 的 5.2 a)。
        internal const int PreConditions = 2;
        internal const int PostConditions = 1;

        /// <summary>
        /// 按设备配置 (<c>?</c> 或组帮助) 核对 Film 的四个影子对象。
        /// </summary>
        /// <param name="film">Film。</param>
        /// <param name="valueOf">设置项的值;设备上没有该项时为 null。</param>
        public static ShadowCheck Check(ShadowFilm film, Func<string, string?> valueOf)
        {
            var missing = new List<string>();
            var problems = new List<string>();
            foreach (var objectId in new[] { film.MaterialId, film.FilmId, film.LayerId, film.ProcessId })
            {
                var name = valueOf(DeviceIds.NameOf(objectId));
                if (name == null)
                {
                    missing.Add(objectId);
                    continue;
                }

                if (!string.Equals(name.Trim(), film.Name, StringComparison.Ordinal))
                {
                    return new ShadowCheck(film, ShadowStatus.Occupied, objectId + " is in use by \"" + name.Trim() + "\"", missing);
                }

                problems.AddRange(Structure(film, objectId)
                    .Where(item => !ParameterValue.AreEquivalent(item.Value, valueOf(item.Id)?.Trim()))
                    .Select(item => item.Id + " = " + (valueOf(item.Id)?.Trim() ?? "(missing)") + ", expected " + item.Value));
            }

            if (problems.Count > 0)
            {
                return new ShadowCheck(film, ShadowStatus.Invalid, string.Join("; ", problems), missing);
            }

            return missing.Count > 0
                ? new ShadowCheck(film, ShadowStatus.Missing, "missing " + string.Join(", ", missing), missing)
                : new ShadowCheck(film, ShadowStatus.Ready, string.Empty, missing);
        }

        /// <summary>
        /// 创建不存在的影子对象的配置 (<see cref="RestorePlanner"/> 按它新建对象、追加子列表、写入初值)。
        /// 只含 <see cref="ShadowCheck.MissingObjects"/> 中对象的设置项,已有的对象不写。
        /// </summary>
        /// <param name="checks">要创建的 Film 的核对结果 (<see cref="ShadowStatus.Missing"/>)。</param>
        /// <param name="snapshot">设备的当前配置。</param>
        public static InstrumentRecipe BuildRecipe(IEnumerable<ShadowCheck> checks, InstrumentSnapshot snapshot)
        {
            var entries = new List<RecipeEntry>();
            foreach (var check in checks.Where(c => c.Status == ShadowStatus.Missing))
            {
                foreach (var objectId in check.MissingObjects)
                {
                    entries.AddRange(InitialValues(check.Film, objectId).Select(item => new RecipeEntry
                    {
                        Id = item.Id,
                        Value = item.Value,
                        Description = ZevisionCommandSet.Find(item.Id)?.EnglishName ?? string.Empty,
                    }));
                }
            }

            return new InstrumentRecipe
            {
                DeviceName = snapshot.DeviceName,
                ModelNumber = snapshot.ModelNumber,
                FirmwareVersion = snapshot.FirmwareVersion,
                SerialNumber = snapshot.SerialNumber,
                SavedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                Entries = entries,
            };
        }

        /// <summary>对象应有的列表条数和引用 (核对用)。</summary>
        private static IEnumerable<(string Id, string Value)> Structure(ShadowFilm film, string objectId)
        {
            var number = film.ObjectNumber.ToString(CultureInfo.InvariantCulture);
            switch (objectId[0])
            {
                case 'F':
                    yield return (ShadowFilm.Item(objectId, 0x02), number);
                    yield return (DeviceIds.CountOf(ShadowFilm.Item(objectId, 0x03)), Count(PreConditions));
                    yield return (DeviceIds.CountOf(ShadowFilm.Item(objectId, 0x05)), Count(PostConditions));
                    yield return (DeviceIds.CountOf(ShadowFilm.Item(objectId, 0x07)), Count(1));
                    break;
                case 'L':
                    yield return (DeviceIds.CountOf(ShadowFilm.Item(objectId, 0x02)), Count(1));
                    yield return (ShadowFilm.Item(objectId, 0x02, 1, 1), number);
                    yield return (DeviceIds.CountOf(ShadowFilm.Item(objectId, 0x02, 1, 0x06)), Count(1));
                    break;
                case 'P':
                    yield return (DeviceIds.CountOf(ShadowFilm.Item(objectId, 0x07)), Count(1));
                    yield return (ShadowFilm.Item(objectId, 0x07, 1), number);
                    break;
            }
        }

        /// <summary>新建对象的设置项:名称、引用、子列表的条目 (由此追加列表项) 和 ZEV-FAR-01 表 6 规定的初值。</summary>
        private static IEnumerable<(string Id, string Value)> InitialValues(ShadowFilm film, string objectId)
        {
            var number = film.ObjectNumber.ToString(CultureInfo.InvariantCulture);
            yield return (DeviceIds.NameOf(objectId), film.Name);
            switch (objectId[0])
            {
                case 'M':
                    yield return (ShadowFilm.Item(objectId, 0x03), "1.000");
                    yield return (ShadowFilm.Item(objectId, 0x04), "1.000");
                    yield return (ShadowFilm.Item(objectId, 0x07), "100.00");
                    break;
                case 'F':
                    yield return (ShadowFilm.Item(objectId, 0x02), number);
                    for (var step = 1; step <= PreConditions; step++)
                    {
                        foreach (var item in Condition(ShadowFilm.Item(objectId, 0x03, step)))
                        {
                            yield return item;
                        }
                    }

                    foreach (var item in Condition(ShadowFilm.Item(objectId, 0x05, 1)))
                    {
                        yield return item;
                    }

                    yield return (ShadowFilm.Item(objectId, 0x07, 1, 1), "1");
                    yield return (ShadowFilm.Item(objectId, 0x07, 1, 3), "100.00");
                    yield return (ShadowFilm.Item(objectId, 0x08, 2), "100.00");
                    yield return (ShadowFilm.Item(objectId, 0x09, 1), "1");
                    yield return (ShadowFilm.Item(objectId, 0x09, 8), "0.00");
                    break;
                case 'L':
                    yield return (ShadowFilm.Item(objectId, 0x02, 1, 1), number);
                    yield return (ShadowFilm.Item(objectId, 0x02, 1, 0x06, 1, 1), "0.0000");
                    break;
                case 'P':
                    yield return (ShadowFilm.Item(objectId, 0x02), "1");
                    yield return (ShadowFilm.Item(objectId, 0x04), "0");
                    yield return (ShadowFilm.Item(objectId, 0x07, 1), number);
                    break;
            }
        }

        private static IEnumerable<(string Id, string Value)> Condition(string stepId)
        {
            yield return (DeviceIds.Item(stepId, 1), "0:00:00");
            yield return (DeviceIds.Item(stepId, 2), "0.00");
            yield return (DeviceIds.Item(stepId, 3), "0:00:00");
        }

        private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);
    }
}
