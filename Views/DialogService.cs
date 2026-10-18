using System.Windows;
using Microsoft.Win32;
using Zevision.ViewModels;

namespace Zevision.Views
{
    /// <summary>
    /// <see cref="IDialogService"/> 的 WPF 实现。对话框以主窗口为所有者,保证模态并居中。
    /// </summary>
    public sealed class DialogService : IDialogService
    {
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
        public void ShowInfo(string title, string message)
        {
            Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);
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

            window.ShowDialog();
        }

        private static Window? Owner => Application.Current?.MainWindow;

        private static bool ShowDialog(CommonDialog dialog)
        {
            var owner = Owner;
            return (owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
        }

        private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
        {
            var owner = Owner;
            return owner != null
                ? MessageBox.Show(owner, message, title, buttons, image, defaultResult)
                : MessageBox.Show(message, title, buttons, image, defaultResult);
        }
    }
}
