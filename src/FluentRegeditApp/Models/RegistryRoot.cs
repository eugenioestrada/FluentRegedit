using Microsoft.Win32;

namespace FluentRegeditApp.Models;

public enum RegistryRoot
{
    ClassesRoot,
    CurrentUser,
    LocalMachine,
    Users,
    CurrentConfig,
}

public static class RegistryRootExtensions
{
    public static string ShortName(this RegistryRoot root) => root switch
    {
        RegistryRoot.ClassesRoot => "HKCR",
        RegistryRoot.CurrentUser => "HKCU",
        RegistryRoot.LocalMachine => "HKLM",
        RegistryRoot.Users => "HKU",
        RegistryRoot.CurrentConfig => "HKCC",
        _ => root.ToString(),
    };

    public static string FullName(this RegistryRoot root) => root switch
    {
        RegistryRoot.ClassesRoot => "HKEY_CLASSES_ROOT",
        RegistryRoot.CurrentUser => "HKEY_CURRENT_USER",
        RegistryRoot.LocalMachine => "HKEY_LOCAL_MACHINE",
        RegistryRoot.Users => "HKEY_USERS",
        RegistryRoot.CurrentConfig => "HKEY_CURRENT_CONFIG",
        _ => root.ToString(),
    };

    public static RegistryHive ToHive(this RegistryRoot root) => root switch
    {
        RegistryRoot.ClassesRoot => RegistryHive.ClassesRoot,
        RegistryRoot.CurrentUser => RegistryHive.CurrentUser,
        RegistryRoot.LocalMachine => RegistryHive.LocalMachine,
        RegistryRoot.Users => RegistryHive.Users,
        RegistryRoot.CurrentConfig => RegistryHive.CurrentConfig,
        _ => RegistryHive.CurrentUser,
    };

    public static bool TryParse(string name, out RegistryRoot root)
    {
        switch (name.Trim().ToUpperInvariant())
        {
            case "HKCR":
            case "HKEY_CLASSES_ROOT":
                root = RegistryRoot.ClassesRoot; return true;
            case "HKCU":
            case "HKEY_CURRENT_USER":
                root = RegistryRoot.CurrentUser; return true;
            case "HKLM":
            case "HKEY_LOCAL_MACHINE":
                root = RegistryRoot.LocalMachine; return true;
            case "HKU":
            case "HKEY_USERS":
                root = RegistryRoot.Users; return true;
            case "HKCC":
            case "HKEY_CURRENT_CONFIG":
                root = RegistryRoot.CurrentConfig; return true;
            default:
                root = RegistryRoot.CurrentUser; return false;
        }
    }
}
