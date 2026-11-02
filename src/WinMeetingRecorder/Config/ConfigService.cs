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
    /// exeと同階層に同梱された初期設定ファイル (config.json または config.default.json) を探索・読み込み
    /// </summary>
    private AppConfig? LoadBundledConfig()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "config.json"),
            Path.Combine(AppContext.BaseDirectory, "config.default.json")
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
                    Console.WriteLine($"[ConfigService] 同梱設定ファイルの読み込みスキップ ({path}): {ex.Message}");
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
                    config = new AppConfig();
                }
            }
            else
            {
                // すでに %AppData% に存在する場合でも、Box 共通設定の未入力項目を同梱設定ファイルから補完
                config.Screen ??= new ScreenConfig();
                config.Box ??= new BoxConfig();

                var bundled = LoadBundledConfig();
                if (bundled != null)
                {
                    var modified = false;

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
                            Console.WriteLine("[ConfigService] 🔄 同梱設定ファイルから未入力の共通設定を自動補完しました。");
                        }
                        catch { }
                    }
                }
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
