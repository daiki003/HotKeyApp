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
        public double ButtonWidth { get; set; } = 350;
        public double ButtonHeight { get; set; } = 50;
        public double MovementSpeed { get; set; } = 1200.0;
        public double FontSize { get; set; } = 15.0;
        public string GlobalHotkey { get; set; } = "Win+Alt+Z";

        /// <summary>アプリ選択画面に表示する登録済みアプリ一覧</summary>
        public List<RegisteredApp> RegisteredApps { get; set; } = new();

        /// <summary>SlackのチームID履歴</summary>
        public List<string> SlackTeamIdHistory { get; set; } = new();
    }
}
