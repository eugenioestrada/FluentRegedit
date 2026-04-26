using Microsoft.Win32;

namespace FluentRegeditApp.Models;

public sealed class RegistryValueItem
{
    public string Name { get; init; } = string.Empty;
    public RegistryValueKind Kind { get; init; }
    public object? RawData { get; init; }

    public string DisplayName => string.IsNullOrEmpty(Name) ? "(Default)" : Name;
    public bool IsDefault => string.IsNullOrEmpty(Name);

    public string KindDisplay => Kind switch
    {
        RegistryValueKind.String => "REG_SZ",
        RegistryValueKind.ExpandString => "REG_EXPAND_SZ",
        RegistryValueKind.MultiString => "REG_MULTI_SZ",
        RegistryValueKind.DWord => "REG_DWORD",
        RegistryValueKind.QWord => "REG_QWORD",
        RegistryValueKind.Binary => "REG_BINARY",
        RegistryValueKind.None => "REG_NONE",
        RegistryValueKind.Unknown => "REG_UNKNOWN",
        _ => $"REG_{Kind}".ToUpperInvariant(),
    };

    public string DataDisplay
    {
        get
        {
            if (RawData is null) return "(value not set)";
            return Kind switch
            {
                RegistryValueKind.DWord =>
                    RawData is int i ? $"0x{i:x8} ({(uint)i})" : RawData.ToString() ?? string.Empty,
                RegistryValueKind.QWord =>
                    RawData is long l ? $"0x{l:x16} ({(ulong)l})" : RawData.ToString() ?? string.Empty,
                RegistryValueKind.Binary =>
                    RawData is byte[] b ? FormatBytes(b) : RawData.ToString() ?? string.Empty,
                RegistryValueKind.MultiString =>
                    RawData is string[] s ? string.Join(" \u2502 ", s) : RawData.ToString() ?? string.Empty,
                _ => RawData.ToString() ?? string.Empty,
            };
        }
    }

    private static string FormatBytes(byte[] bytes)
    {
        const int max = 32;
        var len = System.Math.Min(bytes.Length, max);
        var sb = new System.Text.StringBuilder(len * 3);
        for (int i = 0; i < len; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(bytes[i].ToString("x2"));
        }
        if (bytes.Length > max) sb.Append(" \u2026");
        return sb.ToString();
    }
}
