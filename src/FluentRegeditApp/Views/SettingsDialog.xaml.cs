using Microsoft.UI.Xaml.Controls;
using FluentRegeditApp.Services;

namespace FluentRegeditApp.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    public AppSettings Result { get; private set; }

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

        PrimaryButtonClick += (_, _) =>
        {
            Result.Theme = (AppTheme)System.Math.Max(0, ThemeCombo.SelectedIndex);
            if (View32.IsChecked == true) Result.View = RegView.Registry32;
            else if (View64.IsChecked == true) Result.View = RegView.Registry64;
            else Result.View = RegView.Default;
            Result.ConfirmDestructive = ConfirmDestructiveToggle.IsOn;
            Result.RegexSearch = RegexSearchToggle.IsOn;
        };
    }
}
