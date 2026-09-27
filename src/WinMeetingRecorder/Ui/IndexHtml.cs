namespace WinMeetingRecorder.Ui;

public static class IndexHtml
{
    public const string Content = """
<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>WinMeetingRecorder</title>
  <script src="https://cdn.tailwindcss.com"></script>
</head>
<body class="bg-slate-900 text-slate-100 min-h-screen flex flex-col items-center justify-center p-6 font-sans">
  <div class="w-full max-w-lg bg-slate-800 rounded-2xl shadow-2xl border border-slate-700 p-8 space-y-6">
    <div class="flex items-center justify-between border-b border-slate-700 pb-4">
      <div class="flex items-center space-x-3">
        <span class="text-3xl">🎙️</span>
        <div>
          <h1 class="text-xl font-bold tracking-tight">WinMeetingRecorder</h1>
          <p class="text-xs text-slate-400">Windows専用 ダイアログレス会議記録＆画面差分キャプチャ</p>
        </div>
      </div>
      <div id="statusBadge" class="px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30">
        待機中
      </div>
    </div>

    <!-- デバイス・画面選択 -->
    <div class="space-y-4">
      <div>
        <label class="block text-xs font-medium text-slate-300 mb-1">🔊 スピーカー (相手の声 / Loopback)</label>
        <select id="playbackSelect" class="w-full bg-slate-950 border border-slate-700 rounded-lg px-3 py-2 text-sm text-slate-200 focus:outline-none focus:border-indigo-500">
          <option value="">既定の再生デバイス</option>
        </select>
      </div>
      <div>
        <label class="block text-xs font-medium text-slate-300 mb-1">🎤 マイク (自分の声)</label>
        <select id="captureSelect" class="w-full bg-slate-950 border border-slate-700 rounded-lg px-3 py-2 text-sm text-slate-200 focus:outline-none focus:border-indigo-500">
          <option value="">既定のマイクデバイス</option>
        </select>
      </div>
      <div>
        <label class="block text-xs font-medium text-slate-300 mb-1">🖥️ 対象ディスプレイ (スライド変化自動検知)</label>
        <select id="screenSelect" class="w-full bg-slate-950 border border-slate-700 rounded-lg px-3 py-2 text-sm text-slate-200 focus:outline-none focus:border-indigo-500">
          <option value="0">プライマリディスプレイ (既定)</option>
        </select>
      </div>
    </div>

    <!-- 録画タイマー & カウンター -->
    <div class="bg-slate-950/60 rounded-xl p-4 border border-slate-800 flex items-center justify-around text-center">
      <div>
        <p class="text-xs text-slate-400">記録時間</p>
        <p id="timer" class="text-2xl font-mono font-bold text-white mt-1">00:00:00</p>
      </div>
      <div class="w-px h-8 bg-slate-800"></div>
      <div>
        <p class="text-xs text-slate-400">撮影スクショ</p>
        <p id="screenshotCount" class="text-2xl font-mono font-bold text-indigo-400 mt-1">0 <span class="text-xs font-normal text-slate-400">枚</span></p>
      </div>
      <div class="w-px h-8 bg-slate-800"></div>
      <div>
        <p class="text-xs text-slate-400">音声ミキシング</p>
        <p class="text-sm font-semibold text-emerald-400 mt-1">48kHz Float32</p>
      </div>
    </div>

    <!-- アクションボタン群 -->
    <div class="space-y-3 pt-2">
      <button id="toggleBtn" class="w-full py-4 rounded-xl font-bold text-lg shadow-lg transition-all duration-200 bg-red-600 hover:bg-red-500 text-white flex items-center justify-center space-x-2">
        <span id="btnIcon" class="inline-block w-4 h-4 rounded-full bg-white animate-pulse"></span>
        <span id="btnText">会議の記録を開始する</span>
      </button>

      <!-- 記録中のみ表示される手動スクショボタン -->
      <button id="manualScreenshotBtn" class="hidden w-full py-2.5 rounded-xl font-medium text-sm transition-all duration-200 bg-slate-700 hover:bg-slate-600 text-slate-200 flex items-center justify-center space-x-2 border border-slate-600">
        <span>📸</span>
        <span>今すぐスクショを1枚撮影 (手動)</span>
      </button>
    </div>

    <!-- メッセージ・ログ -->
    <div id="logMessage" class="text-xs text-center text-slate-400 break-all min-h-[1.5rem]">
      初期化完了。音声・画面の記録を開始できます。
    </div>
  </div>

  <script>
    let isRecording = false;
    let timerInterval = null;
    let statusInterval = null;
    let secondsElapsed = 0;

    const toggleBtn = document.getElementById('toggleBtn');
    const btnText = document.getElementById('btnText');
    const btnIcon = document.getElementById('btnIcon');
    const manualScreenshotBtn = document.getElementById('manualScreenshotBtn');
    const statusBadge = document.getElementById('statusBadge');
    const timerDisplay = document.getElementById('timer');
    const screenshotCountDisplay = document.getElementById('screenshotCount');
    const logMessage = document.getElementById('logMessage');
    const playbackSelect = document.getElementById('playbackSelect');
    const captureSelect = document.getElementById('captureSelect');
    const screenSelect = document.getElementById('screenSelect');

    async function loadDevicesAndScreens() {
      try {
        // デバイス一覧
        const devRes = await fetch('/api/devices');
        if (devRes.ok) {
          const data = await devRes.json();
          playbackSelect.innerHTML = '<option value="">既定の再生デバイス</option>';
          data.playback.forEach(d => {
            const opt = document.createElement('option');
            opt.value = d.id;
            opt.textContent = `${d.name} ${d.isDefault ? '(既定)' : ''}`;
            playbackSelect.appendChild(opt);
          });

          captureSelect.innerHTML = '<option value="">既定のマイクデバイス</option>';
          data.capture.forEach(d => {
            const opt = document.createElement('option');
            opt.value = d.id;
            opt.textContent = `${d.name} ${d.isDefault ? '(既定)' : ''}`;
            captureSelect.appendChild(opt);
          });
        }

        // 画面一覧
        const screenRes = await fetch('/api/screens');
        if (screenRes.ok) {
          const data = await screenRes.json();
          screenSelect.innerHTML = '';
          data.screens.forEach(s => {
            const opt = document.createElement('option');
            opt.value = s.index;
            opt.textContent = `${s.name} (${s.bounds.width}x${s.bounds.height}) ${s.isPrimary ? '(プライマリ)' : ''}`;
            screenSelect.appendChild(opt);
          });
          if (data.virtualDesktop) {
            const vdOpt = document.createElement('option');
            vdOpt.value = "-1";
            vdOpt.textContent = `全ディスプレイ結合 (仮想: ${data.virtualDesktop.bounds.width}x${data.virtualDesktop.bounds.height})`;
            screenSelect.appendChild(vdOpt);
          }
          const offOpt = document.createElement('option');
          offOpt.value = "off";
          offOpt.textContent = "画面キャプチャなし (音声のみ)";
          screenSelect.appendChild(offOpt);
        }
      } catch (err) {
        logMessage.textContent = '初期ロードエラー: ' + err.message;
      }
    }

    function formatTime(sec) {
      const h = String(Math.floor(sec / 3600)).padStart(2, '0');
      const m = String(Math.floor((sec % 3600) / 60)).padStart(2, '0');
      const s = String(sec % 60).padStart(2, '0');
      return `${h}:${m}:${s}`;
    }

    function startTimer() {
      secondsElapsed = 0;
      timerDisplay.textContent = '00:00:00';
      screenshotCountDisplay.innerHTML = '0 <span class="text-xs font-normal text-slate-400">枚</span>';
      clearInterval(timerInterval);
      timerInterval = setInterval(() => {
        secondsElapsed++;
        timerDisplay.textContent = formatTime(secondsElapsed);
      }, 1000);

      // ステータスポーリング開始（スクショ枚数リアルタイム同期）
      clearInterval(statusInterval);
      statusInterval = setInterval(async () => {
        try {
          const res = await fetch('/api/status');
          if (res.ok) {
            const s = await res.json();
            if (s.isCapturing !== undefined) {
              screenshotCountDisplay.innerHTML = `${s.capturedCount || 0} <span class="text-xs font-normal text-slate-400">枚</span>`;
            }
          }
        } catch { /* ignore */ }
      }, 2000);
    }

    function stopTimer() {
      clearInterval(timerInterval);
      clearInterval(statusInterval);
    }

    toggleBtn.addEventListener('click', async () => {
      if (!isRecording) {
        toggleBtn.disabled = true;
        logMessage.textContent = '記録開始リクエスト中...';
        try {
          const screenVal = screenSelect.value;
          const enableScreen = screenVal !== 'off';
          const monitorIdx = enableScreen ? parseInt(screenVal, 10) : 0;

          const res = await fetch('/api/record/start', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              playbackDeviceId: playbackSelect.value || null,
              captureDeviceId: captureSelect.value || null,
              monitorIndex: monitorIdx,
              enableScreenCapture: enableScreen
            })
          });
          const data = await res.json();
          if (res.ok) {
            isRecording = true;
            statusBadge.textContent = '記録中';
            statusBadge.className = 'px-3 py-1 rounded-full text-xs font-semibold bg-red-500/20 text-red-400 border border-red-500/30';
            toggleBtn.className = 'w-full py-4 rounded-xl font-bold text-lg shadow-lg transition-all duration-200 bg-slate-700 hover:bg-slate-600 text-white flex items-center justify-center space-x-2';
            btnText.textContent = '記録を停止する';
            btnIcon.className = 'inline-block w-4 h-4 bg-red-400 rounded-sm';
            manualScreenshotBtn.classList.remove('hidden');
            playbackSelect.disabled = true;
            captureSelect.disabled = true;
            screenSelect.disabled = true;
            startTimer();
            logMessage.textContent = `記録セッション開始: ${data.sessionDir}`;
          } else {
            logMessage.textContent = `エラー: ${data.error || '記録開始失敗'}`;
          }
        } catch (err) {
          logMessage.textContent = `エラー: ${err.message}`;
        } finally {
          toggleBtn.disabled = false;
        }
      } else {
        toggleBtn.disabled = true;
        logMessage.textContent = '記録停止 & ファイル保存中...';
        try {
          const res = await fetch('/api/record/stop', { method: 'POST' });
          const data = await res.json();
          if (res.ok) {
            isRecording = false;
            stopTimer();
            statusBadge.textContent = '待機中';
            statusBadge.className = 'px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30';
            toggleBtn.className = 'w-full py-4 rounded-xl font-bold text-lg shadow-lg transition-all duration-200 bg-red-600 hover:bg-red-500 text-white flex items-center justify-center space-x-2';
            btnText.textContent = '会議の記録を開始する';
            btnIcon.className = 'inline-block w-4 h-4 rounded-full bg-white animate-pulse';
            manualScreenshotBtn.classList.add('hidden');
            playbackSelect.disabled = false;
            captureSelect.disabled = false;
            screenSelect.disabled = false;
            screenshotCountDisplay.innerHTML = `${data.capturedCount || 0} <span class="text-xs font-normal text-slate-400">枚</span>`;
            logMessage.textContent = `保存完了！ 音声: ${data.audioFile || 'なし'} / スクショ: ${data.capturedCount || 0}枚`;
          } else {
            logMessage.textContent = `エラー: ${data.error || '停止失敗'}`;
          }
        } catch (err) {
          logMessage.textContent = `エラー: ${err.message}`;
        } finally {
          toggleBtn.disabled = false;
        }
      }
    });

    manualScreenshotBtn.addEventListener('click', async () => {
      try {
        manualScreenshotBtn.disabled = true;
        const res = await fetch('/api/record/screenshot', { method: 'POST' });
        const data = await res.json();
        if (res.ok) {
          logMessage.textContent = `📸 手動スクショ保存: ${data.capture?.fileName || '完了'}`;
        } else {
          logMessage.textContent = `スクショ撮影失敗: ${data.error || ''}`;
        }
      } catch (err) {
        logMessage.textContent = `スクショエラー: ${err.message}`;
      } finally {
        manualScreenshotBtn.disabled = false;
      }
    });

    loadDevicesAndScreens();
  </script>
</body>
</html>
""";
}
