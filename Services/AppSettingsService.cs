using System.Text.Json;

namespace Codewright.Services;

public sealed class AppSettingsService
{
    private readonly string _settingsPath;

    public AppSettingsService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Codewright");
        Directory.CreateDirectory(root);
        _settingsPath = Path.Combine(root, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_settingsPath, json);
    }
}

public sealed class AppSettings
{
    public bool UseSavedWorkDirectory { get; set; }
    public string? WorkDirectory { get; set; }
    public string? LastGitHubUrl { get; set; }
}
