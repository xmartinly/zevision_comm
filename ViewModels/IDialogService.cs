namespace Zevision.ViewModels
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

        /// <summary>显示提示信息。</summary>
        void ShowInfo(string title, string message);

        /// <summary>显示错误信息。</summary>
        void ShowError(string title, string message);

        /// <summary>
        /// 以模态方式显示修改配置窗口,关闭后返回。
        /// 参数类型用 object,避免服务层依赖视图模型;实现方据此创建窗口并设为 DataContext。
        /// </summary>
        void ShowRecipeEditor(object viewModel);
    }
}
