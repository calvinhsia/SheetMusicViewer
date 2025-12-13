using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// A control that displays a filterable, sortable list with column headers.
/// Similar to WPF's BrowsePanel but using Avalonia's ListBox with virtualization.
/// </summary>
public class BrowseControl : DockPanel
{
    public ListBoxBrowseView ListView { get; private set; } = null!;
    internal int[]? _colWidths;
    public IEnumerable _query = null!;

    public BrowseControl(IEnumerable query, int[]? colWidths = null)
    {
        try
        {
            _query = query;
            _colWidths = colWidths;
            
            this.LastChildFill = true;
            this.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.VerticalAlignment = VerticalAlignment.Stretch;
            
            var listFilter = new ListBoxListFilter(null!);
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
            
            Trace.WriteLine($"BrowseControl: Created with virtualization and column resizing support");
        }
        catch (Exception ex)
        {
            this.Children.Add(new TextBlock { Text = ex.ToString() });
            Trace.WriteLine($"BrowseControl: Exception: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Adds a custom context menu item to the browse control
    /// </summary>
    /// <param name="itemName">The display name for the menu item</param>
    /// <param name="tooltip">Optional tooltip for the menu item</param>
    /// <param name="action">Action to execute with the currently selected items</param>
    public void AddContextMenuItem(string itemName, string tooltip, Action<IList<object>> action)
    {
        ListView?.AddContextMenuItem(itemName, tooltip, action);
    }
}

internal class ListBoxListFilter : DockPanel
{
    readonly Button _btnApply = new Button() { Content = "Apply" };
    readonly TextBox _txtFilter = new TextBox { Width = 200 };
    readonly TextBlock _txtStatus = new TextBlock();
    ListBoxBrowseView? _browse;
    private static string? _LastFilter;

    internal ListBoxListFilter(ListBoxBrowseView? browse)
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

    void On_BtnApply_Click(object? o, RoutedEventArgs e)
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
    private readonly int[]? _colWidths;
    private readonly IEnumerable _originalQuery;
    private ObservableCollection<object> _allItems = null!;
    private ObservableCollection<object> _filteredItems = null!;
    private Grid _headerGrid = null!;
    private ListBox _listBox = null!;
    private List<ListBoxColumnInfo> _columns = new List<ListBoxColumnInfo>();
    private int _lastSortedColumnIndex = -1;
    private bool _lastSortAscending = true;
#pragma warning disable CS0414 // Field is assigned but never used
    private bool _isResizing = false;
#pragma warning restore CS0414
    private const int DefaultColumnWidth = 120; // Default width when colWidths not provided

    public Grid HeaderGrid => _headerGrid;
    public IList SelectedItems => _listBox?.SelectedItems ?? new List<object>();
    public int SelectedIndex => _listBox?.SelectedIndex ?? -1;
    public object? SelectedItem => _listBox?.SelectedItem;

    public ListBoxBrowseView(IEnumerable query, BrowseControl browseControl)
    {
        this._colWidths = browseControl._colWidths;
        this._originalQuery = query;
        
        // Optimize: Materialize once and use constructor for batch initialization
        var itemsList = query.Cast<object>().ToList();
        _allItems = new ObservableCollection<object>(itemsList);
        _filteredItems = new ObservableCollection<object>(itemsList);

        // Analyze query type to build column info
        var ienum = query.GetType().GetInterface(typeof(IEnumerable<>).FullName!);
        var itemType = ienum!.GetGenericArguments()[0];
        
        var members = itemType.GetProperties();
        int colIndex = 0;
        
        foreach (var prop in members)
        {
            if (prop.Name.StartsWith("_"))
                continue;

            int width = DefaultColumnWidth; // Use default width
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
            Margin = new Thickness(0, 8, 0, 0)
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
        
        // Add an extra dummy column at the end to allow the last column to resize
        _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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
            
            // Add GridSplitter at the right edge of every column (including the last one)
            var splitter = new GridSplitter
            {
                Width = 3,
                Background = Brushes.Transparent,
                ResizeDirection = GridResizeDirection.Columns,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = new Cursor(StandardCursorType.SizeWestEast)
            };
            
            Grid.SetColumn(splitter, i);
            _headerGrid.Children.Add(splitter);
            
            // Subscribe to drag events to trigger item grid regeneration
            splitter.DragStarted += OnSplitterDragStarted;
            splitter.DragCompleted += OnSplitterDragCompleted;
        }

        // Create ListBox with virtualization
        _listBox = new ListBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            SelectionMode = SelectionMode.Multiple,
            ItemsSource = _filteredItems
        };
        
        // Reduce ListBoxItem padding/margin to minimize vertical spacing
        var itemStyle = new Style(x => x.OfType<ListBoxItem>());
        itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(ListBoxItem.MinHeightProperty, 18.0));
        itemStyle.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, Brushes.Blue));
        _listBox.Styles.Add(itemStyle);

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
        
