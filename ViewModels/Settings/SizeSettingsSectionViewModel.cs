using System;
using System.Collections.ObjectModel;
using System.Globalization;
using HotKeyCommandApp.ViewModels;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class SizeSettingsSectionViewModel : SettingsSectionViewModelBase
    {
        private readonly MainViewModel _source;
        private double _editingFontSize;
        private double _editingButtonWidth;
        private double _editingButtonHeight;

        public SizeSettingsSectionViewModel(MainViewModel source)
            : base("Size", "サイズ調整")
        {
            _source = source;
            Records = new ObservableCollection<EditableValueRecordViewModel>
            {
                CreateRecord("フォントサイズ", () => EditingFontSize, value => EditingFontSize = value),
                CreateRecord("ボタン幅", () => EditingButtonWidth, value => EditingButtonWidth = value),
                CreateRecord("ボタン高", () => EditingButtonHeight, value => EditingButtonHeight = value)
            };
            Reset();
        }

        public ObservableCollection<EditableValueRecordViewModel> Records { get; }

        public double EditingFontSize
        {
            get => _editingFontSize;
            set { _editingFontSize = value; OnPropertyChanged(); }
        }

        public double EditingButtonWidth
        {
            get => _editingButtonWidth;
            set { _editingButtonWidth = value; OnPropertyChanged(); }
        }

        public double EditingButtonHeight
        {
            get => _editingButtonHeight;
            set { _editingButtonHeight = value; OnPropertyChanged(); }
        }

        public override void Save()
        {
            _source.EditingFontSize = EditingFontSize;
            _source.EditingButtonWidth = EditingButtonWidth;
            _source.EditingButtonHeight = EditingButtonHeight;
        }

        public override void Reset()
        {
            EditingFontSize = _source.EditingFontSize;
            EditingButtonWidth = _source.EditingButtonWidth;
            EditingButtonHeight = _source.EditingButtonHeight;
            RefreshRecords();
        }

        private EditableValueRecordViewModel CreateRecord(string label, Func<double> getter, Action<double> setter)
        {
            return new EditableValueRecordViewModel(
                label,
                () => getter().ToString("0.##", CultureInfo.CurrentCulture),
                text =>
                {
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) &&
                        !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    {
                        return false;
                    }

                    setter(value);
                    return true;
                });
        }

        private void RefreshRecords()
        {
            foreach (EditableValueRecordViewModel record in Records)
            {
                record.RefreshFromSource();
            }
        }
    }
}
