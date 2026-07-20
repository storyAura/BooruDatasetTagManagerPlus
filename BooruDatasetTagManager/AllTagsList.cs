using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public class AllTagsList : CollectionBase, IBindingListView//, IBindingList
    {
        private ListChangedEventArgs resetEvent = new ListChangedEventArgs(ListChangedType.Reset, -1);
        private ListChangedEventHandler onListChanged;
        private ListSortDescriptionCollection sortDescriptions = new ListSortDescriptionCollection();
        private List<AllTagsItem> tagsList = new List<AllTagsItem>();
        private readonly Dictionary<string, AllTagsItem> tagIndex = new Dictionary<string, AllTagsItem>(StringComparer.Ordinal);
        private readonly object translationSync = new object();
        private Task translationTask = Task.CompletedTask;
        private int batchUpdateDepth = 0;
        private bool batchDirty = false;

        private string filterText = string.Empty;
        private bool filterByCount = false;
        private int filterTagsCount = 0;

        private bool _isSorted = false;
        private PropertyDescriptor _propertyDescriptor;
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;

        public AllTagsItem this[int index]
        {
            get
            {
                return (AllTagsItem)(List[index]);
            }
            set
            {
                List[index] = value;
            }
        }

        public AllTagsList() : base()
        {
        }


        public Task TranslateAllTags()
        {
            return TranslateAllAsync();
        }

        public bool IsFilterByCount() => filterByCount;


        public Task TranslateAllAsync()
        {
            return TranslateAllAsync(Program.TransManager.TranslateAsync);
        }

        public Task TranslateAllAsync(Func<string, Task<string>> translateAsync)
        {
            if (translateAsync == null)
                throw new ArgumentNullException(nameof(translateAsync));

            lock (translationSync)
            {
                if (!translationTask.IsCompleted)
                    return translationTask;

                translationTask = TranslatePendingAsync(translateAsync);
                return translationTask;
            }
        }

        private async Task TranslatePendingAsync(Func<string, Task<string>> translateAsync)
        {
            while (true)
            {
                var pending = tagsList
                    .Where(item => item.IsNeedTranslate())
                    .Select(item => new KeyValuePair<AllTagsItem, string>(item, item.Tag))
                    .ToList();

                if (pending.Count == 0)
                    return;

                foreach (var entry in pending)
                {
                    string result = string.Empty;
                    if (!string.IsNullOrEmpty(entry.Value))
                    {
                        try
                        {
                            result = await translateAsync(entry.Value) ?? string.Empty;
                        }
                        catch
                        {
                            result = string.Empty;
                        }
                    }

                    if (tagIndex.TryGetValue(entry.Value, out var current)
                        && ReferenceEquals(current, entry.Key)
                        && current.Tag == entry.Value)
                    {
                        current.SetTranslation(result);
                        int listIndex = List.IndexOf(current);
                        if (listIndex != -1)
                            OnListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, listIndex));
                    }
                }
            }
        }

        public IDisposable BeginBatchUpdate()
        {
            batchUpdateDepth++;
            return new BatchUpdateScope(this);
        }

        private void EndBatchUpdate()
        {
            if (batchUpdateDepth == 0)
                return;

            batchUpdateDepth--;
            if (batchUpdateDepth != 0 || !batchDirty)
                return;

            SortTagIndex();
            RebuildVisibleList();
            batchDirty = false;
            OnListChanged(resetEvent);
        }

        private void SortTagIndex()
        {
            if (_propertyDescriptor == null
                || (_propertyDescriptor.Name == "Tag" && _sortDirection == ListSortDirection.Ascending))
            {
                tagsList.Sort(new SortByTagNameTIAscending());
            }
            else if (_propertyDescriptor.Name == "Tag")
            {
                tagsList.Sort(new SortByTagNameTIDescending());
            }
            else if (_propertyDescriptor.Name == "Count" && _sortDirection == ListSortDirection.Ascending)
            {
                tagsList.Sort(new SortByCountTIAscending());
            }
            else if (_propertyDescriptor.Name == "Count")
            {
                tagsList.Sort(new SortByCountTIDescending());
            }
        }

        private void RebuildVisibleList()
        {
            InnerList.Clear();
            foreach (var item in tagsList)
            {
                if (CheckCurrentFilter(item))
                    InnerList.Add(item);
            }
        }

        private bool CheckCurrentFilter(AllTagsItem item)
        {
            return (!filterByCount || item.Count == filterTagsCount)
                && CheckFilterOnTag(item, filterText);
        }

        public void AddTag(string tag)
        {
            if (tagIndex.TryGetValue(tag, out var existing))
            {
                existing.Count++;
                if (batchUpdateDepth > 0)
                {
                    batchDirty = true;
                }
                else if (_isSorted && _propertyDescriptor.Name == "Count")
                {
                    ((IBindingListView)this).ApplySort(_propertyDescriptor, _sortDirection);
                }
            }
            else
            {
                var tagItem = new AllTagsItem(tag);
                tagIndex[tag] = tagItem;
                if (batchUpdateDepth > 0)
                {
                    tagItem.Parent = this;
                    tagsList.Add(tagItem);
                    if (CheckCurrentFilter(tagItem))
                        List.Add(tagItem);
                    batchDirty = true;
                }
                else
                {
                    AddWithSortingInternal(tagsList, tagItem);
                    if (CheckCurrentFilter(tagItem))
                        AddWithSortingInternal(List, tagItem);
                }
            }
        }

        public void RemoveTag(string tag, bool allTags = false)
        {
            if (tagIndex.TryGetValue(tag, out var item))
            {
                if (allTags)
                {
                    tagsList.Remove(item);
                    tagIndex.Remove(tag);
                    if (List.Contains(item))
                        List.Remove(item);
                }
                else
                {
                    if (item.Count > 1)
                    {
                        item.Count--;
                        if (batchUpdateDepth == 0 && _isSorted && _propertyDescriptor.Name == "Count")
                        {
                            ((IBindingListView)this).ApplySort(_propertyDescriptor, _sortDirection);
                        }
                    }
                    else
                    {
                        tagsList.Remove(item);
                        tagIndex.Remove(tag);
                        if (List.Contains(item))
                            List.Remove(item);
                    }
                }

                if (batchUpdateDepth > 0)
                    batchDirty = true;
            }
        }

        public void ChangeTag(string oldTag, string newTag)
        {
            RemoveTag(oldTag);
            AddTag(newTag);
        }

        public string[] GetAllTagsList()
        {
            return tagsList.Select(x => x.Tag).ToArray();
        }

        private void AddWithSortingInternal(IList lst, AllTagsItem item)
        {
            item.Parent = this;
            if (lst.Count == 0)
            {
                lst.Add(item);
            }
            else
            {
                for (int i = 0; i < lst.Count; i++)
                {
                    int compareResult = 0;
                    if (_propertyDescriptor == null)
                        compareResult = item.Tag.CompareTo(((AllTagsItem)lst[i]).Tag);
                    else if (_propertyDescriptor.Name == "Tag" && _sortDirection == ListSortDirection.Ascending)
                        compareResult = item.Tag.CompareTo(((AllTagsItem)lst[i]).Tag);
                    else if (_propertyDescriptor.Name == "Tag" && _sortDirection == ListSortDirection.Descending)
                        compareResult = ((AllTagsItem)lst[i]).Tag.CompareTo(item.Tag);
                    else if (_propertyDescriptor.Name == "Count" && _sortDirection == ListSortDirection.Ascending)
                    {
                        compareResult = item.Count.CompareTo(((AllTagsItem)lst[i]).Count);
                        if (compareResult == 0)
                        {
                            compareResult = item.Tag.CompareTo(((AllTagsItem)lst[i]).Tag);
                        }
                    }
                    else if (_propertyDescriptor.Name == "Count" && _sortDirection == ListSortDirection.Descending)
                    {
                        compareResult = ((AllTagsItem)lst[i]).Count.CompareTo(item.Count);
                        if (compareResult == 0)
                        {
                            compareResult = item.Tag.CompareTo(((AllTagsItem)lst[i]).Tag);
                        }
                    }
                    if (compareResult < 0)
                    {
                        lst.Insert(i, item);
                        return;
                    }
                }
                lst.Add(item);
            }
        }

        //public int Add(AllTagsItem item)
        //{
        //    item.Parent = this;
        //    int res = List.Add(item);
        //    return res;
        //}

        /// <summary>
        /// List<AllTagsItem> tagsList
        /// </summary>
        /// <param name="tag"></param>
        /// <returns></returns>
        public int IndexOfTagsList(string tag)
        {
            if (!tagIndex.TryGetValue(tag, out var item))
                return -1;
            return tagsList.IndexOf(item);
        }
        /// <summary>
        /// internal List
        /// </summary>
        /// <param name="tag"></param>
        /// <returns></returns>
        public int IndexOfList(string tag)
        {
            if (!tagIndex.TryGetValue(tag, out var item))
                return -1;
            return List.IndexOf(item);
        }

        public int FindTagStartWith(string tag)
        {
            for (int i = 0; i < List.Count; i++)
            {
                if (((AllTagsItem)List[i]).Tag.StartsWith(tag))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Case-insensitive search used by the All Tags search box. Scans from
        /// <paramref name="startIndex"/> and wraps around; a prefix match wins
        /// over the first substring match.
        /// </summary>
        public int FindTagBestMatch(string text, int startIndex)
        {
            return FindTagBestMatch(text, startIndex, null);
        }

        /// <summary>
        /// Match priority: tag prefix > tag substring > translation substring >
        /// alias hit. <paramref name="aliasTags"/> carries English tags resolved
        /// from the Chinese CSV dictionary so typing Chinese finds their rows.
        /// </summary>
        public int FindTagBestMatch(string text, int startIndex, ISet<string> aliasTags)
        {
            int count = List.Count;
            if (string.IsNullOrEmpty(text) || count == 0)
                return -1;
            startIndex = ((startIndex % count) + count) % count;
            int containsMatch = -1;
            int translationMatch = -1;
            int aliasMatch = -1;
            for (int offset = 0; offset < count; offset++)
            {
                int i = (startIndex + offset) % count;
                var item = (AllTagsItem)List[i];
                string tag = item.Tag;
                if (tag == null)
                    continue;
                if (tag.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    return i;
                if (containsMatch == -1 && tag.Contains(text, StringComparison.OrdinalIgnoreCase))
                    containsMatch = i;
                if (translationMatch == -1 && !string.IsNullOrEmpty(item.Translation)
                    && item.Translation.Contains(text, StringComparison.OrdinalIgnoreCase))
                    translationMatch = i;
                if (aliasMatch == -1 && aliasTags != null && aliasTags.Contains(tag))
                    aliasMatch = i;
            }
            if (containsMatch != -1)
                return containsMatch;
            if (translationMatch != -1)
                return translationMatch;
            return aliasMatch;
        }

        private bool CheckFilterOnTag(AllTagsItem tagsItem, string filter)
        {
            if (filterText == string.Empty)
                return true;
            if (tagsItem.Tag.Contains(filter))
                return true;
            return false;
        }

        public void SetFilterByCount(int tagsCount)
        {
            filterTagsCount = tagsCount;
            foreach (var item in tagsList)
            {
                if (item.Count == tagsCount)
                {
                    if (!List.Contains(item))
                        AddWithSortingInternal(List, item);
                }
                else
                {
                    if (List.Contains(item))
                        List.Remove(item);
                }
            }
            filterByCount = true;
        }

        public void UpdateFilter()
        {
            if (filterByCount)
            {
                SetFilterByCount(filterTagsCount);
            }
            else
            {
                foreach (var item in tagsList)
                {
                    if (CheckFilterOnTag(item, filterText))
                    {
                        if (!List.Contains(item))
                            AddWithSortingInternal(List, item);
                    }
                    else
                    {
                        if (List.Contains(item))
                            List.Remove(item);
                    }
                }
            }
        }

        protected virtual void OnListChanged(ListChangedEventArgs ev)
        {
            if (batchUpdateDepth > 0)
            {
                batchDirty = true;
                return;
            }
            if (onListChanged != null)
            {
                onListChanged(this, ev);
            }
        }

        protected override void OnClearComplete()
        {
            OnListChanged(resetEvent);
        }

        protected override void OnInsertComplete(int index, object value)
        {
            AllTagsItem c = (AllTagsItem)value;
            c.Parent = this;
            OnListChanged(new ListChangedEventArgs(ListChangedType.ItemAdded, index));
        }

        protected override void OnRemoveComplete(int index, object value)
        {
            AllTagsItem c = (AllTagsItem)value;
            c.Parent = this;
            OnListChanged(new ListChangedEventArgs(ListChangedType.ItemDeleted, index));
        }

        protected override void OnSetComplete(int index, object oldValue, object newValue)
        {
            if (oldValue != newValue)
            {
                AllTagsItem olddata = (AllTagsItem)oldValue;
                AllTagsItem newdata = (AllTagsItem)newValue;

                olddata.Parent = null;
                newdata.Parent = this;

                OnListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, index));
            }
        }

        internal void AllTagsItemChanged(AllTagsItem tag)
        {
            int index = List.IndexOf(tag);
            OnListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, index));
        }

        // Implements IBindingList.
        bool IBindingList.AllowEdit
        {
            get { return true; }
        }

        bool IBindingList.AllowNew
        {
            get { return true; }
        }

        bool IBindingList.AllowRemove
        {
            get { return true; }
        }

        bool IBindingList.SupportsChangeNotification
        {
            get { return true; }
        }

        bool IBindingList.SupportsSearching
        {
            get { return false; }
        }

        bool IBindingList.SupportsSorting
        {
            get { return true; }
        }

        // Events.
        public event ListChangedEventHandler ListChanged
        {
            add
            {
                onListChanged += value;
            }
            remove
            {
                onListChanged -= value;
            }
        }

        // Methods.
        object IBindingList.AddNew()
        {
            AllTagsItem c = new AllTagsItem();
            List.Add(c);
            return c;
        }

        void IBindingListView.RemoveFilter()
        {
            filterText = string.Empty;
            filterByCount = false;
            UpdateFilter();
        }

        //private int GetNextId()
        //{
        //    if (List.Count == 0)
        //        return 0;
        //    int index = 0;
        //    for (int i = 0; i < this.Count; i++)
        //    {
        //        if (((EditableTag)List[i]).Id > index)
        //            index = ((EditableTag)List[i]).Id;
        //    }
        //    return index + 1;
        //}

        // Unsupported properties.
        bool IBindingList.IsSorted
        {
            get { return _isSorted; }
        }

        ListSortDirection IBindingList.SortDirection
        {
            get { return _sortDirection; }
        }

        PropertyDescriptor IBindingList.SortProperty
        {
            get { return _propertyDescriptor; }
        }

        public string Filter
        {
            get
            {
                return filterText;
            }
            set
            {
                filterText = value;
                UpdateFilter();
            }
        }

        public ListSortDescriptionCollection SortDescriptions
        {
            get { return sortDescriptions; }
        }

        public bool SupportsAdvancedSorting
        {
            get { return false; }
        }

        public bool SupportsFiltering
        {
            get { return true; }
        }

        // Unsupported Methods.
        void IBindingList.AddIndex(PropertyDescriptor property)
        {
            throw new NotSupportedException();
        }

        void IBindingList.ApplySort(PropertyDescriptor property, ListSortDirection direction)
        {
            _propertyDescriptor = property;
            _sortDirection = direction;
            _isSorted = true;
            if (property.Name == "Tag")
            {
                if (direction == ListSortDirection.Ascending)
                {
                    this.InnerList.Sort(new SortByTagNameAscending());
                    this.tagsList.Sort(new SortByTagNameTIAscending());
                }
                else
                {
                    this.InnerList.Sort(new SortByTagNameDescending());
                    this.tagsList.Sort(new SortByTagNameTIDescending());
                }
            }
            else if (property.Name == "Count")
            {
                if (direction == ListSortDirection.Ascending)
                {
                    this.InnerList.Sort(new SortByCountAscending());
                    this.tagsList.Sort(new SortByCountTIAscending());
                }
                else
                {
                    this.InnerList.Sort(new SortByCountDescending());
                    this.tagsList.Sort(new SortByCountTIDescending());
                }
            }
            else
                { throw new NotSupportedException(); }
            OnListChanged(resetEvent);
        }

        private sealed class BatchUpdateScope : IDisposable
        {
            private AllTagsList owner;

            public BatchUpdateScope(AllTagsList owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                var current = owner;
                owner = null;
                current?.EndBatchUpdate();
            }
        }

        int IBindingList.Find(PropertyDescriptor property, object key)
        {
            throw new NotSupportedException();
        }

        void IBindingList.RemoveIndex(PropertyDescriptor property)
        {
            throw new NotSupportedException();
        }

        void IBindingList.RemoveSort()
        {
            throw new NotSupportedException();
        }

        public void ApplySort(ListSortDescriptionCollection sorts)
        {
            throw new NotImplementedException();
        }

        //public void RemoveFilter()
        //{
        //    throw new NotImplementedException();
        //}

        //public object Clone()
        //{
        //    EditableTagList eTagList = new EditableTagList();
        //    foreach (AllTagsItem item in List)
        //    {
        //        eTagList.Add((AllTagsItem)item.Clone(), false);
        //    }
        //    return eTagList;
        //}

        private class SortByTagNameAscending : IComparer
        {
            int IComparer.Compare(object x, object y)
            {
                AllTagsItem t1 = (AllTagsItem)x;
                AllTagsItem t2 = (AllTagsItem)y;
                return t1.Tag.CompareTo(t2.Tag);
            }
        }

        private class SortByTagNameDescending : IComparer
        {
            int IComparer.Compare(object x, object y)
            {
                AllTagsItem t1 = (AllTagsItem)x;
                AllTagsItem t2 = (AllTagsItem)y;
                return t2.Tag.CompareTo(t1.Tag);
            }
        }

        private class SortByTagNameTIAscending : IComparer<AllTagsItem>
        {
            int IComparer<AllTagsItem>.Compare(AllTagsItem x, AllTagsItem y)
            {
                return x.Tag.CompareTo(y.Tag);
            }
        }

        private class SortByTagNameTIDescending : IComparer<AllTagsItem>
        {
            int IComparer<AllTagsItem>.Compare(AllTagsItem x, AllTagsItem y)
            {
                return y.Tag.CompareTo(x.Tag);
            }
        }

        private class SortByCountAscending : IComparer
        {
            int IComparer.Compare(object x, object y)
            {
                AllTagsItem t1 = (AllTagsItem)x;
                AllTagsItem t2 = (AllTagsItem)y;
                var result = t1.Count.CompareTo(t2.Count);
                if(result == 0)
                    return t1.Tag.CompareTo(t2.Tag);
                return result;
            }
        }

        private class SortByCountDescending : IComparer
        {
            int IComparer.Compare(object x, object y)
            {
                AllTagsItem t1 = (AllTagsItem)x;
                AllTagsItem t2 = (AllTagsItem)y;
                var result = t2.Count.CompareTo(t1.Count);
                if (result == 0)
                    return t1.Tag.CompareTo(t2.Tag);
                return result;
            }
        }

        private class SortByCountTIAscending : IComparer<AllTagsItem>
        {
            int IComparer<AllTagsItem>.Compare(AllTagsItem x, AllTagsItem y)
            {
                var result = x.Count.CompareTo(y.Count);
                if (result == 0)
                    return x.Tag.CompareTo(y.Tag);
                return result;
            }
        }

        private class SortByCountTIDescending : IComparer<AllTagsItem>
        {
            int IComparer<AllTagsItem>.Compare(AllTagsItem x, AllTagsItem y)
            {
                var result = y.Count.CompareTo(x.Count);
                if (result == 0)
                    return x.Tag.CompareTo(y.Tag);
                return result;
            }
        }
    }
}
