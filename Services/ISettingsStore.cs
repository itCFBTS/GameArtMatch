using GameArtMatch.Models;

namespace GameArtMatch.Services;

public interface ISettingsStore
{
    PersistedSettings Load();
    void Save(PersistedSettings settings);
}
