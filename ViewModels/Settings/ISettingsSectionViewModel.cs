namespace HotKeyCommandApp.ViewModels.Settings
{
    public interface ISettingsSectionViewModel
    {
        string Id { get; }
        string Title { get; }
        void OnSelected();
        void Save();
        void Reset();
    }
}
