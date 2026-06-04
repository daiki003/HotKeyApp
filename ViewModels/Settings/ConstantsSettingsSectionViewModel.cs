using System.Collections.ObjectModel;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.ViewModels;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class ConstantsSettingsSectionViewModel : SettingsSectionViewModelBase
    {
        private readonly MainViewModel _source;
        private ObservableCollection<ConstantEntry> _editingConstants;
        private ObservableCollection<PairEntryFolder> _editingConstantFolders;

        public ConstantsSettingsSectionViewModel(MainViewModel source)
            : base("Constants", "定数定義")
        {
            _source = source;
            _editingConstants = new ObservableCollection<ConstantEntry>();
            _editingConstantFolders = new ObservableCollection<PairEntryFolder>();
            AddItemCommand = new RelayCommand<object>(_ => _editingConstants.Add(new ConstantEntry { Name = "NEW_CONSTANT", Value = string.Empty }));
            Reset();
        }

        public ObservableCollection<ConstantEntry> EditingConstants
        {
            get => _editingConstants;
            set { _editingConstants = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PairEntryFolder> EditingConstantFolders
        {
            get => _editingConstantFolders;
            set { _editingConstantFolders = value; OnPropertyChanged(); }
        }

        public RelayCommand<object> AddItemCommand { get; }

        public override void Save()
        {
            _source.EditingConstants = new ObservableCollection<ConstantEntry>(EditingConstants);
            _source.EditingConstantFolders = new ObservableCollection<PairEntryFolder>(EditingConstantFolders);
        }

        public override void Reset()
        {
            EditingConstants = new ObservableCollection<ConstantEntry>(_source.EditingConstants);
            EditingConstantFolders = new ObservableCollection<PairEntryFolder>(_source.EditingConstantFolders);
        }
    }
}
