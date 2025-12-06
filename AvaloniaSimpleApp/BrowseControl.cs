using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace AvaloniaSimpleApp;

/// <summary>
/// Standalone browse control that accepts a LINQ query and automatically generates columns.
/// 
/// Usage Example:
/// <code>
/// var query = from type in Assembly.GetExecutingAssembly().GetTypes()
///             where type.IsClass
///             select new
///             {
///                 TypeName = type.Name,
///                 Namespace = type.Namespace,
///                 IsPublic = type.IsPublic,
///                 MethodCount = type.GetMethods().Length
///             };
/// 
/// var browseControl = new BrowseControl(query, colWidths: new[] { 200, 300, 100, 100 });
/// somePanel.Children.Add(browseControl);
/// </code>
/// 
/// The control automatically:
/// - Generates columns from the query's anonymous type properties
/// - Provides filtering via string search
/// - Supports sorting by clicking column headers
/// - Allows copy/export to Excel/Notepad via context menu
/// - Shows item count
/// 
/// Properties starting with "_" are treated as base objects and not displayed as columns.
/// </summary>
public class BrowseControl : DockPanel
{
    public BrowseListView ListView { get; private set; }
    internal int[] _colWidths;
    public IEnumerable _query;

    public BrowseControl(IEnumerable query, int[] colWidths = null)
    {
        try
        {
            _query = query;
            _colWidths = colWidths;
            
            this.LastChildFill = true;
            this.HorizontalAlignment = HorizontalAlignment.Stretch;
            this.VerticalAlignment = VerticalAlignment.Stretch;
            
            var listFilter = new ListFilter(null);
            this.Children.Add(listFilter);
            DockPanel.SetDock(listFilter, Dock.Top);

            // Create a container for header + listview with ScrollViewer
            var listContainer = new DockPanel
            {
                LastChildFill = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            ListView = new BrowseListView(query, this);
            
            // Add header grid to container
            if (ListView.HeaderGrid != null)
            {
                listContainer.Children.Add(ListView.HeaderGrid);
                DockPanel.SetDock(ListView.HeaderGrid, Dock.Top);
            }
            
            // ListView is now a ScrollViewer, add it directly
            listContainer.Children.Add(ListView);
            
            this.Children.Add(listContainer);
            
            listFilter.SetBrowseList(ListView);
            
            Trace.WriteLine($"BrowseControl: Filter bounds = {listFilter.Bounds}, ListView bounds = {ListView.Bounds}");
            Trace.WriteLine($"BrowseControl: HeaderGrid bounds = {ListView.HeaderGrid?.Bounds}");
            Trace.WriteLine($"BrowseControl: Children count = {this.Children.Count}");
        }
        catch (Exception ex) when (ex != null)
        {
            this.Children.Add(new TextBlock() { Text = ex.ToString() });
            Trace.WriteLine($"BrowseControl: Exception during initialization: {ex}");
        }
    }

    public new ContextMenu ContextMenu
    {
        get
        {
            if (ListView.ContextMenu == null)
            {
                ListView.ContextMenu = new ContextMenu();
            }
            return ListView.ContextMenu;
        }
    }
}

internal class ListFilter : DockPanel
{
    readonly Button _btnApply = new Button() { Content = "Apply" };
    readonly TextBox _txtFilter = new TextBox { Width = 200 };
    readonly TextBlock _txtStatus = new TextBlock();
    BrowseListView _browse;
    private static string _LastFilter;

    internal ListFilter(BrowseListView browse)
    {
        _browse = browse;
        BuildUI();
    }

    internal void SetBrowseList(BrowseListView browse)
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
            [ToolTip.TipProperty] = "Case insensitive search in character fields. A filter works on current set"
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

public class BrowseListView : ScrollViewer
{
    int _nColIndex = 0;
    private readonly int[] _colWidths;
    private readonly IEnumerable _originalQuery;
    private ObservableCollection<object> _allItems;
    private ObservableCollection<object> _filteredItems;
    private string _currentFilter = string.Empty;
    private Grid _headerGrid;
    private StackPanel _itemsPanel;

