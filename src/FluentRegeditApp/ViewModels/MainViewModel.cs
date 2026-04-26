using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentRegeditApp.Models;
using FluentRegeditApp.Services;

namespace FluentRegeditApp.ViewModels;

public sealed class MainViewModel
{
    public RegistryService Registry { get; } = new();

    public ObservableCollection<RegistryKeyNode> Roots { get; } = new();

    public ObservableCollection<RegistryValueItem> Values { get; } = new();

    private readonly List<RegistryValueItem> _allValues = new();
    private string _filter = string.Empty;

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

    /// <summary>Populate <see cref="RegistryKeyNode.Children"/> the first time the node expands.</summary>
    public void EnsureChildrenLoaded(RegistryKeyNode node)
    {
        if (node.ChildrenLoaded) return;
        node.Children.Clear();
        foreach (var name in Registry.GetSubKeyNames(node.Root, node.SubPath))
        {
            var child = node.CreateChild(name);
            child.AddPlaceholder();
            node.Children.Add(child);
        }
        node.ChildrenLoaded = true;
    }

    /// <summary>Replace the right pane with the values of the given node.</summary>
    public void LoadValues(RegistryKeyNode node)
    {
        _allValues.Clear();
        foreach (var v in Registry.GetValues(node.Root, node.SubPath))
            _allValues.Add(v);
        ApplyFilter();
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
    public RegistryKeyNode? Resolve(RegistryRoot root, string subPath)
    {
        var rootNode = Roots.FirstOrDefault(r => r.Root == root);
        if (rootNode is null) return null;
        EnsureChildrenLoaded(rootNode);

        if (string.IsNullOrEmpty(subPath))
            return rootNode;

        var current = rootNode;
        var segments = subPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (var seg in segments)
        {
            EnsureChildrenLoaded(current);
            var next = current.Children.FirstOrDefault(c =>
                string.Equals(c.Name, seg, StringComparison.OrdinalIgnoreCase));
            if (next is null) return null;
            current.IsExpanded = true;
            current = next;
        }
        return current;
    }
}
