using System;
using System.Collections.Generic;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class SettingsTabShellController
    {
        private readonly SettingsSectionsViewModel _sections = new();

        public IReadOnlyList<ISettingsSectionViewModel> Sections => _sections.Sections;
        public int SelectedIndex => _sections.SelectedIndex;
        public ISettingsSectionViewModel? SelectedSection => _sections.SelectedSection;

        public void AddSection(SettingsSectionViewModelBase section, Action onSelected)
        {
            section.SelectedAction = onSelected;
            _sections.Add(section);
        }

        public bool SelectByIndex(int index)
        {
            return _sections.SelectByIndex(index);
        }

        public bool MoveSelection(int direction)
        {
            return _sections.MoveSelection(direction);
        }

        public void SaveAll()
        {
            _sections.SaveAll();
        }

        public void ResetAll()
        {
            _sections.ResetAll();
        }
    }
}
