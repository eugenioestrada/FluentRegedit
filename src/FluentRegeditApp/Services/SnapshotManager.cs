using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FluentRegeditApp.Services;

public sealed record SnapshotInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }

    public string SizeDisplay
    {
        get
        {
            double s = SizeBytes;
            string[] units = { "B", "KB", "MB", "GB" };
            int i = 0;
            while (s >= 1024 && i < units.Length - 1) { s /= 1024; i++; }
            return $"{s:0.##} {units[i]}";
        }
    }

    public string CreatedDisplay => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
}

public sealed class SnapshotManager
{
    private readonly BackupService _backup;
    private readonly RegFileImporter _importer;

    public SnapshotManager(BackupService backup, RegFileImporter importer)
    {
        _backup = backup;
        _importer = importer;
    }

    public string BackupDirectory => _backup.BackupDirectory;

    public IReadOnlyList<SnapshotInfo> List()
    {
        if (!Directory.Exists(_backup.BackupDirectory)) return Array.Empty<SnapshotInfo>();
        return Directory.EnumerateFiles(_backup.BackupDirectory, "*.reg")
            .Select(p =>
            {
                var fi = new FileInfo(p);
                return new SnapshotInfo
                {
                    FilePath = fi.FullName,
                    FileName = fi.Name,
                    CreatedAt = fi.LastWriteTime,
                    SizeBytes = fi.Length,
                };
            })
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    public void Delete(SnapshotInfo info)
    {
        if (File.Exists(info.FilePath))
            File.Delete(info.FilePath);
    }

    public RegImportResult Restore(SnapshotInfo info)
    {
        return _importer.Import(info.FilePath);
    }
}
