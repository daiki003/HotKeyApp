# ビルドと回帰テスト

Windows / .NET 8 SDK と Git を用いる。以下のコマンドはリポジトリのルートで実行する。

## ビルド前の確認

- 実行中のアプリがある場合は `taskkill /f /im HotKeyCommandApp.exe` で終了する。
- ルートに残った WPF 一時プロジェクトがないか確認する。削除対象はルート直下の `*_wpftmp.csproj` に限定する。
- 同じ出力先を使うため、ビルドと各テストプロジェクトの `dotnet run` は順番に実行する。

```powershell
dotnet build HotKeyCommandApp.csproj
```

## 回帰テスト

既存のテストと同じコンソール形式で、失敗時は0以外の終了コードを返す。テスト用プロジェクトは [アプリのプロジェクト](../HotKeyCommandApp.csproj) からコンパイル対象外にしている。

| 対象 | プロジェクト | 内容 |
| --- | --- | --- |
| 共通処理・設定 UI | [RefactoringIntegration.csproj](../Tests/RefactoringIntegration/RefactoringIntegration.csproj) | 引数展開、文字コード、キャンセル、出力履歴、検索、画面履歴、階層編集、設定セッション、4種類のペアレコード UI |
| ステージ・スタッシュ | [GitStageIntegration.csproj](../Tests/GitStageIntegration/GitStageIntegration.csproj) | 部分ステージ、解除、破棄、初回コミット、新規・削除・改名、対象ファイルのスタッシュ |
| 差分画面 | [GitDiffIntegration.csproj](../Tests/GitDiffIntegration/GitDiffIntegration.csproj) | 差分表示、強調、複数リポジトリ、フォーカス・キー操作、ショートカット保存、ブランチ・スタッシュ操作 |

```powershell
dotnet run --project Tests/RefactoringIntegration/RefactoringIntegration.csproj
dotnet run --project Tests/GitStageIntegration/GitStageIntegration.csproj
dotnet run --project Tests/GitDiffIntegration/GitDiffIntegration.csproj
```

WPF のテストは一時ウィンドウを表示するため、デスクトップへアクセスできる環境で実行する。

Windows が前面化を拒否する環境では、差分画面の再アクティブ化テストがタイムアウトすることがある。残りのケースを確認する場合は次のように実行する。この実行だけでは再アクティブ化による自動更新を検証できないため、通常の全件実行と区別して報告する。

```powershell
dotnet run --project Tests/GitDiffIntegration/GitDiffIntegration.csproj -- --skip-activation
```

## 共通処理を変更した場合のテスト追加先

- [GitCommandTests.cs](../Tests/RefactoringIntegration/GitCommandTests.cs): エイリアス／関数の実行順、引用符付き引数、出力の上限と履歴、UTF-8／Shift_JIS、終了コード、実行中キャンセル。
- [PaletteTests.cs](../Tests/RefactoringIntegration/PaletteTests.cs): 同名の階層項目、履歴の分岐と件数上限、一時ディレクトリでの検索、編集セッションの値の分離。
- [PaletteWindowTests.cs](../Tests/RefactoringIntegration/PaletteWindowTests.cs): 実ウィンドウへのホットキー通知による、非表示・最小化状態からの復元。実キー入力の送信は行わない。
- [SingleInstanceTests.cs](../Tests/RefactoringIntegration/SingleInstanceTests.cs): 重複起動の抑止、既存プロセスへの表示要求、終了後の再起動。
- [PairEditorTests.cs](../Tests/RefactoringIntegration/PairEditorTests.cs): 4種類のペアレコードの平坦化・順序操作と、実際の WPF Control での追加・削除・編集・高さ維持、内部タブの編集と行移動。
- [Program.cs](../Tests/RefactoringIntegration/Program.cs): WPF の起動と終了、一時ディレクトリ、文字コード・キャンセル検証用の子プロセス。

共通処理のテストでは Git リポジトリとファイル検索用のデータを一時ディレクトリに作り、最後に削除する。設定の読み書き先はテストプロジェクトの実行出力ディレクトリであり、通常のアプリの設定とは分かれる。

ペアレコードの通常表示・編集表示と内部タブのプレビュー画像も、テストの実行出力ディレクトリへ PNG で保存する。高さの検証と合わせて見た目を確認できる。

## 作業終了時

テスト完了後は [プロジェクトルール](../AGENTS.md) に従って、アプリ終了 → ビルド → 起動を一連で行う。

```powershell
taskkill /f /im HotKeyCommandApp.exe
dotnet build HotKeyCommandApp.csproj
.\run_app.ps1
```

[run_app.ps1](../run_app.ps1) は、ビルド済みのアプリを `dotnet run --no-build` の独立したプロセスで起動する。起動元のコマンドを完了させた後、返された PID を別のコマンドで再確認する。名前だけの一時的なプロセス確認で常駐できたと判断しない。

アプリは単一起動とし、再度起動された場合は既存のパレットへ表示を要求する。閉じる操作はウィンドウの破棄ではなく非表示に変え、ホットキーの受信を維持する。

起動・終了・ウィンドウの読み込み／破棄は、[Logger.cs](../Services/Logger.cs) が定めるアプリ出力先のログに `[Lifecycle]` と PID を付けて記録する。起動後にプロセスが消えていた場合は、対応する終了記録と Windows の例外ログを確認する。終了記録がない場合の終了要因や実行元は推測で断定しない。
