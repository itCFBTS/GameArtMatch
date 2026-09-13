using System.Reflection;

namespace GameArtMatch.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    public string VersionText { get; } =
        $"GameArtMatch v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.1"}";
}
