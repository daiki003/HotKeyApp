using System.Collections.Generic;

namespace HotKeyCommandApp.Models
{
    public class GitSettings
    {
        public List<GitAliasEntry> Aliases { get; set; } = new()
        {
            new GitAliasEntry { Alias = "st", TargetCommand = "git status -s", SortOrder = 0 },
            new GitAliasEntry { Alias = "br", TargetCommand = "git branch", SortOrder = 1 },
            new GitAliasEntry { Alias = "co", TargetCommand = "git checkout", SortOrder = 2 },
            new GitAliasEntry { Alias = "cm", TargetCommand = "git commit -m", SortOrder = 3 },
            new GitAliasEntry { Alias = "lg", TargetCommand = "git log --oneline -n 10", SortOrder = 4 }
        };

        public List<PairEntryFolder> AliasFolders { get; set; } = new();

        public List<GitFunctionEntry> Functions { get; set; } = new()
        {
            new GitFunctionEntry
            {
                Name = "acp",
                Description = "螟画峩繧偵☆縺ｹ縺ｦ繧ｹ繝・・繧ｸ縺励※繧ｳ繝溘ャ繝医＠縲√・繝・す繝･縺吶ｋ",
                Commands = new List<string> { "git add .", "git commit -m \"{0}\"", "git push" }
            }
        };

        public List<RepositoryNameMapping> RepositoryNameMappings { get; set; } = new();
        public List<PairEntryFolder> RepositoryNameMappingFolders { get; set; } = new();
    }

    public class GitAliasEntry : IPairEntryEditable
    {
        public string Alias { get; set; } = string.Empty;
        public string TargetCommand { get; set; } = string.Empty;
        public string ParentFolderId { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string FirstValue
        {
            get => Alias;
            set => Alias = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string SecondValue
        {
            get => TargetCommand;
            set => TargetCommand = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsFolder => false;
    }

    public class GitFunctionEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Commands { get; set; } = new();
    }

    public class RepositoryNameMapping : IPairEntryEditable
    {
        public string Path { get; set; } = string.Empty;
        public string OverwrittenName { get; set; } = string.Empty;
        public string BaseBranch { get; set; } = string.Empty;
        public string ParentFolderId { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string FirstValue
        {
            get => Path;
            set => Path = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string SecondValue
        {
            get => OverwrittenName;
            set => OverwrittenName = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsFolder => false;
    }
}
