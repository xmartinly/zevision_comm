using System.Collections.Generic;
using System.Threading.Tasks;

namespace Zevision.Core
{
    /// <summary>
    /// 保存通讯数据:把数据显示区的内容按显示的样子追加保存。只是记录用途,写入失败不影响通讯。
    /// </summary>
    public interface ICommunicationRecorder
    {
        /// <summary>保存位置 (显示给用户)。</summary>
        string Path { get; }

        /// <summary>追加一批行 (一次通讯显示的全部行)。不阻塞调用方,不抛出 I/O 异常。</summary>
        void Append(IReadOnlyList<string> lines);

        /// <summary>写完已排队的行后关闭。之后的 <see cref="Append"/> 被忽略。</summary>
        /// <returns>关闭后完成;不会失败。</returns>
        Task CloseAsync();
    }

    /// <summary>
    /// 为一次连接创建 <see cref="ICommunicationRecorder"/>。
    /// </summary>
    public interface ICommunicationRecorderFactory
    {
        /// <summary>创建新的记录 (如以当前时间命名的文件)。无法创建时返回失败,原因可直接显示。</summary>
        Result<ICommunicationRecorder> Create();
    }
}
