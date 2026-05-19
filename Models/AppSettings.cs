using System.Collections.Generic;

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
    }

    /// <summary>定数定義情報</summary>
    public class ConstantEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
