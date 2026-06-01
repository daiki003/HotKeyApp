using System.Collections;

namespace HotKeyCommandApp.Models
{
    public interface IPairEditorTab
    {
        string Name { get; set; }
        bool IsEditing { get; set; }
        IList Items { get; }
        IList Folders { get; }
        IPairEntryEditable CreateNewItem();
    }

    public interface IPairEditorTabHost
    {
        IList Tabs { get; }
        IPairEditorTab? SelectedTab { get; set; }
        IPairEditorTab CreateNewTab();
        bool DeleteTab(IPairEditorTab tab);
    }
}
