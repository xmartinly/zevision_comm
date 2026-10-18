using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Zevision.Commands;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;
using Zevision.Services;
using M = Zevision.ViewModels.RecipeEditorMessages;

namespace Zevision.ViewModels
{
    /// <summary>修改窗口对设备的操作,由 <see cref="RecipePageBuilder"/> 挂到"设置"页的按钮上。</summary>
    internal interface IRecipePageActions
    {
        /// <summary>新建、删除、列表修改的命令:页面没有未保存的修改时才可用。</summary>
        ICommand Command(Func<Task> execute, Func<bool>? canExecute = null);

        /// <summary>新建对象。</summary>
        Task CreateObjectAsync(char group);

        /// <summary>删除对象。</summary>
        Task DeleteObjectAsync(string objectId);

        /// <summary>在列表末尾追加一项。</summary>
        Task AppendListEntryAsync(string listId, string listName, string? value = null, string? valueLabel = null);

        /// <summary>删除列表的一项 (<paramref name="number"/> 为 null 时删除最后一项)。</summary>
        Task RemoveListEntryAsync(string listId, string listName, int? number = null);
    }

    /// <summary>
    /// 修改窗口"设置"页的内容:可修改的配置项按对象分组、按类别分页,
    /// 并按 <see cref="ObjectTypes"/> 给材料、膜系、膜层、工艺挂上新建、删除和子列表的按钮。
    /// 指令集中只能查询的项 (如 N**08) 不显示,文件中的值保持不变。
    /// </summary>
    internal static class RecipePageBuilder
    {
        /// <summary>生成全部分组 (按恢复顺序) 和类别页。</summary>
        public static (IReadOnlyList<RecipeGroupViewModel> Groups, IReadOnlyList<RecipeTabViewModel> Tabs) Build(RecipeDocument document, IRecipePageActions actions)
        {
            var options = new ParameterOptions(document.Entries);
            var writable = document.Entries.Where(e => ZevisionCommandSet.Find(e.Id)?.CanUpdate != false);
            var groups = RecipeGrouping.Build(writable).Select(g => new RecipeGroupViewModel(g, options)).ToList();

            // 可新建的类别即使没有分组也显示,删光后仍能新建。
            var tabs = RecipeTabViewModel.Build(groups, string.Concat(ObjectTypes.All.Select(t => t.Group)));
            foreach (var tab in tabs)
            {
                var type = tab.Letters.Length == 1 ? ObjectTypes.Find(tab.Letters[0]) : null;
                if (type == null)
                {
                    continue;
                }

                tab.NewText = Lang.T("新建", "New ") + type.Kind;
                tab.NewCommand = actions.Command(() => actions.CreateObjectAsync(type.Group));
                foreach (var group in tab.Groups.Where(g => ObjectTypes.OfObjectId(g.Key) != null))
                {
                    AddObjectCommands(group, type, document, options, actions);
                }
            }

            return (groups, tabs);
        }

        /// <summary>对象分组的删除按钮和子列表按钮。</summary>
        private static void AddObjectCommands(RecipeGroupViewModel group, ObjectType type, RecipeDocument document, ParameterOptions options, IRecipePageActions actions)
        {
            var key = group.Key;
            group.DeleteCommand = actions.Command(() => actions.DeleteObjectAsync(key));

            var headerLists = new List<ListActionViewModel>();
            foreach (var list in type.Lists)
            {
                var listId = key + list.Suffix;
                var listName = ZevisionCommandSet.IndexName(type.ListPattern(list)).Value();
                var count = document.ListItemCount(listId);
                if (list.Style == ObjectListStyle.ItemRows)
                {
                    AddItemRows(group, listId, listName, count, options, actions);
                    continue;
                }

                var emptyRow = list.Style == ObjectListStyle.HeaderButtonsWithEmptyRow;
                headerLists.Add(new ListActionViewModel(
                    listId,
                    listName,
                    count,
                    actions.Command(() => actions.AppendListEntryAsync(listId, listName)),
                    actions.Command(() => actions.RemoveListEntryAsync(listId, listName), () => count > 0),
                    emptyRow ? M.EmptyListHint : null,
                    emptyRow ? M.AddFirstListItem : null));
            }

            group.ListActions = headerLists;
        }

        /// <summary>逐项删除的列表 (工艺的膜层):每一项的删除按钮,以及选择被引用对象的"添加"行。</summary>
        private static void AddItemRows(RecipeGroupViewModel owner, string listId, string listName, int count, ParameterOptions options, IRecipePageActions actions)
        {
            for (var number = 1; number <= count; number++)
            {
                var n = number;
                var item = owner.Items.FirstOrDefault(i => i.Id == DeviceIds.Item(listId, n));
                if (item != null)
                {
                    item.RemoveCommand = actions.Command(() => actions.RemoveListEntryAsync(listId, listName, n));
                }
            }

            var definition = ZevisionCommandSet.Find(DeviceIds.Item(listId, CommandDefinition.MinIndex));
            var choices = definition == null ? Array.Empty<ParameterOption>() : options.For(definition).Where(o => o.Value != "0");
            ProcessLayerAddViewModel? add = null;
            add = new ProcessLayerAddViewModel(
                listId,
                choices,
                actions.Command(() => actions.AppendListEntryAsync(listId, listName, add!.SelectedValue, add.SelectedLabel), () => add?.SelectedValue != null));
            owner.LayerAdd = add;
        }
    }
}
