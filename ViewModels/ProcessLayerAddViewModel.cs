using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Zevision.Core.Localization;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 工艺分组标题中的"添加膜层":下拉框选择已有的膜层,点"添加"在设备上把它追加到该工艺膜层列表的末尾
    /// (<c>AP02xx0700</c> 再写入新条目),完成后重新读取工艺页。
    /// </summary>
    public sealed class ProcessLayerAddViewModel : ViewModelBase
    {
        private string? _selectedValue;

        /// <summary>创建添加行。</summary>
        /// <param name="listId">工艺的膜层列表 ID,如 <c>P020107</c>。</param>
        /// <param name="layers">可选的膜层 (值为膜层编号);默认选中第一个。</param>
        /// <param name="addCommand">添加命令。</param>
        public ProcessLayerAddViewModel(string listId, IEnumerable<ParameterOption> layers, ICommand addCommand)
        {
            ListId = listId ?? throw new ArgumentNullException(nameof(listId));
            Options = (layers ?? throw new ArgumentNullException(nameof(layers))).ToList();
            _selectedValue = Options.FirstOrDefault()?.Value;
            AddCommand = addCommand ?? throw new ArgumentNullException(nameof(addCommand));
        }

        /// <summary>膜层列表 ID,如 <c>P020107</c>。</summary>
        public string ListId { get; }

        /// <summary>可选的膜层。</summary>
        public IReadOnlyList<ParameterOption> Options { get; }

        /// <summary>选中的膜层编号;没有膜层时为 null。</summary>
        public string? SelectedValue
        {
            get => _selectedValue;
            set => SetProperty(ref _selectedValue, value);
        }

        /// <summary>在设备上添加选中的膜层。</summary>
        public ICommand AddCommand { get; }

        /// <summary>提示。</summary>
        public string ToolTip => Lang.T(
            "选择一个已有的膜层,点\"添加\"在设备上把它添加到这个工艺膜层列表的末尾,完成后重新读取。有勾选的项时不可用:请先保存或取消勾选",
            "Choose an existing layer and click Add to append it to this process's layer list on the device, then read again. Not available while settings are checked: save or uncheck them first");
    }
}
