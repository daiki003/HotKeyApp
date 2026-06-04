using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class GitAliasSettingsSectionViewModel : SettingsSectionViewModelBase
    {
        public GitAliasSettingsSectionViewModel(IPairEditorTabHost tabHost)
            : base("Aliases", "エイリアス")
        {
            TabHost = tabHost;
        }

        public IPairEditorTabHost TabHost { get; }

        public override void Save()
        {
        }

        public override void Reset()
        {
        }
    }
}
