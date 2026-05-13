using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.ViewModels.ButtonCreationSteps;
using R3;

namespace HotKeyCommandApp.ViewModels
{
    public class StepViewModel : IDisposable
    {
        public BindableReactiveProperty<ICreationStep> Step { get; } = new();
        public BindableReactiveProperty<bool> IsCurrent { get; } = new(false);
        public BindableReactiveProperty<string> Title { get; }
        private readonly DisposableBag _disposables = new();

        public StepViewModel()
        {
            Title = Step.Select(s => s?.Title ?? "").ToBindableReactiveProperty("");

            Step.AddTo(ref _disposables);
            IsCurrent.AddTo(ref _disposables);
            Title.AddTo(ref _disposables);
        }

        public void Dispose() => _disposables.Dispose();
    }

    public class ButtonCreationViewModel : IDisposable
    {
        private readonly ConfigService _configService;
        private readonly WindowService _windowService;
        private readonly AppSettings _appSettings;
        private readonly DisposableBag _disposables = new();

        public CommandEntry? ResultCommand { get; private set; }

        private readonly ObservableCollection<StepViewModel> _steps = new();
        public ObservableCollection<StepViewModel> Steps => _steps;
        public ObservableCollection<StepViewModel> PageIndicatorSteps { get; } = new();

        public Dictionary<string, CreationStepResultBase> StepResults { get; } = new();

        private readonly BindableReactiveProperty<int> _currentStepIndex = new(0);
        public int CurrentStepIndex
        {
            get => _currentStepIndex.Value;
            private set => _currentStepIndex.Value = value;
        }

        public BindableReactiveProperty<ICreationStep?> CurrentStepObject { get; }
        private readonly BindableReactiveProperty<CommandPreset?> _currentPreset = new(null);

        public BindableReactiveProperty<string> Title { get; }
        public BindableReactiveProperty<string> InputPrompt { get; }
        public BindableReactiveProperty<bool> IsListBoxVisible { get; }
        public BindableReactiveProperty<bool> IsTextInputVisible { get; }
        public BindableReactiveProperty<string> InputText { get; } = new("");
        public BindableReactiveProperty<ObservableCollection<CommandEntry>> DisplayCommands { get; }
        public BindableReactiveProperty<CommandEntry?> SelectedItem { get; private set; } = new(null);
        public BindableReactiveProperty<bool> IsSearching { get; }
        private IDisposable? _stepSubscription;
        public BindableReactiveProperty<bool> IsItemSelected { get; }
        public BindableReactiveProperty<bool> ShowStepIndicators { get; }
        public BindableReactiveProperty<bool> ShowMainBrowseButton { get; }

        // 詳細オプションの表示制御
        public BindableReactiveProperty<bool> ShowArgumentOption { get; }
        public BindableReactiveProperty<bool> ShowFileSearchOption { get; }
        public BindableReactiveProperty<bool> ShowBatchModeOption { get; }
        public BindableReactiveProperty<bool> ShowFocusLogicOption { get; }

        private CommandEntry? _editingCommand;
        public CommandEntry? EditingCommand => _editingCommand;

        public CommandCategory NewEntryCategory { get; set; } = CommandCategory.Open;
        public string? NewEntryTemplateId { get; set; }

        public double ButtonWidth => _appSettings.ButtonWidth;
        public double ButtonHeight => _appSettings.ButtonHeight;
        public double FontSize => _appSettings.FontSize;

        public event Action<string>? RequestControlFocus;
        public event Action? RequestClose;

