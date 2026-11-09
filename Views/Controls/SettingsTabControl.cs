using System.Windows.Controls;

namespace HotKeyCommandApp.Views.Controls
{
    public class SettingsTabControl : TabControl
    {
        public bool MoveSelection(int direction)
        {
            if (Items.Count == 0)
            {
                return false;
            }

            int currentIndex = SelectedIndex < 0 ? 0 : SelectedIndex;
            int nextIndex = (currentIndex + direction + Items.Count) % Items.Count;
            if (nextIndex == SelectedIndex)
            {
                return false;
            }

            SelectedIndex = nextIndex;
            return true;
        }

        public bool MoveSelectedTab(int direction)
        {
            if (SelectedItem is not TabItem tab) return false;
            return MoveTab(tab, Math.Clamp(SelectedIndex + direction, 0, Items.Count - 1));
        }

        public bool MoveTab(TabItem tab, int targetIndex)
        {
            int sourceIndex = Items.IndexOf(tab);
            if (ItemsSource != null || sourceIndex < 0 || targetIndex < 0 || targetIndex >= Items.Count || sourceIndex == targetIndex)
                return false;

            object selectedTab = SelectedItem;
            Items.RemoveAt(sourceIndex);
            Items.Insert(targetIndex, tab);
            SelectedItem = selectedTab;
            return true;
        }
    }
}
