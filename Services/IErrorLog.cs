using System;

namespace Zevision.Services
{
    /// <summary>
    /// 运行时错误日志。实现必须线程安全,且写入失败时不能抛出异常。
    /// </summary>
    public interface IErrorLog
    {
        /// <summary>
        /// 记录一条错误。
        /// </summary>
        /// <param name="source">来源,如"通讯"、"未处理异常"。</param>
        /// <param name="message">错误说明。</param>
        /// <param name="exception">相关异常;记录其类型、消息和完整堆栈。</param>
        void Write(string source, string message, Exception? exception = null);
    }
}
