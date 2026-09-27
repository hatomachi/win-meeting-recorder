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
- **次に着手するタスク（Phase 5: 単一exe最適化 ＆ エンドツーエンド総合検証）**:
  - 実機での連続稼働テスト（長時間の安定性確認）
  - exeファイルサイズ最適化・配布パッケージングの総仕上げ
  - セルフホストWhisper等の将来構想に向けた拡張性の確認

---

## 🏗️ 開発・ビルド・検証の基本サイクル

1. **開発**: Mac環境でAIとともにC#コード・フロントエンドを記述
2. **ビルド**: GitHub Actions（`runs-on: windows-latest`）に `git push` し、単一exe（Self-contained win-x64）を自動生成
3. **検証**: Macから `ssh win-test` を叩いて最新zipの展開・実機実行・成果物取得（`scp`）まで完全自動で検証
