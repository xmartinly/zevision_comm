using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Zevision.Commands;
using Zevision.Core;
using Zevision.Core.Configuration;
using Zevision.Core.Localization;

namespace Zevision.ViewModels
{
    /// <summary>
    /// 修改窗口中的配置与设备的同步方式。本类是实机数据:页面就是设备数据,设备变化后重新读取该组替换,不写文件。
    /// 保存的配置 (<see cref="FileTarget"/>) 页面是文件内容,设备变化后只把变化的部分写进文件并立即保存。
    /// </summary>
    internal class RecipeEditTarget
    {
        private RecipeEditTarget(RecipeDocument document, IInstrumentConfigReader config)
        {
            Document = document;
            Config = config;
        }

        /// <summary>正在编辑的配置。</summary>
        public RecipeDocument Document { get; }

        /// <summary>配置文件路径;实机数据时为 null。</summary>
        public virtual string? FilePath => null;

        protected IInstrumentConfigReader Config { get; }

        /// <summary>按来源创建:<paramref name="path"/> 为 null 时是实机数据。</summary>
        public static RecipeEditTarget Create(RecipeDocument document, string? path, IInstrumentConfigReader config, IRecipeStore recipes) =>
            path == null ? new RecipeEditTarget(document, config) : new FileTarget(document, path, config, recipes);

        /// <summary>新建/删除前设备上该组的项 (用来确定设备上有哪些对象)。实机数据:页面就是设备数据。</summary>
        public virtual Task<Result<IReadOnlyList<RecipeEntry>>> ReadDeviceGroupAsync(char group, CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<RecipeEntry>>.Ok(Document.Entries.FindAll(e => e.Id[0] == group)));

        /// <summary>新建对象时要避开的编号。</summary>
        public virtual ISet<string> UsedObjectIds(char group, ISet<string> onDevice) => onDevice;

        /// <summary>
        /// 重新读取了设备上的一个组 (无论之前的指令是否成功,设备状态以读取为准)。
        /// 实机数据用它替换该组;保存的配置不变 (页面是文件内容)。
        /// </summary>
        public virtual void OnDeviceGroupRead(char group, IReadOnlyList<RecipeEntry> deviceGroup) => Document.ReplaceGroup(group, deviceGroup);

        /// <summary>设备上新建/删除对象成功后更新配置 (实机数据已在 <see cref="OnDeviceGroupRead"/> 中更新)。</summary>
        /// <param name="action">新建或删除。</param>
        /// <param name="objectId">对象 ID。</param>
        /// <param name="deviceGroup">设备上该组当前的项 (重新读取的结果)。</param>
        /// <returns>保存文件失败的原因;成功时为 null。</returns>
        public virtual string? ApplyObjectChange(CommandActions action, string objectId, IReadOnlyList<RecipeEntry> deviceGroup) => null;

