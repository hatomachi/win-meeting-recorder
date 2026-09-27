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
  - **Phase 2 (画面キャプチャ＆変化検知エンジン)**:
    - GDI+ `Graphics.CopyFromScreen` による指定ディスプレイのBitBltキャプチャ (`ScreenService.cs`)
    - Win32 API (`EnumDisplayMonitors`, `GetMonitorInfoW`, `GetSystemMetrics`) による個別モニタ・プライマリ・仮想デスクトップ全体の安全列挙
    - 低解像度グレースケール差分判定アルゴリズム (`ScreenDiffDetector.cs`、128x72バイリニア縮小＋ITU-R BT.601)
    - デバウンス＆クールダウン（最小間隔2秒、スライドめくりアニメーション安定待ち500ms、定期キーフレーム60秒）
    - フル解像度JPEG圧縮保存（品質80%）
    - CLI検証モード（`--list-screens`, `--test-screen [秒数] [モニタIndex] [出力先]`）の追加
    - Web UI 連携（モニタ選択、リアルタイム撮影枚数カウンター、手動即時スクショボタン、音声＋画面同時記録）
- **次に着手するタスク**:
  - 手元Windows検証機での実機動作確認:
    - `WinMeetingRecorder.exe --list-screens` でディスプレイ列挙の確認
    - `WinMeetingRecorder.exe --test-screen 15` でスライド切り替え変化検知・JPEG保存の確認
    - `http://localhost:5000` で音声＋画面の同時記録テスト
  - **Phase 4: 社内GitLab REST API 連携 ＆ 自動クリーンアップ**:
    - 会議終了後の GitLab Commits API 一括送信（`meeting_audio.wav` / `images/*.jpg` / `README.md`）
    - 送信成功後のローカル一時スプール自動削除

---

## 🏗️ 開発・ビルド・検証の基本サイクル

1. **開発**: Mac環境でAIとともにC#コード・フロントエンドを記述
2. **ビルド**: GitHub Actions（`runs-on: windows-latest`）に `git push` し、単一exe（Self-contained win-x64）を自動生成
3. **検証**: 秋葉原で調達した手元Windows検証機にビルド成果物を落とし、MacからMicrosoft Remote Desktopで遠隔操作して実機検証・ログ確認
