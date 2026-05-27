namespace HotKeyCommandApp.Models
{
    public interface IPairEntryEditable
    {
        string FirstValue { get; set; }
        string SecondValue { get; set; }
        string ParentFolderId { get; set; }
        int SortOrder { get; set; }
        bool IsFolder { get; }
    }
}
