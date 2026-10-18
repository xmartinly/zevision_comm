using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Zevision.Commands;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;

namespace Zevision.ViewModels.Editor
{
    /// <summary>
    /// 修改配置窗口"可写"页中的一行:勾选后才能编辑值,取消勾选时值还原为文件中的原值。
    /// 名称、单位、下拉选项和范围来自 <see cref="ZevisionCommandSet"/>;文档未列出的项按自由文本处理。
    /// </summary>
    public sealed class RecipeItemViewModel : ViewModelBase
    {
        private string _value;
        private bool _isChecked;
        private bool _isVisible = true;
        private string _originalValue;

        /// <summary>
        /// 由文件中的配置项创建。
        /// </summary>
        /// <param name="entry">配置项。</param>
        /// <param name="definition">说明;文档未列出时为 null。</param>
        /// <param name="options">下拉选项 (枚举或引用);不是下拉框时为空。</param>
        public RecipeItemViewModel(RecipeEntry entry, CommandDefinition? definition = null, IReadOnlyList<ParameterOption>? options = null)
        {
            if (entry is null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            Id = entry.Id;
            Description = entry.Description;
            Definition = definition;
            _originalValue = entry.Value;
            _value = entry.Value;
            Options = WithCurrentValue(options ?? Array.Empty<ParameterOption>(), entry.Value, definition);
            Label = definition?.LabelFor(entry.Id, entry.Description) ?? entry.Description;
            ToolTip = BuildToolTip();
        }

        /// <summary>勾选状态或值变化时触发,供分组和窗口刷新统计。</summary>
        public event EventHandler? Changed;

        /// <summary>命令 ID。</summary>
        public string Id { get; }

        /// <summary>行末的删除按钮 (工艺膜层列表中的一项);没有时为 null。</summary>
        public ICommand? RemoveCommand { get; internal set; }

        /// <summary>是否显示行末的删除按钮。</summary>
        public bool CanRemove => RemoveCommand != null;

        /// <summary>设备帮助表中的说明 (英文)。</summary>
        public string Description { get; }

        /// <summary>说明表中的定义;文档未列出时为 null。</summary>
        public CommandDefinition? Definition { get; }

        /// <summary>显示名称,带单位,如 <c>密度 (g/cm³)</c>;文档未列出时为设备说明。</summary>
        public string Label { get; }

        /// <summary>鼠标提示:ID、说明、范围、设备说明。</summary>
        public string ToolTip { get; }

        /// <summary>下拉选项;为空时用文本框输入。</summary>
        public IReadOnlyList<ParameterOption> Options { get; }

        /// <summary>是否用下拉框输入。</summary>
        public bool UsesOptions => Options.Count > 0;

        /// <summary>未勾选时显示的值:下拉项显示选项文本 (如 <c>1 - On</c>),其他显示原值。</summary>
        public string DisplayValue => Options.FirstOrDefault(o => o.Value == Value)?.Label ?? Value;

        /// <summary>文件中的原值;保存成功后更新为新值。</summary>
        public string OriginalValue
        {
            get => _originalValue;
            private set
            {
                if (SetProperty(ref _originalValue, value))
                {
                    OnPropertiesChanged(nameof(IsModified), nameof(Error), nameof(HasError));
                }
            }
        }

        /// <summary>是否勾选 (勾选后可编辑,保存时写入设备)。取消勾选会把值还原为原值。</summary>
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (!SetProperty(ref _isChecked, value))
                {
                    return;
                }

                if (!value)
                {
                    Value = OriginalValue;
                }

                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>当前值。</summary>
        public string Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value ?? string.Empty))
                {
                    OnPropertiesChanged(nameof(DisplayValue), nameof(IsModified), nameof(Error), nameof(HasError));
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>值是否与原值不同。</summary>
        public bool IsModified => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);

        /// <summary>
        /// 值的校验错误;合法时为 null。协议字符总是检查;类型和范围只在值被修改后检查,
        /// 因为设备上的现有值可能超出文档给出的范围,原样写回不应被拦下。
        /// </summary>
        public string? Error => IsModified ? ParameterValue.Validate(Definition, Value) : ParameterValue.Validate(null, Value);

        /// <summary>值是否不合法。</summary>
        public bool HasError => Error != null;

        /// <summary>写入设备的值:按类型规整 (数字按原值的小数位数,时间为 <c>H:MM:SS</c>)。</summary>
        public string ValueToWrite => IsModified ? ParameterValue.Normalize(Definition, Value, OriginalValue) : Value;

        /// <summary>是否符合筛选条件。</summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        /// <summary>
        /// 是否匹配筛选文本 (ID、名称、设备说明或当前值包含该文本,不区分大小写)。
        /// </summary>
        public bool Matches(string filter) =>
            string.IsNullOrEmpty(filter)
            || Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
            || Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
            || Description.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
            || Value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// 保存成功后调用:把当前值作为新的原值,并取消勾选。
        /// </summary>
        public void Commit()
        {
            OriginalValue = Value;
            IsChecked = false;
        }

        /// <summary>
        /// 由配置项创建,名称、单位和选项从 <see cref="ZevisionCommandSet"/> 查找;下拉选项来自 <paramref name="options"/>。
        /// </summary>
        public static RecipeItemViewModel Create(RecipeEntry entry, ParameterOptions options)
        {
            if (entry is null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var definition = ZevisionCommandSet.Find(entry.Id);
            return new RecipeItemViewModel(entry, definition, definition == null ? null : options.For(definition, entry.Id));
        }

        /// <summary>当前值不在选项中时 (设备值超出文档、引用的对象已不存在) 追加一项,保证下拉框能显示原值。</summary>
        private static IReadOnlyList<ParameterOption> WithCurrentValue(IReadOnlyList<ParameterOption> options, string value, CommandDefinition? definition)
        {
            if (options.Count == 0 || options.Any(o => o.Value == value))
            {
                return options;
            }

            var text = definition?.Kind == CommandValueKind.Reference ? Lang.T("(不存在)", "(missing)") : Lang.T("(未知)", "(unknown)");
            return options.Concat(new[] { new ParameterOption(value, text) }).ToList();
        }

        private string BuildToolTip()
        {
            var sb = new StringBuilder(Id);
            if (Definition != null)
            {
                // 英文界面用设备帮助表中的 DESCRIPTION (与行名称一致);中文界面用中文名称,设备说明另起一行。
                sb.Append("  ").Append(Lang.IsChinese || string.IsNullOrEmpty(Description) ? Definition.Name() : Description);
                if (!string.IsNullOrEmpty(Definition.RangeText()))
                {
                    sb.AppendLine().Append(Lang.T("范围: ", "Range: ")).Append(Definition.RangeText());
                }
            }
            else
            {
                sb.AppendLine().Append(Lang.T("协议文档未列出此项,按文本输入", "Not in the protocol document; entered as text"));
            }

            if (Lang.IsChinese && !string.IsNullOrEmpty(Description))
            {
                sb.AppendLine().Append("设备说明: ").Append(Description);
            }

            return sb.ToString();
        }
    }
}
