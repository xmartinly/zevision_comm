namespace Zevision.ViewModels.Common
{
    /// <summary>
    /// 文件对话框和消息框。视图模型通过它与用户交互,不直接依赖 WPF 窗口,便于测试。
    /// </summary>
    public interface IDialogService
    {
        /// <summary>显示"另存为"对话框;取消时返回 null。</summary>
        string? ShowSaveFileDialog(string title, string filter, string initialDirectory, string defaultFileName);

        /// <summary>显示"打开"对话框;取消时返回 null。</summary>
        string? ShowOpenFileDialog(string title, string filter, string initialDirectory);

        /// <summary>显示带"确定/取消"的确认框,用户确定时返回 true。</summary>
        bool Confirm(string title, string message);

        /// <summary>显示错误信息。</summary>
        void ShowError(string title, string message);

        /// <summary>是/否/取消三选一的问题。</summary>
        /// <returns>是为 true,否为 false,取消 (或关闭) 为 null。</returns>
        bool? AskYesNoCancel(string title, string message);

        /// <summary>
        /// 以模态方式显示修改配置窗口,关闭后返回。
        /// 参数类型用 object,避免服务层依赖视图模型;实现方据此创建窗口并设为 DataContext。
        /// </summary>
        void ShowRecipeEditor(object viewModel);

        /// <summary>以模态方式显示工艺编辑窗口,关闭后返回。参数是窗口的视图模型 (同 <see cref="ShowRecipeEditor"/>)。</summary>
        void ShowProcessDesigner(object viewModel);
    }
}
