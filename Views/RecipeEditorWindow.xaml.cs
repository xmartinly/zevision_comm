using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Zevision.ViewModels.Common;
using Zevision.ViewModels.Editor;

namespace Zevision.Views
{
    /// <summary>
    /// 修改配置窗口。代码隐藏只负责纯界面的行为:按视图模型的要求关闭 (关闭前由视图模型确认),
    /// Ctrl+F 定位筛选框,点击值或勾选后把焦点放到输入框,以及每个类别页各自记住滚动位置 (重新生成页面后恢复)。
    /// </summary>
    public partial class RecipeEditorWindow : Window
    {
        /// <summary>各类别页 (按标题) 的滚动位置。</summary>
        private readonly Dictionary<string, double> _scrollOffsets = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>页面重新生成或切换类别页后、恢复滚动位置之前:期间的滚动变化不记录 (内容重建时会先回到顶部)。</summary>
        private bool _restoringScroll;

        /// <summary>
        /// 创建窗口。
        /// </summary>
        public RecipeEditorWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        /// <inheritdoc />
        protected override void OnClosing(CancelEventArgs e)
        {
            // 写入设备期间不能关闭 (请用"取消写入");有未保存的修改时由视图模型确认是否放弃。
            if (DataContext is IRequestClose viewModel && !viewModel.ConfirmClose())
            {
                e.Cancel = true;
            }

            base.OnClosing(e);
        }

        /// <inheritdoc />
        protected override void OnClosed(EventArgs e)
        {
            // 视图模型不再引用窗口:即使视图模型关闭后仍被别处引用,窗口 (整个可视树) 也能回收。
            // 不清空 DataContext:解除绑定的过程中下拉框可能把 null 写回视图模型。
            Detach(DataContext);
            base.OnClosed(e);
        }

        private string? CurrentTabTitle => (SettingsTabs.SelectedItem as RecipeTabViewModel)?.Title;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Detach(e.OldValue);
            if (e.NewValue is IRequestClose newViewModel)
            {
                newViewModel.CloseRequested += OnCloseRequested;
            }

            if (e.NewValue is INotifyPropertyChanged newNotifier)
            {
                newNotifier.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void Detach(object? viewModel)
        {
            if (viewModel is IRequestClose closable)
            {
                closable.CloseRequested -= OnCloseRequested;
            }

            if (viewModel is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged -= OnViewModelPropertyChanged;
            }
        }

        private void OnCloseRequested(object? sender, EventArgs e) => Close();

        /// <summary>新建、删除、保存后页面重新生成:恢复当前类别页的滚动位置,而不是回到顶部。</summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RecipeEditorViewModel.Tabs))
            {
                ScheduleScrollRestore();
            }
        }

        /// <summary>切换类别页:恢复该页上次的滚动位置。</summary>
        private void OnSettingsTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, SettingsTabs))
            {
                ScheduleScrollRestore();
            }
        }

        private void OnSettingsScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 只记录类别页内容的滚动 (下拉框弹出列表等也有 ScrollViewer)。
            if (!_restoringScroll && e.OriginalSource is ScrollViewer { Name: "GroupScroll" } viewer && CurrentTabTitle is string title)
            {
                _scrollOffsets[title] = viewer.VerticalOffset;
            }
        }

        private void ScheduleScrollRestore()
        {
            _restoringScroll = true;

            // Loaded 优先级在布局之后执行:新的分组已经生成,滚动范围已经确定。
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _restoringScroll = false;
                var viewer = FindDescendant<ScrollViewer>(SettingsTabs, v => v.Name == "GroupScroll");
                if (viewer != null && CurrentTabTitle is string title)
                {
                    viewer.ScrollToVerticalOffset(_scrollOffsets.TryGetValue(title, out var offset) ? offset : 0);
                }
            }));
        }

        /// <summary>Ctrl+F:定位到筛选框并全选其中的文字。</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                FilterBox.Focus();
                FilterBox.SelectAll();
                e.Handled = true;
            }
        }

        /// <summary>点击未勾选项的值:勾选它并进入编辑。</summary>
        private void OnValueClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: RecipeItemViewModel item } element && !item.IsChecked)
            {
                // 先找到这一行:勾选后值的显示控件会被输入控件替换,脱离可视树。
                var row = FindRow(element);
                item.IsChecked = true;
                FocusEditor(row);
                e.Handled = true;
            }
        }

        /// <summary>用复选框勾选后:焦点放到这一行的输入框。</summary>
        private void OnItemCheckBoxClick(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox { IsChecked: true } box)
            {
                FocusEditor(FindRow(box));
            }
        }

        /// <summary>控件所在的配置项行 (含输入栏 ContentControl 的 Grid)。</summary>
        private static Grid? FindRow(DependencyObject inRow) => FindAncestor<Grid>(inRow, g => g.Children.OfType<ContentControl>().Any());

        /// <summary>勾选后输入控件才生成:等布局完成后把焦点放到这一行的文本框 (全选文字) 或下拉框。</summary>
        private void FocusEditor(Grid? row)
        {
            if (row == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var editor = FindDescendant<Control>(row, c => c is TextBox || c is ComboBox);
                if (editor != null && editor.Focus() && editor is TextBox text)
                {
                    text.SelectAll();
                }
            }));
        }

        private static T? FindAncestor<T>(DependencyObject start, Func<T, bool> match)
            where T : DependencyObject
        {
            for (var current = VisualTreeHelper.GetParent(start); current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is T candidate && match(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> match)
            where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T candidate && match(candidate))
                {
                    return candidate;
                }

                var found = FindDescendant(child, match);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
