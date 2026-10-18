using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Zevision.ViewModels.Common
{
    /// <summary>
    /// 实现 <see cref="INotifyPropertyChanged"/> 的视图模型基类。
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 值发生变化时更新字段并发出通知。
        /// </summary>
        /// <returns>值是否发生了变化。</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// 发出属性变更通知。
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 一次通知多个属性,用于由其他状态计算出的属性 (如 <c>CanSave</c>)。
        /// </summary>
        protected void OnPropertiesChanged(params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                OnPropertyChanged(name);
            }
        }

        /// <summary>
        /// 请求 WPF 重新查询各命令的可执行状态 (<see cref="RelayCommand"/> 和 <see cref="AsyncRelayCommand"/> 通过 <see cref="CommandManager"/> 刷新)。
        /// </summary>
        protected static void RefreshCommands() => CommandManager.InvalidateRequerySuggested();
    }
}
