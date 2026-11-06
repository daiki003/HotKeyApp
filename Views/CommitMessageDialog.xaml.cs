using System.Windows;
using System.Windows.Input;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.Views
{
    public partial class CommitMessageDialog : Window
    {
        public string Message => MessageBox.Text.Trim();

        public CommitMessageDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => MessageBox.Focus();
        }

        private void CommitButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Message))
            {
                ErrorText.Text = "コミットメッセージを入力してください。";
                MessageBox.Focus();
                return;
            }
            DialogResult = true;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                if (CancelButton.IsKeyboardFocused) DialogResult = false;
                else CommitButton_Click(sender, e);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.DisableSystemMenu(this);
            WindowHelper.EnableWindowMoveShortcut(this);
            WindowHelper.EnableWindowDragMove(this);
        }
    }
}