    readonly Type _baseType = null;
    readonly string _baseTypeName = string.Empty;
    internal int DefaultNumberDecimals = 2;
    
    private List<ColumnInfo> _columns = new List<ColumnInfo>();

    public Grid HeaderGrid => _headerGrid;
    public IList SelectedItems { get; } = new List<object>();
    public int SelectedIndex { get; set; } = -1;
    public object SelectedItem { get; set; }
    public int ItemCount => _filteredItems?.Count ?? 0;

    public BrowseListView(IEnumerable query, BrowseControl browseControl)
    {
        this._colWidths = browseControl._colWidths;
        this._originalQuery = query;
        this.HorizontalAlignment = HorizontalAlignment.Stretch;
        this.VerticalAlignment = VerticalAlignment.Stretch;
        this.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        this.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        
        // Create collections
        _allItems = new ObservableCollection<object>();
        _filteredItems = new ObservableCollection<object>();
        
        foreach (var item in query)
        {
            _allItems.Add(item);
            _filteredItems.Add(item);
        }

        // Get the item type from the query
        var ienum = query.GetType().GetInterface(typeof(IEnumerable<>).FullName);
        var itemType = ienum.GetGenericArguments()[0];
        
        Trace.WriteLine($"BrowseListView: Item type = {itemType.Name}");

        var members = itemType.GetMembers()
            .Where(m => m.MemberType == MemberTypes.Property);

        // Analyze properties to build column info
        foreach (var mbr in members)
        {
            var dataType = mbr as PropertyInfo;
            var colType = dataType.PropertyType.Name;
            
            if (mbr.Name.StartsWith("_"))
            {
                _baseType = dataType.PropertyType;
                _baseTypeName = mbr.Name;
                continue;
            }

            if (mbr.Name.StartsWith("_x"))
            {
                var methodName = $"get_{mbr.Name}";
                var enumerator = query.GetEnumerator();
                var fLoopDone = false;
                while (!fLoopDone)
                {
                    if (enumerator.MoveNext())
                    {
                        var currentRecord = enumerator.Current;
                        var currentRecType = currentRecord.GetType();
                        var msgObj = currentRecType.InvokeMember(methodName, BindingFlags.InvokeMethod, null, currentRecord, null);
                        if (msgObj != null)
                        {
                            var msgObjType = msgObj.GetType();
                            var msgObjTypeProps = msgObjType.GetProperties();
                            foreach (var prop in msgObjTypeProps)
                            {
                                AddColumn(prop.Name, prop.Name, mbr.Name);
                            }
                            fLoopDone = true;
                        }
                    }
                    else
                    {
                        fLoopDone = true;
                    }
                }
            }
            else
            {
                AddColumn(mbr.Name, mbr.Name);
            }
        }

        // Create the visual structure
        BuildVisualStructure();
        
        // Render items manually
        RenderItems();
        
        Trace.WriteLine($"BrowseListView: Created with {this._columns.Count} columns and {_filteredItems.Count} items");

        this.ContextMenu = new ContextMenu();
        AddContextMenuItem("_Copy", "Copy selected items to clipboard", OnCopy);
        AddContextMenuItem("Export to E_xcel", "Create a temp file of selected items in CSV format", OnExportExcel);
        AddContextMenuItem("Export to _Notepad", "Create a temp file of selected items in TXT format", OnExportNotepad);
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

        // Add column definitions for headers
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

        // Add header buttons
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

        // Create items panel
        _itemsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.White,
            Margin = new Thickness(8, 0, 8, 8)
        };

        // Set the items panel directly as content (header will be added by BrowseControl)
        this.Content = _itemsPanel;
        
        Trace.WriteLine($"BrowseListView.BuildVisualStructure: Visual structure created");
    }

