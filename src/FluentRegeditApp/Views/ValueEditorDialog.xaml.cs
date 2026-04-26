using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using FluentRegeditApp.Models;

namespace FluentRegeditApp.Views;

public sealed partial class ValueEditorDialog : ContentDialog
{
    public string ValueName { get; private set; } = string.Empty;
    public RegistryValueKind Kind { get; private set; } = RegistryValueKind.String;
    public object? Data { get; private set; }

    private bool _nameLocked;

    public ValueEditorDialog()
    {
        InitializeComponent();
        PrimaryButtonClick += OnPrimary;
    }

    public static ValueEditorDialog ForCreate() => new() { Title = "New value" };

    public static ValueEditorDialog ForEdit(RegistryValueItem v)
    {
        var d = new ValueEditorDialog { Title = "Edit value" };
        d.NameBox.Text = v.Name;
        d.NameBox.IsEnabled = false;
        d._nameLocked = true;

        d.KindCombo.SelectedIndex = KindToIndex(v.Kind);
        d.KindCombo.IsEnabled = false;

        d.PopulateData(v.Kind, v.RawData);
        d.NameLabel.Text = v.IsDefault ? "(Default)" : v.Name;
        return d;
    }

    private void PopulateData(RegistryValueKind kind, object? raw)
    {
        switch (kind)
        {
            case RegistryValueKind.String:
            case RegistryValueKind.ExpandString:
                TextBoxData.Text = raw as string ?? string.Empty; break;
            case RegistryValueKind.MultiString:
                TextBoxData.Text = string.Join("\r\n", (raw as string[]) ?? Array.Empty<string>()); break;
            case RegistryValueKind.DWord:
                NumberBox.Text = raw is int i ? ((uint)i).ToString("x", CultureInfo.InvariantCulture) : "0"; break;
            case RegistryValueKind.QWord:
                NumberBox.Text = raw is long l ? ((ulong)l).ToString("x", CultureInfo.InvariantCulture) : "0"; break;
            case RegistryValueKind.Binary:
            case RegistryValueKind.None:
                TextBoxData.Text = raw is byte[] b ? FormatHex(b) : string.Empty; break;
        }
        ApplyKindUI();
    }

    private static int KindToIndex(RegistryValueKind k) => k switch
    {
        RegistryValueKind.String => 0,
        RegistryValueKind.ExpandString => 1,
        RegistryValueKind.MultiString => 2,
        RegistryValueKind.DWord => 3,
        RegistryValueKind.QWord => 4,
        RegistryValueKind.Binary => 5,
        RegistryValueKind.None => 6,
        _ => 0,
    };

    private RegistryValueKind SelectedKind => KindCombo.SelectedIndex switch
    {
        0 => RegistryValueKind.String,
        1 => RegistryValueKind.ExpandString,
        2 => RegistryValueKind.MultiString,
        3 => RegistryValueKind.DWord,
        4 => RegistryValueKind.QWord,
        5 => RegistryValueKind.Binary,
        6 => RegistryValueKind.None,
        _ => RegistryValueKind.String,
    };

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => ApplyKindUI();

    private void ApplyKindUI()
    {
        var k = SelectedKind;
        bool number = k == RegistryValueKind.DWord || k == RegistryValueKind.QWord;
        TextPanel.Visibility = number ? Visibility.Collapsed : Visibility.Visible;
        NumberPanel.Visibility = number ? Visibility.Visible : Visibility.Collapsed;
        TextBoxData.AcceptsReturn = (k == RegistryValueKind.MultiString || k == RegistryValueKind.Binary || k == RegistryValueKind.None);
    }

    private void OnBaseChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (!ulong.TryParse(NumberBox.Text, NumberStyles.HexNumber | NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var _))
        { /* ignore */ }
        // Try to convert between bases when toggling.
        if (HexRadio.IsChecked == true)
        {
            if (ulong.TryParse(NumberBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec))
                NumberBox.Text = dec.ToString("x", CultureInfo.InvariantCulture);
        }
        else
        {
            if (ulong.TryParse(NumberBox.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                NumberBox.Text = hex.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            ValueName = _nameLocked ? NameBox.Text : (NameBox.Text ?? string.Empty);
            Kind = SelectedKind;

            switch (Kind)
            {
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    Data = TextBoxData.Text ?? string.Empty;
                    break;
                case RegistryValueKind.MultiString:
                    Data = (TextBoxData.Text ?? string.Empty)
                        .Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    break;
                case RegistryValueKind.DWord:
                    {
                        var style = HexRadio.IsChecked == true ? NumberStyles.HexNumber : NumberStyles.Integer;
                        if (!uint.TryParse((NumberBox.Text ?? "0").TrimStart('0', 'x', 'X'), style,
                                CultureInfo.InvariantCulture, out var u))
                            throw new FormatException("Invalid DWORD.");
                        Data = unchecked((int)u);
                        break;
                    }
                case RegistryValueKind.QWord:
                    {
                        var style = HexRadio.IsChecked == true ? NumberStyles.HexNumber : NumberStyles.Integer;
                        if (!ulong.TryParse((NumberBox.Text ?? "0").TrimStart('0', 'x', 'X'), style,
                                CultureInfo.InvariantCulture, out var u))
                            throw new FormatException("Invalid QWORD.");
                        Data = unchecked((long)u);
                        break;
                    }
                case RegistryValueKind.Binary:
                case RegistryValueKind.None:
                    Data = ParseHex(TextBoxData.Text ?? string.Empty);
                    break;
            }
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private static string FormatHex(byte[] b)
    {
        var sb = new StringBuilder(b.Length * 3);
        for (int i = 0; i < b.Length; i++)
        {
            if (i > 0 && i % 16 == 0) sb.Append('\n');
            else if (i > 0) sb.Append(' ');
            sb.Append(b[i].ToString("x2"));
        }
        return sb.ToString();
    }

    private static byte[] ParseHex(string s)
    {
        var clean = new string(s.Where(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')).ToArray());
        if ((clean.Length & 1) == 1) throw new FormatException("Hex data must have an even number of digits.");
        var bytes = new byte[clean.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(clean.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }
}
