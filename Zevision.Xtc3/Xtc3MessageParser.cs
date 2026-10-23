using System;
using System.Collections.Generic;

namespace Zevision.Xtc3
{
    /// <summary>
    /// 命令包消息的解析结果:命令,或包错误 (此时不执行任何命令,只回答包错误)。
    /// </summary>
    public sealed class Xtc3ParsedMessage
    {
        internal Xtc3ParsedMessage(IReadOnlyList<Xtc3Command> commands, Xtc3PacketError? error)
        {
            Commands = commands;
            Error = error;
        }

        /// <summary>命令,按包内顺序;有包错误时为空。</summary>
        public IReadOnlyList<Xtc3Command> Commands { get; }

        /// <summary>包错误:格式错误 (缺少参数字节) 或命令过多。</summary>
        public Xtc3PacketError? Error { get; }
    }

    /// <summary>
    /// 按 <see cref="Xtc3Syntax"/> 把命令包的消息拆成命令。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>参数字节不够 (消息在命令中间结束):包错误 <see cref="Xtc3PacketError.IllegalFormat"/>。</item>
    /// <item>超过 <see cref="Xtc3Protocol.MaxCommands"/> 条命令:包错误 <see cref="Xtc3PacketError.TooManyCommands"/>。</item>
    /// <item>H、S、R、QF/UF、QG/UG 的 ID 不存在:该命令为 <see cref="Xtc3ErrorCode.IllegalId"/>,按语法跳过它的参数
    /// (UF、UG 的值都是 4 字节),继续解析。</item>
    /// <item>XTC/3M 专用命令 (QN、QI、QO、QT、QP) 和块读取 QB:<see cref="Xtc3ErrorCode.IllegalCommand"/>,按手册的格式跳过参数。</item>
    /// <item>不认识的命令组或子组、UN、UI、UO、UT、UP、UB:<see cref="Xtc3ErrorCode.IllegalCommand"/>,无法确定下一条命令的起点,
    /// 消息的其余部分都属于这条命令 (XTC/3S 的实际应答待确认,ZEV-FAR-01 的 8.2 e)。</item>
    /// </list>
    /// </remarks>
    public static class Xtc3MessageParser
    {
        private const int ValueSize = 4;

        /// <summary>解析一个命令包的消息。</summary>
        /// <param name="message">消息 (不含长度和校验和)。</param>
        public static Xtc3ParsedMessage Parse(IReadOnlyList<byte> message)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            var commands = new List<Xtc3Command>();
            var position = 0;
            while (position < message.Count)
            {
                if (commands.Count == Xtc3Protocol.MaxCommands)
                {
                    return Fail(Xtc3PacketError.TooManyCommands);
                }

                var command = ParseCommand(message, ref position);
                if (command == null)
                {
                    return Fail(Xtc3PacketError.IllegalFormat);
                }

                commands.Add(command);
            }

            return new Xtc3ParsedMessage(commands, null);
        }

        private static Xtc3ParsedMessage Fail(Xtc3PacketError error) => new Xtc3ParsedMessage(Array.Empty<Xtc3Command>(), error);

        /// <summary>解析一条命令;字节不够时为 null。</summary>
        private static Xtc3Command? ParseCommand(IReadOnlyList<byte> message, ref int position)
        {
            var group = (char)message[position++];
            switch (group)
            {
                case Xtc3Command.EchoGroup:
                    return Xtc3Value.TryRead(message, ref position, Xtc3ValueType.String, 0, out var text)
                        ? new Xtc3Command(group, null, null, new[] { text })
                        : null;

                case Xtc3Command.HelloGroup:
                    return TryReadId(message, ref position, out var hello)
                        ? new Xtc3Command(group, null, hello, null, Xtc3Syntax.IsHelloId(hello) ? (Xtc3ErrorCode?)null : Xtc3ErrorCode.IllegalId)
                        : null;

                case Xtc3Command.StatusGroup:
                    if (!TryReadId(message, ref position, out var status))
                    {
                        return null;
                    }

                    if (!Xtc3Syntax.IsStatusId(status))
                    {
                        return new Xtc3Command(group, null, status, null, Xtc3ErrorCode.IllegalId);
                    }

                    if (status != 0)
                    {
                        return new Xtc3Command(group, null, status);
                    }

                    return Xtc3Value.TryRead(message, ref position, Xtc3ValueType.Byte, 0, out var tick)
                        ? new Xtc3Command(group, null, status, new[] { tick })
                        : null;

                case Xtc3Command.RemoteGroup:
                    if (!TryReadId(message, ref position, out var remote))
                    {
                        return null;
                    }

                    if (!Xtc3Syntax.IsRemoteId(remote))
                    {
                        return new Xtc3Command(group, null, remote, null, Xtc3ErrorCode.IllegalId);
                    }

                    if (!(Xtc3Syntax.RemoteParameter(remote) is { } parameterType))
                    {
                        return new Xtc3Command(group, null, remote);
                    }

                    return Xtc3Value.TryRead(message, ref position, parameterType, 0, out var parameter)
                        ? new Xtc3Command(group, null, remote, new[] { parameter })
                        : null;

                case Xtc3Command.QueryGroup:
                case Xtc3Command.UpdateGroup:
                    return ParseParameterCommand(message, ref position, group);

                default:
                    return RestIsIllegal(message, ref position, group, null);
            }
        }

