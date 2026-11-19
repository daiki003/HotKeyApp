# 設定画面の共通構造

この資料は現在の実装を示す。セクションの共通化と一覧ナビゲーションの共通化は実装済み。

## ウィンドウから一覧への委譲

```text
SettingsWindow / GitSettingsDialog
    ├─ SettingsTabShellController
    │    └─ ISettingsSectionViewModel: Save / Reset
    └─ セクションの Control
         ├─ ISettingsRecordSectionControl
         └─ ISettingsRecordListNavigator
              ├─ SettingsRecordListControl
              └─ AliasPairEditorControl
```

- ウィンドウ全体の操作は [SettingsWindow](../Views/SettingsWindow.xaml.cs)、[GitSettingsDialog](../Views/GitSettingsDialog.xaml.cs)、[SettingsDialogKeyboardNavigationHelper](../Views/Controls/SettingsDialogKeyboardNavigationHelper.cs) が担当する。
- セクションの選択と保存は [SettingsTabShellController](../ViewModels/Settings/SettingsTabShellController.cs) と [ISettingsSectionViewModel](../ViewModels/Settings/ISettingsSectionViewModel.cs) を入口にする。
- セクション単位のフォーカス移動には [ISettingsRecordSectionControl](../Views/Controls/ISettingsRecordSectionControl.cs)、一覧のキー操作には [ISettingsRecordListNavigator](../Views/Controls/ISettingsRecordListNavigator.cs) を使う。
- [PairEditorSectionControl](../Views/Controls/PairEditorSectionControl.xaml.cs) はセクションの契約を実装し、一覧操作を内部の共通エディターへ委譲する。

## 画面ごとの入口

| 画面 | セクションの状態 | 一覧 UI |
| --- | --- | --- |
| サイズ調整 | [SizeSettingsSectionViewModel](../ViewModels/Settings/SizeSettingsSectionViewModel.cs) | [SizeSettingsSectionControl](../Views/Controls/SizeSettingsSectionControl.xaml.cs) |
| ショートカット | [ShortcutSettingsSectionViewModel](../ViewModels/Settings/ShortcutSettingsSectionViewModel.cs) | [ShortcutSettingsSectionControl](../Views/Controls/ShortcutSettingsSectionControl.xaml.cs) |
| その他 | [OthersSettingsSectionViewModel](../ViewModels/Settings/OthersSettingsSectionViewModel.cs) | [OthersSettingsSectionControl](../Views/Controls/OthersSettingsSectionControl.xaml.cs) |
| 定数定義 | [ConstantsSettingsSectionViewModel](../ViewModels/Settings/ConstantsSettingsSectionViewModel.cs) | [PairEditorSectionControl](../Views/Controls/PairEditorSectionControl.xaml) |
| 選択式テンプレート | [SelectTemplatesSettingsSectionViewModel](../ViewModels/Settings/SelectTemplatesSettingsSectionViewModel.cs) | [PairEditorSectionControl](../Views/Controls/PairEditorSectionControl.xaml) |
| Git エイリアス | [GitAliasSettingsSectionViewModel](../ViewModels/Settings/GitAliasSettingsSectionViewModel.cs) | [AliasPairEditorControl](../Views/Controls/AliasPairEditorControl.xaml) |
| リポジトリ名 | [GitRepositoryMappingsSectionViewModel](../ViewModels/Settings/GitRepositoryMappingsSectionViewModel.cs) | [AliasPairEditorControl](../Views/Controls/AliasPairEditorControl.xaml) |
| Git 関数 | [GitFunctionsSettingsSectionViewModel](../ViewModels/Settings/GitFunctionsSettingsSectionViewModel.cs) | [GitFunctionsSectionControl](../Views/Controls/GitFunctionsSectionControl.xaml) |

定数定義・選択式テンプレートも、セクションの内側で同じ [AliasPairEditorControl](../Views/Controls/AliasPairEditorControl.xaml.cs) を使う。名前に Alias と付いていても、エイリアス専用の部品として扱わない。

## ペアレコード系の責務

| 担当 | 責務 |
| --- | --- |
| [AliasPairEditorControl](../Views/Controls/AliasPairEditorControl.xaml.cs) | 行・内部タブの表示、キーとドラッグ操作の受付、編集欄のフォーカス、変更後の再描画 |
| [PairEntryListEditor](../Services/PairEntryListEditor.cs) | 表示順の取得、旧フォルダ構造の平坦化、行の並び替え、隣接タブへの移動、保存用の順序の正規化 |
| [PairEditorTabEditSession](../Models/PairEditorTabEditSession.cs) | タブ名編集の開始・確定・キャンセルと空欄時の復元 |
| [IPairEditorTab / IPairEditorTabHost](../Models/PairEditorTabs.cs) | タブのデータと選択状態の契約 |
| [GitAliasTabHost](../Models/GitAliasTabHost.cs) | エイリアス設定のタブの作成・削除・選択 |
| [IPairEntryEditable](../Models/IPairEntryEditable.cs) | 2項目の値、親フォルダ、保存順の共通契約 |

データの順序操作には WPF のフォーカスや Dispatcher を持ち込まない。Control は操作後に表示を更新し、元の行または適切な隣接行へフォーカスを戻す。

旧フォルダデータは読み取り時に一覧へ平坦化する。参照先のない行も保持し、平坦化後は親フォルダを空にして順序を正規化する。

## 見た目とキー操作を変更するとき

- 共通スタイルは [SettingsSharedResources.xaml](../Views/Controls/SettingsSharedResources.xaml) と [AliasPairEditorControl.xaml](../Views/Controls/AliasPairEditorControl.xaml) を確認する。
- ペアレコードの通常表示と編集欄は `SettingsRecordMinHeight` を共有する。F2時の行・タブの高さを維持する。
- 行の先頭・末尾・隣接セルへの移動、Shift+Up / Shift+Down、内部タブへの移動は共通 Control を入口にし、4画面で同じ動作を維持する。
- 一覧上部の内部タブもペアレコード系の範囲に含める。エイリアス設定だけの挙動は、その用途を明示して変更する。

通常設定の編集・保存は [AppSettingsEditorSession](../Services/AppSettingsEditorSession.cs)、Git 設定の編集・保存は [GitSettingsEditorSession](../Services/GitSettingsEditorSession.cs) を参照する。

回帰テストは [PairEditorTests.cs](../Tests/RefactoringIntegration/PairEditorTests.cs)。実行方法は [テスト手順](./Testing.md) を参照する。
