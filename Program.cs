using Avalonia;
using Avalonia.Media;
using System;

namespace GameArtMatch;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            // Bundled font (see Assets/Fonts, Tokens.axaml's FontFamilyBase) as the app's
            // implicit default, same role Avalonia.Fonts.Inter's WithInterFont() played before.
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://GameArtMatch/Assets/Fonts#Share Tech Mono"
            })
            .LogToTrace();
}
