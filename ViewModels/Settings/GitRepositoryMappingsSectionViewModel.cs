using System.Collections.ObjectModel;
using System.Linq;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.ViewModels;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class GitRepositoryMappingsSectionViewModel : SettingsSectionViewModelBase
    {
        private readonly GitSettings _source;

        public GitRepositoryMappingsSectionViewModel(GitSettings settings)
            : base("Mappings", "リポジトリ名")
        {
            _source = settings;
            RepositoryNameMappings = new ObservableCollection<RepositoryNameMapping>();
            RepositoryNameMappingFolders = new ObservableCollection<PairEntryFolder>();
            AddItemCommand = new RelayCommand<object>(_ =>
                RepositoryNameMappings.Add(new RepositoryNameMapping
                {
                    Path = "C:\\path\\to\\repo",
                    OverwrittenName = "MyRepo"
                }));
            Reset();
        }

        public ObservableCollection<RepositoryNameMapping> RepositoryNameMappings { get; }
        public ObservableCollection<PairEntryFolder> RepositoryNameMappingFolders { get; }
        public RelayCommand<object> AddItemCommand { get; }

        public override void Save()
        {
            _source.RepositoryNameMappings = RepositoryNameMappings.ToList();
            _source.RepositoryNameMappingFolders = RepositoryNameMappingFolders.ToList();
        }

        public override void Reset()
        {
            RepositoryNameMappings.Clear();
            foreach (RepositoryNameMapping mapping in _source.RepositoryNameMappings)
            {
                RepositoryNameMappings.Add(mapping);
            }

            RepositoryNameMappingFolders.Clear();
            foreach (PairEntryFolder folder in _source.RepositoryNameMappingFolders)
            {
                RepositoryNameMappingFolders.Add(folder);
            }
        }
    }
}
