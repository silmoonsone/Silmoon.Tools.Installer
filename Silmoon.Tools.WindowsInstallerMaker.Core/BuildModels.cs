namespace Silmoon.Tools.WindowsInstallerMaker.Core;

public sealed record BuildProgress(string Stage, string Message, double? Percent = null, bool IsError = false);

public sealed record BuildArtifact(string Kind, string Path, long Size, string Sha256);

public sealed class BuildResult
{
    public bool Success { get; init; }
    public bool Canceled { get; init; }
    public int? ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string SetupPath { get; init; } = "";
    public string LogPath { get; init; } = "";
    public IReadOnlyList<BuildArtifact> Artifacts { get; init; } = [];
}
