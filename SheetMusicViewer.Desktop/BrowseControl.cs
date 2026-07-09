using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using SheetMusicLib;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// A control that displays a filterable, sortable list with column headers.
/// Similar to WPF's BrowsePanel but using Avalonia's ListBox with virtualization.
/// </summary>
public class BrowseControl : DockPanel
{
    public ListBoxBrowseView ListView { get; private set; } = null!;
    internal int[]? _colWidths;
    internal int _rowHeight;
    public IEnumerable _query = null!;
    private ListBoxListFilter _listFilter = null!;

    /// <summary>
    /// Default row height for normal density (good for mouse interaction)
    /// </summary>
    public const int DefaultRowHeight = 20;
    public const int DefaultColumnWidth = 120; // Default width when colWidths not provided

    /// <summary>
    /// Larger row height for touch/fat finger interaction
    /// </summary>
    public const int TouchRowHeight = 32;

    /// <summary>
    /// Non-generic interface so the renderer can call CreateControl() without knowing T and U.
    /// </summary>
    public interface IBrowseCustomField
    {
        Control? CreateControl();
        string SortKey { get; set; }
    }

    /// <summary>
    /// Typed extension that also exposes the bound data and entry objects.
    /// </summary>
    public interface IBrowseCustomField<T, U> : IBrowseCustomField
    {
        T Data { get; }
        U Entry { get; }
    }

    /// <summary>
    /// Inline custom-field: supply a factory lambda <c>(data, entry) =&gt; Control</c>
    /// and the renderer will call it to produce the cell control.
    /// </summary>
    public class BrowseField<T, U> : IBrowseCustomField<T, U>
    {
        private readonly Func<BrowseField<T, U>, Control> _getControlFunc;

        public BrowseField(T data, U entry, Func<BrowseField<T, U>, Control> getControl)
        {
            Data = data;
            Entry = entry;
            _getControlFunc = getControl;
        }

        public T Data { get; }
        public U Entry { get; }
        public string SortKey { get; set; } = string.Empty;

