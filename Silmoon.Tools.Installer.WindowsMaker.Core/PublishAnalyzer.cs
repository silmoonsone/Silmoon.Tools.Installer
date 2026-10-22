using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Silmoon.Tools.Installer.WindowsMaker.Core;

public sealed class PublishAnalyzer
{
    public Task<PublishAnalysis> AnalyzeAsync(string directory, string mainExecutable, CancellationToken ct = default) => Task.Run(() => Analyze(directory, mainExecutable, ct), ct);

    private static PublishAnalysis Analyze(string directory, string mainExecutable, CancellationToken ct)
    {
        var result = new PublishAnalysis { Directory = directory, MainExecutable = mainExecutable };
        if (!System.IO.Directory.Exists(directory))
        {
            result.Issues.Add(new(IssueSeverity.Error, "analysis.directory", "发布目录不存在。"));
            return result;
        }
        if (string.IsNullOrWhiteSpace(mainExecutable) || mainExecutable.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            result.Issues.Add(new(IssueSeverity.Error, "analysis.main", "请选择发布目录根部的主 EXE 文件名。"));
            return result;
        }
        directory = Path.GetFullPath(directory);
        result.Directory = directory;
        var mainPath = Path.Combine(directory, mainExecutable);
        if (!File.Exists(mainPath))
        {
            result.Issues.Add(new(IssueSeverity.Error, "analysis.main.missing", "发布目录中不存在主 EXE。"));
            return result;
        }
        string[] files;
        try { files = System.IO.Directory.GetFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Issues.Add(new(IssueSeverity.Error, "analysis.enumeration", $"读取发布目录失败：{ex.Message}"));
            return result;
        }
        var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var topLevel = files.Where(x => string.Equals(Path.GetDirectoryName(x), Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), StringComparison.OrdinalIgnoreCase)).ToArray();
        var topNames = topLevel.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = Path.GetFileNameWithoutExtension(mainExecutable);
        var runtimeConfig = Path.Combine(directory, stem + ".runtimeconfig.json");
        if (!File.Exists(runtimeConfig)) runtimeConfig = topLevel.SingleOrDefaultSafe(x => x.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)) ?? "";
        if (runtimeConfig.Length != 0) ReadRuntimeConfig(runtimeConfig, result);
        else result.Issues.Add(new(IssueSeverity.Warning, "analysis.runtimeconfig.missing", "没有找到唯一对应的 runtimeconfig，无法仅从配置确定 .NET 运行方式；原生应用可能没有此文件。"));
        var depsPath = Path.Combine(directory, stem + ".deps.json");
        if (!File.Exists(depsPath)) depsPath = topLevel.SingleOrDefaultSafe(x => x.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)) ?? "";
        if (depsPath.Length != 0) ReadDeps(depsPath, result);
        else result.Issues.Add(new(IssueSeverity.Warning, "analysis.deps.missing", "没有找到唯一对应的 deps.json，无法确认依赖包版本。"));
        result.IsDotNetSelfContained = topNames.Contains("coreclr.dll") && topNames.Contains("System.Private.CoreLib.dll") && topNames.Contains("hostpolicy.dll");
        result.HasWindowsAppSdk |= names.Contains("Microsoft.UI.Xaml.dll") || names.Contains("Microsoft.WindowsAppRuntime.Bootstrap.dll");
        result.IsWindowsAppSdkSelfContained = topNames.Contains("Microsoft.UI.Xaml.dll") && (topNames.Contains("Microsoft.WindowsAppRuntime.dll") || topNames.Contains("WindowsAppRuntime.dll"));
        try
        {
            var info = FileVersionInfo.GetVersionInfo(mainPath);
            result.MainFileVersion = info.FileVersion ?? "";
            result.MainProductName = info.ProductName ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { result.Issues.Add(new(IssueSeverity.Warning, "analysis.resources", $"无法读取主程序版本资源：{ex.Message}")); }
        var imports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            ct.ThrowIfCancellationRequested();
            var isMain = string.Equals(file, mainPath, StringComparison.OrdinalIgnoreCase);
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var pe = new PEReader(stream);
                if (pe.PEHeaders.PEHeader is null)
                {
                    if (isMain) result.Issues.Add(new(IssueSeverity.Error, "analysis.main.not-pe", "主程序不是有效的 Windows PE 文件。"));
                    continue;
                }
                if (isMain) result.Architecture = GetArchitecture(pe);
                foreach (var import in ReadImports(pe, false).Concat(ReadImports(pe, true))) imports.Add(import);
                if (pe.HasMetadata && (isMain || string.Equals(Path.GetFileName(file), stem + ".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    var metadata = pe.GetMetadataReader();
                    foreach (var handle in metadata.AssemblyReferences)
                    {
                        var reference = metadata.GetAssemblyReference(handle);
                        if (metadata.GetString(reference.Name) == "Velopack") result.HasVelopackReference = true;
                    }
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                result.Issues.Add(new(isMain ? IssueSeverity.Error : IssueSeverity.Warning, "analysis.pe", $"无法完整检查 {Path.GetRelativePath(directory, file)}：{ex.Message}"));
            }
        }
        result.NativeImports.AddRange(imports.Order(StringComparer.OrdinalIgnoreCase));
        if (result.Architecture is null) result.Issues.Add(new(IssueSeverity.Warning, "analysis.architecture.unknown", "主程序的固定目标架构无法确定，需人工确认配置；托管 AnyCPU 不能证明部署架构。"));
        else result.Issues.Add(new(IssueSeverity.Info, "analysis.architecture", $"主程序 PE 架构：{result.Architecture}。"));
        if (result.IsDotNetSelfContained) result.Issues.Add(new(IssueSeverity.Info, "analysis.dotnet.bundled", "发现 coreclr.dll、hostpolicy.dll 与 System.Private.CoreLib.dll，具有 .NET 自包含发布证据。"));
        else if (result.TargetFramework.Length != 0) result.Suggestions.Add("未找到完整的 .NET 自包含文件证据。请核对 runtimeconfig 声明，并配置对应 Runtime，或使用 SelfContained=true 重新发布。");
        if (result.IsWindowsAppSdkSelfContained) result.Issues.Add(new(IssueSeverity.Info, "analysis.wasdk.bundled", "发布目录包含 Microsoft.UI.Xaml.dll 和 Windows App Runtime 主模块，具有 Windows App SDK 自包含证据。"));
        else if (result.HasWindowsAppSdk) result.Suggestions.Add("发现 Windows App SDK 引用，但未确认完整自包含发布；请核对 WindowsAppSDKSelfContained=true，或配置经过验证的完整离线依赖安装器。");
        var crt = imports.Where(x => Regex.IsMatch(x, "^(vcruntime|msvcp|concrt)[0-9].*\\.dll$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        if (crt.Length == 0) result.Issues.Add(new(IssueSeverity.Info, "analysis.vc.no-evidence", "本次 PE 普通导入及延迟导入扫描未发现 VC CRT 动态依赖证据；这不能排除动态加载或未来版本新增依赖。"));
        else
        {
            var missing = crt.Where(x => !names.Contains(x)).ToArray();
            if (missing.Length != 0) result.Suggestions.Add($"发现 VC CRT 导入且发布目录未见对应文件：{string.Join(", ", missing)}。请确认目标系统和 VC Redistributable 需求。");
            else result.Issues.Add(new(IssueSeverity.Info, "analysis.vc.bundled", "发现 VC CRT 导入，也在发布目录找到同名文件；请核实应用实际 DLL 搜索路径与分发许可。"));
        }
        if (result.HasVelopackReference) result.Issues.Add(new(IssueSeverity.Info, "analysis.velopack.reference", $"主程序程序集引用 Velopack，deps 版本：{result.VelopackVersion ?? "未知"}。引用并不能证明启动顺序正确，构建仍须通过 vpk VeloAppCheck。"));
        else result.Suggestions.Add("未发现主程序直接引用 Velopack 的静态证据。目标应用需要在框架启动前调用 VelopackApp.Build().SetArgs(args).Run()；请保留 STAThread 并使用项目支持的入口，不修改 obj 生成源码。最终由 vpk VeloAppCheck 验证，本工具不修改业务代码。");
        result.Suggestions.Add("静态分析不执行目标应用，不能枚举所有动态加载、插件、数据库服务或 WebView2 运行需求。请人工补充依赖并在干净系统验收。");
        return result;
    }

    private static void ReadRuntimeConfig(string path, PublishAnalysis result)
    {
        try
        {
            using var document = ReadJson(path);
            if (!document.RootElement.TryGetProperty("runtimeOptions", out var options)) throw new InvalidDataException("缺少 runtimeOptions。");
            if (options.TryGetProperty("tfm", out var tfm)) result.TargetFramework = tfm.GetString() ?? "";
            foreach (var name in new[] { "framework", "frameworks", "includedFrameworks" })
            {
                if (!options.TryGetProperty(name, out var frameworks)) continue;
                var entries = frameworks.ValueKind == JsonValueKind.Array ? frameworks.EnumerateArray().ToArray() : [frameworks];
                foreach (var framework in entries)
                {
                    var frameworkName = framework.TryGetProperty("name", out var n) ? n.GetString() : "未知框架";
                    var version = framework.TryGetProperty("version", out var v) ? v.GetString() : "未知版本";
                    if (name != "includedFrameworks" && frameworkName is not null && version is not null) result.RequiredFrameworks.Add(new(frameworkName, version));
                    result.Issues.Add(new(IssueSeverity.Info, "analysis.runtimeconfig.framework", $"runtimeconfig {name}：{frameworkName} {version}。"));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException) { result.Issues.Add(new(IssueSeverity.Error, "analysis.runtimeconfig.invalid", $"runtimeconfig 无效：{ex.Message}")); }
    }

    private static void ReadDeps(string path, PublishAnalysis result)
    {
        try
        {
            using var document = ReadJson(path);
            if (document.RootElement.TryGetProperty("runtimeTarget", out var target) && target.TryGetProperty("name", out var runtimeName))
            {
                var value = runtimeName.GetString() ?? "";
                if (value.Contains('/')) result.RuntimeIdentifier = value[(value.LastIndexOf('/') + 1)..];
            }
            if (document.RootElement.TryGetProperty("libraries", out var libraries))
            {
                foreach (var library in libraries.EnumerateObject())
                {
                    if (library.Name.StartsWith("Velopack/", StringComparison.OrdinalIgnoreCase)) result.VelopackVersion = library.Name["Velopack/".Length..];
                    if (library.Name.StartsWith("Microsoft.WindowsAppSDK", StringComparison.OrdinalIgnoreCase)) result.HasWindowsAppSdk = true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException) { result.Issues.Add(new(IssueSeverity.Error, "analysis.deps.invalid", $"deps.json 无效：{ex.Message}")); }
    }

    private static JsonDocument ReadJson(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("依赖元数据超过 32 MiB，拒绝读取。");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static TargetArchitecture? GetArchitecture(PEReader pe)
    {
        var anyCpu = pe.PEHeaders.CoffHeader.Machine == Machine.I386 && pe.PEHeaders.CorHeader is { } cor && (cor.Flags & CorFlags.ILOnly) != 0 && (cor.Flags & CorFlags.Requires32Bit) == 0;
        if (anyCpu) return null;
        return pe.PEHeaders.CoffHeader.Machine switch { Machine.Amd64 => TargetArchitecture.X64, Machine.I386 => TargetArchitecture.X86, Machine.Arm64 => TargetArchitecture.Arm64, _ => null };
    }

    private static IEnumerable<string> ReadImports(PEReader pe, bool delay)
    {
        var directory = delay ? pe.PEHeaders.PEHeader!.DelayImportTableDirectory : pe.PEHeaders.PEHeader!.ImportTableDirectory;
        if (directory.RelativeVirtualAddress == 0 || directory.Size == 0) yield break;
        var reader = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader();
        var descriptorSize = delay ? 32 : 20;
        var count = Math.Min(4096, Math.Min(reader.RemainingBytes, directory.Size) / descriptorSize);
        for (var i = 0; i < count; i++)
        {
            var values = new uint[descriptorSize / 4];
            for (var j = 0; j < values.Length; j++) values[j] = reader.ReadUInt32();
            if (values.All(x => x == 0)) yield break;
            var nameAddress = (ulong)values[delay ? 1 : 3];
            if (delay && (values[0] & 1) == 0)
            {
                if (nameAddress < pe.PEHeaders.PEHeader!.ImageBase) continue;
                nameAddress -= pe.PEHeaders.PEHeader.ImageBase;
            }
            if (nameAddress is 0 or > int.MaxValue) continue;
            var nameReader = pe.GetSectionData((int)nameAddress).GetReader();
            var name = new StringBuilder();
            for (var j = 0; j < 512 && nameReader.RemainingBytes > 0; j++)
            {
                var value = nameReader.ReadByte();
                if (value == 0) break;
                name.Append((char)value);
            }
            if (name.Length > 0) yield return name.ToString();
        }
    }
}

internal static class AnalysisEnumerableExtensions
{
    public static T? SingleOrDefaultSafe<T>(this IEnumerable<T> values, Func<T, bool> predicate) where T : class
    {
        var matches = values.Where(predicate).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
