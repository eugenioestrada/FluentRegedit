using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FluentRegeditApp.Views;

public sealed partial class CommandPaletteDialog : ContentDialog
{
    public sealed record PaletteCommand(string Name, string? Description, Action Invoke);

    private readonly List<PaletteCommand> _all;

    public CommandPaletteDialog(IEnumerable<PaletteCommand> commands)
    {
        InitializeComponent();
        _all = commands.ToList();
        ResultsList.ItemsSource = _all;
        Loaded += (_, _) =>
        {
            QueryBox.Focus(FocusState.Programmatic);
            if (_all.Count > 0) ResultsList.SelectedIndex = 0;
        };
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        var q = (QueryBox.Text ?? string.Empty).Trim();
        IEnumerable<PaletteCommand> filtered = _all;
        if (q.Length > 0)
            filtered = _all.Where(c =>
                (c.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        var list = filtered.ToList();
        ResultsList.ItemsSource = list;
        if (list.Count > 0) ResultsList.SelectedIndex = 0;
    }

    private void OnQueryKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down)
        {
            if (ResultsList.Items.Count > 0)
                ResultsList.SelectedIndex = Math.Min(ResultsList.SelectedIndex + 1, ResultsList.Items.Count - 1);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Up)
        {
            if (ResultsList.SelectedIndex > 0) ResultsList.SelectedIndex--;
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter)
        {
            InvokeSelected();
            e.Handled = true;
        }
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PaletteCommand cmd) Invoke(cmd);
    }

    private void InvokeSelected()
    {
        if (ResultsList.SelectedItem is PaletteCommand cmd) Invoke(cmd);
    }

    private void Invoke(PaletteCommand cmd)
    {
        Hide();
        try { cmd.Invoke(); } catch { /* swallow; caller is responsible */ }
    }
}
