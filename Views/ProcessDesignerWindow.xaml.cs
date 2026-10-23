using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zevision.ViewModels.Common;

namespace Zevision.Views
{
    /// <summary>
    /// 工艺编辑窗口。代码隐藏处理关闭 (上传期间不能关闭,有未保存的修改时由视图模型确认;视图模型请求关闭时关闭)
    /// 和材料库搜索框的光标。
    /// </summary>
    public partial class ProcessDesignerWindow : Window
    {
        /// <summary>
        /// 创建窗口。
        /// </summary>
        public ProcessDesignerWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        /// <inheritdoc />
        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is IRequestClose viewModel && !viewModel.ConfirmClose())
            {
                e.Cancel = true;
            }

            base.OnClosing(e);
        }

        /// <inheritdoc />
        protected override void OnClosed(EventArgs e)
        {
            // 视图模型不再引用窗口;不清空 DataContext:解除绑定的过程中下拉框可能把 null 写回视图模型。
            Detach(DataContext);
            base.OnClosed(e);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Detach(e.OldValue);
            if (e.NewValue is IRequestClose viewModel)
            {
                viewModel.CloseRequested += OnCloseRequested;
            }
        }

        private void Detach(object? viewModel)
        {
            if (viewModel is IRequestClose closable)
            {
                closable.CloseRequested -= OnCloseRequested;
            }
        }

        private void OnCloseRequested(object? sender, EventArgs e) => Close();

        /// <summary>
        /// 材料库的下拉框:输入关键字时视图模型展开列表,而 ComboBox 展开时会全选文本框 (WPF 的行为),下一个字符就覆盖了已输入的内容。
        /// 不是用鼠标展开的 (单击箭头时保留全选,便于重新输入) 就取消全选,光标放在末尾。
        /// </summary>
        private void OnLibraryDropDownOpened(object sender, EventArgs e)
        {
            if (Mouse.LeftButton == MouseButtonState.Released
                && sender is ComboBox combo
                && combo.Template?.FindName("PART_EditableTextBox", combo) is TextBox box)
            {
                box.Select(box.Text.Length, 0);
            }
        }
    }
}
