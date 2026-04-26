using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluentRegeditApp.Models;

/// <summary>
/// Tree node representing a registry key. Children are loaded lazily.
/// </summary>
public sealed class RegistryKeyNode : INotifyPropertyChanged
{
    public RegistryRoot Root { get; }
    public string SubPath { get; }
    public string Name { get; }
    public bool IsRoot => string.IsNullOrEmpty(SubPath);

    public ObservableCollection<RegistryKeyNode> Children { get; } = new();
    public bool ChildrenLoaded { get; set; }

    /// <summary>
    /// Re-entry guard: true while an async EnsureChildrenLoaded is in flight for this node.
    /// Not a bound property — used only to short-circuit duplicate expansion requests.
    /// </summary>
    public bool IsLoading { get; set; }

    /// <summary>
    /// True when <see cref="Children"/> contains only the placeholder used to
    /// force the TreeView to render an expansion chevron before real children
    /// are fetched.
    /// </summary>
    public bool IsPlaceholder { get; init; }

    /// <summary>Adds a single placeholder child so the chevron appears in the tree.</summary>
    public void AddPlaceholder()
    {
        if (Children.Count == 0)
            Children.Add(new RegistryKeyNode(Root, SubPath, "…") { IsPlaceholder = true });
    }

    /// <summary>Replaces children with a single "Loading…" placeholder for visual feedback.</summary>
    public void ShowLoadingPlaceholder()
    {
        Children.Clear();
        Children.Add(new RegistryKeyNode(Root, SubPath, "Loading…") { IsPlaceholder = true });
    }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnChanged(); } }
    }

    public string FullPath => IsRoot ? Root.FullName() : $"{Root.FullName()}\\{SubPath}";

    public RegistryKeyNode(RegistryRoot root, string subPath, string name)
    {
        Root = root;
        SubPath = subPath ?? string.Empty;
        Name = name;
    }

    public static RegistryKeyNode CreateRoot(RegistryRoot root) =>
        new(root, string.Empty, root.FullName());

    public RegistryKeyNode CreateChild(string childName)
    {
        var sub = string.IsNullOrEmpty(SubPath) ? childName : $"{SubPath}\\{childName}";
        return new RegistryKeyNode(Root, sub, childName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
