using System.Collections.Generic;
using System.Linq;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using R3;

namespace HotKeyCommandApp.ViewModels.ButtonCreationSteps
{
    public class PresetSelectionStep : CreationStepBase
    {
        private readonly PresetStepDefinition _def;
        private readonly ConfigService _configService;

        private readonly List<CommandEntry> _topLevelItems = new();
        private readonly Dictionary<string, List<CommandEntry>> _categoryItems = new();

        public override string Title => _def.Title;
        public override string Prompt => _def.Prompt;
        public override bool IsTextInputVisible => false;
        public override bool ShowInPageIndicator => false;

        public PresetSelectionStep(PresetStepDefinition def, ConfigService configService)
        {
            _def = def;
            _configService = configService;

            IsListBoxVisible = Observable.Return(true).ToBindableReactiveProperty().AddTo(ref _disposables);
        }

        public override void Initialize(IDictionary<string, CreationStepResultBase> results)
        {
            _topLevelItems.Clear();
            _categoryItems.Clear();

            var presets = _configService.LoadPresets();

            // 1. フォルダの抽出と初期化
            foreach (var preset in presets.Where(p => p.IsFolder))
            {
                _categoryItems[preset.Id] = new List<CommandEntry>
                {
                    new CommandEntry { Name = "<  戻る", Value = "ACTION:BACK" }
                };
            }

            // 2. プリセットの割り当て (カスタムは除外)
            foreach (var preset in presets.Where(p => p.Id != "custom"))
            {
                var entry = new CommandEntry { Name = preset.Name, Value = preset.Id };

                if (string.IsNullOrEmpty(preset.ParentId))
                {
                    _topLevelItems.Add(entry);
                }
                else if (_categoryItems.TryGetValue(preset.ParentId, out var list))
                {
                    list.Add(entry);
                }
            }

            ShowTopLevel();
        }

        private void ShowTopLevel()
        {
            DisplayCommands.Clear();
            foreach (var item in _topLevelItems) DisplayCommands.Add(item);
            SelectedItem.Value = DisplayCommands.Count > 0 ? DisplayCommands[0] : null;
        }

        private void ShowCategory(string folderId)
        {
            if (_categoryItems.TryGetValue(folderId, out var items))
            {
                DisplayCommands.Clear();
                foreach (var item in items) DisplayCommands.Add(item);
                SelectedItem.Value = DisplayCommands.Count > 1 ? DisplayCommands[1] : DisplayCommands[0]; // 戻るボタンの次を選択
            }
        }

        public override bool ShouldInterceptCommit()
        {
            var val = SelectedItem.Value?.Value;
            return val != null && (val == "ACTION:BACK" || _categoryItems.ContainsKey(val));
        }

        public override void InterceptCommit()
        {
            var val = SelectedItem.Value?.Value;
            if (val == "ACTION:BACK")
            {
                ShowTopLevel();
            }
            else if (val != null && _categoryItems.ContainsKey(val))
            {
                ShowCategory(val);
            }
        }

        public override CreationStepResultBase? OnCommitted()
        {
            if (SelectedItem.Value == null) return null;
            if (ShouldInterceptCommit()) return null; // フォルダ移動時は確定しない

            var presets = _configService.LoadPresets();
            var preset = presets.FirstOrDefault(p => p.Id == SelectedItem.Value.Value);
            if (preset == null) return null;

            return new PresetSelectionResult { DataKey = _def.DataKey, SelectedPreset = preset };
        }

        public override bool HandleHorizontalNavigation(int direction)
        {
            if (direction > 0) // 右
            {
                var val = SelectedItem.Value?.Value;
                if (val != null && _categoryItems.ContainsKey(val))
                {
                    ShowCategory(val);
                    return true;
                }
            }
            else if (direction < 0) // 左
            {
                if (DisplayCommands.Any(c => c.Value == "ACTION:BACK"))
                {
                    ShowTopLevel();
                    return true;
                }
            }
            return false;
        }
    }
}
