using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

/// <summary>
/// P/Invoke wrappers for save / load / unload of registry hives.
/// Requires SE_BACKUP_NAME and SE_RESTORE_NAME privileges, which are auto-enabled
/// on the calling process token. If the process is not elevated, these calls will
/// surface a clear InvalidOperationException.
/// </summary>
public sealed class HiveSaveLoadService
{
    private const int ERROR_PRIVILEGE_NOT_HELD = 1314;
    private const uint REG_LATEST_FORMAT = 2;

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegSaveKeyExW(SafeRegistryHandle hKey, [MarshalAs(UnmanagedType.LPWStr)] string lpFile, IntPtr lpSecurityAttributes, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegLoadKeyW(SafeRegistryHandle hKey, [MarshalAs(UnmanagedType.LPWStr)] string lpSubKey, [MarshalAs(UnmanagedType.LPWStr)] string lpFile);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegUnLoadKeyW(SafeRegistryHandle hKey, [MarshalAs(UnmanagedType.LPWStr)] string lpSubKey);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    public void SaveHive(RegistryRoot root, string subPath, string filePath)
    {
        EnablePrivileges();
        using var baseKey = RegistryKey.OpenBaseKey(root.ToHive(), RegistryView.Default);
        using var key = string.IsNullOrEmpty(subPath) ? baseKey : baseKey.OpenSubKey(subPath, writable: false);
        if (key is null) throw new InvalidOperationException($"Key not accessible: {subPath}");

        int rc = RegSaveKeyExW(key.Handle, filePath, IntPtr.Zero, REG_LATEST_FORMAT);
        if (rc != 0) ThrowFor(rc, $"RegSaveKeyEx failed for '{subPath}'");
    }

    public void LoadHive(RegistryRoot root, string newKeyName, string filePath)
    {
        EnablePrivileges();
        using var baseKey = RegistryKey.OpenBaseKey(root.ToHive(), RegistryView.Default);
        int rc = RegLoadKeyW(baseKey.Handle, newKeyName, filePath);
        if (rc != 0) ThrowFor(rc, $"RegLoadKey failed for '{newKeyName}'");
    }

    public void UnloadHive(RegistryRoot root, string keyName)
    {
        EnablePrivileges();
        using var baseKey = RegistryKey.OpenBaseKey(root.ToHive(), RegistryView.Default);
        int rc = RegUnLoadKeyW(baseKey.Handle, keyName);
        if (rc != 0) ThrowFor(rc, $"RegUnLoadKey failed for '{keyName}'");
    }

    private static void ThrowFor(int rc, string action)
    {
        if (rc == ERROR_PRIVILEGE_NOT_HELD)
            throw new InvalidOperationException($"{action}: this operation requires running as administrator (SE_BACKUP_NAME/SE_RESTORE_NAME).");
        throw new InvalidOperationException($"{action}: Win32 error {rc}.");
    }

    private static void EnablePrivileges()
    {
        TryEnablePrivilege("SeBackupPrivilege");
        TryEnablePrivilege("SeRestorePrivilege");
    }

    private static void TryEnablePrivilege(string name)
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                return;

            if (!LookupPrivilegeValueW(null, name, out var luid))
                return;

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED },
            };

            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
        }
    }
}
