using System;
using System.IO;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

/// <summary>
/// Creates timestamped <c>.reg</c> snapshots of a key (or whole hive) and stores them
/// under <c>%LOCALAPPDATA%\FluentRegedit\Backups</c>. The same exporter that produces
/// user-facing exports is reused, so backups can be restored simply by importing them.
/// </summary>
public sealed class BackupService
{
    private readonly RegFileExporter _exporter;
    public string BackupDirectory { get; }

    public BackupService(RegFileExporter exporter, string? overrideDirectory = null)
    {
        _exporter = exporter;
        BackupDirectory = overrideDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentRegedit", "Backups");
        Directory.CreateDirectory(BackupDirectory);
    }

    public string CreateSnapshot(RegistryRoot root, string subPath, string? label = null)
    {
        var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var slug = Slug(string.IsNullOrEmpty(subPath) ? root.ShortName() : $"{root.ShortName()}_{subPath}");
        var name = $"{ts}_{slug}{(string.IsNullOrEmpty(label) ? "" : "_" + Slug(label))}.reg";
        var path = Path.Combine(BackupDirectory, name);
        _exporter.Export(root, subPath, path);
        return path;
    }

    private static string Slug(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        int j = 0;
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) buf[j++] = c;
            else if (c == '\\' || c == '/' || c == '_' || c == '-') buf[j++] = '_';
        }
        return j == 0 ? "snapshot" : new string(buf[..j]);
    }
}
