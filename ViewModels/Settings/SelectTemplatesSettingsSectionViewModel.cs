using System.Collections.ObjectModel;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.ViewModels;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class SelectTemplatesSettingsSectionViewModel : SettingsSectionViewModelBase
    {
        private readonly MainViewModel _source;
        private ObservableCollection<SelectTemplate> _editingSelectTemplates;
        private ObservableCollection<PairEntryFolder> _editingSelectTemplateFolders;

        public SelectTemplatesSettingsSectionViewModel(MainViewModel source)
            : base("SelectTemplates", "選択式テンプレート")
        {
            _source = source;
            _editingSelectTemplates = new ObservableCollection<SelectTemplate>();
            _editingSelectTemplateFolders = new ObservableCollection<PairEntryFolder>();
            AddItemCommand = new RelayCommand<object>(_ => _editingSelectTemplates.Add(new SelectTemplate { Name = "NEW_TEMPLATE", OptionsString = string.Empty }));
            Reset();
        }

        public ObservableCollection<SelectTemplate> EditingSelectTemplates
        {
            get => _editingSelectTemplates;
            set { _editingSelectTemplates = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PairEntryFolder> EditingSelectTemplateFolders
        {
            get => _editingSelectTemplateFolders;
            set { _editingSelectTemplateFolders = value; OnPropertyChanged(); }
        }

        public RelayCommand<object> AddItemCommand { get; }

        public override void Save()
        {
            _source.EditingSelectTemplates = new ObservableCollection<SelectTemplate>(EditingSelectTemplates);
            _source.EditingSelectTemplateFolders = new ObservableCollection<PairEntryFolder>(EditingSelectTemplateFolders);
        }

        public override void Reset()
        {
            EditingSelectTemplates = new ObservableCollection<SelectTemplate>(_source.EditingSelectTemplates);
            EditingSelectTemplateFolders = new ObservableCollection<PairEntryFolder>(_source.EditingSelectTemplateFolders);
        }
    }
}
