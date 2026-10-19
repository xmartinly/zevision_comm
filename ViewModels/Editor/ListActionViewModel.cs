using System;
using System.Windows.Input;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;

namespace Zevision.ViewModels.Editor
{
    /// <summary>
    /// 分组标题中一个子列表的"+ / −"按钮,如膜系的预熔、后处理,膜层的膜系列表。
    /// "+"在设备上把一条 (设备默认值) 追加到列表末尾,"−"删除最后一条;完成后重新读取该页。
    /// </summary>
    public sealed class ListActionViewModel : ImmutableBindingSource
    {
        private readonly string? _addFirstText;

        /// <summary>创建按钮组。</summary>
        /// <param name="listId">列表 ID,如 <c>F0103</c> (膜系 F01 的预熔)。</param>
        /// <param name="listName">列表名称,如"预熔"。</param>
        /// <param name="count">现有条数。</param>
        /// <param name="addCommand">追加命令。</param>
        /// <param name="removeLastCommand">删除最后一条的命令。</param>
        /// <param name="emptyHint">列表为空时在分组中显示的说明 (和添加按钮);为 null 时不显示 (只有标题中的按钮)。</param>
        /// <param name="addFirstText">列表为空时分组中添加按钮的文字;为 null 时为"添加 + 列表名称"。</param>
        public ListActionViewModel(string listId, string listName, int count, ICommand addCommand, ICommand removeLastCommand, string? emptyHint = null, string? addFirstText = null)
        {
            ListId = listId ?? throw new ArgumentNullException(nameof(listId));
            ListName = listName ?? throw new ArgumentNullException(nameof(listName));
            EmptyHint = emptyHint;
            _addFirstText = addFirstText;
            Count = count;
            AddCommand = addCommand ?? throw new ArgumentNullException(nameof(addCommand));
            RemoveLastCommand = removeLastCommand ?? throw new ArgumentNullException(nameof(removeLastCommand));
        }

        /// <summary>列表 ID,如 <c>F0103</c>。</summary>
        public string ListId { get; }

        /// <summary>列表名称,如"预熔"。</summary>
        public string ListName { get; }

        /// <summary>现有条数。</summary>
        public int Count { get; }

        /// <summary>是否在分组中显示"列表为空"的说明和添加按钮:列表为空且有 <see cref="EmptyHint"/> (目前只有膜层的速率/厚度)。</summary>
        public bool ShowsEmptyRow => Count == 0 && EmptyHint != null;

        /// <summary>列表为空时在分组中显示的说明;没有时为 null。</summary>
        public string? EmptyHint { get; }

        /// <summary>列表为空时分组中添加按钮的文字,如"添加膜系"。</summary>
        public string AddFirstText => _addFirstText ?? Lang.T("添加", "Add ") + (Lang.IsChinese ? ListName : ListName.ToLowerInvariant());

        /// <summary>追加按钮的文字,如"+预熔"。</summary>
        public string AddText => "+" + ListName;

        /// <summary>删除按钮的文字,如"−预熔"。</summary>
        public string RemoveText => "−" + ListName;

        /// <summary>在设备上追加一条。</summary>
        public ICommand AddCommand { get; }

        /// <summary>在设备上删除最后一条 (列表为空时不可用)。</summary>
        public ICommand RemoveLastCommand { get; }

        /// <summary>追加按钮的提示。</summary>
        public string AddToolTip => Lang.T(
            $"在设备上添加一条{ListName};有勾选项时不可用",
            $"Add a {ListName.ToLowerInvariant()} on the device; unavailable while settings are checked");

        /// <summary>删除按钮的提示。</summary>
        public string RemoveToolTip => Lang.T(
            $"删除最后一条{ListName};有勾选项时不可用",
            $"Remove the last {ListName.ToLowerInvariant()}; unavailable while settings are checked");
    }
}
