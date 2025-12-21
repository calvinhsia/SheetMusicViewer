using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using PDFtoImage;
using SheetMusicLib;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// ChooseMusic window for selecting PDF books and songs.
/// Replicates the WPF ChooseMusic functionality with cross-platform support.
/// </summary>
public class ChooseMusicWindow : Window
{
    private TabControl _tabControl;
    private ListBox _lbBooks;
    private TextBlock _tbxTotals;
    private ComboBox _cboRootFolder;
    private TextBox _tbxFilter;
    private RadioButton _rbtnByDate;
    private RadioButton _rbtnByFolder;
    private RadioButton _rbtnByNumPages;
    
    // Favorites tab
    private ListBox _favoritesListBox;
    private TextBlock _favoritesStatus;
    
    // Query tab - uses BrowseControl
    private BrowseControl? _queryBrowseControl;
    private Grid _queryTabGrid;

    private List<PdfMetaDataReadResult> _pdfMetadata;
    private string _rootFolder;
    
    // Cache for book items (bitmap + metadata)
    private List<BookItemCache> _bookItemCache = new();
    private bool _isLoading = false;
    
    // Favorites data source
    private List<FavoriteItem> _allFavoriteItems = new();
    
    // Flag to prevent recursive selection change
    private bool _enableCboSelectionChange = true;
    
    // Shared double-tap helper for consistent detection across all item types
    private readonly DoubleTapHelper _doubleTapHelper = new();

    private const int ThumbnailWidth = 150;
    private const int ThumbnailHeight = 225;
    private const string NewFolderDialogString = "New...";
    
    /// <summary>
    /// If true, skip cloud-only files instead of triggering download.
    /// </summary>
    public bool SkipCloudOnlyFiles { get; set; } = false;
    
    /// <summary>
    /// The selected PDF metadata (set when user clicks OK)
    /// </summary>
    public PdfMetaDataReadResult? ChosenPdfMetaData { get; private set; }
    
    /// <summary>
    /// The selected page number (for favorites/query selection)
    /// </summary>
    public int ChosenPageNo { get; private set; }
    
    /// <summary>
    /// The current root folder (may have changed if user selected a new folder)
    /// </summary>
    public string CurrentRootFolder => _rootFolder;
    
    /// <summary>
    /// The current PDF metadata list (may have changed if user selected a new folder)
    /// </summary>
    public List<PdfMetaDataReadResult> CurrentPdfMetadata => _pdfMetadata;

    private class BookItemCache
    {
        public PdfMetaDataReadResult Metadata { get; set; } = null!;
        public string BookName { get; set; } = string.Empty;
        public int NumSongs { get; set; }
        public int NumPages { get; set; }
        public int NumFavs { get; set; }
        public Bitmap? Bitmap => Metadata?.GetCachedThumbnail<Bitmap>();
    }
    
    private class FavoriteItem
    {
        public PdfMetaDataReadResult Metadata { get; set; } = null!;
        public Favorite Favorite { get; set; } = null!;
        public string Description { get; set; } = string.Empty;
        public int PageNo { get; set; }
        public Bitmap? Thumbnail { get; set; }
        public string BookName { get; set; } = string.Empty;
    }

    public ChooseMusicWindow() : this(null, null)
    {
    }

