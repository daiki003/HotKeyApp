using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class SettingsRecordButtonControl : UserControl
    {
        public static readonly DependencyProperty LabelTextProperty =
            DependencyProperty.Register(nameof(LabelText), typeof(string), typeof(SettingsRecordButtonControl), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ValueTextProperty =
            DependencyProperty.Register(nameof(ValueText), typeof(string), typeof(SettingsRecordButtonControl), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty LabelColumnWidthProperty =
            DependencyProperty.Register(nameof(LabelColumnWidth), typeof(GridLength), typeof(SettingsRecordButtonControl), new PropertyMetadata(new GridLength(140)));

        public static readonly DependencyProperty ValueColumnWidthProperty =
            DependencyProperty.Register(nameof(ValueColumnWidth), typeof(GridLength), typeof(SettingsRecordButtonControl), new PropertyMetadata(new GridLength(1, GridUnitType.Star)));

        public static readonly DependencyProperty LeadingColumnWidthProperty =
            DependencyProperty.Register(nameof(LeadingColumnWidth), typeof(GridLength), typeof(SettingsRecordButtonControl), new PropertyMetadata(new GridLength(0)));

        public static readonly DependencyProperty TrailingColumnWidthProperty =
            DependencyProperty.Register(nameof(TrailingColumnWidth), typeof(GridLength), typeof(SettingsRecordButtonControl), new PropertyMetadata(new GridLength(0)));

        public static readonly DependencyProperty LeadingContentProperty =
            DependencyProperty.Register(nameof(LeadingContent), typeof(object), typeof(SettingsRecordButtonControl), new PropertyMetadata(null));

        public static readonly DependencyProperty TrailingContentProperty =
            DependencyProperty.Register(nameof(TrailingContent), typeof(object), typeof(SettingsRecordButtonControl), new PropertyMetadata(null));

        public static readonly DependencyProperty ContentMarginProperty =
            DependencyProperty.Register(nameof(ContentMargin), typeof(Thickness), typeof(SettingsRecordButtonControl), new PropertyMetadata(new Thickness(8, 0, 0, 0)));

        public static readonly DependencyProperty ValueTextMarginProperty =
            DependencyProperty.Register(nameof(ValueTextMargin), typeof(Thickness), typeof(SettingsRecordButtonControl), new PropertyMetadata(new Thickness(0)));

        public static readonly DependencyProperty LabelForegroundProperty =
            DependencyProperty.Register(nameof(LabelForeground), typeof(Brush), typeof(SettingsRecordButtonControl), new PropertyMetadata(Brushes.White));

        public static readonly DependencyProperty ValueForegroundProperty =
            DependencyProperty.Register(nameof(ValueForeground), typeof(Brush), typeof(SettingsRecordButtonControl), new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D4D4D4"))));

        public SettingsRecordButtonControl()
        {
            InitializeComponent();
        }

        public string LabelText
        {
            get => (string)GetValue(LabelTextProperty);
            set => SetValue(LabelTextProperty, value);
        }

        public string ValueText
        {
            get => (string)GetValue(ValueTextProperty);
            set => SetValue(ValueTextProperty, value);
        }

        public GridLength LabelColumnWidth
        {
            get => (GridLength)GetValue(LabelColumnWidthProperty);
            set => SetValue(LabelColumnWidthProperty, value);
        }

        public GridLength ValueColumnWidth
        {
            get => (GridLength)GetValue(ValueColumnWidthProperty);
            set => SetValue(ValueColumnWidthProperty, value);
        }

        public GridLength LeadingColumnWidth
        {
            get => (GridLength)GetValue(LeadingColumnWidthProperty);
            set => SetValue(LeadingColumnWidthProperty, value);
        }

        public GridLength TrailingColumnWidth
        {
            get => (GridLength)GetValue(TrailingColumnWidthProperty);
            set => SetValue(TrailingColumnWidthProperty, value);
        }

        public object? LeadingContent
        {
            get => GetValue(LeadingContentProperty);
            set => SetValue(LeadingContentProperty, value);
        }

        public object? TrailingContent
        {
            get => GetValue(TrailingContentProperty);
            set => SetValue(TrailingContentProperty, value);
        }

        public Thickness ContentMargin
        {
            get => (Thickness)GetValue(ContentMarginProperty);
            set => SetValue(ContentMarginProperty, value);
        }

        public Thickness ValueTextMargin
        {
            get => (Thickness)GetValue(ValueTextMarginProperty);
            set => SetValue(ValueTextMarginProperty, value);
        }

        public Brush LabelForeground
        {
            get => (Brush)GetValue(LabelForegroundProperty);
            set => SetValue(LabelForegroundProperty, value);
        }

        public Brush ValueForeground
        {
            get => (Brush)GetValue(ValueForegroundProperty);
            set => SetValue(ValueForegroundProperty, value);
        }

        public Button RowButtonElement => RowButton;

        public event RoutedEventHandler? ButtonClick;
        public event KeyEventHandler? ButtonPreviewKeyDown;

        public void FocusRowButton()
        {
            RowButton.Focus();
        }

        private void RowButton_Click(object sender, RoutedEventArgs e)
        {
            ButtonClick?.Invoke(sender, e);
        }

        private void RowButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            ButtonPreviewKeyDown?.Invoke(sender, e);
        }
    }
}
