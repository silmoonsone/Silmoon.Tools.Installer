using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Silmoon.Tools.Installer.Core;

public sealed class VelopackEngine
{
    private IProcessRunner ProcessRunner { get; }
    private DependencyVerifier DependencyVerifier { get; }

    public VelopackEngine(IProcessRunner? processRunner = null)
    {
        ProcessRunner = processRunner ?? new ProcessRunner();
        DependencyVerifier = new DependencyVerifier(ProcessRunner);
    }

    public async Task<BuildResult> BuildAsync(InstallerProject project, string? projectFilePath, IProgress<BuildProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string stageDirectory = "", logPath = "";
        string logWriteError = "";
        int? exitCode = null;
        var messages = new List<string>();
        var logLock = new object();
        void Report(string stage, string message, double? percent = null, bool isError = false)
        {
            lock (logLock)
            {
                messages.Add($"{DateTimeOffset.Now:O} [{stage}] {message}");
                if (logPath.Length != 0)
                {
                    try { File.AppendAllText(logPath, messages[^1] + "\r\n", new UTF8Encoding(true)); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logWriteError = $"构建日志无法写入：{ex.Message}"; }
                }
            }
            progress?.Report(new BuildProgress(stage, message, percent, isError));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("第一版仅支持在 Windows 上生成 Windows 安装包。");
            project = ProjectStore.Clone(project);
            Report("验证配置", "检查产品身份、路径与引擎能力。", 2);
            var validation = new ProjectValidator().Validate(project, projectFilePath);
            foreach (var issue in validation.Issues) Report("验证配置", $"{issue.Severity}: {issue.Message}", 3, issue.Severity == IssueSeverity.Error);
            if (!validation.IsValid) throw new InvalidDataException("安装项目验证失败，请修正错误后再构建。");
            if (project.ExpectedVelopackVersion != "1.2.161") throw new InvalidDataException("第一版引擎适配经过验证的 vpk 版本为 1.2.161；其他版本需要先进行兼容性验证，不能仅修改配置跳过检查。");
            var publishDirectory = ProjectStore.ResolvePath(project.PublishDirectory, projectFilePath);
            var outputRoot = ProjectStore.ResolvePath(project.OutputDirectory, projectFilePath);
            Report("分析输入", "读取主程序、runtimeconfig、deps 与 PE 信息。", 5);
            var analysis = await new PublishAnalyzer().AnalyzeAsync(publishDirectory, project.MainExecutable, cancellationToken).ConfigureAwait(false);
            foreach (var issue in analysis.Issues) Report("分析输入", $"{issue.Severity}: {issue.Message}", 7, issue.Severity == IssueSeverity.Error);
            if (analysis.Issues.Any(x => x.Severity == IssueSeverity.Error)) throw new InvalidDataException("发布目录分析发现错误，未开始打包。");
            if (analysis.Architecture != project.Architecture) throw new InvalidDataException($"主程序架构 {analysis.Architecture?.ToString() ?? "未知"} 与项目 {project.Architecture} 不一致。");
            foreach (var dependency in project.Dependencies.Where(x => x.Delivery == DependencyDelivery.Bundled))
            {
                if (dependency.Kind is DependencyKind.DotNetRuntime or DependencyKind.DotNetDesktopRuntime or DependencyKind.DotNetSdk && !analysis.IsDotNetSelfContained) throw new InvalidDataException($"{dependency.Name} 声明随应用携带，但分析未发现 .NET 自包含运行时。请更正依赖策略或重新发布。");
                if (dependency.Kind == DependencyKind.WindowsAppSdk && !analysis.IsWindowsAppSdkSelfContained) throw new InvalidDataException($"{dependency.Name} 声明随应用携带，但发布目录缺少 Windows App SDK 自包含证据。");
            }
            if (analysis.TargetFramework.Length != 0 && !analysis.IsDotNetSelfContained && !project.Dependencies.Any(x => x.Kind is DependencyKind.DotNetRuntime or DependencyKind.DotNetDesktopRuntime or DependencyKind.DotNetFramework)) throw new InvalidDataException("框架依赖应用尚未声明 .NET Runtime 依赖。请配置相应 Runtime 的在线安装或完整离线旁置安装器，或重新自包含发布；SDK 声明不能代替 Runtime 版本声明。");
            foreach (var requirement in analysis.RequiredFrameworks)
            {
                var candidates = project.Dependencies.Where(x => x.Architecture == project.Architecture && (requirement.Name switch { "Microsoft.NETCore.App" => x.Kind is DependencyKind.DotNetRuntime or DependencyKind.DotNetDesktopRuntime, "Microsoft.WindowsDesktop.App" => x.Kind == DependencyKind.DotNetDesktopRuntime, _ => false })).ToArray();
                if (candidates.Length == 0) throw new InvalidDataException($"未配置 {requirement.Name} {requirement.Version}（{project.Architecture}）的对应依赖。请选择支持的在线 Runtime 或完整离线旁置安装器；不支持的框架需先使用自包含发布。");
                if (!System.Version.TryParse(requirement.Version, out var requiredVersion)) throw new InvalidDataException($"runtimeconfig 中 {requirement.Name} 的版本无效：{requirement.Version}");
                requiredVersion = NormalizeRuntimeVersion(requiredVersion);
                foreach (var dependency in candidates.Where(x => x.Delivery is DependencyDelivery.Online or DependencyDelivery.OfflineSidecar))
                {
                    var minimumVersion = dependency.MinimumVersion;
                    if (dependency.Delivery == DependencyDelivery.Online)
                    {
                        DependencyCatalog.TryGetFrameworkId(dependency, out var framework);
                        var match = Regex.Match(framework, @"^net(?<version>\d+(?:\.\d+){0,2})-", RegexOptions.CultureInvariant);
                        minimumVersion = match.Success ? match.Groups["version"].Value : "";
                        if (minimumVersion.Length != 0 && !minimumVersion.Contains('.')) minimumVersion += ".0";
                    }
                    if (!System.Version.TryParse(minimumVersion, out var declaredVersion)) throw new InvalidDataException($"依赖 {dependency.Name} 必须明确声明所携带 Runtime 的最低版本，以满足 {requirement.Name} {requirement.Version}；安装器文件版本不能代替 Runtime 版本。");
                    declaredVersion = NormalizeRuntimeVersion(declaredVersion);
                    if (declaredVersion.Major != requiredVersion.Major || declaredVersion.Minor != requiredVersion.Minor || declaredVersion < requiredVersion) throw new InvalidDataException($"依赖 {dependency.Name} 的最低 Runtime 版本 {minimumVersion} 不能满足 runtimeconfig 所需 {requirement.Name} {requirement.Version}；需要相同主/次版本且不低于所需补丁版本。");
                }
            }
            if (analysis.HasWindowsAppSdk && !analysis.IsWindowsAppSdkSelfContained && !project.Dependencies.Any(x => x.Kind == DependencyKind.WindowsAppSdk && x.Architecture == project.Architecture && x.Delivery == DependencyDelivery.OfflineSidecar)) throw new InvalidDataException($"发布目录依赖 Windows App SDK Runtime，却未自包含或配置匹配 {project.Architecture} 架构的完整离线旁置安装器。");
            if (string.IsNullOrWhiteSpace(analysis.VelopackVersion)) throw new InvalidDataException("无法确认目标应用的 Velopack NuGet 版本。请完成一次性启动生命周期接入并保留 deps.json；本工具不会修改业务源码或跳过 VeloAppCheck。");
            if (!analysis.VelopackVersion.Equals(project.ExpectedVelopackVersion, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"目标应用 Velopack {analysis.VelopackVersion} 与项目要求的 vpk {project.ExpectedVelopackVersion} 不匹配。");
            Report("检查工具", "检测全局 vpk 版本，不安装或升级工具。", 10);
            var toolResult = await ProcessRunner.RunAsync(new ProcessRequest { FileName = "dotnet", Arguments = ["tool", "list", "--global"], RemoveVelopackEnvironment = true }, cancellationToken: cancellationToken).ConfigureAwait(false);
            exitCode = toolResult.ExitCode;
            if (toolResult.Canceled) throw new OperationCanceledException(cancellationToken);
            if (toolResult.ExitCode != 0) throw new InvalidOperationException($"dotnet tool list 失败（退出码 {toolResult.ExitCode}）：{toolResult.StandardError}");
            var versionMatch = Regex.Match(toolResult.StandardOutput, @"(?m)^\s*vpk\s+(\S+)\s+vpk\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (!versionMatch.Success) throw new InvalidOperationException($"未发现全局 vpk。请安装与目标应用一致的版本：dotnet tool install --global vpk --version {project.ExpectedVelopackVersion}");
            var toolVersion = versionMatch.Groups[1].Value;
            if (!toolVersion.Equals(project.ExpectedVelopackVersion, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"全局 vpk {toolVersion} 与目标应用/项目要求 {project.ExpectedVelopackVersion} 不匹配；未自动升级。");
            var toolPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "vpk.exe");
            if (!File.Exists(toolPath)) throw new FileNotFoundException("全局 vpk.exe 不在用户 .dotnet/tools 标准目录，请修复全局工具安装。", toolPath);
            Directory.CreateDirectory(outputRoot);
            var runId = Guid.NewGuid().ToString("N");
            stageDirectory = Path.Combine(outputRoot, $".building-{runId}");
            Directory.CreateDirectory(stageDirectory);
            logPath = Path.Combine(stageDirectory, "build.log");
            lock (logLock) File.WriteAllText(logPath, string.Join("\r\n", messages) + "\r\n", new UTF8Encoding(true));
            Report("准备依赖", "验证完整离线安装器；作为旁置文件分发，不内嵌、不自动执行。", 15);
            var verifiedDependencies = new List<VerifiedDependency>();
            foreach (var dependency in project.Dependencies.Where(x => x.Delivery == DependencyDelivery.OfflineSidecar))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var originalPath = ProjectStore.ResolvePath(dependency.OfflineInstallerPath, projectFilePath);
                var dependencyDirectory = Path.Combine(stageDirectory, "prerequisites", dependency.Id);
                Directory.CreateDirectory(dependencyDirectory);
                var destinationPath = Path.Combine(dependencyDirectory, Path.GetFileName(originalPath));
                await CopyFileAsync(originalPath, destinationPath, cancellationToken).ConfigureAwait(false);
                var verified = await DependencyVerifier.VerifyAsync(dependency, destinationPath, cancellationToken).ConfigureAwait(false);
                verifiedDependencies.Add(verified with { FileName = Path.GetRelativePath(stageDirectory, destinationPath) });
                Report("准备依赖", $"已验证 {dependency.Name}：SHA256 {verified.Sha256}，签名 {verified.Signer}。文件版本 {verified.InstallerVersion} 是安装器版本。", 20);
            }
            var arguments = CreatePackArguments(project, projectFilePath, stageDirectory);
            Report("打包", $"vpk {toolVersion} pack 开始；保留生命周期检查，禁用差分包、便携包，生成当前用户 Setup。", 25);
            Report("打包参数", JsonSerializer.Serialize(arguments), 25);
            exitCode = null;
            var packResult = await ProcessRunner.RunAsync(new ProcessRequest { FileName = toolPath, Arguments = arguments, WorkingDirectory = stageDirectory, RemoveVelopackEnvironment = true, EnvironmentVariables = new Dictionary<string, string?> { ["NO_COLOR"] = "1", ["DOTNET_NOLOGO"] = "1" } }, line => Report("vpk", line.Text, null, line.IsError), cancellationToken).ConfigureAwait(false);
            exitCode = packResult.ExitCode;
            if (packResult.Canceled) throw new OperationCanceledException(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (packResult.ExitCode != 0) throw new InvalidOperationException($"vpk pack 失败，退出码 {packResult.ExitCode}。暂存目录仅用于诊断，不是可交付安装包。");
            Report("验证产物", "验证 Setup PE、完整包身份及版本、渠道清单和 SHA256。", 85);
            var artifacts = await VerifyArtifactsAsync(project, stageDirectory, cancellationToken).ConfigureAwait(false);
            var verifiedSetup = artifacts.Single(x => x.Kind == "Setup");
            foreach (var dependency in verifiedDependencies)
            {
                var path = Path.Combine(stageDirectory, dependency.FileName);
                artifacts.Add(new BuildArtifact("OfflinePrerequisite", path, new FileInfo(path).Length, dependency.Sha256));
            }
            await WriteDistributionNotesAsync(project, stageDirectory, verifiedDependencies, cancellationToken).ConfigureAwait(false);
            await WriteTextAsync(Path.Combine(stageDirectory, "build-manifest.json"), JsonSerializer.Serialize(new { schemaVersion = 1, productId = project.ProductId, version = project.Version, channel = project.Channel, architecture = project.Architecture.ToString(), velopackVersion = toolVersion, createdUtc = DateTimeOffset.UtcNow, signed = false, dependenciesEmbedded = false, onlineDependencies = project.Dependencies.Any(x => x.Delivery == DependencyDelivery.Online), updateUrlMetadataOnly = project.UpdateUrl, artifacts = artifacts.Select(x => new { x.Kind, FileName = Path.GetRelativePath(stageDirectory, x.Path), x.Size, x.Sha256 }) }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var finalDirectory = Path.Combine(outputRoot, $"{project.ProductId}-{project.Version}-{project.Channel}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{runId[..8]}");
            Report("完成验证", "产物验证通过，正在将本次独立构建目录标记为成功。", 98);
            Directory.Move(stageDirectory, finalDirectory);
            logPath = Path.Combine(finalDirectory, "build.log");
            var finalArtifacts = artifacts.Select(x => x with { Path = Path.Combine(finalDirectory, Path.GetRelativePath(stageDirectory, x.Path)) }).ToArray();
            var setupPath = Path.Combine(finalDirectory, Path.GetRelativePath(stageDirectory, verifiedSetup.Path));
            stageDirectory = finalDirectory;
            Report("成功", $"最终安装器：{setupPath}。构建不等于干净系统安装、卸载或升级数据保留验收。", 100);
            return new BuildResult { Success = true, ExitCode = exitCode, Message = "安装包已生成并完成产物完整性验证。" + logWriteError, OutputDirectory = finalDirectory, SetupPath = setupPath, LogPath = logPath, Artifacts = finalArtifacts };
        }
        catch (OperationCanceledException)
        {
            Report("已取消", "构建已取消；仅终止本次创建的子进程树，未发布半成品。", null, true);
            return new BuildResult { Canceled = true, ExitCode = exitCode, Message = "构建已取消。" + logWriteError, OutputDirectory = stageDirectory, LogPath = logPath };
        }
        catch (Exception ex)
        {
            Report("失败", ex.Message, null, true);
            return new BuildResult { ExitCode = exitCode, Message = ex.Message + logWriteError, OutputDirectory = stageDirectory, LogPath = logPath };
        }
    }

    public static IReadOnlyList<string> CreatePackArguments(InstallerProject project, string? projectFilePath, string stagingDirectory)
    {
        var arguments = new List<string> { "pack", "--packId", project.ProductId, "--packVersion", project.Version, "--packDir", ProjectStore.ResolvePath(project.PublishDirectory, projectFilePath), "--mainExe", project.MainExecutable, "--packTitle", project.DisplayName, "--packAuthors", project.Publisher, "--channel", project.Channel, "--runtime", $"win-{project.Architecture.ToString().ToLowerInvariant()}", "--outputDir", stagingDirectory, "--noPortable", "--delta", "None", "--instLocation", "PerUser" };
        var shortcuts = new List<string>();
        if (project.DesktopShortcut) shortcuts.Add("Desktop");
        if (project.StartMenuShortcut) shortcuts.Add("StartMenuRoot");
        arguments.AddRange(["--shortcuts", shortcuts.Count == 0 ? "None" : string.Join(',', shortcuts)]);
        if (!string.IsNullOrWhiteSpace(project.IconPath)) arguments.AddRange(["--icon", ProjectStore.ResolvePath(project.IconPath, projectFilePath)]);
        if (!string.IsNullOrWhiteSpace(project.ReleaseNotesPath)) arguments.AddRange(["--releaseNotes", ProjectStore.ResolvePath(project.ReleaseNotesPath, projectFilePath)]);
        var frameworks = new List<string>();
        foreach (var dependency in project.Dependencies.Where(x => x.Delivery == DependencyDelivery.Online))
        {
            if (!DependencyCatalog.TryGetFrameworkId(dependency, out var framework)) throw new InvalidDataException($"不支持在线依赖：{dependency.Name}");
            frameworks.Add(framework);
        }
        if (frameworks.Count > 0) arguments.AddRange(["--framework", string.Join(',', frameworks.Distinct(StringComparer.OrdinalIgnoreCase))]);
        return arguments;
    }

    private static async Task<List<BuildArtifact>> VerifyArtifactsAsync(InstallerProject project, string directory, CancellationToken cancellationToken)
    {
        var setupPath = Path.Combine(directory, $"{project.ProductId}-{project.Channel}-Setup.exe");
        if (!File.Exists(setupPath) || new FileInfo(setupPath).Length < 65536) throw new InvalidDataException("打包工具未生成有效的最终 Setup.exe。");
        using (var stream = File.OpenRead(setupPath))
        using (var pe = new PEReader(stream))
            if (pe.PEHeaders.PEHeader == null || (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.ExecutableImage) == 0) throw new InvalidDataException("Setup.exe 不是有效 PE 可执行文件。");
        var packagePaths = Directory.GetFiles(directory, "*-full.nupkg", SearchOption.TopDirectoryOnly);
        if (packagePaths.Length != 1) throw new InvalidDataException("本次独立构建必须生成且仅生成一个完整更新包。");
        var packagePath = packagePaths[0];
        using (var archive = ZipFile.OpenRead(packagePath))
        {
            var nuspec = archive.Entries.SingleOrDefault(x => x.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("完整包缺少 nuspec。");
            using var stream = nuspec.Open();
            var document = XDocument.Load(stream);
            var metadata = document.Root?.Elements().SingleOrDefault(x => x.Name.LocalName == "metadata") ?? throw new InvalidDataException("完整包 metadata 无效。");
            string Value(string name) => metadata.Elements().SingleOrDefault(x => x.Name.LocalName == name)?.Value ?? "";
            if (Value("id") != project.ProductId || Value("version") != project.Version) throw new InvalidDataException("完整包产品 ID 或版本与本次项目不匹配。");
            if (!archive.Entries.Any(x => x.FullName.Equals($"lib/app/{project.MainExecutable}", StringComparison.OrdinalIgnoreCase) && x.Length > 0)) throw new InvalidDataException("完整包未包含指定主程序。");
        }
        var manifestPath = Path.Combine(directory, $"releases.{project.Channel}.json");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false));
        var release = manifest.RootElement.GetProperty("Assets").EnumerateArray().SingleOrDefault(x => x.GetProperty("Type").GetString() == "Full" && x.GetProperty("FileName").GetString() == Path.GetFileName(packagePath));
        if (release.ValueKind != JsonValueKind.Object || release.GetProperty("PackageId").GetString() != project.ProductId || release.GetProperty("Version").GetString() != project.Version) throw new InvalidDataException("渠道清单缺少本次完整更新包的正确身份。");
        var packageHash = await HashAsync(packagePath, cancellationToken).ConfigureAwait(false);
        if (!packageHash.Equals(release.GetProperty("SHA256").GetString(), StringComparison.OrdinalIgnoreCase) || new FileInfo(packagePath).Length != release.GetProperty("Size").GetInt64()) throw new InvalidDataException("完整更新包大小或 SHA256 与渠道清单不匹配。");
        return [new("Setup", setupPath, new FileInfo(setupPath).Length, await HashAsync(setupPath, cancellationToken).ConfigureAwait(false)), new("FullPackage", packagePath, new FileInfo(packagePath).Length, packageHash), new("ReleaseManifest", manifestPath, new FileInfo(manifestPath).Length, await HashAsync(manifestPath, cancellationToken).ConfigureAwait(false))];
    }

    private static async Task WriteDistributionNotesAsync(InstallerProject project, string directory, IReadOnlyList<VerifiedDependency> dependencies, CancellationToken cancellationToken)
    {
        var notes = new StringBuilder($"# {project.DisplayName} {project.Version}\r\n\r\nSetup.exe 包含经过 Velopack 1.2.161 默认排除规则筛选的应用发布文件；PDB、createdump*、.vshost. 与 .nupkg 等匹配文件不会打入包。本次安装器未签名。\r\n\r\n");
        if (project.Dependencies.Any(x => x.Delivery == DependencyDelivery.Online)) notes.Append("此分发需要联网：Setup 使用 Velopack 官方预设检测并下载安装缺失依赖，不是完整离线安装。下载、检测、安装参数及退出码由 vpk 管理。\r\n\r\n");
        if (dependencies.Count > 0)
        {
            notes.Append("依赖安装器是独立旁置文件，未嵌入 Setup，也不会由本工具或 Setup 自动执行。请连同 prerequisites 文件夹一起分发，用户按供应商说明先手动安装缺失依赖，再运行 Setup。文件版本仅代表安装器，不能据此断言已安装 Runtime 版本。\r\n\r\n");
            foreach (var dependency in dependencies) notes.Append($"- {dependency.Name}: `{dependency.FileName}`；SHA256 `{dependency.Sha256}`；安装器文件版本 `{dependency.InstallerVersion}`。\r\n");
        }
        notes.Append("\r\n检测规则、最低系统/Runtime 版本、提权、安装参数、退出码及重启要求记录于 dependencies.json，旁置模式下供人工部署参考，不是已实现的自动检测安装逻辑。\r\n\r\n更新包和渠道清单已生成；更新源只是项目元数据，不会为目标应用添加更新 UI、后台更新或安全退出逻辑。用户持久数据应存放在应用安装目录之外；本工具不增加删除个人数据的卸载钩子。构建未验收干净 Windows、标准用户安装、卸载、升级数据保留或失败恢复。\r\n");
        await WriteTextAsync(Path.Combine(directory, "DISTRIBUTION.md"), notes.ToString(), cancellationToken).ConfigureAwait(false);
        var declarations = ProjectStore.Clone(project).Dependencies;
        foreach (var dependency in declarations.Where(x => x.Delivery == DependencyDelivery.OfflineSidecar)) dependency.OfflineInstallerPath = dependencies.Single(x => x.Id == dependency.Id).FileName;
        await WriteTextAsync(Path.Combine(directory, "dependencies.json"), JsonSerializer.Serialize(new { schemaVersion = 1, automaticOfflineInstallation = false, verifiedOfflineInstallers = dependencies, declarations }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static Task WriteTextAsync(string path, string text, CancellationToken cancellationToken) => File.WriteAllTextAsync(path, text.ReplaceLineEndings("\r\n") + "\r\n", new UTF8Encoding(true), cancellationToken);

    private static Version NormalizeRuntimeVersion(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
