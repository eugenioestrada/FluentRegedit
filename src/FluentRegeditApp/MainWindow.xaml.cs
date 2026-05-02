using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Win32;
using Windows.UI;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using FluentRegeditApp.Models;
using FluentRegeditApp.Services;
using FluentRegeditApp.ViewModels;
using FluentRegeditApp.Views;

namespace FluentRegeditApp
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new();

        private readonly NavigationHistory<RegistryKeyNode> _history = new();
        private readonly RegistrySearchService _search;
        private readonly RegFileExporter _exporter;
        private readonly RegFileImporter _importer = new();
        private BackupService _backup;
        private readonly RegistryEditService _edit = new();
        private readonly SettingsService _settingsService = new();
        private readonly FavoritesService _favorites = new();
        private readonly RecentLocationsService _recent;
        private SnapshotManager _snapshots;
        private readonly UndoJournal _undo = new();
        private readonly JsonCsvExporter _jsonCsv;
        private readonly DiffPreviewService _diff;
        private readonly HiveSaveLoadService _hive = new();
        private Microsoft.UI.Windowing.AppWindow? _appWindow;

        private AppSettings _settings;
        private readonly bool _isElevated;
        private bool _navigatingFromHistory;

        // Find Next state
        private List<SearchHit> _lastHits = new();
        private int _lastHitIndex = -1;
        private string? _lastQuery;
        private bool _lastUseRegex;

        public MainWindow()
        {
            InitializeComponent();
            Title = "FluentRegedit";

            _settings = _settingsService.Load();
            _recent = new RecentLocationsService(_settings.RecentLocationsLimit);
            _search = new RegistrySearchService(ViewModel.Registry);
            _exporter = new RegFileExporter(ViewModel.Registry);
            _backup = new BackupService(_exporter, _settings.SnapshotDirectory);
            _snapshots = new SnapshotManager(_backup, _importer);
            _jsonCsv = new JsonCsvExporter(ViewModel.Registry);
            _diff = new DiffPreviewService(_importer, ViewModel.Registry);

            _isElevated = DetectElevation();
            ApplySettings(initial: true);
            InitializeCustomTitleBar();
            UpdateStatus(null);
            UpdateUndoState();

            FavoritesMenuBar.Loaded += (_, _) => HookFavoritesMenu();
            RecentMenu.Loaded += (_, _) => HookRecentMenu();
            _favorites.Items.CollectionChanged += (_, _) => RefreshFavoritesMenu();
        }

        // ---- Settings & view application ----

        private static bool DetectElevation()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                var p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private void InitializeCustomTitleBar()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            _appWindow.Changed += (_, _) => DispatcherQueue.TryEnqueue(UpdateTitleBarInsets);

            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                _appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                _appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                _appWindow.TitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
                ApplyTitleBarButtonColors();
            }

            UpdateTitleBarInsets();
        }

        private void ApplyTitleBarButtonColors()
        {
            if (_appWindow is null || _appWindow.TitleBar is null ||
                !Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var isDark = Content is FrameworkElement fe && fe.ActualTheme == ElementTheme.Dark;
            var foreground = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            var inactiveForeground = isDark ? Color.FromArgb(160, 255, 255, 255) : Color.FromArgb(160, 0, 0, 0);
            var hoverBackground = isDark ? Color.FromArgb(32, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
            var pressedBackground = isDark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);

            var titleBar = _appWindow.TitleBar;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = hoverBackground;
            titleBar.ButtonPressedBackgroundColor = pressedBackground;
        }

        private void OnRootThemeChanged(FrameworkElement sender, object args) => ApplyTitleBarButtonColors();

        private void UpdateTitleBarInsets()
        {
            if (_appWindow is null || _appWindow.TitleBar is null)
            {
                return;
            }

            TitleBarLeftInsetColumn.Width = new GridLength(Math.Max(0, _appWindow.TitleBar.LeftInset));
            TitleBarRightInsetColumn.Width = new GridLength(Math.Max(0, _appWindow.TitleBar.RightInset));
        }

        private void ApplySettings(bool initial)
        {
            // Theme
            if (Content is FrameworkElement fe)
            {
                fe.ActualThemeChanged -= OnRootThemeChanged;
                fe.RequestedTheme = _settings.Theme switch
                {
                    AppTheme.Light => ElementTheme.Light,
                    AppTheme.Dark => ElementTheme.Dark,
                    _ => ElementTheme.Default,
                };
                fe.ActualThemeChanged += OnRootThemeChanged;
                ApplyTitleBarButtonColors();
            }

            // Registry view
            var rv = _settings.View switch
            {
                RegView.Registry32 => RegistryView.Registry32,
                RegView.Registry64 => RegistryView.Registry64,
                _ => RegistryView.Default,
            };
            ViewModel.Registry.View = rv;
            _edit.View = rv;
            _importer.View = rv;

            // View menu toggle states
            ViewDefaultItem.IsChecked = _settings.View == RegView.Default;
            View32Item.IsChecked = _settings.View == RegView.Registry32;
            View64Item.IsChecked = _settings.View == RegView.Registry64;
            StatusViewText.Text = _settings.View switch
            {
                RegView.Registry32 => "[32-bit view]",
                RegView.Registry64 => "[64-bit view]",
                _ => string.Empty,
            };

            // Hive submenu only enabled when elevated
            HiveMenu.IsEnabled = _isElevated;
            if (!_isElevated) HiveMenu.Text = "Hive (admin only — disabled)";

            if (!initial)
            {
                // Rebuild tree because the view changed
                _history.Clear();
                BackButton.IsEnabled = false;
                ForwardButton.IsEnabled = false;
                ViewModel.RebuildRoots();
                PathBox.Text = string.Empty;
                _undo.Clear();
                UpdateUndoState();
                UpdateStatus(null);
            }
        }

        // ---- Tree & navigation ----

        private async void OnTreeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            if (args.Item is RegistryKeyNode node && !node.IsPlaceholder)
            {
                try { await ViewModel.EnsureChildrenLoadedAsync(node); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"OnTreeExpanding: {ex}");
                    ShowToast("Failed to load subkeys", ex.Message, InfoBarSeverity.Warning);
                }
            }
        }

        private async void OnTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is RegistryKeyNode node && !node.IsPlaceholder)
                await NavigateToAsync(node, recordHistory: true);
        }

        private async void OnTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            if (sender.SelectedItem is RegistryKeyNode node && !node.IsPlaceholder)
                await NavigateToAsync(node, recordHistory: true);
        }

        private void OnKeysTreeKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (KeysTree.SelectedItem is not RegistryKeyNode node) return;
            // Don't act on root nodes or placeholders.
            if (node.IsPlaceholder) return;
            switch (e.Key)
            {
                case VirtualKey.Delete:
                    if (!string.IsNullOrEmpty(node.SubPath))
                    {
                        OnDeleteKeyClick(sender, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;
                case VirtualKey.Enter:
                    node.IsExpanded = !node.IsExpanded;
                    e.Handled = true;
                    break;
                case VirtualKey.F2:
                    if (!string.IsNullOrEmpty(node.SubPath))
                    {
                        OnRenameKeyClick(sender, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void OnValuesListKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (ValuesList.SelectedItem is not RegistryValueItem) return;
            switch (e.Key)
            {
                case VirtualKey.Delete:
                    OnDeleteValueClick(sender, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case VirtualKey.Enter:
                    OnModifyValueClick(sender, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case VirtualKey.F2:
                    OnRenameValueClick(sender, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }

        private async Task NavigateToAsync(RegistryKeyNode node, bool recordHistory)
        {
            int count;
            try
            {
                count = await ViewModel.LoadValuesAsync(node);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NavigateToAsync: {ex}");
                ShowToast("Failed to load values", ex.Message, InfoBarSeverity.Warning);
                count = -1;
            }

            // Cancelled (a newer load is in flight) — don't update path/history.
            if (count < 0) return;

            PathBox.Text = node.FullPath;
            UpdateStatus(node);

            if (recordHistory && !_navigatingFromHistory && _history.Current != node)
            {
                _history.Visit(node);
                _recent.Visit(node.Root, node.SubPath);
                RefreshRecentMenu();
            }

            BackButton.IsEnabled = _history.CanGoBack;
            ForwardButton.IsEnabled = _history.CanGoForward;
        }

        private void UpdateStatus(RegistryKeyNode? node)
        {
            if (node is null)
            {
                StatusPathText.Text = "Computer";
                StatusCountText.Text = string.Empty;
                return;
            }
            StatusPathText.Text = "Computer\\" + node.FullPath;
            var n = ViewModel.Values.Count;
            var total = ViewModel.TotalValueCount;
            StatusCountText.Text = (n == total)
                ? (n == 1 ? "1 value" : $"{n} values")
                : $"{n} of {total} values";
        }

        private async void OnBackClick(object sender, RoutedEventArgs e)
        {
            var n = _history.Back();
            if (n is not null) await NavigateFromHistoryAsync(n);
        }

        private async void OnForwardClick(object sender, RoutedEventArgs e)
        {
            var n = _history.Forward();
            if (n is not null) await NavigateFromHistoryAsync(n);
        }

        private async Task NavigateFromHistoryAsync(RegistryKeyNode node)
        {
            _navigatingFromHistory = true;
            try
            {
                var resolved = await ViewModel.ResolveAsync(node.Root, node.SubPath) ?? node;
                await SelectInTreeAsync(resolved);
                await NavigateToAsync(resolved, recordHistory: false);
            }
            finally { _navigatingFromHistory = false; }
        }

        private async void OnUpClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null || current.IsRoot) return;
            var idx = current.SubPath.LastIndexOf('\\');
            var parentSub = idx < 0 ? string.Empty : current.SubPath[..idx];
            var parent = await ViewModel.ResolveAsync(current.Root, parentSub);
            if (parent is not null)
            {
                await SelectInTreeAsync(parent);
                await NavigateToAsync(parent, recordHistory: true);
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null) return;
            current.ChildrenLoaded = false;
            current.Children.Clear();
            current.AddPlaceholder();
            await ViewModel.EnsureChildrenLoadedAsync(current);
            await NavigateToAsync(current, recordHistory: false);
        }

        private async void OnPathBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            await NavigateToPathAsync(PathBox.Text);
        }

        private async Task NavigateToPathAsync(string? input)
        {
            if (!PathParser.TryParse(input, out var root, out var sub))
            {
                ShowToast("Invalid path", $"'{input}' is not a recognized registry path.", InfoBarSeverity.Warning);
                return;
            }
            var node = await ViewModel.ResolveAsync(root, sub);
            if (node is null)
            {
                ShowToast("Path not found", PathParser.Combine(root, sub), InfoBarSeverity.Warning);
                return;
            }
            await SelectInTreeAsync(node);
            await NavigateToAsync(node, recordHistory: true);
        }

        private async void OnRegistryShortcutClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not string path)
            {
                return;
            }

            await NavigateToPathAsync(path);
        }

        // ---- Search ----

        private async void OnSearchClick(object sender, RoutedEventArgs e) => await ShowSearchAsync();

        private async void OnFindAccelerator(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
        { e.Handled = true; await ShowSearchAsync(); }

        private void OnFindNextAccelerator(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
        { e.Handled = true; _ = FindNextAsync(); }

        private void OnFindNextClick(object sender, RoutedEventArgs e) => _ = FindNextAsync();

        private void OnRefreshAccelerator(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
        { e.Handled = true; OnRefreshClick(s, new RoutedEventArgs()); }

        private void OnBackAccelerator(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
        { e.Handled = true; OnBackClick(s, new RoutedEventArgs()); }

        private void OnForwardAccelerator(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
        { e.Handled = true; OnForwardClick(s, new RoutedEventArgs()); }

        private void OnUpAccelerator(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
        { e.Handled = true; OnUpClick(s, new RoutedEventArgs()); }

        private void OnFocusPathAccelerator(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
        { e.Handled = true; PathBox.Focus(FocusState.Programmatic); PathBox.SelectAll(); }

        private void OnRenameAccelerator(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
        { e.Handled = true; OnRenameClick(s, new RoutedEventArgs()); }

        private void OnUndoAccelerator(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
        { e.Handled = true; OnUndoClick(s, new RoutedEventArgs()); }

        private async void OnPaletteAccelerator(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
        { e.Handled = true; await ShowCommandPaletteAsync(); }

        private async Task ShowSearchAsync()
        {
            var current = _history.Current ?? ViewModel.Roots[0];
            var dlg = PrepareDialog(new SearchDialog(_search, current.Root, current.SubPath,
                prefilledQuery: _lastQuery, defaultRegex: _settings.RegexSearch || _lastUseRegex));
            var result = await dlg.ShowAsync();

            // Capture results for Find Next regardless of outcome
            if (dlg.LastOptions is not null)
            {
                _lastQuery = dlg.LastOptions.Query;
                _lastUseRegex = dlg.LastOptions.UseRegex;
                _lastHits = dlg.Results.ToList();
                _lastHitIndex = -1;
            }

            SearchHit? selected = dlg.SelectedHit;
            if (selected is null) return;

            // Set index to selected so that Find Next moves forward from there.
            _lastHitIndex = _lastHits.IndexOf(selected);
            await JumpToHitAsync(selected);
        }

        private async Task FindNextAsync()
        {
            if (_lastHits.Count == 0)
            {
                ShowToast("Find next", "No previous search. Press Ctrl+F to search.", InfoBarSeverity.Informational);
                return;
            }
            _lastHitIndex = (_lastHitIndex + 1) % _lastHits.Count;
            var hit = _lastHits[_lastHitIndex];
            await JumpToHitAsync(hit);
            ShowToast("Find next", $"{_lastHitIndex + 1} of {_lastHits.Count}: {hit.Display}", InfoBarSeverity.Informational);
        }

        private async Task JumpToHitAsync(SearchHit hit)
        {
            var node = await ViewModel.ResolveAsync(hit.Root, hit.SubPath);
            if (node is null) return;
            await SelectInTreeAsync(node);
            await NavigateToAsync(node, recordHistory: true);
        }

        private async Task SelectInTreeAsync(RegistryKeyNode target)
        {
            var rootNode = ViewModel.Roots.FirstOrDefault(r => r.Root == target.Root);
            if (rootNode is null) return;
            await ViewModel.EnsureChildrenLoadedAsync(rootNode);
            rootNode.IsExpanded = true;

            if (target.IsRoot)
            {
                KeysTree.SelectedItem = rootNode;
                return;
            }

            var current = rootNode;
            foreach (var seg in target.SubPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                await ViewModel.EnsureChildrenLoadedAsync(current);
                var next = current.Children.FirstOrDefault(c =>
                    string.Equals(c.Name, seg, StringComparison.OrdinalIgnoreCase));
                if (next is null) return;
                current.IsExpanded = true;
                current = next;
            }
            KeysTree.SelectedItem = current;
        }

        // ---- Filter ----

        private void OnValueFilterChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.ValueFilter = ValueFilterBox.Text;
            UpdateStatus(_history.Current);
        }

        private void OnClearFilterClick(object sender, RoutedEventArgs e)
        {
            ValueFilterBox.Text = string.Empty;
        }

        // ---- File menu handlers ----

        private async void OnExportClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current ?? ViewModel.Roots[0];
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeChoices.Add("Registration entries", new List<string> { ".reg" });
            picker.SuggestedFileName = SuggestedExportName(current);
            InitializeWithWindow(picker);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            try
            {
                await Task.Run(() => _exporter.Export(current.Root, current.SubPath, file.Path));
                ShowToast("Exported", $"{current.FullPath}  →  {file.Path}", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Export failed", ex.Message);
            }
        }

        private async void OnExportValueClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current ?? ViewModel.Roots[0];
            if (ValuesList.SelectedItem is not RegistryValueItem item)
            {
                await ShowMessageAsync("Export value", "Select a value to export.");
                return;
            }

            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeChoices.Add("Registration entries", new List<string> { ".reg" });
            picker.SuggestedFileName = SuggestedValueExportName(current, item);
            InitializeWithWindow(picker);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            try
            {
                await Task.Run(() => _exporter.ExportValue(current.Root, current.SubPath, item.Name, file.Path));
                ShowToast("Value exported", $"{item.DisplayName}  →  {file.Path}", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Export value failed", ex.Message);
            }
        }

        private async void OnExportJsonClick(object sender, RoutedEventArgs e) =>
            await ExportFlatAsync("JSON", new List<string> { ".json" }, "application/json", _jsonCsv.ExportJson);

        private async void OnExportCsvClick(object sender, RoutedEventArgs e) =>
            await ExportFlatAsync("CSV", new List<string> { ".csv" }, "text/csv", _jsonCsv.ExportCsv);

        private async Task ExportFlatAsync(string label, List<string> exts, string mime,
            Action<RegistryRoot, string, string> exportFn)
        {
            var current = _history.Current ?? ViewModel.Roots[0];
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeChoices.Add(label, exts);
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(SuggestedExportName(current));
            InitializeWithWindow(picker);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            try
            {
                await Task.Run(() => exportFn(current.Root, current.SubPath, file.Path));
                ShowToast($"Exported to {label}", file.Path, InfoBarSeverity.Success);
            }
            catch (Exception ex) { await ShowMessageAsync($"{label} export failed", ex.Message); }
        }

        private async void OnImportClick(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeFilter.Add(".reg");
            InitializeWithWindow(picker);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            await PreviewThenImportAsync(file.Path);
        }

        private async void OnImportValueClick(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeFilter.Add(".reg");
            InitializeWithWindow(picker);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            await PreviewThenImportSingleValueAsync(file.Path);
        }

        private async Task PreviewThenImportSingleValueAsync(string path)
        {
            var current = _history.Current ?? ViewModel.Roots[0];
            RegFileEntry.SetValue value;
            try
            {
                var entries = await Task.Run(() => _importer.Parse(path).ToList());
                var values = entries.OfType<RegFileEntry.SetValue>().ToList();
                var unsupported = entries.Any(e => e is RegFileEntry.DeleteKey or RegFileEntry.DeleteValue);
                if (unsupported || values.Count != 1)
                {
                    var reason = unsupported
                        ? "The file contains delete entries. Choose a .reg file with exactly one value assignment."
                        : $"The file must contain exactly one value assignment; it contains {values.Count}.";
                    await ShowMessageAsync("Cannot import single value", reason);
                    return;
                }
                value = values[0];
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Cannot read .reg", ex.Message);
                return;
            }

            var sourcePath = PathParser.Combine(value.Root, value.SubPath);
            var valueDisplayName = string.IsNullOrEmpty(value.Name) ? "(Default)" : value.Name;
            var confirm = PrepareDialog(new ContentDialog
            {
                Title = "Import single value",
                Content = $"Import '{valueDisplayName}' from\n{sourcePath}\n\ninto\n{current.FullPath}?",
                PrimaryButtonText = "Import",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            });
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            RegImportResult result;
            try { result = await Task.Run(() => _importer.ImportSingleValue(path, current.Root, current.SubPath)); }
            catch (Exception ex) { await ShowMessageAsync("Import value failed", ex.Message); return; }

            await ViewModel.LoadValuesAsync(current);
            UpdateStatus(current);

            if (result.Errors.Count > 0)
                await ShowMessageAsync("Import value failed", string.Join("\n", result.Errors.Take(20)));
            else
                ShowToast("Value imported", $"{valueDisplayName} → {current.FullPath}", InfoBarSeverity.Success);
        }

        private async Task PreviewThenImportAsync(string path)
        {
            IReadOnlyList<DiffEntry>? diff;
            try { diff = await Task.Run(() => _diff.Compute(path)); }
            catch (Exception ex)
            {
                await ShowMessageAsync("Cannot preview .reg", ex.Message);
                return;
            }

            var preview = PrepareDialog(new DiffPreviewDialog(diff!));
            preview.PrimaryButtonText = "Import";
            preview.CloseButtonText = "Cancel";
            preview.DefaultButton = ContentDialogButton.Primary;
            preview.Title = $"Preview import — {Path.GetFileName(path)}";
            if (await preview.ShowAsync() != ContentDialogResult.Primary) return;

            RegImportResult? result;
            try { result = await Task.Run(() => _importer.Import(path)); }
            catch (Exception ex) { await ShowMessageAsync("Import failed", ex.Message); return; }

            // Refresh current view
            var cur = _history.Current;
            if (cur is not null)
            {
                cur.ChildrenLoaded = false;
                cur.Children.Clear();
                cur.AddPlaceholder();
                await ViewModel.EnsureChildrenLoadedAsync(cur);
                await NavigateToAsync(cur, recordHistory: false);
            }

            var summary =
                $"Keys created/updated: {result.KeysCreated}\n" +
                $"Keys deleted:         {result.KeysDeleted}\n" +
                $"Values written:       {result.ValuesWritten}\n" +
                $"Values deleted:       {result.ValuesDeleted}";
            if (result.Errors.Count > 0)
                summary += "\n\nErrors:\n" + string.Join("\n", result.Errors.Take(20));

            await ShowMessageAsync("Import complete", summary);
        }

        private async void OnBackupClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current ?? ViewModel.Roots[0];
            try
            {
                var path = await Task.Run(() => _backup.CreateSnapshot(current.Root, current.SubPath));
                ShowToast("Backup saved", path, InfoBarSeverity.Success);
            }
            catch (Exception ex) { await ShowMessageAsync("Backup failed", ex.Message); }
        }

        private async void OnManageBackupsClick(object sender, RoutedEventArgs e)
        {
            var dlg = PrepareDialog(new SnapshotManagerDialog(_snapshots));
            await dlg.ShowAsync();
            if (dlg.Result is not null)
            {
                ShowToast("Snapshot restored",
                    $"{dlg.Result.KeysCreated} key(s), {dlg.Result.ValuesWritten} value(s) written.",
                    InfoBarSeverity.Success);
                var cur = _history.Current;
                if (cur is not null)
                {
                    cur.ChildrenLoaded = false; cur.Children.Clear(); cur.AddPlaceholder();
                    await ViewModel.EnsureChildrenLoadedAsync(cur);
                    await NavigateToAsync(cur, recordHistory: false);
                }
            }
        }

        private async void OnOpenBackupsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_backup.BackupDirectory);
                await Launcher.LaunchFolderPathAsync(_backup.BackupDirectory);
            }
            catch (Exception ex) { await ShowMessageAsync("Cannot open folder", ex.Message); }
        }

        // ---- Hive Save/Load ----

        private async void OnSaveHiveClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null || current.IsRoot)
            {
                await ShowMessageAsync("Save hive", "Select a non-root subkey to save.");
                return;
            }
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeChoices.Add("Registry hive", new List<string> { ".hiv" });
            picker.SuggestedFileName = SuggestedExportName(current).Replace(".reg", ".hiv");
            InitializeWithWindow(picker);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            try
            {
                if (File.Exists(file.Path)) File.Delete(file.Path); // RegSaveKeyEx fails if exists
                await Task.Run(() => _hive.SaveHive(current.Root, current.SubPath, file.Path));
                ShowToast("Hive saved", file.Path, InfoBarSeverity.Success);
            }
            catch (Exception ex) { await ShowMessageAsync("Save hive failed", ex.Message); }
        }

        private async void OnLoadHiveClick(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeFilter.Add(".hiv");
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow(picker);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var nameInput = PrepareDialog(new RenameDialog("Load hive — pick mount key name", "MyLoadedHive"));
            if (await nameInput.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                await Task.Run(() => _hive.LoadHive(RegistryRoot.Users, nameInput.NewName, file.Path));
                ShowToast("Hive loaded", $"Mounted as HKU\\{nameInput.NewName}", InfoBarSeverity.Success);
                var hkuRoot = ViewModel.Roots.FirstOrDefault(r => r.Root == RegistryRoot.Users);
                if (hkuRoot is not null) { hkuRoot.ChildrenLoaded = false; hkuRoot.Children.Clear(); hkuRoot.AddPlaceholder(); await ViewModel.EnsureChildrenLoadedAsync(hkuRoot); }
            }
            catch (Exception ex) { await ShowMessageAsync("Load hive failed", ex.Message); }
        }

        private async void OnUnloadHiveClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null || current.IsRoot || current.Root != RegistryRoot.Users || current.SubPath.Contains('\\'))
            {
                await ShowMessageAsync("Unload hive", "Select a mounted top-level key under HKU first (e.g. HKU\\MyLoadedHive).");
                return;
            }
            var confirm = PrepareDialog(new ContentDialog
            {
                Title = "Unload hive",
                Content = $"Unload HKU\\{current.SubPath}? Any process holding handles to it may misbehave.",
                PrimaryButtonText = "Unload",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            });
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                await Task.Run(() => _hive.UnloadHive(RegistryRoot.Users, current.SubPath));
                ShowToast("Hive unloaded", current.SubPath, InfoBarSeverity.Success);
                var hkuRoot = ViewModel.Roots.FirstOrDefault(r => r.Root == RegistryRoot.Users);
                if (hkuRoot is not null) { hkuRoot.ChildrenLoaded = false; hkuRoot.Children.Clear(); hkuRoot.AddPlaceholder(); await ViewModel.EnsureChildrenLoadedAsync(hkuRoot); }
            }
            catch (Exception ex) { await ShowMessageAsync("Unload hive failed", ex.Message); }
        }

        // ---- View menu ----

        private void OnViewModeClick(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleMenuFlyoutItem item || item.Tag is not string tag) return;
            var newView = tag switch
            {
                "Registry32" => RegView.Registry32,
                "Registry64" => RegView.Registry64,
                _ => RegView.Default,
            };
            if (newView == _settings.View) { ApplySettings(initial: true); return; }
            _settings.View = newView;
            _settingsService.Save(_settings);
            ApplySettings(initial: false);
        }

        // ---- Tools / Settings / Palette ----

        private async void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var dlg = PrepareDialog(new SettingsDialog(_settings)
            {
                OwnerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this),
            });
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            var oldView = _settings.View;
            var oldSnapDir = _settings.SnapshotDirectory;
            _settings = dlg.Result;
            _settingsService.Save(_settings);
            if (!string.Equals(oldSnapDir ?? string.Empty, _settings.SnapshotDirectory ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                // Recreate the backup pipeline so future snapshots land in the new directory.
                _backup = new BackupService(_exporter, _settings.SnapshotDirectory);
                _snapshots = new SnapshotManager(_backup, _importer);
            }
            ApplySettings(initial: oldView == _settings.View);
        }

        private async void OnPaletteClick(object sender, RoutedEventArgs e) => await ShowCommandPaletteAsync();

        private async Task ShowCommandPaletteAsync()
        {
            var commands = BuildPaletteCommands();
            var dlg = PrepareDialog(new CommandPaletteDialog(commands));
            await dlg.ShowAsync();
        }

        private IEnumerable<CommandPaletteDialog.PaletteCommand> BuildPaletteCommands()
        {
            yield return new("Find…", "Search the registry (Ctrl+F)", () => _ = ShowSearchAsync());
            yield return new("Find next", "Jump to next search hit (F3)", () => _ = FindNextAsync());
            yield return new("Refresh", "Reload current key (F5)", () => OnRefreshClick(this, new RoutedEventArgs()));
            yield return new("Up one level", "Go to parent key (Alt+Up)", () => OnUpClick(this, new RoutedEventArgs()));
            yield return new("Back", "Navigate back (Alt+Left)", () => OnBackClick(this, new RoutedEventArgs()));
            yield return new("Forward", "Navigate forward (Alt+Right)", () => OnForwardClick(this, new RoutedEventArgs()));
            yield return new("Copy current path", "Copy registry path to clipboard", () => OnCopyPathClick(this, new RoutedEventArgs()));
            yield return new("New subkey…", null, () => OnNewKeyClick(this, new RoutedEventArgs()));
            yield return new("New value…", null, () => OnNewValueClick(this, new RoutedEventArgs()));
            yield return new("Rename selected", "F2", () => OnRenameClick(this, new RoutedEventArgs()));
            yield return new("Delete current key", null, () => OnDeleteKeyClick(this, new RoutedEventArgs()));
            yield return new("Undo last change", "Ctrl+Z", () => OnUndoClick(this, new RoutedEventArgs()));
            yield return new("Backup current key", null, () => OnBackupClick(this, new RoutedEventArgs()));
            yield return new("Manage backups…", null, () => OnManageBackupsClick(this, new RoutedEventArgs()));
            yield return new("Import .reg…", null, () => OnImportClick(this, new RoutedEventArgs()));
            yield return new("Import single value into current key…", null, () => OnImportValueClick(this, new RoutedEventArgs()));
            yield return new("Export current key as .reg…", null, () => OnExportClick(this, new RoutedEventArgs()));
            yield return new("Export selected value as .reg…", null, () => OnExportValueClick(this, new RoutedEventArgs()));
            yield return new("Export current key as .json…", null, () => OnExportJsonClick(this, new RoutedEventArgs()));
            yield return new("Export current key as .csv…", null, () => OnExportCsvClick(this, new RoutedEventArgs()));
            yield return new("Add to favorites…", null, () => OnAddFavoriteClick(this, new RoutedEventArgs()));
            yield return new("Manage favorites…", null, () => OnManageFavoritesClick(this, new RoutedEventArgs()));
            yield return new("Settings…", null, () => OnSettingsClick(this, new RoutedEventArgs()));
            yield return new("Open backups folder", null, () => OnOpenBackupsClick(this, new RoutedEventArgs()));
            if (_isElevated)
            {
                yield return new("Save hive…", "Admin only", () => OnSaveHiveClick(this, new RoutedEventArgs()));
                yield return new("Load hive…", "Admin only", () => OnLoadHiveClick(this, new RoutedEventArgs()));
                yield return new("Unload selected hive", "Admin only", () => OnUnloadHiveClick(this, new RoutedEventArgs()));
            }
        }

        // ---- Favorites & Recent menus (regenerate on changes) ----

        private void HookFavoritesMenu()
        {
            RefreshFavoritesMenu();
        }

        private void RefreshFavoritesMenu()
        {
            // Items[0..2] are static (Add / Manage / Separator). Trim the rest, repopulate.
            for (int i = FavoritesMenuBar.Items.Count - 1; i >= 3; i--)
                FavoritesMenuBar.Items.RemoveAt(i);
            foreach (var fav in _favorites.Items)
            {
                var item = new MenuFlyoutItem { Text = fav.Name };
                var captured = fav;
                item.Click += async (_, _) =>
                {
                    var node = await ViewModel.ResolveAsync(captured.Root, captured.SubPath);
                    if (node is null)
                    { ShowToast("Favorite", "Path no longer exists.", InfoBarSeverity.Warning); return; }
                    await SelectInTreeAsync(node); await NavigateToAsync(node, recordHistory: true);
                };
                FavoritesMenuBar.Items.Add(item);
            }
        }

        private void HookRecentMenu()
        {
            RefreshRecentMenu();
        }

        private void RefreshRecentMenu()
        {
            RecentMenu.Items.Clear();
            var entries = _recent.GetAll();
            if (entries.Count == 0)
            {
                RecentMenu.Items.Add(new MenuFlyoutItem { Text = "(empty)", IsEnabled = false });
                return;
            }
            foreach (var entry in entries.Take(_settings.RecentLocationsLimit))
            {
                var label = string.IsNullOrEmpty(entry.SubPath) ? entry.Root.FullName() : $"{entry.Root.FullName()}\\{entry.SubPath}";
                var item = new MenuFlyoutItem { Text = label };
                var captured = entry;
                item.Click += async (_, _) =>
                {
                    var node = await ViewModel.ResolveAsync(captured.Root, captured.SubPath);
                    if (node is null) { ShowToast("Recent", "Path no longer exists.", InfoBarSeverity.Warning); return; }
                    await SelectInTreeAsync(node); await NavigateToAsync(node, recordHistory: true);
                };
                RecentMenu.Items.Add(item);
            }
            RecentMenu.Items.Add(new MenuFlyoutSeparator());
            var clear = new MenuFlyoutItem { Text = "Clear recent" };
            clear.Click += (_, _) => { _recent.Clear(); RefreshRecentMenu(); };
            RecentMenu.Items.Add(clear);
        }

        private async void OnAddFavoriteClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null) return;
            var defaultName = current.IsRoot ? current.Root.ShortName() : current.Name;
            var dlg = PrepareDialog(new RenameDialog("Add favorite", defaultName));
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            _favorites.Add(new Favorite { Name = dlg.NewName, Root = current.Root, SubPath = current.SubPath });
            RefreshFavoritesMenu();
            ShowToast("Favorite added", dlg.NewName, InfoBarSeverity.Success);
        }

        private async void OnManageFavoritesClick(object sender, RoutedEventArgs e)
        {
            var dlg = PrepareDialog(new FavoritesManagerDialog(_favorites));
            await dlg.ShowAsync();
            RefreshFavoritesMenu();
        }

        // ---- Drag & drop ----

        private void OnRootDragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Import .reg";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsGlyphVisible = true;
            }
        }

        private async void OnRootDrop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            var regFile = items.OfType<StorageFile>()
                .FirstOrDefault(f => string.Equals(f.FileType, ".reg", StringComparison.OrdinalIgnoreCase));
            if (regFile is null)
            {
                ShowToast("Drop ignored", "Drop a .reg file to import it.", InfoBarSeverity.Warning);
                return;
            }
            await PreviewThenImportAsync(regFile.Path);
        }

        // ---- Editing handlers ----

        private void OnExitClick(object sender, RoutedEventArgs e) => Close();

        private void OnCopyPathClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null) return;
            var pkg = new DataPackage();
            pkg.SetText(current.FullPath);
            Clipboard.SetContent(pkg);
            ShowToast("Copied", current.FullPath, InfoBarSeverity.Informational);
        }

        private static string SuggestedExportName(RegistryKeyNode node)
        {
            var name = node.IsRoot ? node.Root.ShortName() : node.Name;
            var safe = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return $"{safe}.reg";
        }

        private static string SuggestedValueExportName(RegistryKeyNode node, RegistryValueItem item)
        {
            var name = item.IsDefault ? "Default" : item.Name;
            var safe = new System.Text.StringBuilder(node.Name.Length + name.Length + 1);
            foreach (var c in $"{node.Name}_{name}")
                safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return $"{safe}.reg";
        }

        private void InitializeWithWindow(object target)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(target, hwnd);
        }

        private T PrepareDialog<T>(T dialog) where T : ContentDialog
        {
            dialog.XamlRoot = Content.XamlRoot;
            dialog.RequestedTheme = Content is FrameworkElement fe ? fe.ActualTheme : ElementTheme.Default;
            return dialog;
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dlg = PrepareDialog(new ContentDialog
            {
                Title = title,
                Content = new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, MaxHeight = 360 },
                CloseButtonText = "OK",
            });
            await dlg.ShowAsync();
        }

        private void ShowToast(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        {
            Toast.Title = title;
            Toast.Message = message;
            Toast.Severity = severity;
            Toast.IsOpen = true;
        }

        // ---- Key edit handlers ----

        private async void OnNewKeyClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null) return;

            var dlg = PrepareDialog(new RenameDialog("New subkey", "NewKey"));
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            var name = dlg.NewName;
            try
            {
                _edit.CreateSubKey(current.Root, current.SubPath, name);
                var newSub = string.IsNullOrEmpty(current.SubPath) ? name : $"{current.SubPath}\\{name}";
                _undo.Push(new DeleteKeyOp(current.Root, newSub));
                UpdateUndoState();
                current.ChildrenLoaded = false;
                current.Children.Clear();
                current.AddPlaceholder();
                await ViewModel.EnsureChildrenLoadedAsync(current);
                current.IsExpanded = true;
                ShowToast("Created", $"{current.FullPath}\\{name}", InfoBarSeverity.Success);
            }
            catch (Exception ex) { await ShowMessageAsync("Create key failed", ex.Message); }
        }

        private async void OnDeleteKeyClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null || current.IsRoot)
            {
                await ShowMessageAsync("Cannot delete", "You cannot delete a root hive.");
                return;
            }

            if (_settings.ConfirmDestructive)
            {
                var confirm = PrepareDialog(new ContentDialog
                {
                    Title = "Delete key",
                    Content = $"Permanently delete this key and all its subkeys?\n\n{current.FullPath}\n\nA snapshot will be saved to the backups folder first.",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                });
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            }

            try
            {
                var snapshotPath = _backup.CreateSnapshot(current.Root, current.SubPath, "before-delete");
                _edit.DeleteSubKey(current.Root, current.SubPath);
                _undo.Push(new RestoreKeyOp(current.Root, current.SubPath, snapshotPath));
                UpdateUndoState();

                var idx = current.SubPath.LastIndexOf('\\');
                var parentSub = idx < 0 ? string.Empty : current.SubPath[..idx];
                var parent = await ViewModel.ResolveAsync(current.Root, parentSub);
                if (parent is not null)
                {
                    parent.ChildrenLoaded = false;
                    parent.Children.Clear();
                    parent.AddPlaceholder();
                    await ViewModel.EnsureChildrenLoadedAsync(parent);
                    await SelectInTreeAsync(parent);
                    await NavigateToAsync(parent, recordHistory: true);
                }
                ShowToast("Deleted", current.FullPath, InfoBarSeverity.Success);
            }
            catch (Exception ex) { await ShowMessageAsync("Delete key failed", ex.Message); }
        }

        private async void OnRenameClick(object sender, RoutedEventArgs e)
        {
            // Context-aware: if a value is selected, rename that; otherwise rename the current key.
            if (ValuesList.SelectedItem is RegistryValueItem) { await RenameValueAsync(); return; }
            await RenameKeyAsync();
        }

        private async void OnRenameKeyClick(object sender, RoutedEventArgs e) => await RenameKeyAsync();
        private async void OnRenameValueClick(object sender, RoutedEventArgs e) => await RenameValueAsync();

        private async Task RenameKeyAsync()
        {
            var current = _history.Current;
            if (current is null || current.IsRoot)
            {
                await ShowMessageAsync("Rename", "Select a non-root subkey to rename.");
                return;
            }
            var dlg = PrepareDialog(new RenameDialog("Rename key", current.Name));
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                _edit.RenameSubKey(current.Root, current.SubPath, dlg.NewName);
                // Refresh parent
                var idx = current.SubPath.LastIndexOf('\\');
                var parentSub = idx < 0 ? string.Empty : current.SubPath[..idx];
                var parent = await ViewModel.ResolveAsync(current.Root, parentSub);
                if (parent is not null)
                {
                    parent.ChildrenLoaded = false; parent.Children.Clear(); parent.AddPlaceholder();
                    await ViewModel.EnsureChildrenLoadedAsync(parent);
                    var newSub = string.IsNullOrEmpty(parentSub) ? dlg.NewName : $"{parentSub}\\{dlg.NewName}";
                    var newNode = await ViewModel.ResolveAsync(current.Root, newSub);
                    if (newNode is not null) { await SelectInTreeAsync(newNode); await NavigateToAsync(newNode, recordHistory: true); }
                }
                _history.Clear();
                BackButton.IsEnabled = false; ForwardButton.IsEnabled = false;
                ShowToast("Renamed", $"→ {dlg.NewName}", InfoBarSeverity.Success);
            }
            catch (Exception ex) { await ShowMessageAsync("Rename key failed", ex.Message); }
        }

        private async Task RenameValueAsync()
        {
            var current = _history.Current;
            var item = ValuesList.SelectedItem as RegistryValueItem;
            if (current is null || item is null || item.IsDefault)
            {
                await ShowMessageAsync("Rename value", "Select a non-default value to rename.");
                return;
            }
            var dlg = PrepareDialog(new RenameDialog("Rename value", item.Name));
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            try
            {
                _edit.RenameValue(current.Root, current.SubPath, item.Name, dlg.NewName);
                // Undo: rename back (capture the old data via RestoreValueOp for the new name → delete)
                _undo.Push(new DeleteValueOp(current.Root, current.SubPath, dlg.NewName));
                if (item.RawData is not null)
                    _undo.Push(new RestoreValueOp(current.Root, current.SubPath, item.Name,
                        item.Kind, item.RawData, Existed: true));
                UpdateUndoState();
                await ViewModel.LoadValuesAsync(current);
                UpdateStatus(current);
                ShowToast("Renamed value", $"{item.Name} → {dlg.NewName}", InfoBarSeverity.Success);
            }
            catch (Exception ex) { await ShowMessageAsync("Rename value failed", ex.Message); }
        }

        // ---- Value edit handlers ----

        private void OnValueDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
            => OnModifyValueClick(sender, new RoutedEventArgs());

        private async void OnNewValueClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null) return;

            var dlg = ValueEditorDialog.ForCreate();
            PrepareDialog(dlg);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                var (existed, oldKind, oldData) = CaptureValueState(current.Root, current.SubPath, dlg.ValueName);
                _edit.SetValue(current.Root, current.SubPath, dlg.ValueName, dlg.Kind, dlg.Data!);
                _undo.Push(existed
                    ? new RestoreValueOp(current.Root, current.SubPath, dlg.ValueName, oldKind, oldData, Existed: true)
                    : new DeleteValueOp(current.Root, current.SubPath, dlg.ValueName));
                UpdateUndoState();
                await ViewModel.LoadValuesAsync(current);
                UpdateStatus(current);
            }
            catch (Exception ex) { await ShowMessageAsync("Create value failed", ex.Message); }
        }

        private async void OnModifyValueClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            var item = ValuesList.SelectedItem as RegistryValueItem;
            if (current is null || item is null) return;

            if (item.IsReadOnly)
            {
                ShowToast("Read-only value",
                    $"{item.KindDisplay} values are displayed in read-only mode and cannot be edited.",
                    InfoBarSeverity.Informational);
                return;
            }

            var dlg = ValueEditorDialog.ForEdit(item);
            PrepareDialog(dlg);
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                var (existed, oldKind, oldData) = CaptureValueState(current.Root, current.SubPath, dlg.ValueName);
                _edit.SetValue(current.Root, current.SubPath, dlg.ValueName, dlg.Kind, dlg.Data!);
                _undo.Push(existed
                    ? new RestoreValueOp(current.Root, current.SubPath, dlg.ValueName, oldKind, oldData, Existed: true)
                    : new DeleteValueOp(current.Root, current.SubPath, dlg.ValueName));
                UpdateUndoState();
                await ViewModel.LoadValuesAsync(current);
                UpdateStatus(current);
            }
            catch (Exception ex) { await ShowMessageAsync("Modify failed", ex.Message); }
        }

        private async void OnDeleteValueClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            var item = ValuesList.SelectedItem as RegistryValueItem;
            if (current is null || item is null) return;

            if (_settings.ConfirmDestructive)
            {
                var confirm = PrepareDialog(new ContentDialog
                {
                    Title = "Delete value",
                    Content = $"Delete the value '{item.DisplayName}' from\n{current.FullPath}?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                });
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            }

            try
            {
                _backup.CreateSnapshot(current.Root, current.SubPath, "before-delete-value");
                var (existed, oldKind, oldData) = CaptureValueState(current.Root, current.SubPath, item.Name);
                _edit.DeleteValue(current.Root, current.SubPath, item.Name);
                if (existed)
                {
                    _undo.Push(new RestoreValueOp(current.Root, current.SubPath, item.Name, oldKind, oldData, Existed: true));
                    UpdateUndoState();
                }
                await ViewModel.LoadValuesAsync(current);
                UpdateStatus(current);
            }
            catch (Exception ex) { await ShowMessageAsync("Delete value failed", ex.Message); }
        }

        private void OnCopyValueNameClick(object sender, RoutedEventArgs e)
        {
            if (ValuesList.SelectedItem is not RegistryValueItem item) return;
            var pkg = new DataPackage();
            pkg.SetText(item.DisplayName);
            Clipboard.SetContent(pkg);
            ShowToast("Copied", item.DisplayName, InfoBarSeverity.Informational);
        }

        // ---- Undo ----

        private async void OnUndoClick(object sender, RoutedEventArgs e)
        {
            if (!_undo.CanUndo) return;
            try
            {
                _undo.Undo(_edit, _importer);
                UpdateUndoState();
                var cur = _history.Current;
                if (cur is not null)
                {
                    cur.ChildrenLoaded = false; cur.Children.Clear(); cur.AddPlaceholder();
                    await ViewModel.EnsureChildrenLoadedAsync(cur);
                    await NavigateToAsync(cur, recordHistory: false);
                }
                ShowToast("Undone", "Last change was reverted.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                _ = ShowMessageAsync("Undo failed", ex.Message);
            }
        }

        private void UpdateUndoState()
        {
            UndoMenuItem.IsEnabled = _undo.CanUndo;
        }

        private (bool existed, RegistryValueKind kind, object? data) CaptureValueState(
            RegistryRoot root, string subPath, string name)
        {
            using var key = ViewModel.Registry.OpenKey(root, subPath);
            if (key is null) return (false, RegistryValueKind.Unknown, null);
            try
            {
                var names = key.GetValueNames();
                if (!Array.Exists(names, n => string.Equals(n, name, StringComparison.Ordinal)))
                    return (false, RegistryValueKind.Unknown, null);
                var kind = key.GetValueKind(name);
                var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return (true, kind, data);
            }
            catch { return (false, RegistryValueKind.Unknown, null); }
        }
    }
}
