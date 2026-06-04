using System;
using System.Collections.Generic;
using System.Linq;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class SettingsSectionsViewModel
    {
        private readonly List<ISettingsSectionViewModel> _sections = new();

        public IReadOnlyList<ISettingsSectionViewModel> Sections => _sections;
        public ISettingsSectionViewModel? SelectedSection { get; private set; }
        public int SelectedIndex => SelectedSection == null ? -1 : _sections.IndexOf(SelectedSection);

        public void Add(ISettingsSectionViewModel section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            _sections.Add(section);
        }

        public bool SelectById(string id)
        {
            ISettingsSectionViewModel? section = _sections.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
            return Select(section);
        }

        public bool SelectByIndex(int index)
        {
            if (index < 0 || index >= _sections.Count)
            {
                return false;
            }

            return Select(_sections[index]);
        }

        public bool MoveSelection(int direction)
        {
            if (_sections.Count == 0)
            {
                return false;
            }

            int currentIndex = SelectedIndex;
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            int nextIndex = (currentIndex + direction + _sections.Count) % _sections.Count;
            return SelectByIndex(nextIndex);
        }

        public void SaveAll()
        {
            foreach (ISettingsSectionViewModel section in _sections)
            {
                section.Save();
            }
        }

        public void ResetAll()
        {
            foreach (ISettingsSectionViewModel section in _sections)
            {
                section.Reset();
            }
        }

        private bool Select(ISettingsSectionViewModel? section)
        {
            if (section == null)
            {
                return false;
            }

            SelectedSection = section;
            section.OnSelected();
            return true;
        }
    }
}