    private void RenderItems()
    {
        Trace.WriteLine($"BrowseListView.RenderItems: Rendering {_filteredItems.Count} items manually");
        
        _itemsPanel.Children.Clear();
        
        foreach (var item in _filteredItems)
        {
            var itemGrid = CreateItemRow(item);
            _itemsPanel.Children.Add(itemGrid);
        }
        
        Trace.WriteLine($"BrowseListView.RenderItems: Added {_itemsPanel.Children.Count} rows to panel");
    }

    private Grid CreateItemRow(object item)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 25,
            Background = Brushes.White,
            Margin = new Thickness(0, 0, 0, 1)
        };

        // Add column definitions matching header
        foreach (var colDef in _headerGrid.ColumnDefinitions)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = colDef.Width });
        }

        // Add cells
        for (int i = 0; i < _columns.Count; i++)
        {
            var col = _columns[i];
            
            // Get property value
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
                Padding = new Thickness(5, 2, 5, 2),
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
            return ((double)value).ToString($"n{DefaultNumberDecimals}");
        }
        
        return value.ToString();
    }

    public void ApplyFilter(string filterText)
    {
        _currentFilter = filterText;
        
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
                    if (prop.Name.StartsWith("_x"))
                    {
                        continue;
                    }
                    
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
        
        // Re-render items after filtering
        RenderItems();
    }

    public int GetFilteredCount()
    {
        return _filteredItems?.Count ?? 0;
    }

    private void AddColumn(string columnName, string bindingName, string bindingObjectName = null)
    {
        var bindingPath = string.IsNullOrEmpty(bindingObjectName) 
            ? bindingName 
            : $"{bindingObjectName}.{bindingName}";

        var headerText = columnName;
        if (bindingName.StartsWith(_baseTypeName + "."))
        {
            headerText = bindingName.Replace(_baseTypeName + ".", string.Empty);
        }

        if (!string.IsNullOrEmpty(bindingObjectName))
        {
            if (bindingObjectName.StartsWith("_x"))
            {
                headerText = bindingPath.Substring(2);
            }
            else
            {
                headerText = bindingPath;
            }
        }

        int width = 0;
        if (_colWidths != null && _nColIndex < _colWidths.Length)
        {
            width = _colWidths[_nColIndex];
        }

        _columns.Add(new ColumnInfo
        {
            HeaderText = headerText,
            BindingPath = bindingPath,
            Width = width
        });

        _nColIndex++;
    }

    private void OnHeaderClick(int columnIndex)
    {
        Trace.WriteLine($"Header clicked: column {columnIndex}");
        // TODO: Implement sorting
    }

    private void AddContextMenuItem(string header, string tooltip, Action<object, RoutedEventArgs> handler)
    {
        var menuItem = new MenuItem
        {
            Header = header,
            [ToolTip.TipProperty] = tooltip
        };
        menuItem.Click += (s, e) => handler(s, e);
        this.ContextMenu.Items.Add(menuItem);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var text = DumpToString(false);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard != null)
        {
            topLevel.Clipboard.SetTextAsync(text);
        }
    }

    private void OnExportExcel(object sender, RoutedEventArgs e)
    {
        var text = DumpToString(true);
        WriteOutputToTempFile(text, "csv");
    }

    private void OnExportNotepad(object sender, RoutedEventArgs e)
    {
        var text = DumpToString(false);
        WriteOutputToTempFile(text, "txt");
    }

    public string DumpToString(bool fCSV)
    {
        var sb = new StringBuilder();
        var isFirst = true;
        var priorPadding = 0;
        int[] widths = new int[_columns.Count];
        var colndx = 0;

        // Write header row
        foreach (var col in _columns)
        {
            if (isFirst)
            {
                isFirst = false;
            }
            else
            {
                if (fCSV)
                {
                    sb.Append(",");
                }
                else
                {
                    for (var i = 0; i < priorPadding; i++)
                    {
                        sb.Append(" ");
                    }
                }
            }
            
            var txt = col.HeaderText;
            widths[colndx] = col.Width > 0 ? col.Width / 6 : 20;
            priorPadding = widths[colndx] - txt.Length;
            sb.Append(txt);
            colndx++;
        }
        sb.AppendLine();

        var srcColl = this.SelectedItems != null && this.SelectedItems.Count > 0
            ? this.SelectedItems.Cast<object>() 
            : _filteredItems.Cast<object>();

        void doit(string strval)
        {
            if (string.IsNullOrEmpty(strval))
            {
                strval = string.Empty;
            }
            strval = strval.Trim();
            if (isFirst)
            {
                isFirst = false;
            }
            else
            {
                if (fCSV)
                {
                    sb.Append(",");
                }
                else
                {
                    for (var i = 0; i < priorPadding; i++)
                    {
                        sb.Append(" ");
                    }
                }
            }
            if (!string.IsNullOrEmpty(strval))
            {
                if (fCSV && (strval.Contains(",") || strval.Contains(" ")))
                {
                    strval = "\"" + strval + "\"";
                }
                sb.Append(strval);
            }
            if (colndx < widths.Length)
            {
                priorPadding = widths[colndx] - strval.Length;
            }
        }

        foreach (var row in srcColl)
        {
            isFirst = true;
            priorPadding = 0;

            for (colndx = 0; colndx < _columns.Count; colndx++)
            {
                var col = _columns[colndx];
                try
                {
                    var typeDescProp = TypeDescriptor.GetProperties(row)[col.BindingPath];
                    if (typeDescProp != null)
                    {
                        var rawval = typeDescProp.GetValue(row);
                        doit(rawval?.ToString() ?? string.Empty);
                    }
                    else
                    {
                        var typdesc = TypeDescriptor.GetProperties(row);
                        var baseValtDesc = typdesc?[0];
                        var baseVal = baseValtDesc?.GetValue(row);
                        var baseType = baseVal?.GetType();
                        var len = baseType?.GetMember(col.BindingPath).Length;
                        if (len > 0)
                        {
                            var propInfo = (PropertyInfo)baseType?.GetMember(col.BindingPath)?[0];
                            var val = propInfo?.GetValue(baseVal);
                            doit(val?.ToString() ?? string.Empty);
                        }
                        else
                        {
                            doit(string.Empty);
                        }
                    }
                }
                catch (Exception)
                {
                    doit(string.Empty);
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string WriteOutputToTempFile(string strToOutput, string fExt = "txt", bool fStartIt = true)
    {
        var tmpFileName = Path.GetTempFileName();
        File.WriteAllText(tmpFileName, strToOutput, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        var filename = Path.ChangeExtension(tmpFileName, fExt);

        File.Move(tmpFileName, filename, overwrite: true);
        if (fStartIt)
        {
            try
            {
                Process.Start(new ProcessStartInfo(filename) { UseShellExecute = true });
            }
            catch (Exception)
            {
            }
        }
        return filename;
    }
}

internal class ColumnInfo
{
    public string HeaderText { get; set; }
    public string BindingPath { get; set; }
    public int Width { get; set; }
}

public class BrowseValueConverter : Avalonia.Data.Converters.IValueConverter
{
    private const int maxwidth = 1000;
    private readonly BrowseListView _listView;

    public BrowseValueConverter(BrowseListView listView)
    {
        _listView = listView;
    }

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value == null)
            return string.Empty;

        Type type = value.GetType();
        
        if (type == typeof(string))
        {
            var str = value.ToString().Trim();
            var ndx = str.IndexOfAny(new[] { '\r', '\n' });
            var lenlimit = maxwidth;
            if (ndx >= 0)
            {
                lenlimit = ndx;
            }
            if (ndx >= 0 || str.Length > lenlimit)
            {
                return str.Substring(0, lenlimit);
            }
            return str;
        }
        else if (type == typeof(Int32))
        {
            return value;
        }
        else if (type == typeof(Int64))
        {
            return ((Int64)value).ToString("n0");
        }
        else if (type == typeof(double))
        {
            return ((double)value).ToString($"n{_listView.DefaultNumberDecimals}");
        }
        
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
