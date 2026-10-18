using System;
using System.Collections.Generic;
using System.Linq;
using Zevision.Commands;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;

namespace Zevision.ViewModels
{
    /// <summary>修改配置窗口"只读"页中的一节。</summary>
    public sealed class ReadOnlySectionViewModel : ImmutableBindingSource
    {
        /// <summary>由一节的定义创建。</summary>
        public ReadOnlySectionViewModel(ReadOnlySection section)
        {
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            Title = section.Title;
            Items = section.Ids.Select(id => new ReadOnlyItemViewModel(id)).ToList();
        }

        /// <summary>标题。</summary>
        public string Title { get; }

        /// <summary>各项。</summary>
        public IReadOnlyList<ReadOnlyItemViewModel> Items { get; }
    }

    /// <summary>"只读"页中的一项:名称、从设备读取的当前值。</summary>
    public sealed class ReadOnlyItemViewModel : ViewModelBase
    {
        /// <summary>尚未读取或读取失败时显示的值。</summary>
        public const string NoValue = "—";

        private string _value = NoValue;
        private string _toolTip;

        /// <summary>创建一项。</summary>
        public ReadOnlyItemViewModel(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Definition = ZevisionCommandSet.Find(id);
            Label = Definition?.Label() ?? id;
            _toolTip = BaseToolTip;
        }

        /// <summary>命令 ID。</summary>
        public string Id { get; }

        /// <summary>说明表中的定义;没有时为 null。</summary>
        public CommandDefinition? Definition { get; }

        /// <summary>显示名称,带单位。</summary>
        public string Label { get; }

        /// <summary>显示的值:枚举显示为 <c>1 - Failed</c>。</summary>
        public string Value
        {
            get => _value;
            private set => SetProperty(ref _value, value);
        }

        /// <summary>鼠标提示:ID、名称、选项;读取失败时附带原因。</summary>
        public string ToolTip
        {
            get => _toolTip;
            private set => SetProperty(ref _toolTip, value);
        }

        private string BaseToolTip
        {
            get
            {
                var tip = Definition == null ? Id : Id + "  " + Definition.Name();
                return Definition?.Kind == CommandValueKind.Enum ? tip + Environment.NewLine + Definition.RangeText() : tip;
            }
        }

        /// <summary>显示查询结果。</summary>
        public void Apply(QueryValue result)
        {
            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (result.Value == null)
            {
                Value = NoValue;
                ToolTip = BaseToolTip + Environment.NewLine + Lang.T("读取失败: ", "Read failed: ") + result.Error;
                return;
            }

            var option = Definition?.Options.FirstOrDefault(o => o.Value == result.Value);
            Value = option?.GetLabel(CommandText.Language) ?? result.Value;
            ToolTip = BaseToolTip;
        }
    }
}
