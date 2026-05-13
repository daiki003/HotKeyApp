using System.Collections.Generic;

namespace HotKeyCommandApp.Models
{
    /// <summary>
    /// ステップでのUI操作の種別
    /// </summary>
    public enum StepType
    {
        TextInput,      // テキストを入力する
        ListSelection,  // リストから1つ選ぶ
        Options,        // チェックボックスで動作フラグを設定する
        PresetSelection // プリセットを選択する
    }

    public enum OptionDisplayMode
    {
        AlwaysOn,  // 常にON（表示しない）
        AlwaysOff, // 常にOFF（表示しない）
        Custom     // カスタム（表示する）
    }

    /// <summary>
    /// プリセット内のステップ1件分の定義
    /// </summary>
    public class PresetStepDefinition
    {
        public StepType StepType { get; set; }

        /// <summary>ステップ一覧に表示されるタイトル</summary>
        public string Title { get; set; } = "";

        /// <summary>入力画面に表示されるプロンプト文言</summary>
        public string Prompt { get; set; } = "";

        /// <summary>
        /// ViewModelのどのデータと連携するか
        /// 例: "name", "value", "app_path", "slack_team", "slack_channel", "behaviors", "category", "preset"
        /// </summary>
        public string DataKey { get; set; } = "";

        // --- TextInput 用オプション ---
        /// <summary>参照ボタン（ファイルダイアログ）を表示するか</summary>
        public bool ShowBrowseButton { get; set; }
        /// <summary>入力に応じてリストをインクリメンタル検索するか</summary>
        public bool ShowIncrementalSearch { get; set; }
        /// <summary>参照ボタン押下時のモード ("File" または "Folder")</summary>
        public string BrowseFileType { get; set; } = "File";

        // --- ListSelection 用オプション ---
        /// <summary>
        /// リストの生成元
        /// 例: "categories", "presets", "registered_apps", "windows"
        /// </summary>
        public string? ListSource { get; set; }
    }

    public class ActionRunnerOptions
    {
        public OptionDisplayMode RequiresArgument { get; set; } = OptionDisplayMode.Custom;
        public OptionDisplayMode IsFileSearchEnabled { get; set; } = OptionDisplayMode.Custom;
        public OptionDisplayMode IsBatchMode { get; set; } = OptionDisplayMode.Custom;
        public OptionDisplayMode UseWindowFocusLogic { get; set; } = OptionDisplayMode.Custom;

        public bool IsCompatible(CommandBehavior behavior)
        {
            if (RequiresArgument == OptionDisplayMode.AlwaysOn && !behavior.RequiresArgument) return false;
            if (RequiresArgument == OptionDisplayMode.AlwaysOff && behavior.RequiresArgument) return false;

            if (IsFileSearchEnabled == OptionDisplayMode.AlwaysOn && !behavior.IsFileSearchEnabled) return false;
            if (IsFileSearchEnabled == OptionDisplayMode.AlwaysOff && behavior.IsFileSearchEnabled) return false;

            if (IsBatchMode == OptionDisplayMode.AlwaysOn && !behavior.IsBatchMode) return false;
            if (IsBatchMode == OptionDisplayMode.AlwaysOff && behavior.IsBatchMode) return false;

            if (UseWindowFocusLogic == OptionDisplayMode.AlwaysOn && !behavior.UseWindowFocusLogic) return false;
            if (UseWindowFocusLogic == OptionDisplayMode.AlwaysOff && behavior.UseWindowFocusLogic) return false;

            return true;
        }
    }

    public class CommandPreset
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public CommandCategory Category { get; set; }
        public string Description { get; set; } = string.Empty;

        public ActionRunnerOptions? ActionOptions { get; set; }

        public string? Icon { get; set; }

        public bool IsFolder { get; set; }
        public string? ParentId { get; set; }

        /// <summary>このプリセットで実行するステップの定義リスト</summary>
        public List<PresetStepDefinition> Steps { get; set; } = new();

