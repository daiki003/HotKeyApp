using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using R3;

namespace HotKeyCommandApp.ViewModels.ButtonCreationSteps
{
    public class ListSelectionCreationStep : CreationStepBase
    {
        private readonly PresetStepDefinition _def;
        private readonly ConfigService _configService;
        private readonly WindowService _windowService;
        private readonly AppSettings _appSettings;

        public override string Title => _def.Title;
        public override string Prompt => _def.Prompt;
        public string? ListSource => _def.ListSource;

        public override bool IsTextInputVisible => _def.ShowIncrementalSearch;

        public ListSelectionCreationStep(PresetStepDefinition def, ConfigService configService, WindowService windowService, AppSettings appSettings)
        {
            _def = def;
            _configService = configService;
            _windowService = windowService;
            _appSettings = appSettings;

            IsListBoxVisible = Observable.Return(true).ToBindableReactiveProperty().AddTo(ref _disposables);
        }

        public override void Initialize(IDictionary<string, CreationStepResultBase> results)
        {
            PopulateList();

            // 初期値の復元
            if (results.TryGetValue(_def.DataKey, out var result) && result is ListSelectionResult listResult)
            {
                SelectedItem.Value = listResult.SelectedItem;
            }
        }

        private void PopulateList()
        {
            DisplayCommands.Clear();
            SelectedItem.Value = null;

            if (_def.ListSource == "categories")
            {
                foreach (CommandCategory cat in Enum.GetValues(typeof(CommandCategory)))
                {
                    if (cat == CommandCategory.None) continue;
                    DisplayCommands.Add(new CommandEntry { Name = cat.ToString(), Value = cat.ToString() });
                }
            }
            else if (_def.ListSource == "presets")
            {
                var presets = _configService.LoadPresets();
                foreach (var p in presets) DisplayCommands.Add(new CommandEntry { Name = p.Name, Value = p.Id });
            }
            else if (_def.ListSource == "registered_apps")
            {
                DisplayCommands.Add(new CommandEntry { Name = "(既定のソフトを使用)", Value = "NONE", IsSystem = true });
                foreach (var app in _appSettings.RegisteredApps)
                {
                    DisplayCommands.Add(new CommandEntry { Name = app.Name, Value = app.Path, IsSystem = false });
                }
                DisplayCommands.Add(new CommandEntry { Name = "+ 新しいソフトを登録...", Value = "ADD_REGISTERED_APP", IsSystem = true });
            }
            else if (_def.ListSource == "windows")
            {
                var windows = _windowService.GetVisibleWindowsByTitle("")
                    .OrderBy(w => w.Title);
                foreach (var win in windows) DisplayCommands.Add(new CommandEntry { Name = win.Title, Value = win.Title });
            }

            if (DisplayCommands.Count > 0 && SelectedItem.Value == null) 
                SelectedItem.Value = DisplayCommands[0];
        }

        public override CreationStepResultBase? OnCommitted()
        {
            if (SelectedItem.Value == null) return null;

            // 特殊な項目が選択された場合
            if (_def.ListSource == "registered_apps" && SelectedItem.Value.Value == "ADD_REGISTERED_APP")
            {
                // Result として返し、ViewModel 側でハンドリングさせる
                return new ListSelectionResult { DataKey = _def.DataKey, SelectedItem = SelectedItem.Value };
            }

            if (_def.ListSource == "categories" && Enum.TryParse<CommandCategory>(SelectedItem.Value.Value, out var cat))
                return new CategorySelectionResult { DataKey = _def.DataKey, SelectedCategory = cat };
            
            if (_def.ListSource == "presets")
            {
                var preset = _configService.LoadPresets().FirstOrDefault(p => p.Id == SelectedItem.Value.Value);
                if (preset != null) return new PresetSelectionResult { DataKey = _def.DataKey, SelectedPreset = preset };
            }

            return new ListSelectionResult { DataKey = _def.DataKey, SelectedItem = SelectedItem.Value };
        }

        public override async Task PerformSearchAsync(string query)
        {
            if (_def.ListSource == "windows")
            {
                App.Current.Dispatcher.Invoke(() => 
                {
                    DisplayCommands.Clear();
                    SelectedItem.Value = null;
                });

                var windows = _windowService.GetVisibleWindowsByTitle(query)
                    .OrderByDescending(w => w.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(w => w.Title.Length)
                    .Take(30);
                
                foreach (var win in windows) 
                    App.Current.Dispatcher.Invoke(() => DisplayCommands.Add(new CommandEntry { Name = win.Title, Value = win.Title }));
                
                if (DisplayCommands.Count > 0) 
                    App.Current.Dispatcher.Invoke(() => SelectedItem.Value = DisplayCommands[0]);
            }
            await Task.CompletedTask;
        }
    }
}
