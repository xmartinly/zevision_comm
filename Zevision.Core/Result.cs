using System;

namespace Zevision.Core
{
    /// <summary>
    /// 不带返回值的操作结果。用于预期内的失败(端口被占用、设备返回错误、超时等)。
    /// </summary>
    public readonly struct Result
    {
        private Result(bool isSuccess, string? error)
        {
            IsSuccess = isSuccess;
            Error = error;
        }

        /// <summary>是否成功。</summary>
        public bool IsSuccess { get; }

        /// <summary>失败原因;成功时为 null。</summary>
        public string? Error { get; }

        /// <summary>创建成功结果。</summary>
        public static Result Ok() => new Result(true, null);

        /// <summary>创建失败结果。</summary>
        public static Result Fail(string error) => new Result(false, error ?? throw new ArgumentNullException(nameof(error)));
    }

    /// <summary>
    /// 带返回值的操作结果。
    /// </summary>
    /// <typeparam name="T">成功时的返回值类型。</typeparam>
    public readonly struct Result<T>
    {
        private readonly T _value;

        private Result(bool isSuccess, T value, string? error)
        {
            IsSuccess = isSuccess;
            _value = value;
            Error = error;
        }

        /// <summary>是否成功。</summary>
        public bool IsSuccess { get; }

        /// <summary>失败原因;成功时为 null。</summary>
        public string? Error { get; }

        /// <summary>返回值。失败时访问会抛出 <see cref="InvalidOperationException"/>。</summary>
        public T Value => IsSuccess ? _value : throw new InvalidOperationException(Texts.FailedResultHasNoValue + Error);

        /// <summary>创建成功结果。</summary>
        public static Result<T> Ok(T value) => new Result<T>(true, value, null);

        /// <summary>创建失败结果。</summary>
        public static Result<T> Fail(string error) => new Result<T>(false, default!, error ?? throw new ArgumentNullException(nameof(error)));
    }
}
