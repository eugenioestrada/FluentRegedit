using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

public sealed class RecentEntry
{
    public RegistryRoot Root { get; set; }
    public string SubPath { get; set; } = string.Empty;
    public DateTime VisitedAt { get; set; }
}

public sealed class RecentLocationsService
{
    private readonly string _path;
    private readonly int _capacity;
    private readonly List<RecentEntry> _entries = new();
    private static readonly JsonSerializerOptions s_opts = new() { WriteIndented = true };

    public RecentLocationsService(int capacity = 12, string? overridePath = null)
    {
        _capacity = capacity > 0 ? capacity : 12;
        _path = overridePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentRegedit", "recent.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<RecentEntry>>(json, s_opts);
            if (list is null) return;
            _entries.AddRange(list);
        }
        catch { /* ignore */ }
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, s_opts);
            File.WriteAllText(_path, json);
        }
        catch { /* ignore */ }
    }

    public void Visit(RegistryRoot root, string subPath)
    {
        subPath ??= string.Empty;
        var existing = _entries.FirstOrDefault(e =>
            e.Root == root && string.Equals(e.SubPath, subPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            _entries.Remove(existing);

        _entries.Insert(0, new RecentEntry { Root = root, SubPath = subPath, VisitedAt = DateTime.Now });

        while (_entries.Count > _capacity)
            _entries.RemoveAt(_entries.Count - 1);

        Persist();
    }

    public IReadOnlyList<RecentEntry> GetAll() => _entries.ToList();

    public void Clear()
    {
        _entries.Clear();
        Persist();
    }
}
