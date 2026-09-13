using System;
using System.IO;
using System.Text.Json;
using GameArtMatch.Models;

namespace GameArtMatch.Services;

/// <summary>
/// Plain JSON file under the OS's per-user app-data folder — resolved by
/// Environment.SpecialFolder.ApplicationData, which .NET maps cross-platform:
/// ~/.config on Linux, %AppData% on Windows, ~/Library/Application Support on macOS.
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GameArtMatch", "settings.json");

    public PersistedSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<PersistedSettings>(json) ?? new PersistedSettings();
            }
        }
        catch
        {
            // Missing, unreadable, or corrupt — fall back to defaults rather than crash on startup.
        }

        return new PersistedSettings();
    }

    public void Save(PersistedSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
