using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

public sealed class JsonCsvExporter
{
    private readonly RegistryService _registry;

    public JsonCsvExporter(RegistryService registry) => _registry = registry;

    public void ExportJson(RegistryRoot root, string subPath, string filePath)
    {
        var tree = BuildNode(root, subPath);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(filePath, JsonSerializer.Serialize(tree, opts));
    }

    public void ExportCsv(RegistryRoot root, string subPath, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Path,Name,Kind,Data");
        WriteCsv(sb, root, subPath);
        File.WriteAllText(filePath, sb.ToString());
    }

    private object BuildNode(RegistryRoot root, string subPath)
    {
        var path = string.IsNullOrEmpty(subPath) ? root.FullName() : root.FullName() + "\\" + subPath;
        var values = new List<object>();
        foreach (var v in _registry.GetValues(root, subPath))
        {
            values.Add(new
            {
                name = v.Name,
                kind = v.KindDisplay,
                data = SerializableData(v.Kind, v.RawData),
            });
        }

        var subkeys = new List<object>();
        foreach (var sub in _registry.GetSubKeyNames(root, subPath))
        {
            var childSub = string.IsNullOrEmpty(subPath) ? sub : subPath + "\\" + sub;
            subkeys.Add(BuildNode(root, childSub));
        }

        return new { path, values, subkeys };
    }

    private void WriteCsv(StringBuilder sb, RegistryRoot root, string subPath)
    {
        var path = string.IsNullOrEmpty(subPath) ? root.FullName() : root.FullName() + "\\" + subPath;
        foreach (var v in _registry.GetValues(root, subPath))
        {
            sb.Append(CsvEscape(path)).Append(',');
            sb.Append(CsvEscape(v.Name)).Append(',');
            sb.Append(CsvEscape(v.KindDisplay)).Append(',');
            sb.Append(CsvEscape(v.DataDisplay));
            sb.AppendLine();
        }
        foreach (var sub in _registry.GetSubKeyNames(root, subPath))
        {
            var childSub = string.IsNullOrEmpty(subPath) ? sub : subPath + "\\" + sub;
            WriteCsv(sb, root, childSub);
        }
    }

    private static object? SerializableData(RegistryValueKind kind, object? raw)
    {
        if (raw is null) return null;
        return kind switch
        {
            RegistryValueKind.DWord => raw is int i ? i : raw,
            RegistryValueKind.QWord => raw is long l ? l : raw,
            RegistryValueKind.MultiString => raw is string[] arr ? arr : raw,
            RegistryValueKind.Binary or RegistryValueKind.None =>
                raw is byte[] b ? Convert.ToHexString(b) : raw,
            _ => raw.ToString(),
        };
    }

    private static string CsvEscape(string? s)
    {
        s ??= string.Empty;
        // Spreadsheet formula-injection guard: prefix with single quote if the cell starts with =, +, -, @
        if (s.Length > 0 && (s[0] == '=' || s[0] == '+' || s[0] == '-' || s[0] == '@'))
            s = "'" + s;
        bool needsQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        if (!needsQuote) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
