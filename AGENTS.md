# AGENTS.md (win-meeting-recorder)

> **このリポジトリで作業するAIエージェント（Claude / Antigravity等）は、作業開始前に必ずこのファイルを読んでください。**

---

## 🗺️ 作戦ノート（個人作戦ボード連携）
本プロジェクトの全体構想、現在地、Next Actions、フィードバックログは、以下の `personal-vault` 作戦ノートにて一元管理されています。

- **作戦ノート**: [/Users/s-ikari/work/personal-vault/10_職人・発明家/win-meeting-recorder.md](file:///Users/s-ikari/work/personal-vault/10_職人・発明家/win-meeting-recorder.md)
- **検証機調達ノート**: [/Users/s-ikari/work/personal-vault/10_職人・発明家/windows-test-machine.md](file:///Users/s-ikari/work/personal-vault/10_職人・発明家/windows-test-machine.md)
- **総合ダッシュボード**: [/Users/s-ikari/work/personal-vault/00_Dashboard.md](file:///Users/s-ikari/work/personal-vault/00_Dashboard.md)

---

## 🖥️ Windows検証機（SSH直接接続 ＆ 自動化情報）

手元Windows検証機は同一LAN内に配置され、**Macからパスワードなし（ED25519公開鍵認証）でSSH接続可能**です。

| 項目 | 設定値 |
| :--- | :--- |
| **SSHホスト名** | `win-test` (`~/.ssh/config` に設定済み) |
| **IPアドレス** | `192.168.11.19` (プライベートネットワーク) |
| **SSHユーザー** | `dev` (Administrators権限、初期パスワード: `DevPass123!`) |
| **秘密鍵** | `~/.ssh/id_ed25519_win` (パスフレーズなし) |
| **実機作業ディレクトリ** | `C:\work` |
| **開放済みポート** | 22 (SSH), 5000 (Web UI) |

### 🛠️ よく使うリモート操作コマンド例

```bash
# 1. 疎通確認
ssh win-test "whoami"

# 2. GitHub Releases から最新単一exeを自動DL & 解凍
ssh win-test "Invoke-WebRequest -Uri 'https://github.com/hatomachi/win-meeting-recorder/releases/download/vlatest/WinMeetingRecorder-win-x64.zip' -OutFile 'C:\work\WinMeetingRecorder.zip'; Expand-Archive -Path 'C:\work\WinMeetingRecorder.zip' -DestinationPath 'C:\work' -Force"

# 3. オーディオデバイス一覧取得
ssh win-test "C:\work\WinMeetingRecorder.exe --list-devices"

# 4. ディスプレイ一覧取得
ssh win-test "C:\work\WinMeetingRecorder.exe --list-screens"

# 5. 音声合成テスト（10秒）
ssh win-test "C:\work\WinMeetingRecorder.exe --test-audio 10 C:\work\test_mixed.wav"

# 6. 画面キャプチャテスト（Session 0 Isolation対策: 対話セッション実行）
ssh win-test "schtasks /create /tn 'ScreenTest' /tr 'C:\work\WinMeetingRecorder.exe --test-screen 5 0 C:\work\test_screen_out' /sc once /st '23:59' /ru 'sohik' /it /f; schtasks /run /tn 'ScreenTest'; Start-Sleep -Seconds 7; schtasks /delete /tn 'ScreenTest' /f"

# 7. 実機上の生成ファイル（スクショや音声）をMacへ転送して検証
scp win-test:C:/work/test_screen_out/images/*.jpg ./
```

> [!NOTE]
> **Session 0 Isolation（Windowsの画面保護仕様）についての注意**:  
> SSHサービスはバックグラウンドの「Session 0」で稼働するため、SSH経由で直接 GDI BitBlt (`CopyFromScreen`) を呼ぶと「ハンドルが無効です」エラーになります。物理画面・RDP画面を撮影するには、上記のようにタスクスケジューラ（`schtasks /it`）でログオン中ユーザー（`sohik`）の対話セッション（Session 1〜3）として起動するか、RDP側で直接実行する必要があります。

---

## 🦊 GitLab検証環境（検証用リポジトリ ＆ 設定メモ）

| 項目 | 設定値 |
| :--- | :--- |
| **GitLab URL** | `https://gitlab.com` |
| **検証用リポジトリ** | `https://gitlab.com/test7891019/win-meeting-recorder-test` |
| **プロジェクトパス** | `test7891019/win-meeting-recorder-test` |
| **対象ブランチ** | `main` |
| **保存先フォルダ** | `meetings` |
| **PATスコープ知見** | GitLabのFine-grained PATでは「Repository: Read/Write」があればコミット作成（Commits API）は100%成功する。`GET /api/v4/projects/:id` は「Project: Read」が必要なため、疎通テストAPIではリポジトリコミットAPIへのフォールバックを実装して完全対応済み。 |

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
    - CLI検証モード（`--list-screens`, `--test-screen`）の追加
    - Web UI 連携（モニタ選択、リアルタイム撮影枚数カウンター、手動即時スクショボタン、音声＋画面同時記録）
    - **実機検証完了**: SSH経由で自動配置し、物理画面（1536x960、フル解像度JPEG 192KB）の撮影・Mac転送・目視確認に成功！
  - **Phase 4 (社内GitLab REST API 連携 ＆ 自動クリーンアップ ＆ LANバインド)**:
    - 社内GitLab（Self-hosted）の Commits API (`POST /api/v4/projects/:id/repository/commits`) によるアトミック一括ファイルコミット (`README.md`, `meeting_audio.wav`, `images/*.jpg`)
    - 社内GitLab用の自己署名SSL許可（`IgnoreSslErrors`）およびURLエスケープ
    - スプールマネージャー (`SpoolService.cs`) による `metadata.json` 保持、未送信セッション一覧取得、コミット成功時のスプール完全自動消去（失敗時は安全保持）
    - Web UI 連携（GitLab設定アコーディオン、接続テスト、自動プッシュ、手動アップロード、未送信通知）
    - LAN内バインド（`0.0.0.0:5000`）対応により、同一LAN内のMacブラウザ（`http://192.168.11.19:5000`）から直接全操作盤を遠隔操作可能に！
    - **実機検証完了**: Macから `http://192.168.11.19:5000` 経由で記録開始・手動スクショ・停止・Commits API送信検証を実施し、音声2.28MB＋スクショ2枚＋metadata.jsonの生成と失敗時スプール安全保持を確認完了！
  - **Phase 5 (単一exe最適化 ＆ エンドツーエンド総合検証 ＆ パッケージング完了)**:
    - 実機での連続稼働ストレステスト（60秒間連続稼働）を実施。メモリリークゼロ（GCが正常に働き135MBに安定）、ハンドル数完全一定（676前後）、CPU負荷わずか6%の極めて軽量・高安定性を実証！
    - デフォルトでの**既定ブラウザ自動オープン**（ダブルクリックで即 `http://localhost:5000` 起動、`--no-browser` でスキップ可能）を実装。
    - Web UIからの**安全なアプリ終了（シャットダウンAPI）**および**未送信スプールの個別削除機能**を実装。
    - 単一exe最適化（デバッグシンボル削除）および `README_HOW_TO_USE.txt`（利用ガイド）を同梱した配布zipパッケージングの自動化完了。
    - 手元Windows検証機にて、ダブルクリック起動（Edge自動オープン）➜ 記録開始 ➜ 手動スクショ ➜ 停止 ➜ GitLab自動コミット ➜ スプール完全自動消去 ➜ Web UIからの安全終了まで、エンドツーエンドの全フロー完全動作を確認完了！
  - **Phase 5 (単一exe最適化 ＆ エンドツーエンド総合検証 ＆ パッケージング完了)**:
    - 実機での連続稼働ストレステスト（60秒間連続稼働）を実施。メモリリークゼロ（GCが正常に働き135MBに安定）、ハンドル数完全一定（676前後）、CPU負荷わずか6%の極めて軽量・高安定性を実証！
    - デフォルトでの**既定ブラウザ自動オープン**（ダブルクリックで即 `http://localhost:5000` 起動、`--no-browser` でスキップ可能）を実装。
    - Web UIからの**安全なアプリ終了（シャットダウンAPI）**および**未送信スプールの個別削除機能**を実装。
    - 単一exe最適化（デバッグシンボル削除）および `README_HOW_TO_USE.txt`（利用ガイド）を同梱した配布zipパッケージングの自動化完了。
    - 手元Windows検証機にて、ダブルクリック起動（Edge自動オープン）➜ 記録開始 ➜ 手動スクショ ➜ 停止 ➜ GitLab自動コミット ➜ スプール完全自動消去 ➜ Web UIからの安全終了まで、エンドツーエンドの全フロー完全動作を確認完了！
  - **Phase 6A (音声のMP3軽量化・24kHzモノラル)**:
    - 非圧縮WAV（48kHz Float32ステレオ、1時間約700MB）を、NAudio MediaFoundation（Windows標準機能・追加DLL不要）で 24kHz モノラル MP3（64kbps、1時間約21MB、削減率96.8%）へ自動変換 (`AudioConverter.cs`)。
    - 外部ffmpegや追加DLL一切不要で、Windows標準機能だけで超高速（3.5秒音声の変換がわずか63ms）変換を実現。
    - スプール内に `meeting_audio.mp3` を生成し、GitLabコミット容量の激減とWhisper転送の高速化を達成！
  - **Phase 6B (OpenAI Whisper API互換連携 ＆ タイムライン自動統合)**:
    - 会社環境やローカルモックの OpenAI Whisper API 互換エンドポイント（`POST {WhisperBaseUrl}/v1/audio/transcriptions`、`response_format: verbose_json`）を呼ぶクライアント実装 (`WhisperService.cs`)。
    - 返却された各セグメント（`start`, `end`, `text`）を `transcript.json` としてスプールに整形保存。
    - 発話テキストとスクリーンショット画像を秒数順で時系列マージした**統合タイムラインMarkdown**（`README.md`）の自動生成。
    - GitLab Commits API へのアトミック一括コミット（`README.md`, `meeting_audio.mp3`, `transcript.json`, `images/*.jpg`）。
    - Web UIに「🎙️ OpenAI Whisper 文字起こし設定」アコーディオンパネル、接続テスト機能、未送信スプールへの手動文字起こしボタンを追加。
  - **Phase 7A (AI議事録作成 ＆ 外部プロンプト調整 ＆ チャット修正)**:
    - `portable-ssh-ftp` 方式（C#内蔵のローカルCLI直接サブプロセス実行）を採用し、単一exeの自己完結性を完全維持。
    - GitHub Copilot CLI と Claude Code をフラットに選択可能（Engine切り替え）。
    - exe同階層の外部ファイル `meeting_minutes_prompt.txt`（または `%AppData%`）および Web UI からプロンプトを直接調整可能。
    - スプールフォルダを作業ディレクトリとして CLI を実行し、Whisper文字起こし（`transcript.json`）とスライド画像（`images/*.jpg`）から `MINUTES.md` を自動生成。
    - Web UI からチャットで「修正指示」を送信すると、CLI のセッション継続（`--resume <UUID>`）機能により文脈を維持したまま議事録を再生成・即時反映。
  - **Phase 8A (タスクトレイ常駐 ＆ リアルタイム音声波形アニメーション・チラ見安心UX)**:
    - `<OutputType>WinExe</OutputType>` ＋ `<UseWindowsForms>true</UseWindowsForms>` により、黒いコマンドプロンプト画面を出さず完全静音起動。
    - Windows Forms の `NotifyIcon` によるタスクトレイ常駐（`TrayIconService.cs`）。
    - 録音中は左上に鮮やかな赤丸（● REC）、右側に4本のリアルタイム波形バーが音量レベル・周波数スペクトラムに合わせて鮮やかなエメラルドグリーン（`#22C55E`）でピョコピョコ跳ねる動的描画（100ms周期）を実装！
    - 会議中にタスクトレイを「チラ見」するだけで、「相手の声も自分の声も確実に拾えている」ことが一目でわかり、絶大な安心感を提供。
    - 古い HICON を Win32 API `DestroyIcon` で毎回確実に解放し、GDI/USER ハンドルリークを完全防止。
    - 右クリックメニューから「🔴 記録開始」「⏹️ 記録停止」「📸 スクリーンショット」「🌐 操作盤を開く」「❌ 終了」を完備。
    - Web UI 側ともバックグラウンド相互ポーリング・UIスレッド安全同期（`SynchronizationContext` / 自己修復監視ループ）で完全双方向同期。
    - 社内プロキシ環境対応: 社内GitLab・社内Whisperへの直接接続時、OS/環境変数プロキシをバイパスする `BypassProxy` オプションを完備。
- **次期実装タスク（Phase 8B: webapp-obsidian 議事録ビューア連携 ＆ GitLabバックフィル ＆ 外部音声インポート）**:
  - ~~**Phase 8B-0**: `MINUTES_GUIDE.md` 準拠フォーマット（経過時間ベース `HH_MM_SS_screen.jpg` スクショ命名、`transcript.yaml` 同時生成、`README.md` / `MINUTES.md` アトミックコミット）の完全対応。~~ ✅ **完了** (`75ca6d2`)
  - **Phase 8B-1**: GitLab上の未処理フォルダ（音声はあるがtranscriptやminutesがない過去会議）の自動検出 ＆ バックフィル（文字起こし＋AI議事録作成・再コミット）機能。
  - **Phase 8B-2**: 単品録音ファイル（iPhoneボイスメモ等のm4a/mp3）のドラッグ＆ドロップインポートと全自動パイプライン接続。

---

## ⚡ 爆速ローカル開発サイクル（GitHub Actions スキップ）

Windows検証機（`win-test`）上に `.NET 8.0.425 SDK`（`C:\dotnet`）の配備が完了したため、**開発中のGitHub Actions待ち（毎回1〜2分）を完全にスキップ可能**です！

```bash
# Macから1コマンドでWindows検証機へソース転送 ➜ 実機ローカルビルド ➜ プロセス再起動 (約10秒で完了)
./tools/deploy-local.sh
```

- **開発時**: コード修正後、`./tools/deploy-local.sh` で手元実機へ瞬時に反映・即座検証（PDCAが10秒で回る）。
- **リリース時**: 機能実装が一区切りついた段階でのみ GitHub に `git push` し、GitHub Actions で公式 zip リリース（`vlatest`）を生成する。

---

## 🎙️ Whisper テスト・検証環境（Macローカル）

開発・動作確認用として、以下の2つのテスト環境を用意済みです：

1. **超軽量Pythonモックサーバー (外部ライブラリ不要)**:
   ```bash
   # Mac上で起動 (ポート8000)
   python3 tools/whisper-mock/mock_server.py
   # 接続先Base URL: http://<MacのLAN_IP>:8000
   ```
   - `POST /v1/audio/transcriptions` にて即座にリアルな `verbose_json`（タイムスタンプ付き日本語セグメント4件）を返却。API通信・パース・タイムライン生成の疎通確認に最適。

2. **Docker 本物Whisperサーバー (CPU最軽量 tinyモデル)**:
   ```bash
   # Mac上で起動 (ポート8000)
   docker compose -f tools/whisper-mock/docker-compose.yml up -d
   # 接続先Base URL: http://<MacのLAN_IP>:8000
   ```
   - 実際に音声を解析してリアルな文字起こしを行いたい場合に使用可能。
