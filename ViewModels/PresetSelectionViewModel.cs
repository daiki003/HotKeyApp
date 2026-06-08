using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.ViewModels
{
    public class PresetSelectionCategory
    {
        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public ObservableCollection<CommandPreset> Presets { get; } = new();
    }

    public class PresetSelectionViewModel : INotifyPropertyChanged
    {
        private const string RootCategoryId = "ROOT";
        private readonly AppSettings _appSettings;
        private readonly ConfigService _configService = new();
        private readonly PresetCreationWizard _presetCreationWizard;
        private readonly ObservableCollection<PresetSelectionCategory> _categories = new();
        private PresetSelectionCategory? _selectedCategory;
        private CommandEntry? _selectedListItem;
        private CommandPreset? _selectedPreset;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<bool>? RequestClose;
        public event Action? RequestPresetCreation;

        public ObservableCollection<PresetSelectionCategory> Categories => _categories;
        public ObservableCollection<CommandEntry> VisiblePresetItems { get; } = new();

        public PresetSelectionCategory? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (_selectedCategory == value)
                {
                    return;
                }

                _selectedCategory = value;
                OnPropertyChanged();
                RefreshVisiblePresets();
            }
        }

        public CommandPreset? SelectedPreset
        {
            get => _selectedPreset;
            private set
            {
                if (_selectedPreset == value)
                {
                    return;
                }

                _selectedPreset = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Description));
            }
        }

        public CommandEntry? SelectedListItem
        {
            get => _selectedListItem;
            set
            {
                if (_selectedListItem == value)
                {
                    return;
                }

                _selectedListItem = value;
                OnPropertyChanged();
                SelectedPreset = value?.Tag as CommandPreset;
            }
        }

        public string Description => SelectedPreset?.Description ?? string.Empty;
        public CommandPreset? ResultPreset { get; private set; }
        public AppSettings AppSettings => _appSettings;
        public double ButtonHeight => _appSettings.ButtonHeight;
        public double FontSize => _appSettings.FontSize;
        public string? SelectedCategoryFolderId => SelectedCategory != null && SelectedCategory.Id != RootCategoryId ? SelectedCategory.Id : null;

        public ICommand ConfirmCommand => new RelayCommand<object>(_ => ConfirmSelection(), _ => SelectedPreset != null);
        public ICommand CancelCommand => new RelayCommand<object>(_ => RequestClose?.Invoke(false));
        public ICommand CreatePresetCommand => new RelayCommand<object>(_ => RequestPresetCreation?.Invoke());

        public PresetSelectionViewModel(AppSettings appSettings)
        {
            _appSettings = appSettings;
            _presetCreationWizard = new PresetCreationWizard(appSettings);
            Reload();
        }

        public bool MoveCategory(int direction)
        {
            if (Categories.Count == 0)
            {
                return false;
            }

            int currentIndex = SelectedCategory == null ? 0 : Categories.IndexOf(SelectedCategory);
            int nextIndex = (currentIndex + direction + Categories.Count) % Categories.Count;
            SelectedCategory = Categories[nextIndex];
            return true;
        }

        public void MovePreset(int direction)
        {
            if (VisiblePresetItems.Count == 0)
            {
                return;
            }

            int currentIndex = SelectedListItem == null ? 0 : VisiblePresetItems.IndexOf(SelectedListItem);
            int nextIndex = currentIndex + direction;

            if (nextIndex < 0)
            {
                nextIndex = VisiblePresetItems.Count - 1;
            }
            else if (nextIndex >= VisiblePresetItems.Count)
            {
                nextIndex = 0;
            }

            SelectedListItem = VisiblePresetItems[nextIndex];
        }

        public void ConfirmSelection()
        {
            if (SelectedPreset == null)
            {
                return;
            }

            ResultPreset = SelectedPreset;
            RequestClose?.Invoke(true);
        }

        public void Reload(string? selectPresetId = null)
        {
            BuildCategories(_configService.LoadPresets(), selectPresetId);
        }

        public CommandPreset SaveCreatedPreset(CommandEntry command, CommandPreset? basePreset = null)
        {
            List<CommandPreset> presets = _configService.LoadPresets().ToList();
            string? folderId = basePreset?.ParentId ?? SelectedCategoryFolderId;
            CommandPreset createdPreset = _presetCreationWizard.CreatePreset(command, basePreset, folderId, presets);
            presets.Add(createdPreset);
            _configService.SavePresets(presets);
            Reload(createdPreset.Id);
            return createdPreset;
        }

        private void BuildCategories(IReadOnlyList<CommandPreset> presets, string? selectPresetId = null)
        {
            _categories.Clear();

            Dictionary<string, PresetSelectionCategory> categories = new();

            foreach (CommandPreset folder in presets.Where(p => p.IsFolder))
            {
                PresetSelectionCategory category = new()
                {
                    Id = folder.Id,
                    Title = folder.Name
                };
                categories[folder.Id] = category;
                _categories.Add(category);
            }

            PresetSelectionCategory? rootCategory = null;
            foreach (CommandPreset preset in presets.Where(p => !p.IsFolder))
            {
                if (!string.IsNullOrWhiteSpace(preset.ParentId) &&
                    categories.TryGetValue(preset.ParentId, out PresetSelectionCategory? category))
                {
                    category.Presets.Add(preset);
                    continue;
                }

                rootCategory ??= new PresetSelectionCategory
                {
                    Id = RootCategoryId,
                    Title = "その他"
                };
                rootCategory.Presets.Add(preset);
            }

            if (rootCategory != null)
            {
                _categories.Add(rootCategory);
            }

            PresetSelectionCategory? initialCategory = null;
            if (!string.IsNullOrWhiteSpace(selectPresetId))
            {
                initialCategory = _categories.FirstOrDefault(c => c.Presets.Any(p => p.Id == selectPresetId));
            }

            SelectedCategory = initialCategory ?? _categories.FirstOrDefault(c => c.Presets.Count > 0) ?? _categories.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(selectPresetId))
            {
                SelectedListItem = VisiblePresetItems.FirstOrDefault(p => p.Value == selectPresetId) ?? SelectedListItem;
            }
        }

        private void RefreshVisiblePresets()
        {
            VisiblePresetItems.Clear();

            if (SelectedCategory == null)
            {
                SelectedListItem = null;
                return;
            }

            foreach (CommandPreset preset in SelectedCategory.Presets)
            {
                VisiblePresetItems.Add(new CommandEntry
                {
                    Name = preset.Name,
                    Value = preset.Id,
                    Tag = preset
                });
            }

            VisiblePresetItems.Add(new CommandEntry
            {
                Name = "ボタン追加",
                Value = "ADD_BUTTON",
                IsSystem = true
            });

            SelectedListItem = VisiblePresetItems.FirstOrDefault(item => item.Tag is CommandPreset) ?? VisiblePresetItems.FirstOrDefault();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