        private static Xtc3Command? ParseParameterCommand(IReadOnlyList<byte> message, ref int position, char group)
        {
            if (position >= message.Count)
            {
                return null;
            }

            var subGroup = (char)message[position++];
            var isUpdate = group == Xtc3Command.UpdateGroup;
            switch (subGroup)
            {
                case Xtc3Command.FilmSubGroup:
                    if (!TryReadId(message, ref position, out var filmId)
                        || !Xtc3Value.TryRead(message, ref position, Xtc3Syntax.FilmNumberType, 0, out var film))
                    {
                        return null;
                    }

                    var known = filmId == Xtc3Parameters.AllId || Xtc3Parameters.FindFilm(filmId) != null;
                    if (!isUpdate)
                    {
                        return new Xtc3Command(group, subGroup, filmId, new[] { film }, known ? (Xtc3ErrorCode?)null : Xtc3ErrorCode.IllegalId);
                    }

                    var filmValues = new List<Xtc3Value> { film };
                    if (!known)
                    {
                        return Skip(message, ref position, ValueSize) ? new Xtc3Command(group, subGroup, filmId, filmValues, Xtc3ErrorCode.IllegalId) : null;
                    }

                    var filmTypes = filmId == Xtc3Parameters.AllId ? Xtc3Parameters.FilmTypes : new[] { Xtc3Parameters.Film[filmId].Type };
                    return TryReadValues(message, ref position, filmTypes, filmValues) ? new Xtc3Command(group, subGroup, filmId, filmValues) : null;

                case Xtc3Command.GeneralSubGroup:
                    if (!TryReadId(message, ref position, out var generalId))
                    {
                        return null;
                    }

                    var isGeneral = generalId == Xtc3Parameters.AllId || Xtc3Parameters.FindGeneral(generalId) != null;
                    if (!isUpdate)
                    {
                        return new Xtc3Command(group, subGroup, generalId, null, isGeneral ? (Xtc3ErrorCode?)null : Xtc3ErrorCode.IllegalId);
                    }

                    if (!isGeneral)
                    {
                        return Skip(message, ref position, ValueSize) ? new Xtc3Command(group, subGroup, generalId, null, Xtc3ErrorCode.IllegalId) : null;
                    }

                    var count = generalId == Xtc3Parameters.AllId ? Xtc3Parameters.General.Count : 1;
                    var generalValues = new List<Xtc3Value>();
                    var generalTypes = new Xtc3ValueType[count];
                    for (var i = 0; i < count; i++)
                    {
                        generalTypes[i] = Xtc3ValueType.Integer;
                    }

                    return TryReadValues(message, ref position, generalTypes, generalValues) ? new Xtc3Command(group, subGroup, generalId, generalValues) : null;

                case Xtc3Command.BlockSubGroup when !isUpdate:
                    // QB <ID>:块结构未确认,回答 A (ZEV-FAR-01 的 Q-06)。
                    return TryReadId(message, ref position, out var block) ? new Xtc3Command(group, subGroup, block, null, Xtc3ErrorCode.IllegalCommand) : null;

                case 'N' when !isUpdate:
                case 'I' when !isUpdate:
                case 'O' when !isUpdate:
                case 'T' when !isUpdate:
                    // XTC/3M 专用:QN <Film>、QI/QO/QT <编号>。
                    return TryReadId(message, ref position, out var number) ? new Xtc3Command(group, subGroup, number, null, Xtc3ErrorCode.IllegalCommand) : null;

                case 'P' when !isUpdate:
                    // XTC/3M 专用:QP <ID> <Process> [<2 字节层号> (ID 3)]。
                    if (!TryReadId(message, ref position, out var processId) || !Skip(message, ref position, processId == 3 ? 3 : 1))
                    {
                        return null;
                    }

                    return new Xtc3Command(group, subGroup, processId, null, Xtc3ErrorCode.IllegalCommand);

                default:
                    return RestIsIllegal(message, ref position, group, subGroup);
            }
        }

        /// <summary>不认识的命令:消息的其余部分都算作这条命令,回答 A。</summary>
        private static Xtc3Command RestIsIllegal(IReadOnlyList<byte> message, ref int position, char group, char? subGroup)
        {
            position = message.Count;
            return new Xtc3Command(group, subGroup, null, null, Xtc3ErrorCode.IllegalCommand);
        }

        private static bool TryReadId(IReadOnlyList<byte> message, ref int position, out int id)
        {
            id = -1;
            if (position >= message.Count)
            {
                return false;
            }

            id = message[position++];
            return true;
        }

        private static bool TryReadValues(IReadOnlyList<byte> message, ref int position, IReadOnlyList<Xtc3ValueType> types, List<Xtc3Value> values)
        {
            foreach (var type in types)
            {
                if (!Xtc3Value.TryRead(message, ref position, type, 0, out var value))
                {
                    return false;
                }

                values.Add(value);
            }

            return true;
        }

        private static bool Skip(IReadOnlyList<byte> message, ref int position, int count)
        {
            if (message.Count - position < count)
            {
                return false;
            }

            position += count;
            return true;
        }
    }
}
