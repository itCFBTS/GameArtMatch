using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GameArtMatch.ViewModels;

namespace GameArtMatch.Views;

public partial class ReportView : UserControl
{
    public ReportView()
    {
        InitializeComponent();
    }

    // TopLevel.GetTopLevel(this) rather than a Window-typed field — this UserControl is
    // hosted inside ReportWindow, but doesn't need to know that to reach StorageProvider.
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var documents = await topLevel.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Report",
            SuggestedFileName = $"GameArtMatch-Report-{DateTime.Now:yyyyMMdd-HHmmss}",
            DefaultExtension = "txt",
            SuggestedStartLocation = documents,
            FileTypeChoices = [new FilePickerFileType("Text File") { Patterns = ["*.txt"] }],
        });

        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new System.IO.StreamWriter(stream);
        await writer.WriteAsync(vm.BuildExportText());
    }
}
