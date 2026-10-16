using System;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 由视图模型决定何时关闭窗口 (视图订阅事件并关闭自身),以及能否关闭 (例如操作进行中)。
    /// </summary>
    public interface IRequestClose
    {
        /// <summary>视图模型请求关闭窗口。</summary>
        event EventHandler? CloseRequested;

        /// <summary>当前是否允许关闭窗口 (不询问用户,如操作进行中时为 false)。</summary>
        bool CanClose { get; }

        /// <summary>
        /// 窗口关闭前调用:可以询问用户 (如是否放弃未保存的修改)。返回 false 时取消关闭。
        /// </summary>
        bool ConfirmClose();
    }
}
