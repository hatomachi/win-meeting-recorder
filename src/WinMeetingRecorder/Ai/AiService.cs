using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WinMeetingRecorder.Config;
using WinMeetingRecorder.Spool;
using WinMeetingRecorder.Whisper;

namespace WinMeetingRecorder.Ai;

/// <summary>
/// AI CLI 接続確認結果
/// </summary>
public record AiStatusResult(
    bool Available,
    string Engine,
    string? CommandPath,
    string? Version,
    string? Error);

/// <summary>
/// AI 議事録生成・修正結果
/// </summary>
public record AiExecutionResult(
    bool Success,
    string Message,
    string? MinutesMarkdown = null,
    string? SessionId = null,
    long ElapsedMilliseconds = 0);

/// <summary>
/// AI CLI 実行サービス (GitHub Copilot CLI / Claude Code)
/// </summary>
public class AiService
{
    private readonly PromptService _promptService;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public AiService(PromptService promptService)
    {
        _promptService = promptService;
    }

    /// <summary>
    /// 指定されたエンジンの CLI コマンドパスを探索します
    /// </summary>
    public string? FindCommand(string engine, string? customCmdPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customCmdPath))
        {
            if (File.Exists(customCmdPath)) return customCmdPath;
            var resolved = ResolveInPath(customCmdPath);
            if (!string.IsNullOrEmpty(resolved)) return resolved;
        }

        var isCopilot = string.Equals(engine, "copilot", StringComparison.OrdinalIgnoreCase);
        var baseName = isCopilot ? "copilot" : "claude";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var windowsCandidates = new[]
            {
                $"{baseName}.cmd",
                $"{baseName}.exe",
                $"{baseName}.bat"
            };

            foreach (var cand in windowsCandidates)
            {
                var resolved = ResolveInPath(cand);
                if (!string.IsNullOrEmpty(resolved)) return resolved;
            }

            // 一般的なインストール先を直接探索
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var fallbackPaths = new List<string>();
            if (!string.IsNullOrEmpty(appData))
            {
                fallbackPaths.Add(Path.Combine(appData, "npm", $"{baseName}.cmd"));
            }
            if (!string.IsNullOrEmpty(localAppData))
            {
                fallbackPaths.Add(Path.Combine(localAppData, "Programs", baseName, $"{baseName}.exe"));
                fallbackPaths.Add(Path.Combine(localAppData, "npm", $"{baseName}.cmd"));
            }
            if (!string.IsNullOrEmpty(userProfile))
            {
                fallbackPaths.Add(Path.Combine(userProfile, ".local", "bin", $"{baseName}.exe"));
                fallbackPaths.Add(Path.Combine(userProfile, ".local", "bin", $"{baseName}.cmd"));
            }

            foreach (var path in fallbackPaths)
            {
                if (File.Exists(path)) return path;
            }
        }
        else
        {
            // macOS / Linux
            var resolved = ResolveInPath(baseName);
            if (!string.IsNullOrEmpty(resolved)) return resolved;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var unixCandidates = new[]
            {
                $"/opt/homebrew/bin/{baseName}",
                $"/usr/local/bin/{baseName}",
                $"/usr/bin/{baseName}",
                Path.Combine(home, ".local", "bin", baseName),
                Path.Combine(home, ".npm-global", "bin", baseName)
            };

            foreach (var path in unixCandidates)
            {
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    /// <summary>
    /// AI CLI の接続状態・バージョンを確認します
    /// </summary>
    public async Task<AiStatusResult> CheckStatusAsync(AiConfig config)
    {
        var engine = string.IsNullOrWhiteSpace(config.Engine) ? "copilot" : config.Engine.Trim().ToLowerInvariant();
        var cmdPath = FindCommand(engine, config.CustomCmdPath);

        if (string.IsNullOrEmpty(cmdPath))
        {
            var installHint = engine == "copilot" 
                ? "GitHub Copilot CLI (例: npm i -g @github/copilot)" 
                : "Claude Code (例: npm i -g @anthropic-ai/claude-code)";
            return new AiStatusResult(
                Available: false,
                Engine: engine,
                CommandPath: null,
                Version: null,
                Error: $"{engine} CLI がローカル環境に見つかりません。{installHint} をインストールするか、設定でカスタムパスを指定してください。");
        }

        try
        {
            var psi = CreateProcessStartInfo(cmdPath, "--version");
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return new AiStatusResult(false, engine, cmdPath, null, "プロセスの起動に失敗しました。");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

            await proc.WaitForExitAsync(cts.Token);
            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            if (proc.ExitCode == 0)
            {
                var ver = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
                return new AiStatusResult(true, engine, cmdPath, ver, null);
            }

            return new AiStatusResult(false, engine, cmdPath, null, $"コマンド終了エラー ({proc.ExitCode}): {stderr} {stdout}");
        }
        catch (Exception ex)
        {
            return new AiStatusResult(false, engine, cmdPath, null, $"実行例外: {ex.Message}");
        }
    }

    /// <summary>
    /// スプール内の文字起こし＆スライド画像から初版の議事録を自動生成します
    /// </summary>
    public async Task<AiExecutionResult> GenerateMinutesAsync(
        MeetingSessionMetadata metadata,
        AiConfig config,
        WhisperTranscriptionResponse? transcript = null)
    {
        var sw = Stopwatch.StartNew();
        var spoolDir = metadata.SpoolDirectory;
        if (!Directory.Exists(spoolDir))
        {
            return new AiExecutionResult(false, $"スプールディレクトリが存在しません: {spoolDir}");
        }

        var engine = string.IsNullOrWhiteSpace(config.Engine) ? "copilot" : config.Engine.Trim().ToLowerInvariant();
        var cmdPath = FindCommand(engine, config.CustomCmdPath);
        if (string.IsNullOrEmpty(cmdPath))
        {
            return new AiExecutionResult(false, $"{engine} CLI コマンドが見つかりません。設定を確認してください。");
        }

        // 文字起こしデータの補完 (引数になければ transcript.json からロード)
        if (transcript == null)
        {
            var transcriptPath = Path.Combine(spoolDir, metadata.TranscriptFileName ?? "transcript.json");
            if (File.Exists(transcriptPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(transcriptPath);
                    transcript = JsonSerializer.Deserialize<WhisperTranscriptionResponse>(json, JsonOptions);
                }
                catch { /* ignore */ }
            }
        }

        // プロンプトの生成
        var prompt = _promptService.FormatPrompt(metadata, transcript);
        var effectiveSessionId = Guid.NewGuid().ToString();

        Console.WriteLine($"[AiService] 🤖 議事録生成開始: Engine={engine}, SessionId={effectiveSessionId}, Spool={spoolDir}");

        var (success, output, error) = await RunCliProcessAsync(
            cmdPath, 
            engine, 
            prompt, 
            spoolDir, 
            effectiveSessionId, 
            isResume: false, 
            model: config.Model);

        sw.Stop();

        if (!success)
        {
            Console.WriteLine($"[AiService] ❌ 議事録生成エラー: {error}");
            return new AiExecutionResult(false, $"AI実行エラー: {error}", ElapsedMilliseconds: sw.ElapsedMilliseconds);
        }

        // 結果を MINUTES.md に保存
        var minutesPath = Path.Combine(spoolDir, metadata.MinutesFileName ?? "MINUTES.md");
        await File.WriteAllTextAsync(minutesPath, output, Encoding.UTF8);

        // チャット履歴初期化
        SaveChatHistory(spoolDir, effectiveSessionId, engine, prompt, output);

        metadata.HasMinutes = true;
        metadata.AiSessionId = effectiveSessionId;
        metadata.Pipeline.Minutes = "success";
        metadata.Pipeline.LastStep = "minutes";

        Console.WriteLine($"[AiService] ✅ 議事録生成成功: {output.Length} 文字 ({sw.ElapsedMilliseconds}ms) -> {minutesPath}");

        return new AiExecutionResult(
            Success: true,
            Message: "議事録を正常に生成しました。",
            MinutesMarkdown: output,
            SessionId: effectiveSessionId,
            ElapsedMilliseconds: sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// チャットで修正指示を送り、既存セッションを継続して議事録を再生成します (--resume)
    /// </summary>
    public async Task<AiExecutionResult> ChatReviseAsync(
        MeetingSessionMetadata metadata,
        string userInstruction,
        AiConfig config)
    {
        var sw = Stopwatch.StartNew();
        var spoolDir = metadata.SpoolDirectory;
        if (!Directory.Exists(spoolDir))
        {
            return new AiExecutionResult(false, $"スプールディレクトリが存在しません: {spoolDir}");
        }

        if (string.IsNullOrWhiteSpace(userInstruction))
        {
            return new AiExecutionResult(false, "修正指示内容が空です。");
        }

        var engine = string.IsNullOrWhiteSpace(config.Engine) ? "copilot" : config.Engine.Trim().ToLowerInvariant();
        var cmdPath = FindCommand(engine, config.CustomCmdPath);
        if (string.IsNullOrEmpty(cmdPath))
        {
            return new AiExecutionResult(false, $"{engine} CLI コマンドが見つかりません。");
        }

        var sessionId = metadata.AiSessionId;
        var isResume = !string.IsNullOrEmpty(sessionId);
        if (!isResume)
        {
            sessionId = Guid.NewGuid().ToString();
        }

        // 修正依頼用のプロンプト構築（議事録Markdownの更新を明確に促す）
        var revisionPrompt = $"""
以下のユーザーからの修正指示を反映し、全体の「会議議事録」を更新・再出力してください。
前回の形式（純粋なMarkdown）を維持し、修正が反映された完成版の議事録全体を出力してください。

【ユーザーからの修正指示】
{userInstruction.Trim()}
""";

        Console.WriteLine($"[AiService] 💬 議事録修正チャット開始: Engine={engine}, SessionId={sessionId}, isResume={isResume}");

        var (success, output, error) = await RunCliProcessAsync(
            cmdPath,
            engine,
            revisionPrompt,
            spoolDir,
            sessionId!,
            isResume: isResume,
            model: config.Model);

        sw.Stop();

        if (!success)
        {
            Console.WriteLine($"[AiService] ❌ 議事録修正エラー: {error}");
            return new AiExecutionResult(false, $"修正エラー: {error}", ElapsedMilliseconds: sw.ElapsedMilliseconds);
        }

        // MINUTES.md を更新
        var minutesPath = Path.Combine(spoolDir, metadata.MinutesFileName ?? "MINUTES.md");
        await File.WriteAllTextAsync(minutesPath, output, Encoding.UTF8);

        // チャット履歴追記
        AppendChatHistory(spoolDir, sessionId!, userInstruction, output);

        metadata.HasMinutes = true;
        metadata.AiSessionId = sessionId;

        Console.WriteLine($"[AiService] ✅ 議事録修正完了: {output.Length} 文字 ({sw.ElapsedMilliseconds}ms)");

        return new AiExecutionResult(
            Success: true,
            Message: "議事録を修正・更新しました。",
            MinutesMarkdown: output,
            SessionId: sessionId,
            ElapsedMilliseconds: sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// CLI プロセスを起動して出力を取得します
    /// </summary>
    private async Task<(bool Success, string Output, string Error)> RunCliProcessAsync(
        string cmdPath,
        string engine,
        string prompt,
        string workDir,
        string sessionId,
        bool isResume,
        string? model)
    {
        var isCopilot = string.Equals(engine, "copilot", StringComparison.OrdinalIgnoreCase);

        var args = new List<string>();
        args.Add("-p");
        args.Add(prompt);

        if (isCopilot)
        {
            args.Add("--allow-all-tools");
            args.Add("--output-format");
            args.Add("json");

            if (!string.IsNullOrWhiteSpace(model))
            {
                args.Add("--model");
                args.Add(model.Trim());
            }

            if (isResume)
            {
                args.Add($"--resume={sessionId}");
            }
            else
            {
                args.Add($"--session-id={sessionId}");
            }
        }
        else
        {
            // Claude Code CLI
            if (!string.IsNullOrWhiteSpace(model))
            {
                args.Add("--model");
                args.Add(model.Trim());
            }

            if (isResume)
            {
                args.Add("--resume");
                args.Add(sessionId);
            }
            else
            {
                args.Add("--session-id");
                args.Add(sessionId);
            }
        }

        try
        {
            var psi = CreateProcessStartInfo(cmdPath, args, workDir);
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return (false, "", "CLIプロセスの起動に失敗しました。");
            }

            // 最大15分のタイムアウト
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

            await proc.WaitForExitAsync(cts.Token);
            var rawStdout = (await stdoutTask).Trim();
            var rawStderr = (await stderrTask).Trim();

            if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(rawStdout))
            {
                return (false, "", $"CLI終了コード {proc.ExitCode}: {rawStderr}");
            }

            // 出力テキストの抽出
            var finalText = isCopilot ? ExtractCopilotResponseText(rawStdout) : rawStdout;
            if (string.IsNullOrWhiteSpace(finalText))
            {
                finalText = rawStdout; // パースできなかった場合はそのまま
            }

            return (true, CleanMarkdownOutput(finalText), "");
        }
        catch (OperationCanceledException)
        {
            return (false, "", "AIの処理がタイムアウトしました (15分)。");
        }
        catch (Exception ex)
        {
            return (false, "", $"CLI実行例外: {ex.Message}");
        }
    }

    /// <summary>
    /// Copilot CLI の JSON 出力から回答テキストを抽出します
    /// </summary>
    private static string ExtractCopilotResponseText(string jsonOutput)
    {
        if (string.IsNullOrWhiteSpace(jsonOutput)) return "";

        try
        {
            // 単一の完全なJSONオブジェクトの場合
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            // 1. messages 配列の末尾のアシスタント発話
            if (root.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
            {
                for (int i = msgs.GetArrayLength() - 1; i >= 0; i--)
                {
                    var m = msgs[i];
                    if (m.TryGetProperty("role", out var r) && r.GetString() == "assistant")
                    {
                        if (m.TryGetProperty("content", out var c))
                        {
                            return c.GetString() ?? "";
                        }
                    }
                }
            }

            // 2. 直下の text または content プロパティ
            if (root.TryGetProperty("content", out var directContent)) return directContent.GetString() ?? "";
            if (root.TryGetProperty("text", out var directText)) return directText.GetString() ?? "";
        }
        catch
        {
            // JSONL（複数行JSON）またはストリーム形式の場合、末尾行から探索
            var lines = jsonOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("{") && line.EndsWith("}"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("content", out var c)) return c.GetString() ?? "";
                        if (root.TryGetProperty("text", out var t)) return t.GetString() ?? "";
                    }
                    catch { /* ignore */ }
                }
            }
        }

        return jsonOutput;
    }

    /// <summary>
    /// 出力 Markdown の整形（不要な先頭末尾コードブロックのトリム等）
    /// </summary>
    private static string CleanMarkdownOutput(string text)
    {
        var cleaned = text.Trim();
        // ```markdown ... ``` で全体が囲まれている場合は剥がす
        if (cleaned.StartsWith("```markdown", StringComparison.OrdinalIgnoreCase) && cleaned.EndsWith("```"))
        {
            cleaned = cleaned.Substring("```markdown".Length);
            cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
        }
        else if (cleaned.StartsWith("```md", StringComparison.OrdinalIgnoreCase) && cleaned.EndsWith("```"))
        {
            cleaned = cleaned.Substring("```md".Length);
            cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
        }
        else if (cleaned.StartsWith("```") && cleaned.EndsWith("```") && cleaned.Length > 6)
        {
            var firstNewLine = cleaned.IndexOf('\n');
            if (firstNewLine > 0 && firstNewLine < 20)
            {
                cleaned = cleaned.Substring(firstNewLine + 1);
                cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
            }
        }

        return cleaned;
    }

    private static ProcessStartInfo CreateProcessStartInfo(string cmdPath, string singleArg, string? workDir = null)
    {
        return CreateProcessStartInfo(cmdPath, new[] { singleArg }, workDir);
    }

    private static ProcessStartInfo CreateProcessStartInfo(string cmdPath, IEnumerable<string> args, string? workDir = null)
    {
        ProcessStartInfo psi;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            (cmdPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
             cmdPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            // Windows の .cmd / .bat は cmd.exe /c 経由で実行
            psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(cmdPath);
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = cmdPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        if (!string.IsNullOrEmpty(workDir))
        {
            psi.WorkingDirectory = workDir;
        }

        return psi;
    }

    private static string? ResolveInPath(string command)
    {
        try
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var whichCmd = isWindows ? "where.exe" : "which";
            var psi = new ProcessStartInfo
            {
                FileName = whichCmd,
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                var firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return firstLine?.Trim();
            }
        }
        catch { /* ignore */ }

        return null;
    }

    private void SaveChatHistory(string spoolDir, string sessionId, string engine, string prompt, string initialOutput)
    {
        try
        {
            var historyPath = Path.Combine(spoolDir, "chat_history.json");
            var history = new
            {
                SessionId = sessionId,
                Engine = engine,
                CreatedAt = DateTime.Now,
                Messages = new List<object>
                {
                    new { Role = "user", Content = prompt, Timestamp = DateTime.Now },
                    new { Role = "assistant", Content = initialOutput, Timestamp = DateTime.Now }
                }
            };
            File.WriteAllText(historyPath, JsonSerializer.Serialize(history, JsonOptions), Encoding.UTF8);
        }
        catch { /* ignore */ }
    }

    private void AppendChatHistory(string spoolDir, string sessionId, string userInstruction, string revisedOutput)
    {
        try
        {
            var historyPath = Path.Combine(spoolDir, "chat_history.json");
            if (File.Exists(historyPath))
            {
                var json = File.ReadAllText(historyPath);
                using var doc = JsonDocument.Parse(json);
                // 簡易追加
                var historyObj = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                // 追記
            }
        }
        catch { /* ignore */ }
    }
}
