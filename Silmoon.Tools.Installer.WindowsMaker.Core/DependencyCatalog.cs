using System.Text.RegularExpressions;

namespace Silmoon.Tools.Installer.WindowsMaker.Core;

public static class DependencyCatalog
{
    public static IReadOnlyList<DependencyDefinition> Presets =>
    [
        new() { Id = "dotnet-runtime", Name = ".NET 10 Runtime", Kind = DependencyKind.DotNetRuntime, Delivery = DependencyDelivery.Bundled, MinimumVersion = "10.0", Notes = "自包含应用内置；框架依赖应用可选择缺失时在线安装。" },
        new() { Id = "dotnet-desktop", Name = ".NET 10 Desktop Runtime", Kind = DependencyKind.DotNetDesktopRuntime, Delivery = DependencyDelivery.Online, MinimumVersion = "10.0" },
        new() { Id = "windows-app-sdk", Name = "Windows App SDK Runtime", Kind = DependencyKind.WindowsAppSdk, Delivery = DependencyDelivery.Bundled, Notes = "请使用 WindowsAppSDKSelfContained 发布，或提供已验证的独立离线安装器。" },
        new() { Id = "webview2", Name = "WebView2 Evergreen Runtime", Kind = DependencyKind.WebView2, Delivery = DependencyDelivery.Online, FrameworkId = "webview2" },
        new() { Id = "vcredist143", Name = "Visual C++ 2015–2022 Redistributable", Kind = DependencyKind.VisualCpp, Delivery = DependencyDelivery.Online, FrameworkId = "vcredist143-x64" },
        new() { Id = "net48", Name = ".NET Framework 4.8", Kind = DependencyKind.DotNetFramework, Delivery = DependencyDelivery.Online, MinimumVersion = "4.8", FrameworkId = "net48" },
        new() { Id = "custom-offline", Name = "自定义完整离线安装器", Kind = DependencyKind.Custom, Delivery = DependencyDelivery.OfflineSidecar, Notes = "与 Setup 同目录分发；用户先手动安装，不自动执行。" }
    ];

    public static bool TryGetFrameworkId(DependencyDefinition dependency, out string id)
    {
        id = "";
        var architecture = dependency.Architecture.ToString().ToLowerInvariant();
        if (!Enum.IsDefined(dependency.Architecture)) return false;
        var requested = dependency.FrameworkId?.Trim().ToLowerInvariant() ?? "";
        var version = dependency.MinimumVersion?.Trim() ?? "";
        if (dependency.Kind == DependencyKind.WebView2 && version.Length == 0) id = "webview2";
        else if (dependency.Kind == DependencyKind.VisualCpp && version.Length == 0) id = $"vcredist143-{architecture}";
        else if (dependency.Kind == DependencyKind.DotNetFramework)
        {
            if (version is "" or "4.8") id = requested == "net481" && version.Length == 0 ? "net481" : "net48";
            else if (version == "4.8.1") id = "net481";
        }
        else if (dependency.Kind is DependencyKind.DotNetRuntime or DependencyKind.DotNetDesktopRuntime or DependencyKind.DotNetSdk)
        {
            var type = dependency.Kind switch { DependencyKind.DotNetDesktopRuntime => "desktop", DependencyKind.DotNetSdk => "sdk", _ => "runtime" };
            if (version.Length == 0 && requested.Length != 0)
            {
                var match = Regex.Match(requested, $"^net((?:8|9|10)(?:\\.0(?:\\.[0-9]+)?)?)-{architecture}-{type}$", RegexOptions.CultureInvariant);
                if (match.Success) id = requested;
            }
            else
            {
                if (version.Length == 0) version = "10.0";
                if (Regex.IsMatch(version, "^(8|9|10)\\.0(?:\\.[0-9]+)?$", RegexOptions.CultureInvariant)) id = $"net{version}-{architecture}-{type}";
            }
        }
        if (id.Length == 0) return false;
        if (requested.Length == 0 || requested == id) return true;
        if (requested.Replace(".0-", "-", StringComparison.Ordinal) == id.Replace(".0-", "-", StringComparison.Ordinal)) return true;
        id = "";
        return false;
    }
}
