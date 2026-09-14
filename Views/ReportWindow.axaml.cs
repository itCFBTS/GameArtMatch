using Avalonia.Controls;
using GameArtMatch.ViewModels;

namespace GameArtMatch.Views;

public partial class ReportWindow : Window
{
    public ReportWindow()
    {
        InitializeComponent();

        // DataContext is set externally (new ReportWindow { DataContext = ... }) after
        // the constructor runs, so wait for Opened rather than generating here — by then
        // it's guaranteed to be assigned. Runs every time a Report window is opened,
        // matching "clicking Report generates all the info" rather than requiring an
        // extra manual click first.
        Opened += async (_, _) =>
        {
            if (DataContext is ReportViewModel vm)
                await vm.GenerateAllCommand.ExecuteAsync(null);
        };
    }
}
