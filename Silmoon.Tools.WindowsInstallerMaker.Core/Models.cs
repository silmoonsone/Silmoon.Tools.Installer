using System.ComponentModel.DataAnnotations;

namespace Silmoon.Tools.WindowsInstallerMaker.Core;

public enum TargetArchitecture
{
    [Display(Name = "x64")] X64,
    [Display(Name = "x86")] X86,
    [Display(Name = "ARM64")] Arm64
}

public enum InstallScope
{
    [Display(Name = "当前用户")] CurrentUser,
    [Display(Name = "所有用户（未支持）")] AllUsers
}

public enum DistributionMode
{
    [Display(Name = "完整应用安装包")] FullApplication,
    [Display(Name = "小型在线入口（未支持）")] OnlineBootstrap
}

public enum SigningMode
{
    [Display(Name = "不签名")] None,
    [Display(Name = "外部签名（未支持）")] External
}

public enum DependencyKind
{
    [Display(Name = "Visual C++ Redistributable")] VisualCpp,
    [Display(Name = ".NET Runtime")] DotNetRuntime,
    [Display(Name = ".NET Desktop Runtime")] DotNetDesktopRuntime,
    [Display(Name = ".NET Framework")] DotNetFramework,
    [Display(Name = "Windows App SDK Runtime")] WindowsAppSdk,
    [Display(Name = "WebView2 Runtime")] WebView2,
    [Display(Name = "自定义组件")] Custom,
    [Display(Name = "数据库客户端库")] DatabaseClient,
    [Display(Name = "嵌入式数据库")] EmbeddedDatabase,
    [Display(Name = "数据库服务")] DatabaseService,
    [Display(Name = ".NET SDK（高级开发工具）")] DotNetSdk,
    [Display(Name = "Windows SDK（高级开发工具）")] WindowsSdk
}

public enum DependencyDelivery
{
    [Display(Name = "已随应用携带")] Bundled,
    [Display(Name = "缺失时在线安装")] Online,
    [Display(Name = "完整离线安装器（同目录，手动安装）")] OfflineSidecar
}

public enum RebootPolicy
{
    [Display(Name = "不要求重启")] Never,
    [Display(Name = "按安装器要求")] IfRequired,
    [Display(Name = "需要重启")] Always
}

public enum IssueSeverity { Info, Warning, Error }

public sealed class InstallerProject
{
    public int SchemaVersion { get; set; } = 1;
    public string ProductId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string PublishDirectory { get; set; } = "";
    public string MainExecutable { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public TargetArchitecture Architecture { get; set; } = TargetArchitecture.X64;
    public string Channel { get; set; } = "win-x64";
    public string IconPath { get; set; } = "";
    public bool DesktopShortcut { get; set; } = true;
    public bool StartMenuShortcut { get; set; } = true;
    public InstallScope InstallScope { get; set; } = InstallScope.CurrentUser;
    public DistributionMode DistributionMode { get; set; } = DistributionMode.FullApplication;
    public string UpdateUrl { get; set; } = "";
    public string ReleaseNotesPath { get; set; } = "";
    public SigningMode SigningMode { get; set; } = SigningMode.None;
    public string ExpectedVelopackVersion { get; set; } = "1.2.161";
    public List<DependencyDefinition> Dependencies { get; set; } = [];
    public string Notes { get; set; } = "";
    public bool PreserveUserData { get; set; } = true;
    public bool GenerateDelta { get; set; }
}

public sealed class DependencyDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DependencyKind Kind { get; set; } = DependencyKind.Custom;
    public DependencyDelivery Delivery { get; set; } = DependencyDelivery.Bundled;
    public string MinimumVersion { get; set; } = "";
    public TargetArchitecture Architecture { get; set; } = TargetArchitecture.X64;
    public string MinimumWindowsVersion { get; set; } = "";
    public string Detection { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string OfflineInstallerPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string ExpectedSigner { get; set; } = "";
    public string ExpectedInstallerVersion { get; set; } = "";
    public List<string> InstallArguments { get; set; } = [];
    public List<int> SuccessExitCodes { get; set; } = [0];
    public List<int> RebootExitCodes { get; set; } = [1641, 3010];
    public bool RequiresElevation { get; set; }
    public RebootPolicy RebootPolicy { get; set; } = RebootPolicy.IfRequired;
    public string FrameworkId { get; set; } = "";
    public bool AdvancedDevelopmentTool { get; set; }
    public string Notes { get; set; } = "";
}

public sealed record ValidationIssue(IssueSeverity Severity, string Code, string Message, string? Property = null);

public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = [];
    public bool IsValid => Issues.All(x => x.Severity != IssueSeverity.Error);
}

public sealed class PublishAnalysis
{
    public string Directory { get; set; } = "";
    public string MainExecutable { get; set; } = "";
    public TargetArchitecture? Architecture { get; set; }
    public bool IsDotNetSelfContained { get; set; }
    public bool HasWindowsAppSdk { get; set; }
    public bool IsWindowsAppSdkSelfContained { get; set; }
    public bool HasVelopackReference { get; set; }
    public string? VelopackVersion { get; set; }
    public string MainFileVersion { get; set; } = "";
    public string MainProductName { get; set; } = "";
    public string TargetFramework { get; set; } = "";
    public string RuntimeIdentifier { get; set; } = "";
    public List<RuntimeRequirement> RequiredFrameworks { get; } = [];
    public List<string> NativeImports { get; } = [];
    public List<ValidationIssue> Issues { get; } = [];
    public List<string> Suggestions { get; } = [];
}

public sealed record RuntimeRequirement(string Name, string Version);
