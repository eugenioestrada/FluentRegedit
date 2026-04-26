using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

public static class PathParser
{
    public static bool TryParse(string? input, out RegistryRoot root, out string subPath)
    {
        root = RegistryRoot.CurrentUser;
        subPath = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var trimmed = input.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
        var slash = trimmed.IndexOf('\\');
        var head = slash < 0 ? trimmed : trimmed[..slash];
        var tail = slash < 0 ? string.Empty : trimmed[(slash + 1)..];

        if (!RegistryRootExtensions.TryParse(head, out root)) return false;
        subPath = tail;
        return true;
    }

    public static string Combine(RegistryRoot root, string subPath) =>
        string.IsNullOrEmpty(subPath) ? root.FullName() : $"{root.FullName()}\\{subPath}";
}
