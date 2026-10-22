using System.Text.RegularExpressions;

namespace Silmoon.Tools.Installer.Core;

public sealed class ProjectValidator
{
    public ValidationResult Validate(InstallerProject project, string? projectPath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var result = new ValidationResult();
        void Error(string code, string message, string? property = null) => result.Issues.Add(new(IssueSeverity.Error, code, message, property));
        void Warning(string code, string message, string? property = null) => result.Issues.Add(new(IssueSeverity.Warning, code, message, property));
        if (!CheckShape(project, result)) return result;
        if (project.SchemaVersion != ProjectStore.CurrentSchemaVersion) Error("schema.unsupported", $"只支持 schemaVersion={ProjectStore.CurrentSchemaVersion}。", nameof(project.SchemaVersion));
        if (!IsSafeId(project.ProductId)) Error("product.id", "产品 ID 必须是 1–100 个字母、数字、点、下划线或连字符，并以字母或数字开头。", nameof(project.ProductId));
        if (string.IsNullOrWhiteSpace(project.DisplayName)) Error("product.name", "请填写显示名称。", nameof(project.DisplayName));
        if (string.IsNullOrWhiteSpace(project.Publisher)) Error("product.publisher", "请填写发布者。", nameof(project.Publisher));
        if (!Regex.IsMatch(project.Version, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)) Error("product.version", "版本必须是三段版本号，可带预发布后缀，例如 1.0.0 或 0.1.0-preview.1。", nameof(project.Version));
        if (!Regex.IsMatch(project.Channel, "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)) Error("product.channel", "请填写稳定的渠道标识，只允许字母、数字、点、下划线和连字符。", nameof(project.Channel));
        if (!Regex.IsMatch(project.ExpectedVelopackVersion, "^[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)) Error("engine.version", "请填写预期 vpk 版本，例如 1.2.161。", nameof(project.ExpectedVelopackVersion));
        if (!Enum.IsDefined(project.Architecture)) Error("architecture.unknown", "不支持该目标架构。", nameof(project.Architecture));
        if (project.InstallScope != InstallScope.CurrentUser) Error("scope.unsupported", "当前引擎适配只支持当前用户安装，不能构建所有用户安装包。", nameof(project.InstallScope));
        if (project.DistributionMode != DistributionMode.FullApplication) Error("distribution.unsupported", "第一版只支持包含完整应用文件的 Setup.exe，不支持小型在线入口。", nameof(project.DistributionMode));
        if (project.SigningMode != SigningMode.None) Error("signing.unsupported", "第一版尚未实现签名执行，不能忽略该设置继续构建。请改为不签名，或使用外部签名流程。", nameof(project.SigningMode));
        if (project.GenerateDelta) Error("delta.unsupported", "第一版只生成完整更新包与版本清单，尚未实现差分包生成与基线管理。", nameof(project.GenerateDelta));
        if (!project.PreserveUserData) Error("data.delete.unsupported", "本工具保留用户数据，不提供卸载时删除数据库、账户、源码或脚本的行为。", nameof(project.PreserveUserData));
        if (project.UpdateUrl.Length != 0)
        {
            if (!IsPublicHttps(project.UpdateUrl)) Error("update.url", "更新地址必须为无凭据和查询参数的 HTTPS URL；配置中不能保存访问令牌。", nameof(project.UpdateUrl));
            else Warning("update.application.required", "更新源仅作为分发元数据记录；目标应用仍须接入更新 UI、安全退出及更新应用流程，本工具不会开启后台升级。", nameof(project.UpdateUrl));
        }
        var publishDirectory = Resolve(project.PublishDirectory, projectPath, nameof(project.PublishDirectory), result);
        var outputDirectory = Resolve(project.OutputDirectory, projectPath, nameof(project.OutputDirectory), result);
        if (publishDirectory.Length == 0 || !Directory.Exists(publishDirectory)) Error("publish.missing", "发布目录不存在，请选择已经完成 Release publish 的目录。", nameof(project.PublishDirectory));
        if (outputDirectory.Length == 0) Error("output.required", "请选择输出目录。", nameof(project.OutputDirectory));
        else if (File.Exists(outputDirectory)) Error("output.file", "输出路径指向文件，必须选择目录。", nameof(project.OutputDirectory));
        if (publishDirectory.Length != 0 && outputDirectory.Length != 0 && IsWithin(outputDirectory, publishDirectory)) Error("output.inside.publish", "输出目录不能等于发布目录或位于其内部，以免把构建产物再次打包。", nameof(project.OutputDirectory));
        var mainIsFileName = project.MainExecutable.Length > 0 && project.MainExecutable.IndexOfAny(['/', '\\', ':']) < 0 && project.MainExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        if (!mainIsFileName) Error("main.invalid", "主程序必须是发布目录根部的 EXE 文件名。", nameof(project.MainExecutable));
        else if (publishDirectory.Length != 0 && !File.Exists(Path.Combine(publishDirectory, project.MainExecutable))) Error("main.missing", "发布目录内找不到所选主 EXE。", nameof(project.MainExecutable));
        CheckOptionalFile(project.IconPath, ".ico", nameof(project.IconPath), projectPath, result);
        CheckOptionalFile(project.ReleaseNotesPath, null, nameof(project.ReleaseNotesPath), projectPath, result);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in project.Dependencies)
        {
            if (!CheckShape(dependency, result)) continue;
            var label = string.IsNullOrWhiteSpace(dependency.Name) ? dependency.Id : dependency.Name;
            if (!IsSafeId(dependency.Id) || !ids.Add(dependency.Id)) Error("dependency.id", $"依赖 {label} 的 ID 无效或重复；只能使用以字母或数字开头的 1–100 个字母、数字、点、下划线和连字符，不能使用 Windows 保留设备名或以点结尾。", nameof(project.Dependencies));
            if (!Enum.IsDefined(dependency.Kind) || !Enum.IsDefined(dependency.Delivery) || !Enum.IsDefined(dependency.Architecture) || !Enum.IsDefined(dependency.RebootPolicy)) Error("dependency.enum", $"依赖 {label} 使用了不支持的枚举值。");
            if (dependency.Kind is DependencyKind.DotNetSdk or DependencyKind.WindowsSdk && !dependency.AdvancedDevelopmentTool) Error("dependency.sdk", $"{label} 是开发工具，需要显式开启 AdvancedDevelopmentTool 高级选项；普通用户不应默认安装 SDK。");
            if (dependency.MinimumWindowsVersion.Length != 0 && !System.Version.TryParse(dependency.MinimumWindowsVersion, out _)) Error("dependency.windows.version", $"{label} 的最低 Windows 版本格式无效。");
            if (dependency.MinimumVersion.Length != 0 && !System.Version.TryParse(dependency.MinimumVersion, out _)) Error("dependency.minimum.version", $"{label} 的最低版本格式无效。");
            if (dependency.DownloadUrl.Length != 0 && !IsPublicHttps(dependency.DownloadUrl, true)) Error("dependency.source", $"{label} 的来源必须为无凭据和查询参数的 HTTPS URL。");
            if (dependency.Delivery == DependencyDelivery.Online)
            {
                if (!DependencyCatalog.TryGetFrameworkId(dependency, out var framework)) Error("dependency.online.unsupported", $"{label} 没有受支持且与类别、架构、最低版本匹配的 Velopack 在线框架预设。");
                else Warning("dependency.online.network", $"{label} 使用 Velopack --framework {framework}，缺失时联网下载；这不是完整离线依赖安装。");
                var customOnlineSettings = dependency.DownloadUrl.Length != 0 || dependency.OfflineInstallerPath.Length != 0 || dependency.Sha256.Length != 0 || dependency.ExpectedSigner.Length != 0 || dependency.ExpectedInstallerVersion.Length != 0 || dependency.InstallArguments.Count != 0 || dependency.Detection.Length != 0 || dependency.MinimumWindowsVersion.Length != 0 || !dependency.SuccessExitCodes.SequenceEqual([0]) || !dependency.RebootExitCodes.SequenceEqual([1641, 3010]) || dependency.RequiresElevation || dependency.RebootPolicy != RebootPolicy.IfRequired;
                if (customOnlineSettings) Error("dependency.online.overrides", $"{label} 的在线安装由引擎管理，不能应用自定义检测、下载、哈希、签名、安装参数或系统版本限制；请清空这些覆盖项，或选择离线旁置模式。");
                if (dependency.Architecture != project.Architecture) Error("dependency.architecture", $"{label} 的在线框架架构必须与应用目标架构一致。");
            }
            else if (dependency.Delivery == DependencyDelivery.OfflineSidecar)
            {
                var installer = Resolve(dependency.OfflineInstallerPath, projectPath, nameof(dependency.OfflineInstallerPath), result);
                if (installer.Length == 0 || !File.Exists(installer)) Error("dependency.offline.missing", $"{label} 的离线安装器不存在。");
                if (installer.Length != 0 && Path.GetExtension(installer).ToLowerInvariant() is not (".exe" or ".msi")) Error("dependency.offline.type", $"{label} 的完整离线安装器应为 EXE 或 MSI。");
                if (!Regex.IsMatch(dependency.Sha256, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)) Error("dependency.hash", $"{label} 必须提供完整的 64 位 SHA256，构建会核验实际分发文件。");
                if (string.IsNullOrWhiteSpace(dependency.ExpectedSigner)) Error("dependency.signer", $"{label} 必须提供预期签名者，构建会检查有效 Authenticode 签名。");
                Warning("dependency.offline.manual", $"{label} 作为完整离线安装器放在 Setup 旁边，需要用户先手动安装；不会内嵌或自动执行，检测、最低版本、参数与重启信息作为操作说明保存。");
            }
            else if (dependency.Delivery == DependencyDelivery.Bundled)
            {
                if (dependency.OfflineInstallerPath.Length != 0 || dependency.FrameworkId.Length != 0) Error("dependency.bundled.settings", $"{label} 已随应用携带，不能同时配置离线安装器或在线框架 ID。");
                result.Issues.Add(new(IssueSeverity.Info, "dependency.bundled", $"{label} 声明随应用携带；请确认真实发布产物及其许可，静态分析不能保证发现所有动态依赖。"));
            }
        }
        Warning("signing.unsigned", "当前输出不进行代码签名，Windows 可能显示未知发布者提示。");
        return result;
    }

    private static bool CheckShape(object value, ValidationResult result)
    {
        var valid = true;
        foreach (var property in value.GetType().GetProperties())
        {
            var item = property.GetValue(value);
            if (property.PropertyType.IsValueType) continue;
            if (item is null || item is System.Collections.IEnumerable collection && item is not string && collection.Cast<object?>().Any(x => x is null))
            {
                result.Issues.Add(new(IssueSeverity.Error, "config.null", $"配置字段 {property.Name} 及其数组元素不能为 null。", property.Name));
                valid = false;
            }
        }
        return valid;
    }

    private static string Resolve(string path, string? projectPath, string property, ValidationResult result)
    {
        try { return ProjectStore.ResolvePath(path, projectPath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            result.Issues.Add(new(IssueSeverity.Error, "path.invalid", $"{property} 路径无效：{ex.Message}", property));
            return "";
        }
    }

    private static void CheckOptionalFile(string path, string? extension, string property, string? projectPath, ValidationResult result)
    {
        if (path.Length == 0) return;
        var resolved = Resolve(path, projectPath, property, result);
        if (!File.Exists(resolved)) result.Issues.Add(new(IssueSeverity.Error, "file.missing", $"{property} 文件不存在：{path}", property));
        if (extension is not null && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) result.Issues.Add(new(IssueSeverity.Error, "file.type", $"{property} 必须使用 {extension} 文件。", property));
    }

    private static bool IsWithin(string path, string directory) => string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(directory), StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool IsSafeId(string id) => Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant) && !id.EndsWith('.') && !Regex.IsMatch(id, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static bool IsPublicHttps(string value, bool allowMicrosoftDownloadLink = false)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        return uri.Query.Length == 0 || allowMicrosoftDownloadLink && uri.Host.Equals("go.microsoft.com", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(uri.Query, "^\\?LinkId=[0-9]+(?:&clcid=0x[0-9a-f]+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
