using System.Diagnostics;

namespace Silmoon.Tools.WindowsInstallerMaker.Core;

public sealed class ProcessRequest
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public bool RemoveVelopackEnvironment { get; init; }
    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } = new Dictionary<string, string?>();
}

public sealed record ProcessOutput(string Text, bool IsError);
public sealed record ProcessResult(int ExitCode, bool Canceled, string StandardOutput, string StandardError);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutput>? output = null, CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, Action<ProcessOutput>? output = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(request.FileName) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)) startInfo.WorkingDirectory = request.WorkingDirectory;
        foreach (var argument in request.Arguments) startInfo.ArgumentList.Add(argument);
        if (request.RemoveVelopackEnvironment)
            foreach (var key in startInfo.Environment.Keys.Where(x => x.StartsWith("VPK_", StringComparison.OrdinalIgnoreCase)).ToArray()) startInfo.Environment.Remove(key);
        foreach (var pair in request.EnvironmentVariables) startInfo.Environment[pair.Key] = pair.Value;
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException($"无法启动进程：{request.FileName}");
        process.StandardInput.Close();
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        var stdoutTask = ReadOutputAsync(process.StandardOutput, stdout, false, output);
        var stderrTask = ReadOutputAsync(process.StandardError, stderr, true, output);
        var canceled = false;
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            canceled = true;
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, canceled, stdout.ToString(), stderr.ToString());
    }

    private static async Task ReadOutputAsync(StreamReader reader, System.Text.StringBuilder buffer, bool isError, Action<ProcessOutput>? output)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            buffer.AppendLine(line);
            output?.Invoke(new ProcessOutput(line, isError));
        }
    }
}