    public ChooseMusicWindow(List<PdfMetaDataReadResult>? pdfMetadata, string? rootFolder)
    {
        _pdfMetadata = pdfMetadata ?? new List<PdfMetaDataReadResult>();
        _rootFolder = rootFolder ?? string.Empty;
        
        // Load setting from AppSettings
        SkipCloudOnlyFiles = AppSettings.Instance.UserOptions.SkipCloudOnlyFiles;
        
        Title = "Choose Music";
        ShowInTaskbar = false; // Don't show separate taskbar icon
        
        // Restore window size/position from settings
        var settings = AppSettings.Instance;
        
        Width = settings.ChooseWindowWidth > 0 ? settings.ChooseWindowWidth : 900;
        Height = settings.ChooseWindowHeight > 0 ? settings.ChooseWindowHeight : 700;
        
        if (settings.ChooseWindowLeft >= 0 && settings.ChooseWindowTop >= 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new Avalonia.PixelPoint((int)settings.ChooseWindowLeft, (int)settings.ChooseWindowTop);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        
        // Note: WindowState is set in Opened event because setting it in constructor 
        // doesn't work reliably in Avalonia
        
        BuildUI();
        
        this.Opened += OnWindowOpened;
        this.Closing += OnWindowClosing;
        this.KeyDown += OnKeyDown;
    }
    
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        
        // Handle Alt+key combinations for tab navigation (mnemonics)
        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            switch (e.Key)
            {
                case Key.A: // Apply filter - focus the filter textbox
                    _tbxFilter?.Focus();
                    e.Handled = true;
                    break;
                case Key.B: // _Books
                    _tabControl.SelectedIndex = 0;
                    e.Handled = true;
                    break;
                case Key.Q: // _Query
                    _tabControl.SelectedIndex = 1;
                    e.Handled = true;
                    break;
                case Key.V: // Fa_vorites
                    _tabControl.SelectedIndex = 2;
                    e.Handled = true;
                    break;
            }
        }
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        // Apply maximized state after window opens (doesn't work reliably in constructor)
        if (AppSettings.Instance.ChooseWindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
        
        // Restore last selected tab
        var lastTab = AppSettings.Instance.ChooseQueryTab;
        _tabControl.SelectedIndex = lastTab switch
        {
            "_Query" => 1,
            "Fa_vorites" => 2,
            _ => 0 // "_Books" or default
        };
        
        // If no root folder and only "New..." is in the list, automatically show folder picker
        if (string.IsNullOrEmpty(_rootFolder) || !Directory.Exists(_rootFolder))
        {
            await ShowFolderPickerAsync();
        }
        else if (_pdfMetadata.Count > 0)
        {
            await LoadBooksAsync();
        }
        else
        {
            await FillBooksTabAsync();
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Save window state
        var settings = AppSettings.Instance;
        settings.ChooseWindowMaximized = WindowState == WindowState.Maximized;
        
        // Only save position/size if not maximized
        if (WindowState != WindowState.Maximized)
        {
            settings.ChooseWindowWidth = Width;
            settings.ChooseWindowHeight = Height;
            settings.ChooseWindowLeft = Position.X;
            settings.ChooseWindowTop = Position.Y;
        }
        
        // Save selected tab
        if (_tabControl.SelectedItem is TabItem selectedTab)
        {
            settings.ChooseQueryTab = selectedTab.Header?.ToString() ?? "_Books";
        }
        
        settings.Save();
    }
    
    private void BuildUI()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        
        _tabControl = new TabControl();
        Grid.SetRow(_tabControl, 0);
        Grid.SetRowSpan(_tabControl, 2);
        
        // Style for tab headers - make them look like traditional tabs
        var tabItemStyle = new Style(x => x.OfType<TabItem>());
        tabItemStyle.Setters.Add(new Setter(TabItem.FontSizeProperty, 12.0));
        tabItemStyle.Setters.Add(new Setter(TabItem.PaddingProperty, new Thickness(12, 6)));
        tabItemStyle.Setters.Add(new Setter(TabItem.MarginProperty, new Thickness(2, 0, 0, 0)));
        tabItemStyle.Setters.Add(new Setter(TabItem.BackgroundProperty, Brushes.LightGray));
        tabItemStyle.Setters.Add(new Setter(TabItem.BorderBrushProperty, Brushes.Gray));
        tabItemStyle.Setters.Add(new Setter(TabItem.BorderThicknessProperty, new Thickness(1, 1, 1, 0)));
        tabItemStyle.Setters.Add(new Setter(TabItem.CornerRadiusProperty, new CornerRadius(4, 4, 0, 0)));
        _tabControl.Styles.Add(tabItemStyle);
        
        // Style for selected tab - make it stand out
        var selectedTabStyle = new Style(x => x.OfType<TabItem>().Class(":selected"));
        selectedTabStyle.Setters.Add(new Setter(TabItem.BackgroundProperty, Brushes.White));
        selectedTabStyle.Setters.Add(new Setter(TabItem.FontWeightProperty, FontWeight.SemiBold));
        _tabControl.Styles.Add(selectedTabStyle);
        
        // Books tab
        var booksTab = new TabItem { Header = "_Books" };
        booksTab.Content = BuildBooksTabContent();
        _tabControl.Items.Add(booksTab);
        
        // Query tab (moved to 2nd position)
        var queryTab = new TabItem { Header = "_Query" };
        queryTab.Content = BuildQueryTabContent();
        _tabControl.Items.Add(queryTab);
        
        // Favorites tab
        var favTab = new TabItem { Header = "Fa_vorites" };
        favTab.Content = BuildFavoritesTabContent();
        _tabControl.Items.Add(favTab);
        
        _tabControl.SelectionChanged += OnTabSelectionChanged;
        
        grid.Children.Add(_tabControl);
        
        // Top bar
        var topBar = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 10, 5)
        };
        Grid.SetRow(topBar, 0);
        
        _tbxTotals = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        topBar.Children.Add(_tbxTotals);
        
        topBar.Children.Add(new Label 
        { 
            Content = "Music Folder:", 
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0) 
        });
        _cboRootFolder = new ComboBox 
        { 
            Width = 300, 
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0) 
        };
        PopulateRootFolderComboBox();
        _cboRootFolder.SelectionChanged += OnRootFolderSelectionChanged;
        _cboRootFolder.DropDownOpened += OnRootFolderDropDownOpened;
        topBar.Children.Add(_cboRootFolder);
        
        var btnCancel = new Button 
        { 
            Content = "Cancel", 
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0) 
        };
        btnCancel.Click += (s, e) => Close();
        topBar.Children.Add(btnCancel);
        
        var btnOk = new Button 
        { 
            Content = "_OK", 
            Width = 50, 
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0) 
        };
        btnOk.Click += BtnOk_Click;
        topBar.Children.Add(btnOk);
        
        grid.Children.Add(topBar);
        
        Content = grid;
    }
    
    private void PopulateRootFolderComboBox()
    {
        _enableCboSelectionChange = false;
        _cboRootFolder.Items.Clear();
        
        if (!string.IsNullOrEmpty(_rootFolder))
        {
            _cboRootFolder.Items.Add(new ComboBoxItem { Content = _rootFolder });
        }
        
        var settings = AppSettings.Instance;
        foreach (var folder in settings.RootFolderMRU)
        {
            if (folder != _rootFolder && !string.IsNullOrEmpty(folder))
            {
                _cboRootFolder.Items.Add(new ComboBoxItem { Content = folder });
            }
        }
        
        _cboRootFolder.Items.Add(new ComboBoxItem { Content = NewFolderDialogString });
        
        if (_cboRootFolder.Items.Count > 0)
        {
            _cboRootFolder.SelectedIndex = 0;
        }
        _enableCboSelectionChange = true;
    }
    
    private void OnRootFolderDropDownOpened(object? sender, EventArgs e)
    {
        // If only "New..." is in the combo box, automatically show folder picker
        if (_cboRootFolder.Items.Count == 1)
        {
            _cboRootFolder.IsDropDownOpen = false;
            _ = ShowFolderPickerAsync();
        }
    }
    
    private async void OnRootFolderSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_enableCboSelectionChange)
            return;
            
        if (_cboRootFolder.SelectedItem is ComboBoxItem selectedItem)
        {
            var path = selectedItem.Content?.ToString();
            
            if (path == NewFolderDialogString)
            {
                await ShowFolderPickerAsync();
            }
            else if (!string.IsNullOrEmpty(path) && path != _rootFolder)
            {
                await ChangeRootFolderAsync(path);
            }
        }
    }
    
    private async Task ShowFolderPickerAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            // Reset to first valid folder if available, otherwise close
            if (_cboRootFolder.Items.Count > 1)
            {
                _enableCboSelectionChange = false;
                _cboRootFolder.SelectedIndex = 0;
                _enableCboSelectionChange = true;
            }
            else
            {
                Close();
            }
            return;
        }
        
        IStorageFolder? startLocation = null;
        if (!string.IsNullOrEmpty(_rootFolder) && Directory.Exists(_rootFolder))
        {
            try
            {
                startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(_rootFolder);
            }
            catch { }
        }
        
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a root folder with PDF music files",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });
        
        if (folders.Count > 0)
        {
            var selectedPath = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(selectedPath) && Directory.Exists(selectedPath))
            {
                // Update settings MRU
                var settings = AppSettings.Instance;
                settings.AddToMRU(selectedPath);
                settings.Save();
                
                // Re-populate combo box and change to new folder
                PopulateRootFolderComboBox();
                await ChangeRootFolderAsync(selectedPath);
                return;
            }
        }
        
        // User cancelled - if we have a valid folder to fall back to, use it; otherwise close
        _enableCboSelectionChange = false;
        if (!string.IsNullOrEmpty(_rootFolder) && Directory.Exists(_rootFolder))
        {
            // We have a valid root folder, just reset the combo selection
            _cboRootFolder.SelectedIndex = 0;
            _enableCboSelectionChange = true;
        }
        else if (_cboRootFolder.Items.Count > 1)
        {
            // Try the first item (not "New...")
            var firstItem = _cboRootFolder.Items[0] as ComboBoxItem;
            var firstPath = firstItem?.Content?.ToString();
            if (!string.IsNullOrEmpty(firstPath) && firstPath != NewFolderDialogString && Directory.Exists(firstPath))
            {
                _cboRootFolder.SelectedIndex = 0;
                _enableCboSelectionChange = true;
                await ChangeRootFolderAsync(firstPath);
            }
            else
            {
                _enableCboSelectionChange = true;
                Close();
            }
        }
        else
        {
            // No valid folders available, close the dialog
            _enableCboSelectionChange = true;
            Close();
        }
    }
    
    private async Task ChangeRootFolderAsync(string newRootFolder)
    {
        if (!Directory.Exists(newRootFolder))
        {
            return;
        }
        
        _rootFolder = newRootFolder;
        
        // Update settings
        var settings = AppSettings.Instance;
        settings.AddToMRU(newRootFolder);
        settings.Save();
        
        // Clear cached data
        _bookItemCache.Clear();
        _allFavoriteItems.Clear();
        _queryBrowseControl = null;
        _lbBooks.ItemsSource = null;
        _favoritesListBox.ItemsSource = null;
        
        _tbxTotals.Text = "Loading...";
        
        try
        {
            var provider = new PdfToImageDocumentProvider();
            (_pdfMetadata, _) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
                newRootFolder, 
                provider,
                useParallelLoading: true);
            
            await LoadBooksAsync();
        }
        catch (Exception ex)
        {
            _tbxTotals.Text = $"Error: {ex.Message}";
            Logger.LogException("Failed to load PDF metadata from folder", ex);
        }
    }
    
    private Control BuildBooksTabContent()
    {
        var booksGrid = new Grid();
        booksGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        booksGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        
        var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        
        _rbtnByDate = new RadioButton { Content = "ByDate", GroupName = "Sort", IsChecked = true, Margin = new Thickness(20, 5, 0, 0) };
        _rbtnByDate.IsCheckedChanged += OnSortChanged;
        filterPanel.Children.Add(_rbtnByDate);
        
        _rbtnByFolder = new RadioButton { Content = "ByFolder", GroupName = "Sort", Margin = new Thickness(20, 5, 0, 0) };
        _rbtnByFolder.IsCheckedChanged += OnSortChanged;
        filterPanel.Children.Add(_rbtnByFolder);
        
        _rbtnByNumPages = new RadioButton { Content = "ByNumPages", GroupName = "Sort", Margin = new Thickness(20, 5, 0, 0) };
        _rbtnByNumPages.IsCheckedChanged += OnSortChanged;
        filterPanel.Children.Add(_rbtnByNumPages);
        
        filterPanel.Children.Add(new Label { Content = "Filter", Margin = new Thickness(20, 0, 0, 0) });
        _tbxFilter = new TextBox { Width = 150, Margin = new Thickness(5, 0, 0, 0) };
        _tbxFilter.TextChanged += OnFilterChanged;
        filterPanel.Children.Add(_tbxFilter);
        
        Grid.SetRow(filterPanel, 0);
        booksGrid.Children.Add(filterPanel);
        
        _lbBooks = new ListBox();
        
        _lbBooks.ItemContainerTheme = new ControlTheme(typeof(ListBoxItem))
        {
            Setters =
            {
                new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                new Setter(ListBoxItem.MarginProperty, new Thickness(0)),
                new Setter(ListBoxItem.MinHeightProperty, 0.0),
            }
        };
        
        var wrapPanelFactory = new FuncTemplate<Panel?>(() => new WrapPanel
        {
            Orientation = Orientation.Horizontal
        });
        _lbBooks.ItemsPanel = wrapPanelFactory;
        // Note: DoubleTapped is now handled on individual items in CreateBookItemControl
        // to ensure selection is set before processing
        
        var scrollViewer = new ScrollViewer 
        { 
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _lbBooks
        };
        
        Grid.SetRow(scrollViewer, 1);
        booksGrid.Children.Add(scrollViewer);
        
        return booksGrid;
    }
    
    private Control BuildFavoritesTabContent()
    {
        var favGrid = new Grid();
        favGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        favGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        
        _favoritesStatus = new TextBlock { Margin = new Thickness(10, 5, 10, 5) };
        Grid.SetRow(_favoritesStatus, 0);
        favGrid.Children.Add(_favoritesStatus);
        
        _favoritesListBox = new ListBox();
        // Note: DoubleTapped is now handled on individual items in RefreshFavoritesDisplay
        // to ensure selection is set before processing
        
        Grid.SetRow(_favoritesListBox, 1);
        favGrid.Children.Add(_favoritesListBox);
        
        return favGrid;
    }
    
    private Control BuildQueryTabContent()
    {
        _queryTabGrid = new Grid();
        _queryTabGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        
        var placeholder = new TextBlock 
        { 
            Text = "Select this tab to load query data...", 
            Margin = new Thickness(20),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _queryTabGrid.Children.Add(placeholder);
        
        return _queryTabGrid;
    }
    
    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_tabControl.SelectedItem is TabItem selectedTab)
        {
            var header = selectedTab.Header?.ToString() ?? "";
            
            if (header == "Fa_vorites" && _allFavoriteItems.Count == 0)
            {
                FillFavoritesTab();
            }
            else if (header == "_Query")
            {
                if (_queryBrowseControl == null)
                {
                    FillQueryTab();
                }
                // Focus the filter textbox when Query tab is activated
                _queryBrowseControl?.FocusFilter();
            }
        }
    }
    
    private void FillFavoritesTab()
    {
        if (_pdfMetadata.Count == 0) return;
            
        _allFavoriteItems.Clear();
        
        foreach (var pdfMetaData in _pdfMetadata.OrderBy(p => p.GetBookName(_rootFolder)))
        {
            foreach (var fav in pdfMetaData.Favorites)
            {
                var description = GetDescription(pdfMetaData, fav.Pageno);
                var thumbnail = pdfMetaData.GetCachedThumbnail<Bitmap>();
                
                _allFavoriteItems.Add(new FavoriteItem
                {
                    Metadata = pdfMetaData,
                    Favorite = fav,
                    Description = description,
                    PageNo = fav.Pageno,
                    Thumbnail = thumbnail,
                    BookName = pdfMetaData.GetBookName(_rootFolder)
                });
            }
        }
        
        RefreshFavoritesDisplay();
    }
    
    private void RefreshFavoritesDisplay()
    {
        var items = new List<Control>();
        
        foreach (var favItem in _allFavoriteItems)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5), Tag = favItem };
            
            if (favItem.Thumbnail != null)
            {
                sp.Children.Add(new Image { Source = favItem.Thumbnail, Height = 60, Width = 40, Margin = new Thickness(0, 0, 10, 0) });
            }
            
            var textPanel = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(new TextBlock { Text = favItem.Description, FontWeight = FontWeight.Bold });
            textPanel.Children.Add(new TextBlock { Text = $"Page {favItem.PageNo} - {favItem.BookName}", Foreground = Brushes.Gray, FontSize = 11 });
            sp.Children.Add(textPanel);
            
            // Use shared DoubleTapHelper for more reliable double-tap detection
            sp.PointerPressed += (sender, e) =>
            {
                if (sender is StackPanel panel && panel.Tag is FavoriteItem item)
                {
                    if (_doubleTapHelper.IsDoubleTap(panel, e))
                    {
                        _favoritesListBox.SelectedItem = panel;
                        ChosenPdfMetaData = item.Metadata;
                        ChosenPageNo = item.PageNo;
                        Close();
                        e.Handled = true;
                    }
                }
            };
            
            items.Add(sp);
        }
        
        _favoritesListBox.ItemsSource = items;
        _favoritesStatus.Text = $"# Favorites = {_allFavoriteItems.Count}";
    }
    
    private void FillQueryTab()
    {
        if (_pdfMetadata.Count == 0) return;
        
        var uberToc = new List<Tuple<PdfMetaDataReadResult, TOCEntry>>();
        foreach (var pdfMetaDataItem in _pdfMetadata)
        {
            foreach (var tentry in pdfMetaDataItem.TocEntries)
            {
                uberToc.Add(Tuple.Create(pdfMetaDataItem, tentry));
            }
        }

        var query = from tup in uberToc
                    let itm = tup.Item2
                    orderby itm.SongName
                    select new
                    {
                        itm.SongName,
                        Page = itm.PageNo,
                        Vol = tup.Item1.GetVolNumFromPageNum(itm.PageNo),
                        itm.Composer,
                        CompositionDate = itm.Date,
                        Fav = tup.Item1.IsFavorite(itm.PageNo) ? "★" : string.Empty,
                        BookName = tup.Item1.GetBookName(_rootFolder),
                        itm.Notes,
                        LastModified = tup.Item1.LastWriteTime,
                        _Tup = tup
                    };

        _queryBrowseControl = new BrowseControl(query, colWidths: new[] { 250, 50, 40, 150, 80, 40, 300, 200, 150 });
        _queryBrowseControl.ListView.DoubleTapped += (s, e) => BtnOk_Click(s, e);
        
        _queryTabGrid.Children.Clear();
        Grid.SetRow(_queryBrowseControl, 0);
        _queryTabGrid.Children.Add(_queryBrowseControl);
    }
    
    private string GetDescription(PdfMetaDataReadResult metadata, int pageNo)
    {
        var tocEntry = metadata.TocEntries.FirstOrDefault(t => t.PageNo == pageNo);
        
        if (tocEntry == null)
        {
            tocEntry = metadata.TocEntries
                .Where(t => t.PageNo <= pageNo)
                .OrderByDescending(t => t.PageNo)
                .FirstOrDefault();
        }
        
        if (tocEntry != null)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(tocEntry.SongName)) parts.Add(tocEntry.SongName);
            if (!string.IsNullOrEmpty(tocEntry.Composer)) parts.Add(tocEntry.Composer);
            if (!string.IsNullOrEmpty(tocEntry.Date)) parts.Add(tocEntry.Date);
            return string.Join(" - ", parts);
        }
        
        return metadata.GetBookName(_rootFolder);
    }
    
    private void BtnOk_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_tabControl.SelectedItem is TabItem selectedTab)
        {
            var header = selectedTab.Header?.ToString() ?? "";
            
            switch (header)
            {
                case "_Books":
                    if (_lbBooks.SelectedItem is StackPanel sp && sp.Tag is BookItemCache cache)
                    {
                        ChosenPdfMetaData = cache.Metadata;
                        ChosenPageNo = cache.Metadata.LastPageNo;
                    }
                    break;
                    
                case "Fa_vorites":
                    if (_favoritesListBox.SelectedItem is StackPanel favSp && favSp.Tag is FavoriteItem favItem)
                    {
                        ChosenPdfMetaData = favItem.Metadata;
                        ChosenPageNo = favItem.PageNo;
                    }
                    break;
                    
                case "_Query":
                    if (_queryBrowseControl?.ListView?.SelectedItem != null)
                    {
                        var selectedItem = _queryBrowseControl.ListView.SelectedItem;
                        var tupProp = selectedItem.GetType().GetProperty("_Tup");
                        if (tupProp != null)
                        {
                            var tup = tupProp.GetValue(selectedItem) as Tuple<PdfMetaDataReadResult, TOCEntry>;
                            if (tup != null)
                            {
                                ChosenPdfMetaData = tup.Item1;
                                ChosenPageNo = tup.Item2.PageNo;
                            }
                        }
                    }
                    break;
            }
        }
        
        Close();
    }

    private void OnSortChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_isLoading && _bookItemCache.Count > 0)
        {
            RefreshBooksDisplay();
        }
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isLoading && _bookItemCache.Count > 0)
        {
            RefreshBooksDisplay();
        }
    }

    private IEnumerable<PdfMetaDataReadResult> GetSortedMetadata()
    {
        if (_rbtnByFolder?.IsChecked == true)
        {
            return _pdfMetadata.OrderBy(p => p.GetBookName(_rootFolder));
        }
        else if (_rbtnByNumPages?.IsChecked == true)
        {
            return _pdfMetadata.OrderByDescending(p => p.VolumeInfoList.Sum(v => v.NPagesInThisVolume));
        }
        else
        {
            return _pdfMetadata.OrderByDescending(p => p.LastWriteTime);
        }
    }

    private async Task LoadBooksAsync()
    {
        _isLoading = true;
        _bookItemCache.Clear();
        
        var random = new Random(42);
        int index = 0;
        int totalSongs = 0;
        int totalPages = 0;
        int totalFavs = 0;
        
        var sortedMetadata = GetSortedMetadata().ToList();
        
        foreach (var pdfMetaData in sortedMetadata)
        {
            var bookName = pdfMetaData.GetBookName(_rootFolder);
            var numSongs = pdfMetaData.TocEntries.Count;
            var numPages = pdfMetaData.VolumeInfoList.Sum(v => v.NPagesInThisVolume);
            var numFavs = pdfMetaData.Favorites.Count;
            
            totalSongs += numSongs;
            totalPages += numPages;
            totalFavs += numFavs;
            
            var localIndex = index;
            var localBookName = bookName;
            try
            {
                await pdfMetaData.GetOrCreateThumbnailAsync(async () =>
                {
                    try
                    {
                        return await GetPdfThumbnailAsync(pdfMetaData);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to get PDF thumbnail for {localBookName}: {ex.Message}");
                        return GenerateBookCoverBitmap(ThumbnailWidth, ThumbnailHeight, random, localBookName, localIndex);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to get PDF thumbnail for {bookName}: {ex.Message}");
                pdfMetaData.ThumbnailCache = GenerateBookCoverBitmap(ThumbnailWidth, ThumbnailHeight, random, bookName, index);
            }
            
            _bookItemCache.Add(new BookItemCache
            {
                Metadata = pdfMetaData,
                BookName = bookName,
                NumSongs = numSongs,
                NumPages = numPages,
                NumFavs = numFavs
            });
            
            if (index % 10 == 9)
            {
                UpdateBooksDisplayDuringLoad(totalSongs, totalPages, totalFavs);
                await Task.Delay(10);
            }
            
            index++;
        }
        
        _isLoading = false;
        RefreshBooksDisplay();
    }

    private void UpdateBooksDisplayDuringLoad(int totalSongs, int totalPages, int totalFavs)
    {
        var filterText = _tbxFilter?.Text?.Trim() ?? string.Empty;
        
        IEnumerable<BookItemCache> displayItems = _bookItemCache;
        if (!string.IsNullOrEmpty(filterText))
        {
            displayItems = displayItems.Where(item =>
                item.BookName.Contains(filterText, StringComparison.OrdinalIgnoreCase));
        }
        
        var items = new List<Control>();
        foreach (var cacheItem in displayItems)
        {
            items.Add(CreateBookItemControl(cacheItem));
        }
        
        _lbBooks.ItemsSource = items;
        _tbxTotals.Text = $"#Books = {_bookItemCache.Count} #Songs = {totalSongs:n0} #Pages = {totalPages:n0} #Fav={totalFavs:n0}";
    }

    private Control CreateBookItemControl(BookItemCache cacheItem)
    {
        var sp = new StackPanel { Orientation = Orientation.Vertical, Width = 150, Tag = cacheItem };
        
        var img = new Image
        {
            Source = cacheItem.Bitmap,
            Width = ThumbnailWidth,
            Height = ThumbnailHeight,
            Stretch = Stretch.UniformToFill
        };
        sp.Children.Add(img);
        
        sp.Children.Add(new TextBlock
        {
            Text = cacheItem.BookName,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 150,
            FontSize = 11
        });
        
        sp.Children.Add(new TextBlock
        {
            Text = $"#Sngs={cacheItem.NumSongs} Pg={cacheItem.NumPages} Fav={cacheItem.NumFavs}",
            FontSize = 10,
            Foreground = Brushes.Gray
        });
        
        // Use shared DoubleTapHelper for more reliable double-tap detection
        sp.PointerPressed += (sender, e) =>
        {
            if (sender is StackPanel panel && panel.Tag is BookItemCache cache)
            {
                if (_doubleTapHelper.IsDoubleTap(panel, e))
                {
                    _lbBooks.SelectedItem = panel;
                    ChosenPdfMetaData = cache.Metadata;
                    ChosenPageNo = cache.Metadata.LastPageNo;
                    Close();
                    e.Handled = true;
                }
            }
        };
        
        return sp;
    }
    
    private void RefreshBooksDisplay()
    {
        var filterText = _tbxFilter?.Text?.Trim() ?? string.Empty;
        
        IEnumerable<BookItemCache> filteredItems = _bookItemCache;
        if (!string.IsNullOrEmpty(filterText))
        {
            filteredItems = filteredItems.Where(item =>
                item.BookName.Contains(filterText, StringComparison.OrdinalIgnoreCase));
        }
        
        IEnumerable<BookItemCache> sortedItems;
        if (_rbtnByFolder?.IsChecked == true)
        {
            sortedItems = filteredItems.OrderBy(item => item.BookName);
        }
        else if (_rbtnByNumPages?.IsChecked == true)
        {
            sortedItems = filteredItems.OrderByDescending(item => item.NumPages);
        }
        else
        {
            sortedItems = filteredItems.OrderByDescending(item => item.Metadata.LastWriteTime);
        }
        
        var items = new List<Control>();
        int totalSongs = 0;
        int totalPages = 0;
        int totalFavs = 0;
        
        foreach (var cacheItem in sortedItems)
        {
            totalSongs += cacheItem.NumSongs;
            totalPages += cacheItem.NumPages;
            totalFavs += cacheItem.NumFavs;
            
            items.Add(CreateBookItemControl(cacheItem));
        }
        
        _lbBooks.ItemsSource = items;
        _tbxTotals.Text = $"#Books = {items.Count} #Songs = {totalSongs:n0} #Pages = {totalPages:n0} #Fav={totalFavs:n0}";
    }

    private async Task<Bitmap> GetPdfThumbnailAsync(PdfMetaDataReadResult pdfMetaData)
    {
        return await Task.Run(() =>
        {
            if (pdfMetaData.VolumeInfoList.Count == 0)
            {
                throw new InvalidOperationException($"No volumes in metadata");
            }
            
            var firstVolume = pdfMetaData.VolumeInfoList[0];
            var pdfPath = pdfMetaData.GetFullPathFileFromVolno(0);
            
            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
            {
                throw new FileNotFoundException($"PDF file not found: {pdfPath}");
            }
            
            if (SkipCloudOnlyFiles)
            {
                var fileInfo = new FileInfo(pdfPath);
                const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
                const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
                
                var attrs = fileInfo.Attributes;
                bool isCloudOnly = (attrs & RecallOnDataAccess) == RecallOnDataAccess ||
                                   (attrs & RecallOnOpen) == RecallOnOpen ||
                                   (attrs & FileAttributes.Offline) == FileAttributes.Offline;
                
                if (isCloudOnly)
                {
                    throw new IOException($"Cloud-only file, skipping: {pdfPath}");
                }
            }
            
            var rotation = firstVolume.Rotation;
            var pdfRotation = rotation switch
            {
                1 => PdfRotation.Rotate90,
                2 => PdfRotation.Rotate180,
                3 => PdfRotation.Rotate270,
                _ => PdfRotation.Rotate0
            };
            
            using var pdfStream = File.OpenRead(pdfPath);
            using var skBitmap = Conversion.ToImage(pdfStream, page: (Index)0, options: new PDFtoImage.RenderOptions(
                Width: ThumbnailWidth, 
                Height: ThumbnailHeight,
                Rotation: pdfRotation));
            
            using var data = skBitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Seek(0, SeekOrigin.Begin);
            
            return new Bitmap(stream);
        });
    }

    private async Task FillBooksTabAsync()
    {
        // Demo mode
        var random = new Random(42);
        var items = new List<Control>();
        
        var bookNames = new[]
        {
            "Classical Piano Vol 1", "Jazz Standards", "Pop Hits 2020", "Rock Classics",
            "Broadway Favorites", "Country Gold", "Blues Collection", "Folk Songs"
        };
        
        for (int i = 0; i < 20; i++)
        {
            var bookName = bookNames[i % bookNames.Length];
            var bitmap = GenerateBookCoverBitmap(ThumbnailWidth, ThumbnailHeight, random, bookName, i);
            
            var sp = new StackPanel { Orientation = Orientation.Vertical, Width = 150 };
            
            sp.Children.Add(new Image { Source = bitmap, Width = ThumbnailWidth, Height = ThumbnailHeight, Stretch = Stretch.UniformToFill });
            sp.Children.Add(new TextBlock { Text = bookName, TextWrapping = TextWrapping.Wrap, MaxWidth = 150, FontSize = 11 });
            sp.Children.Add(new TextBlock { Text = $"#Sngs={random.Next(10, 100)} Pg={random.Next(20, 500)}", FontSize = 10, Foreground = Brushes.Gray });
            
            items.Add(sp);
        }
        
        _lbBooks.ItemsSource = items;
        _tbxTotals.Text = $"#Books = {items.Count} (demo mode)";
    }

    private Bitmap GenerateBookCoverBitmap(int width, int height, Random random, string title, int index)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        
        var color1 = SKColor.FromHsv(random.Next(360), random.Next(60, 100), random.Next(70, 100));
        var color2 = SKColor.FromHsv((random.Next(360) + 180) % 360, random.Next(60, 100), random.Next(40, 70));
        
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            new[] { color1, color2 },
            SKShaderTileMode.Clamp);
        
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawRect(0, 0, width, height, paint);
        
        using var font = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 14);
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        
        canvas.DrawText(title, width / 2f, height - 30, SKTextAlign.Center, font, textPaint);
        
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        
        return new Bitmap(stream);
    }
    
    // Double-tap detection state for more sensitive handling
    private DateTime _lastTapTime = DateTime.MinValue;
    private Point _lastTapPosition;
    private object? _lastTapTarget;
    private const int DoubleTapTimeThresholdMs = 500; // More generous than Avalonia's default
    private const double DoubleTapDistanceThreshold = 50; // More generous distance threshold

    /// <summary>
    /// Helper method to simulate double-tap detection with customizable thresholds.
    /// Use this instead of Avalonia's built-in DoubleTapped event for more sensitive scenarios.
    /// </summary>
    /// <param name="tapTime">The time of the tap.</param>
    /// <param name="tapPosition">The position of the tap.</param>
    /// <param name="tapTarget">The target element of the tap.</param>
    /// <param name="onDoubleTap">The action to perform on double-tap.</param>
    /// <param name="timeThresholdMs">The time threshold for double-tap detection (in milliseconds).</param>
    /// <param name="distanceThreshold">The distance threshold for double-tap detection.</param>
    public void HandleTap(
        DateTime tapTime, 
        Point tapPosition, 
        object tapTarget,
        Action onDoubleTap,
        int timeThresholdMs = DoubleTapTimeThresholdMs,
        double distanceThreshold = DoubleTapDistanceThreshold)
    {
        if (_lastTapTarget == tapTarget && (tapTime - _lastTapTime).TotalMilliseconds <= timeThresholdMs)
        {
            // Potential double-tap detected, check distance
            var distance = Math.Sqrt(Math.Pow(tapPosition.X - _lastTapPosition.X, 2) + 
                                     Math.Pow(tapPosition.Y - _lastTapPosition.Y, 2));
            
            if (distance <= distanceThreshold)
            {
                // Confirmed double-tap
                onDoubleTap();
            }
        }
        
        // Update last tap info
        _lastTapTime = tapTime;
        _lastTapPosition = tapPosition;
        _lastTapTarget = tapTarget;
    }
}

/// <summary>
/// PDF document provider using PDFtoImage for cross-platform support
/// </summary>
public class PdfToImageDocumentProvider : IPdfDocumentProvider
{
    public Task<int> GetPageCountAsync(string pdfFilePath)
    {
        return Task.Run(() =>
        {
            try
            {
                var fileInfo = new FileInfo(pdfFilePath);
                const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
                const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
                
                var attrs = fileInfo.Attributes;
                bool isCloudOnly = (attrs & RecallOnDataAccess) == RecallOnDataAccess ||
                                   (attrs & RecallOnOpen) == RecallOnOpen ||
                                   (attrs & FileAttributes.Offline) == FileAttributes.Offline;
                
                if (isCloudOnly)
                {
                    return 0;
                }
                
                // Use byte array overload to avoid base64 detection issues
                var pdfBytes = File.ReadAllBytes(pdfFilePath);
                return Conversion.GetPageCount(pdfBytes);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"PdfToImageDocumentProvider: Error getting page count for {Path.GetFileName(pdfFilePath)}: {ex.Message}");
                return 0;
            }
        });
    }
}
