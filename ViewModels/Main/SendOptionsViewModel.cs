using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;
using Zevision.ViewModels.Display;

namespace Zevision.ViewModels.Main
{
    /// <summary>
    /// 发送选项:数据显示格式、重复发送及其间隔。
    /// </summary>
    /// <remarks>
    /// 显示格式在后台线程格式化收发数据时读取 (<see cref="DisplayFormat"/>),只影响之后的数据,已显示的不变。
    /// 重复发送:一次收发完成后等待间隔再发送下一次,不会重叠;间隔在重复期间修改,下一次等待即生效。
    /// </remarks>
    public sealed class SendOptionsViewModel : ViewModelBase
    {
        /// <summary>默认重复间隔 (毫秒)。</summary>
        public const int DefaultRepeatInterval = 1000;

        private volatile DisplayFormat _displayFormat = DisplayFormat.Ascii;
        private bool _isRepeatEnabled;
        private int _repeatInterval = DefaultRepeatInterval;
        private bool _isRepeating;
        private CancellationTokenSource? _repeat;

        /// <summary>创建发送选项。</summary>
        public SendOptionsViewModel()
        {
            StopRepeatCommand = new RelayCommand(_ => Stop(), _ => IsRepeating);
        }

        /// <summary>显示格式选项。</summary>
        public IReadOnlyList<Choice<DisplayFormat>> DisplayFormats { get; } = new[]
        {
            new Choice<DisplayFormat>(DisplayFormat.Hex, "HEX", Lang.T("十六进制,每行 32 字节", "Hexadecimal, 32 bytes per line")),
            new Choice<DisplayFormat>(DisplayFormat.Ascii, "ASCII", Lang.T("文本;不可打印的字节显示为 <XX>", "Text; non-printable bytes are shown as <XX>")),
            new Choice<DisplayFormat>(DisplayFormat.Binary, "Binary", Lang.T("二进制,每行 16 字节", "Binary, 16 bytes per line")),
        };

        /// <summary>重复间隔选项 (毫秒)。</summary>
        public IReadOnlyList<int> RepeatIntervals { get; } = new[] { 100, 200, 500, 1000, 1500, 2000 };

        /// <summary>收发数据的显示格式。可在任意线程读取。</summary>
        public DisplayFormat DisplayFormat
        {
            get => _displayFormat;
            set
            {
                if (_displayFormat != value && Enum.IsDefined(typeof(DisplayFormat), value))
                {
                    _displayFormat = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>点击发送后是否按间隔重复发送。取消勾选时停止正在进行的重复发送。</summary>
        public bool IsRepeatEnabled
        {
            get => _isRepeatEnabled;
            set
            {
                if (SetProperty(ref _isRepeatEnabled, value) && !value)
                {
                    Stop();
                }
            }
        }

        /// <summary>重复间隔 (毫秒):一次收发完成到下一次发送之间的等待时间。只接受 <see cref="RepeatIntervals"/> 中的值。</summary>
        public int RepeatInterval
        {
            get => _repeatInterval;
            set
            {
                if (RepeatIntervals.Contains(value))
                {
                    SetProperty(ref _repeatInterval, value);
                }
            }
        }

        /// <summary>是否正在重复发送。</summary>
        public bool IsRepeating
        {
            get => _isRepeating;
            private set
            {
                if (SetProperty(ref _isRepeating, value))
                {
                    OnPropertyChanged(nameof(IsNotRepeating));
                    RefreshCommands();
                }
            }
        }

        /// <summary><see cref="IsRepeating"/> 的反值 (绑定发送按钮的可见性)。</summary>
        public bool IsNotRepeating => !IsRepeating;

        /// <summary>停止重复发送。</summary>
        public ICommand StopRepeatCommand { get; }

        /// <summary>
        /// 执行一次收发;勾选了重复发送时,之后每隔 <see cref="RepeatInterval"/> 再执行,直到 <see cref="Stop"/>、
        /// 取消勾选或 <paramref name="canContinue"/> 返回 false (如连接已断开)。
        /// 正在进行的收发不会被打断,停止在本次收发结束后生效。
        /// </summary>
        /// <param name="exchange">一次收发。</param>
        /// <param name="canContinue">每次收发后检查是否继续。</param>
        public async Task RunAsync(Func<Task> exchange, Func<bool> canContinue)
        {
            if (exchange is null)
            {
                throw new ArgumentNullException(nameof(exchange));
            }

            if (canContinue is null)
            {
                throw new ArgumentNullException(nameof(canContinue));
            }

            if (!IsRepeatEnabled || IsRepeating)
            {
                await exchange();
                return;
            }

            using (var repeat = new CancellationTokenSource())
            {
                _repeat = repeat;
                IsRepeating = true;
                try
                {
                    while (true)
                    {
                        await exchange();
                        if (repeat.IsCancellationRequested || !canContinue())
                        {
                            break;
                        }

                        await Task.Delay(RepeatInterval, repeat.Token);
                        if (!canContinue())
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (repeat.IsCancellationRequested)
                {
                    // 停止:等待间隔时被取消。
                }
                finally
                {
                    _repeat = null;
                    IsRepeating = false;
                }
            }
        }

        /// <summary>停止重复发送 (没有在重复时什么也不做)。</summary>
        public void Stop() => _repeat?.Cancel();
    }
}
