using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HotKeyCommandApp.Views.Controls
{
    public class SettingsDialogShell : ContentControl
    {
        static SettingsDialogShell()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingsDialogShell), new FrameworkPropertyMetadata(typeof(SettingsDialogShell)));
        }

        public static readonly DependencyProperty HeaderTitleProperty =
            DependencyProperty.Register(nameof(HeaderTitle), typeof(string), typeof(SettingsDialogShell), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty HeaderActionTextProperty =
            DependencyProperty.Register(nameof(HeaderActionText), typeof(string), typeof(SettingsDialogShell), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty HeaderActionVisibilityProperty =
            DependencyProperty.Register(nameof(HeaderActionVisibility), typeof(Visibility), typeof(SettingsDialogShell), new PropertyMetadata(Visibility.Visible));

        public static readonly DependencyProperty HeaderActionCommandProperty =
            DependencyProperty.Register(nameof(HeaderActionCommand), typeof(ICommand), typeof(SettingsDialogShell), new PropertyMetadata(null));

        public string HeaderTitle
        {
            get => (string)GetValue(HeaderTitleProperty);
            set => SetValue(HeaderTitleProperty, value);
        }

        public string HeaderActionText
        {
            get => (string)GetValue(HeaderActionTextProperty);
            set => SetValue(HeaderActionTextProperty, value);
        }

        public Visibility HeaderActionVisibility
        {
            get => (Visibility)GetValue(HeaderActionVisibilityProperty);
            set => SetValue(HeaderActionVisibilityProperty, value);
        }

        public ICommand? HeaderActionCommand
        {
            get => (ICommand?)GetValue(HeaderActionCommandProperty);
            set => SetValue(HeaderActionCommandProperty, value);
        }
    }
}
