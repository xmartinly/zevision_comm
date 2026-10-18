using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Zevision.ViewModels.Common
{
    /// <summary>
    /// 可以一次添加、删除或替换多项的集合:每次批量修改只发一次 Reset 通知。
    /// 逐项修改时每一项都让绑定的列表更新一次,几百上千项时界面会卡住。
    /// </summary>
    /// <typeparam name="T">项的类型。</typeparam>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>在末尾添加多项。</summary>
        public void AddRange(IReadOnlyList<T> items)
        {
            if (items is null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            CheckReentrancy();
            if (items.Count == 0)
            {
                return;
            }

            // Collection<T> 的默认底层存储是 List<T>。
            ((List<T>)Items).AddRange(items);
            NotifyReset();
        }

        /// <summary>删除最前面的 <paramref name="count"/> 项。</summary>
        public void RemoveFirst(int count)
        {
            CheckReentrancy();
            count = Math.Min(count, Count);
            if (count <= 0)
            {
                return;
            }

            ((List<T>)Items).RemoveRange(0, count);
            NotifyReset();
        }

        /// <summary>把全部内容换成 <paramref name="items"/>。</summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            if (items is null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            CheckReentrancy();
            var list = (List<T>)Items;
            list.Clear();
            list.AddRange(items);
            NotifyReset();
        }

        private void NotifyReset()
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
