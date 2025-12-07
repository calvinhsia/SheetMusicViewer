using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace AvaloniaSimpleApp;

public class ListBoxBrowseControl : DockPanel
{
    public ListBoxBrowseView ListView { get; private set; }
    internal int[] _colWidths;
    public IEnumerable _query;

    public ListBoxBrowseControl(IEnumerable query, int[] colWidths = null)
    {
        try
        {
            _query = query;
            _colWidths = colWidths;
            
            this.LastChildFill = true;
            this.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.VerticalAlignment = VerticalAlignment.Stretch;
            
            var listFilter = new ListBoxListFilter(null);
            this.Children.Add(listFilter);
            DockPanel.SetDock(listFilter, Dock.Top);

            ListView = new ListBoxBrowseView(query, this);
            
            var listContainer = new DockPanel
            {
                LastChildFill = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            if (ListView.HeaderGrid != null)
            {
                listContainer.Children.Add(ListView.HeaderGrid);
                DockPanel.SetDock(ListView.HeaderGrid, Dock.Top);
            }
            
            listContainer.Children.Add(ListView);
            
            this.Children.Add(listContainer);
            
            listFilter.SetBrowseList(ListView);
            
            Trace.WriteLine($"ListBoxBrowseControl: Created with virtualization support");
        }
        catch (Exception ex)
        {
            this.Children.Add(new TextBlock { Text = ex.ToString() });
            Trace.WriteLine($"ListBoxBrowseControl: Exception: {ex}");
        }
    }
}

internal class ListBoxListFilter : DockPanel
{
    readonly Button _btnApply = new Button() { Content = "Apply" };
    readonly TextBox _txtFilter = new TextBox { Width = 200 };
    readonly TextBlock _txtStatus = new TextBlock();
    ListBoxBrowseView _browse;
    private static string _LastFilter;

    internal ListBoxListFilter(ListBoxBrowseView browse)
    {
        _browse = browse;
        BuildUI();
    }

    internal void SetBrowseList(ListBoxBrowseView browse)
    {
        _browse = browse;
        RefreshFilterStat();
    }

    private void BuildUI()
    {
        var spFilter = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 5,
            Height = 40
        };
        spFilter.Children.Add(_txtStatus);
        spFilter.Children.Add(new Label 
        { 
            Content = "StringFilter",
            [ToolTip.TipProperty] = "Case insensitive search (ListBox with virtualization)"
        });
        _txtFilter.Text = _LastFilter;
        _txtFilter.Watermark = "Enter filter text...";
        spFilter.Children.Add(_txtFilter);
        spFilter.Children.Add(_btnApply);
        _btnApply.Click += (oc, ec) => { On_BtnApply_Click(oc, ec); };
        this.Children.Add(spFilter);

        _txtFilter.KeyDown += (o, e) =>
        {
            if (e.Key == Key.Enter)
            {
                On_BtnApply_Click(o, e);
            }
        };
    }

    void On_BtnApply_Click(object o, RoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            var filtText = _txtFilter.Text?.Trim().ToLower() ?? string.Empty;
            _LastFilter = filtText;

            _browse?.ApplyFilter(filtText);
            RefreshFilterStat();
        }
        catch (Exception ex)
        {
            _txtStatus.Text = ex.ToString();
        }
    }

    void RefreshFilterStat()
    {
        if (_browse != null)
        {
            var filteredCount = _browse.GetFilteredCount();
            _txtStatus.Text = $"# items = {filteredCount:n0} ";
        }
    }
}

public class ListBoxBrowseView : UserControl
{
    private readonly int[] _colWidths;
    private readonly IEnumerable _originalQuery;
    private ObservableCollection<object> _allItems;
    private ObservableCollection<object> _filteredItems;
    private Grid _headerGrid;
    private ListBox _listBox;
    private List<ListBoxColumnInfo> _columns = new List<ListBoxColumnInfo>();
    private int _lastSortedColumnIndex = -1;
    private bool _lastSortAscending = true;

