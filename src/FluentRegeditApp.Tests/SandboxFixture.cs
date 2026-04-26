using System;
using Microsoft.Win32;

namespace FluentRegeditApp.Tests;

/// <summary>
/// Creates an isolated subkey under HKCU\Software\FluentRegedit\Tests\&lt;guid&gt;
/// and deletes it on disposal. Never touches HKLM.
/// </summary>
public sealed class SandboxFixture : IDisposable
{
    public const string RootSubPath = @"Software\FluentRegedit\Tests";

    public string Id { get; }
    public string SubPath { get; }
    public string FullPath => $@"HKEY_CURRENT_USER\{SubPath}";

    public SandboxFixture()
    {
        Id = Guid.NewGuid().ToString("N");
        SubPath = $@"{RootSubPath}\{Id}";
        using var key = Registry.CurrentUser.CreateSubKey(SubPath, writable: true)
            ?? throw new InvalidOperationException("Failed to create sandbox key.");
    }

    public RegistryKey Open(bool writable = true) =>
        Registry.CurrentUser.OpenSubKey(SubPath, writable)
        ?? throw new InvalidOperationException($"Sandbox key {SubPath} missing.");

    public void DeleteTree()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(SubPath, throwOnMissingSubKey: false); }
        catch { /* best-effort */ }
    }

    public void Dispose() => DeleteTree();
}
