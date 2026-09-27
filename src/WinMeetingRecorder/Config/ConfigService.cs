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

    public AppConfig LoadConfig()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                    if (config != null)
                    {
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigService] 設定ファイル読み込み警告: {ex.Message}");
            }

            return new AppConfig();
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
