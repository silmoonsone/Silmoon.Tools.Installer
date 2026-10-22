using System.Text;
using System.Text.Json;
using Silmoon.Tools.Installer.Core;

namespace Silmoon.Tools.Installer.Tests;

internal static class RuntimeRegressionTests
{
    public static async Task RunAsync(InstallerProject demo, string configPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "WindowsInstallerMaker-runtime-regressions", Guid.NewGuid().ToString("N"));
        var publish = Path.Combine(root, "publish");
        Directory.CreateDirectory(publish);
        File.Copy(Path.Combine(ProjectStore.ResolvePath(demo.PublishDirectory, configPath), demo.MainExecutable), Path.Combine(publish, demo.MainExecutable));
        var stem = Path.GetFileNameWithoutExtension(demo.MainExecutable);
        await WriteJsonAsync(Path.Combine(publish, stem + ".runtimeconfig.json"), new { runtimeOptions = new { tfm = "net10.0", framework = new { name = "Microsoft.NETCore.App", version = "10.0.13" } } });
        await WriteJsonAsync(Path.Combine(publish, stem + ".deps.json"), new { runtimeTarget = new { name = ".NETCoreApp,Version=v10.0/win-x64" }, libraries = new Dictionary<string, object> { ["Velopack/1.2.161"] = new { type = "package" }, ["Microsoft.WindowsAppSDK/2.5.1"] = new { type = "package" } } });
        var template = ProjectStore.Clone(demo);
        template.PublishDirectory = publish;
        template.OutputDirectory = Path.Combine(root, "output");
        template.IconPath = "";
        template.ReleaseNotesPath = "";
        template.Dependencies.Clear();
        var fixture = demo.Dependencies.First(x => x.Delivery == DependencyDelivery.OfflineSidecar);

        DependencyDefinition Offline(string id, DependencyKind kind, string version, TargetArchitecture architecture = TargetArchitecture.X64)
        {
            var dependency = ProjectStore.Clone(new InstallerProject { Dependencies = [fixture] }).Dependencies.Single();
            dependency.Id = id;
            dependency.Name = id;
            dependency.Kind = kind;
            dependency.MinimumVersion = version;
            dependency.Architecture = architecture;
            dependency.OfflineInstallerPath = ProjectStore.ResolvePath(fixture.OfflineInstallerPath, configPath);
            return dependency;
        }

        var wrongDotNet = ProjectStore.Clone(template);
        wrongDotNet.Dependencies.Add(Offline("test-dotnet8", DependencyKind.DotNetRuntime, "8.0.0"));
        wrongDotNet.Dependencies.Add(Offline("test-wasdk-x64", DependencyKind.WindowsAppSdk, "2.5.1"));
        await AssertRejectedAsync("Offline .NET 8 cannot satisfy .NET 10", wrongDotNet, "不能满足 runtimeconfig");

        var wrongWindowsAppSdk = ProjectStore.Clone(template);
        wrongWindowsAppSdk.Dependencies.Add(Offline("test-dotnet10", DependencyKind.DotNetRuntime, "10.0.13"));
        wrongWindowsAppSdk.Dependencies.Add(Offline("test-wasdk-x86", DependencyKind.WindowsAppSdk, "2.5.1", TargetArchitecture.X86));
        await AssertRejectedAsync("x86 Windows App SDK cannot satisfy x64 app", wrongWindowsAppSdk, "匹配 X64 架构");

        var sdkWithoutRuntime = ProjectStore.Clone(template);
        sdkWithoutRuntime.Dependencies.Add(new() { Id = "test-bundled-sdk", Name = "Test SDK declaration", Kind = DependencyKind.DotNetSdk, Delivery = DependencyDelivery.Bundled, MinimumVersion = "10.0.13", AdvancedDevelopmentTool = true });
        await AssertRejectedAsync("Bundled SDK cannot substitute absent runtime", sdkWithoutRuntime, ".NET 自包含运行时");

        await AssertRejectedAsync("Framework-dependent app must declare runtime", ProjectStore.Clone(template), "尚未声明 .NET Runtime");
    }

    private static async Task AssertRejectedAsync(string name, InstallerProject project, string expectedMessage)
    {
        var validation = new ProjectValidator().Validate(project);
        if (!validation.IsValid) throw new InvalidOperationException($"{name}: fixture failed ordinary validation instead of runtime checks: {string.Join("; ", validation.Issues.Where(x => x.Severity == IssueSeverity.Error).Select(x => x.Message))}");
        var runner = new RecordingProcessRunner();
        var result = await new VelopackEngine(runner).BuildAsync(project, null);
        if (result.Success || result.Canceled || result.SetupPath.Length != 0 || result.Artifacts.Count != 0 || runner.Requests.Count != 0 || !result.Message.Contains(expectedMessage, StringComparison.Ordinal)) throw new InvalidOperationException($"{name}: expected runtime rejection before invoking tools. Requests={runner.Requests.Count}; Result={result.Message}");
        Console.WriteLine($"PASS {name}");
    }

    private static Task WriteJsonAsync(string path, object value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\r\n") + "\r\n", new UTF8Encoding(true));

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutput>? output = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessResult(97, false, "", "Regression fixture must be rejected before any process request."));
        }
    }
}