    public Grid HeaderGrid => _headerGrid;
    public IList SelectedItems => _listBox?.SelectedItems ?? new List<object>();
    public int SelectedIndex => _listBox?.SelectedIndex ?? -1;
    public object SelectedItem => _listBox?.SelectedItem;

    public ListBoxBrowseView(IEnumerable query, ListBoxBrowseControl browseControl)
    {
        this._colWidths = browseControl._colWidths;
        this._originalQuery = query;
        
        // Create collections
        _allItems = new ObservableCollection<object>();
        _filteredItems = new ObservableCollection<object>();
        
        foreach (var item in query)
        {
            _allItems.Add(item);
            _filteredItems.Add(item);
        }

        // Analyze query type to build column info
        var ienum = query.GetType().GetInterface(typeof(IEnumerable<>).FullName);
        var itemType = ienum.GetGenericArguments()[0];
        
        var members = itemType.GetProperties();
        int colIndex = 0;
        
        foreach (var prop in members)
        {
            if (prop.Name.StartsWith("_"))
                continue;

            int width = 0;
            if (_colWidths != null && colIndex < _colWidths.Length)
            {
                width = _colWidths[colIndex];
            }

            _columns.Add(new ListBoxColumnInfo
            {
                HeaderText = prop.Name,
                BindingPath = prop.Name,
                Width = width
            });
            
            colIndex++;
        }

        BuildVisualStructure();
        
        Trace.WriteLine($"ListBoxBrowseView: Created with {_columns.Count} columns, {_filteredItems.Count} items");
    }

    private void BuildVisualStructure()
    {
        // Create header grid
        _headerGrid = new Grid
        {
            Background = Brushes.LightGray,
            Height = 25,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(8, 8, 8, 0)
        };

        double minWidth = 0;
        foreach (var col in _columns)
        {
            minWidth += col.Width > 0 ? col.Width : 150;
        }
        _headerGrid.MinWidth = minWidth;

        foreach (var col in _columns)
        {
            var colDef = new ColumnDefinition();
            if (col.Width > 0)
            {
                colDef.Width = new GridLength(col.Width);
            }
            else
            {
                colDef.Width = new GridLength(1, GridUnitType.Star);
            }
            _headerGrid.ColumnDefinitions.Add(colDef);
        }

        for (int i = 0; i < _columns.Count; i++)
        {
            var col = _columns[i];
            var headerButton = new Button
            {
                Content = col.HeaderText,
                Background = Brushes.LightGray,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0, 0, 1, 1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(5, 2, 5, 2)
            };
            
            var columnIndex = i;
            headerButton.Click += (s, e) => OnHeaderClick(columnIndex);
            
            Grid.SetColumn(headerButton, i);
            _headerGrid.Children.Add(headerButton);
        }

        // Create ListBox with virtualization
        _listBox = new ListBox
        {
            ItemsSource = _filteredItems,
            SelectionMode = SelectionMode.Multiple,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.White
        };

        // Create and attach context menu
        var contextMenu = new ContextMenu();
        
        var copyMenuItem = new MenuItem { Header = "Copy" };
        copyMenuItem.Click += OnCopyClick;
        contextMenu.Items.Add(copyMenuItem);
        
        var exportCsvMenuItem = new MenuItem { Header = "Export to CSV" };
        exportCsvMenuItem.Click += OnExportCsvClick;
        contextMenu.Items.Add(exportCsvMenuItem);
        
        var exportTxtMenuItem = new MenuItem { Header = "Export to Notepad" };
        exportTxtMenuItem.Click += OnExportTxtClick;
        contextMenu.Items.Add(exportTxtMenuItem);
        
        _listBox.ContextMenu = contextMenu;
        Trace.WriteLine($"ListBoxBrowseView: Context menu created with {contextMenu.Items.Count} items");

        // Use Loaded event to customize containers after they're created
        _listBox.Loaded += OnListBoxLoaded;

        this.Content = _listBox;
        
        Trace.WriteLine($"ListBoxBrowseView: Visual structure created with ListBox virtualization");
    }

