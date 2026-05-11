using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Newtonsoft.Json;

namespace HotKeyCommandApp.Models
{
    public enum HotkeyType
    {
        WindowGlobal,
        HierarchyLocal,
        Global
    }

    public enum CommandCategory
    {
        None,           // 未指定
        Open,           // 「開く」
        WindowSwitcher, // 「ウィンドウ切替」
        Hierarchy       // 「階層」
    }

    /// <summary>
    /// ボタンの動作フラグを集約するクラス
    /// </summary>
    public class CommandBehavior
    {
        public bool RequiresArgument { get; set; }
        public bool IsFileSearchEnabled { get; set; }
        public bool IsBatchMode { get; set; }
        public bool UseWindowFocusLogic { get; set; } = true;
    }

    public class CommandEntry : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        
        // TemplateId は作成時のみ使用するため、モデルからは削除
        
        public CommandCategory Category { get; set; }
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// システム側で生成されたボタン（保存対象外）かどうか
        /// </summary>
        public bool IsSystem { get; set; }

        public bool IsSystemButton => IsSystem || (Value?.StartsWith("ADD_") ?? false);

        public string? Icon { get; set; }
        public string? Hotkey { get; set; }
        public HotkeyType HotkeyType { get; set; } = HotkeyType.WindowGlobal;

        /// <summary>
        /// 動作に関わる各種フラグ
        /// </summary>
        public CommandBehavior Behavior { get; set; } = new CommandBehavior();

        public string? AppPath { get; set; }

        [JsonIgnore]
        public object? Tag { get; set; }

        [JsonIgnore]
        public System.IntPtr WindowHandle { get; set; }
        [JsonIgnore]
        public int WindowWidth { get; set; }
        [JsonIgnore]
        public int WindowHeight { get; set; }

        private ImageSource? _iconSource;
        [JsonIgnore]
        public ImageSource? IconSource
        {
            get => _iconSource;
            set { _iconSource = value; OnPropertyChanged(); }
        }

        private bool _isTargetedForMove;
        [JsonIgnore]
        public bool IsTargetedForMove
        {
            get => _isTargetedForMove;
            set { _isTargetedForMove = value; OnPropertyChanged(); }
        }

        // Navigation properties
        public List<CommandEntry>? Children { get; set; }

        public List<string> ArgumentsHistory { get; set; } = new List<string>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
