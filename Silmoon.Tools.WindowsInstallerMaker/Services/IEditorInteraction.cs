using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Silmoon.Tools.WindowsInstallerMaker.Services;

public enum SaveDecision { Save, Discard, Cancel }

public interface IEditorInteraction
{
    Task<string?> OpenFileAsync(string extension);
    Task<string?> SaveFileAsync(string suggestedName);
    Task<string?> PickFolderAsync();
    Task<SaveDecision> ConfirmUnsavedAsync();
    Task<bool> ConfirmAsync(string title, string message, string action);
    Task OpenDirectoryAsync(string directory);
}

public sealed record EnumChoice<T>(T Value, string Name) where T : struct, Enum;

public static class EnumChoices
{
    public static IReadOnlyList<EnumChoice<T>> For<T>() where T : struct, Enum => Enum.GetValues<T>().Select(value => new EnumChoice<T>(value, typeof(T).GetMember(value.ToString())[0].GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString())).ToArray();
}
