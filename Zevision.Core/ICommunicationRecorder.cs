using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Zevision.Core
{
    /// <summary>
    /// 保存通讯数据:把数据显示区的内容按显示的样子追加保存。只是记录用途,写入失败不影响通讯。
    /// 实现可以先缓存在内存中,积累到一定大小、关闭或释放时再写入。
    /// </summary>
    /// <remarks>
    /// <see cref="IDisposable.Dispose"/> 在调用线程上写完缓存和已排队的行后关闭,用于程序退出 (后台写入可能来不及完成);
    /// 不抛出 I/O 异常,可以在 <see cref="CloseAsync"/> 之后调用。
    /// </remarks>
    public interface ICommunicationRecorder : IDisposable
    {
        /// <summary>当前保存位置 (显示给用户)。实现可以在记录过程中换到新文件 (如文件超过大小上限)。</summary>
        string Path { get; }

        /// <summary>追加一批行 (一次通讯显示的全部行)。不阻塞调用方,不抛出 I/O 异常。</summary>
        void Append(IReadOnlyList<string> lines);

        /// <summary>在后台写完缓存和已排队的行后关闭 (断开连接时)。之后的 <see cref="Append"/> 被忽略。</summary>
        /// <returns>关闭后完成;不会失败。</returns>
        Task CloseAsync();
    }

    /// <summary>
    /// 为一次连接创建 <see cref="ICommunicationRecorder"/>。
    /// </summary>
    public interface ICommunicationRecorderFactory
    {
        /// <summary>创建新的记录 (如以设备序列号和当前时间命名的文件)。无法创建时返回失败,原因可直接显示。</summary>
        /// <param name="deviceSerialNumber">设备的系统序列号 (G06,握手时读取);未知 (未启用握手、设备未回答) 时为 null。</param>
        Result<ICommunicationRecorder> Create(string? deviceSerialNumber);
    }
}
