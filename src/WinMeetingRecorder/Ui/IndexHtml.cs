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
<body class="bg-slate-900 text-slate-100 min-h-screen flex flex-col items-center justify-start py-8 px-4 font-sans">
  <div class="w-full max-w-2xl bg-slate-800 rounded-2xl shadow-2xl border border-slate-700 p-6 md:p-8 space-y-6">
    
    <!-- ヘッダー -->
    <div class="flex items-center justify-between border-b border-slate-700 pb-4">
      <div class="flex items-center space-x-3">
        <span class="text-3xl">🎙️</span>
        <div>
          <h1 class="text-xl font-bold tracking-tight">WinMeetingRecorder</h1>
          <p class="text-xs text-slate-400">Windows専用 会議記録 ＆ 文字起こし ＆ AI議事録 ＆ GitLab自動同期</p>
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

    <!-- 設定アコーディオン群 -->
    <div class="space-y-2">
      <!-- 1. GitLab 設定 -->
      <div class="border border-slate-700 rounded-xl bg-slate-950/40 overflow-hidden">
        <button id="toggleConfigBtn" class="w-full px-4 py-2.5 text-left text-xs font-semibold text-slate-300 flex items-center justify-between hover:bg-slate-800/50 transition">
          <span class="flex items-center space-x-2">
            <span>🦊</span>
            <span>社内GitLab連携設定</span>
            <span id="configSummaryBadge" class="text-[10px] px-2 py-0.5 rounded bg-slate-800 text-slate-400">未設定</span>
          </span>
          <span id="configArrow" class="text-xs transition-transform transform duration-200">▼</span>
        </button>

        <div id="configSection" class="hidden p-4 border-t border-slate-800 space-y-3 text-xs">
          <div>
            <label class="block text-slate-400 mb-1">GitLab サーバーURL</label>
            <input type="text" id="gitlabUrl" placeholder="https://gitlab.example.com" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="block text-slate-400 mb-1">プロジェクトID / パス</label>
              <input type="text" id="gitlabProjectId" placeholder="1234 または group/project" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
            </div>
            <div>
              <label class="block text-slate-400 mb-1">ブランチ名</label>
              <input type="text" id="gitlabBranch" value="main" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
            </div>
          </div>
          <div>
            <label class="block text-slate-400 mb-1">Personal Access Token (PAT)</label>
            <div class="relative">
              <input type="password" id="gitlabToken" placeholder="glpat-xxxxxxxxxxxxxxxxxxxx" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500 pr-16">
              <button type="button" id="toggleTokenVisibility" class="absolute right-2 top-1.5 text-[11px] text-slate-400 hover:text-slate-200">表示</button>
            </div>
          </div>
          <div>
            <label class="block text-slate-400 mb-1">リポジトリ内 保存先フォルダ</label>
            <input type="text" id="gitlabBasePath" value="meetings" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
          </div>
          <div class="flex items-center space-x-4 pt-1">
            <label class="flex items-center space-x-1.5 cursor-pointer">
              <input type="checkbox" id="gitlabIgnoreSsl" checked class="rounded bg-slate-900 border-slate-700 text-indigo-600 focus:ring-0">
              <span class="text-slate-300">自己署名SSL許可</span>
            </label>
            <label class="flex items-center space-x-1.5 cursor-pointer">
              <input type="checkbox" id="gitlabAutoUpload" checked class="rounded bg-slate-900 border-slate-700 text-indigo-600 focus:ring-0">
              <span class="text-slate-300">停止時に自動プッシュ＆スプール消去</span>
            </label>
          </div>
          <div class="flex space-x-2 pt-2">
            <button id="testGitLabBtn" class="flex-1 py-1.5 rounded bg-slate-700 hover:bg-slate-600 text-slate-200 font-medium transition">🔍 接続テスト</button>
            <button id="saveGitLabBtn" class="flex-1 py-1.5 rounded bg-indigo-600 hover:bg-indigo-500 text-white font-medium transition">💾 設定を保存</button>
          </div>
          <div id="gitlabTestResult" class="text-[11px] min-h-[1rem] break-all"></div>
        </div>
      </div>

      <!-- 2. Whisper 設定 -->
      <div class="border border-slate-700 rounded-xl bg-slate-950/40 overflow-hidden">
        <button id="toggleWhisperBtn" class="w-full px-4 py-2.5 text-left text-xs font-semibold text-slate-300 flex items-center justify-between hover:bg-slate-800/50 transition">
          <span class="flex items-center space-x-2">
            <span>🎙️</span>
            <span>OpenAI Whisper 文字起こし設定</span>
            <span id="whisperSummaryBadge" class="text-[10px] px-2 py-0.5 rounded bg-slate-800 text-slate-400">未設定</span>
          </span>
          <span id="whisperArrow" class="text-xs transition-transform transform duration-200">▼</span>
        </button>

        <div id="whisperSection" class="hidden p-4 border-t border-slate-800 space-y-3 text-xs">
          <div>
            <label class="block text-slate-400 mb-1">Whisper サーバー Base URL (OpenAI 互換)</label>
            <input type="text" id="whisperBaseUrl" placeholder="例: http://192.168.11.x:8000 または https://api.openai.com" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
          </div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="block text-slate-400 mb-1">モデル名</label>
              <input type="text" id="whisperModel" value="whisper-1" placeholder="whisper-1, large-v3, tiny" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
            </div>
            <div>
              <label class="block text-slate-400 mb-1">言語コード</label>
              <input type="text" id="whisperLanguage" value="ja" placeholder="ja" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
            </div>
          </div>
          <div>
            <label class="block text-slate-400 mb-1">API キー (社内ローカル等で不要なら空欄)</label>
            <div class="relative">
              <input type="password" id="whisperApiKey" placeholder="sk-xxxxxxxxxxxxxxxxxxxx" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500 pr-16">
              <button type="button" id="toggleWhisperKeyVisibility" class="absolute right-2 top-1.5 text-[11px] text-slate-400 hover:text-slate-200">表示</button>
            </div>
          </div>
          <div>
            <label class="block text-slate-400 mb-1">専門用語・ヒントプロンプト (任意)</label>
            <input type="text" id="whisperPrompt" placeholder="例: 会議, アジェンダ, GitLab, .NET" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
          </div>
          <div class="flex items-center space-x-4 pt-1">
            <label class="flex items-center space-x-1.5 cursor-pointer">
              <input type="checkbox" id="whisperAutoTranscribe" checked class="rounded bg-slate-900 border-slate-700 text-indigo-600 focus:ring-0">
              <span class="text-slate-300">停止時に自動で文字起こしを実行</span>
            </label>
          </div>
          <div class="flex space-x-2 pt-2">
            <button id="testWhisperBtn" class="flex-1 py-1.5 rounded bg-slate-700 hover:bg-slate-600 text-slate-200 font-medium transition">🔍 接続テスト</button>
            <button id="saveWhisperBtn" class="flex-1 py-1.5 rounded bg-indigo-600 hover:bg-indigo-500 text-white font-medium transition">💾 設定を保存</button>
          </div>
          <div id="whisperTestResult" class="text-[11px] min-h-[1rem] break-all"></div>
        </div>
      </div>

      <!-- 3. AI 議事録設定 (Copilot CLI / Claude Code) -->
      <div class="border border-slate-700 rounded-xl bg-slate-950/40 overflow-hidden">
        <button id="toggleAiBtn" class="w-full px-4 py-2.5 text-left text-xs font-semibold text-slate-300 flex items-center justify-between hover:bg-slate-800/50 transition">
          <span class="flex items-center space-x-2">
            <span>🤖</span>
            <span>AI議事録作成 ＆ チャット修正設定</span>
            <span id="aiSummaryBadge" class="text-[10px] px-2 py-0.5 rounded bg-slate-800 text-slate-400">Copilot</span>
          </span>
          <span id="aiArrow" class="text-xs transition-transform transform duration-200">▼</span>
        </button>

        <div id="aiSection" class="hidden p-4 border-t border-slate-800 space-y-3 text-xs">
          <div class="grid grid-cols-2 gap-3">
            <div>
              <label class="block text-slate-400 mb-1">AI エンジン選択</label>
              <select id="aiEngine" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
                <option value="copilot">GitHub Copilot CLI (copilot)</option>
                <option value="claude">Claude Code (claude)</option>
              </select>
            </div>
            <div>
              <label class="block text-slate-400 mb-1">モデル名 (空なら既定)</label>
              <input type="text" id="aiModel" placeholder="例: gpt-5, claude-3-7-sonnet" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
            </div>
          </div>
          <div>
            <label class="block text-slate-400 mb-1">カスタム CLI パス (空なら PATH から自動検出)</label>
            <input type="text" id="aiCustomCmdPath" placeholder="例: C:\Users\dev\AppData\Roaming\npm\copilot.cmd" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1.5 text-slate-200 focus:outline-none focus:border-indigo-500">
          </div>
          <div class="flex items-center space-x-4 pt-1">
            <label class="flex items-center space-x-1.5 cursor-pointer">
              <input type="checkbox" id="aiAutoGenerate" checked class="rounded bg-slate-900 border-slate-700 text-indigo-600 focus:ring-0">
              <span class="text-slate-300">文字起こし完了後に自動で議事録を作成</span>
            </label>
          </div>
          <div class="flex space-x-2 pt-1">
            <button id="testAiBtn" class="flex-1 py-1.5 rounded bg-slate-700 hover:bg-slate-600 text-slate-200 font-medium transition">🔍 CLI 疎通テスト</button>
            <button id="saveAiBtn" class="flex-1 py-1.5 rounded bg-indigo-600 hover:bg-indigo-500 text-white font-medium transition">💾 設定を保存</button>
          </div>
          <div id="aiTestResult" class="text-[11px] min-h-[1rem] break-all"></div>

          <!-- プロンプト編集アコーディオン / エリア -->
          <div class="border-t border-slate-800 pt-3 space-y-2">
            <div class="flex items-center justify-between">
              <span class="font-medium text-slate-300">📄 議事録生成プロンプト (外部テキストファイル連動)</span>
              <button id="savePromptBtn" class="px-2 py-1 rounded bg-slate-700 hover:bg-slate-600 text-[10px] text-slate-200 transition">💾 プロンプト保存</button>
            </div>
            <div id="promptFilePathDisplay" class="text-[10px] text-slate-500 truncate"></div>
            <textarea id="aiPromptText" rows="6" class="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-2 text-[11px] font-mono text-slate-200 focus:outline-none focus:border-indigo-500 leading-relaxed"></textarea>
            <p class="text-[10px] text-slate-400">※ 変数: <code>{{TRANSCRIPT_TEXT}}</code> (文字起こし), <code>{{IMAGE_COUNT}}</code> (画像枚数), <code>{{START_TIME}}</code>, <code>{{DURATION}}</code></p>
          </div>
        </div>
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
        <p class="text-xs text-slate-400">音声レベル</p>
        <div class="mt-1.5 flex flex-col items-center">
          <div class="w-16 h-2 bg-slate-800 rounded-full overflow-hidden border border-slate-700">
            <div id="audioLevelBar" class="h-full bg-emerald-500 rounded-full transition-all duration-100" style="width: 0%"></div>
          </div>
          <span id="audioLevelText" class="text-[10px] text-slate-500 font-mono mt-0.5">待機</span>
        </div>
      </div>
    </div>

    <!-- アクションボタン群 -->
    <div class="space-y-3 pt-1">
      <button id="toggleBtn" class="w-full py-4 rounded-xl font-bold text-lg shadow-lg transition-all duration-200 bg-red-600 hover:bg-red-500 text-white flex items-center justify-center space-x-2">
        <span id="btnIcon" class="inline-block w-4 h-4 rounded-full bg-white animate-pulse"></span>
        <span id="btnText">会議の記録を開始する</span>
      </button>

      <button id="manualScreenshotBtn" class="hidden w-full py-2.5 rounded-xl font-medium text-sm transition-all duration-200 bg-slate-700 hover:bg-slate-600 text-slate-200 flex items-center justify-center space-x-2 border border-slate-600">
        <span>📸</span>
        <span>今すぐスクショを1枚撮影 (手動)</span>
      </button>

      <div class="flex items-center justify-center space-x-1.5 text-[11px] text-slate-400 pt-0.5">
        <span>💡</span>
        <span>画面右下のタスクトレイに常駐中。トレイアイコンの波形アニメーションで音声入力状況をチラ見確認できます。</span>
      </div>
    </div>

    <!-- 自動パイプライン進捗インジケーター (o - o - o - o) -->
    <div id="pipelineContainer" class="hidden border border-slate-700 rounded-xl bg-slate-950/60 p-4 space-y-3">
      <div class="flex items-center justify-between text-xs">
        <span class="font-semibold text-slate-200 flex items-center space-x-1.5">
          <span>⚡ 会議処理パイプライン</span>
          <span id="pipelineSessionLabel" class="text-[10px] text-slate-400 font-mono"></span>
        </span>
        <span id="pipelineStatusText" class="text-[11px] text-indigo-300 animate-pulse">処理中...</span>
      </div>

      <div class="flex items-center justify-between relative px-2 py-2">
        <div class="absolute left-6 right-6 top-1/2 -translate-y-1/2 h-0.5 bg-slate-700 z-0"></div>

        <!-- Step 1: 録音・MP3 -->
        <div class="relative z-10 flex flex-col items-center group cursor-pointer" onclick="runPipelineStep('audio')">
          <div id="stepDotAudio" class="w-7 h-7 rounded-full bg-emerald-500 text-slate-900 flex items-center justify-center font-bold text-xs shadow-md border-2 border-slate-800">✓</div>
          <span class="text-[10px] mt-1 text-slate-300">① 録音/MP3</span>
        </div>

        <!-- Step 2: 文字起こし -->
        <div class="relative z-10 flex flex-col items-center group cursor-pointer" onclick="runPipelineStep('transcribe')">
          <div id="stepDotTranscribe" class="w-7 h-7 rounded-full bg-slate-700 text-slate-300 flex items-center justify-center font-bold text-xs shadow-md border-2 border-slate-800">2</div>
          <span class="text-[10px] mt-1 text-slate-300">② 文字起こし</span>
        </div>

        <!-- Step 3: AI議事録 -->
        <div class="relative z-10 flex flex-col items-center group cursor-pointer" onclick="runPipelineStep('minutes')">
          <div id="stepDotMinutes" class="w-7 h-7 rounded-full bg-slate-700 text-slate-300 flex items-center justify-center font-bold text-xs shadow-md border-2 border-slate-800">3</div>
          <span class="text-[10px] mt-1 text-slate-300">③ 議事録作成</span>
        </div>

        <!-- Step 4: GitLab -->
        <div class="relative z-10 flex flex-col items-center group cursor-pointer" onclick="runPipelineStep('upload')">
          <div id="stepDotUpload" class="w-7 h-7 rounded-full bg-slate-700 text-slate-300 flex items-center justify-center font-bold text-xs shadow-md border-2 border-slate-800">4</div>
          <span class="text-[10px] mt-1 text-slate-300">④ GitLab保存</span>
        </div>
      </div>
      <p class="text-[10px] text-slate-400 text-center">※ 各ステップの丸アイコンをクリックすると、そのステップから再実行できます</p>
    </div>

    <!-- AI 議事録 プレビュー ＆ チャット修正カード -->
    <div id="minutesCard" class="hidden border border-indigo-500/40 bg-indigo-950/20 rounded-xl p-4 space-y-3">
      <div class="flex items-center justify-between border-b border-indigo-500/20 pb-2">
        <div class="flex items-center space-x-2">
          <span class="text-base">📋</span>
          <span class="font-bold text-xs text-indigo-200">AI 会議議事録</span>
          <span id="minutesSessionBadge" class="text-[10px] px-1.5 py-0.5 rounded bg-indigo-900/60 text-indigo-300 font-mono"></span>
        </div>
        <div class="flex items-center space-x-2">
          <button id="copyMinutesBtn" class="px-2.5 py-1 rounded bg-indigo-600 hover:bg-indigo-500 text-[11px] text-white font-medium transition flex items-center space-x-1">
            <span>📋</span>
            <span>コピー</span>
          </button>
          <button id="closeMinutesBtn" class="text-slate-400 hover:text-white text-xs px-1.5 py-0.5">✕</button>
        </div>
      </div>

      <!-- 議事録テキスト表示 (Markdown) -->
      <div class="max-h-64 overflow-y-auto bg-slate-950/80 rounded-lg p-3 border border-slate-800">
        <pre id="minutesTextDisplay" class="text-xs text-slate-200 whitespace-pre-wrap font-sans leading-relaxed"></pre>
      </div>

      <!-- チャット修正フォーム -->
      <div class="border-t border-indigo-500/20 pt-2 space-y-2">
        <div class="flex items-center justify-between text-[11px] text-indigo-300">
          <span>💬 議事録の修正を依頼（チャット指示）</span>
          <span id="chatEngineLabel" class="text-[10px] text-slate-400"></span>
        </div>
        <div class="flex space-x-2">
          <input type="text" id="chatInput" placeholder="例: 決定事項を箇条書きで強調して、次回までのToDoを整理して..." class="flex-1 bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-slate-200 focus:outline-none focus:border-indigo-500">
          <button id="chatSendBtn" class="px-3 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold transition disabled:opacity-50">修正依頼</button>
        </div>
      </div>
    </div>

    <!-- 未送信スプール一覧エリア -->
    <div id="pendingSpoolArea" class="hidden border border-amber-500/30 bg-amber-500/10 rounded-xl p-3 text-xs space-y-2">
      <div class="flex items-center justify-between text-amber-300 font-semibold">
        <span>⚠️ 保管中のローカル会議記録 (スプール)</span>
        <button id="refreshPendingBtn" class="text-[11px] underline hover:text-white">更新</button>
      </div>
      <div id="pendingList" class="space-y-1 text-slate-300"></div>
    </div>

    <!-- メッセージ・ログ -->
    <div id="logMessage" class="text-xs text-center text-slate-400 break-all min-h-[2rem]">
      初期化完了。音声・画面の記録を開始できます。
    </div>
  </div>

  <script>
    let isRecording = false;
    let timerInterval = null;
    let statusInterval = null;
    let secondsElapsed = 0;
    let currentSessionId = null;
    let activeMinutesSessionId = null;

    const toggleBtn = document.getElementById('toggleBtn');
    const btnText = document.getElementById('btnText');
    const btnIcon = document.getElementById('btnIcon');
    const manualScreenshotBtn = document.getElementById('manualScreenshotBtn');
    const statusBadge = document.getElementById('statusBadge');
    const shutdownBtn = document.getElementById('shutdownBtn');
    const timerDisplay = document.getElementById('timer');
    const screenshotCountDisplay = document.getElementById('screenshotCount');
    const audioLevelBar = document.getElementById('audioLevelBar');
    const audioLevelText = document.getElementById('audioLevelText');
    const logMessage = document.getElementById('logMessage');
    const playbackSelect = document.getElementById('playbackSelect');
    const captureSelect = document.getElementById('captureSelect');
    const screenSelect = document.getElementById('screenSelect');

    // GitLab UI
    const toggleConfigBtn = document.getElementById('toggleConfigBtn');
    const configSection = document.getElementById('configSection');
    const configArrow = document.getElementById('configArrow');
    const configSummaryBadge = document.getElementById('configSummaryBadge');
    const gitlabUrl = document.getElementById('gitlabUrl');
    const gitlabProjectId = document.getElementById('gitlabProjectId');
    const gitlabBranch = document.getElementById('gitlabBranch');
    const gitlabToken = document.getElementById('gitlabToken');
    const gitlabBasePath = document.getElementById('gitlabBasePath');
    const gitlabIgnoreSsl = document.getElementById('gitlabIgnoreSsl');
    const gitlabAutoUpload = document.getElementById('gitlabAutoUpload');
    const testGitLabBtn = document.getElementById('testGitLabBtn');
    const saveGitLabBtn = document.getElementById('saveGitLabBtn');
    const gitlabTestResult = document.getElementById('gitlabTestResult');
    const toggleTokenVisibility = document.getElementById('toggleTokenVisibility');

    // Whisper UI
    const toggleWhisperBtn = document.getElementById('toggleWhisperBtn');
    const whisperSection = document.getElementById('whisperSection');
    const whisperArrow = document.getElementById('whisperArrow');
    const whisperSummaryBadge = document.getElementById('whisperSummaryBadge');
    const whisperBaseUrl = document.getElementById('whisperBaseUrl');
    const whisperModel = document.getElementById('whisperModel');
    const whisperLanguage = document.getElementById('whisperLanguage');
    const whisperApiKey = document.getElementById('whisperApiKey');
    const whisperPrompt = document.getElementById('whisperPrompt');
    const whisperAutoTranscribe = document.getElementById('whisperAutoTranscribe');
    const testWhisperBtn = document.getElementById('testWhisperBtn');
    const saveWhisperBtn = document.getElementById('saveWhisperBtn');
    const whisperTestResult = document.getElementById('whisperTestResult');
    const toggleWhisperKeyVisibility = document.getElementById('toggleWhisperKeyVisibility');

    // AI UI
    const toggleAiBtn = document.getElementById('toggleAiBtn');
    const aiSection = document.getElementById('aiSection');
    const aiArrow = document.getElementById('aiArrow');
    const aiSummaryBadge = document.getElementById('aiSummaryBadge');
    const aiEngine = document.getElementById('aiEngine');
    const aiModel = document.getElementById('aiModel');
    const aiCustomCmdPath = document.getElementById('aiCustomCmdPath');
    const aiAutoGenerate = document.getElementById('aiAutoGenerate');
    const testAiBtn = document.getElementById('testAiBtn');
    const saveAiBtn = document.getElementById('saveAiBtn');
    const aiTestResult = document.getElementById('aiTestResult');
    const savePromptBtn = document.getElementById('savePromptBtn');
    const promptFilePathDisplay = document.getElementById('promptFilePathDisplay');
    const aiPromptText = document.getElementById('aiPromptText');

    // パイプライン ＆ 議事録 UI
    const pipelineContainer = document.getElementById('pipelineContainer');
    const pipelineSessionLabel = document.getElementById('pipelineSessionLabel');
    const pipelineStatusText = document.getElementById('pipelineStatusText');
    const stepDotAudio = document.getElementById('stepDotAudio');
    const stepDotTranscribe = document.getElementById('stepDotTranscribe');
    const stepDotMinutes = document.getElementById('stepDotMinutes');
    const stepDotUpload = document.getElementById('stepDotUpload');

    const minutesCard = document.getElementById('minutesCard');
    const minutesSessionBadge = document.getElementById('minutesSessionBadge');
    const copyMinutesBtn = document.getElementById('copyMinutesBtn');
    const closeMinutesBtn = document.getElementById('closeMinutesBtn');
    const minutesTextDisplay = document.getElementById('minutesTextDisplay');
    const chatInput = document.getElementById('chatInput');
    const chatSendBtn = document.getElementById('chatSendBtn');
    const chatEngineLabel = document.getElementById('chatEngineLabel');

    const pendingSpoolArea = document.getElementById('pendingSpoolArea');
    const pendingList = document.getElementById('pendingList');
    const refreshPendingBtn = document.getElementById('refreshPendingBtn');

    // アコーディオン開閉
    toggleConfigBtn.addEventListener('click', () => toggleAccordion(configSection, configArrow));
    toggleWhisperBtn.addEventListener('click', () => toggleAccordion(whisperSection, whisperArrow));
    toggleAiBtn.addEventListener('click', () => toggleAccordion(aiSection, aiArrow));

    function toggleAccordion(section, arrow) {
      const isHidden = section.classList.contains('hidden');
      if (isHidden) {
        section.classList.remove('hidden');
        arrow.style.transform = 'rotate(180deg)';
      } else {
        section.classList.add('hidden');
        arrow.style.transform = 'rotate(0deg)';
      }
    }

    toggleTokenVisibility.addEventListener('click', () => {
      gitlabToken.type = gitlabToken.type === 'password' ? 'text' : 'password';
      toggleTokenVisibility.textContent = gitlabToken.type === 'password' ? '表示' : '伏せる';
    });

    toggleWhisperKeyVisibility.addEventListener('click', () => {
      whisperApiKey.type = whisperApiKey.type === 'password' ? 'text' : 'password';
      toggleWhisperKeyVisibility.textContent = whisperApiKey.type === 'password' ? '表示' : '伏せる';
    });

    function getGitLabConfigFromForm() {
      return {
        serverUrl: gitlabUrl.value.trim(),
        projectId: gitlabProjectId.value.trim(),
        personalAccessToken: gitlabToken.value.trim(),
        branch: gitlabBranch.value.trim() || 'main',
        basePath: gitlabBasePath.value.trim() || 'meetings',
        ignoreSslErrors: gitlabIgnoreSsl.checked,
        autoUploadOnStop: gitlabAutoUpload.checked
      };
    }

    function getWhisperConfigFromForm() {
      return {
        baseUrl: whisperBaseUrl.value.trim(),
        apiKey: whisperApiKey.value.trim(),
        model: whisperModel.value.trim() || 'whisper-1',
        language: whisperLanguage.value.trim() || 'ja',
        prompt: whisperPrompt.value.trim(),
        autoTranscribeOnStop: whisperAutoTranscribe.checked
      };
    }

    function getAiConfigFromForm() {
      return {
        engine: aiEngine.value,
        model: aiModel.value.trim(),
        customCmdPath: aiCustomCmdPath.value.trim(),
        autoGenerateMinutes: aiAutoGenerate.checked
      };
    }

    function updateConfigBadge(config) {
      if (config && config.serverUrl && config.projectId && config.personalAccessToken) {
        configSummaryBadge.textContent = '設定済み ✓';
        configSummaryBadge.className = 'text-[10px] px-2 py-0.5 rounded bg-emerald-900/60 text-emerald-300 border border-emerald-500/30';
      } else {
        configSummaryBadge.textContent = '未設定';
        configSummaryBadge.className = 'text-[10px] px-2 py-0.5 rounded bg-slate-800 text-slate-400';
      }
    }

    function updateWhisperBadge(config) {
      if (config && config.baseUrl) {
        whisperSummaryBadge.textContent = '設定済み ✓';
        whisperSummaryBadge.className = 'text-[10px] px-2 py-0.5 rounded bg-sky-900/60 text-sky-300 border border-sky-500/30';
      } else {
        whisperSummaryBadge.textContent = '未設定';
        whisperSummaryBadge.className = 'text-[10px] px-2 py-0.5 rounded bg-slate-800 text-slate-400';
      }
    }

    function updateAiBadge(config) {
      const eng = (config && config.engine) || 'copilot';
      aiSummaryBadge.textContent = eng === 'claude' ? 'Claude Code' : 'Copilot CLI';
      aiSummaryBadge.className = 'text-[10px] px-2 py-0.5 rounded bg-indigo-900/60 text-indigo-300 border border-indigo-500/30';
    }

    async function loadConfig() {
      try {
        const res = await fetch('/api/config');
        if (res.ok) {
          const cfg = await res.json();
          const gl = cfg.gitLab || {};
          gitlabUrl.value = gl.serverUrl || '';
          gitlabProjectId.value = gl.projectId || '';
          gitlabBranch.value = gl.branch || 'main';
          gitlabToken.value = gl.personalAccessToken || '';
          gitlabBasePath.value = gl.basePath || 'meetings';
          gitlabIgnoreSsl.checked = gl.ignoreSslErrors ?? true;
          gitlabAutoUpload.checked = gl.autoUploadOnStop ?? true;
          updateConfigBadge(gl);

          const wh = cfg.whisper || {};
          whisperBaseUrl.value = wh.baseUrl || '';
          whisperModel.value = wh.model || 'whisper-1';
          whisperLanguage.value = wh.language || 'ja';
          whisperApiKey.value = wh.apiKey || '';
          whisperPrompt.value = wh.prompt || '';
          whisperAutoTranscribe.checked = wh.autoTranscribeOnStop ?? true;
          updateWhisperBadge(wh);

          const ai = cfg.ai || {};
          aiEngine.value = ai.engine || 'copilot';
          aiModel.value = ai.model || '';
          aiCustomCmdPath.value = ai.customCmdPath || '';
          aiAutoGenerate.checked = ai.autoGenerateMinutes ?? true;
          updateAiBadge(ai);
        }

        // プロンプト取得
        const pRes = await fetch('/api/ai/prompt');
        if (pRes.ok) {
          const pData = await pRes.json();
          aiPromptText.value = pData.prompt || '';
          promptFilePathDisplay.textContent = '保存先: ' + (pData.filePath || '');
        }
      } catch (err) {
        console.warn('Config load error:', err);
      }
    }

    async function saveConfig() {
      const cfg = { 
        gitLab: getGitLabConfigFromForm(),
        whisper: getWhisperConfigFromForm(),
        ai: getAiConfigFromForm()
      };
      try {
        const res = await fetch('/api/config', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(cfg)
        });
        if (res.ok) {
          const msg = '✅ 設定を保存しました。';
          gitlabTestResult.className = 'text-[11px] text-emerald-400';
          gitlabTestResult.textContent = msg;
          whisperTestResult.className = 'text-[11px] text-emerald-400';
          whisperTestResult.textContent = msg;
          aiTestResult.className = 'text-[11px] text-emerald-400';
          aiTestResult.textContent = msg;
          updateConfigBadge(cfg.gitLab);
          updateWhisperBadge(cfg.whisper);
          updateAiBadge(cfg.ai);
        }
      } catch (err) {
        aiTestResult.className = 'text-[11px] text-rose-400';
        aiTestResult.textContent = '保存例外: ' + err.message;
      }
    }

    saveGitLabBtn.addEventListener('click', saveConfig);
    saveWhisperBtn.addEventListener('click', saveConfig);
    saveAiBtn.addEventListener('click', saveConfig);

    savePromptBtn.addEventListener('click', async () => {
      savePromptBtn.disabled = true;
      savePromptBtn.textContent = '保存中...';
      try {
        const res = await fetch('/api/ai/prompt', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ prompt: aiPromptText.value })
        });
        if (res.ok) {
          const data = await res.json();
          promptFilePathDisplay.textContent = '保存先: ' + data.filePath;
          logMessage.innerHTML = '<span class="text-emerald-400">✅ 議事録プロンプトを保存しました。</span>';
        }
      } catch (err) {
        logMessage.innerHTML = '<span class="text-rose-400">❌ プロンプト保存エラー: ' + err.message + '</span>';
      } finally {
        savePromptBtn.disabled = false;
        savePromptBtn.textContent = '💾 プロンプト保存';
      }
    });

    testGitLabBtn.addEventListener('click', async () => {
      const gl = getGitLabConfigFromForm();
      testGitLabBtn.disabled = true;
      testGitLabBtn.textContent = '接続テスト中...';
      try {
        const res = await fetch('/api/gitlab/test', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(gl)
        });
        const data = await res.json();
        gitlabTestResult.className = data.success ? 'text-[11px] text-emerald-400' : 'text-[11px] text-rose-400';
        gitlabTestResult.textContent = (data.success ? '✅ ' : '❌ ') + data.message;
      } catch (err) {
        gitlabTestResult.className = 'text-[11px] text-rose-400';
        gitlabTestResult.textContent = 'テスト例外: ' + err.message;
      } finally {
        testGitLabBtn.disabled = false;
        testGitLabBtn.textContent = '🔍 接続テスト';
      }
    });

    testWhisperBtn.addEventListener('click', async () => {
      const wh = getWhisperConfigFromForm();
      testWhisperBtn.disabled = true;
      testWhisperBtn.textContent = 'テスト中...';
      try {
        const res = await fetch('/api/whisper/test', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(wh)
        });
        const data = await res.json();
        whisperTestResult.className = data.success ? 'text-[11px] text-emerald-400' : 'text-[11px] text-rose-400';
        whisperTestResult.textContent = (data.success ? '✅ ' : '❌ ') + data.message;
      } catch (err) {
        whisperTestResult.className = 'text-[11px] text-rose-400';
        whisperTestResult.textContent = 'テスト例外: ' + err.message;
      } finally {
        testWhisperBtn.disabled = false;
        testWhisperBtn.textContent = '🔍 接続テスト';
      }
    });

    testAiBtn.addEventListener('click', async () => {
      const ai = getAiConfigFromForm();
      testAiBtn.disabled = true;
      testAiBtn.textContent = 'CLI探索中...';
      try {
        const res = await fetch('/api/ai/test', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(ai)
        });
        const data = await res.json();
        if (data.available) {
          aiTestResult.className = 'text-[11px] text-emerald-400';
          aiTestResult.textContent = `✅ ${data.engine} CLI 利用可能 (${data.version || ''}) - ${data.commandPath}`;
        } else {
          aiTestResult.className = 'text-[11px] text-rose-400';
          aiTestResult.textContent = `❌ ${data.engine} CLI 利用不可: ${data.error || '不明なエラー'}`;
        }
      } catch (err) {
        aiTestResult.className = 'text-[11px] text-rose-400';
        aiTestResult.textContent = 'テスト例外: ' + err.message;
      } finally {
        testAiBtn.disabled = false;
        testAiBtn.textContent = '🔍 CLI 疎通テスト';
      }
    });

    // パイプライン表示更新関数
    function renderPipeline(sessionId, pipeline) {
      if (!sessionId || !pipeline) {
        pipelineContainer.classList.add('hidden');
        return;
      }
      pipelineContainer.classList.remove('hidden');
      pipelineSessionLabel.textContent = `(${sessionId})`;

      function updateDot(dotElement, status, defaultText) {
        if (status === 'success') {
          dotElement.className = 'w-7 h-7 rounded-full bg-emerald-500 text-slate-900 flex items-center justify-center font-bold text-xs shadow-md border-2 border-slate-800';
          dotElement.textContent = '✓';
        } else if (status === 'running') {
          dotElement.className = 'w-7 h-7 rounded-full bg-amber-500 text-slate-900 flex items-center justify-center font-bold text-xs shadow-md border-2 border-slate-800 animate-spin';
          dotElement.textContent = '⟳';
        } else if (status === 'error') {
          dotElement.className = 'w-7 h-7 rounded-full bg-rose-600 text-white flex items-center justify-center font-bold text-xs shadow-md border-2 border-slate-800';
          dotElement.textContent = '!';
        } else if (status === 'skipped') {
          dotElement.className = 'w-7 h-7 rounded-full bg-slate-800 text-slate-500 flex items-center justify-center font-bold text-xs shadow-md border-2 border-slate-700';
          dotElement.textContent = '─';
        } else {
          dotElement.className = 'w-7 h-7 rounded-full bg-slate-700 text-slate-300 flex items-center justify-center font-bold text-xs shadow-md border-2 border-slate-800';
          dotElement.textContent = defaultText;
        }
      }

      updateDot(stepDotAudio, pipeline.audio || 'success', '1');
      updateDot(stepDotTranscribe, pipeline.transcribe || 'pending', '2');
      updateDot(stepDotMinutes, pipeline.minutes || 'pending', '3');
      updateDot(stepDotUpload, pipeline.upload || 'pending', '4');

      if (pipeline.errorMessage) {
        pipelineStatusText.className = 'text-[11px] text-rose-400 font-semibold';
        pipelineStatusText.textContent = 'エラー発生: ' + pipeline.errorMessage;
      } else if (pipeline.upload === 'success') {
        pipelineStatusText.className = 'text-[11px] text-emerald-400 font-semibold';
        pipelineStatusText.textContent = '✅ 全パイプライン完了！GitLab保存済み';
      } else if (pipeline.minutes === 'success') {
        pipelineStatusText.className = 'text-[11px] text-sky-400 font-semibold';
        pipelineStatusText.textContent = '✅ 議事録作成完了';
      } else {
        pipelineStatusText.className = 'text-[11px] text-indigo-300 animate-pulse';
        pipelineStatusText.textContent = 'パイプライン処理中...';
      }
    }

    // 指定ステップからのパイプライン再開
    async function runPipelineStep(step, targetSessionId = null) {
      const sid = targetSessionId || activeMinutesSessionId || currentSessionId;
      if (!sid) {
        alert('セッションが選択されていません。');
        return;
      }
      logMessage.innerHTML = `<span class="text-indigo-400 animate-pulse">⚡ セッション ${sid} の [${step}] ステップを実行中...</span>`;
      try {
        const res = await fetch('/api/pipeline/run-step', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sessionId: sid, step: step, autoFollow: true })
        });
        const data = await res.json();
        if (res.ok && data.success) {
          logMessage.innerHTML = `<span class="text-emerald-400 font-semibold">✅ ステップ [${step}] 完了！ ${data.message}</span>`;
          await loadPendingSessions();
          if (step === 'minutes' || data.minutes) {
            await viewMinutes(sid);
          }
        } else {
          logMessage.innerHTML = `<span class="text-rose-400">❌ ステップ [${step}] 失敗: ${data.message || data.error}</span>`;
          await loadPendingSessions();
        }
      } catch (err) {
        logMessage.innerHTML = `<span class="text-rose-400">❌ パイプライン例外: ${err.message}</span>`;
      }
    }

    // 議事録の表示
    async function viewMinutes(sessionId) {
      activeMinutesSessionId = sessionId;
      minutesSessionBadge.textContent = sessionId;
      chatEngineLabel.textContent = aiEngine.value === 'claude' ? 'Claude Code' : 'Copilot CLI';
      minutesTextDisplay.textContent = '議事録読み込み中...';
      minutesCard.classList.remove('hidden');

      try {
        const res = await fetch(`/api/spool/${sessionId}/minutes`);
        if (res.ok) {
          const text = await res.text();
          minutesTextDisplay.textContent = text;
        } else {
          minutesTextDisplay.textContent = '(議事録ファイル MINUTES.md はまだ作成されていません。「③ 議事録作成」を実行してください)';
        }
      } catch (err) {
        minutesTextDisplay.textContent = '議事録取得エラー: ' + err.message;
      }
    }

    closeMinutesBtn.addEventListener('click', () => {
      minutesCard.classList.add('hidden');
    });

    copyMinutesBtn.addEventListener('click', () => {
      navigator.clipboard.writeText(minutesTextDisplay.textContent);
      copyMinutesBtn.textContent = 'コピー完了 ✓';
      setTimeout(() => { copyMinutesBtn.textContent = '📋 コピー'; }, 2000);
    });

    // 議事録修正チャット送信
    chatSendBtn.addEventListener('click', async () => {
      const msg = chatInput.value.trim();
      if (!msg) return;
      if (!activeMinutesSessionId) {
        alert('対象セッションがありません。');
        return;
      }

      chatSendBtn.disabled = true;
      chatSendBtn.textContent = '修正中...';
      logMessage.innerHTML = `<span class="text-indigo-400 animate-pulse">💬 AIが議事録を修正中 (--resume ${activeMinutesSessionId})...</span>`;

      try {
        const res = await fetch('/api/ai/chat', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sessionId: activeMinutesSessionId, message: msg })
        });
        const data = await res.json();
        if (res.ok && data.success) {
          minutesTextDisplay.textContent = data.minutesMarkdown || '';
          chatInput.value = '';
          logMessage.innerHTML = `<span class="text-emerald-400 font-semibold">✅ 議事録の修正が完了しました！ (${data.elapsedMilliseconds}ms)</span>`;
          await loadPendingSessions();
        } else {
          logMessage.innerHTML = `<span class="text-rose-400">❌ 修正失敗: ${data.message || data.error}</span>`;
        }
      } catch (err) {
        logMessage.innerHTML = `<span class="text-rose-400">❌ 修正例外: ${err.message}</span>`;
      } finally {
        chatSendBtn.disabled = false;
        chatSendBtn.textContent = '修正依頼';
      }
    });

    // 未送信スプール一覧取得
    async function loadPendingSessions() {
      try {
        const res = await fetch('/api/sessions/pending');
        if (res.ok) {
          const sessions = await res.json();
          if (sessions && sessions.length > 0) {
            pendingSpoolArea.classList.remove('hidden');
            pendingList.innerHTML = '';
            sessions.forEach(s => {
              const item = document.createElement('div');
              item.className = 'flex flex-col sm:flex-row sm:items-center justify-between py-2 border-b border-amber-500/20 last:border-0 gap-2';

              const mb = (s.totalSizeBytes / (1024 * 1024)).toFixed(1);
              const audioTag = s.hasMp3 ? 'MP3' : (s.hasAudio ? 'WAV' : 'なし');
              const transcriptTag = s.hasTranscript ? '📝文字起こし済' : '未文字起こし';
              const minutesTag = s.hasMinutes ? '📋議事録済' : '未議事録';

              const pipe = s.pipeline || {};

              item.innerHTML = `
                <div>
                  <div class="flex items-center space-x-2">
                    <span class="font-mono font-bold text-amber-200">${s.sessionId}</span>
                    <span class="text-[10px] text-slate-400">(${mb} MB, ${s.screenshotCount}枚, ${audioTag})</span>
                  </div>
                  <div class="flex items-center space-x-1.5 mt-1 text-[10px]">
                    <span class="px-1.5 py-0.2 rounded ${s.hasTranscript ? 'bg-sky-900/60 text-sky-300' : 'bg-slate-800 text-slate-500'}">${transcriptTag}</span>
                    <span class="px-1.5 py-0.2 rounded ${s.hasMinutes ? 'bg-indigo-900/60 text-indigo-300' : 'bg-slate-800 text-slate-500'}">${minutesTag}</span>
                  </div>
                </div>
                <div class="flex items-center space-x-1.5 self-end sm:self-center">
                  <button onclick="viewMinutes('${s.sessionId}')" class="px-2 py-1 rounded bg-indigo-700/80 hover:bg-indigo-600 text-white text-[11px] font-medium transition">
                    ${s.hasMinutes ? '📋 議事録' : '🤖 議事録作成'}
                  </button>
                  <button onclick="runPipelineStep('transcribe', '${s.sessionId}')" class="px-2 py-1 rounded bg-sky-800/80 hover:bg-sky-700 text-sky-200 text-[11px] font-medium transition" title="文字起こし再実行">
                    🎙️
                  </button>
                  <button onclick="runPipelineStep('upload', '${s.sessionId}')" class="px-2.5 py-1 rounded bg-indigo-600 hover:bg-indigo-500 text-white text-[11px] font-semibold transition" title="GitLab送信">
                    ☁️ 送信
                  </button>
                  <button onclick="deleteSingleSession('${s.sessionId}')" class="px-2 py-1 rounded bg-rose-900/60 hover:bg-rose-800 text-rose-300 text-[11px] font-medium transition" title="ローカル一時ファイルを削除">
                    🗑️
                  </button>
                </div>
              `;
              pendingList.appendChild(item);
            });
          } else {
            pendingSpoolArea.classList.add('hidden');
          }
        }
      } catch (err) {
        console.warn('Pending load error:', err);
      }
    }

    async function deleteSingleSession(sessionId) {
      if (!confirm(`セッション ${sessionId} をローカルから削除しますか？`)) return;
      logMessage.innerHTML = `<span class="text-slate-400">🗑️ セッション ${sessionId} を削除中...</span>`;
      try {
        const res = await fetch(`/api/sessions/${sessionId}`, { method: 'DELETE' });
        if (res.ok) {
          logMessage.innerHTML = `<span class="text-slate-300">🗑️ セッション ${sessionId} を削除しました。</span>`;
          await loadPendingSessions();
          if (activeMinutesSessionId === sessionId) {
            minutesCard.classList.add('hidden');
          }
        }
      } catch (err) {
        logMessage.innerHTML = `<span class="text-rose-400">❌ 削除例外: ${err.message}</span>`;
      }
    }

    refreshPendingBtn.addEventListener('click', loadPendingSessions);

    async function loadDevicesAndScreens() {
      try {
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

      clearInterval(statusInterval);
      statusInterval = setInterval(async () => {
        try {
          const res = await fetch('/api/status');
          if (res.ok) {
            const data = await res.json();
            const count = data.capturedCount ?? data.capturedScreenshots ?? 0;
            screenshotCountDisplay.innerHTML = `${count} <span class="text-xs font-normal text-slate-400">枚</span>`;
            
            // 音声レベルメーターのリアルタイム更新
            const levelPct = Math.round((data.audioLevel || 0) * 100);
            audioLevelBar.style.width = Math.min(100, levelPct * 1.5) + '%';
            if (levelPct > 3) {
              audioLevelBar.className = 'h-full bg-emerald-400 rounded-full transition-all duration-100 shadow-[0_0_8px_rgba(52,211,153,0.7)]';
              audioLevelText.textContent = `${levelPct}% 🔊`;
              audioLevelText.className = 'text-[10px] text-emerald-400 font-mono mt-0.5 font-bold';
            } else {
              audioLevelBar.className = 'h-full bg-slate-600 rounded-full transition-all duration-100';
              audioLevelText.textContent = '無音';
              audioLevelText.className = 'text-[10px] text-slate-400 font-mono mt-0.5';
            }
          }
        } catch { /* ignore */ }
      }, 500);
    }

    function stopTimer() {
      clearInterval(timerInterval);
      clearInterval(statusInterval);
      audioLevelBar.style.width = '0%';
      audioLevelText.textContent = '待機';
      audioLevelText.className = 'text-[10px] text-slate-500 font-mono mt-0.5';
    }

    async function startRecording() {
      const playbackId = playbackSelect.value || null;
      const captureId = captureSelect.value || null;
      const screenVal = screenSelect.value;
      const enableScreen = screenVal !== "off";
      const monitorIndex = enableScreen ? parseInt(screenVal, 10) : 0;

      toggleBtn.disabled = true;
      btnText.textContent = '起動中...';

      try {
        const res = await fetch('/api/record/start', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            playbackDeviceId: playbackId,
            captureDeviceId: captureId,
            monitorIndex: monitorIndex,
            enableScreenCapture: enableScreen
          })
        });

        const data = await res.json();
        if (res.ok) {
          isRecording = true;
          currentSessionId = data.sessionId;
          activeMinutesSessionId = currentSessionId;
          statusBadge.textContent = '● 記録中';
          statusBadge.className = 'px-3 py-1 rounded-full text-xs font-semibold bg-red-500/20 text-red-400 border border-red-500/30 animate-pulse';
          
          toggleBtn.className = 'w-full py-4 rounded-xl font-bold text-lg shadow-lg transition-all duration-200 bg-slate-700 hover:bg-slate-600 text-white flex items-center justify-center space-x-2';
          btnIcon.className = 'inline-block w-4 h-4 rounded bg-red-400';
          btnText.textContent = '会議の記録を停止する';

          if (enableScreen) {
            manualScreenshotBtn.classList.remove('hidden');
          }

          startTimer();
          logMessage.innerHTML = `<span class="text-slate-300">セッション <span class="font-mono text-indigo-400">${data.sessionId}</span> 記録開始。相手の声・自分の声・スライド変化を記録中...</span>`;
          
          pipelineContainer.classList.add('hidden');
          minutesCard.classList.add('hidden');
        } else {
          logMessage.innerHTML = `<span class="text-rose-400">❌ 開始エラー: ${data.message || '不明なエラー'}</span>`;
        }
      } catch (err) {
        logMessage.innerHTML = `<span class="text-rose-400">❌ 通信例外: ${err.message}</span>`;
      } finally {
        toggleBtn.disabled = false;
      }
    }

    async function stopRecording() {
      toggleBtn.disabled = true;
      btnText.textContent = '処理中 (MP3圧縮 ＆ 文字起こし ＆ 議事録)...';
      manualScreenshotBtn.classList.add('hidden');

      try {
        const res = await fetch('/api/record/stop', { method: 'POST' });
        const data = await res.json();
        
        stopTimer();
        isRecording = false;

        statusBadge.textContent = '待機中';
        statusBadge.className = 'px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30';

        toggleBtn.className = 'w-full py-4 rounded-xl font-bold text-lg shadow-lg transition-all duration-200 bg-red-600 hover:bg-red-500 text-white flex items-center justify-center space-x-2';
        btnIcon.className = 'inline-block w-4 h-4 rounded-full bg-white animate-pulse';
        btnText.textContent = '会議の記録を開始する';

        if (res.ok) {
          const sid = data.sessionId;
          renderPipeline(sid, data.pipeline || { audio: 'success' });

          let msg = `✅ 記録完了: スクショ ${data.capturedCount} 枚`;
          if (data.mp3Conversion && data.mp3Conversion.success) {
            msg += ` 🎵 MP3化 (${data.mp3Conversion.compressionRatioPercent.toFixed(1)}%減)`;
          }
          if (data.whisperTranscription && data.whisperTranscription.success) {
            msg += ` 🎙️ 文字起こし完了`;
          }
          if (data.aiMinutes && data.aiMinutes.success) {
            msg += ` 🤖 AI議事録生成完了！`;
            viewMinutes(sid);
          }
          if (data.gitLabUpload && data.gitLabUpload.success) {
            const link = data.gitLabUpload.webUrl ? `<a href="${data.gitLabUpload.webUrl}" target="_blank" class="underline text-indigo-400 font-semibold ml-1">コミットを開く ↗</a>` : '';
            msg += ` ☁️ GitLab自動コミット完了！ ${link}`;
          }
          logMessage.innerHTML = `<span class="text-emerald-400 font-medium">${msg}</span>`;
        } else {
          logMessage.innerHTML = `<span class="text-rose-400">❌ 停止処理エラー: ${data.message || '不明なエラー'}</span>`;
        }
      } catch (err) {
        logMessage.innerHTML = `<span class="text-rose-400">❌ 通信例外: ${err.message}</span>`;
      } finally {
        toggleBtn.disabled = false;
        await loadPendingSessions();
      }
    }

    manualScreenshotBtn.addEventListener('click', async () => {
      manualScreenshotBtn.disabled = true;
      try {
        const res = await fetch('/api/record/screenshot', { method: 'POST' });
        const data = await res.json();
        if (res.ok) {
          logMessage.innerHTML = `<span class="text-indigo-300">📸 手動スクショ撮影成功: <span class="font-mono">${data.capture.fileName}</span></span>`;
        }
      } catch (err) {
        logMessage.innerHTML = `<span class="text-rose-400">❌ スクショ撮影エラー: ${err.message}</span>`;
      } finally {
        manualScreenshotBtn.disabled = false;
      }
    });

    toggleBtn.addEventListener('click', () => {
      if (!isRecording) {
        startRecording();
      } else {
        stopRecording();
      }
    });

    shutdownBtn.addEventListener('click', async () => {
      if (confirm('WinMeetingRecorder アプリケーションを終了しますか？')) {
        try {
          shutdownBtn.disabled = true;
          shutdownBtn.textContent = '終了中...';
          await fetch('/api/app/shutdown', { method: 'POST' });
          logMessage.innerHTML = '<span class="text-amber-400">👋 アプリケーションを安全に終了しました。ブラウザタブを閉じてください。</span>';
          setTimeout(() => { window.close(); }, 1500);
        } catch {
          window.close();
        }
      }
    });

    // 初期化ロード
    loadDevicesAndScreens();
    loadConfig();
    loadPendingSessions();

    // タスクトレイ操作等とのバックグラウンド状態同期 (2秒間隔)
    setInterval(async () => {
      if (toggleBtn.disabled) return;
      try {
        const res = await fetch('/api/status');
        if (res.ok) {
          const st = await res.json();
          if (st.isRecording && !isRecording) {
            // タスクトレイ側で録音開始された
            isRecording = true;
            currentSessionId = st.sessionId;
            activeMinutesSessionId = currentSessionId;
            statusBadge.textContent = '● 記録中';
            statusBadge.className = 'px-3 py-1 rounded-full text-xs font-semibold bg-red-500/20 text-red-400 border border-red-500/30 animate-pulse';
            toggleBtn.className = 'w-full py-4 rounded-xl font-bold text-lg shadow-lg transition-all duration-200 bg-slate-700 hover:bg-slate-600 text-white flex items-center justify-center space-x-2';
            btnIcon.className = 'inline-block w-4 h-4 rounded bg-red-400';
            btnText.textContent = '会議の記録を停止する';
            manualScreenshotBtn.classList.remove('hidden');
            startTimer();
          } else if (!st.isRecording && isRecording) {
            // タスクトレイ側で録音停止された
            stopTimer();
            isRecording = false;
            statusBadge.textContent = '待機中';
            statusBadge.className = 'px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30';
            toggleBtn.className = 'w-full py-4 rounded-xl font-bold text-lg shadow-lg transition-all duration-200 bg-red-600 hover:bg-red-500 text-white flex items-center justify-center space-x-2';
            btnIcon.className = 'inline-block w-4 h-4 rounded-full bg-white animate-pulse';
            btnText.textContent = '会議の記録を開始する';
            manualScreenshotBtn.classList.add('hidden');
            await loadPendingSessions();
          }
        }
      } catch { /* ignore */ }
    }, 2000);
  </script>
</body>
</html>
""";
}
