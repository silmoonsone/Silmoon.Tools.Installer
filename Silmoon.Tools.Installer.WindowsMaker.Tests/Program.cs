using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Silmoon.Tools.Installer.WindowsMaker.Core;

namespace Silmoon.Tools.Installer.WindowsMaker.Tests;

internal static class Program
{
    private static List<object> Results { get; } = [];
    private static string TestRoot { get; } = Path.Combine(Path.GetTempPath(), "WindowsInstallerMaker-tests", Guid.NewGuid().ToString("N"));

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.FirstOrDefault() == "--child-echo")
        {
            Console.WriteLine(args[1]);
            Console.Error.WriteLine("expected stderr");
            return 37;
        }
        if (args.FirstOrDefault() == "--child-wait")
        {
            Console.WriteLine(Environment.ProcessId);
            await Task.Delay(TimeSpan.FromSeconds(30));
            return 0;
        }
        Directory.CreateDirectory(TestRoot);
        var store = new ProjectStore();
        var source = Path.Combine(TestRoot, "配置 中文", "project.installer.json");
        var publish = Path.Combine(TestRoot, "publish folder");
        Directory.CreateDirectory(publish);
        File.Copy(Environment.ProcessPath!, Path.Combine(publish, "Example.exe"));
        var project = new InstallerProject { ProductId = "Example.Application", DisplayName = "示例应用", Publisher = "示例发布者", Version = "1.0.0-preview.1", PublishDirectory = publish, MainExecutable = "Example.exe", OutputDirectory = Path.Combine(TestRoot, "output") };

        await Test("JSON roundtrip retains Unicode, identity and dependencies", async () =>
        {
            var copy = ProjectStore.Clone(project);
            copy.Dependencies.Add(new DependencyDefinition { Id = "sample", Name = "组件", Kind = DependencyKind.Custom, Delivery = DependencyDelivery.OfflineSidecar, OfflineInstallerPath = Path.Combine(TestRoot, "依赖.exe"), InstallArguments = ["/quiet", "a b", "$(never-execute)"], Sha256 = new string('A', 64), ExpectedSigner = "CN=Example", ExpectedInstallerVersion = "1.0.0.0" });
            var saved = await store.SaveAsync(copy, source);
            var loaded = await store.LoadAsync(source);
            Assert(loaded.ProductId == copy.ProductId && loaded.DisplayName == "示例应用" && loaded.Version == copy.Version, "Product identity changed.");
            Assert(loaded.Dependencies[0].InstallArguments.SequenceEqual(copy.Dependencies[0].InstallArguments), "Structured arguments lost.");
            Assert(ProjectStore.ResolvePath(loaded.PublishDirectory, source) == publish, "Relative input lost.");
            Assert(!Path.IsPathRooted(saved.PublishDirectory), "Saved input should be relative.");
            var bytes = await File.ReadAllBytesAsync(source);
            Assert(bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "Missing UTF-8 BOM.");
            Assert(!Encoding.UTF8.GetString(bytes).Replace("\r\n", "").Contains('\n'), "Bare LF found.");
        });
        await Test("Save As rebases every file reference without changing targets", async () =>
        {
            var original = await store.LoadAsync(source);
            original.IconPath = "../sample.ico";
            original.ReleaseNotesPath = "../notes.md";
            var destination = Path.Combine(TestRoot, "different", "nested", "saved.installer.json");
            var saved = await store.SaveAsync(original, destination, source);
            foreach (var pair in new[] { (original.PublishDirectory, saved.PublishDirectory), (original.OutputDirectory, saved.OutputDirectory), (original.IconPath, saved.IconPath), (original.ReleaseNotesPath, saved.ReleaseNotesPath), (original.Dependencies[0].OfflineInstallerPath, saved.Dependencies[0].OfflineInstallerPath) }) Assert(ProjectStore.ResolvePath(pair.Item1, source) == ProjectStore.ResolvePath(pair.Item2, destination), "Save As changed path target.");
            Assert((await store.LoadAsync(source)).IconPath == "", "Save As rewrote source document.");
        });
        foreach (var invalid in new[] { "{", "null", "[]", "{}", "{\"schemaVersion\":2}", "{\"schemaVersion\":\"1\"}", "{\"schemaVersion\":null}", "{\"schemaVersion\":1,\"dependencies\":null}", "{\"schemaVersion\":1,\"architecture\":999}", "{\"schemaVersion\":1,\"architecture\":\"riscv\"}", "{\"schemaVersion\":1,\"schemaVersion\":1}", "{\"schemaVersion\":1,\"password\":\"never-persist\"}" })
            await Test($"Reject invalid config: {invalid}", () => { Throws<InvalidDataException>(() => ProjectStore.Deserialize(invalid)); return Task.CompletedTask; });
        await Test("Canceled save preserves existing bytes", async () =>
        {
            var before = await File.ReadAllBytesAsync(source);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            try { await store.SaveAsync(project, source, ct: cts.Token); throw new Exception("Save unexpectedly completed."); }
            catch (OperationCanceledException) { }
            var after = await File.ReadAllBytesAsync(source);
            Assert(before.SequenceEqual(after), "Canceled save overwrote file.");
        });
        await Test("Missing configuration yields error", async () =>
        {
            try { await store.LoadAsync(Path.Combine(TestRoot, "missing.json")); throw new Exception("Missing config accepted."); }
            catch (FileNotFoundException) { }
        });

        CheckInvalid("Unsupported all-users install", project, p => p.InstallScope = InstallScope.AllUsers);
        CheckInvalid("Unsupported online bootstrap", project, p => p.DistributionMode = DistributionMode.OnlineBootstrap);
        CheckInvalid("Unsupported signing", project, p => p.SigningMode = SigningMode.External);
        CheckInvalid("Unsupported deltas", project, p => p.GenerateDelta = true);
        CheckInvalid("Destructive data policy", project, p => p.PreserveUserData = false);
        CheckInvalid("Invalid SemVer", project, p => p.Version = "1.2.3.4");
        CheckInvalid("Product traversal", project, p => p.ProductId = "../escape");
        CheckInvalid("Channel injection", project, p => p.Channel = "preview; whoami");
        CheckInvalid("Main EXE traversal", project, p => p.MainExecutable = "../Example.exe");
        CheckInvalid("Missing main EXE", project, p => p.MainExecutable = "Missing.exe");
        CheckInvalid("Output inside input", project, p => p.OutputDirectory = Path.Combine(p.PublishDirectory, "output"));
        CheckInvalid("Missing offline installer", project, p => p.Dependencies.Add(new DependencyDefinition { Id = "offline", Name = "Offline", Delivery = DependencyDelivery.OfflineSidecar, OfflineInstallerPath = "missing.exe" }));
        CheckInvalid("Unknown custom automatic install", project, p => p.Dependencies.Add(new DependencyDefinition { Id = "custom", Name = "Custom", Delivery = DependencyDelivery.Online, Kind = DependencyKind.Custom, DownloadUrl = "https://example.com/setup.exe" }));
        CheckInvalid("SDK requires advanced opt-in", project, p => p.Dependencies.Add(new DependencyDefinition { Id = "sdk", Name = "SDK", Delivery = DependencyDelivery.Online, Kind = DependencyKind.DotNetSdk, MinimumVersion = "10.0" }));
        CheckInvalid("Dependency path traversal", project, p => p.Dependencies.Add(new DependencyDefinition { Id = "../../escape", Name = "Invalid" }));
        CheckInvalid("Dependency Windows reserved name", project, p => p.Dependencies.Add(new DependencyDefinition { Id = "CON", Name = "Invalid" }));
        CheckInvalid("Online custom execution flags cannot be silently ignored", project, p => p.Dependencies.Add(new DependencyDefinition { Id = "webview", Name = "WebView2", Kind = DependencyKind.WebView2, Delivery = DependencyDelivery.Online, InstallArguments = ["/custom"] }));
        CheckInvalid("Update URL must not store access tokens", project, p => p.UpdateUrl = "https://example.com/releases?token=secret");
        await Test("WebView2 online preset maps to vpk and cannot promise a custom minimum", () =>
        {
            var dep = new DependencyDefinition { Kind = DependencyKind.WebView2, Delivery = DependencyDelivery.Online };
            Assert(DependencyCatalog.TryGetFrameworkId(dep, out var id) && id == "webview2", "Built-in framework mapping missing.");
            dep.MinimumVersion = "999.0";
            Assert(!DependencyCatalog.TryGetFrameworkId(dep, out _), "Unsupported minimum silently ignored.");
            return Task.CompletedTask;
        });
        await Test("Analyzer rejects corrupt main executable without running it", async () =>
        {
            var corruptDir = Path.Combine(TestRoot, "corrupt");
            Directory.CreateDirectory(corruptDir);
            await File.WriteAllTextAsync(Path.Combine(corruptDir, "bad.exe"), "not a PE");
            var analysis = await new PublishAnalyzer().AnalyzeAsync(corruptDir, "bad.exe");
            Assert(analysis.Issues.Any(x => x.Severity == IssueSeverity.Error), "Corrupt executable accepted.");
        });
        await Test("ProcessRunner preserves argument boundaries and nonzero exit", async () =>
        {
            const string argument = "空 格 \"quoted\" ; $(exit 0) & | %PATH%";
            var result = await new ProcessRunner().RunAsync(new ProcessRequest { FileName = Environment.ProcessPath!, Arguments = ["--child-echo", argument] });
            Assert(result.ExitCode == 37 && !result.Canceled, "Nonzero exit hidden.");
            Assert(result.StandardOutput.TrimEnd() == argument && result.StandardError.Contains("expected stderr"), $"Arguments/logs mismatch: stdout={JsonSerializer.Serialize(result.StandardOutput)}, stderr={JsonSerializer.Serialize(result.StandardError)}");
        });
        await Test("Cancellation stops only the owned process tree", async () =>
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("--child-wait");
            using var sentinel = Process.Start(start)!;
            try
            {
                await sentinel.StandardOutput.ReadLineAsync();
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
                var result = await new ProcessRunner().RunAsync(new ProcessRequest { FileName = Environment.ProcessPath!, Arguments = ["--child-wait"] }, cancellationToken: cts.Token);
                Assert(result.Canceled && !sentinel.HasExited, "Cancellation affected a separate process or was lost.");
            }
            finally
            {
                if (!sentinel.HasExited) sentinel.Kill(entireProcessTree: true);
                await sentinel.WaitForExitAsync();
            }
        });

        var demoArgument = Array.IndexOf(args, "--demo");
        if (demoArgument >= 0 && demoArgument + 1 < args.Length) await TestDemo(args[demoArgument + 1]);
        var report = new { completedAtUtc = DateTimeOffset.UtcNow, testRoot = TestRoot, passed = Results.Count(x => x.GetType().GetProperty("passed")?.GetValue(x) is true), failed = Results.Count(x => x.GetType().GetProperty("passed")?.GetValue(x) is false), results = Results };
        var reportPath = Path.Combine(GetProjectDirectory(), "artifacts", "tests", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}", "core-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\r\n") + "\r\n", new UTF8Encoding(true));
        Console.WriteLine($"Passed: {report.passed}; Failed: {report.failed}; {reportPath}");
        return report.failed == 0 ? 0 : 1;
    }

    private static string GetProjectDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Silmoon.Tools.Installer.WindowsMaker.Tests.csproj"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Cannot locate the test project directory for artifacts.");
    }

    private static async Task TestDemo(string path)
    {
        var project = await new ProjectStore().LoadAsync(path);
        await Test("Framework-dependent runtime compatibility regressions (four cases)", () => RuntimeRegressionTests.RunAsync(project, path));
        await Test("Real demo publish static analysis", async () =>
        {
            var analysis = await new PublishAnalyzer().AnalyzeAsync(ProjectStore.ResolvePath(project.PublishDirectory, path), project.MainExecutable);
            Assert(analysis.Architecture == TargetArchitecture.X64, "Demo architecture mismatch.");
            Assert(analysis.IsDotNetSelfContained && analysis.IsWindowsAppSdkSelfContained, "Self-contained evidence missing.");
            Assert(analysis.VelopackVersion == "1.2.161", "Velopack package mismatch.");
            Assert(analysis.Issues.All(x => x.Severity != IssueSeverity.Error), string.Join("; ", analysis.Issues.Select(x => x.Message)));
        });
        await Test("Real demo validates", () =>
        {
            var result = new ProjectValidator().Validate(project, path);
            Assert(result.IsValid, string.Join("; ", result.Issues.Select(x => x.Message)));
            return Task.CompletedTask;
        });
        await Test("Engine nonzero pack exit never exposes an installer", async () =>
        {
            var copy = ProjectStore.Clone(project);
            copy.OutputDirectory = Path.Combine(TestRoot, "failed-pack");
            copy.Dependencies.Clear();
            var runner = new FailingPackRunner();
            var result = await new VelopackEngine(runner).BuildAsync(copy, path);
            Assert(!result.Success && result.ExitCode == 42 && result.SetupPath.Length == 0 && result.Artifacts.Count == 0, "Failed pack exposed success/artifacts.");
            Assert(runner.Requests.Any(x => x.Arguments.Contains("pack")), "Pack failure path was not exercised.");
            Assert(runner.Requests.All(x => !x.Arguments.Contains("--skipVeloAppCheck")), "Lifecycle check was disabled.");
        });
        await Test("Zero process exit with no valid artifacts is still failure", async () =>
        {
            var copy = ProjectStore.Clone(project);
            copy.OutputDirectory = Path.Combine(TestRoot, "empty-success");
            copy.Dependencies.Clear();
            var runner = new FailingPackRunner { PackExitCode = 0 };
            var result = await new VelopackEngine(runner).BuildAsync(copy, path);
            Assert(!result.Success && result.SetupPath.Length == 0 && result.Artifacts.Count == 0, "Missing output incorrectly accepted.");
            Assert(runner.Requests.Any(x => x.Arguments.Contains("pack")), "Output verification path not reached.");
        });
        await Test("Offline hash mismatch stops before pack", async () =>
        {
            var copy = ProjectStore.Clone(project);
            copy.OutputDirectory = Path.Combine(TestRoot, "bad-hash");
            copy.Dependencies.Single(x => x.Delivery == DependencyDelivery.OfflineSidecar).Sha256 = new string('0', 64);
            var runner = new FailingPackRunner();
            var result = await new VelopackEngine(runner).BuildAsync(copy, path);
            Assert(!result.Success && result.SetupPath.Length == 0, "Bad dependency accepted.");
            Assert(runner.Requests.All(x => !x.Arguments.Contains("pack")), "Pack ran before dependency integrity succeeded.");
        });
        await Test("Tool version mismatch does not upgrade or pack", async () =>
        {
            var copy = ProjectStore.Clone(project);
            copy.Dependencies.Clear();
            var runner = new FailingPackRunner { ToolVersion = "1.2.160" };
            var result = await new VelopackEngine(runner).BuildAsync(copy, path);
            Assert(!result.Success && runner.Requests.Count == 1, "Mismatched tool packed or extra commands ran.");
            Assert(runner.Requests[0].Arguments.SequenceEqual(new[] { "tool", "list", "--global" }), "Unexpected install/upgrade command.");
        });
        await Test("Pre-canceled build never spawns a tool", async () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var runner = new FailingPackRunner();
            var result = await new VelopackEngine(runner).BuildAsync(project, path, cancellationToken: cts.Token);
            Assert(result.Canceled && !result.Success && runner.Requests.Count == 0 && result.SetupPath.Length == 0, "Canceled build ran or exposed output.");
        });
        await Test("Pack argument list preserves title, shortcuts and lifecycle checks", () =>
        {
            var copy = ProjectStore.Clone(project);
            copy.DisplayName = "Quoted \"Title\" & $(bad)";
            copy.DesktopShortcut = false;
            copy.StartMenuShortcut = false;
            var arguments = VelopackEngine.CreatePackArguments(copy, path, TestRoot).ToList();
            Assert(arguments[arguments.IndexOf("--packTitle") + 1] == copy.DisplayName, "Title was shell-quoted or split.");
            Assert(arguments[arguments.IndexOf("--shortcuts") + 1] == "None", "Disabled shortcuts ignored.");
            Assert(arguments.Contains("--noPortable") && !arguments.Contains("--skipVeloAppCheck"), "Lifecycle/package policy changed.");
            return Task.CompletedTask;
        });
    }

    private static void CheckInvalid(string name, InstallerProject source, Action<InstallerProject> mutate)
    {
        try
        {
            var project = ProjectStore.Clone(source);
            mutate(project);
            Assert(!new ProjectValidator().Validate(project).IsValid, "Unsupported configuration accepted.");
            Results.Add(new { name, passed = true });
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Results.Add(new { name, passed = false, error = ex.ToString() });
            Console.WriteLine($"FAIL {name}: {ex.Message}");
        }
    }

    private static async Task Test(string name, Func<Task> test)
    {
        try
        {
            await test();
            Results.Add(new { name, passed = true });
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Results.Add(new { name, passed = false, error = ex.ToString() });
            Console.WriteLine($"FAIL {name}: {ex.Message}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class FailingPackRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];
        public int PackExitCode { get; init; } = 42;
        public string ToolVersion { get; init; } = "1.2.161";
        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutput>? output = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Arguments.Contains("pack")) return Task.FromResult(new ProcessResult(PackExitCode, false, "", "Injected pack outcome"));
            return Task.FromResult(new ProcessResult(0, false, $"Package Id Version Commands\r\nvpk {ToolVersion} vpk\r\n", ""));
        }
    }
}
