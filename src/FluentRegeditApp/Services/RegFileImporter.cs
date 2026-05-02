using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

public sealed class RegImportResult
{
    public int KeysCreated;
    public int KeysDeleted;
    public int ValuesWritten;
    public int ValuesDeleted;
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Imports a ".reg" file (Windows Registry Editor Version 5.00, Unicode).
/// Supports the value kinds emitted by <see cref="RegFileExporter"/>:
/// REG_SZ, REG_DWORD, REG_QWORD (hex(b)), REG_EXPAND_SZ (hex(2)),
/// REG_MULTI_SZ (hex(7)), REG_BINARY (hex), REG_NONE (hex(0)),
/// plus the regedit conventions <c>"name"=-</c> (delete value)
/// and <c>[-HKEY_...\path]</c> (delete key).
/// </summary>
public abstract record RegFileEntry
{
    public sealed record DeleteKey(RegistryRoot Root, string SubPath) : RegFileEntry;
    public sealed record KeyHeader(RegistryRoot Root, string SubPath) : RegFileEntry;
    public sealed record SetValue(RegistryRoot Root, string SubPath, string Name, RegistryValueKind Kind, object? Data) : RegFileEntry;
    public sealed record DeleteValue(RegistryRoot Root, string SubPath, string Name) : RegFileEntry;
}

public sealed class RegFileImporter
{
    public RegistryView View { get; set; } = RegistryView.Default;

    public IEnumerable<RegFileEntry> Parse(string filePath)
    {
        var text = File.ReadAllText(filePath, DetectEncoding(filePath));
        text = JoinHexContinuations(text);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        int i = 0;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i >= lines.Length || !(lines[i].StartsWith("Windows Registry Editor Version 5.00", StringComparison.Ordinal)
                                   || lines[i].StartsWith("REGEDIT4", StringComparison.Ordinal)))
            yield break;
        i++;

        RegistryRoot currentRoot = RegistryRoot.CurrentUser;
        string currentSub = string.Empty;
        bool haveKey = false;

        for (; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.Length == 0 || line.StartsWith(';')) continue;

            if (line.StartsWith('['))
            {
                var end = line.IndexOf(']');
                if (end < 0) continue;
                var inside = line.Substring(1, end - 1).Trim();
                bool delete = inside.StartsWith('-');
                if (delete) inside = inside[1..].Trim();
                if (!PathParser.TryParse(inside, out currentRoot, out currentSub)) { haveKey = false; continue; }
                haveKey = !delete;
                if (delete)
                    yield return new RegFileEntry.DeleteKey(currentRoot, currentSub);
                else
                    yield return new RegFileEntry.KeyHeader(currentRoot, currentSub);
                continue;
            }

            if (!haveKey) continue;
            if (!TryParseValueLine(line, out var name, out var rhs)) continue;

            if (rhs == "-")
            {
                yield return new RegFileEntry.DeleteValue(currentRoot, currentSub, name);
                continue;
            }

