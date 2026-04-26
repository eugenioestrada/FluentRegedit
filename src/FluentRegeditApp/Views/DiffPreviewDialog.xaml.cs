using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FluentRegeditApp.Services;

namespace FluentRegeditApp.Views;

public sealed partial class DiffPreviewDialog : ContentDialog
{
    public DiffPreviewDialog(IReadOnlyList<DiffEntry> entries)
    {
        InitializeComponent();

        var adds = entries.Where(e => e.Kind == ChangeKind.KeyAdded || e.Kind == ChangeKind.ValueAdded).ToList();
        var mods = entries.Where(e => e.Kind == ChangeKind.ValueModified).ToList();
        var dels = entries.Where(e => e.Kind == ChangeKind.KeyDeleted || e.Kind == ChangeKind.ValueDeleted).ToList();

        AddList.ItemsSource = adds;
        ModList.ItemsSource = mods;
        DelList.ItemsSource = dels;

        AddHeader.Text = $"Additions ({adds.Count})";
        ModHeader.Text = $"Modifications ({mods.Count})";
        DelHeader.Text = $"Deletions ({dels.Count})";

        AddExpander.Visibility = adds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ModExpander.Visibility = mods.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DelExpander.Visibility = dels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
