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

    public enum CommandType
    {
        URL,
        Folder,
        Batch,
        Command,
        Menu,
        File,
        Window
    }

    public class CommandEntry : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public CommandType Type { get; set; }
        public string Value { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string? Hotkey { get; set; }
        public HotkeyType HotkeyType { get; set; } = HotkeyType.WindowGlobal;
        public bool RequiresArgument { get; set; }
        public bool IsFileSearchEnabled { get; set; }
        public string? AppPath { get; set; }

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
