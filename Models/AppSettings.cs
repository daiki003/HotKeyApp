using System.Collections.Generic;
using System.Linq;

namespace HotKeyCommandApp.Models
{
    /// <summary>よく使うアプリとして登録された情報</summary>
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

        /// <summary>アプリ選択画面に表示する登録済みアプリ一覧</summary>
        public List<RegisteredApp> RegisteredApps { get; set; } = new();

        /// <summary>Slack of Team ID history</summary>
        public List<string> SlackTeamIdHistory { get; set; } = new();

        /// <summary>定数定義一覧</summary>
        public List<ConstantEntry> Constants { get; set; } = new();
        /// <summary>選択式プレースホルダのテンプレート一覧</summary>
        public List<SelectTemplate> SelectTemplates { get; set; } = new();
    }

    /// <summary>定数定義情報</summary>
    public class ConstantEntry : IPairEntryEditable
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

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
    }

    /// <summary>選択式プレースホルダのテンプレート情報</summary>
    public class SelectTemplate : IPairEntryEditable
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Options { get; set; } = new();

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
    }
}
