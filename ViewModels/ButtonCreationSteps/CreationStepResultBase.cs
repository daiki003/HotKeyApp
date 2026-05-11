using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.ViewModels.ButtonCreationSteps
{
    public abstract class CreationStepResultBase
    {
        // どの項目の入力結果かを識別するキー（元のDataKey）
        public string DataKey { get; init; } = "";
    }

    public class TextInputResult : CreationStepResultBase
    {
        public string Text { get; init; } = "";
    }

    public class ListSelectionResult : CreationStepResultBase
    {
        public CommandEntry? SelectedItem { get; init; }
    }

    public class OptionsResult : CreationStepResultBase
    {
        public bool RequiresArgument { get; init; }
        public bool IsFileSearchEnabled { get; init; }
        public bool IsBatchMode { get; init; }
        public bool UseWindowFocusLogic { get; init; }
    }

    // プリセット選択時の特殊な結果
    public class PresetSelectionResult : CreationStepResultBase
    {
        public CommandPreset SelectedPreset { get; init; } = null!;
    }
    // カテゴリ選択時の特殊な結果
    public class CategorySelectionResult : CreationStepResultBase
    {
        public CommandCategory SelectedCategory { get; init; }
    }
}
