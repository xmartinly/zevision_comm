using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;

namespace Zevision.ViewModels.Editor
{
    /// <summary>
    /// 修改窗口的"信息"页:设备信息、工艺状态、对象数量,以及传感器、源、输入输出的运行状态 (只读)。
    /// 各节由 <see cref="ReadOnlyLayout"/> 按配置中的对象生成。
    /// </summary>
    public sealed class InfoPageViewModel : ViewModelBase, IDisposable
    {
        private readonly IInstrumentConfigReader _reader;
        private readonly IErrorLog _errorLog;
        private CancellationTokenSource? _refresh;
        private bool _isRefreshing;
        private string _status = string.Empty;

        /// <summary>创建"信息"页。</summary>
        /// <param name="entries">配置项,决定列出哪些传感器、源、输入输出。</param>
        /// <param name="reader">读取设备。</param>
        /// <param name="errorLog">读取失败时记录。</param>
        /// <param name="onError">刷新命令的意外异常。</param>
        public InfoPageViewModel(IEnumerable<RecipeEntry> entries, IInstrumentConfigReader reader, IErrorLog errorLog, Action<Exception> onError)
        {
            Sections = ReadOnlyLayout.Build(entries).Select(s => new ReadOnlySectionViewModel(s)).ToList();
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _errorLog = errorLog ?? throw new ArgumentNullException(nameof(errorLog));
            RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), onError, _ => !IsRefreshing);
        }

        /// <summary>各节。</summary>
        public IReadOnlyList<ReadOnlySectionViewModel> Sections { get; }

        /// <summary>状态,如"读取于 15:30:12"。</summary>
        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        /// <summary>是否正在读取。</summary>
        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set
            {
                if (SetProperty(ref _isRefreshing, value))
                {
                    RefreshCommands();
                }
            }
        }

        /// <summary>从设备读取各项的当前值。</summary>
        public ICommand RefreshCommand { get; }

        private IEnumerable<ReadOnlyItemViewModel> Items => Sections.SelectMany(s => s.Items);

        /// <summary>
        /// 用已经读取的完整设备数据 (<c>?</c> 的结果) 填充,不再与设备通讯。设备上没有的项显示为读取失败。
        /// 需要最新值时用 <see cref="RefreshCommand"/>。
        /// </summary>
        /// <param name="snapshot">设备数据。</param>
        /// <param name="readAt">读取时间。</param>
        public void ApplySnapshot(InstrumentSnapshot snapshot, DateTime readAt)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            foreach (var item in Items)
            {
                var value = snapshot.GetValue(item.Id);
                item.Apply(new QueryValue(item.Id, value, value == null ? Lang.T("设备上没有该项", "Not present on the device") : null));
            }

            Status = Lang.T("读取于 ", "Read at ") + FormatTime(readAt) + Lang.T(" (与设备配置一起读取,点刷新获取最新值)", " (with the device configuration; click Refresh for current values)");
        }

        /// <summary>
        /// 用 Q 指令 (批量) 读取各项的当前值。再次读取或 <see cref="Dispose"/> (窗口关闭) 时取消上一次读取。
        /// </summary>
        public async Task RefreshAsync()
        {
            _refresh?.Cancel();
            var refresh = new CancellationTokenSource();
            _refresh = refresh;
            IsRefreshing = true;
            Status = Lang.T("正在读取设备…", "Reading the device…");
            try
            {
                var items = Items.ToList();
                var result = await _reader.QueryAsync(items.Select(i => i.Id).ToList(), refresh.Token);
                if (!result.IsSuccess)
                {
                    Status = Lang.T("读取失败: ", "Read failed: ") + result.Error;
                    _errorLog.Write(RecipeEditorMessages.LogSource, Status);
                    return;
                }

                for (var i = 0; i < items.Count; i++)
                {
                    items[i].Apply(result.Value[i]);
                }

                Status = Lang.T("读取于 ", "Read at ") + FormatTime(DateTime.Now);
            }
            catch (OperationCanceledException) when (refresh.IsCancellationRequested)
            {
                Status = Lang.T("已取消读取", "Read cancelled");
            }
            finally
            {
                if (ReferenceEquals(_refresh, refresh))
                {
                    _refresh = null;
                    IsRefreshing = false;
                }

                refresh.Dispose();
            }
        }

        /// <summary>取消正在进行的读取 (窗口关闭时调用)。</summary>
        public void Dispose() => _refresh?.Cancel();

        private static string FormatTime(DateTime time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
