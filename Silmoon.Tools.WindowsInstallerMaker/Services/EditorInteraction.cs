using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Windows.Storage.Pickers;
using System.Diagnostics;

namespace Silmoon.Tools.WindowsInstallerMaker.Services;

public sealed class EditorInteraction(Func<XamlRoot> root) : IEditorInteraction
{
    private Func<XamlRoot> Root { get; } = root;

    public async Task<string?> OpenFileAsync(string extension)
    {
        var picker = new FileOpenPicker(App.Window.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<string?> SaveFileAsync(string suggestedName)
    {
        var picker = new FileSavePicker(App.Window.AppWindow.Id) { SuggestedFileName = suggestedName, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add(TextResources.Get("Message124"), new List<string> { ".json" });
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public async Task<string?> PickFolderAsync() => (await new FolderPicker(App.Window.AppWindow.Id).PickSingleFolderAsync())?.Path;

    public async Task<SaveDecision> ConfirmUnsavedAsync()
    {
        var dialog = new ContentDialog { XamlRoot = Root(), Title = TextResources.Get("Message125"), Content = TextResources.Get("Message126"), PrimaryButtonText = TextResources.Get("Message127"), SecondaryButtonText = TextResources.Get("Message128"), CloseButtonText = TextResources.Get("Message129"), DefaultButton = ContentDialogButton.Primary };
        AutomationProperties.SetAutomationId(dialog, "UnsavedDialog");
        return (await dialog.ShowAsync()) switch { ContentDialogResult.Primary => SaveDecision.Save, ContentDialogResult.Secondary => SaveDecision.Discard, _ => SaveDecision.Cancel };
    }

    public async Task<bool> ConfirmAsync(string title, string message, string action)
    {
        var dialog = new ContentDialog { XamlRoot = Root(), Title = title, Content = message, PrimaryButtonText = action, CloseButtonText = TextResources.Get("Message130"), DefaultButton = ContentDialogButton.Close };
        AutomationProperties.SetAutomationId(dialog, "ConfirmDialog");
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public Task OpenDirectoryAsync(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        return Task.CompletedTask;
    }
}
