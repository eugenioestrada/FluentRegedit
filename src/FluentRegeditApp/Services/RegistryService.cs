using System;
using System.Collections.Generic;
using Microsoft.Win32;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

/// <summary>
/// Thin wrapper over the Win32 registry. Reads degrade gracefully on
/// access-denied so the UI never throws while browsing protected keys.
/// </summary>
public class RegistryService
{
    public virtual RegistryView View { get; set; } = RegistryView.Default;

    public RegistryKey OpenBaseKey(RegistryRoot root) =>
        RegistryKey.OpenBaseKey(root.ToHive(), View);

    public RegistryKey? OpenKey(RegistryRoot root, string subPath, bool writable = false)
    {
        try
        {
            if (string.IsNullOrEmpty(subPath))
                return RegistryKey.OpenBaseKey(root.ToHive(), View);
            using var baseKey = OpenBaseKey(root);
            return baseKey.OpenSubKey(subPath, writable);
        }
        catch
        {
            return null;
        }
    }

    public IEnumerable<string> GetSubKeyNames(RegistryRoot root, string subPath)
    {
        using var key = OpenKey(root, subPath);
        if (key is null) yield break;
        string[] names;
        try { names = key.GetSubKeyNames(); }
        catch { yield break; }
        Array.Sort(names, StringComparer.OrdinalIgnoreCase);
        foreach (var n in names) yield return n;
    }

    public IReadOnlyList<RegistryValueItem> GetValues(RegistryRoot root, string subPath)
    {
        var list = new List<RegistryValueItem>();
        using var key = OpenKey(root, subPath);
        if (key is null) return list;

        string[] names;
        try { names = key.GetValueNames(); }
        catch { return list; }

        bool sawDefault = false;
        foreach (var name in names)
        {
            if (name.Length == 0) sawDefault = true;
            try
            {
                var kind = key.GetValueKind(name);
                var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                list.Add(new RegistryValueItem { Name = name, Kind = kind, RawData = data });
            }
            catch
            {
                list.Add(new RegistryValueItem { Name = name, Kind = RegistryValueKind.Unknown, RawData = null });
            }
        }

        if (!sawDefault)
            list.Add(new RegistryValueItem { Name = string.Empty, Kind = RegistryValueKind.String, RawData = null });

        list.Sort((a, b) =>
        {
            if (a.IsDefault) return -1;
            if (b.IsDefault) return 1;
            return StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        });

        return list;
    }
}
