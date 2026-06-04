using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotKeyCommandApp.ViewModels.Settings;

namespace HotKeyCommandApp.Views.Controls
{
    public delegate void ActionValueRecordActivatedEventHandler(object sender, ActionValueRecordViewModel item);

    public partial class ActionValueRecordControl : UserControl
    {
        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.Register(nameof(Item), typeof(ActionValueRecordViewModel), typeof(ActionValueRecordControl), new PropertyMetadata(null));

        public ActionValueRecordControl()
        {
            InitializeComponent();
        }

        public ActionValueRecordViewModel? Item
        {
            get => (ActionValueRecordViewModel?)GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }

        public event ActionValueRecordActivatedEventHandler? Activated;

        public void FocusRowButton()
        {
            RecordButton.FocusRowButton();
        }

        private void RowButton_Click(object sender, RoutedEventArgs e)
        {
            ActivateCurrentItem();
        }

        private void RowButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                case Key.F2:
                    ActivateCurrentItem();
                    e.Handled = true;
                    return;
            }

            SettingsRecordButtonKeyHandler.TryHandleStandardNavigation(RecordButton, e);
        }

        private void ActivateCurrentItem()
        {
            if (Item != null)
            {
                Activated?.Invoke(this, Item);
            }
        }
    }
}
