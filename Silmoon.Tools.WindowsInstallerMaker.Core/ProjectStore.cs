using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Silmoon.Tools.WindowsInstallerMaker.Core;

public sealed class ProjectStore
{
    public const int CurrentSchemaVersion = 1;
    private static JsonSerializerOptions JsonOptions { get; } = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) } };

    public async Task<InstallerProject> LoadAsync(string path, CancellationToken ct = default)
    {
        var file = new FileInfo(path);
        if (file.Length > 4 * 1024 * 1024) throw new InvalidDataException("安装项目配置超过 4 MiB，拒绝读取。");
        return Deserialize(await File.ReadAllTextAsync(path, Encoding.UTF8, ct));
    }

    public async Task<InstallerProject> SaveAsync(InstallerProject project, string path, string? sourceProjectPath = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        path = Path.GetFullPath(path);
        var saved = Clone(project);
        saved.PublishDirectory = Rebase(saved.PublishDirectory, sourceProjectPath, path);
        saved.OutputDirectory = Rebase(saved.OutputDirectory, sourceProjectPath, path);
        saved.IconPath = Rebase(saved.IconPath, sourceProjectPath, path);
        saved.ReleaseNotesPath = Rebase(saved.ReleaseNotesPath, sourceProjectPath, path);
        foreach (var dependency in saved.Dependencies) dependency.OfflineInstallerPath = Rebase(dependency.OfflineInstallerPath, sourceProjectPath, path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, Serialize(saved), new UTF8Encoding(true), ct);
            ct.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            return saved;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static string ResolvePath(string path, string? projectPath = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var directory = string.IsNullOrWhiteSpace(projectPath) ? Environment.CurrentDirectory : Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        return Path.GetFullPath(path, directory);
    }

    public static InstallerProject Clone(InstallerProject project) => Deserialize(Serialize(project));
    public static string Serialize(InstallerProject project) => JsonSerializer.Serialize(project, JsonOptions).ReplaceLineEndings("\r\n") + "\r\n";

    public static InstallerProject Deserialize(string json)
    {
        try
        {
            json = json.TrimStart('\uFEFF');
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("安装项目必须是 JSON 对象。");
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version)) throw new InvalidDataException("配置缺少有效的 schemaVersion。");
            if (version != CurrentSchemaVersion) throw new InvalidDataException($"不支持配置 schemaVersion={version}，当前仅支持 {CurrentSchemaVersion}。请使用匹配的工具版本。");
            ValidateJson(document.RootElement, "$");
            return JsonSerializer.Deserialize<InstallerProject>(json, JsonOptions) ?? throw new InvalidDataException("安装项目不能为 null。");
        }
        catch (JsonException ex) { throw new InvalidDataException($"安装项目 JSON 无效：{ex.Message}", ex); }
    }

    private static string Rebase(string path, string? sourceProjectPath, string targetProjectPath) => string.IsNullOrWhiteSpace(path) ? "" : Path.GetRelativePath(Path.GetDirectoryName(targetProjectPath)!, ResolvePath(path, sourceProjectPath));

    private static void ValidateJson(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Null) throw new InvalidDataException($"配置字段 {path} 不能为 null，请使用空字符串或空数组。");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"配置字段 {path}.{property.Name} 重复。");
                ValidateJson(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray()) ValidateJson(item, $"{path}[{index++}]");
        }
    }
}
