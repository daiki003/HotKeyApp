namespace HotKeyCommandApp.Models
{
    public sealed class PaletteNavigationState
    {
        public string Title { get; init; } = string.Empty;
        public CommandEntry? SelectedItem { get; init; }
        public string? SelectedTemplateId { get; init; }
        public List<(CommandEntry? Parent, CommandEntry? LastSelected, string Title)> NavigationStack { get; init; } = new();
        public CommandEntry? ParentMenu { get; init; }
        public bool IsRoot { get; init; }
        public bool IsInputMode { get; init; }
        public string InputText { get; init; } = string.Empty;
        public string InputPrompt { get; init; } = string.Empty;
    }

    public sealed class PaletteNavigationHistory
    {
        private const int MaxEntries = 50;
        private readonly List<PaletteNavigationState> _entries = new();
        private int _index = -1;

        public void Record(PaletteNavigationState state)
        {
            if (_index >= 0 && _index < _entries.Count - 1)
                _entries.RemoveRange(_index + 1, _entries.Count - (_index + 1));

            if (_entries.Count > 0)
            {
                var last = _entries[^1];
                if (last.Title == state.Title && last.IsInputMode == state.IsInputMode &&
                    last.InputText == state.InputText && last.SelectedItem == state.SelectedItem)
                    return;
            }

            _entries.Add(state);
            _index++;
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
                _index--;
            }
        }

        public bool TryGoBack(out PaletteNavigationState state)
        {
            state = null;
            if (_index <= 0) return false;
            state = _entries[--_index];
            return true;
        }

        public bool TryGoForward(out PaletteNavigationState state)
        {
            state = null;
            if (_index < 0 || _index >= _entries.Count - 1) return false;
            state = _entries[++_index];
            return true;
        }
    }
}
