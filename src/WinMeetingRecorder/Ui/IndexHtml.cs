namespace WinMeetingRecorder.Ui;

public static class IndexHtml
{
    public const string Content = """
<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>WinMeetingRecorder - 会議記録クライアント</title>
  <script src="https://cdn.tailwindcss.com"></script>
</head>
<body class="bg-slate-900 text-slate-100 min-h-screen flex flex-col items-center justify-start py-6 px-4 font-sans select-none">
  <div class="w-full max-w-2xl bg-slate-800 rounded-2xl shadow-2xl border border-slate-700 p-5 md:p-6 space-y-4">
    
    <!-- 1. ヘッダー -->
    <div class="flex items-center justify-between pb-2 border-b border-slate-700/80">
      <div class="flex items-center space-x-2.5">
        <span class="text-2xl">🎙️</span>
        <div>
          <h1 class="text-base font-bold tracking-tight text-white flex items-center space-x-2">
            <span>WinMeetingRecorder</span>
            <span id="boxAuthBadge" class="text-[10px] font-normal px-2 py-0.5 rounded bg-slate-800 text-slate-400">確認中...</span>
          </h1>
        </div>
      </div>
      <div class="flex items-center space-x-2">
        <button id="boxLoginQuickBtn" class="hidden px-2.5 py-1 rounded text-xs font-semibold bg-blue-600 hover:bg-blue-500 text-white transition flex items-center space-x-1">
          <span>🌐</span>
          <span>Boxログイン</span>
        </button>
        <button id="openSettingsBtn" title="詳細設定を開く" class="p-1.5 rounded-lg bg-slate-700/60 hover:bg-slate-700 text-slate-300 hover:text-white transition flex items-center">
          <span class="text-sm">⚙️</span>
        </button>
        <button id="shutdownBtn" title="アプリを終了" class="p-1.5 rounded-lg bg-slate-700/60 hover:bg-rose-900/60 text-slate-400 hover:text-rose-200 transition flex items-center">
          <span class="text-sm">✕</span>
        </button>
      </div>
    </div>

    <!-- 2. 【現在の会議】カード (待機中 / 録音中) -->
    <div id="currentSessionCard" class="bg-slate-900/70 border border-slate-700/80 rounded-xl p-4 space-y-3 shadow-inner">
      
      <!-- 行1: ステータス・タイマー ＆ テーマ2連スイッチ ＆ 記録開始/停止ボタン -->
      <div class="flex items-center justify-between gap-2 flex-wrap">
        
        <!-- 左: ステータス ＆ タイマー -->
        <div class="flex items-center space-x-2">
          <div id="statusBadge" class="px-2.5 py-1 rounded-full text-xs font-bold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 flex items-center space-x-1.5">
            <span id="recordBtnIcon" class="w-2 h-2 rounded-full bg-emerald-400"></span>
            <span id="timerText">待機中</span>
          </div>
        </div>

        <!-- 中央: 保存先テーマ 2択セグメントスイッチ (インライン) -->
        <div class="flex items-center space-x-1">
          <div id="themeSegmentContainer" class="inline-flex rounded-lg bg-slate-950 p-0.5 border border-slate-700/80">
            <!-- JSで動的生成: 例 [ 👥 社員限定 ] [ 🌐 全体共有 ] -->
            <button class="px-2.5 py-1 rounded-md text-xs font-medium text-slate-400">テーマ読込中...</button>
          </div>
          <select id="themeSelect" class="hidden"></select>
        </div>

        <!-- 右: 記録アクションボタン (開始/停止 ＆ 手動スクショ) -->
        <div class="flex items-center space-x-1.5">
          <button id="screenshotBtn" disabled title="即座に画面キャプチャ" class="hidden px-2.5 py-1.5 rounded-lg font-medium text-xs bg-slate-800 text-slate-500 transition cursor-not-allowed">
            <span>📸</span>
          </button>
          <button id="recordBtn" class="px-4 py-1.5 rounded-lg font-bold text-xs bg-rose-600 hover:bg-rose-500 text-white shadow-md shadow-rose-600/30 transition flex items-center space-x-1.5">
            <span class="w-2 h-2 rounded-full bg-white"></span>
            <span id="recordBtnText">記録開始</span>
          </button>
        </div>
      </div>

      <!-- 行2: 会議メモ (1行・任意) -->
      <div class="relative">
        <div class="flex items-center space-x-2 bg-slate-950 border border-slate-700/80 rounded-lg px-2.5 py-1.5 focus-within:border-indigo-500 transition">
          <span class="text-xs text-slate-400">📝</span>
          <input 
            type="text" 
            id="meetingMemoInput" 
            placeholder="会議メモ / 件名 (例: ○○定例、△△さん1on1 等・空欄でもOK)" 
            class="flex-1 bg-transparent text-xs text-slate-200 placeholder-slate-500 focus:outline-none"
          />
          <span id="memoSaveStatus" class="text-[10px] text-emerald-400 opacity-0 transition-opacity">保存済 ✓</span>
        </div>
      </div>

      <!-- 行3: Zoom風インラインデバイスピル ＆ 音量メーター -->
      <div class="flex items-center justify-between text-xs pt-1 border-t border-slate-800/80 gap-2 flex-wrap">
        
        <div class="flex items-center space-x-2 flex-wrap gap-y-1">
          <!-- マイク ピル (音量メーター付き) -->
          <div class="relative inline-flex items-center bg-slate-950/80 hover:bg-slate-800/80 border border-slate-700/70 rounded-lg px-2 py-1 transition group">
            <span class="text-xs mr-1.5">🎤</span>
            <!-- リアルタイム音量ミニインジケーター (4本バー) -->
            <div class="flex items-end space-x-0.5 h-3 mr-1.5" title="マイク入力レベル">
              <span id="vBar1" class="w-0.5 h-1 bg-slate-700 rounded-full transition-all duration-75"></span>
              <span id="vBar2" class="w-0.5 h-2 bg-slate-700 rounded-full transition-all duration-75"></span>
              <span id="vBar3" class="w-0.5 h-2.5 bg-slate-700 rounded-full transition-all duration-75"></span>
              <span id="vBar4" class="w-0.5 h-3 bg-slate-700 rounded-full transition-all duration-75"></span>
            </div>
            <select id="captureSelect" class="bg-transparent text-[11px] text-slate-300 font-medium focus:outline-none cursor-pointer pr-4 appearance-none">
              <option value="">既定のマイク</option>
            </select>
            <span class="absolute right-1.5 text-[9px] text-slate-500 pointer-events-none">▾</span>
          </div>

          <!-- スピーカー ピル -->
          <div class="relative inline-flex items-center bg-slate-950/80 hover:bg-slate-800/80 border border-slate-700/70 rounded-lg px-2 py-1 transition group">
            <span class="text-xs mr-1">🔊</span>
            <select id="playbackSelect" class="bg-transparent text-[11px] text-slate-300 font-medium focus:outline-none cursor-pointer pr-4 appearance-none">
              <option value="">既定の再生デバイス</option>
            </select>
            <span class="absolute right-1.5 text-[9px] text-slate-500 pointer-events-none">▾</span>
          </div>

          <!-- 画面 ピル -->
          <div class="relative inline-flex items-center bg-slate-950/80 hover:bg-slate-800/80 border border-slate-700/70 rounded-lg px-2 py-1 transition group">
            <span class="text-xs mr-1">🖥️</span>
            <select id="screenSelect" class="bg-transparent text-[11px] text-slate-300 font-medium focus:outline-none cursor-pointer pr-4 appearance-none">
              <option value="0">プライマリディスプレイ</option>
            </select>
            <span class="absolute right-1.5 text-[9px] text-slate-500 pointer-events-none">▾</span>
          </div>
        </div>

        <!-- 右端: 撮影枚数 (録音中) -->
        <div class="flex items-center space-x-2 text-[11px] text-slate-400">
          <span id="shotCountText" class="font-mono">撮影: 0 枚</span>
        </div>
      </div>

      <!-- 隠し互換用 (既存スクリプト・ヘルスモニター用) -->
      <div class="hidden">
        <div id="audioLevelBar"></div>
        <div id="audioLevelText">0%</div>
        <button id="refreshThemesBtn"></button>
        <div id="healthGuardBar"></div>
        <div id="healthStatusLabel"></div>
        <div id="healthDeviceSummary"></div>
        <div id="healthWarningBanner"></div>
        <div id="healthWarningText"></div>
      </div>
    </div>

    <!-- 3. アップロード進捗表示エリア (停止後インライン表示) -->
    <div id="uploadProgressContainer" class="hidden bg-slate-950/90 border border-blue-500/40 rounded-xl p-3.5 space-y-2">
      <div class="flex items-center justify-between text-xs">
        <div class="flex items-center space-x-2">
          <span class="animate-spin text-blue-400">🔄</span>
          <span id="uploadStatusText" class="font-semibold text-blue-300">Boxへアップロード準備中...</span>
        </div>
        <span id="uploadCountBadge" class="text-[10px] px-2 py-0.5 rounded bg-blue-900/50 text-blue-300 font-mono">0%</span>
      </div>
      <div class="w-full bg-slate-900 rounded-full h-2 overflow-hidden border border-slate-700">
        <div id="uploadProgressBar" class="bg-blue-500 h-2 rounded-full transition-all duration-200" style="width: 0%"></div>
      </div>
      <div id="uploadSuccessMessage" class="hidden text-xs text-emerald-400 flex items-center justify-between pt-0.5">
        <span>✅ BoxへのZIPアップロードが完了しました！</span>
        <a id="boxFolderLink" href="#" target="_blank" class="text-indigo-400 hover:underline flex items-center space-x-1 font-semibold">
          <span>Boxで確認</span>
          <span>↗</span>
        </a>
      </div>
    </div>

    <!-- 4. 未送信スプール一覧 (0件時は完全非表示) -->
    <div id="pendingSpoolArea" class="hidden border border-amber-500/40 rounded-xl bg-amber-950/20 p-3 space-y-2.5">
      <div class="flex items-center justify-between text-xs">
        <span class="font-semibold text-amber-300 flex items-center space-x-1.5">
          <span>📁</span>
          <span id="pendingTitle">未送信スプール</span>
        </span>
        <button id="refreshPendingBtn" class="text-[11px] text-slate-400 hover:text-slate-200 flex items-center space-x-1">
          <span>🔄</span>
          <span>更新</span>
        </button>
      </div>
      <div id="pendingList" class="space-y-2 max-h-56 overflow-y-auto"></div>
    </div>

    <!-- 5. 動作ログステータスバー ＆ ワンクリック展開コンソール (案1) -->
    <div class="border border-slate-700/70 rounded-xl bg-slate-950/40 overflow-hidden text-xs">
      <!-- 1行ステータスバー (折りたたみ時) -->
      <div id="logStatusBar" class="px-3 py-2 flex items-center justify-between hover:bg-slate-800/40 transition cursor-pointer select-none">
        <div class="flex items-center space-x-2 truncate mr-2 flex-1">
          <span class="text-slate-400">📜</span>
          <span id="latestLogSummary" class="text-[11px] text-slate-400 truncate">ログ取得中...</span>
        </div>
        <button id="toggleLogBtn" class="text-[11px] text-indigo-400 hover:text-indigo-300 flex items-center space-x-1 font-medium shrink-0">
          <span id="toggleLogText">展開</span>
          <span id="toggleLogArrow">▼</span>
        </button>
      </div>

      <!-- 展開コンソール本体 (デフォルト非表示) -->
      <div id="logExpandedArea" class="hidden border-t border-slate-800 p-3 space-y-2 bg-slate-950/90">
        <div class="flex items-center justify-between text-[11px] text-slate-400 pb-1 border-b border-slate-800/80">
          <div class="flex items-center space-x-2">
            <span class="font-semibold text-slate-300">動作ログ ＆ Box格納履歴</span>
          </div>
          <div class="flex items-center space-x-3">
            <label class="flex items-center space-x-1 cursor-pointer select-none">
              <input type="checkbox" id="autoScrollLogs" checked class="rounded bg-slate-900 border-slate-700 text-indigo-500 focus:ring-0">
              <span>追従</span>
            </label>
            <button id="refreshLogsBtn" class="hover:text-slate-200">🔄 更新</button>
            <button id="clearLogsBtn" class="hover:text-rose-300">🗑️ クリア</button>
          </div>
        </div>
        <div id="logConsole" class="w-full h-44 font-mono text-[11px] leading-relaxed overflow-y-auto space-y-1 text-slate-300 select-text p-1">
          <div class="text-slate-500 text-[10px]">ログ取得中...</div>
        </div>
      </div>
    </div>

    <!-- 6. ⚙️ 設定モーダル (普段は非表示) -->
    <div id="settingsModal" class="hidden fixed inset-0 z-50 bg-black/70 backdrop-blur-sm flex items-center justify-center p-4">
      <div class="w-full max-w-xl bg-slate-900 border border-slate-700 rounded-2xl shadow-2xl p-5 space-y-4 max-h-[90vh] overflow-y-auto">
        
        <div class="flex items-center justify-between pb-3 border-b border-slate-800">
          <h2 class="text-sm font-bold text-white flex items-center space-x-2">
            <span>⚙️</span>
            <span>詳細設定 (Box連携 ＆ 画面キャプチャ)</span>
          </h2>
          <button id="closeSettingsBtn" class="text-slate-400 hover:text-white text-lg p-1">✕</button>
        </div>

        <!-- Box 連携設定 -->
        <div class="border border-slate-800 rounded-xl bg-slate-950/50 p-3.5 space-y-2.5 text-xs">
          <h3 class="font-semibold text-slate-300 flex items-center space-x-1.5">
            <span>📦</span>
            <span>Box API 連携設定</span>
          </h3>
          <div class="space-y-2">
            <div>
              <label class="block text-slate-400 text-[11px] mb-0.5">Box Client ID</label>
              <input type="text" id="boxClientId" placeholder="Client ID" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-slate-200">
            </div>
            <div>
              <label class="block text-slate-400 text-[11px] mb-0.5">Box Client Secret</label>
              <div class="relative">
                <input type="password" id="boxClientSecret" placeholder="Client Secret" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-slate-200 pr-14">
                <button type="button" id="toggleSecretVisibility" class="absolute right-2 top-1 text-[10px] text-slate-400 hover:text-slate-200">表示</button>
              </div>
            </div>
            <div>
              <label class="block text-slate-400 text-[11px] mb-0.5">企業・独自ドメイン (任意・SSO認可画面用)</label>
              <input type="text" id="boxDomain" placeholder="例: mycompany" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-slate-200">
            </div>
            <div>
              <label class="block text-slate-400 text-[11px] mb-0.5">既定フォールバックBoxフォルダID (任意)</label>
              <input type="text" id="boxSharedFolderId" placeholder="テーマ設定外の予備ID" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-slate-200">
              <p class="text-[10px] text-slate-500 mt-0.5">※ テーマ（社員限定・全体共有等）のフォルダIDは exe同フォルダの config.json で設定します。</p>
            </div>
            <div>
              <label class="block text-slate-400 text-[11px] mb-0.5">Developer Token (一時テスト用・任意)</label>
              <div class="relative">
                <input type="password" id="boxDevToken" placeholder="一時テスト用トークン" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-slate-200 pr-14">
                <button type="button" id="toggleDevTokenVisibility" class="absolute right-2 top-1 text-[10px] text-slate-400 hover:text-slate-200">表示</button>
              </div>
            </div>
            <div class="flex items-center space-x-2 pt-1">
              <label class="flex items-center space-x-1.5 cursor-pointer">
                <input type="checkbox" id="boxAutoUpload" checked class="rounded bg-slate-900 border-slate-700 text-indigo-600 focus:ring-0">
                <span class="text-slate-300 text-[11px]">停止時にBoxへ自動ZIP送信＆スプール消去</span>
              </label>
            </div>
            <div class="flex items-center space-x-2">
              <label class="flex items-center space-x-1.5 cursor-pointer">
                <input type="checkbox" id="boxBypassProxy" class="rounded bg-slate-900 border-slate-700 text-indigo-600 focus:ring-0">
                <span class="text-slate-400 text-[11px]">プロキシをバイパスして直接接続</span>
              </label>
            </div>
            <div class="flex space-x-2 pt-1.5">
              <button id="startBoxLoginBtn" class="flex-1 py-1.5 rounded bg-blue-600 hover:bg-blue-500 text-white font-medium transition text-xs">
                🌐 ブラウザでBoxログイン
              </button>
              <button id="saveBoxConfigBtn" class="flex-1 py-1.5 rounded bg-indigo-600 hover:bg-indigo-500 text-white font-medium transition text-xs">
                💾 設定保存
              </button>
            </div>
            <div id="boxStatusResult" class="text-[10px] min-h-[1rem] break-all text-slate-400"></div>
          </div>
        </div>

        <!-- 画面キャプチャ設定 -->
        <div class="border border-slate-800 rounded-xl bg-slate-950/50 p-3.5 space-y-2.5 text-xs">
          <h3 class="font-semibold text-slate-300 flex items-center space-x-1.5">
            <span>📸</span>
            <span>画面キャプチャ＆差分検知設定</span>
          </h3>
          <div class="grid grid-cols-2 gap-2 text-[11px]">
            <div>
              <label class="block text-slate-400 mb-0.5">監視周期 (秒)</label>
              <input type="number" id="screenCheckIntervalSec" min="1" max="60" value="5" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-slate-200">
            </div>
            <div>
              <label class="block text-slate-400 mb-0.5">定期強制保存 (秒, 0で無効)</label>
              <input type="number" id="screenMaxIntervalSec" min="0" max="600" value="0" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-slate-200">
            </div>
          </div>
          <div>
            <label class="block text-slate-400 text-[11px] mb-0.5">差分アルゴリズム</label>
            <select id="screenDiffAlgorithm" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-xs text-slate-200">
              <option value="phash">pHash 8x8 DCT (推奨・過剰撮影防止・スライド変化検知)</option>
              <option value="hybrid">Hybrid (輝度差＋面積比率)</option>
            </select>
          </div>
          <div class="grid grid-cols-2 gap-2 text-[11px]">
            <div>
              <label class="block text-slate-400 mb-0.5">pHashハミング距離閾値</label>
              <input type="number" id="screenPhashThreshold" min="1" max="64" value="5" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-slate-200">
            </div>
            <div>
              <label class="block text-slate-400 mb-0.5">最小クールダウン (秒)</label>
              <input type="number" id="screenMinIntervalSec" min="1" max="30" value="2" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-slate-200">
            </div>
          </div>
          <div class="grid grid-cols-2 gap-2 text-[11px]">
            <div>
              <label class="block text-slate-400 mb-0.5">JPEG品質 (%)</label>
              <input type="number" id="screenJpegQuality" min="10" max="100" value="80" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-slate-200">
            </div>
            <div>
              <label class="block text-slate-400 mb-0.5">デバウンス安定待ち (ms)</label>
              <input type="number" id="screenDebounceMs" min="0" max="2000" step="100" value="500" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-slate-200">
            </div>
          </div>
          <button id="saveScreenConfigBtn" class="w-full py-1.5 rounded bg-indigo-600 hover:bg-indigo-500 text-white font-medium transition text-xs mt-1">
            💾 画面設定保存
          </button>
          <div id="screenStatusResult" class="text-[10px] min-h-[1rem] break-all text-slate-400"></div>
        </div>

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
    const meetingMemoInput = document.getElementById('meetingMemoInput');
    const memoSaveStatus = document.getElementById('memoSaveStatus');

    // Zoom風 音量インジケーター (4本バー)
    const vBar1 = document.getElementById('vBar1');
    const vBar2 = document.getElementById('vBar2');
    const vBar3 = document.getElementById('vBar3');
    const vBar4 = document.getElementById('vBar4');

    // Box UI & テーマ
    const boxAuthBadge = document.getElementById('boxAuthBadge');
    const boxLoginQuickBtn = document.getElementById('boxLoginQuickBtn');
    const themeSelect = document.getElementById('themeSelect');
    const themeSegmentContainer = document.getElementById('themeSegmentContainer');
    let cachedThemes = [];
    let currentSelectedThemeId = null;

    // 設定モーダル
    const openSettingsBtn = document.getElementById('openSettingsBtn');
    const closeSettingsBtn = document.getElementById('closeSettingsBtn');
    const settingsModal = document.getElementById('settingsModal');
    const boxClientId = document.getElementById('boxClientId');
    const boxClientSecret = document.getElementById('boxClientSecret');
    const boxSharedFolderId = document.getElementById('boxSharedFolderId');
    const boxDomain = document.getElementById('boxDomain');
    const boxDevToken = document.getElementById('boxDevToken');
    const boxAutoUpload = document.getElementById('boxAutoUpload');
    const boxBypassProxy = document.getElementById('boxBypassProxy');
    const startBoxLoginBtn = document.getElementById('startBoxLoginBtn');
    const saveBoxConfigBtn = document.getElementById('saveBoxConfigBtn');
    const boxStatusResult = document.getElementById('boxStatusResult');
    const toggleSecretVisibility = document.getElementById('toggleSecretVisibility');
    const toggleDevTokenVisibility = document.getElementById('toggleDevTokenVisibility');

    // 画面キャプチャ設定 UI
    const screenCheckIntervalSec = document.getElementById('screenCheckIntervalSec');
    const screenMaxIntervalSec = document.getElementById('screenMaxIntervalSec');
    const screenDiffAlgorithm = document.getElementById('screenDiffAlgorithm');
    const screenPhashThreshold = document.getElementById('screenPhashThreshold');
    const screenMinIntervalSec = document.getElementById('screenMinIntervalSec');
    const screenJpegQuality = document.getElementById('screenJpegQuality');
    const screenDebounceMs = document.getElementById('screenDebounceMs');
    const saveScreenConfigBtn = document.getElementById('saveScreenConfigBtn');
    const screenStatusResult = document.getElementById('screenStatusResult');

    // アップロード進捗 UI
    const uploadProgressContainer = document.getElementById('uploadProgressContainer');
    const uploadStatusText = document.getElementById('uploadStatusText');
    const uploadCountBadge = document.getElementById('uploadCountBadge');
    const uploadProgressBar = document.getElementById('uploadProgressBar');
    const uploadSuccessMessage = document.getElementById('uploadSuccessMessage');
    const boxFolderLink = document.getElementById('boxFolderLink');

    // 未送信スプール UI
    const pendingSpoolArea = document.getElementById('pendingSpoolArea');
    const pendingTitle = document.getElementById('pendingTitle');
    const pendingList = document.getElementById('pendingList');
    const refreshPendingBtn = document.getElementById('refreshPendingBtn');

    // ログ UI (案1: 1行バー ＆ 展開)
    const logStatusBar = document.getElementById('logStatusBar');
    const latestLogSummary = document.getElementById('latestLogSummary');
    const toggleLogBtn = document.getElementById('toggleLogBtn');
    const toggleLogText = document.getElementById('toggleLogText');
    const toggleLogArrow = document.getElementById('toggleLogArrow');
    const logExpandedArea = document.getElementById('logExpandedArea');
    const logConsole = document.getElementById('logConsole');
    const autoScrollLogs = document.getElementById('autoScrollLogs');
    const refreshLogsBtn = document.getElementById('refreshLogsBtn');
    const clearLogsBtn = document.getElementById('clearLogsBtn');
    let lastLogJson = '';

    let isRecording = false;
    let timerInterval = null;
    let startTime = null;

    // 設定モーダルの開閉
    openSettingsBtn.addEventListener('click', () => {
      settingsModal.classList.remove('hidden');
    });
    closeSettingsBtn.addEventListener('click', () => {
      settingsModal.classList.add('hidden');
    });
    settingsModal.addEventListener('click', (e) => {
      if (e.target === settingsModal) {
        settingsModal.classList.add('hidden');
      }
    });

    // ログ開閉トグル (案1)
    function toggleLogView() {
      const isHidden = logExpandedArea.classList.contains('hidden');
      if (isHidden) {
        logExpandedArea.classList.remove('hidden');
        toggleLogText.textContent = '閉じる';
        toggleLogArrow.textContent = '▲';
        if (autoScrollLogs.checked) {
          logConsole.scrollTop = logConsole.scrollHeight;
        }
      } else {
        logExpandedArea.classList.add('hidden');
        toggleLogText.textContent = '展開';
        toggleLogArrow.textContent = '▼';
      }
    }
    logStatusBar.addEventListener('click', (e) => {
      // リンククリック時はトグルしない
      if (e.target.tagName === 'A') return;
      toggleLogView();
    });
    toggleLogBtn.addEventListener('click', (e) => {
      e.stopPropagation();
      toggleLogView();
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
      await loadThemes();
      await loadBoxStatus();
      await loadPendingSessions();
      await loadLogs();
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
          opt.textContent = s.isPrimary ? `画面${s.index + 1} (${s.width}x${s.height}) ★主` : `画面${s.index + 1} (${s.width}x${s.height})`;
          screenSelect.appendChild(opt);
        });
      } catch (err) {
        console.error('画面一覧取得エラー:', err);
      }
    }

    // Box 認証ステータス
    async function loadBoxStatus() {
      try {
        const res = await fetch('/api/box/status');
        const data = await res.json();
        if (data.isAuthenticated) {
          boxAuthBadge.className = 'text-[10px] font-semibold px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-400 border border-emerald-500/30';
          boxAuthBadge.textContent = 'Box認証済 ✓';
          boxLoginQuickBtn.classList.add('hidden');
        } else {
          boxAuthBadge.className = 'text-[10px] font-semibold px-2 py-0.5 rounded bg-amber-500/20 text-amber-300 border border-amber-500/30';
          boxAuthBadge.textContent = 'Box未認証';
          boxLoginQuickBtn.classList.remove('hidden');
        }
      } catch (err) {
        boxAuthBadge.className = 'text-[10px] font-semibold px-2 py-0.5 rounded bg-rose-500/20 text-rose-400 border border-rose-500/30';
        boxAuthBadge.textContent = 'Box確認失敗';
      }
    }

    // テーマ一覧読み込み ＆ インライン2連スイッチ描画
    async function loadThemes(forceRefresh = false) {
      try {
        const url = forceRefresh ? '/api/box/themes/refresh' : '/api/box/themes';
        const res = await (forceRefresh ? fetch(url, { method: 'POST' }) : fetch(url));
        const themes = await res.json();
        cachedThemes = themes || [];

        const statusRes = await fetch('/api/box/status');
        const status = await statusRes.json();
        currentSelectedThemeId = status.selectedThemeId || (cachedThemes[0] ? cachedThemes[0].id : null);

        renderThemeSegment();
      } catch (err) {
        console.error('テーマ一覧取得エラー:', err);
      }
    }

    // テーマ選択セグメントスイッチの描画
    function renderThemeSegment() {
      themeSegmentContainer.innerHTML = '';
      if (!cachedThemes || cachedThemes.length === 0) {
        themeSegmentContainer.innerHTML = '<span class="text-[11px] text-slate-500 px-2 py-1">テーマなし</span>';
        return;
      }

      cachedThemes.forEach(t => {
        const btn = document.createElement('button');
        const isSelected = t.id === currentSelectedThemeId;
        const icon = t.name.includes('社員') ? '👥 ' : (t.name.includes('全体') ? '🌐 ' : '📁 ');
        btn.innerHTML = `<span>${icon}</span><span>${escapeHtml(t.name)}</span>`;
        btn.className = isSelected 
          ? 'px-2.5 py-1 rounded-md text-xs font-bold bg-indigo-600 text-white shadow-sm transition' 
          : 'px-2.5 py-1 rounded-md text-xs font-medium text-slate-400 hover:text-slate-200 hover:bg-slate-900 transition';
        
        btn.addEventListener('click', async () => {
          if (currentSelectedThemeId === t.id) return;
          currentSelectedThemeId = t.id;
          renderThemeSegment();
          try {
            await fetch('/api/box/theme/select', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ themeId: t.id })
            });
            setTimeout(loadLogs, 300);
          } catch (err) {
            console.error('テーマ切替エラー:', err);
          }
        });

        themeSegmentContainer.appendChild(btn);
      });
    }

    boxLoginQuickBtn.addEventListener('click', async () => {
      try {
        boxLoginQuickBtn.disabled = true;
        boxLoginQuickBtn.innerHTML = '<span>⏳</span><span>認可画面へ...</span>';
        const res = await fetch('/api/box/login', { method: 'POST' });
        const data = await res.json();
        if (data.authUrl) {
          window.open(data.authUrl, '_blank');
        }
      } catch (err) {
        alert('ログイン開始エラー: ' + err.message);
      } finally {
        boxLoginQuickBtn.disabled = false;
        boxLoginQuickBtn.innerHTML = '<span>🌐</span><span>Boxログイン</span>';
      }
    });

    startBoxLoginBtn.addEventListener('click', async () => {
      try {
        startBoxLoginBtn.disabled = true;
        startBoxLoginBtn.textContent = '認可画面を開いています...';
        const res = await fetch('/api/box/login', { method: 'POST' });
        const data = await res.json();
        if (data.authUrl) {
          window.open(data.authUrl, '_blank');
        }
      } catch (err) {
        alert('ログイン開始エラー: ' + err.message);
      } finally {
        startBoxLoginBtn.disabled = false;
        startBoxLoginBtn.textContent = '🌐 ブラウザでBoxログイン';
      }
    });

    saveBoxConfigBtn.addEventListener('click', async () => {
      try {
        saveBoxConfigBtn.disabled = true;
        saveBoxConfigBtn.textContent = '保存中...';
        const req = {
          clientId: boxClientId.value.trim(),
          clientSecret: boxClientSecret.value.trim(),
          sharedFolderId: boxSharedFolderId.value.trim(),
          domain: boxDomain.value.trim() || null,
          developerToken: boxDevToken.value.trim() || null,
          autoUploadOnStop: boxAutoUpload.checked,
          bypassProxy: boxBypassProxy.checked
        };
        const res = await fetch('/api/config/box', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(req)
        });
        const data = await res.json();
        boxStatusResult.textContent = '✅ 設定を保存しました。';
        boxStatusResult.className = 'text-[11px] text-emerald-400 mt-1';
        await loadBoxStatus();
        await loadThemes();
      } catch (err) {
        boxStatusResult.textContent = '❌ 保存エラー: ' + err.message;
        boxStatusResult.className = 'text-[11px] text-rose-400 mt-1';
      } finally {
        saveBoxConfigBtn.disabled = false;
        saveBoxConfigBtn.textContent = '💾 設定保存';
      }
    });

    saveScreenConfigBtn.addEventListener('click', async () => {
      try {
        saveScreenConfigBtn.disabled = true;
        saveScreenConfigBtn.textContent = '保存中...';
        const req = {
          checkIntervalSec: parseInt(screenCheckIntervalSec.value, 10) || 5,
          maxIntervalSec: parseInt(screenMaxIntervalSec.value, 10) || 0,
          diffAlgorithm: screenDiffAlgorithm.value || 'phash',
          phashThreshold: parseInt(screenPhashThreshold.value, 10) || 5,
          minIntervalSec: parseInt(screenMinIntervalSec.value, 10) || 2,
          jpegQuality: parseInt(screenJpegQuality.value, 10) || 80,
          debounceStabilityMs: parseInt(screenDebounceMs.value, 10) || 500
        };
        const res = await fetch('/api/config/screen', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(req)
        });
        const data = await res.json();
        screenStatusResult.textContent = '✅ 画面キャプチャ設定を保存しました。';
        screenStatusResult.className = 'text-[11px] text-emerald-400 mt-1';
      } catch (err) {
        screenStatusResult.textContent = '❌ 保存エラー: ' + err.message;
        screenStatusResult.className = 'text-[11px] text-rose-400 mt-1';
      } finally {
        saveScreenConfigBtn.disabled = false;
        saveScreenConfigBtn.textContent = '💾 画面設定保存';
      }
    });

    async function loadConfig() {
      try {
        const res = await fetch('/api/config');
        const cfg = await res.json();
        if (cfg.box) {
          boxClientId.value = cfg.box.clientId || '';
          boxClientSecret.value = cfg.box.clientSecret || '';
          boxSharedFolderId.value = cfg.box.sharedFolderId || '';
          boxDomain.value = cfg.box.domain || '';
          boxDevToken.value = cfg.box.developerToken || '';
          boxAutoUpload.checked = cfg.box.autoUploadOnStop ?? true;
          boxBypassProxy.checked = cfg.box.bypassProxy ?? false;
        }
        if (cfg.screen) {
          screenCheckIntervalSec.value = cfg.screen.checkIntervalSec ?? 5;
          screenMaxIntervalSec.value = cfg.screen.maxIntervalSec ?? 0;
          screenDiffAlgorithm.value = cfg.screen.diffAlgorithm || 'phash';
          screenPhashThreshold.value = cfg.screen.phashThreshold ?? 5;
          screenMinIntervalSec.value = cfg.screen.minIntervalSec ?? 2;
          screenJpegQuality.value = cfg.screen.jpegQuality ?? 80;
          screenDebounceMs.value = cfg.screen.debounceStabilityMs ?? 500;
        }
      } catch (err) {
        console.error('設定ロードエラー:', err);
      }
    }

    // 録音開始 / 停止
    recordBtn.addEventListener('click', async () => {
      if (!isRecording) {
        await startRecording();
      } else {
        await stopRecording();
      }
    });

    async function startRecording() {
      const playbackId = playbackSelect.value || null;
      const captureId = captureSelect.value || null;
      const monitorIndex = parseInt(screenSelect.value, 10) || 0;
      const memo = meetingMemoInput.value.trim() || null;

      recordBtn.disabled = true;
      recordBtnText.textContent = '準備中...';

      try {
        const res = await fetch('/api/record/start', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            playbackDeviceId: playbackId,
            captureDeviceId: captureId,
            monitorIndex: monitorIndex,
            enableScreenCapture: true,
            themeId: currentSelectedThemeId,
            memo: memo
          })
        });
        const data = await res.json();
        if (data.success) {
          isRecording = true;
          recordBtn.className = 'px-4 py-1.5 rounded-lg font-bold text-xs bg-slate-700 hover:bg-slate-600 text-white shadow-md transition flex items-center space-x-1.5';
          recordBtnIcon.className = 'w-2 h-2 rounded-sm bg-rose-400';
          recordBtnText.textContent = '記録停止';
          screenshotBtn.disabled = false;
          screenshotBtn.classList.remove('hidden');
          screenshotBtn.classList.remove('cursor-not-allowed', 'text-slate-500');
          screenshotBtn.classList.add('bg-slate-700', 'text-slate-200', 'hover:bg-slate-600');

          statusBadge.className = 'px-2.5 py-1 rounded-full text-xs font-bold bg-rose-500/20 text-rose-400 border border-rose-500/30 flex items-center space-x-1.5 animate-pulse';

          startTime = Date.now();
          startTimer();
          uploadProgressContainer.classList.add('hidden');
        } else {
          alert('開始エラー: ' + data.error);
        }
      } catch (err) {
        alert('通信エラー: ' + err.message);
      } finally {
        recordBtn.disabled = false;
        await loadLogs();
      }
    }

    async function stopRecording() {
      recordBtn.disabled = true;
      recordBtnText.textContent = '停止中...';

      uploadProgressContainer.classList.remove('hidden');
      uploadSuccessMessage.classList.add('hidden');
      uploadStatusText.textContent = 'BoxへZIP圧縮・アップロード準備中...';
      uploadCountBadge.textContent = '処理中';
      uploadProgressBar.style.width = '20%';
      uploadProgressBar.className = 'bg-blue-600 h-2 rounded-full transition-all duration-300';

      const memo = meetingMemoInput.value.trim() || null;

      try {
        const res = await fetch('/api/record/stop', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            autoUpload: boxAutoUpload.checked,
            themeId: currentSelectedThemeId,
            memo: memo
          })
        });
        const data = await res.json();

        isRecording = false;
        stopTimer();
        timerText.textContent = '待機中';
        statusBadge.className = 'px-2.5 py-1 rounded-full text-xs font-bold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 flex items-center space-x-1.5';
        statusBadge.classList.remove('animate-pulse');

        recordBtn.className = 'px-4 py-1.5 rounded-lg font-bold text-xs bg-rose-600 hover:bg-rose-500 text-white shadow-md shadow-rose-600/30 transition flex items-center space-x-1.5';
        recordBtnIcon.className = 'w-2 h-2 rounded-full bg-emerald-400';
        recordBtnText.textContent = '記録開始';
        screenshotBtn.disabled = true;
        screenshotBtn.classList.add('hidden');

        if (data.success) {
          if (data.boxUpload && data.boxUpload.success) {
            uploadProgressBar.style.width = '100%';
            uploadStatusText.textContent = '✅ BoxへのZIPアップロードが完了しました！';
            uploadCountBadge.textContent = '完了';
            uploadSuccessMessage.classList.remove('hidden');
            if (data.boxUpload.folderUrl) {
              boxFolderLink.href = data.boxUpload.folderUrl;
            }
          } else if (data.boxUpload && !data.boxUpload.success) {
            uploadStatusText.textContent = `⚠️ Boxアップロードエラー: ${data.boxUpload.message}`;
            uploadProgressBar.className = 'bg-rose-500 h-2 rounded-full';
          } else {
            uploadProgressBar.style.width = '100%';
            uploadStatusText.textContent = '✅ ローカル保存が完了しました。';
            uploadCountBadge.textContent = '完了';
          }
        } else {
          uploadStatusText.textContent = `❌ 停止処理エラー: ${data.error}`;
          uploadProgressBar.className = 'bg-rose-500 h-2 rounded-full';
        }
      } catch (err) {
        uploadStatusText.textContent = `❌ 通信エラー: ${err.message}`;
        uploadProgressBar.className = 'bg-rose-500 h-2 rounded-full';
      } finally {
        recordBtn.disabled = false;
        await loadPendingSessions();
        await loadLogs();
      }
    }

    screenshotBtn.addEventListener('click', async () => {
      try {
        const res = await fetch('/api/record/screenshot', { method: 'POST' });
        if (res.ok) {
          const current = parseInt(shotCountText.textContent.replace(/[^0-9]/g, '') || '0', 10);
          shotCountText.textContent = `撮影: ${current + 1} 枚`;
          setTimeout(loadLogs, 300);
        }
      } catch (err) {
        console.error('スクショ撮影エラー:', err);
      }
    });

    // 会議メモのリアルタイム同期 (入力デバウンス ＆ フォーカスアウト時)
    let memoDebounceTimer = null;
    meetingMemoInput.addEventListener('input', () => {
      clearTimeout(memoDebounceTimer);
      memoDebounceTimer = setTimeout(syncMeetingMemo, 500);
    });
    meetingMemoInput.addEventListener('blur', syncMeetingMemo);

    async function syncMeetingMemo() {
      const memo = meetingMemoInput.value;
      try {
        const res = await fetch('/api/record/memo', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ memo: memo ? memo.trim() : null })
        });
        if (res.ok) {
          memoSaveStatus.style.opacity = '1';
          setTimeout(() => { memoSaveStatus.style.opacity = '0'; }, 1500);
        }
      } catch { /* ignore */ }
    }

    function startTimer() {
      stopTimer();
      timerInterval = setInterval(() => {
        if (!startTime) return;
        const diff = Math.floor((Date.now() - startTime) / 1000);
        const h = String(Math.floor(diff / 3600)).padStart(2, '0');
        const m = String(Math.floor((diff % 3600) / 60)).padStart(2, '0');
        const s = String(diff % 60).padStart(2, '0');
        timerText.textContent = `${h}:${m}:${s}`;
      }, 500);
    }

    function stopTimer() {
      if (timerInterval) {
        clearInterval(timerInterval);
        timerInterval = null;
      }
    }

    // Zoom風 音量インジケーター更新 (4本バー)
    function updateVolumeBars(levelPct) {
      // levelPct: 0〜100
      vBar1.className = levelPct > 4 
        ? 'w-0.5 h-1 bg-emerald-400 rounded-full transition-all duration-75' 
        : 'w-0.5 h-1 bg-slate-700 rounded-full transition-all duration-75';
      vBar2.className = levelPct > 20 
        ? 'w-0.5 h-2 bg-emerald-400 rounded-full transition-all duration-75' 
        : 'w-0.5 h-2 bg-slate-700 rounded-full transition-all duration-75';
      vBar3.className = levelPct > 45 
        ? 'w-0.5 h-2.5 bg-emerald-400 rounded-full transition-all duration-75' 
        : 'w-0.5 h-2.5 bg-slate-700 rounded-full transition-all duration-75';
      vBar4.className = levelPct > 70 
        ? 'w-0.5 h-3 bg-amber-400 rounded-full transition-all duration-75' 
        : 'w-0.5 h-3 bg-slate-700 rounded-full transition-all duration-75';
    }

    // ステータスポーリング
    function startPollingStatus() {
      setInterval(async () => {
        try {
          const res = await fetch('/api/record/status');
          if (!res.ok) return;
          const data = await res.json();
          
          if (data.isRecording && !isRecording) {
            isRecording = true;
            recordBtn.className = 'px-4 py-1.5 rounded-lg font-bold text-xs bg-slate-700 hover:bg-slate-600 text-white shadow-md transition flex items-center space-x-1.5';
            recordBtnIcon.className = 'w-2 h-2 rounded-sm bg-rose-400';
            recordBtnText.textContent = '記録停止';
            screenshotBtn.disabled = false;
            screenshotBtn.classList.remove('hidden');
            screenshotBtn.classList.remove('cursor-not-allowed', 'text-slate-500');
            screenshotBtn.classList.add('bg-slate-700', 'text-slate-200', 'hover:bg-slate-600');

            statusBadge.className = 'px-2.5 py-1 rounded-full text-xs font-bold bg-rose-500/20 text-rose-400 border border-rose-500/30 flex items-center space-x-1.5 animate-pulse';

            if (data.elapsedSeconds) {
              startTime = Date.now() - (data.elapsedSeconds * 1000);
            } else if (!startTime) {
              startTime = Date.now();
            }
            startTimer();
          } else if (!data.isRecording && isRecording) {
            isRecording = false;
            stopTimer();
            timerText.textContent = '待機中';
            statusBadge.className = 'px-2.5 py-1 rounded-full text-xs font-bold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 flex items-center space-x-1.5';
            statusBadge.classList.remove('animate-pulse');

            recordBtn.className = 'px-4 py-1.5 rounded-lg font-bold text-xs bg-rose-600 hover:bg-rose-500 text-white shadow-md shadow-rose-600/30 transition flex items-center space-x-1.5';
            recordBtnIcon.className = 'w-2 h-2 rounded-full bg-emerald-400';
            recordBtnText.textContent = '記録開始';
            screenshotBtn.disabled = true;
            screenshotBtn.classList.add('hidden');
          }

          if (data.isRecording) {
            shotCountText.textContent = `撮影: ${data.screenshotCount || 0} 枚`;
            const level = Math.round((data.audioLevel || 0) * 100);
            updateVolumeBars(level);
          } else {
            updateVolumeBars(0);
          }

          // アップロード中プログレス更新
          if (data.progress && data.progress.phase === 'uploading') {
            uploadProgressContainer.classList.remove('hidden');
            uploadStatusText.textContent = data.progress.message || 'Boxへアップロード中...';
            uploadCountBadge.textContent = data.progress.badge || `${data.progress.percentage}%`;
            uploadProgressBar.style.width = `${data.progress.percentage}%`;
            uploadProgressBar.className = 'bg-blue-600 h-2 rounded-full transition-all duration-300';
          }
        } catch { /* ignore */ }
      }, 500);

      // ログ＆スプールの定期自動更新 (3秒ごと)
      setInterval(() => {
        loadLogs();
        loadPendingSessions();
      }, 3000);
    }

    // HTMLエスケープヘルパー
    function escapeHtml(str) {
      if (!str) return '';
      return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
    }

    // 未送信スプール
    let currentPendingSessions = [];

    async function loadPendingSessions() {
      try {
        const res = await fetch('/api/sessions/pending');
        const list = await res.json();
        currentPendingSessions = list || [];
        
        if (!list || list.length === 0) {
          pendingSpoolArea.classList.add('hidden');
          pendingList.innerHTML = '';
          return;
        }

        pendingSpoolArea.classList.remove('hidden');
        pendingTitle.textContent = `未送信スプール (${list.length}件)`;
        pendingList.innerHTML = '';

        list.forEach(item => {
          const row = document.createElement('div');
          row.className = 'p-3 rounded-lg bg-slate-900 border border-slate-800 space-y-2 text-xs shadow-sm';
          const memoEscaped = escapeHtml(item.memo || '');
          const currentTheme = item.themeId || currentSelectedThemeId;

          // テーマ2連スイッチのHTML生成
          let themeButtonsHtml = '<div class="inline-flex rounded bg-slate-950 p-0.5 border border-slate-700/60">';
          cachedThemes.forEach(t => {
            const isSel = t.id === currentTheme;
            const icon = t.name.includes('社員') ? '👥' : (t.name.includes('全体') ? '🌐' : '📁');
            const cls = isSel 
              ? 'px-2 py-0.5 rounded text-[10px] font-bold bg-indigo-600 text-white' 
              : 'px-2 py-0.5 rounded text-[10px] font-medium text-slate-400 hover:text-slate-200';
            themeButtonsHtml += `<button onclick="updateSpoolTheme('${item.sessionId}', '${t.id}')" class="${cls}">${icon} ${escapeHtml(t.name)}</button>`;
          });
          themeButtonsHtml += '</div>';

          row.innerHTML = `
            <div class="flex items-center justify-between gap-1 flex-wrap">
              <div class="flex items-center space-x-2">
                <span class="font-mono text-slate-200 text-xs font-semibold">📁 ${item.sessionId}</span>
                ${themeButtonsHtml}
              </div>
              <div class="flex items-center space-x-1.5">
                <button onclick="manualUploadBox('${item.sessionId}')" class="px-2.5 py-1 rounded bg-blue-600 hover:bg-blue-500 text-white text-[11px] font-semibold transition flex items-center space-x-1">
                  <span>🚀</span>
                  <span>Box送信</span>
                </button>
                <button onclick="deleteSession('${item.sessionId}')" class="px-2 py-1 rounded bg-slate-800 hover:bg-rose-900/60 text-slate-400 hover:text-rose-300 text-[11px] transition">削除</button>
              </div>
            </div>
            <div class="flex items-center space-x-2 bg-slate-950 border border-slate-800 focus-within:border-indigo-500 rounded px-2 py-1 transition">
              <span class="text-slate-500 text-[11px]">📝</span>
              <input 
                type="text" 
                value="${memoEscaped}" 
                placeholder="会議メモを追加・編集..." 
                class="flex-1 bg-transparent text-[11px] text-slate-200 placeholder-slate-600 focus:outline-none" 
                onchange="updateSpoolMemo('${item.sessionId}', this.value, this)" 
                onkeydown="if(event.key==='Enter') this.blur();"
              />
              <span class="spool-memo-badge text-[10px] text-emerald-400 opacity-0 transition-opacity">✓</span>
            </div>
            <div class="flex items-center justify-between text-[10px] text-slate-400 pt-0.5">
              <div class="flex items-center space-x-2">
                <span>📷 スクショ ${item.screenshotCount}枚</span>
                <span>•</span>
                <span>💾 ${(item.totalSizeBytes / 1024 / 1024).toFixed(1)}MB</span>
              </div>
              <span>${new Date(item.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
            </div>
          `;
          pendingList.appendChild(row);
        });
      } catch (err) {
        console.error('未送信スプール取得エラー:', err);
      }
    }

    // スプールのテーマ更新
    window.updateSpoolTheme = async function(sessionId, themeId) {
      try {
        const res = await fetch(`/api/sessions/${sessionId}/theme`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ themeId })
        });
        if (res.ok) {
          await loadPendingSessions();
          await loadLogs();
        }
      } catch (err) {
        console.error('スプールテーマ更新エラー:', err);
      }
    };

    // スプールメモ更新
    window.updateSpoolMemo = async function(sessionId, memo, inputEl) {
      const badge = inputEl.parentElement.querySelector('.spool-memo-badge');
      try {
        const res = await fetch(`/api/sessions/${sessionId}/memo`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ memo: memo ? memo.trim() : null })
        });
        if (res.ok) {
          if (badge) {
            badge.style.opacity = '1';
            setTimeout(() => { badge.style.opacity = '0'; }, 1500);
          }
          await loadPendingSessions();
          await loadLogs();
        }
      } catch (err) {
        console.error('スプールメモ更新エラー:', err);
      }
    };

    window.manualUploadBox = async function(sessionId) {
      const item = currentPendingSessions.find(s => s.sessionId === sessionId);
      const targetThemeId = (item && item.themeId) || currentSelectedThemeId;
      const targetTheme = cachedThemes.find(t => t.id === targetThemeId);
      const themeName = targetTheme ? targetTheme.name : '既定';

      const memoText = item && item.memo ? `\n📝 メモ: 【${item.memo}】` : '';

      const confirmMsg =
        `セッション ${sessionId} をBoxへアップロードしますか？\n` +
        `${memoText}\n\n` +
        `📁 保存先テーマ: 【${themeName}】`;

      if (!confirm(confirmMsg)) return;

      uploadProgressContainer.classList.remove('hidden');
      uploadSuccessMessage.classList.add('hidden');
      uploadStatusText.textContent = `Boxへアップロード準備中: ${sessionId}`;
      uploadCountBadge.textContent = '送信中';
      uploadProgressBar.style.width = '20%';
      uploadProgressBar.className = 'bg-blue-600 h-2 rounded-full transition-all duration-300';

      try {
        const res = await fetch('/api/box/upload', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sessionId, themeId: targetThemeId })
        });
        const data = await res.json();
        if (data.success) {
          uploadProgressBar.style.width = '100%';
          uploadStatusText.textContent = '✅ Box へのZIPアップロードが完了しました！';
          uploadCountBadge.textContent = '完了';
          uploadSuccessMessage.classList.remove('hidden');
          if (data.folderUrl) {
            boxFolderLink.href = data.folderUrl;
          }
          await loadPendingSessions();
          await loadLogs();
          setTimeout(() => {
            uploadProgressContainer.classList.add('hidden');
          }, 5000);
        } else {
          uploadStatusText.textContent = `❌ アップロード失敗: ${data.message}`;
          uploadProgressBar.className = 'bg-rose-500 h-2 rounded-full';
        }
      } catch (err) {
        uploadStatusText.textContent = `❌ 通信エラー: ${err.message}`;
        uploadProgressBar.className = 'bg-rose-500 h-2 rounded-full';
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

    // 動作ログの整形と取得 (案1: 1行バー ＆ 展開コンソール)
    function formatLogLine(line) {
      let cls = 'text-slate-400';
      let bg = '';
      
      if (line.includes('【Box格納完了】')) {
        cls = 'text-emerald-300 font-bold';
        bg = 'bg-emerald-950/60 border border-emerald-800/80 px-1.5 py-0.5 rounded my-0.5';
      } else if (line.includes('❌') || line.includes('エラー') || line.includes('FAIL')) {
        cls = 'text-rose-400 font-semibold';
        bg = 'bg-rose-950/30 px-1 rounded';
      } else if (line.includes('⚠️') || line.includes('警告') || line.includes('WARN')) {
        cls = 'text-amber-400';
      } else if (line.includes('🎙️') || line.includes('⏹️')) {
        cls = 'text-sky-300 font-medium';
      } else if (line.includes('🚀') || line.includes('⭐')) {
        cls = 'text-blue-300';
      } else if (line.includes('📸')) {
        cls = 'text-purple-300';
      } else if (line.includes('🔗')) {
        cls = 'text-indigo-300 font-semibold';
      }

      const escaped = line
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
      
      const linked = escaped.replace(/(https?:\/\/[^\s]+)/g, '<a href="$1" target="_blank" class="text-indigo-400 hover:text-indigo-300 underline font-semibold">$1 ↗</a>');

      return `<div class="${cls} ${bg} break-all">${linked}</div>`;
    }

    async function loadLogs() {
      try {
        const res = await fetch('/api/logs?limit=150');
        if (!res.ok) return;
        const logs = await res.json();
        const jsonStr = JSON.stringify(logs);
        if (jsonStr === lastLogJson) return;
        lastLogJson = jsonStr;

        if (!logs || logs.length === 0) {
          latestLogSummary.innerHTML = '<span class="text-slate-500 text-[10px]">ログはありません。</span>';
          logConsole.innerHTML = '<div class="text-slate-500 text-[10px]">ログはありません。</div>';
          return;
        }

        // 1行ステータスバー用の最新ログ（直近のBox格納完了があればそれを優先）
        let boxDoneLog = logs.slice().reverse().find(l => l.includes('【Box格納完了】'));
        let targetLine = boxDoneLog || logs[logs.length - 1];
        
        let summaryHtml = escapeHtml(targetLine);
        summaryHtml = summaryHtml.replace(/(https?:\/\/[^\s]+)/g, '<a href="$1" target="_blank" class="text-indigo-400 hover:underline font-bold">$1 ↗</a>');
        
        if (targetLine.includes('【Box格納完了】')) {
          latestLogSummary.innerHTML = `<span class="text-emerald-400 font-bold">${summaryHtml}</span>`;
        } else if (targetLine.includes('❌') || targetLine.includes('エラー')) {
          latestLogSummary.innerHTML = `<span class="text-rose-400 font-medium">${summaryHtml}</span>`;
        } else {
          latestLogSummary.innerHTML = `<span class="text-slate-300">${summaryHtml}</span>`;
        }

        logConsole.innerHTML = logs.map(formatLogLine).join('');

        if (autoScrollLogs.checked && !logExpandedArea.classList.contains('hidden')) {
          logConsole.scrollTop = logConsole.scrollHeight;
        }
      } catch (err) {
        console.error('ログ取得エラー:', err);
      }
    }

    refreshLogsBtn.addEventListener('click', loadLogs);
    clearLogsBtn.addEventListener('click', async () => {
      try {
        await fetch('/api/logs', { method: 'DELETE' });
        await loadLogs();
      } catch (err) {
        console.error('ログクリアエラー:', err);
      }
    });

    // アプリ終了
    shutdownBtn.addEventListener('click', async () => {
      if (!confirm('WinMeetingRecorder を終了しますか？\n（録音中の場合は停止してから終了してください）')) return;
      try {
        shutdownBtn.disabled = true;
        shutdownBtn.textContent = '終了中...';
        await fetch('/api/app/shutdown', { method: 'POST' });
        document.body.innerHTML = '<div class="flex items-center justify-center min-h-screen text-slate-400 font-mono text-sm">WinMeetingRecorder は終了しました。このタブを閉じてください。</div>';
      } catch (err) {
        alert('終了エラー: ' + err.message);
        shutdownBtn.disabled = false;
        shutdownBtn.textContent = '✕ 終了';
      }
    });

    // ページロード開始
    init();
  </script>
</body>
</html>
""";
}
