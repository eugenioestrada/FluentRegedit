using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentRegeditApp.Models;
using FluentRegeditApp.Services;

namespace FluentRegeditApp.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const int BatchSize = 200;

    public RegistryService Registry { get; } = new();

    public ObservableCollection<RegistryKeyNode> Roots { get; } = new();

    public ObservableCollection<RegistryValueItem> Values { get; } = new();

    private readonly List<RegistryValueItem> _allValues = new();
    private string _filter = string.Empty;

    private CancellationTokenSource? _valuesCts;

    private bool _isLoadingValues;
    public bool IsLoadingValues
    {
        get => _isLoadingValues;
        private set { if (_isLoadingValues != value) { _isLoadingValues = value; OnChanged(); } }
    }

    private bool _isLoadingTree;
    public bool IsLoadingTree
    {
        get => _isLoadingTree;
        private set { if (_isLoadingTree != value) { _isLoadingTree = value; OnChanged(); } }
    }

    public string ValueFilter
    {
        get => _filter;
        set
        {
            _filter = value ?? string.Empty;
            ApplyFilter();
        }
    }

    public int TotalValueCount => _allValues.Count;

    public MainViewModel()
    {
        RebuildRoots();
    }

    /// <summary>Rebuild the list of root nodes (e.g. after switching 32/64-bit registry view).</summary>
    public void RebuildRoots()
    {
        Roots.Clear();
        foreach (RegistryRoot r in Enum.GetValues(typeof(RegistryRoot)))
        {
            var node = RegistryKeyNode.CreateRoot(r);
            node.AddPlaceholder();
            Roots.Add(node);
        }
        _allValues.Clear();
        Values.Clear();
    }

    /// <summary>
    /// Populate <see cref="RegistryKeyNode.Children"/> the first time the node expands.
    /// Enumeration runs on a background thread; mutation happens after the await on the
    /// captured (UI) SynchronizationContext.
    /// </summary>
    /// <returns>The number of subkeys loaded; -1 if already loaded, cancelled, or an in-flight load was skipped.</returns>
    public async Task<int> EnsureChildrenLoadedAsync(RegistryKeyNode node, CancellationToken ct = default)
    {
        if (node.ChildrenLoaded) return -1;
        if (node.IsLoading) return -1;

        node.IsLoading = true;
        IsLoadingTree = true;
        // Show "Loading…" placeholder so the user sees feedback if the load is slow.
        node.ShowLoadingPlaceholder();

        try
        {
            var root = node.Root;
            var sub = node.SubPath;
            List<string> names;
            try
            {
                names = await Task.Run(() => Registry.GetSubKeyNames(root, sub).ToList(), ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Restore neutral placeholder so chevron still works.
                node.Children.Clear();
                node.AddPlaceholder();
                return -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EnsureChildrenLoadedAsync] {node.FullPath}: {ex}");
                node.Children.Clear();
                node.AddPlaceholder();
                throw;
            }

            if (ct.IsCancellationRequested)
            {
                node.Children.Clear();
                node.AddPlaceholder();
                return -1;
            }

            node.Children.Clear();
            try
            {
                int i = 0;
                foreach (var name in names)
                {
                    var child = node.CreateChild(name);
                    child.AddPlaceholder();
                    node.Children.Add(child);
                    i++;

                    if (i % BatchSize == 0)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            node.Children.Clear();
                            node.AddPlaceholder();
                            return -1;
                        }
                        try
                        {
                            await Task.Delay(1, ct).ConfigureAwait(true);
                        }
                        catch (OperationCanceledException)
                        {
                            node.Children.Clear();
                            node.AddPlaceholder();
                            return -1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EnsureChildrenLoadedAsync/batch] {node.FullPath}: {ex}");
                node.Children.Clear();
                node.AddPlaceholder();
                throw;
            }
            node.ChildrenLoaded = true;
            return names.Count;
        }
        finally
        {
            node.IsLoading = false;
            IsLoadingTree = false;
        }
    }

    /// <summary>
    /// Replace the right pane with the values of the given node. Cancels any prior in-flight
    /// values load. Returns the count loaded, or -1 if cancelled.
    /// </summary>
    public async Task<int> LoadValuesAsync(RegistryKeyNode node, CancellationToken ct = default)
    {
        // Cancel any prior values load.
        var prior = Interlocked.Exchange(ref _valuesCts, null);
        try { prior?.Cancel(); } catch { /* ignored */ }
        prior?.Dispose();

        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _valuesCts = linked;
        var token = linked.Token;

        IsLoadingValues = true;
        try
        {
            var root = node.Root;
            var sub = node.SubPath;
            List<RegistryValueItem> values;
            try
            {
                values = await Task.Run(() => Registry.GetValues(root, sub).ToList(), token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LoadValuesAsync] {node.FullPath}: {ex}");
                throw;
            }

            if (token.IsCancellationRequested) return -1;

            _allValues.Clear();
            _allValues.AddRange(values);
            ApplyFilter();
            return values.Count;
        }
        finally
        {
            // Only clear loading state if this was still the active CTS (we weren't superseded).
            if (ReferenceEquals(_valuesCts, linked))
            {
                IsLoadingValues = false;
                _valuesCts = null;
                linked.Dispose();
            }
        }
    }

    private void ApplyFilter()
    {
        Values.Clear();
        if (string.IsNullOrEmpty(_filter))
        {
            foreach (var v in _allValues) Values.Add(v);
            return;
        }
        foreach (var v in _allValues)
        {
            if (v.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                (v.DataDisplay?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                v.KindDisplay.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            {
                Values.Add(v);
            }
        }
    }

    /// <summary>Find or create the chain of nodes leading to (root, subPath), expanding & loading along the way.</summary>
    public async Task<RegistryKeyNode?> ResolveAsync(RegistryRoot root, string subPath, CancellationToken ct = default)
    {
        var rootNode = Roots.FirstOrDefault(r => r.Root == root);
        if (rootNode is null) return null;
        await EnsureChildrenLoadedAsync(rootNode, ct).ConfigureAwait(true);

        if (string.IsNullOrEmpty(subPath))
            return rootNode;

        var current = rootNode;
        var segments = subPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (var seg in segments)
        {
            await EnsureChildrenLoadedAsync(current, ct).ConfigureAwait(true);
            var next = current.Children.FirstOrDefault(c =>
                string.Equals(c.Name, seg, StringComparison.OrdinalIgnoreCase));
            if (next is null) return null;
            current.IsExpanded = true;
            current = next;
        }
        return current;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
