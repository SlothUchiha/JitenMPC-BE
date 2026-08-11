using System.Text.Json;
using JitenMpcBe.Models;

namespace JitenMpcBe.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public string ApplicationDirectory { get; } = AppContext.BaseDirectory;
    public string DataDirectory { get; }
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public AppSettings Current { get; private set; }

    public SettingsService()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DataDirectory = string.IsNullOrWhiteSpace(local)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(local, "JitenMPC-BE");
        Directory.CreateDirectory(DataDirectory);
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();

            var candidate = FindPrototypeSettings();
            if (candidate is not null)
            {
                var imported = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(candidate), JsonOptions) ?? new AppSettings();
                Current = imported;
                Save();
                return imported;
            }
        }
        catch { }
        return new AppSettings();
    }

    private string? FindPrototypeSettings()
    {
        var roots = new List<string>();
        var current = new DirectoryInfo(ApplicationDirectory);
        for (var i = 0; i < 4 && current is not null; i++, current = current.Parent)
            roots.Add(current.FullName);

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var direct = Path.Combine(root, "settings.json");
                if (root.Contains("JitenMPC-BE-v0.1.8-prototype", StringComparison.OrdinalIgnoreCase) && File.Exists(direct))
                    return direct;

                var candidate = Directory.EnumerateDirectories(root, "JitenMPC-BE-v0.1.8-prototype*")
                    .Select(d => Path.Combine(d, "settings.json"))
                    .FirstOrDefault(File.Exists);
                if (candidate is not null) return candidate;
            }
            catch { }
        }
        return null;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, JsonOptions));
    }
}
