using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Zevision.Core.Localization;
using Zevision.ViewModels.Common;

namespace Zevision.ViewModels.Editor
{
    /// <summary>
    /// 修改配置窗口"可写"页中的一个类别 (系统、材料、膜……),包含该类别的各个对象分组。
    /// </summary>
    public sealed class RecipeTabViewModel : ViewModelBase
    {
        /// <summary>类别顺序与名称:键为配置项 ID 的组字母,输入和输出合为一页。</summary>
        private static readonly (string Letters, string Title)[] Categories =
        {
            ("G", Lang.T("系统", "System")),
            ("M", Lang.T("材料", "Materials")),
            ("F", Lang.T("膜系", "Films")),
            ("L", Lang.T("膜层", "Layers")),
            ("P", Lang.T("工艺", "Processes")),
            ("N", Lang.T("传感器", "Sensors")),
            ("S", Lang.T("输出源", "Sources")),
            ("IO", Lang.T("输入输出", "I/O")),
            ("T", Lang.T("逻辑", "Logic")),
        };

        private const int ExpandedGroupLimit = 3;

        private readonly BulkObservableCollection<object> _rows = new BulkObservableCollection<object>();
        private bool _rowsBuilt;

        /// <summary>创建类别页。</summary>
        public RecipeTabViewModel(string title, IReadOnlyList<RecipeGroupViewModel> groups, string letters = "")
        {
            Title = title;
            Letters = letters;
            Groups = groups ?? throw new ArgumentNullException(nameof(groups));

            // 分组很少的页 (如系统) 默认展开;多的页默认收起,展开时才创建行,打开窗口更快。
            if (groups.Count <= ExpandedGroupLimit)
            {
                foreach (var group in groups)
                {
                    group.IsExpanded = true;
                }
            }

            foreach (var item in groups.SelectMany(g => g.Items))
            {
                item.Changed += (s, e) => OnPropertyChanged(nameof(Header));
            }

            foreach (var group in groups)
            {
                group.RowsChanged += (s, e) =>
                {
                    if (_rowsBuilt)
                    {
                        RebuildRows();
                    }
                };
            }
        }

        /// <summary>类别名称。</summary>
        public string Title { get; }

        /// <summary>本页包含的组字母,如 <c>M</c>、<c>IO</c>;"其他"页为空。</summary>
        public string Letters { get; }

        /// <summary>在设备上新建一个对象 (只有实机数据模式下的材料、膜、工艺页有);没有时为 null。</summary>
        public System.Windows.Input.ICommand? NewCommand { get; internal set; }

        /// <summary>新建按钮的文字,如"新建材料"。</summary>
        public string NewText { get; internal set; } = string.Empty;

        /// <summary>是否显示新建按钮。</summary>
        public bool CanCreate => NewCommand != null;

        /// <summary>页内的对象分组。</summary>
        public IReadOnlyList<RecipeGroupViewModel> Groups { get; }

        /// <summary>页标题,有勾选项时附带数量,如"材料 (2)"。</summary>
        public string Header
        {
            get
            {
                var checkedCount = Groups.Sum(g => g.Items.Count(i => i.IsChecked));
                return checkedCount > 0 ? $"{Title} ({checkedCount})" : Title;
            }
        }

        /// <summary>
        /// 页面显示的行:每个可见分组的标题 (<see cref="RecipeGroupViewModel"/>),分组展开时其后是可见的配置项
        /// (<see cref="RecipeItemViewModel"/>) 和为空的子列表 (<see cref="ListActionViewModel"/>)。
        /// </summary>
        /// <remarks>
        /// 页面用一个虚拟化列表显示,只为可见的行生成控件:展开全部或滚动时不再为几百行一次生成控件。
        /// 分组的行若放在分组内 (Expander 的内容),展开的分组是一个很高的项,虚拟化只能整组生成,按像素滚动到底要约 2 秒。
        /// 第一次读取时生成 (页面的按钮在类别页创建之后才设置),之后分组展开/收起、筛选时整体替换 (一次通知)。
        /// </remarks>
        public ObservableCollection<object> Rows
        {
            get
            {
                if (!_rowsBuilt)
                {
                    _rowsBuilt = true;
                    RebuildRows();
                }

                return _rows;
            }
        }

        /// <summary>
        /// 按类别把分组分到各页,页的顺序为 系统 → 材料 → 膜 → 层 → 工艺 → 传感器 → 源 → 输入输出 → 逻辑,
        /// 其他组放在最后的"其他"页。没有分组的类别不生成页。
        /// </summary>
        /// <param name="groups">全部分组。</param>
        /// <param name="alwaysShow">即使没有分组也生成的类别 (如可新建对象的材料页:删光后仍要能新建)。</param>
        public static IReadOnlyList<RecipeTabViewModel> Build(IReadOnlyList<RecipeGroupViewModel> groups, string alwaysShow = "")
        {
            if (groups is null)
            {
                throw new ArgumentNullException(nameof(groups));
            }

            var tabs = new List<RecipeTabViewModel>();
            foreach (var (letters, title) in Categories)
            {
                var inCategory = groups.Where(g => letters.IndexOf(g.Key[0]) >= 0).ToList();
                if (inCategory.Count > 0 || (letters.Length == 1 && alwaysShow.IndexOf(letters[0]) >= 0))
                {
                    tabs.Add(new RecipeTabViewModel(title, inCategory, letters));
                }
            }

            var known = string.Concat(Categories.Select(c => c.Letters));
            var others = groups.Where(g => known.IndexOf(g.Key[0]) < 0).ToList();
            if (others.Count > 0)
            {
                tabs.Add(new RecipeTabViewModel(Lang.T("其他", "Other"), others));
            }

            return tabs;
        }

        private void RebuildRows()
        {
            var rows = new List<object>();
            foreach (var group in Groups.Where(g => g.IsVisible))
            {
                rows.Add(group);
                if (group.IsExpanded)
                {
                    rows.AddRange(group.Items.Where(i => i.IsVisible));
                    rows.AddRange(group.EmptyLists);
                }
            }

            _rows.ReplaceAll(rows);
        }
    }
}
