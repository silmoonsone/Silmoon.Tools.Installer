using Microsoft.Windows.ApplicationModel.Resources;

namespace Silmoon.Tools.Installer.WindowsMaker.Services;

public static class TextResources
{
    private static ResourceLoader Loader { get; } = new();
    public static string Get(string key) => Loader.GetString(key);
    public static string Format(string key, params object?[] arguments) => string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), arguments);
}
