using System;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class ActionValueRecordViewModel : ViewModelBase
    {
        private readonly Func<string> _getValueText;

        public ActionValueRecordViewModel(string key, string label, Func<string> getValueText)
        {
            Key = key;
            Label = label;
            _getValueText = getValueText;
        }

        public string Key { get; }

        public string Label { get; }

        public string DisplayValue => _getValueText();

        public void Refresh()
        {
            OnPropertyChanged(nameof(DisplayValue));
        }
    }
}
