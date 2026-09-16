using System.Collections.Generic;
using System.Collections.ObjectModel;
using GameArtMatch.Models;

namespace GameArtMatch.ViewModels;

/// <summary>One folder's worth of ignored ROMs — a tree node in the Options window's
/// Ignored ROMs tab, analogous to RomMatchGroup but grouped by containing folder
/// instead of by ROM (see IgnoredRomsViewModel.Refresh). Only groups exact directories
/// together — e.g. "/roms/Saturn" and "/roms/Saturn/discs" are two separate groups, not
/// one collapsed under a shared prefix.</summary>
public sealed class IgnoredRomGroup(string folderPath, IEnumerable<IgnoredRomEntry> entries)
{
    public string FolderPath { get; } = folderPath;

    public ObservableCollection<IgnoredRomEntry> Entries { get; } = new(entries);
}
