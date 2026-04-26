using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using FluentRegeditApp.Services;

namespace FluentRegeditApp.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    public AppSettings Result { get; private set; }
    public IntPtr OwnerHwnd { get; set; }

    public SettingsDialog(AppSettings current)
    {
        InitializeComponent();
        Result = new AppSettings
        {
            Theme = current.Theme,
            View = current.View,
            ConfirmDestructive = current.ConfirmDestructive,
            RecentLocationsLimit = current.RecentLocationsLimit,
            RegexSearch = current.RegexSearch,
            SnapshotDirectory = current.SnapshotDirectory,
        };

        ThemeCombo.SelectedIndex = (int)current.Theme;
        switch (current.View)
        {
            case RegView.Registry32: View32.IsChecked = true; break;
            case RegView.Registry64: View64.IsChecked = true; break;
            default: ViewDefault.IsChecked = true; break;
        }
        ConfirmDestructiveToggle.IsOn = current.ConfirmDestructive;
        RegexSearchToggle.IsOn = current.RegexSearch;
        SnapshotDirBox.Text = current.SnapshotDirectory ?? string.Empty;

        PrimaryButtonClick += (_, _) =>
        {
            Result.Theme = (AppTheme)System.Math.Max(0, ThemeCombo.SelectedIndex);
            if (View32.IsChecked == true) Result.View = RegView.Registry32;
            else if (View64.IsChecked == true) Result.View = RegView.Registry64;
            else Result.View = RegView.Default;
            Result.ConfirmDestructive = ConfirmDestructiveToggle.IsOn;
            Result.RegexSearch = RegexSearchToggle.IsOn;
            var dir = (SnapshotDirBox.Text ?? string.Empty).Trim();
            Result.SnapshotDirectory = string.IsNullOrEmpty(dir) ? null : dir;
        };
    }

    private async void OnBrowseSnapshotDir(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        if (OwnerHwnd != System.IntPtr.Zero)
            WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerHwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) SnapshotDirBox.Text = folder.Path;
    }
}
