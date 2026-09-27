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
          <p class="text-xs text-slate-400">Windows専用 ダイアログレス会議記録ツール</p>
        </div>
      </div>
      <div id="statusBadge" class="px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30">
        待機中
      </div>
    </div>

    <!-- デバイス選択 -->
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
    </div>

    <!-- 録音タイマー & カウンター -->
    <div class="bg-slate-950/60 rounded-xl p-4 border border-slate-800 flex items-center justify-around text-center">
      <div>
        <p class="text-xs text-slate-400">録音時間</p>
        <p id="timer" class="text-2xl font-mono font-bold text-white mt-1">00:00:00</p>
      </div>
      <div class="w-px h-8 bg-slate-800"></div>
      <div>
        <p class="text-xs text-slate-400">音声ミキシング</p>
        <p class="text-sm font-semibold text-indigo-400 mt-1">48kHz Float32</p>
      </div>
    </div>

    <!-- アクションボタン -->
    <div class="pt-2">
      <button id="toggleBtn" class="w-full py-4 rounded-xl font-bold text-lg shadow-lg transition-all duration-200 bg-red-600 hover:bg-red-500 text-white flex items-center justify-center space-x-2">
        <span id="btnIcon" class="inline-block w-4 h-4 rounded-full bg-white animate-pulse"></span>
        <span id="btnText">録音を開始する</span>
      </button>
    </div>

    <!-- メッセージ・ログ -->
    <div id="logMessage" class="text-xs text-center text-slate-400 break-all min-h-[1.5rem]">
      初期化完了。録音を開始できます。
    </div>
  </div>

  <script>
    let isRecording = false;
    let timerInterval = null;
    let secondsElapsed = 0;

    const toggleBtn = document.getElementById('toggleBtn');
    const btnText = document.getElementById('btnText');
    const btnIcon = document.getElementById('btnIcon');
    const statusBadge = document.getElementById('statusBadge');
    const timerDisplay = document.getElementById('timer');
    const logMessage = document.getElementById('logMessage');
    const playbackSelect = document.getElementById('playbackSelect');
    const captureSelect = document.getElementById('captureSelect');

    async function loadDevices() {
      try {
        const res = await fetch('/api/devices');
        if (!res.ok) return;
        const data = await res.json();

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
      } catch (err) {
        logMessage.textContent = 'デバイス取得エラー: ' + err.message;
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
      clearInterval(timerInterval);
      timerInterval = setInterval(() => {
        secondsElapsed++;
        timerDisplay.textContent = formatTime(secondsElapsed);
      }, 1000);
    }

    function stopTimer() {
      clearInterval(timerInterval);
    }

    toggleBtn.addEventListener('click', async () => {
      if (!isRecording) {
        toggleBtn.disabled = true;
        logMessage.textContent = '録音開始リクエスト中...';
        try {
          const res = await fetch('/api/record/start', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              playbackDeviceId: playbackSelect.value || null,
              captureDeviceId: captureSelect.value || null
            })
          });
          const data = await res.json();
          if (res.ok) {
            isRecording = true;
            statusBadge.textContent = '録音中';
            statusBadge.className = 'px-3 py-1 rounded-full text-xs font-semibold bg-red-500/20 text-red-400 border border-red-500/30';
            toggleBtn.className = 'w-full py-4 rounded-xl font-bold text-lg shadow-lg transition-all duration-200 bg-slate-700 hover:bg-slate-600 text-white flex items-center justify-center space-x-2';
            btnText.textContent = '録音を停止する';
            btnIcon.className = 'inline-block w-4 h-4 bg-red-400 rounded-sm';
            startTimer();
            logMessage.textContent = `録音中: ${data.audioPath}`;
          } else {
            logMessage.textContent = `エラー: ${data.error || '録音開始失敗'}`;
          }
        } catch (err) {
          logMessage.textContent = `エラー: ${err.message}`;
        } finally {
          toggleBtn.disabled = false;
        }
      } else {
        toggleBtn.disabled = true;
        logMessage.textContent = '録音停止 & ファイル確定中...';
        try {
          const res = await fetch('/api/record/stop', { method: 'POST' });
          const data = await res.json();
          if (res.ok) {
            isRecording = false;
            stopTimer();
            statusBadge.textContent = '待機中';
            statusBadge.className = 'px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30';
            toggleBtn.className = 'w-full py-4 rounded-xl font-bold text-lg shadow-lg transition-all duration-200 bg-red-600 hover:bg-red-500 text-white flex items-center justify-center space-x-2';
            btnText.textContent = '録音を開始する';
            btnIcon.className = 'inline-block w-4 h-4 rounded-full bg-white animate-pulse';
            logMessage.textContent = `保存完了: ${data.file}`;
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

    loadDevices();
  </script>
</body>
</html>
""";
}