        // ALSO subscribe to LayoutUpdated for additional detection of container changes
        _listBox.LayoutUpdated += OnListBoxLayoutUpdated;

        this.Content = _listBox;
        
        Trace.WriteLine($"ListBoxBrowseView: Visual structure created with ListBox virtualization and resizable columns");
    }
    
    private void OnSplitterDragStarted(object? sender, VectorEventArgs e)
    {
        _isResizing = true;
        Trace.WriteLine("Column resize started");
    }
    
    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        _isResizing = false;
        Trace.WriteLine("Column resize completed - regenerating visible items");
        
        // Regenerate all visible item grids with new column widths
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RecustomizeVisibleContainers();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }
    
    private void OnListBoxLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            Trace.WriteLine("OnListBoxLoaded: ListBox loaded, customizing visible containers");
            
            Trace.WriteLine($"  ItemsSource has {_filteredItems.Count} items");
            
            var presenter = _listBox.Presenter;
            var panel = presenter?.Panel;
            Trace.WriteLine($"  Panel type: {panel?.GetType().Name ?? "NULL"}");
            Trace.WriteLine($"  Panel children count: {panel?.Children.Count ?? 0}");
            
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
            
            // Subscribe to EffectiveViewportChanged for scrolling
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
            // Get info about what's actually visible
            var presenter = _listBox.Presenter;
            var panel = presenter?.Panel;
            var visibleIndices = new List<int>();
            
            // Force re-customization of all visible containers
            int customizedCount = 0;
            int toStringCount = 0;
            int alreadyGridCount = 0;
            
            for (int i = 0; i < _filteredItems.Count; i++)
            {
                var container = _listBox.ContainerFromIndex(i) as ListBoxItem;
                if (container != null)
                {
                    visibleIndices.Add(i);
                    
                    var item = _filteredItems[i];
                    
                    // Log BEFORE setting Content
                    var contentBefore = container.Content;
                    var contentBeforeType = contentBefore?.GetType().Name ?? "NULL";
                    var isGridBefore = contentBefore is Grid;
                    var isToStringBefore = !isGridBefore && contentBefore != null;
                    
                    if (isToStringBefore)
                    {
                        toStringCount++;
                        Trace.WriteLine($"  ?? Container {i}: WAS ToString ({contentBeforeType}), fixing now...");
                    }
                    else if (isGridBefore)
                    {
                        alreadyGridCount++;
                    }
                    
                    // Create and set the new Grid
                    var grid = CreateItemGrid(item);
                    container.Content = grid;
                    
                    // Log IMMEDIATELY AFTER setting Content
                    var contentAfter = container.Content;
                    var contentAfterType = contentAfter?.GetType().Name ?? "NULL";
                    var isSameReference = ReferenceEquals(contentAfter, grid);
                    
                    customizedCount++;
                    
                    // Detailed logging only for ToString cases
                    if (isToStringBefore)
                    {
                        Trace.WriteLine($"       AFTER fix: {contentAfterType}, SameRef={isSameReference}");
                    }
                }
            }
            
            if (customizedCount > 0)
            {
                var indicesStr = visibleIndices.Count <= 5 
                    ? string.Join(", ", visibleIndices)
                    : $"{visibleIndices[0]}..{visibleIndices[visibleIndices.Count - 1]}";
                    
                Trace.WriteLine($"OnEffectiveViewportChanged: Visible indices [{indicesStr}], " +
                    $"Customized={customizedCount}, ToString={toStringCount}, AlreadyGrid={alreadyGridCount}");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ERROR in OnEffectiveViewportChanged: {ex.Message}");
            Trace.WriteLine($"  Stack: {ex.StackTrace}");
        }
    }

    private void OnListBoxLayoutUpdated(object? sender, EventArgs e)
    {
        // This fires frequently during layout changes, including when new containers are virtualized
        // We use it to catch newly created containers and convert them from ToString to Grid
        try
        {
            int fixedCount = 0;
            
            for (int i = 0; i < _filteredItems.Count; i++)
            {
                var container = _listBox.ContainerFromIndex(i) as ListBoxItem;
                if (container != null)
                {
                    var contentBefore = container.Content;
                    var isToStringBefore = contentBefore != null && !(contentBefore is Grid);
                    
                    if (isToStringBefore)
                    {
                        var item = _filteredItems[i];
                        var grid = CreateItemGrid(item);
                        container.Content = grid;
                        fixedCount++;
                    }
                }
            }
            
            // Only log when we actually fix containers (reduce noise)
            // Remove or comment out this line in production if desired
            // if (fixedCount > 0)
            // {
            //     Trace.WriteLine($"OnListBoxLayoutUpdated: Fixed {fixedCount} containers");
            // }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ERROR in OnListBoxLayoutUpdated: {ex.Message}");
        }
    }

    private Control CreateItemGrid(object item)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 18,
            Background = Brushes.Transparent,
            Margin = new Thickness(0)
        };

        // Copy column definitions from header grid to ensure synchronization (including the dummy column at the end)
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
                Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Grid.SetColumn(textBlock, i);
            grid.Children.Add(textBlock);
        }
        
        // Note: The dummy column at the end (_headerGrid.ColumnDefinitions.Count - 1) is left empty

        return grid;
    }

    private string FormatValue(object? value)
    {
        if (value == null)
            return string.Empty;

        Type type = value.GetType();
        
        if (type == typeof(string))
        {
            var str = value.ToString()!.Trim();
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
        
        return value.ToString() ?? string.Empty;
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
            // Use selected items if any are selected, otherwise use all filtered items
            var itemsToExport = _listBox.SelectedItems != null && _listBox.SelectedItems.Count > 0
                ? _listBox.SelectedItems.Cast<object>()
                : _filteredItems.Cast<object>();
            
            var itemCount = itemsToExport.Count();
            
            // Create temp file like the original WPF version
            var tmpFileName = System.IO.Path.GetTempFileName();
            var sb = new System.Text.StringBuilder();
            
            // Add header row
            var headers = _columns.Select(c => c.HeaderText).ToList();
            sb.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));
            
            // Add data rows
            foreach (var item in itemsToExport)
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

            // Write the file
            System.IO.File.WriteAllText(tmpFileName, sb.ToString(), System.Text.Encoding.UTF8);
            var filename = System.IO.Path.ChangeExtension(tmpFileName, "csv");
            System.IO.File.Move(tmpFileName, filename);
            
            Trace.WriteLine($"OnExportCsvClick: Exported {itemCount} items to {filename}");
            
            // Use shell execute to open with default .csv handler (like original)
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
            Trace.WriteLine($"ERROR in OnExportCsvClick: {ex.Message}");
        }
    }

    private async void OnExportTxtClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Use selected items if any are selected, otherwise use all filtered items
            var itemsToExport = _listBox.SelectedItems != null && _listBox.SelectedItems.Count > 0
                ? _listBox.SelectedItems.Cast<object>()
                : _filteredItems.Cast<object>();
            
            var itemCount = itemsToExport.Count();
            
            // Create temp file like the original WPF version
            var tmpFileName = System.IO.Path.GetTempFileName();
            var sb = new System.Text.StringBuilder();
            
            // Add header row
            var headers = _columns.Select(c => c.HeaderText).ToList();
            sb.AppendLine(string.Join("\t", headers));
            
            // Add data rows
            foreach (var item in itemsToExport)
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
            
            Trace.WriteLine($"OnExportTxtClick: Exported {itemCount} items to {filename}");
            
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

    private void OnHeaderClick(int columnIndex)
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
        // Iterate through children and only update buttons (skip GridSplitters)
        int buttonIndex = 0;
        foreach (var child in _headerGrid.Children)
        {
            if (child is Button btn)
            {
                var col = _columns[buttonIndex];
                if (buttonIndex == sortedColumnIndex)
                {
                    btn.Content = $"{col.HeaderText} {(ascending ? "?" : "?")}";
                }
                else
                {
                    btn.Content = col.HeaderText;
                }
                buttonIndex++;
            }
        }
    }

    /// <summary>
    /// Adds a custom context menu item
    /// </summary>
    /// <param name="itemName">The display name for the menu item</param>
    /// <param name="tooltip">Optional tooltip for the menu item</param>
    /// <param name="action">Action to execute with the currently selected items</param>
    public void AddContextMenuItem(string itemName, string tooltip, Action<IList<object>> action)
    {
        if (_listBox?.ContextMenu == null)
        {
            Trace.WriteLine($"AddContextMenuItem: ContextMenu is null");
            return;
        }

        var menuItem = new MenuItem { Header = itemName };
        
        if (!string.IsNullOrEmpty(tooltip))
        {
            ToolTip.SetTip(menuItem, tooltip);
        }

        menuItem.Click += (s, e) =>
        {
            try
            {
                var selectedItems = _listBox.SelectedItems;
                if (selectedItems == null || selectedItems.Count == 0)
                {
                    Trace.WriteLine($"{itemName}: No items selected");
                    return;
                }

                var itemsList = selectedItems.Cast<object>().ToList();
                action?.Invoke(itemsList);
                
                Trace.WriteLine($"{itemName}: Executed on {itemsList.Count} items");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ERROR in {itemName}: {ex.Message}");
            }
        };

        _listBox.ContextMenu.Items.Add(menuItem);
        Trace.WriteLine($"AddContextMenuItem: Added '{itemName}' to context menu");
    }
}

internal class ListBoxColumnInfo
{
    public required string HeaderText { get; set; }
    public required string BindingPath { get; set; }
    public int Width { get; set; }
}
