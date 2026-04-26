using System;
using System.Collections.ObjectModel;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using FluentRegeditApp.Models;
using FluentRegeditApp.Services;

namespace FluentRegeditApp.Views;

public sealed partial class SearchDialog : ContentDialog
{
    private readonly RegistrySearchService _search;
    private readonly RegistryRoot _startRoot;
    private readonly string _startSubPath;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private CancellationTokenSource? _cts;

    public ObservableCollection<SearchHit> Results { get; } = new();
    public SearchHit? SelectedHit { get; private set; }
    public SearchOptions? LastOptions { get; private set; }

    public SearchDialog(RegistrySearchService search, RegistryRoot startRoot, string startSubPath,
        string? prefilledQuery = null, bool defaultRegex = false, bool autoFind = false)
    {
        InitializeComponent();
        _search = search;
        _startRoot = startRoot;
        _startSubPath = startSubPath;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        ResultsList.ItemsSource = Results;
        ScopeText.Text = "Scope: " + (string.IsNullOrEmpty(startSubPath)
            ? startRoot.FullName()
            : $"{startRoot.FullName()}\\{startSubPath}");
        if (!string.IsNullOrEmpty(prefilledQuery)) QueryBox.Text = prefilledQuery;
        OptRegex.IsChecked = defaultRegex;
        if (autoFind && !string.IsNullOrEmpty(prefilledQuery))
        {
            Loaded += async (_, _) => { await System.Threading.Tasks.Task.Yield(); OnFindClick(this, new RoutedEventArgs()); };
        }
    }

    private SearchScope BuildScope()
    {
        SearchScope s = 0;
        if (OptKeys.IsChecked == true) s |= SearchScope.Keys;
        if (OptValueNames.IsChecked == true) s |= SearchScope.ValueNames;
        if (OptValueData.IsChecked == true) s |= SearchScope.ValueData;
        return s;
    }

    private async void OnFindClick(object sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text;
        if (string.IsNullOrEmpty(query)) return;
        var scope = BuildScope();
        if (scope == 0) { StatusText.Text = "Pick at least one of: Keys, Value names, Value data."; return; }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Results.Clear();
        SelectedHit = null;
        IsPrimaryButtonEnabled = false;
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        StatusText.Text = "Searching…";

        var options = new SearchOptions(
            query, scope,
            MatchWholeString: OptWhole.IsChecked == true,
            CaseSensitive: OptCase.IsChecked == true,
            UseRegex: OptRegex.IsChecked == true);
        LastOptions = options;

        try
        {
            await _search.SearchAsync(_startRoot, _startSubPath, options, hit =>
            {
                _dispatcher.TryEnqueue(() => Results.Add(hit));
            }, ct);
            StatusText.Text = ct.IsCancellationRequested
                ? $"Cancelled. {Results.Count} match(es)."
                : $"Done. {Results.Count} match(es).";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = $"Cancelled. {Results.Count} match(es).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error: " + ex.Message;
        }
        finally
        {
            StartButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; OnFindClick(sender, e); }
    }

    private void OnResultsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedHit = ResultsList.SelectedItem as SearchHit;
        IsPrimaryButtonEnabled = SelectedHit is not null;
    }

    private void OnResultsDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchHit hit)
        {
            SelectedHit = hit;
            Hide();
        }
    }
}
