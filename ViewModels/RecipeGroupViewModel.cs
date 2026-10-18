using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Zevision.Localization;
using Zevision.Models;
using Zevision.Services;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 修改配置窗口中的一个分组 (一个对象,如"材料 M03")。
    /// </summary>
    public sealed class RecipeGroupViewModel : ViewModelBase
    {
        private bool _isExpanded;
        private bool _isVisible = true;

        /// <summary>
        /// 由分组创建。<paramref name="options"/> 给出下拉框的选项 (引用类为配置中已有的对象,如膜系的材料)。
        /// </summary>
        public RecipeGroupViewModel(RecipeGroup group, ParameterOptions options)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            Key = group.Key;
            Title = group.Title;
            Items = group.Entries.Select(e => RecipeItemViewModel.Create(e, options)).ToList();
            foreach (var item in Items)
            {
                item.Changed += (s, e) => OnPropertyChanged(nameof(Header));
            }

            CheckAllCommand = new RelayCommand(_ => SetAllChecked(true));
            UncheckAllCommand = new RelayCommand(_ => SetAllChecked(false), _ => Items.Any(i => i.IsChecked));
        }

        /// <summary>对象 ID。</summary>
        public string Key { get; }

        /// <summary>标题,如"材料 M03  Au"。保存后页面按新值重新生成,标题随之更新。</summary>
        public string Title { get; }

        /// <summary>组内配置项。</summary>
        public IReadOnlyList<RecipeItemViewModel> Items { get; }

        /// <summary>分组标题,附带项数和已勾选数。</summary>
        public string Header
        {
            get
            {
                var checkedCount = Items.Count(i => i.IsChecked);
                return checkedCount > 0
                    ? Lang.T($"{Title}    ({Items.Count} 项,已勾选 {checkedCount})", $"{Title}    ({Items.Count} settings, {checkedCount} checked)")
                    : Lang.T($"{Title}    ({Items.Count} 项)", $"{Title}    ({Items.Count} settings)");
            }
        }

        /// <summary>是否展开。</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>是否有符合筛选条件的项。</summary>
        public bool IsVisible
        {
            get => _isVisible;
            private set => SetProperty(ref _isVisible, value);
        }

        /// <summary>勾选组内全部 (可见的) 项。</summary>
        public ICommand CheckAllCommand { get; }

        /// <summary>取消勾选组内全部项 (值还原)。</summary>
        public ICommand UncheckAllCommand { get; }

        /// <summary>在设备上删除这个对象 (材料、膜系、膜层、工艺);没有时为 null。</summary>
        public ICommand? DeleteCommand { get; internal set; }

        /// <summary>是否显示删除按钮。</summary>
        public bool CanDelete => DeleteCommand != null;

        /// <summary>工艺的"添加膜层"行;不是工艺时为 null。</summary>
        public ProcessLayerAddViewModel? LayerAdd { get; internal set; }

        /// <summary>是否显示"添加膜层"行。</summary>
        public bool HasLayerAdd => LayerAdd != null;

        /// <summary>标题中的子列表"+ / −"按钮 (膜系的预熔、后处理,膜层的膜系列表);没有时为空。</summary>
        public IReadOnlyList<ListActionViewModel> ListActions { get; internal set; } = Array.Empty<ListActionViewModel>();

        /// <summary>要在分组中显示说明和添加按钮的空列表 (只有膜层的速率/厚度;膜系的预熔/后处理只用标题中的按钮)。</summary>
        public IReadOnlyList<ListActionViewModel> EmptyLists => ListActions.Where(a => a.ShowsEmptyRow).ToList();

        /// <summary>
        /// 应用筛选:标题或任一项匹配时分组可见;筛选文本非空时自动展开有匹配项的分组。
        /// </summary>
        /// <param name="filter">筛选文本;为空时显示全部。</param>
        /// <param name="expandMatches">有匹配项时是否展开分组 (匹配的分组太多时由调用方关闭,避免一次生成上千行)。</param>
        /// <returns>分组是否有可见项。</returns>
        public bool ApplyFilter(string filter, bool expandMatches = true)
        {
            var titleMatches = !string.IsNullOrEmpty(filter) && Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            var any = false;
            foreach (var item in Items)
            {
                item.IsVisible = titleMatches || item.Matches(filter);
                any |= item.IsVisible;
            }

            IsVisible = any;
            if (expandMatches && !string.IsNullOrEmpty(filter) && any)
            {
                IsExpanded = true;
            }

            return any;
        }

        private void SetAllChecked(bool isChecked)
        {
            foreach (var item in Items.Where(i => i.IsVisible || !isChecked))
            {
                item.IsChecked = isChecked;
            }
        }
    }
}
