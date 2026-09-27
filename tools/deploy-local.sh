#!/usr/bin/env bash
set -e

# ==============================================================================
# 手元Windows検証機への超高速ローカルビルド＆デプロイスクリプト
# GitHub Actions のビルド待ち（1〜2分）をスキップし、実機上で直接数秒でビルド＆再起動します。
# ==============================================================================

WIN_HOST="win-test"
REMOTE_SRC="C:/work/src"
REMOTE_WORK="C:/work"

echo "🚀 [1/3] ソースコードを Windows 検証機へ転送中..."
ssh $WIN_HOST "if (!(Test-Path 'C:\work\src')) { New-Item -ItemType Directory -Path 'C:\work\src' -Force }"
scp -r src/WinMeetingRecorder $WIN_HOST:C:/work/src/
scp README_HOW_TO_USE.txt $WIN_HOST:C:/work/

echo "🔨 [2/3] Windows 実機上でローカル高速ビルド中 (dotnet publish)..."
ssh $WIN_HOST "C:\dotnet\dotnet.exe publish C:\work\src\WinMeetingRecorder\WinMeetingRecorder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o C:\work"

echo "🔄 [3/3] 実行中プロセスを停止し、新バイナリを対話セッションで再起動中..."
ssh $WIN_HOST "Stop-Process -Name WinMeetingRecorder -Force -ErrorAction SilentlyContinue; schtasks /create /tn 'StartRecorder' /tr 'C:\work\WinMeetingRecorder.exe --no-browser' /sc once /st '23:59' /ru 'sohik' /it /f; schtasks /run /tn 'StartRecorder'; Start-Sleep -Seconds 3; schtasks /delete /tn 'StartRecorder' /f"

echo "✅ デプロイ完了！"
echo "Web UI URL: http://192.168.11.19:5000"