        /// <summary>Invokes the factory to produce the cell control.</summary>
        public Control? CreateControl() => _getControlFunc(this);
    }
    /// <summary>
    /// Creates a new BrowseControl with filterable, sortable list display.
    /// </summary>
    /// <param name="query">The data source to display</param>
    /// <param name="colWidths">Optional column widths array</param>
    /// <param name="filterOnLeft">Whether to place the filter on the left (true) or right (false)</param>
    /// <param name="rowHeight">Height of each row in pixels. Use DefaultRowHeight (20) for high density, TouchRowHeight (32) for touch-friendly spacing</param>
    public BrowseControl(IEnumerable query, int[]? colWidths = null, bool filterOnLeft = true, int rowHeight = DefaultRowHeight)
    {
        try
        {
            _query = query;
            _colWidths = colWidths;
            _rowHeight = rowHeight > 0 ? rowHeight : DefaultRowHeight;

            this.LastChildFill = true;
            this.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.VerticalAlignment = VerticalAlignment.Stretch;

            _listFilter = new ListBoxListFilter(null!, filterOnLeft);
            this.Children.Add(_listFilter);
            DockPanel.SetDock(_listFilter, Dock.Top);

            ListView = new ListBoxBrowseView(query, this);
            this.Children.Add(ListView);

            _listFilter.SetBrowseList(ListView);
        }
        catch (Exception ex)
        {
            this.Children.Add(new TextBlock { Text = ex.ToString() });
            Logger.LogException("BrowseControl creation failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Focus the filter textbox
    /// </summary>
    public void FocusFilter()
    {
        _listFilter?.FocusFilter();
    }

    /// <summary>
    /// Clear the filter textbox
    /// </summary>
    public void ClearFilter()
    {
        _listFilter?.ClearFilter();
    }

    /// <summary>
    /// Get the current filter text
    /// </summary>
    public string GetFilterText()
    {
        return _listFilter?.GetFilterText() ?? string.Empty;
    }

    /// <summary>
    /// Set the filter text
    /// </summary>
    public void SetFilterText(string filterText)
    {
        _listFilter?.SetFilterText(filterText);
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
    readonly TextBox _txtFilter = new TextBox { Width = 200 };
    readonly TextBlock _txtStatus = new TextBlock();
    ListBoxBrowseView? _browse;
    // Removed static _LastFilter - was causing filter sharing between instances
    private readonly bool _filterOnLeft;

    internal ListBoxListFilter(ListBoxBrowseView? browse, bool filterOnLeft = true)
    {
        _browse = browse;
        _filterOnLeft = filterOnLeft;
        BuildUI();
    }

    internal void SetBrowseList(ListBoxBrowseView browse)
    {
        _browse = browse;
        RefreshFilterStat();
    }

    /// <summary>
    /// Focus the filter textbox
    /// </summary>
    internal void FocusFilter()
    {
        _txtFilter.Focus();
    }

    /// <summary>
    /// Clear the filter textbox
    /// </summary>
    internal void ClearFilter()
    {
        _txtFilter.Text = string.Empty;
    }

    /// <summary>
    /// Get the current filter text
    /// </summary>
    internal string GetFilterText()
    {
        return _txtFilter.Text ?? string.Empty;
    }

    /// <summary>
    /// Set the filter text
    /// </summary>
    internal void SetFilterText(string filterText)
    {
        _txtFilter.Text = filterText ?? string.Empty;
    }

    private void BuildUI()
    {
        var spFilter = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = _filterOnLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 5,
            Height = 30
        };
        _txtStatus.VerticalAlignment = VerticalAlignment.Center;
        spFilter.Children.Add(_txtStatus);
        spFilter.Children.Add(new Label
        {
            Content = "StringFilter",
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            [ToolTip.TipProperty] = "Case insensitive search (ListBox with virtualization)"
        });
        // Filter starts empty for each BrowseControl instance
        _txtFilter.Watermark = "Enter filter text...";
        _txtFilter.VerticalAlignment = VerticalAlignment.Center;
        _txtFilter.VerticalContentAlignment = VerticalAlignment.Center;
        spFilter.Children.Add(_txtFilter);
        this.Children.Add(spFilter);

        // Apply filter on every text change
        _txtFilter.TextChanged += (o, e) =>
        {
            ApplyFilter();
        };
    }

    void ApplyFilter()
    {
        try
        {
            var filtText = _txtFilter.Text?.Trim().ToLower() ?? string.Empty;
            // Each BrowseControl manages its own filter state

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
    private readonly int _rowHeight;
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

    public Grid HeaderGrid => _headerGrid;
    public IList SelectedItems => _listBox?.SelectedItems ?? new List<object>();
    public int SelectedIndex => _listBox?.SelectedIndex ?? -1;
    public object? SelectedItem => _listBox?.SelectedItem;

    /// <summary>
    /// Raised when the user changes the selection in the list.
    /// </summary>
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    /// <summary>
    /// Sets the selected index of the underlying ListBox
    /// </summary>
    public void SetSelectedIndex(int index)
    {
        if (_listBox != null && index >= 0 && index < _filteredItems.Count)
        {
            _listBox.SelectedIndex = index;
        }
    }

    /// <summary>
    /// Selects the first item in the filtered list whose property matching <paramref name="predicate"/> returns true.
    /// </summary>
    public void SelectFirstMatch(Func<object, bool> predicate)
    {
        if (_listBox == null) return;
        for (int i = 0; i < _filteredItems.Count; i++)
        {
            if (predicate(_filteredItems[i]))
            {
                _listBox.SelectedIndex = i;
                _listBox.ScrollIntoView(_filteredItems[i]);
                return;
            }
        }
        _listBox.SelectedIndex = -1;
    }

    public ListBoxBrowseView(IEnumerable query, BrowseControl browseControl)
    {
        this._colWidths = browseControl._colWidths;
        this._rowHeight = browseControl._rowHeight;
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

            //if (prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericTypeDefinition() == typeof(BrowseControl.BrowseField<>))
            //{
            //    // For BrowseField<T>, 
            //    var bField = prop.GetValue()


            //    var sampleValue = prop.GetValue(Activator.CreateInstance(itemType)!) as dynamic;
            //    if (sampleValue != null)
            //    {
            //        _columns.Add(new ListBoxColumnInfo
            //        {
            //            HeaderText = sampleValue.Header,
            //            BindingPath = prop.Name,
            //            Width = sampleValue.Width > 0 ? sampleValue.Width : DefaultColumnWidth
            //        });
            //    }
            //}

            int width = BrowseControl.DefaultColumnWidth; // Use default width
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

        Debug.WriteLine($"ListBoxBrowseView: Created with {_columns.Count} columns, {_filteredItems.Count} items");
    }

    private void BuildVisualStructure()
    {
        // Create header grid - use theme-aware colors
        _headerGrid = new Grid
        {
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0)
        };

        double minWidth = 0;
        bool hasStarColumn = false;
        foreach (var col in _columns)
        {
            minWidth += col.Width > 0 ? col.Width : 150;
        }
        for (int ci = 0; ci < _columns.Count; ci++)
        {
            var col = _columns[ci];
            var colDef = new ColumnDefinition();
            bool isLast = ci == _columns.Count - 1;
            if (col.Width <= 0)
            {
                // Caller explicitly requested a Star column
                colDef.Width = new GridLength(1, GridUnitType.Star);
                hasStarColumn = true;
            }
            else if (isLast && !hasStarColumn)
            {
                // All columns so far are fixed: promote the last one to Star so the
                // header button (and the matching item cell) stretches to the right edge,
                // eliminating the unstyled empty gap that a trailing filler column produces.
                colDef.MinWidth = col.Width;
                colDef.Width = new GridLength(1, GridUnitType.Star);
                hasStarColumn = true;
            }
            else
            {
                colDef.Width = new GridLength(col.Width);
            }
            _headerGrid.ColumnDefinitions.Add(colDef);
        }

        // No trailing filler column needed: the last column is already Star.

        for (int i = 0; i < _columns.Count; i++)
        {
            var col = _columns[i];
            var headerButton = new Button
            {
                Content = col.HeaderText,
                BorderThickness = new Thickness(0, 0, 1, 1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(5, 2, 5, 2),
                FontSize = 12,
                FontWeight = FontWeight.Normal,
                [ToolTip.TipProperty] = col.HeaderText
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
            ItemsSource = _filteredItems,
            BorderThickness = new Thickness(0)
        };

        // Reduce ListBoxItem padding/margin to minimize vertical spacing.
        // HorizontalContentAlignment=Stretch is critical: without it the inner ContentPresenter
        // does not stretch its child to fill the item width, so fixed-width item grids appear
        // narrower than the ListBoxItem selection highlight.
        var itemStyle = new Style(x => x.OfType<ListBoxItem>());
        itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(ListBoxItem.MinHeightProperty, (double)_rowHeight));
        itemStyle.Setters.Add(new Setter(ListBoxItem.FontSizeProperty, 12.0));
        itemStyle.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        _listBox.Styles.Add(itemStyle);

        _listBox.SelectionChanged += (s, e) => SelectionChanged?.Invoke(this, e);

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

        // Disable horizontal scroll as soon as the ListBox's control template is applied
        // (fires before the first measure pass, so star-columns in items are never measured with infinite width).
        _listBox.TemplateApplied += OnListBoxTemplateApplied;

        // Use Loaded event to customize containers after they're created
        _listBox.Loaded += OnListBoxLoaded;

        // ALSO subscribe to LayoutUpdated for additional detection of container changes
        _listBox.LayoutUpdated += OnListBoxLayoutUpdated;

        // Place header and listbox together in a shared DockPanel so they always
        // have identical width bounds, keeping columns perfectly aligned.
        var innerPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_headerGrid, Dock.Top);
        innerPanel.Children.Add(_headerGrid);
        innerPanel.Children.Add(_listBox);

        this.Content = innerPanel;

        // When the header re-lays-out (including first render), regenerate item grids
        // using the header's actual pixel column widths so star columns match exactly.
        _headerGrid.LayoutUpdated += OnHeaderGridLayoutUpdated;
    }

    private double _lastHeaderWidth = -1;

    private void OnHeaderGridLayoutUpdated(object? sender, EventArgs e)
    {
        // Only act when the header width has actually changed and column ActualWidths are ready.
        var totalActual = _headerGrid.ColumnDefinitions.Sum(c => c.ActualWidth);
        if (totalActual < 1 || Math.Abs(totalActual - _lastHeaderWidth) < 0.5) return;
        _lastHeaderWidth = totalActual;
        RecustomizeVisibleContainers();
    }

    private void OnSplitterDragStarted(object? sender, VectorEventArgs e)
    {
        _isResizing = true;
    }

    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        _isResizing = false;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RecustomizeVisibleContainers();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void OnListBoxTemplateApplied(object? sender, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        // Find the ListBox's internal ScrollViewer at template-apply time (before the first measure
        // pass) and disable horizontal scrolling so Star columns in item grids are not measured
        // against infinite width.
        var sv = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer")
               ?? _listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (sv != null)
            sv.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }

    private void OnListBoxLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            for (int i = 0; i < _filteredItems.Count; i++)
            {
                var container = _listBox.ContainerFromIndex(i) as ListBoxItem;
                if (container != null)
                {
                    container.Content = CreateItemGrid(_filteredItems[i]);
                }
            }

            _listBox.EffectiveViewportChanged += OnEffectiveViewportChanged;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR in OnListBoxLoaded: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        try
        {
            for (int i = 0; i < _filteredItems.Count; i++)
            {
                var container = _listBox.ContainerFromIndex(i) as ListBoxItem;
                if (container != null)
                {
                    var grid = CreateItemGrid(_filteredItems[i]);
                    container.Content = grid;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR in OnEffectiveViewportChanged: {ex.Message}");
        }
    }

    private void OnListBoxLayoutUpdated(object? sender, EventArgs e)
    {
        try
        {
            for (int i = 0; i < _filteredItems.Count; i++)
            {
                var container = _listBox.ContainerFromIndex(i) as ListBoxItem;
                if (container != null && !(container.Content is Grid))
                {
                    var grid = CreateItemGrid(_filteredItems[i]);
                    container.Content = grid;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR in OnListBoxLayoutUpdated: {ex.Message}");
        }
    }

    private Control CreateItemGrid(object item)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = _rowHeight,
            Background = Brushes.Transparent,
            Margin = new Thickness(0)
        };

        // Use the header's actual measured pixel widths for fixed columns so the item grid
        // is always an exact match to the header. The last column is always Star (promoted
        // in the header construction above), so leave it as Star in the item grid too —
        // with horizontal scrolling disabled and the item grid stretching to fill the
        // ListBoxItem, a Star column here resolves to the same remaining width as the header.
        foreach (var colDef in _headerGrid.ColumnDefinitions)
        {
            ColumnDefinition itemCol;
            if (colDef.Width.IsStar)
            {
                // Star column: copy min-width constraint and let it stretch naturally
                itemCol = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = colDef.MinWidth };
            }
            else if (colDef.ActualWidth > 0)
            {
                // Fixed column: pin to exact measured pixel width
                itemCol = new ColumnDefinition { Width = new GridLength(colDef.ActualWidth) };
            }
            else
            {
                itemCol = new ColumnDefinition { Width = colDef.Width };
            }
            grid.ColumnDefinitions.Add(itemCol);
        }

        for (int i = 0; i < _columns.Count; i++)
        {
            var col = _columns[i];

            try
            {
                var prop = TypeDescriptor.GetProperties(item)[col.BindingPath];
                Control? control = null;
                if (prop != null)
                {
                    var value = prop.GetValue(item);
                    // if the value implements IBrowseCustomField, let it produce its own cell control
                    if (value is BrowseControl.IBrowseCustomField customField)
                    {
                        control = customField.CreateControl();
                    }
                    if (control == null)
                    {
                        var cellText = FormatValue(value);
                        control = new TextBlock
                        {
                            Text = cellText,
                            Padding = new Thickness(5, 0, 5, 0),
                            Margin = new Thickness(0),
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            FontSize = 12
                        };
                    }
                    Grid.SetColumn(control, i);
                    grid.Children.Add(control);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting value for {col.BindingPath}: {ex.Message}");
            }
        }

        return grid;
    }

    private string FormatValue(object? value)
    {
        if (value == null)
            return string.Empty;

        if (value is BrowseControl.IBrowseCustomField customField)
            return customField.SortKey;

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

    /// <summary>
    /// Returns items to export, always in the current sort order from _filteredItems.
    /// When a selection exists, only selected items are included but their order
    /// follows _filteredItems, not the order in which they were selected.
    /// </summary>
    private IEnumerable<object> GetItemsInSortOrder()
    {
        var selected = _listBox.SelectedItems;
        if (selected != null && selected.Count > 0)
        {
            var selectedSet = selected.Cast<object>().ToHashSet(ReferenceEqualityComparer.Instance);
            return _filteredItems.Where(item => selectedSet.Contains(item));
        }
        return _filteredItems;
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var selectedItems = _listBox.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
            {
                Debug.WriteLine("OnCopyClick: No items selected");
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var item in GetItemsInSortOrder())
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
                Debug.WriteLine($"OnCopyClick: Copied {selectedItems.Count} items to clipboard");
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("Copy to clipboard failed", ex);
        }
    }

    private async void OnExportCsvClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var itemsToExport = GetItemsInSortOrder();
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

            Debug.WriteLine($"OnExportCsvClick: Exported {itemCount} items to {filename}");

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
                Logger.LogWarning($"Could not open CSV file: {openEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("Export to CSV failed", ex);
        }
    }

    private async void OnExportTxtClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var itemsToExport = GetItemsInSortOrder();
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

            Debug.WriteLine($"OnExportTxtClick: Exported {itemCount} items to {filename}");

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
                Logger.LogWarning($"Could not open TXT file: {openEx.Message}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("Export to Notepad failed", ex);
        }
    }

    private void OnHeaderClick(int columnIndex)
    {
        Debug.WriteLine($"Header clicked: column {columnIndex}");

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
            // The BrowseField may not have been instantiated yet, so the sort may be wrong
            var sortedItems = ascending
                ? _filteredItems.OrderBy(item => GetPropertyValue(item, col.BindingPath)).ToList()
                : _filteredItems.OrderByDescending(item => GetPropertyValue(item, col.BindingPath)).ToList();

            _filteredItems.Clear();
            foreach (var item in sortedItems)
            {
                _filteredItems.Add(item);
            }

            Debug.WriteLine($"Sorted by {col.HeaderText} ({(ascending ? "ascending" : "descending")})");

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RecustomizeVisibleContainers();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error sorting by {col.HeaderText}: {ex.Message}");
        }
    }

    private void RecustomizeVisibleContainers()
    {
        try
        {
            for (int i = 0; i < _filteredItems.Count; i++)
            {
                var container = _listBox.ContainerFromIndex(i) as ListBoxItem;
                if (container != null)
                {
                    container.Content = CreateItemGrid(_filteredItems[i]);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR in RecustomizeVisibleContainers: {ex.Message}");
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

        Debug.WriteLine($"Filter applied: {_filteredItems.Count} items match '{filterText}'");

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
                var value = prop.GetValue(item);
                if (value is BrowseControl.IBrowseCustomField customField)
                    return customField.SortKey;
                return value ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting property value: {ex.Message}");
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
                    btn.Content = $"{col.HeaderText} {(ascending ? "▲" : "▼")}";
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
            Debug.WriteLine($"AddContextMenuItem: ContextMenu is null");
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
                    Debug.WriteLine($"{itemName}: No items selected");
                    return;
                }

                var itemsList = selectedItems.Cast<object>().ToList();
                action?.Invoke(itemsList);

                Debug.WriteLine($"{itemName}: Executed on {itemsList.Count} items");
            }
            catch (Exception ex)
            {
                Logger.LogException($"Context menu action '{itemName}' failed", ex);
            }
        };

        _listBox.ContextMenu.Items.Add(menuItem);
        Debug.WriteLine($"AddContextMenuItem: Added '{itemName}' to context menu");
    }
}

internal class ListBoxColumnInfo
{
    public required string HeaderText { get; set; }
    public required string BindingPath { get; set; }
    public int Width { get; set; }
}
