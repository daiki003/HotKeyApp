using System;
using System.Collections.ObjectModel;
using System.Globalization;
using HotKeyCommandApp.ViewModels;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class OthersSettingsSectionViewModel : SettingsSectionViewModelBase
    {
        private readonly MainViewModel _source;
        private double _editingMovementSpeed;

        public OthersSettingsSectionViewModel(MainViewModel source)
            : base("Others", "その他")
        {
            _source = source;
            Records = new ObservableCollection<EditableValueRecordViewModel>
            {
                CreateRecord("移動速度 (Ctrl+矢印)", () => EditingMovementSpeed, value => EditingMovementSpeed = value)
            };
            Reset();
        }

        public ObservableCollection<EditableValueRecordViewModel> Records { get; }

        public double EditingMovementSpeed
        {
            get => _editingMovementSpeed;
            set { _editingMovementSpeed = value; OnPropertyChanged(); }
        }

        public override void Save()
        {
            _source.EditingMovementSpeed = EditingMovementSpeed;
        }

        public override void Reset()
        {
            EditingMovementSpeed = _source.EditingMovementSpeed;
            foreach (EditableValueRecordViewModel record in Records)
            {
                record.RefreshFromSource();
            }
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
    }
}
