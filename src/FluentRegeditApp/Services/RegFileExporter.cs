using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

/// <summary>
/// Exports a registry subtree to the standard ".reg" file format
/// (Windows Registry Editor Version 5.00, UTF-16 LE with BOM, CRLF line endings).
/// </summary>
public sealed class RegFileExporter
{
    private readonly RegistryService _registry;
    public RegFileExporter(RegistryService registry) => _registry = registry;

    public void Export(RegistryRoot root, string subPath, string filePath)
    {
        // .reg files are written as UTF-16 LE with BOM and CRLF line breaks.
        using var stream = File.Create(filePath);
        using var writer = new StreamWriter(stream, new UnicodeEncoding(bigEndian: false, byteOrderMark: true))
        { NewLine = "\r\n" };

        writer.WriteLine("Windows Registry Editor Version 5.00");
        writer.WriteLine();

        WriteKey(writer, root, subPath);
    }

    public void ExportValue(RegistryRoot root, string subPath, string valueName, string filePath)
    {
        using var key = _registry.OpenKey(root, subPath);
        if (key is null)
            throw new InvalidOperationException("Key not accessible.");

        var names = key.GetValueNames();
        if (!Array.Exists(names, n => string.Equals(n, valueName, StringComparison.Ordinal)))
            throw new InvalidOperationException("Value does not exist.");

        var kind = key.GetValueKind(valueName);
        var raw = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

        using var stream = File.Create(filePath);
        using var writer = new StreamWriter(stream, new UnicodeEncoding(bigEndian: false, byteOrderMark: true))
        { NewLine = "\r\n" };

        writer.WriteLine("Windows Registry Editor Version 5.00");
        writer.WriteLine();
        writer.WriteLine($"[{(string.IsNullOrEmpty(subPath) ? root.FullName() : root.FullName() + "\\" + subPath)}]");
        writer.WriteLine(FormatValueLine(valueName, kind, raw));
        writer.WriteLine();
    }

    private void WriteKey(StreamWriter writer, RegistryRoot root, string subPath)
    {
        using var key = _registry.OpenKey(root, subPath);
        if (key is null) return;

        writer.WriteLine($"[{(string.IsNullOrEmpty(subPath) ? root.FullName() : root.FullName() + "\\" + subPath)}]");

        string[] valueNames;
        try { valueNames = key.GetValueNames(); }
        catch { valueNames = Array.Empty<string>(); }

        Array.Sort(valueNames, (a, b) =>
        {
            if (a.Length == 0) return -1;
            if (b.Length == 0) return 1;
            return StringComparer.OrdinalIgnoreCase.Compare(a, b);
        });

        foreach (var vname in valueNames)
        {
            try
            {
                var kind = key.GetValueKind(vname);
                var raw = key.GetValue(vname, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                writer.WriteLine(FormatValueLine(vname, kind, raw));
            }
            catch { /* skip unreadable values */ }
        }
        writer.WriteLine();

        string[] subs;
        try { subs = key.GetSubKeyNames(); }
        catch { subs = Array.Empty<string>(); }

        foreach (var sub in subs)
        {
            var childSub = string.IsNullOrEmpty(subPath) ? sub : $"{subPath}\\{sub}";
            WriteKey(writer, root, childSub);
        }
    }

    internal static string FormatValueLine(string name, RegistryValueKind kind, object? raw)
    {
        var lhs = name.Length == 0 ? "@" : "\"" + EscapeString(name) + "\"";

        switch (kind)
        {
            case RegistryValueKind.String:
                return $"{lhs}=\"{EscapeString(raw as string ?? string.Empty)}\"";
            case RegistryValueKind.DWord:
                {
                    uint u = raw is int i ? unchecked((uint)i) : 0u;
                    return $"{lhs}=dword:{u:x8}";
                }
            case RegistryValueKind.QWord:
                {
                    ulong u = raw is long l ? unchecked((ulong)l) : 0ul;
                    var bytes = BitConverter.GetBytes(u);
                    return $"{lhs}=hex(b):{HexCsv(bytes)}";
                }
            case RegistryValueKind.ExpandString:
                {
                    var s = raw as string ?? string.Empty;
                    var bytes = Encoding.Unicode.GetBytes(s + "\0");
                    return WrapHex(lhs, "hex(2)", bytes);
                }
            case RegistryValueKind.MultiString:
                {
                    var arr = raw as string[] ?? Array.Empty<string>();
                    var sb = new StringBuilder();
                    foreach (var s in arr) { sb.Append(s); sb.Append('\0'); }
                    sb.Append('\0');
                    var bytes = Encoding.Unicode.GetBytes(sb.ToString());
                    return WrapHex(lhs, "hex(7)", bytes);
                }
            case RegistryValueKind.Binary:
                {
                    var bytes = raw as byte[] ?? Array.Empty<byte>();
                    return WrapHex(lhs, "hex", bytes);
                }
            case RegistryValueKind.None:
                {
                    var bytes = raw as byte[] ?? Array.Empty<byte>();
                    return WrapHex(lhs, "hex(0)", bytes);
                }
            default:
                {
                    var bytes = raw as byte[] ?? Array.Empty<byte>();
                    return WrapHex(lhs, $"hex({(int)kind:x})", bytes);
                }
        }
    }

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string HexCsv(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(bytes[i].ToString("x2"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Writes <c>name=hex(N):xx,xx,...</c> with line continuations every ~75 chars
    /// (matching the layout produced by regedit).
    /// </summary>
    private static string WrapHex(string lhs, string prefix, byte[] bytes)
    {
        var sb = new StringBuilder();
        sb.Append(lhs).Append('=').Append(prefix).Append(':');
        const int limit = 76;
        int col = sb.Length;
        for (int i = 0; i < bytes.Length; i++)
        {
            var token = bytes[i].ToString("x2") + (i == bytes.Length - 1 ? "" : ",");
            if (col + token.Length > limit)
            {
                sb.Append("\\\r\n  ");
                col = 2;
            }
            sb.Append(token);
            col += token.Length;
        }
        return sb.ToString();
    }
}
