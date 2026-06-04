using System;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class DelegatedSettingsSectionViewModel : ISettingsSectionViewModel
    {
        private readonly Action _onSelected;
        private readonly Action _onSave;
        private readonly Action _onReset;

        public DelegatedSettingsSectionViewModel(string id, string title, Action onSelected, Action? onSave = null, Action? onReset = null)
        {
            Id = id;
            Title = title;
            _onSelected = onSelected ?? throw new ArgumentNullException(nameof(onSelected));
            _onSave = onSave ?? (() => { });
            _onReset = onReset ?? (() => { });
        }

        public string Id { get; }
        public string Title { get; }

        public void OnSelected()
        {
            _onSelected();
        }

        public void Save()
        {
            _onSave();
        }

        public void Reset()
        {
            _onReset();
        }
    }
}
