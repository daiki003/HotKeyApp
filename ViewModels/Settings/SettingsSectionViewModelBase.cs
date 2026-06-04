using System;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public abstract class SettingsSectionViewModelBase : ViewModelBase, ISettingsSectionViewModel
    {
        protected SettingsSectionViewModelBase(string id, string title)
        {
            Id = id;
            Title = title;
        }

        public string Id { get; }
        public string Title { get; }
        public Action? SelectedAction { get; set; }

        public virtual void OnSelected()
        {
            SelectedAction?.Invoke();
        }

        public abstract void Save();
        public abstract void Reset();
    }
}