        public ICommand CommitInputCommand => new RelayCommand<object>(_ => CommitInput());
        public ICommand CancelInputCommand => new RelayCommand<object>(_ => CancelInput());
        public ICommand MoveToPreviousStepCommand => new RelayCommand<object>(_ => MoveToPreviousStep());
        public ICommand MoveToNextStepCommand => new RelayCommand<object>(_ => MoveToNextStep());
        public ICommand BrowseFileCommand => new RelayCommand<object>(_ => BrowseFile());
        public ICommand ExecuteCommand => new RelayCommand<CommandEntry>(Execute);
        public ICommand MoveUpCommand => new RelayCommand<object>(_ => MoveSelection(-1));
        public ICommand MoveDownCommand => new RelayCommand<object>(_ => MoveSelection(1));
        public ICommand MoveLeftCommand => new RelayCommand<object>(_ => CurrentStepObject.Value?.HandleHorizontalNavigation(-1));
        public ICommand MoveRightCommand => new RelayCommand<object>(_ => CurrentStepObject.Value?.HandleHorizontalNavigation(1));
        public ICommand DeleteRegisteredAppCommand => new RelayCommand<CommandEntry>(DeleteRegisteredApp);
        public ICommand EditRegisteredAppCommand => new RelayCommand<CommandEntry>(EditRegisteredApp);
        public ICommand AddRegisteredAppCommand => new RelayCommand<object>(_ => AddRegisteredApp());

        public ButtonCreationViewModel(AppSettings appSettings, CommandEntry? editingCommand = null)
        {
            _configService = new ConfigService();
            _windowService = new WindowService();
            _appSettings = appSettings;
            _editingCommand = editingCommand;

            CurrentStepObject = new BindableReactiveProperty<ICreationStep?>(null);
            _currentPreset.AddTo(ref _disposables);

            Title = CurrentStepObject.CombineLatest(_currentPreset, (step, preset) =>
            {
                if (step == null) return "ボタン作成";
                if (preset == null || step is PresetSelectionStep) return step.Title;
                return preset.Name;
            }).ToBindableReactiveProperty("ボタン作成");
            InputPrompt = CurrentStepObject.Select(s => s?.Prompt ?? "").ToBindableReactiveProperty("");

            IsListBoxVisible = CurrentStepObject
                .Select(s => s == null ? Observable.Return(false) : (Observable<bool>)s.IsListBoxVisible)
                .Switch()
                .ToBindableReactiveProperty(false);

            IsTextInputVisible = CurrentStepObject.Select(s => s?.IsTextInputVisible ?? true).ToBindableReactiveProperty(true);

            DisplayCommands = CurrentStepObject.Select(s => s?.DisplayCommands ?? new ObservableCollection<CommandEntry>()).ToBindableReactiveProperty(new ObservableCollection<CommandEntry>());

            IsSearching = CurrentStepObject
                .Select(s => s == null ? Observable.Return(false) : (Observable<bool>)s.IsSearching)
                .Switch()
                .ToBindableReactiveProperty(false);

            IsItemSelected = SelectedItem.Select(i => i != null).ToBindableReactiveProperty(false);

            ShowStepIndicators = CurrentStepObject.Select(s => 
                s != null && s.ShowInPageIndicator && PageIndicatorSteps.Count > 1
            ).ToBindableReactiveProperty(false);
            ShowMainBrowseButton = CurrentStepObject.Select(s => s?.ShowBrowseButton ?? false).ToBindableReactiveProperty(false);

            // カテゴリやテンプレートに応じてオプションを表示
            ShowArgumentOption = CurrentStepObject.Select(s =>
            {
                if (s is OptionsCreationStep os)
                {
                    return os.IsRequiresArgumentVisible.CombineLatest(os.IsBatchModeChecked, (visible, isBatch) => visible && isBatch);
                }
                return Observable.Return(false);
            }).Switch().ToBindableReactiveProperty(false);

            ShowFileSearchOption = CurrentStepObject.Select(s =>
            {
                if (s is OptionsCreationStep os) return (Observable<bool>)os.IsFileSearchVisible;
                return Observable.Return(false);
            }).Switch().ToBindableReactiveProperty(false);

            ShowBatchModeOption = CurrentStepObject.Select(s =>
            {
                if (s is OptionsCreationStep os) return (Observable<bool>)os.IsBatchModeVisible;
                return Observable.Return(false);
            }).Switch().ToBindableReactiveProperty(false);

            ShowFocusLogicOption = CurrentStepObject.Select(s =>
            {
                if (s is OptionsCreationStep os) return (Observable<bool>)os.IsFocusLogicVisible;
                return Observable.Return(false);
            }).Switch().ToBindableReactiveProperty(false);

            CurrentStepObject.Subscribe(step =>
            {
                if (step == null) return;

                _stepSubscription?.Dispose();
                var bag = new DisposableBag();

                step.InputText.Subscribe(v => InputText.Value = v).AddTo(ref bag);
                InputText.Subscribe(v => step.InputText.Value = v).AddTo(ref bag);

                step.SelectedItem.Subscribe(v => SelectedItem.Value = v).AddTo(ref bag);
                SelectedItem.Subscribe(v => step.SelectedItem.Value = v).AddTo(ref bag);

                step.Initialize(StepResults);
                step.RequestControlFocus += OnStepRequestControlFocus;

                _stepSubscription = bag;
            }).AddTo(ref _disposables);

            StartFlow();
            _currentStepIndex.Subscribe(index => NavigateToStep(index)).AddTo(ref _disposables);
        }

