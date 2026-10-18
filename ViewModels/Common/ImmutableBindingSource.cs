using System.ComponentModel;

namespace Zevision.ViewModels.Common
{
    /// <summary>
    /// 属性不变的绑定源:实现 <see cref="INotifyPropertyChanged"/>,但从不触发。
    /// </summary>
    /// <remarks>
    /// WPF 绑定到没有实现该接口的对象时,改用 <c>PropertyDescriptor.AddValueChanged</c> 监听属性:静态表强引用该对象,
    /// 绑定也更慢。对象再引用视图模型时 (如按钮的命令),窗口关闭后整个窗口都无法回收;修改配置窗口曾因此每打开一次
    /// 泄漏约 40 MB。所以用作绑定源的类都要实现该接口:会变的派生自 <see cref="ViewModelBase"/>,不变的派生自本类。
    /// </remarks>
    public abstract class ImmutableBindingSource : INotifyPropertyChanged
    {
        /// <summary>属性不变,从不触发。</summary>
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }
    }
}