    private void OnListBoxLoaded(object? sender, RoutedEventArgs e)
    {
        Trace.WriteLine($"OnListBoxLoaded: ListBox loaded, customizing visible containers");
        
        try
        {
            var generator = _listBox.ItemContainerGenerator;
            if (generator == null)
            {
                Trace.WriteLine("  ERROR: ItemContainerGenerator is null");
                return;
            }

            Trace.WriteLine($"  Generator exists, ItemsSource has {_filteredItems.Count} items");

            var presenter = _listBox.Presenter;
            if (presenter?.Panel != null)
            {
                Trace.WriteLine($"  Panel type: {presenter.Panel.GetType().Name}");
                Trace.WriteLine($"  Panel children count: {presenter.Panel.Children.Count}");
            }

            int customizedCount = 0;
            for (int i = 0; i < _filteredItems.Count; i++)
            {
                var container = _listBox.ContainerFromIndex(i) as ListBoxItem;
                if (container != null)
                {
                    var item = _filteredItems[i];
                    var grid = CreateItemGrid(item);
                    container.Content = grid;
                    customizedCount++;
                }
            }
            
            Trace.WriteLine($"  ? Customized {customizedCount} visible containers");

            _listBox.EffectiveViewportChanged += OnEffectiveViewportChanged;
            Trace.WriteLine($"  ? Subscribed to EffectiveViewportChanged for dynamic customization");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ERROR in OnListBoxLoaded: {ex.Message}");
            Trace.WriteLine($"  Stack: {ex.StackTrace}");
        }
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        try
        {
            int customizedCount = 0;
            int alreadyCustomizedCount = 0;
            
            for (int i = 0; i < _filteredItems.Count; i++)
            {
                var container = _listBox.ContainerFromIndex(i) as ListBoxItem;
                if (container != null)
                {
                    // Check if the container content is NOT a Grid (meaning it's uncustomized)
                    // The default content is the item's ToString(), which is typically wrapped in ContentPresenter
                    bool needsCustomization = false;
                    
                    if (container.Content == null)
                    {
                        needsCustomization = true;
                    }
                    else if (container.Content is Grid grid)
                    {
                        // It's already a Grid - but verify it has the right structure
                        // Our grids should have ColumnDefinitions matching the header
                        if (grid.ColumnDefinitions.Count != _columns.Count)
                        {
                            needsCustomization = true;
                            Trace.WriteLine($"  Grid has wrong column count: {grid.ColumnDefinitions.Count} vs {_columns.Count}");
                        }
                        else
                        {
                            alreadyCustomizedCount++;
                        }
                    }
                    else
                    {
                        // Content is something else (ContentPresenter, TextBlock, etc. with ToString())
                        needsCustomization = true;
                        Trace.WriteLine($"  Container {i} has content type: {container.Content.GetType().Name}");
                    }
                    
                    if (needsCustomization)
                    {
                        var item = _filteredItems[i];
                        var newGrid = CreateItemGrid(item);
                        container.Content = newGrid;
                        customizedCount++;
                    }
                }
            }
            
            if (customizedCount > 0 || alreadyCustomizedCount > 0)
            {
                Trace.WriteLine($"OnEffectiveViewportChanged: Customized {customizedCount} new containers, {alreadyCustomizedCount} already OK");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ERROR in OnEffectiveViewportChanged: {ex.Message}");
            Trace.WriteLine($"  Stack: {ex.StackTrace}");
        }
    }

    private Control CreateItemGrid(object item)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 20,  // Reduced from 25 to 20 for tighter spacing
            Background = Brushes.Transparent
        };

