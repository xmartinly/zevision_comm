using System;
using System.Collections.Generic;
using Zevision.Core;
using Zevision.ViewModels.Common;

namespace Zevision.Tests
{
    /// <summary>
    /// 记录弹窗内容、按预设结果应答的对话框服务。
    /// </summary>
    internal sealed class FakeDialogService : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;

        public List<string> Confirms { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();

        /// <summary>"另存为"对话框返回的路径;null 表示用户取消。</summary>
        public string? SaveFilePath { get; set; }

        public string? ShowSaveFileDialog(string title, string filter, string initialDirectory, string defaultFileName) => SaveFilePath;

        /// <summary>打开文件对话框返回的路径;null 表示用户取消。</summary>
        public string? OpenFilePath { get; set; }

        /// <summary>打开过的修改配置窗口的视图模型。</summary>
        public List<object> Editors { get; } = new List<object>();

        public string? ShowOpenFileDialog(string title, string filter, string initialDirectory) => OpenFilePath;

        public bool Confirm(string title, string message)
        {
            Confirms.Add(message);
            return ConfirmResult;
        }

        public void ShowError(string title, string message) => Errors.Add(message);

        /// <summary>是/否/取消问题的回答 (默认取消)。</summary>
        public bool? YesNoCancelResult { get; set; }

        /// <summary>问过的是/否/取消问题。</summary>
        public List<string> Questions { get; } = new List<string>();

        public bool? AskYesNoCancel(string title, string message)
        {
            Questions.Add(message);
            return YesNoCancelResult;
        }

        public void ShowRecipeEditor(object viewModel) => Editors.Add(viewModel);

        /// <summary>打开工艺编辑窗口时执行的操作 (测试在其中编辑并保存);参数为窗口的视图模型。</summary>
        public Action<object>? OnProcessDesigner { get; set; }

        /// <summary>打开过的工艺编辑窗口的视图模型。</summary>
        public List<object> Designers { get; } = new List<object>();

        public void ShowProcessDesigner(object viewModel)
        {
            Designers.Add(viewModel);
            OnProcessDesigner?.Invoke(viewModel);
        }
    }

    /// <summary>只记录、不写文件的错误日志。</summary>
    internal sealed class FakeErrorLog : IErrorLog
    {
        public List<string> Messages { get; } = new List<string>();

        public void Write(string source, string message, System.Exception? exception = null) => Messages.Add(message);
    }
}
