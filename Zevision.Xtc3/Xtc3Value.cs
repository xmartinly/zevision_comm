using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Zevision.Xtc3
{
    /// <summary>XTC/3 参数和响应的数据类型。多字节类型均为低字节在前。</summary>
    public enum Xtc3ValueType
    {
        /// <summary>1 字节无符号数 (<c>&lt;Byte&gt;</c>)。</summary>
        Byte,

        /// <summary>2 字节无符号数 (S12、S30 的位图)。</summary>
        Word,

        /// <summary>4 字节有符号整数 (<c>&lt;Integer&gt;</c>)。</summary>
        Integer,

        /// <summary>4 字节 IEEE 单精度浮点数 (<c>&lt;Float&gt;</c>)。</summary>
        Float,

        /// <summary>以 Null 结尾的 ASCII 字符串 (<c>&lt;String&gt;</c>)。</summary>
        String,

        /// <summary>定长的原始字节 (S35 的 8 字节)。</summary>
        Bytes,
    }

    /// <summary>
    /// 一个 XTC/3 参数或响应值。
    /// </summary>
    public readonly struct Xtc3Value : IEquatable<Xtc3Value>
    {
        private readonly int _integer;
        private readonly float _float;
        private readonly string? _text;
        private readonly byte[]? _bytes;

        private Xtc3Value(Xtc3ValueType type, int integer, float single, string? text, byte[]? bytes)
        {
            Type = type;
            _integer = integer;
            _float = single;
            _text = text;
            _bytes = bytes;
        }

        /// <summary>数据类型。</summary>
        public Xtc3ValueType Type { get; }

        /// <summary>整数值 (<see cref="Xtc3ValueType.Byte"/>、<see cref="Xtc3ValueType.Word"/>、<see cref="Xtc3ValueType.Integer"/>)。</summary>
        /// <exception cref="InvalidOperationException">不是整数类型。</exception>
        public int Integer => Type == Xtc3ValueType.Byte || Type == Xtc3ValueType.Word || Type == Xtc3ValueType.Integer
            ? _integer
            : throw new InvalidOperationException(Type + " is not an integer type.");

        /// <summary>浮点数值 (<see cref="Xtc3ValueType.Float"/>)。</summary>
        /// <exception cref="InvalidOperationException">不是浮点类型。</exception>
        public float Float => Type == Xtc3ValueType.Float ? _float : throw new InvalidOperationException(Type + " is not a float.");

        /// <summary>字符串值 (<see cref="Xtc3ValueType.String"/>)。</summary>
        /// <exception cref="InvalidOperationException">不是字符串。</exception>
        public string Text => Type == Xtc3ValueType.String ? _text ?? string.Empty : throw new InvalidOperationException(Type + " is not a string.");

        /// <summary>原始字节 (<see cref="Xtc3ValueType.Bytes"/>) 的副本。</summary>
        /// <exception cref="InvalidOperationException">不是原始字节。</exception>
        public byte[] Bytes => Type == Xtc3ValueType.Bytes ? (byte[])(_bytes ?? Array.Empty<byte>()).Clone() : throw new InvalidOperationException(Type + " is not raw bytes.");

        /// <summary>编码后的字节数。</summary>
        public int Size => Type switch
        {
            Xtc3ValueType.Byte => 1,
            Xtc3ValueType.Word => 2,
            Xtc3ValueType.Integer => 4,
            Xtc3ValueType.Float => 4,
            Xtc3ValueType.String => (_text ?? string.Empty).Length + 1,
            _ => (_bytes ?? Array.Empty<byte>()).Length,
        };

        /// <summary>数值 (整数或浮点数);字符串和原始字节没有数值。</summary>
        /// <exception cref="InvalidOperationException">不是数值类型。</exception>
        public double Number => Type == Xtc3ValueType.Float ? _float : Integer;

        /// <summary>1 字节值。</summary>
        public static Xtc3Value FromByte(byte value) => new Xtc3Value(Xtc3ValueType.Byte, value, 0, null, null);

        /// <summary>2 字节值。</summary>
        public static Xtc3Value FromWord(ushort value) => new Xtc3Value(Xtc3ValueType.Word, value, 0, null, null);

        /// <summary>4 字节整数。</summary>
        public static Xtc3Value FromInteger(int value) => new Xtc3Value(Xtc3ValueType.Integer, value, 0, null, null);

        /// <summary>4 字节浮点数。</summary>
        public static Xtc3Value FromFloat(float value) => new Xtc3Value(Xtc3ValueType.Float, 0, value, null, null);

        /// <summary>以 Null 结尾的 ASCII 字符串。</summary>
        /// <exception cref="ArgumentException">含有 Null 或非 ASCII 字符。</exception>
        public static Xtc3Value FromString(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Any(c => c == '\0' || c > 0x7F))
            {
                throw new ArgumentException("An XTC/3 string holds ASCII characters without Null.", nameof(value));
            }

            return new Xtc3Value(Xtc3ValueType.String, 0, 0, value, null);
        }

        /// <summary>定长的原始字节 (复制一份)。</summary>
        public static Xtc3Value FromBytes(byte[] value) =>
            new Xtc3Value(Xtc3ValueType.Bytes, 0, 0, null, (byte[])(value ?? throw new ArgumentNullException(nameof(value))).Clone());

        /// <summary>按类型和低字节在前的顺序写入。</summary>
        /// <param name="buffer">目标。</param>
        public void WriteTo(List<byte> buffer)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            switch (Type)
            {
                case Xtc3ValueType.Byte:
                    buffer.Add((byte)_integer);
                    break;
                case Xtc3ValueType.Word:
                    buffer.Add((byte)_integer);
                    buffer.Add((byte)(_integer >> 8));
                    break;
                case Xtc3ValueType.Integer:
                    buffer.AddRange(LittleEndian(BitConverter.GetBytes(_integer)));
                    break;
                case Xtc3ValueType.Float:
                    buffer.AddRange(LittleEndian(BitConverter.GetBytes(_float)));
                    break;
                case Xtc3ValueType.String:
                    buffer.AddRange(Encoding.ASCII.GetBytes(_text ?? string.Empty));
                    buffer.Add(0);
                    break;
                default:
                    buffer.AddRange(_bytes ?? Array.Empty<byte>());
                    break;
            }
        }

        /// <summary>
        /// 从 <paramref name="data"/> 的 <paramref name="offset"/> 处读取一个值。
        /// </summary>
        /// <param name="data">数据。</param>
        /// <param name="offset">起始位置;读取成功后移到值的后面。</param>
        /// <param name="type">类型。</param>
        /// <param name="length"><see cref="Xtc3ValueType.Bytes"/> 的字节数;其他类型不用。</param>
        /// <param name="value">读到的值。</param>
        /// <returns>字节不够 (字符串没有 Null 结尾) 时为 false,<paramref name="offset"/> 不变。</returns>
        public static bool TryRead(IReadOnlyList<byte> data, ref int offset, Xtc3ValueType type, int length, out Xtc3Value value)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            value = default;
            var remaining = data.Count - offset;
            switch (type)
            {
                case Xtc3ValueType.Byte:
                    if (remaining < 1)
                    {
                        return false;
                    }

                    value = FromByte(data[offset]);
                    offset += 1;
                    return true;

                case Xtc3ValueType.Word:
                    if (remaining < 2)
                    {
                        return false;
                    }

                    value = FromWord((ushort)(data[offset] | (data[offset + 1] << 8)));
                    offset += 2;
                    return true;

                case Xtc3ValueType.Integer:
                case Xtc3ValueType.Float:
                    if (remaining < 4)
                    {
                        return false;
                    }

                    var bytes = LittleEndian(new[] { data[offset], data[offset + 1], data[offset + 2], data[offset + 3] });
                    value = type == Xtc3ValueType.Integer ? FromInteger(BitConverter.ToInt32(bytes, 0)) : FromFloat(BitConverter.ToSingle(bytes, 0));
                    offset += 4;
                    return true;

                case Xtc3ValueType.String:
                    var end = offset;
                    while (end < data.Count && data[end] != 0)
                    {
                        end++;
                    }

                    if (end >= data.Count)
                    {
                        return false;
                    }

                    var chars = new char[end - offset];
                    for (var i = 0; i < chars.Length; i++)
                    {
                        // 非 ASCII 字节按 Latin-1 保留,回送 (E) 时原样返回。
                        chars[i] = (char)data[offset + i];
                    }

                    value = new Xtc3Value(Xtc3ValueType.String, 0, 0, new string(chars), null);
                    offset = end + 1;
                    return true;

                default:
                    if (remaining < length)
                    {
                        return false;
                    }

                    var raw = new byte[length];
                    for (var i = 0; i < length; i++)
                    {
                        raw[i] = data[offset + i];
                    }

                    value = new Xtc3Value(Xtc3ValueType.Bytes, 0, 0, null, raw);
                    offset += length;
                    return true;
            }
        }

        /// <inheritdoc/>
        public bool Equals(Xtc3Value other) =>
            Type == other.Type
            && _integer == other._integer
            && _float.Equals(other._float)
            && string.Equals(_text, other._text, StringComparison.Ordinal)
            && (_bytes ?? Array.Empty<byte>()).SequenceEqual(other._bytes ?? Array.Empty<byte>());

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Xtc3Value other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => ((int)Type * 397) ^ _integer ^ _float.GetHashCode() ^ (_text?.GetHashCode() ?? 0);

        /// <summary>相等。</summary>
        public static bool operator ==(Xtc3Value left, Xtc3Value right) => left.Equals(right);

        /// <summary>不相等。</summary>
        public static bool operator !=(Xtc3Value left, Xtc3Value right) => !left.Equals(right);

        /// <summary>调试和记录用的文本,如 <c>Int 5</c>、<c>Float 2.5</c>、<c>String "abc"</c>。</summary>
        public override string ToString() => Type switch
        {
            Xtc3ValueType.Byte => "Byte " + _integer.ToString(CultureInfo.InvariantCulture),
            Xtc3ValueType.Word => "Word 0x" + _integer.ToString("X4", CultureInfo.InvariantCulture),
            Xtc3ValueType.Integer => "Int " + _integer.ToString(CultureInfo.InvariantCulture),
            Xtc3ValueType.Float => "Float " + _float.ToString("R", CultureInfo.InvariantCulture),
            Xtc3ValueType.String => "String \"" + _text + "\"",
            _ => "Bytes " + BitConverter.ToString(_bytes ?? Array.Empty<byte>()),
        };

        private static byte[] LittleEndian(byte[] bytes)
        {
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }
    }
}
