using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        private readonly BackupService _backup;
        private readonly RegistryEditService _edit = new();
        private bool _navigatingFromHistory;

        public MainWindow()
        {
            InitializeComponent();
            Title = "FluentRegedit";
            _search = new RegistrySearchService(ViewModel.Registry);
            _exporter = new RegFileExporter(ViewModel.Registry);
            _backup = new BackupService(_exporter);
            UpdateStatus(null);
        }

        private void OnTreeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            if (args.Item is RegistryKeyNode node && !node.IsPlaceholder)
                ViewModel.EnsureChildrenLoaded(node);
        }

        private void OnTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is RegistryKeyNode node && !node.IsPlaceholder)
                NavigateTo(node, recordHistory: true);
        }

        private void OnTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            if (sender.SelectedItem is RegistryKeyNode node && !node.IsPlaceholder)
                NavigateTo(node, recordHistory: true);
        }

        private void NavigateTo(RegistryKeyNode node, bool recordHistory)
        {
            ViewModel.LoadValues(node);
            PathBox.Text = node.FullPath;
            UpdateStatus(node);

            if (recordHistory && !_navigatingFromHistory && _history.Current != node)
                _history.Visit(node);

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
            StatusCountText.Text = n == 1 ? "1 value" : $"{n} values";
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            var n = _history.Back();
            if (n is not null) NavigateFromHistory(n);
        }

        private void OnForwardClick(object sender, RoutedEventArgs e)
        {
            var n = _history.Forward();
            if (n is not null) NavigateFromHistory(n);
        }

        private void NavigateFromHistory(RegistryKeyNode node)
        {
            _navigatingFromHistory = true;
            try
            {
                var resolved = ViewModel.Resolve(node.Root, node.SubPath) ?? node;
                SelectInTree(resolved);
                NavigateTo(resolved, recordHistory: false);
            }
            finally { _navigatingFromHistory = false; }
        }

        private void OnUpClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null || current.IsRoot) return;
            var idx = current.SubPath.LastIndexOf('\\');
            var parentSub = idx < 0 ? string.Empty : current.SubPath[..idx];
            var parent = ViewModel.Resolve(current.Root, parentSub);
            if (parent is not null)
            {
                SelectInTree(parent);
                NavigateTo(parent, recordHistory: true);
            }
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null) return;
            current.ChildrenLoaded = false;
            current.Children.Clear();
            ViewModel.EnsureChildrenLoaded(current);
            NavigateTo(current, recordHistory: false);
        }

        private void OnPathBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            if (!PathParser.TryParse(PathBox.Text, out var root, out var sub))
            {
                StatusPathText.Text = $"Invalid path: {PathBox.Text}";
                return;
            }
            var node = ViewModel.Resolve(root, sub);
            if (node is null)
            {
                StatusPathText.Text = $"Path not found: {PathParser.Combine(root, sub)}";
                return;
            }
            SelectInTree(node);
            NavigateTo(node, recordHistory: true);
        }

        private async void OnSearchClick(object sender, RoutedEventArgs e) => await ShowSearchAsync();

        private async void OnFindAccelerator(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e)
        { e.Handled = true; await ShowSearchAsync(); }

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


        private async System.Threading.Tasks.Task ShowSearchAsync()
        {
            var current = _history.Current ?? ViewModel.Roots[0];
            var dlg = new SearchDialog(_search, current.Root, current.SubPath)
            {
                XamlRoot = Content.XamlRoot,
            };
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary && dlg.SelectedHit is { } hit)
            {
                var node = ViewModel.Resolve(hit.Root, hit.SubPath);
                if (node is not null)
                {
                    SelectInTree(node);
                    NavigateTo(node, recordHistory: true);
                }
            }
            else if (dlg.SelectedHit is { } hit2 && result == ContentDialogResult.None)
            {
                // Double-tapped a result -> dialog Hide() returns None.
                var node = ViewModel.Resolve(hit2.Root, hit2.SubPath);
                if (node is not null)
                {
                    SelectInTree(node);
                    NavigateTo(node, recordHistory: true);
                }
            }
        }

        private void SelectInTree(RegistryKeyNode target)
        {
            // Expand ancestors so the node materializes in the TreeView, then select.
            var rootNode = ViewModel.Roots.FirstOrDefault(r => r.Root == target.Root);
            if (rootNode is null) return;
            ViewModel.EnsureChildrenLoaded(rootNode);
            rootNode.IsExpanded = true;

            if (target.IsRoot)
            {
                KeysTree.SelectedItem = rootNode;
                return;
            }

            var current = rootNode;
            foreach (var seg in target.SubPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                ViewModel.EnsureChildrenLoaded(current);
                var next = current.Children.FirstOrDefault(c =>
                    string.Equals(c.Name, seg, StringComparison.OrdinalIgnoreCase));
                if (next is null) return;
                current.IsExpanded = true;
                current = next;
            }
            KeysTree.SelectedItem = current;
        }

        // ---- File menu handlers ----

        private async void OnExportClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current ?? ViewModel.Roots[0];
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeChoices.Add("Registration entries", new System.Collections.Generic.List<string> { ".reg" });
            picker.SuggestedFileName = SuggestedExportName(current);
            InitializeWithWindow(picker);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            try
            {
                await Task.Run(() => _exporter.Export(current.Root, current.SubPath, file.Path));
                StatusPathText.Text = $"Exported {current.FullPath}  →  {file.Path}";
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Export failed", ex.Message);
            }
        }

        private async void OnImportClick(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeFilter.Add(".reg");
            InitializeWithWindow(picker);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var confirm = new ContentDialog
            {
                Title = "Import .reg",
                Content = $"Apply the contents of\n{file.Path}\nto the registry?",
                PrimaryButtonText = "Import",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            RegImportResult? result = null;
            try
            {
                result = await Task.Run(() => _importer.Import(file.Path));
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Import failed", ex.Message);
                return;
            }

            // Refresh current view
            var cur = _history.Current;
            if (cur is not null)
            {
                cur.ChildrenLoaded = false;
                cur.Children.Clear();
                ViewModel.EnsureChildrenLoaded(cur);
                NavigateTo(cur, recordHistory: false);
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
                StatusPathText.Text = $"Backup saved: {path}";
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Backup failed", ex.Message);
            }
        }

        private async void OnOpenBackupsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_backup.BackupDirectory);
                await Windows.System.Launcher.LaunchFolderPathAsync(_backup.BackupDirectory);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Cannot open folder", ex.Message);
            }
        }

        private void OnExitClick(object sender, RoutedEventArgs e) => Close();

        private void OnCopyPathClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null) return;
            var pkg = new DataPackage();
            pkg.SetText(current.FullPath);
            Clipboard.SetContent(pkg);
            StatusPathText.Text = $"Copied: {current.FullPath}";
        }

        private static string SuggestedExportName(RegistryKeyNode node)
        {
            var name = node.IsRoot ? node.Root.ShortName() : node.Name;
            var safe = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return $"{safe}.reg";
        }

        private void InitializeWithWindow(object target)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(target, hwnd);
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dlg = new ContentDialog
            {
                Title = title,
                Content = new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, MaxHeight = 360 },
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
            };
            await dlg.ShowAsync();
        }

        // ---- Editing handlers ----

        private async void OnNewKeyClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null) return;

            var input = new TextBox { PlaceholderText = "Subkey name" };
            var dlg = new ContentDialog
            {
                Title = "New subkey",
                Content = input,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            var name = input.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            try
            {
                _edit.CreateSubKey(current.Root, current.SubPath, name);
                current.ChildrenLoaded = false;
                current.Children.Clear();
                ViewModel.EnsureChildrenLoaded(current);
                current.IsExpanded = true;
                StatusPathText.Text = $"Created {current.FullPath}\\{name}";
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

            var confirm = new ContentDialog
            {
                Title = "Delete key",
                Content = $"Permanently delete this key and all its subkeys?\n\n{current.FullPath}\n\nA snapshot will be saved to the backups folder first.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                _backup.CreateSnapshot(current.Root, current.SubPath, "before-delete");
                _edit.DeleteSubKey(current.Root, current.SubPath);

                // Navigate to parent and refresh.
                var idx = current.SubPath.LastIndexOf('\\');
                var parentSub = idx < 0 ? string.Empty : current.SubPath[..idx];
                var parent = ViewModel.Resolve(current.Root, parentSub);
                if (parent is not null)
                {
                    parent.ChildrenLoaded = false;
                    parent.Children.Clear();
                    ViewModel.EnsureChildrenLoaded(parent);
                    SelectInTree(parent);
                    NavigateTo(parent, recordHistory: true);
                }
                StatusPathText.Text = $"Deleted {current.FullPath}";
            }
            catch (Exception ex) { await ShowMessageAsync("Delete key failed", ex.Message); }
        }

        private void OnValueDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
            => OnModifyValueClick(sender, new RoutedEventArgs());

        private async void OnNewValueClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            if (current is null) return;

            var dlg = ValueEditorDialog.ForCreate();
            dlg.XamlRoot = Content.XamlRoot;
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                _edit.SetValue(current.Root, current.SubPath, dlg.ValueName, dlg.Kind, dlg.Data!);
                ViewModel.LoadValues(current);
                UpdateStatus(current);
            }
            catch (Exception ex) { await ShowMessageAsync("Create value failed", ex.Message); }
        }

        private async void OnModifyValueClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            var item = ValuesList.SelectedItem as RegistryValueItem;
            if (current is null || item is null) return;

            var dlg = ValueEditorDialog.ForEdit(item);
            dlg.XamlRoot = Content.XamlRoot;
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                _edit.SetValue(current.Root, current.SubPath, dlg.ValueName, dlg.Kind, dlg.Data!);
                ViewModel.LoadValues(current);
                UpdateStatus(current);
            }
            catch (Exception ex) { await ShowMessageAsync("Modify failed", ex.Message); }
        }

        private async void OnDeleteValueClick(object sender, RoutedEventArgs e)
        {
            var current = _history.Current;
            var item = ValuesList.SelectedItem as RegistryValueItem;
            if (current is null || item is null) return;

            var confirm = new ContentDialog
            {
                Title = "Delete value",
                Content = $"Delete the value '{item.DisplayName}' from\n{current.FullPath}?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                _backup.CreateSnapshot(current.Root, current.SubPath, "before-delete-value");
                _edit.DeleteValue(current.Root, current.SubPath, item.Name);
                ViewModel.LoadValues(current);
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
            StatusPathText.Text = $"Copied: {item.DisplayName}";
        }
    }
}


