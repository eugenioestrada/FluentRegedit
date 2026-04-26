using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Services;

[Flags]
public enum SearchScope
{
    Keys = 1,
    ValueNames = 2,
    ValueData = 4,
    All = Keys | ValueNames | ValueData,
}

public sealed record SearchOptions(
    string Query,
    SearchScope Scope = SearchScope.All,
    bool MatchWholeString = false,
    bool CaseSensitive = false,
    bool UseRegex = false,
    int MaxResults = 1000);

public enum SearchHitKind { Key, ValueName, ValueData }

public sealed class SearchHit
{
    public RegistryRoot Root { get; set; }
    public string SubPath { get; set; } = string.Empty;
    public SearchHitKind Kind { get; set; }
    public string? ValueName { get; set; }
    public string? Preview { get; set; }

    public string FullPath => string.IsNullOrEmpty(SubPath)
        ? Root.FullName()
        : $"{Root.FullName()}\\{SubPath}";

    public string Display => Kind switch
    {
        SearchHitKind.Key => FullPath,
        SearchHitKind.ValueName => $"{FullPath}  →  {ValueName}",
        SearchHitKind.ValueData => $"{FullPath}  →  {ValueName}",
        _ => FullPath,
    };

    public string KindDisplay => Kind switch
    {
        SearchHitKind.Key => "key",
        SearchHitKind.ValueName => "value name",
        SearchHitKind.ValueData => "value data",
        _ => string.Empty,
    };
}

public sealed class RegistrySearchService
{
    private readonly RegistryService _registry;
    public RegistrySearchService(RegistryService registry) => _registry = registry;

    public Task SearchAsync(
        RegistryRoot startRoot,
        string startSubPath,
        SearchOptions options,
        Action<SearchHit> onHit,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            int count = 0;
            Regex? rx = null;
            if (options.UseRegex && !string.IsNullOrEmpty(options.Query))
            {
                var rxOpts = RegexOptions.CultureInvariant | RegexOptions.Compiled;
                if (!options.CaseSensitive) rxOpts |= RegexOptions.IgnoreCase;
                rx = new Regex(options.Query, rxOpts, TimeSpan.FromSeconds(2));
            }
            SearchKey(startRoot, startSubPath, options, rx, onHit, ref count, ct);
        }, ct);
    }

    private void SearchKey(
        RegistryRoot root, string subPath, SearchOptions options, Regex? rx,
        Action<SearchHit> onHit, ref int count, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || count >= options.MaxResults) return;

        using var key = _registry.OpenKey(root, subPath);
        if (key is null) return;

        // Match values in this key
        if ((options.Scope & (SearchScope.ValueNames | SearchScope.ValueData)) != 0)
        {
            string[] valueNames;
            try { valueNames = key.GetValueNames(); }
            catch { valueNames = Array.Empty<string>(); }

            foreach (var vname in valueNames)
            {
                if (ct.IsCancellationRequested || count >= options.MaxResults) return;

                if ((options.Scope & SearchScope.ValueNames) != 0 &&
                    Match(vname.Length == 0 ? "(Default)" : vname, options, rx))
                {
                    onHit(new SearchHit
                    {
                        Root = root, SubPath = subPath, Kind = SearchHitKind.ValueName,
                        ValueName = vname, Preview = vname,
                    });
                    count++;
                    if (count >= options.MaxResults) return;
                }

                if ((options.Scope & SearchScope.ValueData) != 0)
                {
                    string? data = null;
                    try
                    {
                        var raw = key.GetValue(vname, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        data = StringifyForSearch(raw);
                    }
                    catch { data = null; }

                    if (data is not null && Match(data, options, rx))
                    {
                        onHit(new SearchHit
                        {
                            Root = root, SubPath = subPath, Kind = SearchHitKind.ValueData,
                            ValueName = vname, Preview = Trim(data, 120),
                        });
                        count++;
                        if (count >= options.MaxResults) return;
                    }
                }
            }
        }

        // Recurse subkeys
        string[] subs;
        try { subs = key.GetSubKeyNames(); }
        catch { subs = Array.Empty<string>(); }

        foreach (var sub in subs)
        {
            if (ct.IsCancellationRequested || count >= options.MaxResults) return;

            var childSub = string.IsNullOrEmpty(subPath) ? sub : $"{subPath}\\{sub}";

            if ((options.Scope & SearchScope.Keys) != 0 && Match(sub, options, rx))
            {
                onHit(new SearchHit
                {
                    Root = root, SubPath = childSub, Kind = SearchHitKind.Key, Preview = sub,
                });
                count++;
                if (count >= options.MaxResults) return;
            }

            SearchKey(root, childSub, options, rx, onHit, ref count, ct);
        }
    }

    private static bool Match(string haystack, SearchOptions options, Regex? rx)
    {
        if (string.IsNullOrEmpty(options.Query)) return false;
        if (rx is not null)
        {
            try { return rx.IsMatch(haystack); }
            catch (RegexMatchTimeoutException) { return false; }
        }
        var cmp = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return options.MatchWholeString
            ? string.Equals(haystack, options.Query, cmp)
            : haystack.Contains(options.Query, cmp);
    }

    private static string StringifyForSearch(object? raw)
    {
        return raw switch
        {
            null => string.Empty,
            string s => s,
            string[] ms => string.Join('\n', ms),
            int i => i.ToString(),
            long l => l.ToString(),
            byte[] b => Convert.ToHexString(b),
            _ => raw.ToString() ?? string.Empty,
        };
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
