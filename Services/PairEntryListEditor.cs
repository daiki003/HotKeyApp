using System.Collections;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Services
{
    public static class PairEntryListEditor
    {
        public static List<IPairEntryEditable> ReadVisibleItems(IList? itemsSource, IList? folderItemsSource)
        {
            var records = itemsSource?.Cast<object>().OfType<IPairEntryEditable>().ToList() ?? new();
            var folders = folderItemsSource?.Cast<PairEntryFolder>().ToList() ?? new();
            if (records.Count == 0)
            {
                folderItemsSource?.Clear();
                return records;
            }

            if (folders.Count > 0 || records.Any(item => !string.IsNullOrEmpty(item.ParentFolderId)))
            {
                var flattened = new List<IPairEntryEditable>();
                AppendFlattenedItems(string.Empty, records, folders, flattened);
                foreach (var record in records)
                {
                    if (!flattened.Contains(record)) flattened.Add(record);
                }
                NormalizeSortOrder(flattened);
                folderItemsSource?.Clear();
            }
            return records.OrderBy(item => item.SortOrder).ToList();
        }

        public static void NormalizeSortOrder(IList<IPairEntryEditable> orderedItems)
        {
            for (int i = 0; i < orderedItems.Count; i++)
            {
                orderedItems[i].SortOrder = i;
                orderedItems[i].ParentFolderId = string.Empty;
            }
        }

        public static bool TryMove(IList<IPairEntryEditable> orderedItems, IPairEntryEditable item, int targetIndex)
        {
            int currentIndex = orderedItems.IndexOf(item);
            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= orderedItems.Count || currentIndex == targetIndex)
                return false;
            orderedItems.RemoveAt(currentIndex);
            orderedItems.Insert(targetIndex, item);
            NormalizeSortOrder(orderedItems);
            return true;
        }

        public static bool TryMoveToAdjacentTab(IPairEditorTabHost tabHost, IPairEntryEditable entry,
            bool moveRight, out IPairEditorTab? targetTab)
        {
            targetTab = null;
            if (tabHost.SelectedTab == null) return false;
            var tabs = tabHost.Tabs.Cast<object>().OfType<IPairEditorTab>().ToList();
            int currentIndex = tabs.IndexOf(tabHost.SelectedTab);
            int targetIndex = moveRight ? currentIndex + 1 : currentIndex - 1;
            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= tabs.Count) return false;

            IPairEditorTab sourceTab = tabs[currentIndex];
            int entryIndex = sourceTab.Items.IndexOf(entry);
            if (entryIndex < 0) return false;

            targetTab = tabs[targetIndex];
            sourceTab.Items.RemoveAt(entryIndex);
            NormalizeSortOrder(sourceTab.Items.Cast<object>().OfType<IPairEntryEditable>().ToList());
            entry.SortOrder = targetTab.Items.Count;
            entry.ParentFolderId = string.Empty;
            targetTab.Items.Add(entry);
            NormalizeSortOrder(targetTab.Items.Cast<object>().OfType<IPairEntryEditable>().ToList());
            return true;
        }

        private static void AppendFlattenedItems(string parentFolderId,
            IReadOnlyCollection<IPairEntryEditable> records, IReadOnlyCollection<PairEntryFolder> folders,
            ICollection<IPairEntryEditable> flattened)
        {
            var childFolders = folders.Where(folder => folder.ParentFolderId == parentFolderId).Cast<IPairEntryEditable>();
            var childRecords = records.Where(record => record.ParentFolderId == parentFolderId);
            foreach (var item in childFolders.Concat(childRecords).OrderBy(item => item.SortOrder).ThenBy(item => item.IsFolder ? 0 : 1))
            {
                if (item is PairEntryFolder folder)
                    AppendFlattenedItems(folder.Id, records, folders, flattened);
                else
                    flattened.Add(item);
            }
        }
    }
}
