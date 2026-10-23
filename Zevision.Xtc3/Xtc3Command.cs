using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Zevision.Xtc3
{
    /// <summary>
    /// 命令包中的一条命令:<c>&lt;Group&gt;(&lt;Sub-group&gt;)(&lt;Command ID&gt;)(&lt;Parameter&gt;…)</c>。
    /// 解析时发现的命令错误 (非法命令、非法 ID) 在 <see cref="ParseError"/> 中,这样的命令不执行,直接回答该错误码。
    /// </summary>
    public sealed class Xtc3Command
    {
        /// <summary>Echo。</summary>
        public const char EchoGroup = 'E';

        /// <summary>Hello。</summary>
        public const char HelloGroup = 'H';

        /// <summary>Query。</summary>
        public const char QueryGroup = 'Q';

        /// <summary>Remote action。</summary>
        public const char RemoteGroup = 'R';

        /// <summary>Status。</summary>
        public const char StatusGroup = 'S';

        /// <summary>Update。</summary>
        public const char UpdateGroup = 'U';

        /// <summary>Q/U 的子组:Film 参数。</summary>
        public const char FilmSubGroup = 'F';

        /// <summary>Q/U 的子组:通用参数。</summary>
        public const char GeneralSubGroup = 'G';

        /// <summary>Q/U 的子组:块。</summary>
        public const char BlockSubGroup = 'B';

        /// <summary>
        /// 创建命令。
        /// </summary>
        /// <param name="group">命令组字母。</param>
        /// <param name="subGroup">子组字母 (只有 Q、U 有)。</param>
        /// <param name="id">Command ID (E 没有)。</param>
        /// <param name="parameters">参数。</param>
        /// <param name="parseError">解析时发现的命令错误。</param>
        public Xtc3Command(char group, char? subGroup, int? id, IReadOnlyList<Xtc3Value>? parameters = null, Xtc3ErrorCode? parseError = null)
        {
            Group = group;
            SubGroup = subGroup;
            Id = id;
            Parameters = parameters ?? Array.Empty<Xtc3Value>();
            ParseError = parseError;
        }

        /// <summary>命令组字母 (E、H、Q、R、S、U;不认识的命令为收到的字节)。</summary>
        public char Group { get; }

        /// <summary>子组字母 (Q、U 的 F、G 等);其他命令为 null。</summary>
        public char? SubGroup { get; }

        /// <summary>Command ID;E 和缺少 ID 的命令为 null。</summary>
        public int? Id { get; }

        /// <summary>参数,按顺序。QF/UF 的第一个参数是 Film 编号。</summary>
        public IReadOnlyList<Xtc3Value> Parameters { get; }

        /// <summary>解析时发现的命令错误;为 null 时命令语法正确,由执行方检查参数值。</summary>
        public Xtc3ErrorCode? ParseError { get; }

        /// <summary>命令名,如 <c>QF8</c>、<c>S0</c>、<c>R11</c>、<c>E</c>。</summary>
        public string Name => Group + (SubGroup?.ToString() ?? string.Empty) + (Id?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        /// <summary>QF/UF 的 Film 编号 (第一个参数);其他命令为 null。</summary>
        public int? FilmNumber => SubGroup == FilmSubGroup && (Group == QueryGroup || Group == UpdateGroup) && Parameters.Count > 0
            ? Parameters[0].Integer
            : (int?)null;

        /// <summary>Echo:<c>E &lt;String&gt;</c>。</summary>
        public static Xtc3Command Echo(string text) => new Xtc3Command(EchoGroup, null, null, new[] { Xtc3Value.FromString(text) });

        /// <summary>Hello:<c>H &lt;ID&gt;</c>,ID 1~3。</summary>
        public static Xtc3Command Hello(int id) => new Xtc3Command(HelloGroup, null, id);

        /// <summary>状态:<c>S &lt;ID&gt;</c> (S0 用 <see cref="ProcessInformation"/>)。</summary>
        public static Xtc3Command Status(int id) => new Xtc3Command(StatusGroup, null, id);

        /// <summary>S0:指定 tick 的过程信息。</summary>
        public static Xtc3Command ProcessInformation(byte tick) => new Xtc3Command(StatusGroup, null, 0, new[] { Xtc3Value.FromByte(tick) });

        /// <summary>远程动作:<c>R &lt;ID&gt; [&lt;Value&gt;]</c> (R11 为 Float,R17、R18 为 Byte)。</summary>
        public static Xtc3Command Remote(int id, Xtc3Value? value = null) =>
            new Xtc3Command(RemoteGroup, null, id, value.HasValue ? new[] { value.Value } : null);

        /// <summary>读取 Film 参数:<c>QF &lt;ID&gt; &lt;Film&gt;</c>。</summary>
        public static Xtc3Command QueryFilm(int id, int film) =>
            new Xtc3Command(QueryGroup, FilmSubGroup, id, new[] { Xtc3Syntax.FilmNumber(film) });

        /// <summary>写入一个 Film 参数:<c>UF &lt;ID&gt; &lt;Film&gt; &lt;Value&gt;</c>。</summary>
        public static Xtc3Command UpdateFilm(int id, int film, Xtc3Value value) =>
            new Xtc3Command(UpdateGroup, FilmSubGroup, id, new[] { Xtc3Syntax.FilmNumber(film), value });

        /// <summary>写入全部 Film 参数:<c>UF255 &lt;Film&gt; &lt;List&gt;</c>,值按 ID 顺序。</summary>
        public static Xtc3Command UpdateFilmAll(int film, IEnumerable<Xtc3Value> values) =>
            new Xtc3Command(UpdateGroup, FilmSubGroup, Xtc3Parameters.AllId, new[] { Xtc3Syntax.FilmNumber(film) }.Concat(values).ToList());

        /// <summary>读取通用参数:<c>QG &lt;ID&gt;</c>。</summary>
        public static Xtc3Command QueryGeneral(int id) => new Xtc3Command(QueryGroup, GeneralSubGroup, id);

        /// <summary>写入一个通用参数:<c>UG &lt;ID&gt; &lt;Integer&gt;</c>。</summary>
        public static Xtc3Command UpdateGeneral(int id, int value) =>
            new Xtc3Command(UpdateGroup, GeneralSubGroup, id, new[] { Xtc3Value.FromInteger(value) });

        /// <summary>写入全部通用参数:<c>UG255 &lt;List&gt;</c>,值按 ID 顺序。</summary>
        public static Xtc3Command UpdateGeneralAll(IEnumerable<int> values) =>
            new Xtc3Command(UpdateGroup, GeneralSubGroup, Xtc3Parameters.AllId, values.Select(Xtc3Value.FromInteger).ToList());

        /// <summary>按命令包的格式写入 (组、子组、ID、参数)。</summary>
        /// <param name="buffer">目标。</param>
        public void WriteTo(List<byte> buffer)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.Add((byte)Group);
            if (SubGroup.HasValue)
            {
                buffer.Add((byte)SubGroup.Value);
            }

            if (Id.HasValue)
            {
                buffer.Add((byte)Id.Value);
            }

            foreach (var parameter in Parameters)
            {
                parameter.WriteTo(buffer);
            }
        }

        /// <summary>记录用的文本,如 <c>UF8 [Byte 1, Float 2.5]</c>、<c>QF99 → C</c>。</summary>
        public override string ToString() =>
            Name
            + (Parameters.Count > 0 ? " [" + string.Join(", ", Parameters) + "]" : string.Empty)
            + (ParseError.HasValue ? " → " + (char)ParseError.Value : string.Empty);
    }
}
