using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Silmoon.Tools.WindowsInstallerMaker;

public sealed partial class MainWindow : Window
{
    private bool ClosingApproved { get; set; }
    private bool ShowingCloseDialog { get; set; }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1220 * scale), (int)(860 * scale)));
        RootFrame.Navigate(typeof(MainPage));
        AppWindow.Closing += OnClosing;
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (ClosingApproved) return;
        args.Cancel = true;
        if (ShowingCloseDialog) return;
        ShowingCloseDialog = true;
        try
        {
            if (RootFrame.Content is MainPage page && await page.ViewModel.CanCloseAsync())
            {
                ClosingApproved = true;
                Close();
            }
        }
        finally { ShowingCloseDialog = false; }
    }
}
