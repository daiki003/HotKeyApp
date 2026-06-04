using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HotKeyCommandApp.Views.Controls
{
    internal static class SettingsRecordNavigationHelper
    {
        public static bool FocusPrevious(Button currentButton)
        {
            return FocusRelative(currentButton, -1);
        }

        public static bool FocusNext(Button currentButton)
        {
            return FocusRelative(currentButton, 1);
        }

        public static bool FocusFirst(Button currentButton)
        {
            List<Button> buttons = FindSiblingRowButtons(currentButton);
            if (buttons.Count == 0)
            {
                return false;
            }

            buttons[0].Focus();
            return true;
        }

        public static bool FocusLast(Button currentButton)
        {
            List<Button> buttons = FindSiblingRowButtons(currentButton);
            if (buttons.Count == 0)
            {
                return false;
            }

            buttons[^1].Focus();
            return true;
        }

        private static bool FocusRelative(Button currentButton, int offset)
        {
            List<Button> buttons = FindSiblingRowButtons(currentButton);
            if (buttons.Count == 0)
            {
                return false;
            }

            int currentIndex = buttons.IndexOf(currentButton);
            if (currentIndex < 0)
            {
                return false;
            }

            int targetIndex = currentIndex + offset;
            if (targetIndex < 0)
            {
                targetIndex = buttons.Count - 1;
            }
            else if (targetIndex >= buttons.Count)
            {
                targetIndex = 0;
            }

            buttons[targetIndex].Focus();
            return true;
        }

        private static List<Button> FindSiblingRowButtons(Button currentButton)
        {
            ItemsControl? itemsControl = FindAncestor<ItemsControl>(currentButton);
            if (itemsControl == null)
            {
                return new List<Button> { currentButton };
            }

            return EnumerateDescendants(itemsControl)
                .OfType<Button>()
                .Where(button => button.Name == "RowButton")
                .ToList();
        }

        private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                yield return child;

                foreach (DependencyObject nested in EnumerateDescendants(child))
                {
                    yield return nested;
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject? current = child;
            while (current != null)
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
