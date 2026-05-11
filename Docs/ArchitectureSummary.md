# HotKeyApp アーキテクチャと処理フロー

本ドキュメントでは、HotKeyAppの全体的な構成と、ユーザーの操作に伴う処理の流れをMermaidのシーケンス図を用いて詳解します。

## 1. アプリケーションの全体構成

このアプリは **WPF (Windows Presentation Foundation)** をベースとし、**MVVM (Model-View-ViewModel)** パターンを採用しています。主な構成要素は以下の通りです。

- **Views (`PaletteWindow.xaml` など)**:
  ユーザーとのインターフェース。キーボード入力の受け付け、フォーカス制御、コマンドのリスト表示を行います。
- **ViewModels (`MainViewModel.cs`)**:
  UIのロジックと状態管理を担当します。履歴の管理、検索フィルタリング、コマンド追加時のウィザード進行などを司ります。
- **Services (`ActionRunner.cs`, `ConfigService.cs`)**:
  - `ActionRunner`: 実際のコマンド実行 (URL, バッチ, 実行ファイル起動など) を `Process.Start` 経由で行います。
  - `ConfigService`: コマンド構造や設定をJSONファイルと同期・保存・読み込みします。

---

## 2. コマンド実行のシーケンス（メインフロー）

ユーザーがグローバルホットキーを押下してから、選択したアクションがシステム上で実行されるまでの一連の流れです。

```mermaid
sequenceDiagram
    actor User as ユーザー
    participant Hook as GlobalKeyHook
    participant Window as PaletteWindow (View)
    participant VM as MainViewModel
    participant ActionRunner as ActionRunner (Service)
    participant OS as OS (プロセス)

    User->>Hook: グローバルホットキー入力
    Hook->>Window: ウィンドウを表示 (Show/Activate)
    Window->>VM: コマンドリスト初期化 / 状態リセット
    Window->>VM: Focus要求 (SearchBox等へ)

    rect rgb(240, 248, 255)
        Note right of User: コマンド選択・検索フェーズ
        User->>Window: 文字入力 / 矢印キーでナビゲーション
        Window->>VM: InputTextの更新 / SelectedItemの更新
        VM-->>Window: リストのフィルタリング (インクリメンタルサーチ)
    end

    User->>Window: Enterキー押下 (実行決定)
    Window->>VM: Execute(SelectedCommand)

    alt 選択したコマンドが「階層(Menu)」の場合
        VM->>VM: 履歴スタックに現在の状態をPush
        VM->>VM: DisplayCommandsを子カテゴリのリストに更新
        VM-->>Window: 下層のリストを表示
    else 選択したコマンドが「アクション (URL/アプリ等)」の場合
        alt コマンドが引数を要求する場合
            VM-->>Window: 引数入力用のテキストボックスを表示
            User->>Window: 引数を入力してEnter
            Window->>VM: CommitInput()
        end
        
        VM->>ActionRunner: Run(Command, Argument)
        ActionRunner->>OS: Process.Start() で対象を起動
        VM->>Window: 処理完了時にウィンドウを非表示化 (Hide)
    end
```

---

## 3. 新規コマンド追加のシーケンス

コマンドパレット上から「＋ アプリを追加...」などのボタンを選択し、新しいショートカットを登録する際のウィザード形式のフローです。

```mermaid
sequenceDiagram
    actor User as ユーザー
    participant Window as PaletteWindow
    participant VM as MainViewModel
    participant Config as ConfigService

    User->>Window: 「追加」ボタンを選択し、Enter
    Window->>VM: StartInputFlow()
    
    Note over VM,Window: 1. 名前入力フェーズ
    VM->>VM: CurrentStep = EnteringName
    VM-->>Window: 名前入力フィールドを表示
    User->>Window: 表示名を入力しEnter
    Window->>VM: CommitInput()
    
    Note over VM,Window: 2. パス・URL入力フェーズ
    VM->>VM: CurrentStep = EnteringValue
    VM-->>Window: パス入力・またはファイル選択ボタン表示
    User->>Window: エクスプローラーからファイル選択 / パス入力
    Window->>VM: CommitInput()
    
    Note over VM,Window: 3. インストール済みアプリ選択フェーズ (アプリ追加時)
    opt アプリ追加モードの場合
        VM->>VM: Start Menuのlnkファイルを解析
        VM-->>Window: アプリ一覧を表示
        User->>Window: アプリを選択しEnter
        Window->>VM: CommitInput()
    end

    Note over VM,Config: 登録処理
    VM->>VM: 新しいCommandEntryオブジェクトを生成・追加
    VM->>Config: SaveCommands()
    Config-->>VM: JSONへ永続化完了
    
    VM->>VM: CurrentStep = None に戻す
    VM-->>Window: 通常のコマンドリスト画面に復帰
```

---

## 4. ウィンドウとフォーカス管理の連携

キーボード駆動のUIを実現するため、ViewとViewModel間でのフォーカス要求システムがあります。（例：リストからテキストボックスへのフォーカス移動）

```mermaid
sequenceDiagram
    participant Window as PaletteWindow (View)
    participant VM as MainViewModel

    Note over VM: 何らかの状態変化 (例: 検索モードへの移行)
    VM->>VM: CurrentStep の変更
    VM->>Window: RequestControlFocus.Invoke("InputTextBox") イベント発火
    Window->>Window: Switch文で対象コントロールを特定<br/>(InputTextBox.Focus())
    Window-->>User: テキストボックスが点滅し、即座に入力可能になる
```

## 今後の拡張性について

現在の構成における主な結合度は以下のようになっており、プロジェクトの進化に伴う影響範囲は局所化されています。

- **新しい機能 (テンプレート) の追加**: `presets.json` に新しいプリセットを定義し、必要に応じて `ActionRunner.Run` や `MainViewModel` にロジックを追加するだけで動作します。
- **UIの大規模変更**: デザインの変更やアニメーションの追加があっても、ロジック自体は `MainViewModel` に閉じているため、XAML側の修正を中心に進めることが可能です。