        public void Dispose()
        {
            foreach (var step in _steps) step.Dispose();
            _disposables.Dispose();
        }

        private void StartFlow()
        {
            StepResults.Clear();
            if (EditingCommand is { } command)
            {
                NewEntryCategory = command.Category;

                // プリセットを推測する
                var allPresets = _configService.LoadPresets();
                
                // 特定の ID でマッチさせるロジックがないため、挙動から推測
                // Category が一致し、かつ Behavior が矛盾しないプリセットを探す
                var matchedPreset = allPresets
                    .Where(p => p.Category == command.Category && p.Id != "custom")
                    .FirstOrDefault(p => p.ActionOptions == null || p.ActionOptions.IsCompatible(command.Behavior));

                // 一致するものがあればそのプリセットを使用、なければ custom にフォールバック
                NewEntryTemplateId = matchedPreset?.Id ?? "custom";

                StepResults["name"] = new TextInputResult { DataKey = "name", Text = command.Name };
                StepResults["value"] = new TextInputResult { DataKey = "value", Text = command.Value };
                StepResults["app_path"] = new TextInputResult { DataKey = "app_path", Text = command.AppPath ?? string.Empty };
                StepResults["behaviors"] = new OptionsResult
                {
                    DataKey = "behaviors",
                    RequiresArgument = command.Behavior.RequiresArgument,
                    IsFileSearchEnabled = command.Behavior.IsFileSearchEnabled,
                    IsBatchMode = command.Behavior.IsBatchMode,
                    UseWindowFocusLogic = command.Behavior.UseWindowFocusLogic
                };
            }
            UpdateStepSequence();
        }

        private void UpdateStepSequence()
        {
            var baseSteps = new List<ICreationStep>();
            var allPresets = _configService.LoadPresets();

            // 新規作成時（またはテンプレート選択前）は、常に機能選択を最初のステップとして保持する
            if (EditingCommand == null)
            {
                var presetStep = new PresetStepDefinition
                {
                    Title = "プリセット選択",
                    Prompt = "機能を選択してください",
                    StepType = StepType.PresetSelection,
                    DataKey = "preset"
                };
                baseSteps.Add(new PresetSelectionStep(presetStep, _configService));
            }

            // プリセットが選択されている、または編集モードの場合は後続のステップを追加
            if (NewEntryTemplateId != null || EditingCommand != null)
            {
                var preset = allPresets.FirstOrDefault(p => p.Id == NewEntryTemplateId)
                             ?? allPresets.FirstOrDefault(p => p.Category == NewEntryCategory)
                             ?? allPresets[0];

                _currentPreset.Value = preset;

                foreach (var stepDef in preset.Steps)
                {
                    switch (stepDef.StepType)
                    {
                        case StepType.TextInput:
                            baseSteps.Add(new TextInputCreationStep(stepDef, _windowService, NewEntryCategory));
                            break;
                        case StepType.ListSelection:
                            baseSteps.Add(new ListSelectionCreationStep(stepDef, _configService, _windowService, _appSettings));
                            break;
                        case StepType.Options:
                            baseSteps.Add(new OptionsCreationStep(stepDef, preset));
                            break;
                        case StepType.PresetSelection:
                            baseSteps.Add(new PresetSelectionStep(stepDef, _configService));
                            break;
                    }
                }

                // オプション画面の動的追加
                if (preset.ActionOptions != null &&
                    (preset.ActionOptions.RequiresArgument == OptionDisplayMode.Custom ||
                     preset.ActionOptions.IsFileSearchEnabled == OptionDisplayMode.Custom ||
                     preset.ActionOptions.IsBatchMode == OptionDisplayMode.Custom ||
                     preset.ActionOptions.UseWindowFocusLogic == OptionDisplayMode.Custom))
                {
                    // 既にStepsにOptionsが含まれている場合は二重に追加しない
                    if (!preset.Steps.Any(s => s.StepType == StepType.Options))
                    {
                        var optionsStepDef = new PresetStepDefinition
                        {
                            StepType = StepType.Options,
                            Title = "動作詳細",
                            Prompt = "動作オプションを設定してください:",
                            DataKey = "behaviors"
                        };
                        baseSteps.Add(new OptionsCreationStep(optionsStepDef, preset));
                    }
                }
            }
            else
            {
                _currentPreset.Value = null;
            }

            foreach (var step in _steps) step.Dispose();
            _steps.Clear();
            PageIndicatorSteps.Clear();
            foreach (var step in baseSteps)
            {
                var vm = new StepViewModel { Step = { Value = step } };
                _steps.Add(vm);
                if (step.ShowInPageIndicator)
                {
                    PageIndicatorSteps.Add(vm);
                }
            }
        }

