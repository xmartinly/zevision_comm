using System;
using System.ComponentModel;
using System.Linq;
using Xunit;
using Zevision.ViewModels.Common;
using Zevision.ViewModels.Display;
using Zevision.ViewModels.Editor;
using Zevision.ViewModels.Main;

namespace Zevision.Tests
{
    /// <summary>
    /// 绑定源都要实现 <see cref="INotifyPropertyChanged"/>:否则 WPF 用 PropertyDescriptor 监听,静态表强引用绑定源,
    /// 绑定源引用视图模型时窗口关闭后无法回收 (修改配置窗口曾每打开一次泄漏约 40 MB,见 <see cref="ImmutableBindingSource"/>)。
    /// </summary>
    public class BindingSourceTests
    {
        [Fact]
        public void ViewModelsAndBoundItems_ImplementNotifyPropertyChanged()
        {
            var bound = typeof(MainViewModel).Assembly.GetTypes()
                .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && t.Namespace != null && t.Namespace.StartsWith("Zevision.ViewModels", StringComparison.Ordinal))
                .Where(t => t.Name.EndsWith("ViewModel", StringComparison.Ordinal)
                    || t == typeof(LogEntry) || t == typeof(ParameterOption) || t == typeof(Choice<>))
                .ToList();

            Assert.Contains(typeof(ListActionViewModel), bound);
            var missing = bound.Where(t => !typeof(INotifyPropertyChanged).IsAssignableFrom(t)).Select(t => t.Name).ToList();
            Assert.True(missing.Count == 0, "Not INotifyPropertyChanged: " + string.Join(", ", missing));
        }
    }
}
