using System.Collections.Generic;

namespace HotKeyCommandApp.Models
{
    public class GitSettings
    {
        public List<GitAliasEntry> Aliases { get; set; } = new()
        {
            new GitAliasEntry { Alias = "st", TargetCommand = "git status -s" },
            new GitAliasEntry { Alias = "br", TargetCommand = "git branch" },
            new GitAliasEntry { Alias = "co", TargetCommand = "git checkout" },
            new GitAliasEntry { Alias = "cm", TargetCommand = "git commit -m" },
            new GitAliasEntry { Alias = "lg", TargetCommand = "git log --oneline -n 10" }
        };

        public List<GitFunctionEntry> Functions { get; set; } = new()
        {
            new GitFunctionEntry
            {
                Name = "acp",
                Description = "変更をすべてステージしてコミットし、プッシュする",
                Commands = new List<string> { "git add .", "git commit -m \"{0}\"", "git push" }
            }
        };

        public List<RepositoryNameMapping> RepositoryNameMappings { get; set; } = new();
    }

    public class GitAliasEntry : IPairEntryEditable
    {
        public string Alias { get; set; } = string.Empty;
        public string TargetCommand { get; set; } = string.Empty;

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
    }
}
