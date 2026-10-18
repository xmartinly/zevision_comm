using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Zevision.ViewModels.Common
{
    /// <summary>
    /// 异步命令。执行期间 <see cref="CanExecute"/> 返回 false,防止重复点击;异常交给错误回调处理。
    /// </summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Predicate<object?>? _canExecute;
        private readonly Action<Exception> _onError;
        private bool _isExecuting;

        /// <summary>
        /// 创建命令。
        /// </summary>
        /// <param name="execute">异步执行逻辑。</param>
        /// <param name="onError">未处理异常的回调 (在 UI 线程上调用)。</param>
        /// <param name="canExecute">可执行判断;为 null 时始终可执行。</param>
        public AsyncRelayCommand(Func<object?, Task> execute, Action<Exception> onError, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _onError = onError ?? throw new ArgumentNullException(nameof(onError));
            _canExecute = canExecute;
        }

        /// <inheritdoc />
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <inheritdoc />
        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

        /// <inheritdoc />
        public async void Execute(object? parameter)
        {
            // async void 只允许出现在命令/事件处理入口,且必须自行处理所有异常。
            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await _execute(parameter);
            }
#pragma warning disable CA1031 // 命令入口必须捕获所有异常,否则会使进程崩溃;异常已交给 _onError 记录。
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _onError(ex);
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
