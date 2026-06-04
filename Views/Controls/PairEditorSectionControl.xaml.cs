using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class PairEditorSectionControl : UserControl, ISettingsRecordSectionControl
    {
        public PairEditorSectionControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IList), typeof(PairEditorSectionControl), new PropertyMetadata(null));

        public static readonly DependencyProperty FolderItemsSourceProperty =
            DependencyProperty.Register(nameof(FolderItemsSource), typeof(IList), typeof(PairEditorSectionControl), new PropertyMetadata(null));

        public static readonly DependencyProperty FirstHeaderProperty =
            DependencyProperty.Register(nameof(FirstHeader), typeof(string), typeof(PairEditorSectionControl), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SecondHeaderProperty =
            DependencyProperty.Register(nameof(SecondHeader), typeof(string), typeof(PairEditorSectionControl), new PropertyMetadata(string.Empty));

        public IList? ItemsSource
        {
            get => (IList?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public IList? FolderItemsSource
        {
            get => (IList?)GetValue(FolderItemsSourceProperty);
            set => SetValue(FolderItemsSourceProperty, value);
        }

        public string FirstHeader
        {
            get => (string)GetValue(FirstHeaderProperty);
            set => SetValue(FirstHeaderProperty, value);
        }

        public string SecondHeader
        {
            get => (string)GetValue(SecondHeaderProperty);
            set => SetValue(SecondHeaderProperty, value);
        }

        public AliasPairEditorControl Editor => PairEditor;

        public void FocusFirstRecord()
        {
            Editor.FocusFirstRecord();
        }

        public void FocusLastRecord()
        {
            Editor.FocusLastRecord();
        }

        public void FocusRecordAfterAppend()
        {
            Editor.FocusRecordAfterAppend();
        }

        public bool IsFocusInEditor()
        {
            return Editor.IsFocusInEditor();
        }

        public bool TryHandleKeyboardShortcut(System.Windows.Input.KeyEventArgs e)
        {
            return Editor.TryHandleKeyboardShortcut(e);
        }

        public bool TryFocusHeaderFromEditor()
        {
            return Editor.TryFocusHeaderFromEditor();
        }
    }
}