        private void NavigateToStep(int index)
        {
            if (index < 0 || index >= _steps.Count) return;

            foreach (var s in _steps) s.IsCurrent.Value = false;
            _steps[index].IsCurrent.Value = true;
            CurrentStepObject.Value = _steps[index].Step.Value;

            // TextInputCreationStep の場合は、リストが表示されていても入力を優先するため TextBox にフォーカスを当てる
            bool preferTextBox = IsListBoxVisible.Value == false || (CurrentStepObject.Value is TextInputCreationStep);
            RequestControlFocus?.Invoke(preferTextBox ? "InputTextBox" : "CommandListBox");
        }

        private void OnStepRequestControlFocus(string controlName) => RequestControlFocus?.Invoke(controlName);

        private void MoveToPreviousStep()
        {
            SaveCurrentStepResult();
            if (CurrentStepIndex > 0) CurrentStepIndex--;
        }

        private void MoveToNextStep()
        {
            if (CurrentStepObject.Value is PresetSelectionStep) return;
            if (SaveCurrentStepResult() && CurrentStepIndex < _steps.Count - 1)
                CurrentStepIndex++;
        }

        private bool SaveCurrentStepResult()
        {
            if (CurrentStepObject.Value == null) return false;
            var result = CurrentStepObject.Value.OnCommitted();

            if (result != null)
            {
                StepResults[result.DataKey] = result;
                if (result is PresetSelectionResult psr)
                {
                    // プリセットが変更された場合のみ、後続のステップを再構築し、既存の入力をクリアする
                    if (NewEntryTemplateId != psr.SelectedPreset.Id)
                    {
                        // 機能選択以外の入力結果をクリア
                        var otherKeys = StepResults.Keys.Where(k => k != result.DataKey).ToList();
                        foreach (var key in otherKeys) StepResults.Remove(key);

                        NewEntryTemplateId = psr.SelectedPreset.Id;
                        NewEntryCategory = psr.SelectedPreset.Category;
                        UpdateStepSequence();
                    }
                }
                return true;
            }
            return false;
        }

        private void CommitInput()
        {
            if (CurrentStepObject.Value != null && CurrentStepObject.Value.ShouldInterceptCommit())
            {
                CurrentStepObject.Value.InterceptCommit();
                return;
            }

            // まず現在のステップを確定・保存する
            if (SaveCurrentStepResult())
            {
                // 保存後に動的にステップが増えている可能性があるため、改めて index と count を比較
                if (CurrentStepIndex < _steps.Count - 1)
                {
                    CurrentStepIndex++;
                }
                else
                {
                    FinishFlow();
                }
            }
        }

