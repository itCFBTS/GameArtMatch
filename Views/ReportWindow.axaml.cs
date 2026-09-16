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
        // extra manual click first. Synchronous now — Refresh just copies an
        // already-computed list (see ReportViewModel), no scan to await.
        Opened += (_, _) =>
        {
            if (DataContext is ReportViewModel vm)
                vm.RefreshCommand.Execute(null);
        };
    }
}
