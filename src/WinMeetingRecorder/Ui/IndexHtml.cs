namespace WinMeetingRecorder.Ui;

public static class IndexHtml
{
    public const string Content = """
<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>WinMeetingRecorder - Box連携</title>
  <script src="https://cdn.tailwindcss.com"></script>
</head>
<body class="bg-slate-900 text-slate-100 min-h-screen flex flex-col items-center justify-start py-8 px-4 font-sans">
  <div class="w-full max-w-2xl bg-slate-800 rounded-2xl shadow-2xl border border-slate-700 p-6 md:p-8 space-y-6">
    
    <!-- ヘッダー -->
    <div class="flex items-center justify-between border-b border-slate-700 pb-4">
      <div class="flex items-center space-x-3">
        <span class="text-3xl">🎙️</span>
        <div>
          <h1 class="text-xl font-bold tracking-tight">WinMeetingRecorder</h1>
          <p class="text-xs text-slate-400">Box API 自動連携・軽量会議記録クライアント</p>
        </div>
      </div>
      <div class="flex items-center space-x-2">
        <div id="statusBadge" class="px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30">
          待機中
        </div>
        <button id="shutdownBtn" title="アプリを終了" class="px-2.5 py-1 rounded-full text-xs font-medium bg-slate-800/80 hover:bg-rose-900/40 text-slate-400 hover:text-rose-300 border border-slate-700 hover:border-rose-500/40 transition flex items-center space-x-1">
          <span>✕</span>
          <span>終了</span>
        </button>
      </div>
    </div>

    <!-- Box 認証ステータス ＆ 会議テーマ選択バー -->
    <div class="bg-slate-950/60 border border-slate-700/80 rounded-xl p-4 space-y-3">
      <div class="flex items-center justify-between">
        <div class="flex items-center space-x-2 text-xs">
          <span class="text-slate-400">Box認証状態:</span>
          <span id="boxAuthBadge" class="px-2 py-0.5 rounded text-[11px] font-medium bg-slate-800 text-slate-400">確認中...</span>
        </div>
        <button id="boxLoginQuickBtn" class="hidden px-2.5 py-1 rounded text-xs font-semibold bg-blue-600 hover:bg-blue-500 text-white transition flex items-center space-x-1">
          <span>🌐</span>
          <span>Boxにログイン</span>
        </button>
      </div>

      <div>
        <div class="flex items-center justify-between mb-1">
          <label class="block text-xs font-semibold text-slate-300">📋 会議テーマ選択 (Box保存先フォルダ)</label>
          <button id="refreshThemesBtn" title="Boxから最新のthemes.jsonを取得" class="text-[11px] text-indigo-400 hover:text-indigo-300 flex items-center space-x-1">
            <span>🔄</span>
            <span>テーマ更新</span>
          </button>
        </div>
        <select id="themeSelect" class="w-full bg-slate-900 border border-slate-700 rounded-lg px-2.5 py-2 text-xs text-slate-200 focus:outline-none focus:border-indigo-500">
          <option value="">テーマ読み込み中...</option>
        </select>
        <p id="themeInfo" class="text-[10px] text-slate-500 mt-1">選択されたテーマのフォルダへ自動アップロードされます。</p>
      </div>
    </div>

    <!-- デバイス・画面選択 -->
    <div class="space-y-3">
      <div class="grid grid-cols-1 md:grid-cols-2 gap-3">
        <div>
          <label class="block text-xs font-medium text-slate-300 mb-1">🔊 スピーカー (相手の声 / Loopback)</label>
          <select id="playbackSelect" class="w-full bg-slate-950 border border-slate-700 rounded-lg px-2.5 py-1.5 text-xs text-slate-200 focus:outline-none focus:border-indigo-500">
            <option value="">既定の再生デバイス</option>
          </select>
        </div>
        <div>
          <label class="block text-xs font-medium text-slate-300 mb-1">🎤 マイク (自分の声)</label>
          <select id="captureSelect" class="w-full bg-slate-950 border border-slate-700 rounded-lg px-2.5 py-1.5 text-xs text-slate-200 focus:outline-none focus:border-indigo-500">
            <option value="">既定のマイクデバイス</option>
          </select>
        </div>
      </div>
      <div>
        <label class="block text-xs font-medium text-slate-300 mb-1">🖥️ 対象ディスプレイ (スライド変化自動検知)</label>
        <select id="screenSelect" class="w-full bg-slate-950 border border-slate-700 rounded-lg px-2.5 py-1.5 text-xs text-slate-200 focus:outline-none focus:border-indigo-500">
          <option value="0">プライマリディスプレイ (既定)</option>
        </select>
      </div>
    </div>

    <!-- メイン操作パネル -->
    <div class="bg-slate-900/50 border border-slate-700/60 rounded-xl p-5 space-y-4">
      <div class="flex items-center justify-between">
        <div class="flex items-center space-x-3">
          <button id="recordBtn" class="px-5 py-2.5 rounded-xl font-bold text-sm bg-rose-600 hover:bg-rose-500 text-white shadow-lg shadow-rose-600/30 transition flex items-center space-x-2">
            <span id="recordBtnIcon" class="w-3 h-3 rounded-full bg-white animate-pulse"></span>
            <span id="recordBtnText">記録開始</span>
          </button>
          <button id="screenshotBtn" disabled class="px-4 py-2.5 rounded-xl font-medium text-xs bg-slate-700 text-slate-400 transition flex items-center space-x-1 cursor-not-allowed">
            <span>📸</span>
            <span>即座に撮影</span>
          </button>
        </div>
        <div class="text-right">
          <div id="timerText" class="text-xl font-mono font-bold text-slate-300">00:00:00</div>
          <div id="shotCountText" class="text-[11px] text-slate-400">撮影: 0 枚</div>
        </div>
      </div>

      <!-- リアルタイム音量インジケーター -->
      <div>
        <div class="flex justify-between text-[10px] text-slate-400 mb-1">
          <span>マイク・相手の声 入力レベル</span>
          <span id="audioLevelText">0%</span>
        </div>
        <div class="w-full bg-slate-950 rounded-full h-2 overflow-hidden border border-slate-800">
          <div id="audioLevelBar" class="bg-emerald-500 h-2 rounded-full transition-all duration-100" style="width: 0%"></div>
        </div>
      </div>

      <!-- 4重保護ガード・ヘルスモニター表示 -->
      <div id="healthGuardBar" class="hidden pt-1 border-t border-slate-800/80 flex flex-wrap items-center justify-between gap-2 text-[11px]">
        <div class="flex items-center space-x-2 text-emerald-400" id="healthStatusLabel">
          <span>🛡️</span>
          <span>自動復旧ガード稼働中 (スリープ抑止: ON / デバイス切断自動再接続)</span>
        </div>
        <div id="healthDeviceSummary" class="text-[10px] text-slate-400 font-mono truncate max-w-xs"></div>
      </div>
      <div id="healthWarningBanner" class="hidden bg-amber-950/70 border border-amber-500/50 rounded-lg px-3 py-2 text-xs text-amber-300 flex items-center space-x-2">
        <span>⚠️</span>
        <span id="healthWarningText">注意メッセージ</span>
      </div>
    </div>

    <!-- Box アップロード進捗表示エリア (停止後表示) -->
    <div id="uploadProgressContainer" class="hidden bg-slate-950/80 border border-blue-500/40 rounded-xl p-4 space-y-3">
      <div class="flex items-center justify-between">
        <div class="flex items-center space-x-2">
          <span class="animate-spin text-blue-400">🔄</span>
          <span id="uploadStatusText" class="text-xs font-semibold text-blue-300">Boxへアップロード準備中...</span>
        </div>
        <span id="uploadCountBadge" class="text-[11px] px-2 py-0.5 rounded bg-blue-900/50 text-blue-300 font-mono">0/0</span>
      </div>
      <div class="w-full bg-slate-900 rounded-full h-2.5 overflow-hidden border border-slate-700">
        <div id="uploadProgressBar" class="bg-blue-500 h-2.5 rounded-full transition-all duration-200" style="width: 0%"></div>
      </div>
      <div id="uploadSuccessMessage" class="hidden text-xs text-emerald-400 flex items-center justify-between pt-1">
        <span>✅ 全ファイルのBoxアップロードが完了しました！</span>
        <a id="boxFolderLink" href="#" target="_blank" class="text-indigo-400 hover:underline flex items-center space-x-1">
          <span>Boxで確認</span>
          <span>↗</span>
        </a>
      </div>
    </div>

    <!-- 設定アコーディオン: Box 連携設定 -->
    <div class="border border-slate-700 rounded-xl bg-slate-950/40 overflow-hidden">
      <button id="toggleBoxConfigBtn" class="w-full px-4 py-2.5 text-left text-xs font-semibold text-slate-300 flex items-center justify-between hover:bg-slate-800/50 transition">
        <span class="flex items-center space-x-2">
          <span>📦</span>
          <span>Box API 連携設定</span>
          <span id="boxConfigSummaryBadge" class="text-[10px] px-2 py-0.5 rounded bg-slate-800 text-slate-400">未認証</span>
        </span>
        <span id="boxConfigArrow" class="text-xs transition-transform transform duration-200">▼</span>
      </button>

      <div id="boxConfigSection" class="hidden p-4 border-t border-slate-800 space-y-3 text-xs">
        <div>
          <label class="block text-slate-400 mb-1">Box Client ID</label>
          <input type="text" id="boxClientId" placeholder="Box Developer Console の Client ID" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
        </div>
        <div>
          <label class="block text-slate-400 mb-1">Box Client Secret</label>
          <div class="relative">
            <input type="password" id="boxClientSecret" placeholder="Box Developer Console の Client Secret" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500 pr-16">
            <button type="button" id="toggleSecretVisibility" class="absolute right-2 top-1.5 text-[11px] text-slate-400 hover:text-slate-200">表示</button>
          </div>
        </div>
        <div>
          <label class="block text-slate-400 mb-1">共通フォルダID (themes.json の格納先)</label>
          <input type="text" id="boxSharedFolderId" placeholder="例: 433029580764 (URL末尾の数字)" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
        </div>
        <div>
          <label class="block text-slate-400 mb-1">Developer Token (開発テスト用・任意)</label>
          <div class="relative">
            <input type="password" id="boxDevToken" placeholder="一時テスト用トークン (OAuth未完了時の検証用)" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500 pr-16">
            <button type="button" id="toggleDevTokenVisibility" class="absolute right-2 top-1.5 text-[11px] text-slate-400 hover:text-slate-200">表示</button>
          </div>
        </div>
        <div class="flex items-center space-x-2 pt-1">
          <label class="flex items-center space-x-1.5 cursor-pointer">
            <input type="checkbox" id="boxAutoUpload" checked class="rounded bg-slate-900 border-slate-700 text-indigo-600 focus:ring-0">
            <span class="text-slate-300">停止時にBoxへ自動一括アップロード＆スプール消去</span>
          </label>
        </div>
        <div class="flex space-x-2 pt-2">
          <button id="startBoxLoginBtn" class="flex-1 py-1.5 rounded bg-blue-600 hover:bg-blue-500 text-white font-medium transition flex items-center justify-center space-x-1">
            <span>🌐</span>
            <span>ブラウザでBoxログイン</span>
          </button>
          <button id="saveBoxConfigBtn" class="flex-1 py-1.5 rounded bg-indigo-600 hover:bg-indigo-500 text-white font-medium transition">
            <span>💾 設定を保存</span>
          </button>
        </div>
        <div id="boxStatusResult" class="text-[11px] min-h-[1rem] break-all text-slate-400"></div>
      </div>
    </div>

    <!-- 未送信スプール一覧 (失敗時リカバリ) -->
    <div id="pendingSpoolArea" class="border border-slate-700/60 rounded-xl bg-slate-950/20 p-4 space-y-2">
      <div class="flex items-center justify-between">
        <span class="text-xs font-semibold text-slate-300 flex items-center space-x-1.5">
          <span>📁</span>
          <span>未送信スプール一覧</span>
        </span>
        <button id="refreshPendingBtn" class="text-[11px] text-slate-400 hover:text-slate-200">🔄 更新</button>
      </div>
      <div id="pendingList" class="space-y-1.5 max-h-40 overflow-y-auto text-xs text-slate-400">
        <div class="text-[11px] text-slate-500">未送信のスプールはありません。</div>
      </div>
    </div>

  </div>

  <script>
    // 主要要素
    const statusBadge = document.getElementById('statusBadge');
    const shutdownBtn = document.getElementById('shutdownBtn');
    const playbackSelect = document.getElementById('playbackSelect');
    const captureSelect = document.getElementById('captureSelect');
    const screenSelect = document.getElementById('screenSelect');
    const recordBtn = document.getElementById('recordBtn');
    const recordBtnIcon = document.getElementById('recordBtnIcon');
    const recordBtnText = document.getElementById('recordBtnText');
    const screenshotBtn = document.getElementById('screenshotBtn');
    const timerText = document.getElementById('timerText');
    const shotCountText = document.getElementById('shotCountText');
    const audioLevelBar = document.getElementById('audioLevelBar');
    const audioLevelText = document.getElementById('audioLevelText');

    // Box UI
    const boxAuthBadge = document.getElementById('boxAuthBadge');
    const boxLoginQuickBtn = document.getElementById('boxLoginQuickBtn');
    const themeSelect = document.getElementById('themeSelect');
    const refreshThemesBtn = document.getElementById('refreshThemesBtn');
    const toggleBoxConfigBtn = document.getElementById('toggleBoxConfigBtn');
    const boxConfigSection = document.getElementById('boxConfigSection');
    const boxConfigArrow = document.getElementById('boxConfigArrow');
    const boxConfigSummaryBadge = document.getElementById('boxConfigSummaryBadge');
    const boxClientId = document.getElementById('boxClientId');
    const boxClientSecret = document.getElementById('boxClientSecret');
    const boxSharedFolderId = document.getElementById('boxSharedFolderId');
    const boxDevToken = document.getElementById('boxDevToken');
    const boxAutoUpload = document.getElementById('boxAutoUpload');
    const startBoxLoginBtn = document.getElementById('startBoxLoginBtn');
    const saveBoxConfigBtn = document.getElementById('saveBoxConfigBtn');
    const boxStatusResult = document.getElementById('boxStatusResult');
    const toggleSecretVisibility = document.getElementById('toggleSecretVisibility');
    const toggleDevTokenVisibility = document.getElementById('toggleDevTokenVisibility');

    // アップロード進捗 UI
    const uploadProgressContainer = document.getElementById('uploadProgressContainer');
    const uploadStatusText = document.getElementById('uploadStatusText');
    const uploadCountBadge = document.getElementById('uploadCountBadge');
    const uploadProgressBar = document.getElementById('uploadProgressBar');
    const uploadSuccessMessage = document.getElementById('uploadSuccessMessage');
    const boxFolderLink = document.getElementById('boxFolderLink');

    // 未送信スプール
    const pendingList = document.getElementById('pendingList');
    const refreshPendingBtn = document.getElementById('refreshPendingBtn');

    let isRecording = false;
    let timerInterval = null;
    let startTime = null;

    // アコーディオン開閉
    toggleBoxConfigBtn.addEventListener('click', () => {
      const isHidden = boxConfigSection.classList.contains('hidden');
      if (isHidden) {
        boxConfigSection.classList.remove('hidden');
        boxConfigArrow.style.transform = 'rotate(180deg)';
      } else {
        boxConfigSection.classList.add('hidden');
        boxConfigArrow.style.transform = 'rotate(0deg)';
      }
    });

    toggleSecretVisibility.addEventListener('click', () => {
      boxClientSecret.type = boxClientSecret.type === 'password' ? 'text' : 'password';
      toggleSecretVisibility.textContent = boxClientSecret.type === 'password' ? '表示' : '伏せる';
    });

    toggleDevTokenVisibility.addEventListener('click', () => {
      boxDevToken.type = boxDevToken.type === 'password' ? 'text' : 'password';
      toggleDevTokenVisibility.textContent = boxDevToken.type === 'password' ? '表示' : '伏せる';
    });

    // 初期化ロード
    async function init() {
      await loadDevices();
      await loadScreens();
      await loadConfig();
      await loadBoxStatus();
      await loadPendingSessions();
      startPollingStatus();
    }

    async function loadDevices() {
      try {
        const res = await fetch('/api/devices');
        const data = await res.json();
        
        playbackSelect.innerHTML = '<option value="">既定の再生デバイス</option>';
        data.playback.forEach(d => {
          const opt = document.createElement('option');
          opt.value = d.id;
          opt.textContent = d.name;
          playbackSelect.appendChild(opt);
        });

        captureSelect.innerHTML = '<option value="">既定のマイクデバイス</option>';
        data.capture.forEach(d => {
          const opt = document.createElement('option');
          opt.value = d.id;
          opt.textContent = d.name;
          captureSelect.appendChild(opt);
        });
      } catch (err) {
        console.error('デバイス取得エラー:', err);
      }
    }

    async function loadScreens() {
      try {
        const res = await fetch('/api/screens');
        const data = await res.json();
        screenSelect.innerHTML = '';
        data.screens.forEach(s => {
          const opt = document.createElement('option');
          opt.value = s.index;
          opt.textContent = `[${s.index}] ${s.name} (${s.bounds.width}x${s.bounds.height}) ${s.isPrimary ? '★' : ''}`;
          screenSelect.appendChild(opt);
        });
        if (data.virtualDesktop) {
          const vOpt = document.createElement('option');
          vOpt.value = "-1";
          vOpt.textContent = `[-1] ${data.virtualDesktop.name} (${data.virtualDesktop.bounds.width}x${data.virtualDesktop.bounds.height})`;
          screenSelect.appendChild(vOpt);
        }
      } catch (err) {
        console.error('画面一覧取得エラー:', err);
      }
    }

    async function loadConfig() {
      try {
        const res = await fetch('/api/config');
        const cfg = await res.json();
        if (cfg && cfg.box) {
          boxClientId.value = cfg.box.clientId || '';
          boxClientSecret.value = cfg.box.clientSecret || '';
          boxSharedFolderId.value = cfg.box.sharedFolderId || '433029580764';
          boxDevToken.value = cfg.box.developerToken || '';
          boxAutoUpload.checked = cfg.box.autoUploadOnStop !== false;
        }
      } catch (err) {
        console.error('設定取得エラー:', err);
      }
    }

    async function loadBoxStatus() {
      try {
        const res = await fetch('/api/box/status');
        const status = await res.json();

        if (status.isAuthenticated) {
          const name = status.userName ? `${status.userName} 様` : '認証済み';
          boxAuthBadge.textContent = `🟢 ${name}`;
          boxAuthBadge.className = 'px-2 py-0.5 rounded text-[11px] font-medium bg-emerald-950 text-emerald-400 border border-emerald-800';
          boxConfigSummaryBadge.textContent = '認証済み';
          boxConfigSummaryBadge.className = 'text-[10px] px-2 py-0.5 rounded bg-emerald-900/60 text-emerald-300';
          boxLoginQuickBtn.classList.add('hidden');
        } else {
          boxAuthBadge.textContent = '⚪ 未認証';
          boxAuthBadge.className = 'px-2 py-0.5 rounded text-[11px] font-medium bg-rose-950 text-rose-300 border border-rose-800';
          boxConfigSummaryBadge.textContent = '未認証';
          boxConfigSummaryBadge.className = 'text-[10px] px-2 py-0.5 rounded bg-rose-900/60 text-rose-300';
          boxLoginQuickBtn.classList.remove('hidden');
        }

        await loadThemes();
      } catch (err) {
        console.error('Boxステータス取得エラー:', err);
      }
    }

    async function loadThemes(forceRefresh = false) {
      try {
        const url = forceRefresh ? '/api/box/themes/refresh' : '/api/box/themes';
        const method = forceRefresh ? 'POST' : 'GET';
        const res = await fetch(url, { method });
        const data = await res.json();
        const themes = Array.isArray(data) ? data : (data.themes || []);

        themeSelect.innerHTML = '';
        if (themes.length === 0) {
          themeSelect.innerHTML = '<option value="">(テーマがありません)</option>';
          return;
        }

        const statusRes = await fetch('/api/box/status');
        const status = await statusRes.json();
        const activeId = status.activeThemeId;

        themes.forEach(t => {
          const opt = document.createElement('option');
          opt.value = t.id;
          opt.textContent = `${t.name} (フォルダ: ${t.box_folder_id})`;
          if (t.id === activeId) opt.selected = true;
          themeSelect.appendChild(opt);
        });
      } catch (err) {
        console.error('テーマ取得エラー:', err);
      }
    }

    // テーマ選択変更
    themeSelect.addEventListener('change', async () => {
      const selectedId = themeSelect.value;
      if (!selectedId) return;
      try {
        await fetch('/api/box/theme/select', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ themeId: selectedId })
        });
      } catch (err) {
        console.error('テーマ選択エラー:', err);
      }
    });

    refreshThemesBtn.addEventListener('click', async () => {
      refreshThemesBtn.disabled = true;
      refreshThemesBtn.innerHTML = '<span>🔄</span><span>取得中...</span>';
      await loadThemes(true);
      refreshThemesBtn.disabled = false;
      refreshThemesBtn.innerHTML = '<span>🔄</span><span>テーマ更新</span>';
    });

    // Box ログインボタン (Quick & Settings)
    async function triggerBoxLogin() {
      boxStatusResult.textContent = 'ブラウザでBox認可画面を起動しています...';
      boxStatusResult.className = 'text-[11px] text-blue-400';
      try {
        // 設定を事前に保存
        await saveBoxConfig();
        const res = await fetch('/api/box/login', { method: 'POST' });
        const data = await res.json();
        if (data.success) {
          boxStatusResult.textContent = '✅ Box認証に成功しました！';
          boxStatusResult.className = 'text-[11px] text-emerald-400';
          await loadBoxStatus();
        } else {
          boxStatusResult.textContent = `❌ ${data.message || '認証エラー'}`;
          boxStatusResult.className = 'text-[11px] text-rose-400';
        }
      } catch (err) {
        boxStatusResult.textContent = `❌ 通信エラー: ${err.message}`;
        boxStatusResult.className = 'text-[11px] text-rose-400';
      }
    }

    boxLoginQuickBtn.addEventListener('click', triggerBoxLogin);
    startBoxLoginBtn.addEventListener('click', triggerBoxLogin);

    // 設定保存
    async function saveBoxConfig() {
      const cfgRes = await fetch('/api/config');
      const cfg = await cfgRes.json();
      cfg.box = cfg.box || {};
      cfg.box.clientId = boxClientId.value.trim();
      cfg.box.clientSecret = boxClientSecret.value.trim();
      cfg.box.sharedFolderId = boxSharedFolderId.value.trim() || '433029580764';
      cfg.box.developerToken = boxDevToken.value.trim();
      cfg.box.autoUploadOnStop = boxAutoUpload.checked;

      const res = await fetch('/api/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(cfg)
      });
      return await res.json();
    }

    saveBoxConfigBtn.addEventListener('click', async () => {
      try {
        await saveBoxConfig();
        boxStatusResult.textContent = '💾 設定を保存しました。';
        boxStatusResult.className = 'text-[11px] text-emerald-400';
        await loadBoxStatus();
      } catch (err) {
        boxStatusResult.textContent = `❌ 保存エラー: ${err.message}`;
        boxStatusResult.className = 'text-[11px] text-rose-400';
      }
    });

    // 記録開始 / 停止
    recordBtn.addEventListener('click', async () => {
      if (!isRecording) {
        await startRecording();
      } else {
        await stopRecording();
      }
    });

    async function startRecording() {
      recordBtn.disabled = true;
      try {
        const body = {
          playbackDeviceId: playbackSelect.value || null,
          captureDeviceId: captureSelect.value || null,
          monitorIndex: parseInt(screenSelect.value, 10),
          enableScreenCapture: true,
          themeId: themeSelect.value || null
        };

        const res = await fetch('/api/record/start', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body)
        });

        const data = await res.json();
        if (res.ok) {
          isRecording = true;
          updateRecordingUI(true);
          uploadProgressContainer.classList.add('hidden');
        } else {
          alert(`開始エラー: ${data.error || '不明なエラー'}`);
        }
      } catch (err) {
        alert(`開始通信エラー: ${err.message}`);
      } finally {
        recordBtn.disabled = false;
      }
    }

    async function stopRecording() {
      recordBtn.disabled = true;
      recordBtnText.textContent = '停止中...';
      try {
        uploadProgressContainer.classList.remove('hidden');
        uploadStatusText.textContent = 'WAV音声をMP3へ高圧縮中...';
        uploadCountBadge.textContent = '処理中';
        uploadProgressBar.style.width = '15%';
        uploadSuccessMessage.classList.add('hidden');

        const res = await fetch('/api/record/stop', { method: 'POST' });
        const data = await res.json();

        isRecording = false;
        updateRecordingUI(false);

        if (res.ok) {
          if (data.boxUpload && data.boxUpload.success) {
            uploadProgressBar.style.width = '100%';
            uploadStatusText.textContent = '✅ Boxへの一括アップロード完了！';
            uploadSuccessMessage.classList.remove('hidden');
            if (data.boxUpload.folderUrl) {
              boxFolderLink.href = data.boxUpload.folderUrl;
            }
          } else if (data.boxUpload && !data.boxUpload.success) {
            uploadStatusText.textContent = `⚠️ Boxアップロードエラー: ${data.boxUpload.message}`;
            uploadProgressBar.className = 'bg-rose-500 h-2.5 rounded-full';
          }
        } else {
          uploadStatusText.textContent = `❌ 停止処理エラー: ${data.error}`;
        }
      } catch (err) {
        uploadStatusText.textContent = `❌ 通信エラー: ${err.message}`;
      } finally {
        recordBtn.disabled = false;
        await loadPendingSessions();
      }
    }

    screenshotBtn.addEventListener('click', async () => {
      try {
        const res = await fetch('/api/record/screenshot', { method: 'POST' });
        if (res.ok) {
          const current = parseInt(shotCountText.textContent.replace(/[^0-9]/g, '') || '0', 10);
          shotCountText.textContent = `撮影: ${current + 1} 枚`;
        }
      } catch (err) {
        console.error('スクショ撮影エラー:', err);
      }
    });

    function updateRecordingUI(recording) {
      if (recording) {
        statusBadge.textContent = '🔴 記録中';
        statusBadge.className = 'px-3 py-1 rounded-full text-xs font-semibold bg-rose-500/20 text-rose-400 border border-rose-500/30 animate-pulse';
        recordBtn.className = 'px-5 py-2.5 rounded-xl font-bold text-sm bg-slate-700 hover:bg-slate-600 text-white shadow-lg transition flex items-center space-x-2';
        recordBtnIcon.className = 'w-3 h-3 rounded bg-white';
        recordBtnText.textContent = '記録停止 (Box保存)';
        screenshotBtn.disabled = false;
        screenshotBtn.className = 'px-4 py-2.5 rounded-xl font-medium text-xs bg-indigo-600 hover:bg-indigo-500 text-white transition flex items-center space-x-1';

        startTime = new Date();
        timerInterval = setInterval(() => {
          const diff = Math.floor((new Date() - startTime) / 1000);
          const h = String(Math.floor(diff / 3600)).padStart(2, '0');
          const m = String(Math.floor((diff % 3600) / 60)).padStart(2, '0');
          const s = String(diff % 60).padStart(2, '0');
          timerText.textContent = `${h}:${m}:${s}`;
        }, 1000);
      } else {
        statusBadge.textContent = '待機中';
        statusBadge.className = 'px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30';
        recordBtn.className = 'px-5 py-2.5 rounded-xl font-bold text-sm bg-rose-600 hover:bg-rose-500 text-white shadow-lg shadow-rose-600/30 transition flex items-center space-x-2';
        recordBtnIcon.className = 'w-3 h-3 rounded-full bg-white animate-pulse';
        recordBtnText.textContent = '記録開始';
        screenshotBtn.disabled = true;
        screenshotBtn.className = 'px-4 py-2.5 rounded-xl font-medium text-xs bg-slate-700 text-slate-400 transition flex items-center space-x-1 cursor-not-allowed';

        if (timerInterval) clearInterval(timerInterval);
        timerInterval = null;
        timerText.textContent = '00:00:00';
        document.getElementById('healthGuardBar')?.classList.add('hidden');
        document.getElementById('healthWarningBanner')?.classList.add('hidden');
      }
    }

    // ポーリング監視 (録音状態・音量レベル・トレイ同期・4重保護ガード状態)
    function startPollingStatus() {
      const healthGuardBar = document.getElementById('healthGuardBar');
      const healthDeviceSummary = document.getElementById('healthDeviceSummary');
      const healthWarningBanner = document.getElementById('healthWarningBanner');
      const healthWarningText = document.getElementById('healthWarningText');

      setInterval(async () => {
        try {
          const res = await fetch('/api/status');
          if (!res.ok) return;
          const data = await res.json();

          // 音量バー更新
          const lvl = Math.min(100, Math.round((data.audioLevel || 0) * 100));
          audioLevelBar.style.width = `${lvl}%`;
          audioLevelText.textContent = `${lvl}%`;

          // トレイ等からの外部トリガー検知同期
          if (data.isRecording && !isRecording) {
            isRecording = true;
            updateRecordingUI(true);
          } else if (!data.isRecording && isRecording) {
            isRecording = false;
            updateRecordingUI(false);
          }

          if (data.isRecording && data.capturedCount !== undefined) {
            shotCountText.textContent = `撮影: ${data.capturedCount} 枚`;
          }

          // ヘルスステータス表示更新
          if (data.isRecording && data.health) {
            healthGuardBar?.classList.remove('hidden');
            const recon = data.health.audioReconnectCount > 0 ? ` (再接続:${data.health.audioReconnectCount}回)` : '';
            healthDeviceSummary.textContent = `🔊 ${data.health.activePlaybackDevice || '既定'} / 🎤 ${data.health.activeMicDevice || '既定'}${recon}`;

            if (data.health.overallStatus === 'warning' && data.health.warningMessage) {
              healthWarningBanner?.classList.remove('hidden');
              healthWarningText.textContent = data.health.warningMessage;
            } else {
              healthWarningBanner?.classList.add('hidden');
            }
          } else {
            healthGuardBar?.classList.add('hidden');
            healthWarningBanner?.classList.add('hidden');
          }
        } catch { /* ignore */ }
      }, 500);
    }

    // 未送信スプール
    async function loadPendingSessions() {
      try {
        const res = await fetch('/api/sessions/pending');
        const list = await res.json();
        pendingList.innerHTML = '';
        if (!list || list.length === 0) {
          pendingList.innerHTML = '<div class="text-[11px] text-slate-500">未送信のスプールはありません。</div>';
          return;
        }

        list.forEach(item => {
          const row = document.createElement('div');
          row.className = 'flex items-center justify-between p-2 rounded bg-slate-900 border border-slate-800';
          row.innerHTML = `
            <div>
              <div class="font-mono text-slate-200 text-xs">${item.sessionId}</div>
              <div class="text-[10px] text-slate-400">スクショ ${item.screenshotCount}枚 / ${(item.totalSizeBytes / 1024 / 1024).toFixed(1)}MB</div>
            </div>
            <div class="flex items-center space-x-1.5">
              <button onclick="manualUploadBox('${item.sessionId}')" class="px-2 py-1 rounded bg-blue-600 hover:bg-blue-500 text-white text-[10px] font-semibold">Box送信</button>
              <button onclick="deleteSession('${item.sessionId}')" class="px-2 py-1 rounded bg-slate-800 hover:bg-rose-900/60 text-slate-400 hover:text-rose-300 text-[10px]">削除</button>
            </div>
          `;
          pendingList.appendChild(row);
        });
      } catch (err) {
        console.error('未送信スプール取得エラー:', err);
      }
    }

    window.manualUploadBox = async function(sessionId) {
      if (!confirm(`セッション ${sessionId} を Box へアップロードしますか？`)) return;
      uploadProgressContainer.classList.remove('hidden');
      uploadStatusText.textContent = `Boxへアップロード中: ${sessionId}`;
      try {
        const res = await fetch('/api/box/upload', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sessionId, themeId: themeSelect.value || null })
        });
        const data = await res.json();
        if (data.success) {
          alert('✅ Box へのアップロードが完了しました！');
          await loadPendingSessions();
        } else {
          alert(`❌ アップロード失敗: ${data.message}`);
        }
      } catch (err) {
        alert(`❌ 通信エラー: ${err.message}`);
      }
    };

    window.deleteSession = async function(sessionId) {
      if (!confirm(`セッション ${sessionId} を削除しますか？`)) return;
      try {
        await fetch(`/api/sessions/${sessionId}`, { method: 'DELETE' });
        await loadPendingSessions();
      } catch (err) {
        alert(`削除エラー: ${err.message}`);
      }
    };

    refreshPendingBtn.addEventListener('click', loadPendingSessions);

    // アプリ終了ボタン
    shutdownBtn.addEventListener('click', async () => {
      if (!confirm('WinMeetingRecorder を終了しますか？')) return;
      try {
        await fetch('/api/app/shutdown', { method: 'POST' });
        document.body.innerHTML = '<div class="text-center py-20 text-slate-400">アプリは終了しました。タブを閉じてください。</div>';
      } catch {
        alert('終了リクエストを送信しました。');
      }
    });

    init();
  </script>
</body>
</html>
""";
}
