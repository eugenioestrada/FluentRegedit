using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FluentRegeditApp.Services;

namespace FluentRegeditApp.Views;

public sealed partial class SnapshotManagerDialog : ContentDialog
{
    private readonly SnapshotManager _manager;

    public RegImportResult? Result { get; private set; }

    public SnapshotManagerDialog(SnapshotManager manager)
    {
        InitializeComponent();
        _manager = manager;
        LocationText.Text = $"Location: {_manager.BackupDirectory}";
        Refresh();
    }

    private void Refresh()
    {
        ItemsList.ItemsSource = _manager.List();
    }

    private SnapshotInfo? Selected => ItemsList.SelectedItem as SnapshotInfo;

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel is null)
        {
            ShowStatus("Select a snapshot to restore.");
            return;
        }
        var confirm = new ContentDialog
        {
            Title = "Restore snapshot",
            Content = $"Import '{sel.FileName}'? This will overwrite affected registry values.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        var r = await confirm.ShowAsync();
        if (r != ContentDialogResult.Primary) return;

        try
        {
            Result = _manager.Restore(sel);
            Hide();
        }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel is null)
        {
            ShowStatus("Select a snapshot to delete.");
            return;
        }
        var confirm = new ContentDialog
        {
            Title = "Delete snapshot",
            Content = $"Permanently delete '{sel.FileName}'?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        var r = await confirm.ShowAsync();
        if (r != ContentDialogResult.Primary) return;

        try
        {
            _manager.Delete(sel);
            Refresh();
            HideStatus();
        }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _manager.BackupDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }

    private void ShowStatus(string msg)
    {
        StatusText.Text = msg;
        StatusText.Visibility = Visibility.Visible;
    }

    private void HideStatus() => StatusText.Visibility = Visibility.Collapsed;
}
