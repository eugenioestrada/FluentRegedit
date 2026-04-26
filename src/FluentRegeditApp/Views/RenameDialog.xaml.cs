using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentRegeditApp.Views;

public sealed partial class RenameDialog : ContentDialog
{
    public string NewName { get; private set; } = string.Empty;

    public RenameDialog(string title, string currentName)
    {
        InitializeComponent();
        Title = title;
        NameBox.Text = currentName ?? string.Empty;
        Loaded += (_, _) => { NameBox.Focus(FocusState.Programmatic); NameBox.SelectAll(); };
        PrimaryButtonClick += OnPrimary;
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var text = (NameBox.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            args.Cancel = true;
            ErrorText.Text = "Name cannot be empty.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        NewName = text;
    }
}
