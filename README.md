# HotKeyApp

Windows上での様々な操作をキーボードのみで素早く行うことを目的とした、WPFベースのランチャー・ユーティリティアプリです。

## 特徴
- **キーボード優先**: マウスを使わず、キーボードだけで完結する操作感（VSCodeに近い操作感）を目指しています。
- **コマンドパレット**: `Ctrl + Shift + P` (想定) のような直感的なコマンド実行。
- **カスタマイズ性**: ショトカットやコマンドの自由な割り当てが可能です。

## 開発環境
- **言語**: C#
- **フレームワーク**: .NET (WPF)
- **IDE**: Visual Studio / VS Code

## セットアップ
1. リポジトリをクローンします。
2. Visual Studio で [HotKeyCommandApp.sln](./HotKeyCommandApp.sln) を開きます。
3. プロジェクトをビルドして実行します。

## 開発資料

- [構成と変更箇所の対応](./Docs/ArchitectureSummary.md)
- [設定画面の共通構造](./Docs/SettingsRefactorDesign.md)
- [設定画面名とクラスの対応](./Docs/SettingsClassReference.md)
- [ビルドと回帰テスト](./Docs/Testing.md)
- [Git 差分画面の操作](./Docs/GitDiff.md)

## ライセンス
MIT License (想定)
