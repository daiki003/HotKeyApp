using System.Collections.Generic;
using HotKeyCommandApp.Models;
using R3;

namespace HotKeyCommandApp.ViewModels.ButtonCreationSteps
{
    public class OptionsCreationStep : CreationStepBase
    {
        private readonly PresetStepDefinition _def;
        private readonly CommandPreset? _preset;

        public override string Title => _def.Title;
        public override string Prompt => _def.Prompt;
        public override bool IsTextInputVisible => false;
        public override bool ShowBehaviorOptions => true;

        public BindableReactiveProperty<bool> IsRequiresArgumentChecked { get; } = new(false);
        public BindableReactiveProperty<bool> IsFileSearchChecked { get; } = new(false);
        public BindableReactiveProperty<bool> IsBatchModeChecked { get; } = new(false);
        public BindableReactiveProperty<bool> UseWindowFocusLogicChecked { get; } = new(false);

        public BindableReactiveProperty<bool> IsRequiresArgumentVisible { get; } = new(true);
        public BindableReactiveProperty<bool> IsFileSearchVisible { get; } = new(true);
        public BindableReactiveProperty<bool> IsBatchModeVisible { get; } = new(true);
        public BindableReactiveProperty<bool> IsFocusLogicVisible { get; } = new(true);

        public OptionsCreationStep(PresetStepDefinition def, CommandPreset? preset = null)
        {
            _def = def;
            _preset = preset;
            
            IsRequiresArgumentChecked.AddTo(ref _disposables);
            IsFileSearchChecked.AddTo(ref _disposables);
            IsBatchModeChecked.AddTo(ref _disposables);
            UseWindowFocusLogicChecked.AddTo(ref _disposables);

            IsRequiresArgumentVisible.AddTo(ref _disposables);
            IsFileSearchVisible.AddTo(ref _disposables);
            IsBatchModeVisible.AddTo(ref _disposables);
            IsFocusLogicVisible.AddTo(ref _disposables);
        }

        public override void Initialize(IDictionary<string, CreationStepResultBase> results)
        {
            DisplayCommands.Clear();
            
            // 初期値の復元
            if (results.TryGetValue(_def.DataKey, out var result) && result is OptionsResult optionsResult)
            {
                IsRequiresArgumentChecked.Value = optionsResult.RequiresArgument;
                IsFileSearchChecked.Value = optionsResult.IsFileSearchEnabled;
                IsBatchModeChecked.Value = optionsResult.IsBatchMode;
                UseWindowFocusLogicChecked.Value = optionsResult.UseWindowFocusLogic;
            }
            else
            {
                IsRequiresArgumentChecked.Value = _preset?.ActionOptions?.RequiresArgument == OptionDisplayMode.AlwaysOn;
                IsFileSearchChecked.Value = _preset?.ActionOptions?.IsFileSearchEnabled == OptionDisplayMode.AlwaysOn;
                IsBatchModeChecked.Value = _preset?.ActionOptions?.IsBatchMode == OptionDisplayMode.AlwaysOn;
                UseWindowFocusLogicChecked.Value = _preset?.ActionOptions?.UseWindowFocusLogic == OptionDisplayMode.AlwaysOn;
            }

            // モードに応じた値の適用と可視性の設定
            ApplyOptionMode(_preset?.ActionOptions?.RequiresArgument ?? OptionDisplayMode.AlwaysOff, IsRequiresArgumentChecked, IsRequiresArgumentVisible);
            ApplyOptionMode(_preset?.ActionOptions?.IsFileSearchEnabled ?? OptionDisplayMode.AlwaysOff, IsFileSearchChecked, IsFileSearchVisible);
            ApplyOptionMode(_preset?.ActionOptions?.IsBatchMode ?? OptionDisplayMode.AlwaysOff, IsBatchModeChecked, IsBatchModeVisible);
            ApplyOptionMode(_preset?.ActionOptions?.UseWindowFocusLogic ?? OptionDisplayMode.AlwaysOff, UseWindowFocusLogicChecked, IsFocusLogicVisible);
        }

        private void ApplyOptionMode(OptionDisplayMode mode, BindableReactiveProperty<bool> checkedProp, BindableReactiveProperty<bool> visibleProp)
        {
            switch (mode)
            {
                case OptionDisplayMode.AlwaysOn:
                    checkedProp.Value = true;
                    visibleProp.Value = false;
                    break;
                case OptionDisplayMode.AlwaysOff:
                    checkedProp.Value = false;
                    visibleProp.Value = false;
                    break;
                case OptionDisplayMode.Custom:
                    // 値は変更せず（初期値または復元された値を使用）、表示のみONにする
                    visibleProp.Value = true;
                    break;
            }
        }

        public override CreationStepResultBase? OnCommitted()
        {
            return new OptionsResult
            {
                DataKey = _def.DataKey,
                RequiresArgument = IsRequiresArgumentChecked.Value,
                IsFileSearchEnabled = IsFileSearchChecked.Value,
                IsBatchMode = IsBatchModeChecked.Value,
                UseWindowFocusLogic = UseWindowFocusLogicChecked.Value
            };
        }
    }
}
