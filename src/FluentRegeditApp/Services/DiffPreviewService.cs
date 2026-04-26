using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

public enum ChangeKind { KeyAdded, KeyDeleted, ValueAdded, ValueModified, ValueDeleted }

public sealed record DiffEntry
{
    public ChangeKind Kind { get; set; }
    public string KeyPath { get; set; } = string.Empty;
    public string? ValueName { get; set; }
    public string? OldDisplay { get; set; }
    public string? NewDisplay { get; set; }

    public string DisplayHeader
    {
        get
        {
            var name = string.IsNullOrEmpty(ValueName) ? null : (ValueName == string.Empty ? "(Default)" : ValueName);
            return name is null ? KeyPath : $"{KeyPath}  →  {name}";
        }
    }

    public string DisplayDetail => Kind switch
    {
        ChangeKind.ValueModified => $"{OldDisplay}  →  {NewDisplay}",
        ChangeKind.ValueDeleted => OldDisplay ?? string.Empty,
        ChangeKind.ValueAdded => NewDisplay ?? string.Empty,
        _ => string.Empty,
    };
}

public sealed class DiffPreviewService
{
    private readonly RegFileImporter _importer;
    private readonly RegistryService _registry;

    public DiffPreviewService(RegFileImporter importer, RegistryService registry)
    {
        _importer = importer;
        _registry = registry;
    }

    public IReadOnlyList<DiffEntry> Compute(string regFilePath)
    {
        var results = new List<DiffEntry>();

        foreach (var entry in _importer.Parse(regFilePath))
        {
            switch (entry)
            {
                case RegFileEntry.DeleteKey dk:
                    {
                        using var k = _registry.OpenKey(dk.Root, dk.SubPath);
                        if (k is not null)
                        {
                            results.Add(new DiffEntry
                            {
                                Kind = ChangeKind.KeyDeleted,
                                KeyPath = PathParser.Combine(dk.Root, dk.SubPath),
                            });
                        }
                        break;
                    }
                case RegFileEntry.KeyHeader kh:
                    {
                        using var k = _registry.OpenKey(kh.Root, kh.SubPath);
                        if (k is null)
                        {
                            results.Add(new DiffEntry
                            {
                                Kind = ChangeKind.KeyAdded,
                                KeyPath = PathParser.Combine(kh.Root, kh.SubPath),
                            });
                        }
                        break;
                    }
                case RegFileEntry.SetValue sv:
                    {
                        var path = PathParser.Combine(sv.Root, sv.SubPath);
                        using var key = _registry.OpenKey(sv.Root, sv.SubPath);
                        var newDisplay = FormatData(sv.Kind, sv.Data);
                        if (key is null)
                        {
                            results.Add(new DiffEntry
                            {
                                Kind = ChangeKind.ValueAdded,
                                KeyPath = path,
                                ValueName = sv.Name,
                                NewDisplay = newDisplay,
                            });
                            break;
                        }
                        object? existing = null;
                        RegistryValueKind existingKind = RegistryValueKind.Unknown;
                        bool exists = false;
                        try
                        {
                            var names = key.GetValueNames();
                            if (names.Contains(sv.Name, StringComparer.OrdinalIgnoreCase) ||
                                (sv.Name.Length == 0 && names.Contains(string.Empty)))
                            {
                                existingKind = key.GetValueKind(sv.Name);
                                existing = key.GetValue(sv.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                                exists = true;
                            }
                        }
                        catch { }

                        if (!exists)
                        {
                            results.Add(new DiffEntry
                            {
                                Kind = ChangeKind.ValueAdded,
                                KeyPath = path,
                                ValueName = sv.Name,
                                NewDisplay = newDisplay,
                            });
                        }
                        else
                        {
                            var oldDisplay = FormatData(existingKind, existing);
                            if (existingKind != sv.Kind || !DataEquals(existing, sv.Data))
                            {
                                results.Add(new DiffEntry
                                {
                                    Kind = ChangeKind.ValueModified,
                                    KeyPath = path,
                                    ValueName = sv.Name,
                                    OldDisplay = oldDisplay,
                                    NewDisplay = newDisplay,
                                });
                            }
                        }
                        break;
                    }
                case RegFileEntry.DeleteValue dv:
                    {
                        var path = PathParser.Combine(dv.Root, dv.SubPath);
                        using var key = _registry.OpenKey(dv.Root, dv.SubPath);
                        if (key is null) break;
                        try
                        {
                            var names = key.GetValueNames();
                            if (!names.Contains(dv.Name, StringComparer.OrdinalIgnoreCase)) break;
                            var oldKind = key.GetValueKind(dv.Name);
                            var oldData = key.GetValue(dv.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                            results.Add(new DiffEntry
                            {
                                Kind = ChangeKind.ValueDeleted,
                                KeyPath = path,
                                ValueName = dv.Name,
                                OldDisplay = FormatData(oldKind, oldData),
                            });
                        }
                        catch { }
                        break;
                    }
            }
        }

        return results;
    }

    private static bool DataEquals(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a is byte[] ab && b is byte[] bb) return ab.AsSpan().SequenceEqual(bb);
        if (a is string[] asa && b is string[] bsa) return asa.SequenceEqual(bsa);
        return Equals(a, b);
    }

    private static string FormatData(RegistryValueKind kind, object? raw)
    {
        var v = new RegistryValueItem { Name = "x", Kind = kind, RawData = raw };
        return $"[{v.KindDisplay}] {v.DataDisplay}";
    }
}
