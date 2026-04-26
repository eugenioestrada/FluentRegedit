using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

public sealed record Favorite
{
    public string Name { get; set; } = string.Empty;
    public RegistryRoot Root { get; set; }
    public string SubPath { get; set; } = string.Empty;
}

public sealed class FavoritesService
{
    private readonly string _path;
    private static readonly JsonSerializerOptions s_opts = new() { WriteIndented = true };

    public ObservableCollection<Favorite> Items { get; } = new();

    public FavoritesService(string? overridePath = null)
    {
        _path = overridePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentRegedit", "favorites.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        Load();
    }

    private void Load()
    {
        Items.Clear();
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<Favorite>>(json, s_opts);
            if (list is null) return;
            foreach (var f in list) Items.Add(f);
        }
        catch { /* ignore corrupt file */ }
    }

    public IReadOnlyList<Favorite> GetAll() => Items.ToList();

    public void Add(Favorite favorite)
    {
        Items.Add(favorite);
        Save();
    }

    public void Remove(string name)
    {
        var found = Items.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (found is not null)
        {
            Items.Remove(found);
            Save();
        }
    }

    public void Rename(string oldName, string newName)
    {
        var found = Items.FirstOrDefault(f => string.Equals(f.Name, oldName, StringComparison.OrdinalIgnoreCase));
        if (found is null) return;
        var idx = Items.IndexOf(found);
        Items[idx] = new Favorite { Name = newName, Root = found.Root, SubPath = found.SubPath };
        Save();
    }

    public void Move(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Items.Count) return;
        if (newIndex < 0 || newIndex >= Items.Count) return;
        if (oldIndex == newIndex) return;
        Items.Move(oldIndex, newIndex);
        Save();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Items.ToList(), s_opts);
        File.WriteAllText(_path, json);
    }
}