        foreach (var colDef in _headerGrid.ColumnDefinitions)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = colDef.Width });
        }

        for (int i = 0; i < _columns.Count; i++)
        {
            var col = _columns[i];
            
            string cellText = string.Empty;
            try
            {
                var prop = TypeDescriptor.GetProperties(item)[col.BindingPath];
                if (prop != null)
                {
                    var value = prop.GetValue(item);
                    cellText = FormatValue(value);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error getting value for {col.BindingPath}: {ex.Message}");
            }
            
            var textBlock = new TextBlock
            {
                Text = cellText,
                Padding = new Thickness(5, 0, 5, 0),  // Reduced vertical padding from 2 to 0
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Grid.SetColumn(textBlock, i);
            grid.Children.Add(textBlock);
        }

        return grid;
    }

    private string FormatValue(object value)
    {
        if (value == null)
            return string.Empty;

        Type type = value.GetType();
        
        if (type == typeof(string))
        {
            var str = value.ToString().Trim();
            var ndx = str.IndexOfAny(new[] { '\r', '\n' });
            if (ndx >= 0)
            {
                return str.Substring(0, ndx);
            }
            if (str.Length > 1000)
            {
                return str.Substring(0, 1000);
            }
            return str;
        }
        else if (type == typeof(Int64))
        {
            return ((Int64)value).ToString("n0");
        }
        else if (type == typeof(double))
        {
            return ((double)value).ToString("n2");
        }
        
        return value.ToString();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var selectedItems = _listBox.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
            {
                Trace.WriteLine("OnCopyClick: No items selected");
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var item in selectedItems)
            {
                var props = TypeDescriptor.GetProperties(item);
                var values = new List<string>();
                foreach (PropertyDescriptor prop in props)
                {
                    var value = prop.GetValue(item);
                    values.Add(FormatValue(value));
                }
                sb.AppendLine(string.Join("\t", values));
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(sb.ToString());
                Trace.WriteLine($"OnCopyClick: Copied {selectedItems.Count} items to clipboard");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ERROR in OnCopyClick: {ex.Message}");
        }
    }

    private async void OnExportCsvClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
                return;

            var saveDialog = new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export to CSV",
                SuggestedFileName = "export.csv",
                DefaultExtension = "csv"
            };

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(saveDialog);
            if (file != null)
            {
                var sb = new System.Text.StringBuilder();
                
                var headers = _columns.Select(c => c.HeaderText).ToList();
                sb.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));
                
                foreach (var item in _filteredItems)
                {
                    var props = TypeDescriptor.GetProperties(item);
                    var values = new List<string>();
                    foreach (var col in _columns)
                    {
                        var prop = props[col.BindingPath];
                        var value = prop?.GetValue(item);
                        var formatted = FormatValue(value);
                        values.Add($"\"{formatted.Replace("\"", "\"\"")}\"");
                    }
                    sb.AppendLine(string.Join(",", values));
                }

                await using var stream = await file.OpenWriteAsync();
                await using var writer = new System.IO.StreamWriter(stream);
                await writer.WriteAsync(sb.ToString());
                
                Trace.WriteLine($"OnExportCsvClick: Exported {_filteredItems.Count} items to {file.Path}");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ERROR in OnExportCsvClick: {ex.Message}");
        }
    }

    private async void OnExportTxtClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Create temp file like the original WPF version
            var tmpFileName = System.IO.Path.GetTempFileName();
            var sb = new System.Text.StringBuilder();
            
            // Add header row
            var headers = _columns.Select(c => c.HeaderText).ToList();
            sb.AppendLine(string.Join("\t", headers));
            
            // Add data rows
            foreach (var item in _filteredItems)
            {
                var props = TypeDescriptor.GetProperties(item);
                var values = new List<string>();
                foreach (var col in _columns)
                {
                    var prop = props[col.BindingPath];
                    var value = prop?.GetValue(item);
                    values.Add(FormatValue(value));
                }
                sb.AppendLine(string.Join("\t", values));
            }

            // Write with Unicode encoding (like original)
            System.IO.File.WriteAllText(tmpFileName, sb.ToString(), new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: true));
            var filename = System.IO.Path.ChangeExtension(tmpFileName, "txt");
            System.IO.File.Move(tmpFileName, filename);
            
            Trace.WriteLine($"OnExportTxtClick: Exported {_filteredItems.Count} items to {filename}");
            
            // Use shell execute to open with default .txt handler (like original)
            try
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = filename;
                process.StartInfo.UseShellExecute = true;
                process.Start();
            }
            catch (Exception openEx)
            {
                Trace.WriteLine($"Could not open file: {openEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ERROR in OnExportTxtClick: {ex.Message}");
        }
    }

    private void OnHeaderClick(int columnIndex
)
    {
        Trace.WriteLine($"Header clicked: column {columnIndex}");
        
        if (columnIndex < 0 || columnIndex >= _columns.Count)
            return;

        var col = _columns[columnIndex];
        
        bool ascending = true;
        if (_lastSortedColumnIndex == columnIndex)
        {
            ascending = !_lastSortAscending;
        }
        
        _lastSortedColumnIndex = columnIndex;
        _lastSortAscending = ascending;

        UpdateHeaderSortIndicators(columnIndex, ascending);

        try
        {
            var sortedItems = ascending
                ? _filteredItems.OrderBy(item => GetPropertyValue(item, col.BindingPath)).ToList()
                : _filteredItems.OrderByDescending(item => GetPropertyValue(item, col.BindingPath)).ToList();

            _filteredItems.Clear();
            foreach (var item in sortedItems)
            {
                _filteredItems.Add(item);
            }
            
            Trace.WriteLine($"Sorted by {col.HeaderText} ({(ascending ? "ascending" : "descending")})");
            
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RecustomizeVisibleContainers();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error sorting: {ex.Message}");
        }
    }

    private void RecustomizeVisibleContainers()
    {
        try
        {
            Trace.WriteLine($"RecustomizeVisibleContainers: Re-customizing after collection change");
            
            int customizedCount = 0;
            for (int i = 0; i < _filteredItems.Count; i++)
            {
                var container = _listBox.ContainerFromIndex(i) as ListBoxItem;
                if (container != null)
                {
                    var item = _filteredItems[i];
                    var grid = CreateItemGrid(item);
                    container.Content = grid;
                    customizedCount++;
                }
            }
            
            Trace.WriteLine($"  ? Re-customized {customizedCount} visible containers after sort");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ERROR in RecustomizeVisibleContainers: {ex.Message}");
        }
    }

    public void ApplyFilter(string filterText)
    {
        _filteredItems.Clear();
        
        if (string.IsNullOrEmpty(filterText))
        {
            foreach (var item in _allItems)
            {
                _filteredItems.Add(item);
            }
        }
        else
        {
            foreach (var itm in _allItems)
            {
                var props = TypeDescriptor.GetProperties(itm);
                var matches = false;
                foreach (PropertyDescriptor prop in props)
                {
                    var str = prop.GetValue(itm) as string;
                    if (!string.IsNullOrEmpty(str) && str.ToLower().Contains(filterText))
                    {
                        matches = true;
                        break;
                    }
                }
                if (matches)
                {
                    _filteredItems.Add(itm);
                }
            }
        }
        
        Trace.WriteLine($"Filter applied: {_filteredItems.Count} items match '{filterText}'");
        
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RecustomizeVisibleContainers();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    public int GetFilteredCount()
    {
        return _filteredItems?.Count ?? 0;
    }

    private object GetPropertyValue(object item, string propertyPath)
    {
        try
        {
            var prop = TypeDescriptor.GetProperties(item)[propertyPath];
            if (prop != null)
            {
                return prop.GetValue(item) ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error getting property value: {ex.Message}");
        }
        return string.Empty;
    }

    private void UpdateHeaderSortIndicators(int sortedColumnIndex, bool ascending)
    {
        for (int i = 0; i < _headerGrid.Children.Count; i++)
        {
            if (_headerGrid.Children[i] is Button btn)
            {
                var col = _columns[i];
                if (i == sortedColumnIndex)
                {
                    btn.Content = $"{col.HeaderText} {(ascending ? "?" : "?")}";
                }
                else
                {
                    btn.Content = col.HeaderText;
                }
            }
        }
    }
}

internal class ListBoxColumnInfo
{
    public string HeaderText { get; set; }
    public string BindingPath { get; set; }
    public int Width { get; set; }
}