        /// <summary>在设备的列表末尾追加或删除之前的检查。</summary>
        /// <returns>不能修改的原因;可以时为 null。</returns>
        public virtual Task<string?> CheckListCountAsync(string listId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        /// <summary>设备上追加一条 (<paramref name="itemId"/>) 成功后更新配置;<paramref name="deviceGroup"/> 为重新读取的组。</summary>
        /// <returns>保存文件失败的原因;成功时为 null。</returns>
        public virtual string? ApplyListAppend(string listId, string itemId, IReadOnlyList<RecipeEntry> deviceGroup) => null;

        /// <summary>
        /// 设备上删除了列表的最后一项 (<paramref name="removedItemId"/>) 后更新配置;
        /// <paramref name="shifted"/> 是删除中间一项时前移写入的值 (没有时为空)。
        /// </summary>
        /// <returns>保存文件失败的原因;成功时为 null。</returns>
        public virtual string? ApplyListRemove(string removedItemId, IReadOnlyDictionary<string, string> shifted) => null;

        /// <summary>
        /// 修改配置。保存的配置先在副本上修改并保存文件,成功后才修改本文档,因此保存失败时页面仍与文件一致。
        /// </summary>
        /// <returns>保存文件失败的原因;成功 (或不需要保存文件) 时为 null。</returns>
        public virtual string? Commit(Action<RecipeDocument> change)
        {
            (change ?? throw new ArgumentNullException(nameof(change)))(Document);
            return null;
        }

        /// <summary>保存的配置:同时改设备和文件,文件立即保存 (设备已经改了)。</summary>
        private sealed class FileTarget : RecipeEditTarget
        {
            private readonly string _path;
            private readonly IRecipeStore _recipes;

            public FileTarget(RecipeDocument document, string path, IInstrumentConfigReader config, IRecipeStore recipes)
                : base(document, config)
            {
                _path = path;
                _recipes = recipes;
            }

            public override string? FilePath => _path;

            public override Task<Result<IReadOnlyList<RecipeEntry>>> ReadDeviceGroupAsync(char group, CancellationToken cancellationToken) =>
                Config.ReadGroupAsync(group, cancellationToken);

            /// <summary>编号取设备和文件中都未使用的,两边才不会冲突。</summary>
            public override ISet<string> UsedObjectIds(char group, ISet<string> onDevice)
            {
                var used = Document.ObjectIds(group);
                used.UnionWith(onDevice);
                return used;
            }

            public override void OnDeviceGroupRead(char group, IReadOnlyList<RecipeEntry> deviceGroup)
            {
            }

            /// <summary>新建:只加入新对象的项 (设备默认值),设备上多出的其他对象不进文件。删除:删去对象的全部项。</summary>
            public override string? ApplyObjectChange(CommandActions action, string objectId, IReadOnlyList<RecipeEntry> deviceGroup) =>
                Commit(d =>
                {
                    if (action == CommandActions.Add)
                    {
                        d.AddObject(objectId, deviceGroup);
                    }
                    else
                    {
                        d.RemoveObject(objectId);
                    }
                });

            /// <summary>设备在末尾追加/删除,只有两边条数相同时,设备上的编号才与文件对得上。</summary>
            public override async Task<string?> CheckListCountAsync(string listId, CancellationToken cancellationToken)
            {
                var result = await Config.QueryAsync(new[] { DeviceIds.CountOf(listId) }, cancellationToken);
                var value = result.IsSuccess ? result.Value[0] : null;
                if (!DeviceIds.TryParseCount(value?.Value, out var deviceCount))
                {
                    return Lang.T($"无法读取设备上 {listId} 的条数: ", $"Cannot read the number of items in {listId} on the device: ") + (result.IsSuccess ? value?.Error : result.Error);
                }

                var fileCount = Document.ListItemCount(listId);
                return deviceCount == fileCount
                    ? null
                    : Lang.T(
                        $"设备与文件中 {listId} 的条数不同 (设备 {deviceCount} 条,文件 {fileCount} 条),无法确定新增条目的编号。请先用\"恢复\"让设备与文件一致。",
                        $"{listId} has {deviceCount} item(s) on the device but {fileCount} in the file, so the number of the new item cannot be determined. Restore the file to the device first.");
            }

            /// <summary>只把新的一条 (设备默认值) 加入文件,文件中其他内容不变。</summary>
            public override string? ApplyListAppend(string listId, string itemId, IReadOnlyList<RecipeEntry> deviceGroup) =>
                Commit(d => d.AddListItem(listId, itemId, deviceGroup));

            /// <summary>前移的值写进文件,并删去最后一项及其子项。</summary>
            public override string? ApplyListRemove(string removedItemId, IReadOnlyDictionary<string, string> shifted) =>
                Commit(d =>
                {
                    d.SetValues(shifted);
                    d.RemoveListItem(removedItemId);
                });

            public override string? Commit(Action<RecipeDocument> change)
            {
                var copy = Document.Clone();
                change(copy);
                copy.Recipe.SavedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
                var saved = _recipes.Save(copy.Recipe, _path);
                if (!saved.IsSuccess)
                {
                    return saved.Error;
                }

                change(Document);
                Document.Recipe.SavedAt = copy.Recipe.SavedAt;
                Document.Recipe.Md5 = copy.Recipe.Md5;
                return null;
            }
        }
    }
}
