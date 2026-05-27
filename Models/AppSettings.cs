using System.Collections.Generic;
using System.Linq;

namespace HotKeyCommandApp.Models
{
    /// <summary>繧医￥菴ｿ縺・い繝励Μ縺ｨ縺励※逋ｻ骭ｲ縺輔ｌ縺滓ュ蝣ｱ</summary>
    public class RegisteredApp
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public class AppSettings
    {
        public double WindowWidth { get; set; } = 200;
        public double WindowHeight { get; set; } = 300;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowLeft { get; set; } = double.NaN;
        public double GitWindowTop { get; set; } = double.NaN;
        public double GitWindowLeft { get; set; } = double.NaN;
        public double GitWindowWidth { get; set; } = 450;
        public double GitWindowHeight { get; set; } = 320;
        public double ButtonWidth { get; set; } = 350;
        public double ButtonHeight { get; set; } = 50;
        public double MovementSpeed { get; set; } = 1200.0;
        public double FontSize { get; set; } = 15.0;
        public string GlobalHotkey { get; set; } = "Win+Alt+Z";
        public string SettingsShortcut { get; set; } = "Ctrl+Comma";
        public string CreateButtonShortcut { get; set; } = "Ctrl+Plus";

        public List<RegisteredApp> RegisteredApps { get; set; } = new();
        public List<string> SlackTeamIdHistory { get; set; } = new();
        public List<ConstantEntry> Constants { get; set; } = new();
        public List<PairEntryFolder> ConstantFolders { get; set; } = new();
        public List<SelectTemplate> SelectTemplates { get; set; } = new();
        public List<PairEntryFolder> SelectTemplateFolders { get; set; } = new();
    }

    public class ConstantEntry : IPairEntryEditable
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string ParentFolderId { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string FirstValue
        {
            get => Name;
            set => Name = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string SecondValue
        {
            get => Value;
            set => Value = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsFolder => false;
    }

    public class SelectTemplate : IPairEntryEditable
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Options { get; set; } = new();
        public string ParentFolderId { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        private string? _optionsString;

        [System.Text.Json.Serialization.JsonIgnore]
        public string OptionsString
        {
            get
            {
                if (_optionsString == null)
                {
                    _optionsString = string.Join(",", Options);
                }
                return _optionsString;
            }
            set
            {
                _optionsString = value;
                Options = new List<string>((value ?? "").Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)));
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string FirstValue
        {
            get => Name;
            set => Name = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string SecondValue
        {
            get => OptionsString;
            set => OptionsString = value;
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsFolder => false;
    }
}