        private bool HasPlaceholder(string? val)
        {
            if (string.IsNullOrEmpty(val)) return false;
            for (int i = 0; i < 10; i++)
            {
                if (val.Contains($"{{{i}}}")) return true;
            }
            return false;
        }

        private void FinishFlow()
        {
            var name = (StepResults.GetValueOrDefault("name") as TextInputResult)?.Text;
            var value = (StepResults.GetValueOrDefault("value") as TextInputResult)?.Text;
            var behaviors = StepResults.GetValueOrDefault("behaviors") as OptionsResult;

            // value が無い場合、他のテキスト入力を探す（プリセット独自のキー対応）
            if (string.IsNullOrEmpty(value))
            {
                // Slack などの特殊対応
                if (NewEntryTemplateId == "slack")
                {
                    var team = (StepResults.GetValueOrDefault("slack_team") as TextInputResult)?.Text;
                    var channel = (StepResults.GetValueOrDefault("slack_channel") as TextInputResult)?.Text;
                    if (!string.IsNullOrEmpty(team) && !string.IsNullOrEmpty(channel))
                    {
                        value = $"slack://channel?team={team}&id={channel}";
                    }
                }
                else
                {
                    value = StepResults.Values.OfType<TextInputResult>()
                        .FirstOrDefault(r => r.DataKey != "name")?.Text;
                }
            }

            string? appPath = null;
            if (StepResults.GetValueOrDefault("app_path") is ListSelectionResult lsr && lsr.SelectedItem != null)
            {
                appPath = lsr.SelectedItem.Value == "NONE" ? null : lsr.SelectedItem.Value;
            }
            else if (StepResults.GetValueOrDefault("app_path") is TextInputResult tir)
            {
                appPath = tir.Text;
            }

            if (string.IsNullOrWhiteSpace(name)) return;
            
            // 階層（Hierarchy）カテゴリ以外は value が必須
            if (NewEntryCategory != CommandCategory.Hierarchy && string.IsNullOrWhiteSpace(value)) return;

            // プリセットからデフォルト動作を取得
            var allPresets = _configService.LoadPresets();
            var preset = allPresets.FirstOrDefault(p => p.Id == NewEntryTemplateId);

            ResultCommand = new CommandEntry
            {
                Name = name,
                Value = value ?? string.Empty,
                AppPath = appPath,
                Category = NewEntryCategory,
                Hotkey = EditingCommand?.Hotkey, // ショートカットを継承
                HotkeyType = EditingCommand?.HotkeyType ?? HotkeyType.WindowGlobal,
                Icon = EditingCommand?.Icon, // アイコンを継承
                Behavior = new CommandBehavior
                {
                    RequiresArgument = (behaviors?.IsBatchMode ?? (preset?.ActionOptions?.IsBatchMode == OptionDisplayMode.AlwaysOn)) 
                        ? (behaviors?.RequiresArgument ?? (preset?.ActionOptions?.RequiresArgument == OptionDisplayMode.AlwaysOn)) 
                        : HasPlaceholder(value),
                    IsFileSearchEnabled = behaviors?.IsFileSearchEnabled ?? (preset?.ActionOptions?.IsFileSearchEnabled == OptionDisplayMode.AlwaysOn),
                    IsBatchMode = behaviors?.IsBatchMode ?? (preset?.ActionOptions?.IsBatchMode == OptionDisplayMode.AlwaysOn),
                    UseWindowFocusLogic = behaviors?.UseWindowFocusLogic ?? (preset?.ActionOptions?.UseWindowFocusLogic == OptionDisplayMode.AlwaysOn)
                },
                Children = NewEntryCategory == CommandCategory.Hierarchy 
                    ? (EditingCommand?.Children ?? new List<CommandEntry>()) 
                    : null,
                ArgumentsHistory = EditingCommand?.ArgumentsHistory ?? new List<string>()
            };
            RequestClose?.Invoke();
        }

        private void CancelInput() => RequestClose?.Invoke();

