using System;
using System.Collections.Generic;
using System.Linq;

namespace Zevision.Xtc3
{
    /// <summary>
    /// 命令的语法表:各命令的参数和成功响应的数据类型 (docs/XTC3.md 第 2~8 章)。XTC/3 的命令之间没有分隔符,
    /// 每条命令的长度由这里的语法决定。
    /// </summary>
    /// <remarks>
    /// 待确认的约定 (ZEV-FAR-01 的 8.2):Film 编号按 <see cref="Xtc3ValueType.Byte"/> 处理 (<see cref="FilmNumberType"/>);
    /// S12、S30 的 2 字节按低字节在前的 16 位值;S19 的 Layer end type 按 Byte。
    /// </remarks>
    public static class Xtc3Syntax
    {
        /// <summary>QF/UF 中 Film 编号的类型。手册未写明,按 Byte 处理 (与 S0 的 Timer Tick、R17 的输出号相同);确认后只改这里。</summary>
        public const Xtc3ValueType FilmNumberType = Xtc3ValueType.Byte;

        /// <summary>S35 (以太网参数) 的字节数:IP 地址 4 字节、子网掩码 4 字节。</summary>
        public const int EthernetParametersLength = 8;

        private static readonly Xtc3ValueType[] None = Array.Empty<Xtc3ValueType>();

        private static readonly Dictionary<int, Xtc3ValueType[]> StatusLayouts = new Dictionary<int, Xtc3ValueType[]>
        {
            [0] = new[]
            {
                Xtc3ValueType.Byte, Xtc3ValueType.Byte, Xtc3ValueType.Byte, Xtc3ValueType.Byte,
                Xtc3ValueType.Float, Xtc3ValueType.Float, Xtc3ValueType.Float, Xtc3ValueType.Float, Xtc3ValueType.Integer,
            },
            [1] = new[] { Xtc3ValueType.Float },
            [2] = new[] { Xtc3ValueType.Float },
            [3] = new[] { Xtc3ValueType.Float },
            [4] = new[] { Xtc3ValueType.Byte },
            [5] = new[] { Xtc3ValueType.Integer },
            [6] = new[] { Xtc3ValueType.Integer },
            [7] = new[] { Xtc3ValueType.Byte },
            [8] = new[] { Xtc3ValueType.Byte },
            [9] = new[] { Xtc3ValueType.Byte },
            [10] = new[] { Xtc3ValueType.Byte },
            [11] = new[] { Xtc3ValueType.Integer },
            [12] = new[] { Xtc3ValueType.Word },
            [13] = new[] { Xtc3ValueType.Integer },
            [14] = new[] { Xtc3ValueType.Byte },
            [15] = new[] { Xtc3ValueType.Byte },
            [16] = new[] { Xtc3ValueType.Byte },
            [17] = new[] { Xtc3ValueType.Byte },
            [18] = new[] { Xtc3ValueType.Byte },
            [19] = new[]
            {
                Xtc3ValueType.Integer, Xtc3ValueType.Integer, Xtc3ValueType.Float, Xtc3ValueType.Float, Xtc3ValueType.Integer,
                Xtc3ValueType.Float, Xtc3ValueType.Integer, Xtc3ValueType.Byte, Xtc3ValueType.Float, Xtc3ValueType.Float,
                Xtc3ValueType.Byte, Xtc3ValueType.Byte, Xtc3ValueType.Byte,
            },
            [20] = new[] { Xtc3ValueType.Byte },
            [21] = new[] { Xtc3ValueType.Byte },
            [30] = new[] { Xtc3ValueType.Word },
            [31] = new[] { Xtc3ValueType.Float },
            [32] = new[] { Xtc3ValueType.Byte },
            [33] = new[] { Xtc3ValueType.Integer },
            [34] = new[] { Xtc3ValueType.Float },
            [35] = new[] { Xtc3ValueType.Bytes },
        };

        /// <summary>XTC/3S 的远程动作 ID:R0~R22、R26~R28。</summary>
        public static bool IsRemoteId(int id) => (id >= 0 && id <= 22) || (id >= 26 && id <= 28);

        /// <summary>状态 ID:S0~S21、S30~S35。</summary>
        public static bool IsStatusId(int id) => StatusLayouts.ContainsKey(id);

        /// <summary>Hello 的 ID:1~3。</summary>
        public static bool IsHelloId(int id) => id >= 1 && id <= 3;

        /// <summary>远程动作的参数类型:R11 为 Float (功率),R17、R18 为 Byte (输出号),其他没有参数。</summary>
        public static Xtc3ValueType? RemoteParameter(int id) => id switch
        {
            11 => Xtc3ValueType.Float,
            17 => Xtc3ValueType.Byte,
            18 => Xtc3ValueType.Byte,
            _ => null,
        };

        /// <summary>Film 编号的值。</summary>
        public static Xtc3Value FilmNumber(int film) =>
            FilmNumberType == Xtc3ValueType.Byte ? Xtc3Value.FromByte(checked((byte)film)) : Xtc3Value.FromInteger(film);

        /// <summary>
        /// 命令成功时 ACK 之后的响应数据类型,按顺序。<see cref="Xtc3ValueType.Bytes"/> 为 <see cref="EthernetParametersLength"/> 字节。
        /// </summary>
        /// <param name="command">命令 (语法正确)。</param>
        /// <exception cref="ArgumentException">命令没有已知的响应格式。</exception>
        public static IReadOnlyList<Xtc3ValueType> ResponseLayout(Xtc3Command command)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var id = command.Id ?? -1;
            switch (command.Group)
            {
                case Xtc3Command.EchoGroup:
                    return new[] { Xtc3ValueType.String };
                case Xtc3Command.HelloGroup:
                    return id switch
                    {
                        1 => new[] { Xtc3ValueType.String },
                        2 => new[] { Xtc3ValueType.Integer, Xtc3ValueType.Integer, Xtc3ValueType.Integer, Xtc3ValueType.Integer },
                        3 => new[] { Xtc3ValueType.Float },
                        _ => throw Unknown(command),
                    };
                case Xtc3Command.StatusGroup:
                    return StatusLayouts.TryGetValue(id, out var layout) ? layout : throw Unknown(command);
                case Xtc3Command.RemoteGroup:
                case Xtc3Command.UpdateGroup:
                    return None;
                case Xtc3Command.QueryGroup when command.SubGroup == Xtc3Command.FilmSubGroup:
                    return id == Xtc3Parameters.AllId ? Xtc3Parameters.FilmTypes
                        : Xtc3Parameters.FindFilm(id) is { } film ? new[] { film.Type } : throw Unknown(command);
                case Xtc3Command.QueryGroup when command.SubGroup == Xtc3Command.GeneralSubGroup:
                    return id == Xtc3Parameters.AllId ? Enumerable.Repeat(Xtc3ValueType.Integer, Xtc3Parameters.General.Count).ToList()
                        : Xtc3Parameters.FindGeneral(id) != null ? new[] { Xtc3ValueType.Integer } : throw Unknown(command);
                default:
                    throw Unknown(command);
            }
        }

        private static ArgumentException Unknown(Xtc3Command command) =>
            new ArgumentException("The command " + command.Name + " has no known response layout.", nameof(command));
    }
}
