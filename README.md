# win-meeting-recorder

> **Windows専用・Box API 自動連携 軽量会議記録クライアント**  
> スピーカー（相手の声）＋マイクを音割れなく自動合成録音（高圧縮MP3）し、画面変化を検知してJPEGスクショを取得、録音停止後に指定テーマのBoxフォルダへ全自動一括アップロードする単一exeツール。

### 📦 最新バイナリのダウンロード（GitHubログイン不要）
- 🚀 **直接ダウンロード（zip）**: [WinMeetingRecorder-win-x64.zip](https://github.com/hatomachi/win-meeting-recorder/releases/download/vlatest/WinMeetingRecorder-win-x64.zip)
- 🔗 **Releases ページ**: [GitHub Releases (Latest)](https://github.com/hatomachi/win-meeting-recorder/releases/tag/vlatest)
- 💻 **Windows PowerShell からの1行ダウンロード**:
  ```powershell
  Invoke-WebRequest -Uri "https://github.com/hatomachi/win-meeting-recorder/releases/download/vlatest/WinMeetingRecorder-win-x64.zip" -OutFile "WinMeetingRecorder.zip"; Expand-Archive WinMeetingRecorder.zip -DestinationPath . -Force
  ```

---

## 📖 背景と設計方針（Box API 1本化 ＆ 役割の軽量化）

1. **手元PCの超軽量化（重い処理の廃止）**:
   - これまでのローカルでのWhisper文字起こしやAI要約、社内GitLabプッシュ機能などの重い処理を全廃。
   - 手元PCの役割は **「高品質な合成録音（MP3）＋ 画面変化スクショ ➔ Box APIで指定フォルダへ自動アップロード」** に特化。
   - 文字起こし・AI要約・Confluence起票は、Boxを監視する後段の社内集約サーバーが全自動で行う設計へ刷新。
2. **社外委託・派遣・社員が共通で使える「Box API 1本化」**:
   - 設定ファイルの取得も、音声・画像の送信も、すべて **Box API（OAuth 2.0 ユーザー認証）** 経由で完結。
   - クライアント側に追加の認証情報（GitLabトークンやConfluenceトークン、社内プロキシ設定等）を持たせず、極めてシンプルに運用可能。
3. **完全ダイアログレス・音割れなしの合成録音**:
   - PWAの画面共有ダイアログや「システム音声を共有する」のチェックつけ忘れによる無音事故をゼロに。
   - C# (.NET 8 + NAudio) による WASAPI Loopback ＋ マイク音声の自動リサンプリング・Float32ミキシングにより、音割れ・歪みなく1本の高音質音声に合成。
   - Windows標準機能（MediaFoundation）により、外部ffmpeg不要で 24kHz モノラル高圧縮 MP3（容量96.8%削減）へ瞬時に自動変換。
4. **ポータブル単一exe（配布・実行が極めて容易）**:
   - 会社PCに .NET SDK やランタイムのインストールは不要。単一 `.exe`（Self-contained win-x64）をダブルクリックするだけで即座に起動。

---

## 🎯 主要機能

### ① Box OAuth 2.0 ユーザー認証基盤
- 初回起動時や未認証時は、ローカル一時HTTPリスナー（`http://localhost:5001/callback`）を自動起動。
- 既定のブラウザで Box 認可画面を自動オープンし、1クリックでログイン完了。
- 企業の独自Boxドメイン（SSO認可画面・サブドメイン `box_subdomain`）および初期設定テンプレート（`config.default.json`）の同梱配布に対応。
- 認可コードから `access_token` / `refresh_token` を取得し、ローカルに安全保存。
- トークン有効期限切れ時の自動リフレッシュ（`grant_type=refresh_token`）を完備。

### ② 会議テーマ台帳 (`themes.json`) の動的取得
- 起動時（および手動更新時）に、Box の共通フォルダ配下にある `themes.json` を動的にGETしてロード。
- Web UI および タスクトレイ右クリックメニューに会議テーマ選択ドロップダウンを配置（オフライン時はキャッシュへ自動フォールバック）。

### ③ 画面変化検知スクリーンショット（pHash 8x8 DCT 知覚ハッシュ ＆ 過剰撮影防止）
- 対象ディスプレイを選択し、スライドめくり等の画面切り替えを**pHash 8x8 DCT（知覚ハッシュ）**で高精度検知。
- 時計の秒針やマウスカーソル、微小な文字の点滅などの高周波ノイズを完全無視し、**スライドが切り替わった時のみ**確実にフル解像度JPEG（`screen_YYYYMMDD_HHMMSS.jpg`）を保存。
- 監視周期（デフォルト5秒）、定期強制保存（デフォルト0秒・無効）、ハミング距離閾値（デフォルト5）などを `config.json` または Web UI から自在にチューニング可能。
- 会議中の手動即時スクショボタン（Web UI / トレイ）も完備。

### ④ 録音停止後の Box 自動一括アップロード ＆ 順序制御
- 会議終了時、選択されたテーマの `box_folder_id` 配下に、セッションフォルダ `YYYYMMDD_HHMMSS` を自動作成。
- **厳格なアップロード順序制御**:
  1. `meeting_audio.mp3`（50MB超の大容量ファイルは Box Chunked Upload API / Upload Session を使用）
  2. `screen_YYYYMMDD_HHMMSS.jpg`（画面差分スクショ全件）
  3. ★**`meta.json`（後段サーバーの検知トリガーとして必ず全ファイル転送完了後に最後にアップロード）**

### ⑤ スプール消去前の二重安全確認（Read-after-Write Verification）
- アップロード完了後、直ちにローカルファイルを消さず、**Box API（`GET /folders/{folderId}/items`）で作成フォルダ内を一覧再取得**。
- アップロード対象だった全ファイルが確実に存在し、ファイルサイズが0より大きいことを厳格に検証。
- 1件でも欠落・サイズ異常がある場合はスプール消去をブロックしてローカルに保持。全件の実在確認が完了して初めてスプール一時ファイルを完全自動消去。

### ⑥ タスクトレイ常駐 ＆ チラ見安心UX
- 黒いコマンド画面（DOS窓）を出さず完全静音起動。
- 録音中はタスクトレイ極小アイコン（32x32）の左上に赤い●RECマーク、右側に**マイク・相手の声の音量に合わせてピョコピョコ跳ねる緑のリアルタイム波形アニメーション（100ms周期）**を描画。
- 右クリックメニューから「🔴 記録開始」「⏹️ 記録停止」「📸 スクリーンショット」「📋 テーマ切替」「🌐 操作盤を開く」「❌ 終了」を操作可能。

---

## 🏗️ システムアーキテクチャ

```
[クライアント PC (社員・派遣・委託)]
  ┌──────────────────────────────────────────────────────────┐
  │ WinMeetingRecorder.exe (単一バイナリ / .NET 8 WinExe)    │
  │                                                          │
  │ ┌──────────────────────────────────────────────────────┐ │
  │ │ Web UI (http://localhost:5000) / タスクトレイ常駐    │ │
  │ │ - テーマ選択、Box認証状態、録音開始・停止、進捗表示 │ │
  │ └──────────────────────────┬───────────────────────────┘ │
  │                            │                             │
  │ ┌──────────────────────────▼───────────────────────────┐ │
  │ │ バックエンドサービス群                               │ │
  │ │ - AudioEngine: NAudio (WasapiLoopback + Mic)         │ │
  │ │ - AudioConverter: 24kHz Mono 64kbps MP3 高圧縮       │ │
  │ │ - ScreenCaptureEngine: 画面差分検知 JPEG 保存         │ │
  │ │ - BoxService: OAuth2, Chunked Upload, meta.json生成  │ │
  │ └──────────────────────────┬───────────────────────────┘ │
  └────────────────────────────┼─────────────────────────────┘
                               │ HTTPS (Box API / OAuth 2.0)
                               ▼
  ┌──────────────────────────────────────────────────────────┐
  │ Box (クラウドストレージ)                                │
  │                                                          │
  │ 📁 共通フォルダ                                          │
  │    └── themes.json (会議テーマ台帳)                      │
  │                                                          │
  │ 📁 各定例フォルダ (例: GroupAWS 定例)                    │
  │    └── 📁 20261031_214500 (セッションフォルダ)           │
  │           ├── meeting_audio.mp3                          │
  │           ├── screen_20261031_214505.jpg                 │
  │           └── meta.json (★最終完了トリガー)            │
  └────────────────────────────┬─────────────────────────────┘
                               │ Webhook / フォルダ監視
                               ▼
  ┌──────────────────────────────────────────────────────────┐
  │ 社内集約サーバー (後段)                                  │
  │ - meta.json 検知 ➜ 文字起こし ➜ AI要約 ➜ Confluence起票  │
  └──────────────────────────────────────────────────────────┘
```

---

## 🚀 使い方

### 1. 起動
`WinMeetingRecorder.exe` をダブルクリックするだけです。
タスクトレイに常駐し、既定のブラウザで操作画面（`http://localhost:5000`）が自動で開きます。

### 2. 初回 Box 認証
1. Web UI の「🌐 Boxにログイン」ボタンをクリック。
2. ブラウザで Box の認可画面が開くので「Boxへのアクセスを許可」をクリック。
3. 自動でアプリと連携され、「🟢 ○○ 様 認証済み」と表示されます。

### 3. 会議の記録と自動アップロード
1. 会議前に Web UI または トレイメニューから **「会議テーマ」** を選択。
2. 「🔴 記録開始」をクリック（会議中、タスクトレイの波形が声に合わせて跳ねます）。
3. 会議終了後、「⏹️ 記録停止」をクリック。
4. MP3変換 ➜ Boxセッションフォルダ作成 ➜ 音声・スクショ・meta.json一括アップロード ➜ Box上での実在二重確認 ➜ ローカル一時ファイル消去 まで全自動で完了します。

---

## 🛠️ CLI オプション (検証・自動化向け)

```bash
# ヘルプ表示
WinMeetingRecorder.exe --help

# デバイス・ディスプレイ一覧
WinMeetingRecorder.exe --list-devices
WinMeetingRecorder.exe --list-screens

# 音声合成テスト (10秒録音)
WinMeetingRecorder.exe --test-audio 10 test_mixed.wav

# MP3高圧縮テスト
WinMeetingRecorder.exe --test-mp3 input.wav output.mp3

# 画面変化検知テスト (5秒間, モニタ0)
WinMeetingRecorder.exe --test-screen 5 0 ./test_out

# ブラウザ自動オープン無効化起動
WinMeetingRecorder.exe --no-browser
```
