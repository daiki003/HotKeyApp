using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Services
{
    public class GitSettingsEditorSession
    {
        public GitSettingsEditorSession(GitSettings source)
        {
            WorkingCopy = Clone(source);
            WorkingCopy.EnsureAliasTabs();
        }

        public GitSettings WorkingCopy { get; }

        public GitSettings BuildSettingsForSave()
        {
            GitSettings normalized = Clone(WorkingCopy);
            normalized.EnsureAliasTabs();

            foreach (GitAliasTab tab in normalized.GetAllAliasTabs())
            {
                for (int i = 0; i < tab.Aliases.Count; i++)
                {
                    tab.Aliases[i].ParentFolderId = string.Empty;
                    tab.Aliases[i].SortOrder = i;
                }

                tab.Folders = new List<PairEntryFolder>();
            }

            for (int i = 0; i < normalized.RepositoryNameMappings.Count; i++)
            {
                normalized.RepositoryNameMappings[i].ParentFolderId = string.Empty;
                normalized.RepositoryNameMappings[i].SortOrder = i;
            }

            normalized.RepositoryNameMappingFolders = new List<PairEntryFolder>();

            GitAliasTab generalTab = normalized.GetAllAliasTabs().First();
            normalized.Aliases = generalTab.Aliases;
            normalized.AliasFolders = generalTab.Folders;

            return normalized;
        }

        private static GitSettings Clone(GitSettings source)
        {
            string json = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<GitSettings>(json) ?? new GitSettings();
        }
    }
}