            if (TryDecodeValue(rhs, out var kind, out var data))
                yield return new RegFileEntry.SetValue(currentRoot, currentSub, name, kind, data);
        }
    }

    private static bool TryDecodeValue(string rhs, out RegistryValueKind kind, out object? data)
    {
        kind = RegistryValueKind.String;
        data = null;
        try
        {
            if (rhs.StartsWith('"'))
            {
                kind = RegistryValueKind.String;
                data = ParseQuoted(rhs);
                return true;
            }
            if (rhs.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
            {
                var num = uint.Parse(rhs.AsSpan("dword:".Length).Trim(),
                    System.Globalization.NumberStyles.HexNumber);
                kind = RegistryValueKind.DWord;
                data = unchecked((int)num);
                return true;
            }
            if (rhs.StartsWith("hex", StringComparison.OrdinalIgnoreCase))
            {
                var (k, payload) = ParseHexBlock(rhs);
                var bytes = ParseHexBytes(payload);
                switch (k)
                {
                    case 1: kind = RegistryValueKind.String; data = BytesToUtf16(bytes); return true;
                    case 2: kind = RegistryValueKind.ExpandString; data = BytesToUtf16(bytes); return true;
                    case 7: kind = RegistryValueKind.MultiString; data = BytesToMultiSz(bytes); return true;
                    case 0xb:
                        if (bytes.Length < 8) bytes = PadTo(bytes, 8);
                        kind = RegistryValueKind.QWord; data = BitConverter.ToInt64(bytes, 0); return true;
                    case 0: kind = RegistryValueKind.None; data = bytes; return true;
                    case 3:
                    default: kind = RegistryValueKind.Binary; data = bytes; return true;
                }
            }
        }
        catch { /* fall through */ }
        return false;
    }

    public RegImportResult Import(string filePath)
    {
        var result = new RegImportResult();
        var text = File.ReadAllText(filePath, DetectEncoding(filePath));

        // Join hex line continuations: a line ending with "\" followed by CRLF and indent
        text = JoinHexContinuations(text);

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        // Header: "Windows Registry Editor Version 5.00" or "REGEDIT4"
        int i = 0;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i >= lines.Length || !(lines[i].StartsWith("Windows Registry Editor Version 5.00", StringComparison.Ordinal)
                                   || lines[i].StartsWith("REGEDIT4", StringComparison.Ordinal)))
        {
            result.Errors.Add("Missing or unsupported header line.");
            return result;
        }
        i++;

        RegistryKey? currentKey = null;
        RegistryRoot currentRoot = RegistryRoot.CurrentUser;
        string currentSub = string.Empty;

        try
        {
            for (; i < lines.Length; i++)
            {
                var raw = lines[i];
                var line = raw.TrimStart();
                if (line.Length == 0 || line.StartsWith(';')) continue;

                if (line.StartsWith('['))
                {
                    currentKey?.Dispose();
                    currentKey = null;

                    var end = line.IndexOf(']');
                    if (end < 0) { result.Errors.Add($"Malformed key header: {line}"); continue; }
                    var inside = line.Substring(1, end - 1).Trim();
                    bool delete = inside.StartsWith('-');
                    if (delete) inside = inside[1..].Trim();

                    if (!PathParser.TryParse(inside, out currentRoot, out currentSub))
                    {
                        result.Errors.Add($"Unknown root in header: {inside}");
                        continue;
                    }

                    if (delete)
                    {
                        try
                        {
                            using var baseKey = RegistryKey.OpenBaseKey(currentRoot.ToHive(), View);
                            baseKey.DeleteSubKeyTree(currentSub, throwOnMissingSubKey: false);
                            result.KeysDeleted++;
                        }
                        catch (Exception ex) { result.Errors.Add($"Delete key '{inside}': {ex.Message}"); }
                    }
                    else
                    {
                        try
                        {
                            using var baseKey = RegistryKey.OpenBaseKey(currentRoot.ToHive(), View);
                            currentKey = string.IsNullOrEmpty(currentSub)
                                ? RegistryKey.OpenBaseKey(currentRoot.ToHive(), View)
                                : baseKey.CreateSubKey(currentSub, writable: true);
                            result.KeysCreated++;
                        }
                        catch (Exception ex) { result.Errors.Add($"Open/create key '{inside}': {ex.Message}"); }
                    }
                    continue;
                }

                if (currentKey is null) continue;

                // Value line:  @=...    or    "name"=...
                if (!TryParseValueLine(line, out var name, out var rhs))
                {
                    result.Errors.Add($"Malformed value line: {line}");
                    continue;
                }

                if (rhs == "-")
                {
                    try
                    {
                        currentKey.DeleteValue(name, throwOnMissingValue: false);
                        result.ValuesDeleted++;
                    }
                    catch (Exception ex) { result.Errors.Add($"Delete value '{name}': {ex.Message}"); }
                    continue;
                }

                try
                {
                    ApplyValue(currentKey, name, rhs);
                    result.ValuesWritten++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Write value '{name}': {ex.Message}");
                }
            }
        }
        finally
        {
            currentKey?.Dispose();
        }

        return result;
    }

    public RegImportResult ImportSingleValue(string filePath, RegistryRoot targetRoot, string targetSubPath)
    {
        var result = new RegImportResult();
        List<RegFileEntry.SetValue> values;
        try
        {
            var entries = Parse(filePath).ToList();
            values = entries.OfType<RegFileEntry.SetValue>().ToList();
            var unsupported = entries.Any(e => e is RegFileEntry.DeleteKey or RegFileEntry.DeleteValue);
            if (unsupported)
                result.Errors.Add("Single-value import does not support delete entries.");
            if (values.Count != 1)
                result.Errors.Add($"Expected exactly one value entry, found {values.Count}.");
            if (result.Errors.Count > 0)
                return result;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Parse failed: {ex.Message}");
            return result;
        }

        var value = values[0];
        if (value.Data is null)
        {
            result.Errors.Add($"Value '{value.Name}' has no data.");
            return result;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(targetRoot.ToHive(), View);
            using var openedSubKey = string.IsNullOrEmpty(targetSubPath)
                ? null
                : baseKey.OpenSubKey(targetSubPath, writable: true);
            var targetKey = string.IsNullOrEmpty(targetSubPath) ? baseKey : openedSubKey;
            if (targetKey is null)
            {
                result.Errors.Add("Target key not accessible.");
                return result;
            }

            targetKey.SetValue(value.Name, value.Data, value.Kind);
            result.ValuesWritten = 1;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Write value '{value.Name}': {ex.Message}");
        }

        return result;
    }

    private static Encoding DetectEncoding(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> bom = stackalloc byte[2];
        var n = fs.Read(bom);
        if (n >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
        if (n >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;
        return Encoding.UTF8;
    }

    private static string JoinHexContinuations(string text)
    {
        // A trailing "\" before CR/LF on a hex line means "continued on next line".
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && (text[i + 1] == '\r' || text[i + 1] == '\n'))
            {
                // skip the backslash and the following newline + leading whitespace
                i++;
                if (i < text.Length && text[i] == '\r') i++;
                if (i < text.Length && text[i] == '\n') i++;
                while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static bool TryParseValueLine(string line, out string name, out string rhs)
    {
        name = string.Empty;
        rhs = string.Empty;
        if (line.StartsWith('@'))
        {
            var eq = line.IndexOf('=');
            if (eq < 0) return false;
            name = string.Empty;
            rhs = line[(eq + 1)..].Trim();
            return true;
        }
        if (!line.StartsWith('"')) return false;
        // find closing quote that is not escaped
        int j = 1;
        var sb = new StringBuilder();
        while (j < line.Length)
        {
            var c = line[j];
            if (c == '\\' && j + 1 < line.Length) { sb.Append(line[j + 1]); j += 2; continue; }
            if (c == '"') { j++; break; }
            sb.Append(c); j++;
        }
        if (j >= line.Length || line[j] != '=') return false;
        name = sb.ToString();
        rhs = line[(j + 1)..].Trim();
        return true;
    }

    private static void ApplyValue(RegistryKey key, string name, string rhs)
    {
        if (rhs.StartsWith('"'))
        {
            // REG_SZ
            var s = ParseQuoted(rhs);
            key.SetValue(name, s, RegistryValueKind.String);
            return;
        }
        if (rhs.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
        {
            var num = uint.Parse(rhs.AsSpan("dword:".Length).Trim(),
                System.Globalization.NumberStyles.HexNumber);
            key.SetValue(name, unchecked((int)num), RegistryValueKind.DWord);
            return;
        }
        if (rhs.StartsWith("hex", StringComparison.OrdinalIgnoreCase))
        {
            var (kind, payload) = ParseHexBlock(rhs);
            var bytes = ParseHexBytes(payload);
            switch (kind)
            {
                case 1: // REG_SZ as hex (rare)
                    key.SetValue(name, BytesToUtf16(bytes), RegistryValueKind.String); break;
                case 2: // REG_EXPAND_SZ
                    key.SetValue(name, BytesToUtf16(bytes), RegistryValueKind.ExpandString); break;
                case 7: // REG_MULTI_SZ
                    key.SetValue(name, BytesToMultiSz(bytes), RegistryValueKind.MultiString); break;
                case 0xb: // REG_QWORD
                    {
                        if (bytes.Length < 8) bytes = PadTo(bytes, 8);
                        long q = BitConverter.ToInt64(bytes, 0);
                        key.SetValue(name, q, RegistryValueKind.QWord);
                        break;
                    }
                case 0: // REG_NONE
                    key.SetValue(name, bytes, RegistryValueKind.None); break;
                case 3: // REG_BINARY
                default:
                    key.SetValue(name, bytes, RegistryValueKind.Binary); break;
            }
            return;
        }
        throw new FormatException($"Unrecognized value RHS: {rhs}");
    }

    private static string ParseQuoted(string rhs)
    {
        var sb = new StringBuilder();
        for (int j = 1; j < rhs.Length; j++)
        {
            var c = rhs[j];
            if (c == '\\' && j + 1 < rhs.Length) { sb.Append(rhs[j + 1]); j++; continue; }
            if (c == '"') break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static (int kind, string payload) ParseHexBlock(string rhs)
    {
        // hex:...   or hex(N):...
        int kind = 3;
        int colon = rhs.IndexOf(':');
        if (colon < 0) throw new FormatException("Missing ':' in hex value.");
        var prefix = rhs[..colon];
        if (prefix.StartsWith("hex(", StringComparison.OrdinalIgnoreCase))
        {
            var close = prefix.IndexOf(')');
            if (close < 0) throw new FormatException("Bad hex(N) prefix.");
            kind = int.Parse(prefix.AsSpan(4, close - 4),
                System.Globalization.NumberStyles.HexNumber);
        }
        return (kind, rhs[(colon + 1)..]);
    }

    private static byte[] ParseHexBytes(string payload)
    {
        var list = new List<byte>(payload.Length / 3 + 1);
        var span = payload.AsSpan();
        for (int i = 0; i < span.Length;)
        {
            while (i < span.Length && (span[i] == ',' || span[i] == ' ' || span[i] == '\t' || span[i] == '\r' || span[i] == '\n')) i++;
            if (i + 1 >= span.Length) break;
            list.Add(byte.Parse(span.Slice(i, 2), System.Globalization.NumberStyles.HexNumber));
            i += 2;
        }
        return list.ToArray();
    }

    private static string BytesToUtf16(byte[] bytes)
    {
        var s = Encoding.Unicode.GetString(bytes);
        if (s.EndsWith('\0')) s = s.TrimEnd('\0');
        return s;
    }

    private static string[] BytesToMultiSz(byte[] bytes)
    {
        var s = Encoding.Unicode.GetString(bytes);
        var parts = s.Split('\0');
        var list = new List<string>();
        foreach (var p in parts) if (p.Length > 0) list.Add(p);
        return list.ToArray();
    }

    private static byte[] PadTo(byte[] src, int len)
    {
        var dst = new byte[len];
        Array.Copy(src, dst, Math.Min(src.Length, len));
        return dst;
    }
}
