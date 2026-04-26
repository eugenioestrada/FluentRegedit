using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

/// <summary>
/// Mutating registry operations. Always opens keys with write access on demand
/// and surfaces failures as exceptions so the UI can show them to the user.
/// </summary>
public sealed class RegistryEditService
{
    public RegistryView View { get; set; } = RegistryView.Default;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int RegRenameKey(SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpSubKeyName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpNewKeyName);

    public void CreateSubKey(RegistryRoot root, string parentSub, string name)
    {
        using var baseKey = RegistryKey.OpenBaseKey(root.ToHive(), View);
        using var parent = string.IsNullOrEmpty(parentSub) ? baseKey : baseKey.OpenSubKey(parentSub, writable: true);
        if (parent is null) throw new InvalidOperationException("Parent key not accessible.");
        using var _ = parent.CreateSubKey(name, writable: true);
    }

    public void DeleteSubKey(RegistryRoot root, string subPath)
    {
        if (string.IsNullOrEmpty(subPath))
            throw new InvalidOperationException("Cannot delete a root hive.");
        using var baseKey = RegistryKey.OpenBaseKey(root.ToHive(), View);
        baseKey.DeleteSubKeyTree(subPath, throwOnMissingSubKey: true);
    }

    public void SetValue(RegistryRoot root, string subPath, string name, RegistryValueKind kind, object data)
    {
        using var baseKey = RegistryKey.OpenBaseKey(root.ToHive(), View);
        using var key = string.IsNullOrEmpty(subPath) ? baseKey : baseKey.OpenSubKey(subPath, writable: true);
        if (key is null) throw new InvalidOperationException("Key not accessible.");
        key.SetValue(name, data, kind);
    }

    public void DeleteValue(RegistryRoot root, string subPath, string name)
    {
        using var baseKey = RegistryKey.OpenBaseKey(root.ToHive(), View);
        using var key = string.IsNullOrEmpty(subPath) ? baseKey : baseKey.OpenSubKey(subPath, writable: true);
        if (key is null) throw new InvalidOperationException("Key not accessible.");
        key.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>
    /// Renames a subkey using <c>RegRenameKey</c> (Vista+). Preserves ACLs and contents.
    /// Only renames the leaf component; the new name must not contain '\\'.
    /// </summary>
    public void RenameSubKey(RegistryRoot root, string subPath, string newLeafName)
    {
        if (string.IsNullOrEmpty(subPath))
            throw new InvalidOperationException("Cannot rename a root hive.");
        if (string.IsNullOrWhiteSpace(newLeafName) || newLeafName.Contains('\\'))
            throw new ArgumentException("Invalid new name.", nameof(newLeafName));

        var idx = subPath.LastIndexOf('\\');
        var parentSub = idx < 0 ? string.Empty : subPath[..idx];
        var oldLeaf = idx < 0 ? subPath : subPath[(idx + 1)..];
        if (string.Equals(oldLeaf, newLeafName, StringComparison.Ordinal)) return;

        using var baseKey = RegistryKey.OpenBaseKey(root.ToHive(), View);
        using var parent = string.IsNullOrEmpty(parentSub) ? baseKey : baseKey.OpenSubKey(parentSub, writable: true);
        if (parent is null) throw new InvalidOperationException("Parent key not accessible.");

        int rc = RegRenameKey(parent.Handle, oldLeaf, newLeafName);
        if (rc != 0)
            throw new InvalidOperationException($"RegRenameKey failed with Win32 error {rc}.");
    }

    /// <summary>Renames a value by reading kind+data, writing under the new name, and deleting the old name.</summary>
    public void RenameValue(RegistryRoot root, string subPath, string oldName, string newName)
    {
        if (oldName == newName) return;
        using var baseKey = RegistryKey.OpenBaseKey(root.ToHive(), View);
        using var key = string.IsNullOrEmpty(subPath) ? baseKey : baseKey.OpenSubKey(subPath, writable: true);
        if (key is null) throw new InvalidOperationException("Key not accessible.");
        var names = key.GetValueNames();
        if (System.Array.Exists(names, n => string.Equals(n, newName, StringComparison.Ordinal)))
            throw new InvalidOperationException($"A value named '{newName}' already exists.");

        var kind = key.GetValueKind(oldName);
        var data = key.GetValue(oldName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (data is null) throw new InvalidOperationException("Cannot rename a value with no data.");
        key.SetValue(newName, data, kind);
        key.DeleteValue(oldName, throwOnMissingValue: false);
    }
}
