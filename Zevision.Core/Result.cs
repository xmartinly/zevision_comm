using System;
using System.Diagnostics.CodeAnalysis;

namespace Zevision.Core
{
    /// <summary>
    /// 不带返回值的操作结果。用于预期内的失败(端口被占用、设备返回错误、超时等)。
    /// </summary>
    /// <remarks>失败时 <see cref="Error"/> 一定有值 (编译器的可空分析也知道);<c>default</c> 视为成功。</remarks>
    public readonly struct Result
    {
        private Result(string? error)
        {
            Error = error;
        }

        /// <summary>是否成功。</summary>
        [MemberNotNullWhen(false, nameof(Error))]
        public bool IsSuccess => Error == null;

        /// <summary>失败原因;成功时为 null。</summary>
        public string? Error { get; }

        /// <summary>创建成功结果。</summary>
        public static Result Ok() => default;

        /// <summary>创建失败结果。</summary>
        public static Result Fail(string error) => new Result(error ?? throw new ArgumentNullException(nameof(error)));
    }

    /// <summary>
    /// 带返回值的操作结果。
    /// </summary>
    /// <typeparam name="T">成功时的返回值类型。</typeparam>
    /// <remarks>失败时 <see cref="Error"/> 一定有值 (编译器的可空分析也知道);<c>default</c> 视为成功,值为 <c>default(T)</c>。</remarks>
    public readonly struct Result<T>
    {
        private readonly T _value;

        private Result(T value, string? error)
        {
            _value = value;
            Error = error;
        }

        /// <summary>是否成功。</summary>
        [MemberNotNullWhen(false, nameof(Error))]
        public bool IsSuccess => Error == null;

        /// <summary>失败原因;成功时为 null。</summary>
        public string? Error { get; }

        /// <summary>返回值。失败时访问会抛出 <see cref="InvalidOperationException"/>。</summary>
        public T Value => IsSuccess ? _value : throw new InvalidOperationException(Texts.FailedResultHasNoValue + Error);

        /// <summary>创建成功结果。</summary>
        public static Result<T> Ok(T value) => new Result<T>(value, null);

        /// <summary>创建失败结果。</summary>
        public static Result<T> Fail(string error) => new Result<T>(default!, error ?? throw new ArgumentNullException(nameof(error)));
    }
}
