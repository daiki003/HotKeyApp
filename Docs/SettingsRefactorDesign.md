# Settings Refactor Design

## 全体像

```text
SettingsWindow / GitSettingsDialog
        │
        │  セクション切替・保存・ウィンドウ全体キー処理
        ▼
ISettingsSectionViewModel
        │
        ├─ SizeSettingsSectionViewModel
        ├─ OthersSettingsSectionViewModel
        ├─ ShortcutSettingsSectionViewModel
        ├─ ConstantsSettingsSectionViewModel
        ├─ SelectTemplatesSettingsSectionViewModel
        ├─ GitAliasSettingsSectionViewModel
        ├─ GitFunctionsSettingsSectionViewModel
        └─ GitRepositoryMappingsSectionViewModel
        │
        │  各セクションに対応する View / Control
        ▼
Section Control
        │
        ├─ 単一値レコード系
        │    ├─ SizeSettingsSectionControl
        │    ├─ OthersSettingsSectionControl
        │    └─ ShortcutSettingsSectionControl
        │
        └─ ペアレコード系
             ├─ PairEditorSectionControl
             └─ AliasPairEditorControl
```

## いまの共通化の核

```text
[セクション単位]
ISettingsRecordSectionControl
    ├─ FocusFirstRecord()
    ├─ FocusLastRecord()
    └─ FocusRecordAfterAppend()

[一覧単位]
SettingsRecordListControl
    ├─ EditableValueRecordControl
    └─ ActionValueRecordControl
```

関連ファイル:

- `Views/Controls/ISettingsRecordSectionControl.cs`
- `Views/Controls/SettingsRecordListControl.xaml.cs`

## ここから整理したい設計

ポイントは、`ISettingsRecordSectionControl` の下に、さらに「一覧ナビゲーション」の抽象を作ることです。

```text
ISettingsSectionViewModel
        │
        ▼
Section Control
        │
        ├─ ISettingsRecordSectionControl   ← セクションとしての共通契約
        │      ├─ FocusFirstRecord()
        │      ├─ FocusLastRecord()
        │      └─ FocusRecordAfterAppend()
        │
        ▼
ISettingsRecordListNavigator            ← 新しく切りたい下位抽象
        ├─ FocusFirstRecord()
        ├─ FocusLastRecord()
        ├─ FocusRecordAfterAppend()
        ├─ IsFocusInEditor()
        └─ TryHandleKeyboardShortcut(...)
```

## その上に載る想定

```text
ISettingsRecordListNavigator
    ├─ SettingsRecordListControl
    │    ├─ EditableValueRecordControl
    │    └─ ActionValueRecordControl
    │
    └─ AliasPairEditorControl
         ├─ レコードボタン
         ├─ 編集 TextBox
         ├─ タブ
         ├─ 追加ボタン
         └─ 並び替え / キー移動
```

## 責務分担のイメージ

```text
1. Window
   - タブ切替
   - Ctrl+Enter / Esc などウィンドウ全体操作
   - SaveAll()

2. Section Control
   - 自分がどの一覧ナビゲータを使うかだけ知る
   - 先頭/末尾フォーカスを一覧へ委譲

3. Record List Navigator
   - レコードのフォーカス移動
   - 編集中判定
   - レコード追加後フォーカス
   - 一覧固有キー処理

4. Record Control
   - 1行の表示/編集切替
   - Enter / Esc
```

## 単一値系とペアレコード系の関係

```text
[単一値系]
Size / Others / Shortcut
    └─ SettingsRecordListControl
         └─ 単純な縦並びレコード

[ペアレコード系]
Constants / SelectTemplates / Git Alias / Repository
    └─ AliasPairEditorControl
         └─ タブ・フォルダ・並び替え付きレコード
```

見た目は違っても、共通点は次です。

```text
- レコードボタンを持つ
- 縦に並ぶ
- 先頭/末尾/追加後フォーカスがある
- 編集中かどうかを持つ
- キーボード移動がある
```

## 最終的に目指している形

```text
SettingsWindow / GitSettingsDialog
        │
        ▼
Section Control
        │
        ▼
ISettingsRecordSectionControl
        │
        ▼
ISettingsRecordListNavigator
        ├─ SettingsRecordListControl
        └─ AliasPairEditorControl
```

## 一言でいうと

- 今は `セクション共通化` までは入っている
- 次にやりたいのは `一覧ナビゲーション共通化`
- ペアレコード系は、その `一覧ナビゲーション共通化` の側へ寄せる

## 次の候補

1. `ISettingsRecordListNavigator` のような下位インターフェースを追加
2. `SettingsRecordListControl` と `AliasPairEditorControl` をそこへ揃える
3. `PairEditorSectionControl` も `ISettingsRecordSectionControl` を実装して委譲する
