using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Silmoon.Tools.Installer.WindowsMaker.Core;

public sealed record VerifiedDependency(string Id, string Name, string FileName, string Sha256, string Signer, string InstallerVersion, string SourceUrl, string MinimumRuntimeVersion);

public sealed class DependencyVerifier
{
    private IProcessRunner ProcessRunner { get; }

    public DependencyVerifier(IProcessRunner? processRunner = null) => ProcessRunner = processRunner ?? new ProcessRunner();

    public async Task<VerifiedDependency> VerifyAsync(DependencyDefinition dependency, string installerPath, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("离线安装器签名验证需要 Windows。");
        await using var stream = File.OpenRead(installerPath);
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!sha256.Equals(dependency.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"依赖 {dependency.Name} SHA256 不匹配。");
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        const string script = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $s=Get-AuthenticodeSignature -LiteralPath $env:SWIM_VERIFY_FILE; [pscustomobject]@{Status=$s.Status.ToString();Subject=$s.SignerCertificate.Subject;FileVersion=[System.Diagnostics.FileVersionInfo]::GetVersionInfo($env:SWIM_VERIFY_FILE).FileVersion} | ConvertTo-Json -Compress";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var result = await ProcessRunner.RunAsync(new ProcessRequest { FileName = powershell, Arguments = ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script], EnvironmentVariables = new Dictionary<string, string?> { ["SWIM_VERIFY_FILE"] = installerPath } }, cancellationToken: timeout.Token).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Canceled) throw new TimeoutException($"依赖 {dependency.Name} 的 Authenticode 验证超时。");
        if (result.ExitCode != 0) throw new InvalidDataException($"依赖 {dependency.Name} 签名验证失败（退出码 {result.ExitCode}）：{result.StandardError.Trim()}");
        using var document = JsonDocument.Parse(result.StandardOutput.Trim().TrimStart('\uFEFF'));
        var status = document.RootElement.GetProperty("Status").GetString();
        var signer = document.RootElement.GetProperty("Subject").GetString() ?? "";
        var version = document.RootElement.GetProperty("FileVersion").GetString() ?? "";
        if (status != "Valid") throw new InvalidDataException($"依赖 {dependency.Name} 的 Authenticode 状态为 {status}，必须为 Valid。");
        var expected = dependency.ExpectedSigner.Trim();
        if (expected.Length == 0 || (!signer.Equals(expected, StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(signer, $"(?:^|,\\s*)(?:CN|O)=\"?{Regex.Escape(expected)}\"?(?:,|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))) throw new InvalidDataException($"依赖 {dependency.Name} 签名者不匹配：{signer}");
        if (!string.IsNullOrWhiteSpace(dependency.ExpectedInstallerVersion) && !version.Equals(dependency.ExpectedInstallerVersion, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"依赖 {dependency.Name} 安装器文件版本不匹配：{version}");
        return new VerifiedDependency(dependency.Id, dependency.Name, Path.GetFileName(installerPath), sha256, signer, version, dependency.DownloadUrl, dependency.MinimumVersion);
    }
}
