using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Zevision.ViewModels.Display;
using Zevision.ViewModels.Main;

namespace Zevision.Views
{
    /// <summary>
    /// 主窗口。代码隐藏只处理纯视图行为:数据显示区自动滚动 (仅当停在底部时)、复制选中行、
    /// 程序不在前台时数据显示区在保存数据的时刻统一刷新 (<see cref="LogViewModel.IsInBackground"/>)。
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _scrollPending;
        private bool _followTail = true;
        private bool _logChanging;
        private bool _connectionClosed;
        private bool _closing;

        /// <summary>程序是否在前台 (本程序的某个窗口是活动窗口)。显示之前当作在前台,不暂存记录。</summary>
        private bool _appActive = true;

        /// <summary>
        /// 创建主窗口。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            FitToWorkArea();
            ((INotifyCollectionChanged)LogList.Items).CollectionChanged += OnLogChanged;
            Closing += OnClosing;
            StateChanged += (s, e) => UpdateLogRefresh();
            var app = Application.Current;
            if (app != null)
            {
                // 程序级的激活:修改配置窗口、消息框在前台时程序仍在前台。
                app.Activated += OnAppActivated;
                app.Deactivated += OnAppDeactivated;
                Closed += (s, e) =>
                {
                    app.Activated -= OnAppActivated;
                    app.Deactivated -= OnAppDeactivated;
                };
            }
        }

        private void OnAppActivated(object? sender, EventArgs e)
        {
            _appActive = true;
            UpdateLogRefresh();
        }

        private void OnAppDeactivated(object? sender, EventArgs e)
        {
            _appActive = false;
            UpdateLogRefresh();
        }

        /// <summary>其他程序在前台或主窗口最小化时,数据显示区在保存数据的时刻统一刷新;回到前台时立即显示。</summary>
        private void UpdateLogRefresh()
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Log.IsInBackground = !_appActive || WindowState == WindowState.Minimized;
            }
        }

        /// <summary>
        /// 关闭窗口时仍连接:先取消关闭,停止重复发送、断开并写完通讯数据文件 (<see cref="MainViewModel.CloseConnectionAsync"/>),再关闭。
        /// </summary>
        private async void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_connectionClosed || !(DataContext is MainViewModel viewModel) || !viewModel.HasConnection)
            {
                return;
            }

            e.Cancel = true;
            if (_closing)
            {
                return; // 已在断开中 (再次点击关闭):等第一次完成后自动关闭
            }

            _closing = true;
            IsEnabled = false;
            try
            {
                await viewModel.CloseConnectionAsync();
            }
            finally
            {
                // CloseConnectionAsync 不抛出通讯异常;意外异常交给全局处理,窗口照样关闭。
                _connectionClosed = true;
                Close();
            }
        }

        /// <summary>
        /// 默认尺寸 (XAML 中的 Width/Height) 保证左侧配置区无需滚动;
        /// 屏幕工作区比它小时 (如 1366×768 笔记本) 缩小到工作区内,避免窗口超出屏幕。
        /// </summary>
        private void FitToWorkArea()
        {
            var workArea = SystemParameters.WorkArea;
            if (Height > workArea.Height)
            {
                MinHeight = System.Math.Min(MinHeight, workArea.Height);
                Height = workArea.Height;
            }

            if (Width > workArea.Width)
            {
                MinWidth = System.Math.Min(MinWidth, workArea.Width);
                Width = workArea.Width;
            }
        }

        private void OnLogScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 内容高度不变说明是用户滚动:滚到底部时恢复自动滚动,向上翻看时暂停。
            // 内容不足一屏 (如清空后) 时总是跟随。记录变化后的布局调整和自动滚动不算 (见 OnLogChanged)。
            if (e.ExtentHeight <= e.ViewportHeight)
            {
                _followTail = true;
            }
            else if (e.ExtentHeightChange == 0 && !_logChanging)
            {
                _followTail = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - 1;
            }
        }

        private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 记录变化后,按像素虚拟化的列表会在随后的布局中微调滚动位置 (内容高度不变),不能当作用户向上翻看。
            // 布局 (Render) 和自动滚动 (Background) 都在 ContextIdle 之前完成。
            if (!_logChanging)
            {
                _logChanging = true;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new System.Action(() => _logChanging = false));
            }

            // 一批记录合并为一次滚动,在本批添加完成后执行。少量行逐行添加 (多个 Add 通知),大响应的几百行一次添加 (Reset 通知)。
            if ((e.Action != NotifyCollectionChangedAction.Add && e.Action != NotifyCollectionChangedAction.Reset) || _scrollPending || !_followTail)
            {
                return;
            }

            _scrollPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new System.Action(() =>
            {
                _scrollPending = false;
                if (LogList.Items.Count > 0)
                {
                    LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                }
            }));
        }

        /// <summary>
        /// 指令输入框:Enter 发送,Shift+Enter 换行 (多行时每行一条指令)。输入法组字时的 Enter 为 ImeProcessed,不会触发发送。
        /// </summary>
        private void OnCommandInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                return;
            }

            e.Handled = true;
            if (DataContext is MainViewModel viewModel && viewModel.SendCommand.CanExecute(null))
            {
                viewModel.SendCommand.Execute(null);
            }
        }

        /// <summary>
        /// GitHub 按钮:用默认浏览器打开项目主页 (<see cref="AppInfo.ProjectUrl"/>)。
        /// 打不开时 (如没有关联的浏览器) 在数据显示区写一条错误,不弹出消息框 (没有改变设备和文件)。
        /// </summary>
        private void OnOpenProjectPage(object sender, RoutedEventArgs e)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(AppInfo.ProjectUrl) { UseShellExecute = true }))
                {
                }
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
            {
                (DataContext as MainViewModel)?.Log.Add(LogKind.Error, UiText.ProjectPageFailed(AppInfo.ProjectUrl, ex.Message));
            }
        }

        private void OnCopyLog(object sender, ExecutedRoutedEventArgs e)
        {
            if (LogList.SelectedItems.Count == 0)
            {
                return;
            }

            // SelectedItems 按点选顺序排列,这里按列表顺序输出。
            var selected = new HashSet<object>();
            foreach (var item in LogList.SelectedItems)
            {
                selected.Add(item);
            }

            var sb = new StringBuilder();
            foreach (var item in LogList.Items)
            {
                if (!(item is LogEntry entry) || !selected.Contains(entry))
                {
                    continue;
                }

                sb.AppendLine(entry.ToDisplayLine());
            }

            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // 剪贴板被其他程序占用 (CLIPBRD_E_CANT_OPEN):放弃本次复制,不弹出错误。
            }

            e.Handled = true;
        }
    }
}
