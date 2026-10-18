using System.Windows;
using Microsoft.Win32;
using Zevision.ViewModels;

namespace Zevision.Views
{
    /// <summary>
    /// <see cref="IDialogService"/> 的 WPF 实现。对话框以当前的顶层窗口为所有者 (主窗口,修改配置窗口打开时是它),保证模态并居中。
    /// </summary>
    public sealed class DialogService : IDialogService
    {
        /// <summary>正在显示的修改配置窗口 (模态);没有时为 null。</summary>
        private Window? _editor;

        /// <inheritdoc />
        public string? ShowSaveFileDialog(string title, string filter, string initialDirectory, string defaultFileName)
        {
            var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                InitialDirectory = initialDirectory,
                FileName = defaultFileName,
                AddExtension = true,
                OverwritePrompt = true,
            };

            return ShowDialog(dialog) ? dialog.FileName : null;
        }

        /// <inheritdoc />
        public string? ShowOpenFileDialog(string title, string filter, string initialDirectory)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                InitialDirectory = initialDirectory,
                CheckFileExists = true,
                Multiselect = false,
            };

            return ShowDialog(dialog) ? dialog.FileName : null;
        }

        /// <inheritdoc />
        public bool Confirm(string title, string message)
        {
            return Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;
        }

        /// <inheritdoc />
        public void ShowError(string title, string message)
        {
            Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);
        }

        /// <inheritdoc />
        public void ShowRecipeEditor(object viewModel)
        {
            var window = new RecipeEditorWindow { DataContext = viewModel };
            var owner = Owner;
            if (owner != null && owner.IsLoaded)
            {
                window.Owner = owner;
            }

            _editor = window;
            try
            {
                window.ShowDialog();
            }
            finally
            {
                _editor = null;
            }
        }

        /// <summary>
        /// 对话框的所有者。修改配置窗口打开时必须是它:提示框只禁用自己的所有者,属于主窗口时修改配置窗口仍可操作,
        /// 点一下就把提示框挡在后面,确认框打开时还能修改值 (不再经过检查就写入) 或再点其他按钮 (两个设备操作同时进行)。
        /// </summary>
        private Window? Owner => _editor ?? Application.Current?.MainWindow;

        private bool ShowDialog(CommonDialog dialog)
        {
            var owner = Owner;
            return (owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
        }

        private MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
        {
            var owner = Owner;
            return owner != null
                ? MessageBox.Show(owner, message, title, buttons, image, defaultResult)
                : MessageBox.Show(message, title, buttons, image, defaultResult);
        }
    }
}
