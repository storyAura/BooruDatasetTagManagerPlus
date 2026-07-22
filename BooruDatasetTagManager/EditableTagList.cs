using Manina.Windows.Forms;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static BooruDatasetTagManager.DatasetManager;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace BooruDatasetTagManager
{
    public class EditableTagList : CollectionBase, IBindingList, ICloneable
    {
        public delegate void TagsListChangedHandler(object sender, string oldTag, string newTag, ListChangedType changedType);
        public event TagsListChangedHandler TagsListChanged;
        private ListChangedEventArgs resetEvent = new ListChangedEventArgs(ListChangedType.Reset, -1);
        private ListChangedEventHandler onListChanged;

        private List<EditableTagHistory> History = new List<EditableTagHistory>();
        private int HistoryPosition = 0;
        private bool isStoreHistory = true;
        private bool suppressTagsListChanged;
        private readonly object translationSync = new object();
        private Task translationTask = Task.CompletedTask;

        private List<string> _tags;

        // Monotonic id source for GetNextId(). Bumped in OnInsert whenever a tag
        // enters the list (including undo/clone paths that carry their own ids),
        // replacing the old O(n) max-scan per insert.
        private int _nextId;

        // Count-map mirror of _tags membership for O(1) Contains (tags may repeat,
        // so we track occurrence counts rather than a plain HashSet). Kept in sync
        // at every _tags mutation point (OnInsert/OnRemove/OnClearComplete/OnSetComplete).
        private readonly Dictionary<string, int> _tagCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public List<string> TextTags { get { return _tags; } }

        // Image path of the DataItem that owns this list (null when detached).
        // DatasetManager uses it to keep the folder-scoped AllTags view in sync
        // only for items inside the active folder scope.
        public string OwnerImagePath { get; set; }

        private void TagCountAdd(string tag)
        {
            if (tag == null)
                return;
            _tagCounts.TryGetValue(tag, out int c);
            _tagCounts[tag] = c + 1;
        }

        private void TagCountRemove(string tag)
        {
            if (tag == null)
                return;
            if (_tagCounts.TryGetValue(tag, out int c))
            {
                if (c <= 1)
                    _tagCounts.Remove(tag);
                else
                    _tagCounts[tag] = c - 1;
            }
        }

        public List<EditableTagHistory> HistoryForDebug { get { return History; } }

        public EditableTag this[int index]
        {
            get
            {
                return (EditableTag)(List[index]);
            }
            set
            {
                List[index] = value;
            }
        }

        public EditableTagList(IEnumerable<string> tags) : base()
        {
            isStoreHistory = false;
            _tags = new List<string>();
            AddRange(tags, false);
            isStoreHistory = true;
        }

        //public EditableTagList(IEnumerable<PromptParser.PromptItem> tags) : base()
        //{
        //    isStoreHistory = false;
        //    _tags = new List<string>();
        //    foreach (var tag in tags)
        //    {
        //        int index = GetNextId();
        //        var eTag = new EditableTag(index, tag.Text, index);
        //        eTag.Weight = tag.Weight;
        //        Add(eTag, false);
        //    }
        //    isStoreHistory = true;
        //}

        public EditableTagList() : base()
        {
            _tags = new List<string>();
        }

        internal void ClearWithoutTagNotifications()
        {
            bool previousHistory = isStoreHistory;
            isStoreHistory = false;
            suppressTagsListChanged = true;
            try
            {
                Clear();
            }
            finally
            {
                suppressTagsListChanged = false;
                isStoreHistory = previousHistory;
            }
        }

        public void LoadFromPromptParserData(IEnumerable<PromptParser.PromptItem> tags)
        {
            isStoreHistory = false;
            _tags = new List<string>();
            _tagCounts.Clear();
            foreach (var tag in tags)
            {
                int index = GetNextId();
                var eTag = new EditableTag(index, tag.Text, index);
                eTag.Weight = tag.Weight;
                Add(eTag, false);
            }
            isStoreHistory = true;
        }

        public override string ToString()
        {
            //DeduplicateTags(); //Not need??
            List<string> tempTagList = new List<string>();
            for (int i = 0; i < List.Count; i++)
            {
                tempTagList.Add(List[i].ToString());
            }
            string fixedSeparator = Program.Settings.SeparatorOnSave.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
            return string.Join(fixedSeparator, tempTagList);
        }


        /// <summary>
        /// Undo
        /// </summary>
        public void PrevState()
        {
            if (HistoryPosition == 0)
                return;
            HistoryPosition--;
            //cancel last state
            var curHistory = History[HistoryPosition];
            if (curHistory.Type == EditableTagHistory.HistoryType.Add)
            {
                isStoreHistory = false;
                RemoveAt(curHistory.Index);
                isStoreHistory = true;
            }
            else if (curHistory.Type == EditableTagHistory.HistoryType.Remove)
            {
                Insert(curHistory.Index, curHistory.TagOld, false);
            }
            else if (curHistory.Type == EditableTagHistory.HistoryType.Modify)
            {
                List[curHistory.Index] = curHistory.TagOld;
                OnListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, curHistory.Index));
            }
            else if (curHistory.Type == EditableTagHistory.HistoryType.Move)
            {
                EditableTag tagToMove = curHistory.TagOld;
                tagToMove.Parent = this;
                isStoreHistory = false;
                RemoveAt(curHistory.Index);
                Insert(curHistory.OldIndex, tagToMove, false);
                isStoreHistory = true;
            }
            else if (curHistory.Type == EditableTagHistory.HistoryType.Clear)
            {
                isStoreHistory = false;
                List.Clear();
                foreach (var item in curHistory.ClearedTags)
                {
                    Add(item, false);
                }
                isStoreHistory = true;
            }
            else if (curHistory.Type == EditableTagHistory.HistoryType.Sort)
            {
                isStoreHistory = false;
                List.Clear();
                foreach (var item in curHistory.ClearedTags)
                {
                    Add(item, false);
                }
                isStoreHistory = true;
            }
        }
        /// <summary>
        /// Redo
        /// </summary>
        public void NextState()
        {
            if (HistoryPosition == History.Count)
                return;
            var curHistory = History[HistoryPosition];
            if (curHistory.Type == EditableTagHistory.HistoryType.Remove)
            {
                isStoreHistory = false;
                RemoveAt(curHistory.Index);
                isStoreHistory = true;
            }
            else if (curHistory.Type == EditableTagHistory.HistoryType.Add)
            {
                Insert(curHistory.Index, curHistory.TagOld, false);
            }
            else if (curHistory.Type == EditableTagHistory.HistoryType.Modify)
            {
                List[curHistory.Index] = curHistory.TagNew;
                OnListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, curHistory.Index));
            }
            else if (curHistory.Type == EditableTagHistory.HistoryType.Move)
            {
                EditableTag tagToMove = curHistory.TagOld;
                tagToMove.Parent = this;
                isStoreHistory = false;
                RemoveAt(curHistory.OldIndex);
                Insert(curHistory.Index, tagToMove, false);
                isStoreHistory = true;
            }
            else if (curHistory.Type == EditableTagHistory.HistoryType.Clear)
            {
                isStoreHistory = false;
                List.Clear();
                isStoreHistory = true;
            }
            else if (curHistory.Type == EditableTagHistory.HistoryType.Sort)
            {
                isStoreHistory = false;
                List.Clear();
                foreach (var item in curHistory.AddedTags)
                {
                    Add(item, false);
                }
                isStoreHistory = true;
            }
            HistoryPosition++;
        }

        /// <summary>
        /// For debug, сhecking list synchronization
        /// </summary>
        /// <returns></returns>
        public bool CheckSyncLists()
        {
            if(_tags.Count!=List.Count)
                return false;
            for (int i = 0; i < List.Count; i++)
            {
                if(_tags[i]!=((EditableTag)List[i]).Tag)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Removing empty tags and duplicates
        /// </summary>
        public void DeduplicateTags()
        {
            Program.EditableTagListLocker.Wait();
            try
            {
                isStoreHistory = false;
                // Single pass: keep the first occurrence of every tag, drop empty
                // tags and later duplicates (was an O(n²) rescan per tag).
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                List<int> indexesToRemove = new List<int>();
                for (int i = 0; i < List.Count; i++)
                {
                    string tag = ((EditableTag)List[i]).Tag;
                    if (string.IsNullOrWhiteSpace(tag) || !seen.Add(tag))
                        indexesToRemove.Add(i);
                }
                for (int i = indexesToRemove.Count - 1; i >= 0; i--)
                    RemoveAt(indexesToRemove[i]);
            }
            finally
            {
                isStoreHistory = true;
                Program.EditableTagListLocker.Release();
            }
        }


        public bool Contains(string tag)
        {
            // O(1) membership check via the count-map mirror (was O(n) linear scan).
            if (tag == null)
                return false;
            return _tagCounts.ContainsKey(tag);
        }

        public int IndexOf(string tag)
        {
            for (int i = 0; i < List.Count; i++)
            {
                if (((EditableTag)List[i]).Tag == tag)
                    return i;
            }
            return -1;
        }

        public List<int> IndexOfAll(string tag, int startIndex, int count)
        {
            List<int> list = new List<int>();
            for (int i = startIndex; i < count && i < List.Count; i++)
            {
                if (((EditableTag)List[i]).Tag == tag)
                    list.Add(i);
            }
            return list;
        }


        public void AddTag(string tag, bool storeHistory)
        {
            Add(new EditableTag(GetNextId(), tag), storeHistory);
        }

        public (int oldIndex, int newIndex) AddTag(string tag, bool skipExist, AddingType addType, int pos = -1)
        {
            int tagIndex = IndexOf(tag);
            if (skipExist && tagIndex != -1)
                return (tagIndex, tagIndex);
            int localCount = Count;
            if (tagIndex != -1)
            {
                switch (addType)
                {
                    case AddingType.Top:
                        {
                            Move(tagIndex, 0);
                            return (tagIndex, 0);
                        }
                    case AddingType.Center:
                        {
                            Move(tagIndex, localCount / 2);
                            return (tagIndex, localCount / 2);
                        }
                    case AddingType.Down:
                        {
                            Move(tagIndex, localCount - 1);
                            return (tagIndex, localCount - 1);
                        }
                    case AddingType.Custom:
                        {
                            if (pos >= localCount)
                            {
                                pos = localCount - 1;
                            }
                            else if (pos < 0)
                            {
                                pos = 0;
                            }
                            Move(tagIndex, pos);
                            return (tagIndex, pos);
                        }
                }
            }
            else
            {
                switch (addType)
                {
                    case AddingType.Top:
                        {
                            InsertTag(0, tag, true);
                            return (tagIndex, 0);
                        }
                    case AddingType.Center:
                        {
                            InsertTag(localCount / 2, tag, true);
                            return (tagIndex, localCount / 2);
                        }
                    case AddingType.Down:
                        {
                            AddTag(tag, true);
                            return (tagIndex, localCount);
                        }
                    case AddingType.Custom:
                        {
                            if (pos >= localCount)
                            {
                                AddTag(tag, true);
                                return (tagIndex, localCount);
                            }
                            else if (pos < 0)
                            {
                                InsertTag(0, tag, true);
                                return (tagIndex, 0);
                            }
                            else
                            {
                                InsertTag(pos, tag, true);
                                return (tagIndex, pos);
                            }
                        }
                }
            }
            return (tagIndex, -1);
        }

        public int Add(EditableTag item, bool storeHistory)
        {
            isStoreHistory = storeHistory;
            item.Parent = this;
            int res = List.Add(item);
            isStoreHistory = true;
            return res;
        }

        public void Insert(int index, EditableTag item, bool storeHistory)
        {
            isStoreHistory = storeHistory;
            item.Parent = this;
            List.Insert(index, item);
            isStoreHistory = true;
        }

        public void AddRange(IEnumerable<string> tags, bool storeHistory)
        {
            foreach (string tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                    continue;
                int index = GetNextId();
                Add(new EditableTag(index, tag.ToLower().Trim(), index), storeHistory);
            }
        }

        public void AddRange(IEnumerable<PromptParser.PromptItem> tags, bool storeHistory)
        {
            foreach (var tag in tags)
            {
                int index = GetNextId();
                var eTag = new EditableTag(index, tag.Text, index);
                eTag.Weight = tag.Weight;
                Add(eTag, storeHistory);
            }
        }

        public void ReplaceTag(string oldTag, string newTag)
        {
            if (oldTag == newTag)
                return;
            int index = IndexOf(oldTag);
            if (index != -1)
            {
                int dstIndex = IndexOf(newTag);
                if (dstIndex == -1)
                {
                    ((EditableTag)List[index]).Tag = newTag;
                    ((EditableTag)List[index]).Weight = 1;
                }
                else
                {
                    RemoveAt(index);
                }
            }
        }

        public object AddNew()
        {
            return (EditableTag)((IBindingList)this).AddNew();
        }

        public object InsertNew(int index)
        {
            List.Insert(index, new EditableTag(GetNextId(), ""));
            return List[index];
        }

        public void InsertTag(int index, string tag, bool storeHistory)
        {
            Insert(index, new EditableTag(GetNextId(), tag), storeHistory);
        }

        public void Remove(EditableTag value, bool storeHistory)
        {
            isStoreHistory = storeHistory;
            List.Remove(value);
            isStoreHistory = true;
        }

        public void RemoveTag(string tag, bool storeHistory)
        {
            RemoveTags(new[] { tag }, storeHistory);
        }

        public void RemoveTags(IEnumerable<string> tags, bool storeHistory)
        {
            if (tags == null)
                throw new ArgumentNullException(nameof(tags));

            var tagsToRemove = new HashSet<string>(
                tags.Where(tag => !string.IsNullOrWhiteSpace(tag)),
                StringComparer.Ordinal);

            if (tagsToRemove.Count == 0)
                return;

            bool previousStoreHistory = isStoreHistory;
            isStoreHistory = storeHistory;
            try
            {
                for (int i = Count - 1; i >= 0; i--)
                {
                    if (tagsToRemove.Contains(((EditableTag)List[i]).Tag))
                        RemoveAt(i);
                }
            }
            finally
            {
                isStoreHistory = previousStoreHistory;
            }
        }

        public int Move(int index, int toIndex)
        {
            if (index < 0 || index > Count - 1)
                throw new IndexOutOfRangeException();

            EditableTag tagToMove = (EditableTag)((EditableTag)List[index]).Clone();
            tagToMove.Parent = this;
            isStoreHistory = false;
            RemoveAt(index);
            if (toIndex < 0 || toIndex > Count)
            {
                toIndex = Count;
            }
            Insert(toIndex, tagToMove, false);
            isStoreHistory = true;
            var h = new EditableTagHistory();
            h.Index = toIndex;
            h.OldIndex = index;
            h.TagOld = (EditableTag)(tagToMove).Clone();
            h.Type = EditableTagHistory.HistoryType.Move;
            AddHistory(h);
            return toIndex;
        }

        public void Sort(int skipFirstCount = 0)
        {
            SortCore(skipFirstCount, new SortEditableTagListAscending(), new SortStringAscending());
        }

        /// <summary>
        /// Sorts by a semantic category rank (then alphabetically within one
        /// rank), honoring the same "don't sort the first N rows" prefix as
        /// <see cref="Sort"/>. The rank comes in as a delegate so this class
        /// stays free of UI/Program dependencies (it is test-linked).
        /// </summary>
        public void SortByCategory(int skipFirstCount, Func<string, int> rankOf)
        {
            if (rankOf == null)
                throw new ArgumentNullException(nameof(rankOf));
            SortCore(skipFirstCount, new SortEditableTagByRank(rankOf), new SortStringByRank(rankOf));
        }

        private void SortCore(int skipFirstCount, IComparer tagComparer, IComparer<string> textComparer)
        {
            skipFirstCount = Math.Clamp(skipFirstCount, 0, InnerList.Count);
            var h = new EditableTagHistory();
            h.Index = 0;
            foreach (EditableTag c in List)
            {
                var clonedETag = (EditableTag)c.Clone();
                clonedETag.Parent = null;
                h.ClearedTags.Add(clonedETag);
            }
            h.Type = EditableTagHistory.HistoryType.Sort;
            InnerList.Sort(skipFirstCount, InnerList.Count - skipFirstCount, tagComparer);
            _tags.Sort(skipFirstCount, _tags.Count - skipFirstCount, textComparer);
            foreach (EditableTag c in List)
            {
                var clonedETag = (EditableTag)c.Clone();
                clonedETag.Parent = null;
                h.AddedTags.Add(clonedETag);
            }
            if (isStoreHistory)
                AddHistory(h);
            OnListChanged(resetEvent);
        }

        public void EndEdit()
        {
            for (int i = 0; i < List.Count; i++)
            {
                var eTag = (EditableTag)List[i];
                if (eTag.IsEditing)
                    eTag.EndEdit();
            }
        }

        public void EndEdit(int rowIndex)
        {
            var eTag = (EditableTag)List[rowIndex];
            if (eTag.IsEditing)
                eTag.EndEdit();
        }

        public bool IsEditMode()
        {
            for (int i = 0; i < List.Count; i++)
            {
                var eTag = (EditableTag)List[i];
                if (eTag.IsEditing)
                    return true;
            }
            return false;
        }


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
            bool previousStoreHistory = isStoreHistory;
            var attempted = new HashSet<(EditableTag Item, string Tag)>();
            isStoreHistory = false;
            try
            {
                while (true)
                {
                    var pending = List.Cast<EditableTag>()
                        .Where(item => string.IsNullOrEmpty(item.Translation))
                        .Select(item => (Item: item, Tag: item.Tag))
                        .Where(entry => attempted.Add(entry))
                        .ToList();

                    if (pending.Count == 0)
                        return;

                    foreach (var entry in pending)
                    {
                        string result;
                        try
                        {
                            result = await translateAsync(entry.Tag) ?? string.Empty;
                        }
                        catch
                        {
                            result = string.Empty;
                        }
                        if (entry.Item.Parent == this
                            && entry.Item.Tag == entry.Tag
                            && List.Contains(entry.Item))
                        {
                            entry.Item.Translation = result;
                        }
                    }
                }
            }
            finally
            {
                isStoreHistory = previousStoreHistory;
            }
        }

        protected virtual void OnListChanged(ListChangedEventArgs ev)
        {
            if (onListChanged != null)
            {
                onListChanged(this, ev);
            }
            if (!CheckSyncLists())
            {
                CreateDataForDebug();
                throw new InvalidAsynchronousStateException("List desynchronization detected!\nPlease post the file \""+Path.Combine(Program.AppPath, "ErrorData.json") +"\" in the topic\nhttps://github.com/storyAura/BooruDatasetTagManagerPlus/discussions");
            }
        }

        private void CreateDataForDebug()
        {
            ListsDebugInfo info = new ListsDebugInfo();
            for (int i = 0; i < InnerList.Count; i++)
            {
                string tag = ((EditableTag)InnerList[i]).Tag == null ? "NULL" : ((EditableTag)InnerList[i]).Tag;
                info.EditableList.Add(tag);
            }
            for (int i = 0; i < _tags.Count; i++)
            {
                string tag = _tags[i] == null ? "NULL" : _tags[i];
                info.TextList.Add(tag);
            }
            info.History = History;
            File.WriteAllText("ErrorData.json", JsonConvert.SerializeObject(info, Formatting.Indented));
        }

        public class ListsDebugInfo
        {
            public List<string> EditableList;
            public List<string> TextList;
            public List<EditableTagHistory> History;

            public ListsDebugInfo()
            {
                EditableList = new List<string>();
                TextList = new List<string>();
                History = new List<EditableTagHistory>();
            }

        }

        protected override void OnClear()
        {
            var h = new EditableTagHistory();
            h.Index = 0;
            foreach (EditableTag c in List)
            {
                var clonedETag = (EditableTag)c.Clone();
                clonedETag.Parent = null;
                h.ClearedTags.Add(clonedETag);
            }
            h.Type = EditableTagHistory.HistoryType.Clear;
            if (isStoreHistory)
                AddHistory(h);
        }


        protected override void OnClearComplete()
        {
            if (!suppressTagsListChanged)
            {
                foreach (var item in _tags)
                    TagsListChanged?.Invoke(this, null, item, ListChangedType.ItemDeleted);
            }
            _tags.Clear();
            _tagCounts.Clear();
            _nextId = 0;
            OnListChanged(resetEvent);
        }

        protected override void OnInsertComplete(int index, object value)
        {
            EditableTag c = (EditableTag)value;
            c.Parent = this;
            OnListChanged(new ListChangedEventArgs(ListChangedType.ItemAdded, index));
        }

        protected override void OnInsert(int index, object value)
        {
            if (isStoreHistory)
            {
                var h = new EditableTagHistory();
                h.Index = index;
                h.TagOld = (EditableTag)((EditableTag)value).Clone();
                h.Type = EditableTagHistory.HistoryType.Add;
                AddHistory(h);
            }
            _nextId = Math.Max(_nextId, ((EditableTag)value).Id + 1);
            string insertedTag = ((EditableTag)value).Tag;
            _tags.Insert(index, insertedTag);
            TagCountAdd(insertedTag);
            if (!suppressTagsListChanged)
                TagsListChanged?.Invoke(this, null, insertedTag, ListChangedType.ItemAdded);
            base.OnInsert(index, value);
        }

        protected override void OnRemove(int index, object value)
        {
            if (isStoreHistory)
            {
                var h = new EditableTagHistory();
                h.Index = index;
                h.TagOld = (EditableTag)((EditableTag)value).Clone();
                h.Type = EditableTagHistory.HistoryType.Remove;
                AddHistory(h);
            }
            if (!suppressTagsListChanged)
                TagsListChanged?.Invoke(this, null, _tags[index], ListChangedType.ItemDeleted);
            TagCountRemove(_tags[index]);
            _tags.RemoveAt(index);
            base.OnRemove(index, value);
        }

        private void AddHistory(EditableTagHistory his)
        {
            if (HistoryPosition != History.Count)
            {
                while (History.Count != HistoryPosition)
                {
                    History.RemoveAt(History.Count - 1);
                }
            }
            History.Add(his);
            HistoryPosition++;
        }

        protected override void OnRemoveComplete(int index, object value)
        {
            EditableTag c = (EditableTag)value;
            c.Parent = this;
            OnListChanged(new ListChangedEventArgs(ListChangedType.ItemDeleted, index));
        }

        protected override void OnSet(int index, object oldValue, object newValue)
        {
            base.OnSet(index, oldValue, newValue);
        }

        protected override void OnSetComplete(int index, object oldValue, object newValue)
        {
            if (oldValue != newValue)
            {

                EditableTag olddata = (EditableTag)oldValue;
                EditableTag newdata = (EditableTag)newValue;

                olddata.Parent = null;
                newdata.Parent = this;
                string oldTagText = _tags[index];
                _tags[index] = newdata.Tag;
                if (oldTagText != newdata.Tag)
                {
                    TagCountRemove(oldTagText);
                    TagCountAdd(newdata.Tag);
                    TagsListChanged?.Invoke(this, oldTagText, newdata.Tag, ListChangedType.ItemChanged);
                }
                OnListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, index));
            }
        }

        // Called by EditableTag when it changes.
        internal void EditableTagChanged(EditableTag tag, bool storeHistory)
        {

            int index = List.IndexOf(tag);
            if (_tags[index] != tag.Tag)
                TagsListChanged?.Invoke(this, _tags[index], tag.Tag, ListChangedType.ItemChanged);
            _tags[index] = tag.Tag;
            if (storeHistory)
            {
                var h = new EditableTagHistory();
                h.Index = index;
                h.TagOld = tag.GetEditableTagFromBackup();
                h.TagNew = (EditableTag)tag.Clone();
                h.Type = EditableTagHistory.HistoryType.Modify;
                AddHistory(h);
            }
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
            get { return false; }
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
            EditableTag c = new EditableTag(GetNextId(), "");
            List.Add(c);
            return c;
        }

        private int GetNextId()
        {
            if (List.Count == 0)
                return 0;
            return _nextId;
        }

        // Unsupported properties.
        bool IBindingList.IsSorted
        {
            get { throw new NotSupportedException(); }
        }

        ListSortDirection IBindingList.SortDirection
        {
            get { throw new NotSupportedException(); }
        }

        PropertyDescriptor IBindingList.SortProperty
        {
            get { throw new NotSupportedException(); }
        }

        // Unsupported Methods.
        void IBindingList.AddIndex(PropertyDescriptor property)
        {
            throw new NotSupportedException();
        }

        void IBindingList.ApplySort(PropertyDescriptor property, ListSortDirection direction)
        {
            throw new NotSupportedException();
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

        public object Clone()
        {
            EditableTagList eTagList = new EditableTagList();
            foreach (EditableTag item in List)
            {
                eTagList.Add((EditableTag)item.Clone(), false);
            }
            return eTagList;
        }

        private class SortEditableTagListAscending : IComparer
        {
            int IComparer.Compare(object x, object y)
            {
                EditableTag t1 = (EditableTag)x;
                EditableTag t2 = (EditableTag)y;
                //if (!t1.Sortiable)
                //    return 1;
                return t1.Tag.CompareTo(t2.Tag);
            }
        }

        private class SortStringAscending : IComparer<string>
        {
            int IComparer<string>.Compare(string x, string y)
            {
                return (x).CompareTo(y);
            }
        }

        private sealed class SortEditableTagByRank : IComparer
        {
            private readonly Func<string, int> rankOf;

            public SortEditableTagByRank(Func<string, int> rankOf)
            {
                this.rankOf = rankOf;
            }

            int IComparer.Compare(object x, object y)
            {
                EditableTag t1 = (EditableTag)x;
                EditableTag t2 = (EditableTag)y;
                int byRank = rankOf(t1.Tag).CompareTo(rankOf(t2.Tag));
                return byRank != 0 ? byRank : t1.Tag.CompareTo(t2.Tag);
            }
        }

        private sealed class SortStringByRank : IComparer<string>
        {
            private readonly Func<string, int> rankOf;

            public SortStringByRank(Func<string, int> rankOf)
            {
                this.rankOf = rankOf;
            }

            int IComparer<string>.Compare(string x, string y)
            {
                int byRank = rankOf(x).CompareTo(rankOf(y));
                return byRank != 0 ? byRank : x.CompareTo(y);
            }
        }
    }
}
