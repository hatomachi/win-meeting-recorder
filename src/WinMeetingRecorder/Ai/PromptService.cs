using System.Text;
using WinMeetingRecorder.Spool;
using WinMeetingRecorder.Whisper;

namespace WinMeetingRecorder.Ai;

/// <summary>
/// 議事録作成プロンプトの読み書き・変数展開サービス
/// (exe同フォルダ または %AppData%\WinMeetingRecorder\meeting_minutes_prompt.txt)
/// </summary>
public class PromptService
{
    private const string PromptFileName = "meeting_minutes_prompt.txt";
    private readonly string _primaryFilePath;
    private readonly string _fallbackAppDataPath;
    private readonly object _lock = new();

    public PromptService()
    {
        // 1. exe同フォルダ
        _primaryFilePath = Path.Combine(AppContext.BaseDirectory, PromptFileName);

        // 2. %AppData% フォルダ
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            appData = AppContext.BaseDirectory;
        }
        var appDataDir = Path.Combine(appData, "WinMeetingRecorder");
        Directory.CreateDirectory(appDataDir);
        _fallbackAppDataPath = Path.Combine(appDataDir, PromptFileName);

        // 初回ファイルが一切存在しない場合、デフォルトを生成
        EnsureDefaultPromptFile();
    }

    /// <summary>
    /// 現在有効なプロンプトファイルの絶対パス
    /// </summary>
    public string ActivePromptFilePath
    {
        get
        {
            if (File.Exists(_primaryFilePath)) return _primaryFilePath;
            return _fallbackAppDataPath;
        }
    }

    /// <summary>
    /// プロンプトテキストを読み込みます
    /// </summary>
    public string LoadPrompt()
    {
        lock (_lock)
        {
            try
            {
                var path = ActivePromptFilePath;
                if (File.Exists(path))
                {
                    return File.ReadAllText(path, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PromptService] プロンプト読み込み警告: {ex.Message}");
            }

            return GetDefaultPromptTemplate();
        }
    }

    /// <summary>
    /// プロンプトテキストを保存します（Web UIからの編集用）
    /// </summary>
    public void SavePrompt(string promptText)
    {
        lock (_lock)
        {
            var targetPath = File.Exists(_primaryFilePath) ? _primaryFilePath : _fallbackAppDataPath;
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(targetPath, promptText, Encoding.UTF8);
            Console.WriteLine($"[PromptService] 💾 プロンプトを保存しました: {targetPath}");
        }
    }

    /// <summary>
    /// セッション情報と文字起こし結果を埋め込んだ実行用プロンプトを生成します
    /// </summary>
    public string FormatPrompt(
        MeetingSessionMetadata metadata, 
        WhisperTranscriptionResponse? transcript)
    {
        var rawPrompt = LoadPrompt();

        var transcriptText = transcript?.Text?.Trim() ?? "(文字起こしデータなし)";
        var dur = TimeSpan.FromSeconds(metadata.DurationSeconds);
        var durationText = $"{(int)dur.TotalMinutes}分 {dur.Seconds:D2}秒";
        var startTimeText = metadata.StartTime.ToString("yyyy-MM-dd HH:mm:ss");

        var imageListSb = new StringBuilder();
        if (metadata.Images != null && metadata.Images.Count > 0)
        {
            foreach (var img in metadata.Images)
            {
                imageListSb.AppendLine($"- {img.RelativePath} (経過時間: {img.ElapsedSeconds}秒)");
            }
        }
        else
        {
            imageListSb.AppendLine("(画面キャプチャなし)");
        }

        var result = rawPrompt
            .Replace("{{TRANSCRIPT_TEXT}}", transcriptText)
            .Replace("{{IMAGE_COUNT}}", (metadata.Images?.Count ?? 0).ToString())
            .Replace("{{IMAGE_LIST}}", imageListSb.ToString().TrimEnd())
            .Replace("{{SESSION_ID}}", metadata.SessionId)
            .Replace("{{START_TIME}}", startTimeText)
            .Replace("{{DURATION}}", durationText);

        return result;
    }

    private void EnsureDefaultPromptFile()
    {
        try
        {
            if (!File.Exists(_primaryFilePath) && !File.Exists(_fallbackAppDataPath))
            {
                File.WriteAllText(_fallbackAppDataPath, GetDefaultPromptTemplate(), Encoding.UTF8);
                Console.WriteLine($"[PromptService] 📄 デフォルトプロンプトファイルを生成しました: {_fallbackAppDataPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PromptService] デフォルトプロンプト生成警告: {ex.Message}");
        }
    }

    public static string GetDefaultPromptTemplate()
    {
        return """
あなたはプロのビジネスファシリテーター兼テクニカルライターです。
添付された会議の「文字起こしテキスト」および「会議中の画面キャプチャ（スライド画像）」を深く読み解き、関係者全員がひと目で内容を把握できる高品質な会議議事録（Markdown形式）を作成してください。

### 【会議の基本情報】
- セッションID: {{SESSION_ID}}
- 会議開始日時: {{START_TIME}}
- 録音時間: {{DURATION}}
- スライド画像枚数: {{IMAGE_COUNT}} 枚 (作業ディレクトリの images/ フォルダに保存されています)

### 【スライド画像一覧】
{{IMAGE_LIST}}

### 【文字起こしテキスト】
{{TRANSCRIPT_TEXT}}

---

### 【作成要件・フォーマット】
以下の構成で、純粋なMarkdown形式（全体のコードブロック等は不要）で出力してください。

# 📋 会議議事録: {{START_TIME}}

## 1. 全体サマリー（Executive Summary）
- 会議の背景、全体の目的、および主要な結論を3〜5行程度で端的に要約してください。

## 2. アジェンダ別 議論内容 ＆ 決定事項
- 会議で扱われたテーマごとに見出しを設け、議論の要点と合意された「決定事項（Decisions）」を明確に箇条書きしてください。
- スライド画像が参照されている場合は、スライドの数値や仕様、図の要点を文章に補完してください。

## 3. 未決事項・検討課題（Open Issues）
- 会議中に結論が出なかった論点や、次回検討となった課題を記載してください（特になければ「なし」と明記）。

## 4. アクションアイテム / ToDo 一覧
- 会議から発生したタスクを表形式で整理してください：
| No | タスク内容 | 担当者（発言から推測） | 期限・マイルストーン |
|:---:|:---|:---:|:---:|

※ 丁寧かつ明瞭な日本語（です・ます調、または箇条書きの体言止め）でまとめてください。
""".Trim();
    }
}
