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
    }
}
