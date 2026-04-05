using System;
using System.Windows;
using System.Windows.Input;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Views
{
    public partial class ShortcutAssignmentWindow : Window
    {
        public string? SelectedHotkey { get; private set; }
        public HotkeyType SelectedHotkeyType { get; private set; }

        public class HotkeyTypeOption
        {
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public HotkeyType Type { get; set; }
        }

        public ShortcutAssignmentWindow(string itemName, HotkeyType currentType = HotkeyType.WindowGlobal)
        {
            InitializeComponent();
            PromptTextBlock.Text = $"「{itemName}」に割り当てるキーを押してください";

            var options = new[]
            {
                new HotkeyTypeOption { Name = "ウィンドウ内グローバル", Description = "ウィンドウ表示時のみ有効", Type = HotkeyType.WindowGlobal },
                new HotkeyTypeOption { Name = "階層内", Description = "このボタンがある階層でのみ有効", Type = HotkeyType.HierarchyLocal },
                new HotkeyTypeOption { Name = "グローバル", Description = "常に有効", Type = HotkeyType.Global }
            };

            TypeListBox.ItemsSource = options;
            TypeListBox.SelectedIndex = (int)currentType;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key key = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
            if (key == Key.System) key = e.SystemKey;

            // Handle Escape for cancel
            if (key == Key.Escape)
            {
                this.DialogResult = false;
                this.Close();
                e.Handled = true;
                return;
            }

            // Handle Delete for clearing
            if (key == Key.Delete || key == Key.Back)
            {
                SelectedHotkey = null;
                this.DialogResult = true;
                this.Close();
                e.Handled = true;
                return;
            }

            // Handle Arrow keys for type selection
            if (key == Key.Up)
            {
                if (TypeListBox.SelectedIndex > 0) TypeListBox.SelectedIndex--;
                e.Handled = true;
                return;
            }
            if (key == Key.Down)
            {
                if (TypeListBox.SelectedIndex < TypeListBox.Items.Count - 1) TypeListBox.SelectedIndex++;
                e.Handled = true;
                return;
            }
            if (key == Key.Left || key == Key.Right)
            {
                e.Handled = true;
                return;
            }

            // Skip modifier-only presses
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            // Capture the key and type
            var parts = new System.Collections.Generic.List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) parts.Add("Win");
            
            parts.Add(key.ToString());
            SelectedHotkey = string.Join("+", parts);

            if (TypeListBox.SelectedItem is HotkeyTypeOption selectedOption)
            {
                SelectedHotkeyType = selectedOption.Type;
            }
            this.DialogResult = true;
            this.Close();
            e.Handled = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedHotkey = null;
            this.DialogResult = true;
            this.Close();
        }
    }
}
