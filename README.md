# win-meeting-recorder

> **Windows専用・ダイアログレス会議記録＆画面差分キャプチャツール**  
> スピーカー（相手の声）＋マイクを音割れなく自動合成録音し、画面変化を検知してJPEGスクショを取得、ローカル一時保存を経て社内GitLabへ自動プッシュする単一exeツール。

### 📦 最新バイナリのダウンロード（GitHubログイン不要）
- 🚀 **直接ダウンロード（zip）**: [WinMeetingRecorder-win-x64.zip](https://github.com/hatomachi/win-meeting-recorder/releases/download/vlatest/WinMeetingRecorder-win-x64.zip)
- 🔗 **Releases ページ**: [GitHub Releases (Latest)](https://github.com/hatomachi/win-meeting-recorder/releases/tag/vlatest)
- 💻 **Windows PowerShell からの1行ダウンロード**:
  ```powershell
  Invoke-WebRequest -Uri "https://github.com/hatomachi/win-meeting-recorder/releases/download/vlatest/WinMeetingRecorder-win-x64.zip" -OutFile "WinMeetingRecorder.zip"; Expand-Archive WinMeetingRecorder.zip -DestinationPath . -Force
  ```

---

## 📖 背景と動機（なぜこれを作るのか）

1. **PWA（ブラウザ）の限界**:
   - 会議録音・画面共有のたびにブラウザの画面選択ダイアログが開き、毎回「システム音声を共有する」を手動でチェックしなければならない。
   - チェックをうっかり忘れると「相手の声が無音になる」という事故が起きるため、完全自動化・忘れ防止のためにネイティブ化が必要。
2. **Go言語での音割れ・ミキシング問題**:
   - Go Native EngineでWASAPIループバック＋マイク合成を試みたが、スピーカー（48kHz Float32）とマイク（44.1kHz Int16等）のサンプリングレート・チャンネル不一致やリサンプラー欠如により、音割れ（クリッピング）が解消しなかった。
   - 一方で、Windowsオーディオ界のデファクトスタンダードである **C# (.NET 8 + NAudio)** であれば、`MixingSampleProvider` による自動リサンプリング・Float32ミキシングが組み込まれており、音割れなく極めて素直に動くことが判明。
3. **会社環境の制約と検証体制の確立**:
   - 会社PCはセキュリティポリシーにより「デバッグログの社外持ち出しが不可」、かつPC容量がカツカツで重い開発環境（SDK等）を入れたくない。
   - クラウド（AWS EC2 Windows Server）は物理オーディオデバイスや画面セッションがなく、この種のテストには不向き。
   - ➜ **秋葉原にて手元検証用のWindows PCを調達完了**。Macで開発 ➜ GitHub Actionsで単一exeビルド ➜ 手元Windows実機でRDP検証・ログ確認という完全な開発ループを確立。

---

## 🎯 やりたいこと（主要機能要件）

### ① 完全ダイアログレス録音（音割れなし）
- 画面共有ピッカーを出さず、ワンクリックで即座に録音開始。
- **スピーカー出力（WASAPI Loopback）**: PCから流れる相手の声（Zoom/Teams等）やシステム音を自動キャプチャ。
- **マイク入力**: 任意のマイクデバイスから自分の声を録音。
- **自動リサンプリング＆ミキシング**: 異なる周波数・チャンネルの音声を音割れ・歪みなく1本の音声ファイル（WAV/MP3）に合成。

### ② 画面選択 & 画面変化検知スクショ
- キャプチャ対象のディスプレイを選択（設定保存で次回以降自動化）。
- スライド等の画面切り替えを自動検知（ピクセル差分比較）し、変化があったタイミングでフル解像度JPEGスクショを自動保存。
- アニメーション中の過剰連写を防ぐクールダウン／デバウンス処理。

### ③ ローカル即時保存 & 社内GitLab連携（堅牢性・オフラインファースト）
- 会議中の音声・スクショをPCローカル（指定フォルダ）にストリーミング即時保存（通信障害やクラッシュでもデータロストゼロ）。
- 会議終了後、社内GitLab（REST API / Commits API）へ一括プッシュ。
- プッシュ成功後はローカル一時ファイルを自動消去し、PCのディスク容量ゼロを維持（ローカル保持設定も可能）。

### ④ ポータブル単一exe配布（Self-contained）
- 会社PCに.NET SDKやランタイムのインストールは不要。
- 容量30〜60MB程度の単一 `.exe` ファイルを落としてダブルクリックするだけで起動。

---

## 🏗️ 実現イメージ（技術選定・アーキテクチャ方針）

```
[手元Windows PC]
  ┌──────────────────────────────────────────────────────────┐
  │ win-meeting-recorder.exe (単一バイナリ / .NET 8)         │
  │                                                          │
  │ ┌──────────────────────────────────────────────────────┐ │
  │ │ Web UI (ブラウザ / msedge.exe --app)                 │ │
  │ │ - 録音開始・停止、設定（マイク選択、GitLabトークン等）│ │
  │ └──────────────────────────┬───────────────────────────┘ │
  │                            │ HTTP (localhost:PORT)       │
  │ ┌──────────────────────────▼───────────────────────────┐ │
  │ │ バックエンド (ASP.NET Core Minimal API)              │ │
  │ │                                                      │ │
  │ │ - オーディオエンジン: NAudio (WasapiLoopback + Mic)  │ │
  │ │ - キャプチャエンジン: GDI+ / Graphics.CopyFromScreen │ │
  │ │ - ストレージ: ローカル一時スプール書き出し           │ │
  │ │ - GitLab クライアント: HttpClient (Commits API)      │ │
  │ └──────────────────────────────────────────────────────┘ │
  └──────────────────────────────────────────────────────────┘
```

- **バックエンド**: C# (.NET 8 / 9)
  - 音声: `NAudio`（`WasapiLoopbackCapture`, `WaveInEvent`, `MixingSampleProvider`）
  - 画面: `System.Drawing.Common`（GDI+ CopyFromScreen）
  - サーバー: ASP.NET Core Minimal API（ローカルREST API）
- **フロントエンド（UI）**: Web UI（HTML / Tailwind / React等）
  - PWA等で培ったUI資産を活用。C#から静的ファイルとして配信し、ブラウザで操作。
- **ビルド・検証体制**:
  - **ビルド**: GitHub Actions（`runs-on: windows-latest`）によるクロスビルド（`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`）
  - **検証**: 手元Windows検証機（MacからMicrosoft Remote Desktop接続、実機ログ手元確認）

---

## 📋 別セッションで詰める実装計画の論点

本リポジトリでの開発開始にあたり、以下のステップで実装計画を策定してください。

1. **プロジェクト初期化 & CI環境構築**:
   - .NET 8 ソリューション／プロジェクト構成の決定
   - GitHub Actions 自動ビルドワークフロー（単一exe出力）の整備
2. **Phase 1: 音声エンジンのプロトタイプ検証**:
   - NAudio による WASAPI Loopback ＋ マイク録音の合成検証（手元Windows機で音割れがないことの確認）
3. **Phase 2: 画面キャプチャ＆変化検知エンジンの検証**:
   - 指定画面のキャプチャ、差分判定ロジック、JPEG保存の動作確認
4. **Phase 3: Web UI ＋ Minimal API 連携**:
   - 操作画面（開始・停止、デバイス設定）とC#バックエンドの疎通
5. **Phase 4: GitLab REST API 連携 ＆ 堅牢化**:
   - ローカルスプール管理、GitLab Commits API 送信、エラー時のリトライ／クリーンアップ