        public static List<CommandPreset> GetDefaults()
        {
            return new List<CommandPreset>
            {
                new CommandPreset
                {
                    Id = "FOLDER:OPEN",
                    Name = "開く",
                    IsFolder = true
                },
                new CommandPreset
                {
                    Id = "url",
                    Name = "URLを開く",
                    Category = CommandCategory.Open,
                    ParentId = "FOLDER:OPEN",
                    ActionOptions = new ActionRunnerOptions
                    {
                        RequiresArgument = OptionDisplayMode.AlwaysOff,
                        IsFileSearchEnabled = OptionDisplayMode.AlwaysOff,
                        IsBatchMode = OptionDisplayMode.AlwaysOff,
                        UseWindowFocusLogic = OptionDisplayMode.AlwaysOff,
                    },
                    Steps = new List<PresetStepDefinition>
                    {
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "名前入力", Prompt = "ボタンの名前を入力してください:", DataKey = "name" },
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "URL", Prompt = "URLを入力してください:", DataKey = "value", ShowBrowseButton = false, ShowIncrementalSearch = false },
                    }
                },
                new CommandPreset
                {
                    Id = "file",
                    Name = "ファイルを開く",
                    Category = CommandCategory.Open,
                    ParentId = "FOLDER:OPEN",
                    ActionOptions = new ActionRunnerOptions
                    {
                        RequiresArgument = OptionDisplayMode.AlwaysOff,
                        IsBatchMode = OptionDisplayMode.AlwaysOff,
                        UseWindowFocusLogic = OptionDisplayMode.AlwaysOn,
                    },
                    Steps = new List<PresetStepDefinition>
                    {
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "名前入力", Prompt = "ボタンの名前を入力してください:", DataKey = "name" },
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "ファイルパス", Prompt = "開くファイルのパスを入力してください:", DataKey = "value", ShowBrowseButton = true, ShowIncrementalSearch = true },
                        new PresetStepDefinition { StepType = StepType.ListSelection, Title = "実行ソフト選択", Prompt = "開く際に使用するソフトを選択してください (任意):", DataKey = "app_path", ListSource = "registered_apps" },
                    }
                },
                new CommandPreset
                {
                    Id = "folder",
                    Name = "フォルダを開く",
                    Category = CommandCategory.Open,
                    ParentId = "FOLDER:OPEN",
                    ActionOptions = new ActionRunnerOptions
                    {
                        RequiresArgument = OptionDisplayMode.AlwaysOff,
                        IsBatchMode = OptionDisplayMode.AlwaysOff,
                        UseWindowFocusLogic = OptionDisplayMode.AlwaysOn,
                    },
                    Steps = new List<PresetStepDefinition>
                    {
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "名前入力", Prompt = "ボタンの名前を入力してください:", DataKey = "name" },
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "フォルダパス", Prompt = "開くフォルダのパスを入力してください:", DataKey = "value", ShowBrowseButton = true, ShowIncrementalSearch = true, BrowseFileType = "Folder" },
                        new PresetStepDefinition { StepType = StepType.ListSelection, Title = "実行ソフト選択", Prompt = "開く際に使用するソフトを選択してください (任意):", DataKey = "app_path", ListSource = "registered_apps" },
                    }
                },
                new CommandPreset
                {
                    Id = "batch",
                    Name = "バッチ実行",
                    Category = CommandCategory.Open,
                    ActionOptions = new ActionRunnerOptions
                    {
                        IsFileSearchEnabled = OptionDisplayMode.AlwaysOff,
                        IsBatchMode = OptionDisplayMode.AlwaysOn,
                        UseWindowFocusLogic = OptionDisplayMode.AlwaysOff,
                    },
                    Steps = new List<PresetStepDefinition>
                    {
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "名前入力", Prompt = "ボタンの名前を入力してください:", DataKey = "name" },
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "バッチファイル", Prompt = "実行するバッチファイルのパスを入力してください:", DataKey = "value", ShowBrowseButton = true, ShowIncrementalSearch = true },
                    }
                },
                new CommandPreset
                {
                    Id = "custom",
                    Name = "カスタム (詳細設定)",
                    Category = CommandCategory.Open,
                    ActionOptions = new ActionRunnerOptions(),
                    Steps = new List<PresetStepDefinition>
                    {
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "名前入力", Prompt = "ボタンの名前を入力してください:", DataKey = "name" },
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "実行パス/URL", Prompt = "実行するファイルのパスやURLを入力してください:", DataKey = "value", ShowBrowseButton = true, ShowIncrementalSearch = true },
                        new PresetStepDefinition { StepType = StepType.ListSelection, Title = "実行ソフト選択", Prompt = "開く際に使用するソフトを選択してください (任意):", DataKey = "app_path", ListSource = "registered_apps" },
                    }
                },
                new CommandPreset
                {
                    Id = "window_switcher",
                    Name = "ウィンドウ切替",
                    Category = CommandCategory.WindowSwitcher,
                    Steps = new List<PresetStepDefinition>
                    {
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "名前入力", Prompt = "ボタンの名前を入力してください:", DataKey = "name" },
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "ウィンドウ選択", Prompt = "切り替えるウィンドウを選択してください:", DataKey = "value", ListSource = "windows", ShowIncrementalSearch = true },
                    }
                },
                new CommandPreset
                {
                    Id = "menu",
                    Name = "階層 (メニュー)",
                    Category = CommandCategory.Hierarchy,
                    Steps = new List<PresetStepDefinition>
                    {
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "名前入力", Prompt = "メニューの名前を入力してください:", DataKey = "name" },
                    }
                },
                new CommandPreset
                {
                    Id = "slack",
                    Name = "Slack チャンネルを開く",
                    Category = CommandCategory.Open,
                    ParentId = "FOLDER:OPEN",
                    ActionOptions = new ActionRunnerOptions
                    {
                        UseWindowFocusLogic = OptionDisplayMode.AlwaysOff,
                    },
                    Steps = new List<PresetStepDefinition>
                    {
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "名前入力", Prompt = "ボタンの名前を入力してください:", DataKey = "name" },
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "Slack ワークスペース", Prompt = "チームIDを入力してください (TXXXXXXXX):", DataKey = "slack_team", ShowIncrementalSearch = true },
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "Slack チャンネル", Prompt = "チャンネルIDを入力してください (CXXXXXXXX):", DataKey = "slack_channel" },
                    }
                },
                new CommandPreset
                {
                    Id = "git",
                    Name = "Git 操作",
                    Category = CommandCategory.Git,
                    Steps = new List<PresetStepDefinition>
                    {
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "名前入力", Prompt = "ボタンの名前を入力してください:", DataKey = "name" },
                        new PresetStepDefinition { StepType = StepType.TextInput, Title = "リポジトリパス", Prompt = "対象リポジトリのパスを入力してください:", DataKey = "value", ShowBrowseButton = true, BrowseFileType = "Folder" },
                    }
                },
            };
        }
    }
}
