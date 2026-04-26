using System.Collections.Generic;

namespace FluentRegeditApp.Services;

/// <summary>
/// Browser-style back/forward stack. Items are arbitrary; <see cref="Current"/>
/// tracks the active entry.
/// </summary>
public sealed class NavigationHistory<T> where T : class
{
    private readonly List<T> _entries = new();
    private int _index = -1;

    public T? Current => _index >= 0 && _index < _entries.Count ? _entries[_index] : null;
    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _entries.Count - 1;

    /// <summary>Push a new entry, dropping any forward history.</summary>
    public void Visit(T entry)
    {
        if (_index >= 0 && _index < _entries.Count - 1)
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        _entries.Add(entry);
        _index = _entries.Count - 1;
    }

    public T? Back() { if (CanGoBack) { _index--; return Current; } return null; }
    public T? Forward() { if (CanGoForward) { _index++; return Current; } return null; }
}
