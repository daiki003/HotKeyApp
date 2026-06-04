# Settings Class Reference

設定まわりの改修依頼時に、画面名とクラス名の対応をすばやく確認するためのメモです。

## 伝え方のおすすめ

次の 3 点があると、対象範囲をかなり正確に判断しやすいです。

1. 画面上の名前
2. 対応クラス名
3. 個別修正か、共通部品側の修正か

例:

- `ショートカット` セクション (`ShortcutSettingsSectionControl`) を修正したい
- `定数定義` の共通部品側 (`PairEditorSectionControl` から `AliasPairEditorControl`) を直したい
- `SettingsRecordListControl` 系全部に反映したい

## 全体マップ

```text
通常設定
  SettingsWindow
    ├─ SizeSettingsSectionControl
    ├─ ShortcutSettingsSectionControl
    ├─ PairEditorSectionControl
    │    └─ AliasPairEditorControl
    ├─ PairEditorSectionControl
    │    └─ AliasPairEditorControl
    └─ OthersSettingsSectionControl

Git 設定
  GitSettingsDialog
    ├─ AliasPairEditorControl
    ├─ GitFunctionsSectionControl
    └─ AliasPairEditorControl
```

## 画面とクラスの対応

### 通常の設定画面

```text
画面名
  通常の設定画面

主なクラス
  Views/SettingsWindow.xaml
  Views/SettingsWindow.xaml.cs

共通外枠
  Views/Controls/SettingsDialogShell.cs
  Views/Controls/SettingsSharedResources.xaml
```

### Git 設定画面

```text
画面名
  Git 設定画面

主なクラス
  Views/GitSettingsDialog.xaml
  Views/GitSettingsDialog.xaml.cs

共通外枠
  Views/Controls/SettingsDialogShell.cs
  Views/Controls/SettingsSharedResources.xaml
```

## 通常設定のセクション対応

```text
サイズ調整
  Control
    Views/Controls/SizeSettingsSectionControl.xaml
  ViewModel
    ViewModels/Settings/SizeSettingsSectionViewModel.cs
  系統
    単一値レコード系

ショートカット
  Control
    Views/Controls/ShortcutSettingsSectionControl.xaml
  ViewModel
    ViewModels/Settings/ShortcutSettingsSectionViewModel.cs
  系統
    アクションレコード系

定数定義
  Control
    Views/Controls/PairEditorSectionControl.xaml
  ViewModel
    ViewModels/Settings/ConstantsSettingsSectionViewModel.cs
  系統
    ペアレコード系

選択式テンプレート
  Control
    Views/Controls/PairEditorSectionControl.xaml
  ViewModel
    ViewModels/Settings/SelectTemplatesSettingsSectionViewModel.cs
  系統
    ペアレコード系

その他
  Control
    Views/Controls/OthersSettingsSectionControl.xaml
  ViewModel
    ViewModels/Settings/OthersSettingsSectionViewModel.cs
  系統
    単一値レコード系
```

## Git 設定のセクション対応

```text
エイリアス
  Control
    Views/Controls/AliasPairEditorControl.xaml
  ViewModel
    ViewModels/Settings/GitAliasSettingsSectionViewModel.cs
  系統
    ペアレコード系

関数
  Control
    Views/Controls/GitFunctionsSectionControl.xaml
  ViewModel
    ViewModels/Settings/GitFunctionsSettingsSectionViewModel.cs
  系統
    専用 UI

リポジトリ名
  Control
    Views/Controls/AliasPairEditorControl.xaml
  ViewModel
    ViewModels/Settings/GitRepositoryMappingsSectionViewModel.cs
  系統
    ペアレコード系
```

## 共通部品の分類

### 単一値レコード系

```text
SizeSettingsSectionControl
OthersSettingsSectionControl
  └─ SettingsRecordListControl
       └─ EditableValueRecordControl
            └─ EditableValueRecordViewModel
```

関連クラス:

- `Views/Controls/SettingsRecordListControl.xaml`
- `Views/Controls/EditableValueRecordControl.xaml`
- `ViewModels/Settings/EditableValueRecordViewModel.cs`
- `Views/Controls/SettingsRecordNavigationHelper.cs`

対象画面:

- `サイズ調整`
- `その他`

### ショートカット系

```text
ShortcutSettingsSectionControl
  └─ SettingsRecordListControl
       └─ ActionValueRecordControl
            └─ ActionValueRecordViewModel
```

関連クラス:

- `Views/Controls/SettingsRecordListControl.xaml`
- `Views/Controls/ActionValueRecordControl.xaml`
- `ViewModels/Settings/ActionValueRecordViewModel.cs`

対象画面:

- `ショートカット`

### ペアレコード系

```text
PairEditorSectionControl
  └─ AliasPairEditorControl
       ├─ レコードボタン
       ├─ 編集 TextBox
       ├─ フォルダ
       ├─ タブ
       └─ 並び替えとキー移動
```

関連クラス:

- `Views/Controls/PairEditorSectionControl.xaml`
- `Views/Controls/AliasPairEditorControl.xaml`
- `Views/Controls/AliasPairEditorControl.xaml.cs`

対象画面:

- `定数定義`
- `選択式テンプレート`
- `Git 設定のエイリアス`
- `Git 設定のリポジトリ名`

## 共通インターフェース

```text
セクション単位
  ISettingsRecordSectionControl

一覧ナビゲーション単位
  ISettingsRecordListNavigator
```

関連クラス:

- `Views/Controls/ISettingsRecordSectionControl.cs`
- `Views/Controls/ISettingsRecordListNavigator.cs`

## 依頼時の言い方の目安

### 個別画面だけ直したい

- `サイズ調整だけ直したい`
- `Git 設定の関数タブだけ直したい`

### 同系統全部に反映したい

- `ペアレコード系全部に反映したい`
- `SettingsRecordListControl 系全部に反映したい`

### 共通部品側を直したい

- `AliasPairEditorControl 側で直したい`
- `SettingsRecordListControl 側で直したい`
- `SettingsDialogShell 側で直したい`

## 迷ったときの伝え方

画面名しかわからない場合は、次のように伝えてもらえれば十分です。

- `定数定義のこの画面だけ`
- `ショートカットとその他に共通で`
- `ペアレコード系全部に横展開で`

クラス名までわかる場合は、画面名とセットだとさらに正確です。

- `ショートカット (ShortcutSettingsSectionControl) を直したい`
- `定数定義 (PairEditorSectionControl から AliasPairEditorControl) を直したい`
