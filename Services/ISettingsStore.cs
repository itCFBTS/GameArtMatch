using GameArtMatch.Models;

namespace GameArtMatch.Services;

public interface ISettingsStore
{
    PersistedSettings Load();
    void Save(PersistedSettings settings);

    /// <summary>Folder the settings file lives in — backs File > Open Settings Folder
    /// (see FileExplorerService.RevealFolder).</summary>
    string FolderPath { get; }
}
