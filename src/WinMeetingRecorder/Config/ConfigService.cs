using System.Text.Json;

namespace WinMeetingRecorder.Config;

/// <summary>
/// アプリケーション設定の読み書きサービス (%AppData%\WinMeetingRecorder\config.json)
/// </summary>
public class ConfigService
{
    private readonly string _configFilePath;
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ConfigService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            appData = AppContext.BaseDirectory;
        }

        var dir = Path.Combine(appData, "WinMeetingRecorder");
        Directory.CreateDirectory(dir);
        _configFilePath = Path.Combine(dir, "config.json");
    }

    public string ConfigPath => _configFilePath;

    /// <summary>
    /// exeと同階層のディレクトリパスを取得 (単一exe実行時も確実にexe本体の配置フォルダを取得)
    /// </summary>
    public static string GetExeDirectory()
    {
        if (!string.IsNullOrEmpty(Environment.ProcessPath))
        {
            var pDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(pDir)) return pDir;
        }
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// exeと同階層に配置されたローカル設定ファイル (config.json または config.default.json) を探索・読み込み
    /// </summary>
    public AppConfig? LoadBundledConfig()
    {
        var exeDir = GetExeDirectory();
        var candidates = new[]
        {
            Path.Combine(exeDir, "config.json"),
            Path.Combine(exeDir, "config.default.json")
        };

        foreach (var path in candidates)
        {
            // %AppData% の保存先と同一ファイルパスならスキップ
            if (string.Equals(path, _configFilePath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                    if (cfg != null)
                    {
                        return cfg;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConfigService] ローカル設定ファイルの読み込みスキップ ({path}): {ex.Message}");
                }
            }
        }

        return null;
    }

    public AppConfig LoadConfig()
    {
        lock (_lock)
        {
            AppConfig? config = null;

            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigService] 設定ファイル読み込み警告: {ex.Message}");
            }

            // 初回起動時 (%AppData% に設定ファイルが存在しない、またはパース失敗時)
            if (config == null)
            {
                var bundled = LoadBundledConfig();
                if (bundled != null)
                {
                    Console.WriteLine("[ConfigService] 📦 exeと同階層の初期設定ファイルからロードしました。");
                    config = bundled;
                }
                else
                {
                    config = new AppConfig();
                }

                try
                {
                    SaveConfig(config);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConfigService] 初期設定の%AppData%への保存スキップ: {ex.Message}");
                }
            }
            else
            {
                // すでに %AppData% に存在する場合でも、Box 共通設定およびローカルテーマ設定を同期
                config.Screen ??= new ScreenConfig();
                config.Box ??= new BoxConfig();

                var bundled = LoadBundledConfig();
                if (bundled != null)
                {
                    var modified = false;

                    // ★重要: テーマ定義 (Themes) はローカルの config.json (exeと同フォルダ) から常に最新を取得・同期
                    if (bundled.Box?.Themes != null && bundled.Box.Themes.Count > 0)
                    {
                        config.Box.Themes = bundled.Box.Themes;
                        modified = true;
                    }

                    if (string.IsNullOrWhiteSpace(config.Box.ClientId) && !string.IsNullOrWhiteSpace(bundled.Box.ClientId))
                    {
                        config.Box.ClientId = bundled.Box.ClientId;
                        modified = true;
                    }
                    if (string.IsNullOrWhiteSpace(config.Box.ClientSecret) && !string.IsNullOrWhiteSpace(bundled.Box.ClientSecret))
                    {
                        config.Box.ClientSecret = bundled.Box.ClientSecret;
                        modified = true;
                    }
                    if (string.IsNullOrWhiteSpace(config.Box.Domain) && !string.IsNullOrWhiteSpace(bundled.Box.Domain))
                    {
                        config.Box.Domain = bundled.Box.Domain;
                        modified = true;
                    }
                    if ((string.IsNullOrWhiteSpace(config.Box.SharedFolderId) || config.Box.SharedFolderId == "0") &&
                        !string.IsNullOrWhiteSpace(bundled.Box.SharedFolderId))
                    {
                        config.Box.SharedFolderId = bundled.Box.SharedFolderId;
                        modified = true;
                    }

                    if (modified)
                    {
                        try
                        {
                            SaveConfig(config);
                            Console.WriteLine("[ConfigService] 🔄 exeと同階層の config.json から最新設定・テーマを自動同期しました。");
                        }
                        catch { }
                    }
                }
            }

            // テーマが未設定の場合はデフォルト（社員限定、全体共有）を生成
            if (config.Box.Themes == null || config.Box.Themes.Count == 0)
            {
                config.Box.Themes = BoxConfig.CreateDefaultThemes(config.Box.SharedFolderId);
            }

            // 選択中のテーマが無効または空の場合は、先頭のテーマを選択
            if (string.IsNullOrEmpty(config.Box.SelectedThemeId) || !config.Box.Themes.Any(t => t.Id == config.Box.SelectedThemeId))
            {
                config.Box.SelectedThemeId = config.Box.Themes[0].Id;
                config.Box.SelectedThemeName = config.Box.Themes[0].Name;
            }

            return config;
        }
    }

    public void SaveConfig(AppConfig config)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigService] 設定ファイル保存エラー: {ex.Message}");
                throw;
            }
        }
    }
}
