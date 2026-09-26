# AGENTS.md (win-meeting-recorder)

> **このリポジトリで作業するAIエージェント（Claude / Antigravity等）は、作業開始前に必ずこのファイルを読んでください。**

---

## 🗺️ 作戦ノート（個人作戦ボード連携）
本プロジェクトの全体構想、現在地、Next Actions、フィードバックログは、以下の `personal-vault` 作戦ノートにて一元管理されています。

- **作戦ノート**: [/Users/s-ikari/work/personal-vault/10_職人・発明家/win-meeting-recorder.md](file:///Users/s-ikari/work/personal-vault/10_職人・発明家/win-meeting-recorder.md)
- **検証機調達ノート**: [/Users/s-ikari/work/personal-vault/10_職人・発明家/windows-test-machine.md](file:///Users/s-ikari/work/personal-vault/10_職人・発明家/windows-test-machine.md)
- **総合ダッシュボード**: [/Users/s-ikari/work/personal-vault/00_Dashboard.md](file:///Users/s-ikari/work/personal-vault/00_Dashboard.md)

---

## 🎯 このリポジトリでやること（別セッションへの引き継ぎ方針）

このリポジトリは、**Windows専用の会議記録・画面差分キャプチャツール（C# / .NET 8）** を新規開発するための独立リポジトリです。

### ⚠️ エージェントへの指示（重要）
- **詳細な実装計画は、このリポジトリでのセッションでユーザーと相談・合意しながら立ててください。**
- 本リポジトリの `README.md` に、事前の検討・会話で合意した「背景」「やりたいこと（要件）」「技術方針の実現イメージ」「検証体制」を整理してあります。
- まずは `README.md` を精読し、実装計画（フェーズ分け、モジュール構成、検証手順）の提案からスタートしてください。

---

## 🏗️ 開発・ビルド・検証の基本サイクル

1. **開発**: Mac環境でAIとともにC#コード・フロントエンドを記述
2. **ビルド**: GitHub Actions（`runs-on: windows-latest`）に `git push` し、単一exe（Self-contained win-x64）を自動生成
3. **検証**: 秋葉原で調達した手元Windows検証機にビルド成果物を落とし、MacからMicrosoft Remote Desktopで遠隔操作して実機検証・ログ確認
