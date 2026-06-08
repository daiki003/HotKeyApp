using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Views;

namespace HotKeyCommandApp.Services
{
    public class PresetCreationWizard
    {
        private readonly AppSettings _appSettings;

        public PresetCreationWizard(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        public CommandEntry? ShowDialog(Window owner, CommandPreset? basePreset)
        {
            var creationViewModel = new ButtonCreationViewModel(_appSettings, selectedPreset: basePreset);
            var creationWindow = new ButtonCreationWindow(creationViewModel)
            {
                Owner = owner,
                Width = owner.Width,
                Height = owner.Height
            };

            creationWindow.ShowDialog();
            return creationViewModel.ResultCommand;
        }

        public CommandPreset CreatePreset(CommandEntry command, CommandPreset? basePreset, string? folderId, IReadOnlyList<CommandPreset> existingPresets)
        {
            return new CommandPreset
            {
                Id = CreateUniquePresetId(command.Name, existingPresets),
                Name = command.Name,
                Description = $"ユーザー作成プリセット: {command.Name}",
                Category = command.Category,
                ParentId = folderId ?? basePreset?.ParentId,
                ActionOptions = CreateActionOptions(command.Behavior),
                Steps = BuildSteps(command)
            };
        }

        private static ActionRunnerOptions CreateActionOptions(CommandBehavior behavior)
        {
            return new ActionRunnerOptions
            {
                RequiresArgument = behavior.RequiresArgument ? OptionDisplayMode.AlwaysOn : OptionDisplayMode.AlwaysOff,
                IsFileSearchEnabled = behavior.IsFileSearchEnabled ? OptionDisplayMode.AlwaysOn : OptionDisplayMode.AlwaysOff,
                IsBatchMode = behavior.IsBatchMode ? OptionDisplayMode.AlwaysOn : OptionDisplayMode.AlwaysOff,
                UseWindowFocusLogic = behavior.UseWindowFocusLogic ? OptionDisplayMode.AlwaysOn : OptionDisplayMode.AlwaysOff
            };
        }

        private static List<PresetStepDefinition> BuildSteps(CommandEntry command)
        {
            List<PresetStepDefinition> steps = new()
            {
                new PresetStepDefinition
                {
                    StepType = StepType.TextInput,
                    Title = "名前入力",
                    Prompt = "ボタンの名前を入力してください:",
                    DataKey = "name"
                }
            };

            switch (command.Category)
            {
                case CommandCategory.Hierarchy:
                    return steps;

                case CommandCategory.WindowSwitcher:
                    steps.Add(new PresetStepDefinition
                    {
                        StepType = StepType.TextInput,
                        Title = "ウィンドウ選択",
                        Prompt = "切り替えるウィンドウを選択してください:",
                        DataKey = "value",
                        ListSource = "windows",
                        ShowIncrementalSearch = true
                    });
                    return steps;

                case CommandCategory.Git:
                    steps.Add(new PresetStepDefinition
                    {
                        StepType = StepType.TextInput,
                        Title = "リポジトリパス",
                        Prompt = "対象リポジトリのパスを入力してください:",
                        DataKey = "value",
                        ShowBrowseButton = true,
                        BrowseFileType = "Folder"
                    });
                    return steps;
            }

            PresetStepDefinition valueStep = CreateOpenValueStep(command);
            steps.Add(valueStep);

            if (ShouldIncludeAppSelection(command, valueStep))
            {
                steps.Add(new PresetStepDefinition
                {
                    StepType = StepType.ListSelection,
                    Title = "実行ソフト選択",
                    Prompt = "開く際に使用するソフトを選択してください (任意):",
                    DataKey = "app_path",
                    ListSource = "registered_apps"
                });
            }

            return steps;
        }

        private static PresetStepDefinition CreateOpenValueStep(CommandEntry command)
        {
            if (command.Behavior.IsBatchMode)
            {
                return new PresetStepDefinition
                {
                    StepType = StepType.TextInput,
                    Title = "バッチファイル",
                    Prompt = "実行するバッチファイルのパスを入力してください:",
                    DataKey = "value",
                    ShowBrowseButton = true,
                    ShowIncrementalSearch = true
                };
            }

            if (LooksLikeUrl(command.Value))
            {
                return new PresetStepDefinition
                {
                    StepType = StepType.TextInput,
                    Title = "URL",
                    Prompt = "URLを入力してください:",
                    DataKey = "value"
                };
            }

            if (LooksLikeFolder(command.Value))
            {
                return new PresetStepDefinition
                {
                    StepType = StepType.TextInput,
                    Title = "フォルダパス",
                    Prompt = "開くフォルダのパスを入力してください:",
                    DataKey = "value",
                    ShowBrowseButton = true,
                    ShowIncrementalSearch = true,
                    BrowseFileType = "Folder"
                };
            }

            if (!string.IsNullOrWhiteSpace(command.AppPath) || command.Behavior.UseWindowFocusLogic)
            {
                return new PresetStepDefinition
                {
                    StepType = StepType.TextInput,
                    Title = "ファイルパス",
                    Prompt = "開くファイルのパスを入力してください:",
                    DataKey = "value",
                    ShowBrowseButton = true,
                    ShowIncrementalSearch = true
                };
            }

            return new PresetStepDefinition
            {
                StepType = StepType.TextInput,
                Title = "実行パス/URL",
                Prompt = "実行するファイルのパスやURLを入力してください:",
                DataKey = "value",
                ShowBrowseButton = true,
                ShowIncrementalSearch = true
            };
        }

        private static bool ShouldIncludeAppSelection(CommandEntry command, PresetStepDefinition valueStep)
        {
            if (command.Category != CommandCategory.Open || command.Behavior.IsBatchMode)
            {
                return false;
            }

            return !LooksLikeUrl(command.Value) && valueStep.DataKey == "value";
        }

        private static bool LooksLikeUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static bool LooksLikeFolder(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Directory.Exists(value) ||
                   value.EndsWith("\\", StringComparison.Ordinal) ||
                   value.EndsWith("/", StringComparison.Ordinal);
        }

        private static string CreateUniquePresetId(string name, IReadOnlyList<CommandPreset> existingPresets)
        {
            string baseId = SanitizeId(name);
            string candidate = baseId;
            int suffix = 2;

            while (existingPresets.Any(p => string.Equals(p.Id, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{baseId}_{suffix}";
                suffix++;
            }

            return candidate;
        }

        private static string SanitizeId(string name)
        {
            StringBuilder builder = new();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
                else if (builder.Length == 0 || builder[^1] != '_')
                {
                    builder.Append('_');
                }
            }

            string result = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(result) ? $"user_{DateTime.Now:yyyyMMddHHmmssfff}" : $"user_{result}";
        }
    }
}
