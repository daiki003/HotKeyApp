using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class SettingsRecordListControl : UserControl, ISettingsRecordListNavigator
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SettingsRecordListControl), new PropertyMetadata(null));

        public SettingsRecordListControl()
        {
            InitializeComponent();
        }

        public IEnumerable? ItemsSource
        {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public event ActionValueRecordActivatedEventHandler? ActionRecordActivated;

        public void FocusFirstRecord()
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                FocusRecordElement(FindFirstRecordElement(this));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public void FocusLastRecord()
        {
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                FocusRecordElement(FindLastRecordElement(this));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public void FocusRecordAfterAppend()
        {
            FocusLastRecord();
        }

        public bool IsFocusInEditor()
        {
            return Keyboard.FocusedElement is DependencyObject focusedElement && IsDescendantOf(focusedElement, this);
        }

        public bool TryHandleKeyboardShortcut(KeyEventArgs e)
        {
            if (!IsFocusInEditor() || Keyboard.Modifiers != ModifierKeys.None)
            {
                return false;
            }

            if (Keyboard.FocusedElement is TextBoxBase)
            {
                return false;
            }

            switch (e.Key)
            {
                case Key.Home:
                    FocusFirstRecord();
                    return true;
                case Key.End:
                    FocusLastRecord();
                    return true;
                case Key.Up:
                    return FocusRelativeFromFocusedRecord(-1);
                case Key.Down:
                    return FocusRelativeFromFocusedRecord(1);
            }

            return false;
        }

        public bool TryFocusHeaderFromEditor()
        {
            return false;
        }

        private void ActionValueRecordControl_Activated(object sender, ViewModels.Settings.ActionValueRecordViewModel item)
        {
            ActionRecordActivated?.Invoke(sender, item);
        }

        private static FrameworkElement? FindFirstRecordElement(DependencyObject root)
        {
            int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is EditableValueRecordControl or ActionValueRecordControl)
                {
                    return (FrameworkElement)child;
                }

                FrameworkElement? nested = FindFirstRecordElement(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static FrameworkElement? FindLastRecordElement(DependencyObject root)
        {
            FrameworkElement? last = null;
            int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is EditableValueRecordControl or ActionValueRecordControl)
                {
                    last = (FrameworkElement)child;
                }

                FrameworkElement? nested = FindLastRecordElement(child);
                if (nested != null)
                {
                    last = nested;
                }
            }

            return last;
        }

        private static void FocusRecordElement(FrameworkElement? recordElement)
        {
            switch (recordElement)
            {
                case EditableValueRecordControl editableRecord:
                    editableRecord.FocusRowButton();
                    break;
                case ActionValueRecordControl actionRecord:
                    actionRecord.FocusRowButton();
                    break;
            }
        }

        private bool FocusRelativeFromFocusedRecord(int offset)
        {
            if (Keyboard.FocusedElement is not DependencyObject focusedElement)
            {
                return false;
            }

            FrameworkElement[] recordElements = FindRecordElements(this);
            int currentIndex = Array.FindIndex(recordElements, element => IsDescendantOf(focusedElement, element));
            if (currentIndex < 0)
            {
                return false;
            }

            int targetIndex = currentIndex + offset;
            if (targetIndex < 0)
            {
                targetIndex = recordElements.Length - 1;
            }
            else if (targetIndex >= recordElements.Length)
            {
                targetIndex = 0;
            }

            FocusRecordElement(recordElements[targetIndex]);
            return true;
        }

        private static FrameworkElement[] FindRecordElements(DependencyObject root)
        {
            var records = new System.Collections.Generic.List<FrameworkElement>();
            CollectRecordElements(root, records);
            return records.ToArray();
        }

        private static void CollectRecordElements(DependencyObject root, System.Collections.Generic.ICollection<FrameworkElement> records)
        {
            int childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childrenCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is EditableValueRecordControl or ActionValueRecordControl)
                {
                    records.Add((FrameworkElement)child);
                }

                CollectRecordElements(child, records);
            }
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
        {
            DependencyObject? current = child;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }
    }
}
