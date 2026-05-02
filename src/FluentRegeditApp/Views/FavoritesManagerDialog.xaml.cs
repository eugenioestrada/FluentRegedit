using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FluentRegeditApp.Services;

namespace FluentRegeditApp.Views;

public sealed partial class FavoritesManagerDialog : ContentDialog
{
    private readonly FavoritesService _service;

    public FavoritesManagerDialog(FavoritesService service)
    {
        InitializeComponent();
        _service = service;
        ItemsList.ItemsSource = _service.Items;
    }

    private Favorite? Selected => ItemsList.SelectedItem as Favorite;
    private int SelectedIndex => ItemsList.SelectedIndex;

    private async void OnRename(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel is null) return;
        var dlg = PrepareDialog(new RenameDialog("Rename favorite", sel.Name));
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dlg.NewName))
        {
            _service.Rename(sel.Name, dlg.NewName);
            ItemsList.ItemsSource = _service.Items;
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel is null) return;
        var confirm = PrepareDialog(new ContentDialog
        {
            Title = "Delete favorite",
            Content = $"Remove '{sel.Name}'?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        });
        var r = await confirm.ShowAsync();
        if (r == ContentDialogResult.Primary)
            _service.Remove(sel.Name);
    }

    private void OnMoveUp(object sender, RoutedEventArgs e)
    {
        var idx = SelectedIndex;
        if (idx <= 0) return;
        _service.Move(idx, idx - 1);
        ItemsList.SelectedIndex = idx - 1;
    }

    private void OnMoveDown(object sender, RoutedEventArgs e)
    {
        var idx = SelectedIndex;
        if (idx < 0 || idx >= _service.Items.Count - 1) return;
        _service.Move(idx, idx + 1);
        ItemsList.SelectedIndex = idx + 1;
    }

    private T PrepareDialog<T>(T dialog) where T : ContentDialog
    {
        dialog.XamlRoot = XamlRoot;
        dialog.RequestedTheme = ActualTheme;
        return dialog;
    }
}
