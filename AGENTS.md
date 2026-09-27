# AGENTS.md (win-meeting-recorder)

> **このリポジトリで作業するAIエージェント（Claude / Antigravity等）は、作業開始前に必ずこのファイルを読んでください。**

---

## 🗺️ 作戦ノート（個人作戦ボード連携）
本プロジェクトの全体構想、現在地、Next Actions、フィードバックログは、以下の `personal-vault` 作戦ノートにて一元管理されています。

- **作戦ノート**: [/Users/s-ikari/work/personal-vault/10_職人・発明家/win-meeting-recorder.md](file:///Users/s-ikari/work/personal-vault/10_職人・発明家/win-meeting-recorder.md)
- **検証機調達ノート**: [/Users/s-ikari/work/personal-vault/10_職人・発明家/windows-test-machine.md](file:///Users/s-ikari/work/personal-vault/10_職人・発明家/windows-test-machine.md)
- **総合ダッシュボード**: [/Users/s-ikari/work/personal-vault/00_Dashboard.md](file:///Users/s-ikari/work/personal-vault/00_Dashboard.md)

---

## 🎯 現在地と次のタスク（セッション引き継ぎ情報）

- **完了済みフェーズ**:
  - **Phase 0 (基盤 & CI)**: .NET 8 プロジェクト定義、GitHub Actions による単一exe自動ビルド & GitHub Releases（ログイン不要DL）自動公開パイプライン整備完了。
  - **Phase 1 (音声 & Web UI)**: NAudio WASAPI Loopback ＋ マイク音声合成エンジン、実時間追従ドレインによる秒数精度同期、内蔵Web UI（`IndexHtml.cs`）を実装し、**手元Windows検証機にて音割れなし・10秒ピッタリ録音・localhost:5000表示の正常動作を確認完了**。
- **次に着手するタスク（Phase 2）**:
  - **Phase 2: 画面キャプチャ＆変化検知エンジンの実装**
    - `System.Drawing.Common` (GDI+) の `Graphics.CopyFromScreen` による指定ディスプレイのBitBltキャプチャ
    - 縮小グレースケール差分判定アルゴリズム（スライド等の変化検知）
    - アニメーション等の過剰連写を防ぐデバウンス＆クールダウン（例: 最小間隔2秒、安定後0.5秒）
    - フル解像度JPEG圧縮保存（品質80%）
    - CLI検証モード（`--test-screen [秒数]`）の追加
- **ユーザーから「続きをやって」と指示された場合**:
  - 直ちに上記の **Phase 2（画面キャプチャ＆変化検知エンジン）** の実装に着手してください。

---

## 🏗️ 開発・ビルド・検証の基本サイクル

1. **開発**: Mac環境でAIとともにC#コード・フロントエンドを記述
2. **ビルド**: GitHub Actions（`runs-on: windows-latest`）に `git push` し、単一exe（Self-contained win-x64）を自動生成
3. **検証**: 秋葉原で調達した手元Windows検証機にビルド成果物を落とし、MacからMicrosoft Remote Desktopで遠隔操作して実機検証・ログ確認
