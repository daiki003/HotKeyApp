namespace HotKeyCommandApp.Models
{
    public sealed class PairEditorTabEditSession
    {
        private string _originalName = string.Empty;
        public IPairEditorTab? EditingTab { get; private set; }

        public void Start(IPairEditorTab tab)
        {
            End(commit: true);
            EditingTab = tab;
            _originalName = tab.Name;
            tab.IsEditing = true;
        }

        public void End(bool commit)
        {
            if (EditingTab == null) return;
            EditingTab.Name = !commit || string.IsNullOrWhiteSpace(EditingTab.Name)
                ? _originalName
                : EditingTab.Name.Trim();
            EditingTab.IsEditing = false;
            EditingTab = null;
            _originalName = string.Empty;
        }
    }
}