        private void BrowseFile()
        {
            if (CurrentStepObject.Value == null) return;
            var filter = CurrentStepObject.Value.BrowseFileType == BrowseFileType.Folder ? "Folders|*.none" : "All Files (*.*)|*.*";
            var path = _windowService.ShowBrowseDialog(CurrentStepObject.Value.BrowseFileType == BrowseFileType.Folder, filter, InputText.Value);
            if (!string.IsNullOrEmpty(path))
            {
                InputText.Value = path;
                _ = CurrentStepObject.Value.PerformSearchAsync(path);
            }
        }

        private void Execute(CommandEntry? command)
        {
            if (command == null) return;
            if (command.Value == "ADD_REGISTERED_APP") { AddRegisteredApp(); return; }
            InputText.Value = command.Value;

            if (CurrentStepObject.Value is TextInputCreationStep)
            {
                // インクリメンタルサーチの場合は確定せず、テキストを入力してフォーカスを戻す
                SelectedItem.Value = null;
                RequestControlFocus?.Invoke("InputTextBox");
            }
            else
            {
                CommitInput();
            }
        }

        private void MoveSelection(int direction)
        {
            if (DisplayCommands.Value.Count == 0) return;

            // テキスト入力ボックスが非表示の場合（リスト選択のみのステップなど）は、常にリスト内でラップさせる
            if (!IsTextInputVisible.Value)
            {
                int idx = SelectedItem.Value == null ? 0 : DisplayCommands.Value.IndexOf(SelectedItem.Value);
                idx += direction;
                if (idx < 0) idx = DisplayCommands.Value.Count - 1;
                else if (idx >= DisplayCommands.Value.Count) idx = 0;
                SelectedItem.Value = DisplayCommands.Value[idx];
                return;
            }

            if (SelectedItem.Value == null)
            {
                // 入力ボックスが選択されている状態（SelectedItem == null）
                if (direction > 0)
                {
                    // 下キー: 一番上に移動
                    SelectedItem.Value = DisplayCommands.Value[0];
                }
                else if (direction < 0)
                {
                    // 上キー: 一番下に移動 (一周)
                    SelectedItem.Value = DisplayCommands.Value[DisplayCommands.Value.Count - 1];
                }
                RequestControlFocus?.Invoke("InputTextBox");
                return;
            }

            int index = DisplayCommands.Value.IndexOf(SelectedItem.Value);
            int nextIndex = index + direction;

            if (nextIndex < 0 || nextIndex >= DisplayCommands.Value.Count)
            {
                // 選択肢の一番上で上キー、または一番下で下キー -> 入力ボックスに戻る (nullにする)
                SelectedItem.Value = null;
                RequestControlFocus?.Invoke("InputTextBox");
            }
            else
            {
                SelectedItem.Value = DisplayCommands.Value[nextIndex];
            }
        }

        private void DeleteRegisteredApp(CommandEntry? app)
        {
            if (app == null) return;
            var target = _appSettings.RegisteredApps.FirstOrDefault(a => a.Path == app.Value);
            if (target != null)
            {
                _appSettings.RegisteredApps.Remove(target);
                _configService.SaveSettings(_appSettings);
                CurrentStepObject.Value?.Initialize(StepResults);
            }
        }

        private void EditRegisteredApp(CommandEntry? app) { }

        private void AddRegisteredApp()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "登録するアプリを選択してください", Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*" };
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (Directory.Exists(pf)) dialog.InitialDirectory = pf;

            if (dialog.ShowDialog() == true)
            {
                string path = dialog.FileName, name;
                try { var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path); name = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName : Path.GetFileNameWithoutExtension(path); }
                catch { name = Path.GetFileNameWithoutExtension(path); }

                if (!_appSettings.RegisteredApps.Any(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                {
                    _appSettings.RegisteredApps.Add(new RegisteredApp { Name = name, Path = path });
                    _configService.SaveSettings(_appSettings);
                }
                CurrentStepObject.Value?.Initialize(StepResults);
                SelectedItem.Value = DisplayCommands.Value.FirstOrDefault(c => c.Value == path);
            }
        }
    }
}
