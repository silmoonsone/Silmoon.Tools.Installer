using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Silmoon.Tools.Installer.WindowsMaker.ViewModels;
using Silmoon.Tools.Installer.WindowsMaker.Services;

namespace Silmoon.Tools.Installer.WindowsMaker;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; }
    public MainPage()
    {
        ViewModel = new(new EditorInteraction(() => XamlRoot));
        InitializeComponent();
        Navigation.SelectedItem = FilesNav;
    }

    public static InfoBarSeverity StatusSeverity(bool error) => error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;

    private void NavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var section = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (FilesPanel is null) return;
        FilesPanel.Visibility = section == "files" ? Visibility.Visible : Visibility.Collapsed;
        ProductPanel.Visibility = section == "product" ? Visibility.Visible : Visibility.Collapsed;
        DependenciesPanel.Visibility = section == "dependencies" ? Visibility.Visible : Visibility.Collapsed;
        DistributionPanel.Visibility = section == "distribution" ? Visibility.Visible : Visibility.Collapsed;
        BuildPanel.Visibility = section == "build" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildClicked(object sender, RoutedEventArgs args) => Navigation.SelectedItem = BuildNav;
}
