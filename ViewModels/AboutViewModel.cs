using System.Reflection;

namespace GameArtMatch.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    /// <summary>Three parts ("v0.5.0"), matching the release tags the build reads its
    /// version from (see SetVersionFromGitTag in GameArtMatch.csproj).</summary>
    public string VersionText { get; } =
        $"GameArtMatch v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";
}
