using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace HotKeyCommandApp.Models
{
    public class GitAliasTabHost : IPairEditorTabHost
    {
        private readonly GitSettings _settings;

        public GitAliasTabHost(GitSettings settings)
        {
            _settings = settings;
            _settings.EnsureAliasTabs();
            SelectedTab = _settings.GetAllAliasTabs().FirstOrDefault();
        }

        public IList Tabs => _settings.GetAllAliasTabs();

        public IPairEditorTab? SelectedTab { get; set; }

        public IPairEditorTab CreateNewTab()
        {
            var newTab = new GitAliasTab
            {
                Name = $"タブ{_settings.GetAllAliasTabs().Count + 1}"
            };

            _settings.GetAllAliasTabs().Add(newTab);
            SelectedTab = newTab;
            return newTab;
        }

        public bool DeleteTab(IPairEditorTab tab)
        {
            if (tab is not GitAliasTab targetTab)
            {
                return false;
            }

            List<GitAliasTab> aliasTabs = _settings.GetAllAliasTabs();
            int currentIndex = aliasTabs.IndexOf(targetTab);
            if (currentIndex < 0)
            {
                return false;
            }

            if (aliasTabs.Count == 1)
            {
                aliasTabs[0] = new GitAliasTab
                {
                    Id = "general",
                    Name = "一般"
                };
                SelectedTab = aliasTabs[0];
                return true;
            }

            aliasTabs.RemoveAt(currentIndex);
            int nextIndex = currentIndex >= aliasTabs.Count ? aliasTabs.Count - 1 : currentIndex;
            SelectedTab = aliasTabs[nextIndex];
            return true;
        }
    }
}
