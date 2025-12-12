using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using PDFtoImage;
using SheetMusicLib;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaTests;

/// <summary>
/// ChooseMusic-style window with book cover bitmaps from actual PDFs.
/// Reusable control that can be used in tests or as part of the application.
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
    
    // Query tab
    private ListBox _queryListBox;
    private TextBox _queryFilterTextBox;
    private TextBlock _queryStatus;
    
    private List<PdfMetaDataReadResult> _pdfMetadata;
    private string _rootFolder;
    
    // Cache for book items (bitmap + metadata) to avoid re-rendering on filter/sort
    private List<BookItemCache> _bookItemCache = new();
    private bool _isLoading = false;
    
    // Query data source
    private List<QueryItem> _allQueryItems = new();
    
    // Favorites data source
    private List<FavoriteItem> _allFavoriteItems = new();

    private const int ThumbnailWidth = 150;
    private const int ThumbnailHeight = 225;

    /// <summary>
    /// If true, skip cloud-only files instead of triggering download for thumbnails.
    /// Set to false to allow on-demand downloading of cloud files.
    /// </summary>
    public bool SkipCloudOnlyFiles { get; set; } = true;
    
    /// <summary>
    /// The selected PDF metadata (set when user clicks OK)
    /// </summary>
    public PdfMetaDataReadResult ChosenPdfMetaData { get; private set; }
    
    /// <summary>
    /// The selected page number (for favorites/query selection)
    /// </summary>
    public int ChosenPageNo { get; private set; }

    /// <summary>
    /// Cache entry for a book item - stores computed values to avoid recalculation on filter/sort.
    /// The bitmap is stored on PdfMetaDataReadResult.ThumbnailCache for cross-component reuse.
    /// </summary>
    private class BookItemCache
    {
        public PdfMetaDataReadResult Metadata { get; set; }
        public string BookName { get; set; }
        public int NumSongs { get; set; }
        public int NumPages { get; set; }
        public int NumFavs { get; set; }
        
        /// <summary>
        /// Gets the cached bitmap from the metadata object
        /// </summary>
        public Bitmap Bitmap => Metadata?.GetCachedThumbnail<Bitmap>();
    }
    
    /// <summary>
    /// Query item for the browse list - represents a song from the TOC
    /// </summary>
    public class QueryItem
    {
        public string SongName { get; set; }
        public int Page { get; set; }
        public int Vol { get; set; }
        public string Composer { get; set; }
        public string CompositionDate { get; set; }
        public string Fav { get; set; }
        public string BookName { get; set; }
        public string Notes { get; set; }
        public DateTime LastModified { get; set; }
        
        // Hidden reference to navigate
        public PdfMetaDataReadResult Metadata { get; set; }
        public TOCEntry TocEntry { get; set; }
    }
    
    /// <summary>
    /// Favorite item for the favorites list
    /// </summary>
    private class FavoriteItem
    {
        public PdfMetaDataReadResult Metadata { get; set; }
        public Favorite Favorite { get; set; }
        public string Description { get; set; }
        public int PageNo { get; set; }
        public Bitmap Thumbnail { get; set; }
        public string BookName { get; set; }
    }

    public ChooseMusicWindow() : this(null, null)
    {
    }

    public ChooseMusicWindow(List<PdfMetaDataReadResult> pdfMetadata, string rootFolder)
    {
        _pdfMetadata = pdfMetadata;
        _rootFolder = rootFolder;
        
        Title = "Choose Music - Avalonia Test";
        Width = 1200;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        
        BuildUI();
        
        // Generate books after window is loaded
        this.Opened += async (s, e) =>
        {
            if (_pdfMetadata != null && _pdfMetadata.Count > 0)
            {
                await LoadBooksAsync();
            }
            else
            {
                await FillBooksTabAsync();
            }
        };
    }

    private void BuildUI()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        
        // Tab control for Books, Favorites, Query, Playlists
        _tabControl = new TabControl();
        Grid.SetRow(_tabControl, 0);
        Grid.SetRowSpan(_tabControl, 2);
        
        // Books tab
        var booksTab = new TabItem { Header = "_Books" };
        booksTab.Content = BuildBooksTabContent();
        _tabControl.Items.Add(booksTab);
        
        // Favorites tab
        var favTab = new TabItem { Header = "Fa_vorites" };
        favTab.Content = BuildFavoritesTabContent();
        _tabControl.Items.Add(favTab);
        
        // Query tab
        var queryTab = new TabItem { Header = "_Query" };
        queryTab.Content = BuildQueryTabContent();
        _tabControl.Items.Add(queryTab);
        
        // Playlists tab (placeholder)
        var playlistsTab = new TabItem { Header = "_Playlists", Content = new TextBlock { Text = "Playlists go here", Margin = new Thickness(20) } };
        _tabControl.Items.Add(playlistsTab);
        
        // Handle tab selection to lazy-load content
        _tabControl.SelectionChanged += OnTabSelectionChanged;
        
        grid.Children.Add(_tabControl);
        
        // Top bar with totals and controls
        var topBar = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 5, 10, 5)
        };
        Grid.SetRow(topBar, 0);
        
        _tbxTotals = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        topBar.Children.Add(_tbxTotals);
        
        topBar.Children.Add(new Label { Content = "Music Folder Path:", Margin = new Thickness(20, 0, 0, 0) });
        _cboRootFolder = new ComboBox { Width = 300, Margin = new Thickness(10, 0, 10, 0) };
        _cboRootFolder.Items.Add("C:\\Users\\Music\\SheetMusic");
        _cboRootFolder.Items.Add("D:\\Music\\PDFs");
        _cboRootFolder.SelectedIndex = 0;
        topBar.Children.Add(_cboRootFolder);
        
        var btnCancel = new Button { Content = "Cancel", Margin = new Thickness(10, 0, 0, 0) };
        btnCancel.Click += (s, e) => Close();
        topBar.Children.Add(btnCancel);
        
        var btnOk = new Button { Content = "_OK", Width = 50, Margin = new Thickness(10, 0, 10, 0) };
        btnOk.Click += BtnOk_Click;
        topBar.Children.Add(btnOk);
        
        grid.Children.Add(topBar);
        
        Content = grid;
    }
    
    private Control BuildBooksTabContent()
    {
        var booksGrid = new Grid();
        booksGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        booksGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        
        // Filter bar
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
        
        // Books list with wrap panel
        _lbBooks = new ListBox();
        
        // Remove default ListBox item padding
        _lbBooks.ItemContainerTheme = new ControlTheme(typeof(ListBoxItem))
        {
            Setters =
            {
                new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                new Setter(ListBoxItem.MarginProperty, new Thickness(0)),
                new Setter(ListBoxItem.MinHeightProperty, 0.0),
            }
        };
        
        // Create a WrapPanel as the items panel
        var wrapPanelFactory = new FuncTemplate<Panel?>(() => new WrapPanel
        {
            Orientation = Orientation.Horizontal
        });
        _lbBooks.ItemsPanel = wrapPanelFactory;
        _lbBooks.DoubleTapped += (s, e) => BtnOk_Click(s, e);
        
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
        
        // Status bar
        _favoritesStatus = new TextBlock { Margin = new Thickness(10, 5, 10, 5) };
        Grid.SetRow(_favoritesStatus, 0);
        favGrid.Children.Add(_favoritesStatus);
        
        // Favorites list
        _favoritesListBox = new ListBox();
        _favoritesListBox.DoubleTapped += (s, e) => BtnOk_Click(s, e);
        
        Grid.SetRow(_favoritesListBox, 1);
        favGrid.Children.Add(_favoritesListBox);
        
        return favGrid;
    }
    
    private Control BuildQueryTabContent()
    {
        var queryGrid = new Grid();
        queryGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        queryGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        
        // Filter bar for query
        var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        _queryStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 20, 0) };
        filterPanel.Children.Add(_queryStatus);
        filterPanel.Children.Add(new Label { Content = "Filter:", Margin = new Thickness(5, 0, 0, 0) });
        _queryFilterTextBox = new TextBox { Width = 200, Margin = new Thickness(5, 0, 0, 0) };
        _queryFilterTextBox.TextChanged += OnQueryFilterChanged;
        filterPanel.Children.Add(_queryFilterTextBox);
        
        Grid.SetRow(filterPanel, 0);
        queryGrid.Children.Add(filterPanel);
        
        // Browse-style ListBox for songs
        _queryListBox = new ListBox();
        _queryListBox.DoubleTapped += (s, e) => BtnOk_Click(s, e);
        
        Grid.SetRow(_queryListBox, 1);
        queryGrid.Children.Add(_queryListBox);
        
        return queryGrid;
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
            else if (header == "_Query" && _allQueryItems.Count == 0)
            {
                FillQueryTab();
            }
        }
    }
    
    private void FillFavoritesTab()
    {
        if (_pdfMetadata == null || _pdfMetadata.Count == 0)
            return;
            
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
            
            items.Add(sp);
        }
        
        _favoritesListBox.ItemsSource = items;
        _favoritesStatus.Text = $"# Favorites = {_allFavoriteItems.Count}";
    }
    
    private void FillQueryTab()
    {
        if (_pdfMetadata == null || _pdfMetadata.Count == 0)
            return;
            
        _allQueryItems.Clear();
        
        foreach (var pdfMetaData in _pdfMetadata)
        {
            foreach (var tocEntry in pdfMetaData.TocEntries)
            {
                _allQueryItems.Add(new QueryItem
                {
                    SongName = tocEntry.SongName ?? "",
                    Page = tocEntry.PageNo,
                    Vol = pdfMetaData.GetVolNumFromPageNum(tocEntry.PageNo),
                    Composer = tocEntry.Composer ?? "",
                    CompositionDate = tocEntry.Date ?? "",
                    Fav = pdfMetaData.IsFavorite(tocEntry.PageNo) ? "?" : "",
                    BookName = pdfMetaData.GetBookName(_rootFolder),
                    Notes = tocEntry.Notes ?? "",
                    LastModified = pdfMetaData.LastWriteTime,
                    Metadata = pdfMetaData,
                    TocEntry = tocEntry
                });
            }
        }
        
        RefreshQueryDisplay();
    }
    
    private void RefreshQueryDisplay(string filterText = null)
    {
        filterText = filterText?.Trim() ?? "";
        
        IEnumerable<QueryItem> displayItems = _allQueryItems;
        
        if (!string.IsNullOrEmpty(filterText))
        {
            displayItems = displayItems.Where(q =>
                q.SongName.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                q.Composer.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                q.BookName.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                q.Notes.Contains(filterText, StringComparison.OrdinalIgnoreCase));
        }
        
        var sortedItems = displayItems.OrderBy(q => q.SongName).ToList();
        
        var items = new List<Control>();
        
        foreach (var queryItem in sortedItems)
        {
            // Create a browse-style row with columns
            var rowGrid = new Grid { Margin = new Thickness(2), Tag = queryItem };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(280))); // Song Name
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(50)));  // Page
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(35)));  // Vol
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(120))); // Composer
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(60)));  // Date
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(30)));  // Fav
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(200))); // Book
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star))); // Notes
            
            var songText = new TextBlock { Text = queryItem.SongName, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(songText, 0);
            rowGrid.Children.Add(songText);
            
            var pageText = new TextBlock { Text = queryItem.Page.ToString(), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pageText, 1);
            rowGrid.Children.Add(pageText);
            
            var volText = new TextBlock { Text = queryItem.Vol.ToString(), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(volText, 2);
            rowGrid.Children.Add(volText);
            
            var composerText = new TextBlock { Text = queryItem.Composer, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(composerText, 3);
            rowGrid.Children.Add(composerText);
            
            var dateText = new TextBlock { Text = queryItem.CompositionDate, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(dateText, 4);
            rowGrid.Children.Add(dateText);
            
            var favText = new TextBlock { Text = queryItem.Fav, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(favText, 5);
            rowGrid.Children.Add(favText);
            
            var bookText = new TextBlock { Text = queryItem.BookName, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bookText, 6);
            rowGrid.Children.Add(bookText);
            
            var notesText = new TextBlock { Text = queryItem.Notes, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(notesText, 7);
            rowGrid.Children.Add(notesText);
            
            items.Add(rowGrid);
        }
        
        // Add header row
        var headerItems = new List<Control>();
        var headerGrid = new Grid { Margin = new Thickness(2), Background = Brushes.LightGray };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(280)));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(50)));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(35)));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(120)));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(60)));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(30)));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(200)));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        
        var headers = new[] { "Song Name", "Page", "Vol", "Composer", "Date", "Fav", "Book", "Notes" };
        for (int i = 0; i < headers.Length; i++)
        {
            var headerText = new TextBlock { Text = headers[i], FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(headerText, i);
            headerGrid.Children.Add(headerText);
        }
        
        headerItems.Add(headerGrid);
        headerItems.AddRange(items);
        
        _queryListBox.ItemsSource = headerItems;
        _queryStatus.Text = $"# Songs = {sortedItems.Count:n0}";
    }
    
    private void OnQueryFilterChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshQueryDisplay(_queryFilterTextBox?.Text);
    }
    
    /// <summary>
    /// Get description for a page (similar to WPF GetDescription)
    /// </summary>
    private string GetDescription(PdfMetaDataReadResult metadata, int pageNo)
    {
        // Find TOC entry for this page or nearest one before it
        var tocEntry = metadata.TocEntries.FirstOrDefault(t => t.PageNo == pageNo);
        
        if (tocEntry == null)
        {
            // Find the nearest TOC entry before this page
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
                    else if (_lbBooks.SelectedIndex >= 0 && _lbBooks.SelectedIndex < _bookItemCache.Count)
                    {
                        var selectedCache = _bookItemCache.ElementAtOrDefault(_lbBooks.SelectedIndex);
                        if (selectedCache != null)
                        {
                            ChosenPdfMetaData = selectedCache.Metadata;
                            ChosenPageNo = selectedCache.Metadata.LastPageNo;
                        }
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
                    if (_queryListBox.SelectedItem is Grid queryGrid && queryGrid.Tag is QueryItem queryItem)
                    {
                        ChosenPdfMetaData = queryItem.Metadata;
                        ChosenPageNo = queryItem.Page;
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

    /// <summary>
    /// Get the sorted metadata based on current sort selection
    /// </summary>
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

    /// <summary>
    /// Load all books and cache their bitmaps.
    /// </summary>
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
                        Trace.WriteLine($"Failed to get PDF thumbnail for {localBookName}: {ex.Message}");
                        return GenerateBookCoverBitmap(ThumbnailWidth, ThumbnailHeight, random, localBookName, localIndex);
                    }
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to get PDF thumbnail for {bookName}: {ex.Message}");
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
        _tbxTotals.Text = $"Total #Books = {_bookItemCache.Count} # Songs = {totalSongs:n0} # Pages = {totalPages:n0} #Fav={totalFavs:n0}";
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
        _tbxTotals.Text = $"Total #Books = {items.Count} # Songs = {totalSongs:n0} # Pages = {totalPages:n0} #Fav={totalFavs:n0}";
    }

    private async Task<Bitmap> GetPdfThumbnailAsync(PdfMetaDataReadResult pdfMetaData)
    {
        return await Task.Run(() =>
        {
            if (pdfMetaData.VolumeInfoList == null || pdfMetaData.VolumeInfoList.Count == 0)
            {
                throw new InvalidOperationException($"No volumes in metadata for: {pdfMetaData.FullPathFile}");
            }
            
            var firstVolume = pdfMetaData.VolumeInfoList[0];
            if (string.IsNullOrEmpty(firstVolume.FileNameVolume))
            {
                throw new InvalidOperationException($"Empty FileNameVolume for: {pdfMetaData.FullPathFile}");
            }
            
            var pdfPath = pdfMetaData.GetFullPathFileFromVolno(0);
            
            if (string.IsNullOrEmpty(pdfPath))
            {
                throw new InvalidOperationException($"GetFullPathFileFromVolno returned empty for: {pdfMetaData.FullPathFile}");
            }
            
            if (!File.Exists(pdfPath))
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
                
                if (!isCloudOnly && (attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint && fileInfo.Length < 1024)
                {
                    isCloudOnly = true;
                }
                
                if (isCloudOnly)
                {
                    throw new IOException($"Cloud-only file, skipping to avoid download: {pdfPath}");
                }
            }
            
            var rotation = firstVolume.Rotation;
            var pdfRotation = rotation switch
            {
                1 => PDFtoImage.PdfRotation.Rotate90,
                2 => PDFtoImage.PdfRotation.Rotate180,
                3 => PDFtoImage.PdfRotation.Rotate270,
                _ => PDFtoImage.PdfRotation.Rotate0
            };
            
            using var pdfStream = File.OpenRead(pdfPath);
            using var skBitmap = Conversion.ToImage(pdfStream, page: 0, options: new PDFtoImage.RenderOptions(
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
        // Demo mode with generated data
        var random = new Random(42);
        var items = new List<Control>();
        
        var bookNames = new[]
        {
            "Classical Piano Vol 1", "Jazz Standards", "Pop Hits 2020", "Rock Classics",
            "Broadway Favorites", "Country Gold", "Blues Collection", "Folk Songs",
            "Movie Themes", "Video Game Music", "Christmas Carols", "Gospel Hymns",
            "Opera Arias", "Chamber Music", "Symphonies", "Concertos",
            "Sonatas", "Etudes", "Preludes", "Fugues"
        };
        
        for (int i = 0; i < 20; i++)
        {
            var bookName = bookNames[i % bookNames.Length];
            var bitmap = GenerateBookCoverBitmap(ThumbnailWidth, ThumbnailHeight, random, bookName, i);
            
            var sp = new StackPanel { Orientation = Orientation.Vertical, Width = 150 };
            
            var img = new Image
            {
                Source = bitmap,
                Width = ThumbnailWidth,
                Height = ThumbnailHeight,
                Stretch = Stretch.UniformToFill
            };
            sp.Children.Add(img);
            
            sp.Children.Add(new TextBlock
            {
                Text = bookName,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 150,
                FontSize = 11
            });
            
            var numSongs = random.Next(10, 100);
            var numPages = random.Next(20, 500);
            var numFavs = random.Next(0, 20);
            
            sp.Children.Add(new TextBlock
            {
                Text = $"#Sngs={numSongs} Pg={numPages} Fav={numFavs}",
                FontSize = 10,
                Foreground = Brushes.Gray
            });
            
            items.Add(sp);
            
            if (i % 5 == 4)
            {
                _lbBooks.ItemsSource = null;
                _lbBooks.ItemsSource = new List<Control>(items);
                _tbxTotals.Text = $"Total #Books = {items.Count}";
                await Task.Delay(10);
            }
        }
        
        _lbBooks.ItemsSource = items;
        _tbxTotals.Text = $"Total #Books = {items.Count}";
    }

    private Bitmap GenerateBookCoverBitmap(int width, int height, Random random, string title, int index)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        
        // Generate a nice gradient background
        var color1 = SKColor.FromHsv(random.Next(360), random.Next(60, 100), random.Next(70, 100));
        var color2 = SKColor.FromHsv((random.Next(360) + 180) % 360, random.Next(60, 100), random.Next(40, 70));
        
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            new[] { color1, color2 },
            SKShaderTileMode.Clamp);
        
        using var paint = new SKPaint
        {
            Shader = shader,
            IsAntialias = true
        };
        canvas.DrawRect(0, 0, width, height, paint);
        
        // Add title text at bottom with word wrapping
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 14,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            TextAlign = SKTextAlign.Center
        };
        
        // Add shadow for text
        using var shadowPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(150),
            IsAntialias = true,
            TextSize = 14,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            TextAlign = SKTextAlign.Center
        };
        
        var yPos = height - 30;
        
        // Word wrap the title
        var words = title.Split(' ');
        var currentLine = "";
        foreach (var word in words)
        {
            var testLine = currentLine.Length > 0 ? currentLine + " " + word : word;
            var lineWidth = textPaint.MeasureText(testLine);
            
            if (lineWidth > width - 20 && currentLine.Length > 0)
            {
                canvas.DrawText(currentLine, width / 2 + 1, yPos + 1, shadowPaint);
                canvas.DrawText(currentLine, width / 2, yPos, textPaint);
                currentLine = word;
                yPos += 18;
            }
            else
            {
                currentLine = testLine;
            }
        }
        
        if (currentLine.Length > 0)
        {
            canvas.DrawText(currentLine, width / 2 + 1, yPos + 1, shadowPaint);
            canvas.DrawText(currentLine, width / 2, yPos, textPaint);
        }
        
        // Convert to Avalonia bitmap
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        
        return new Bitmap(stream);
    }
}

/// <summary>
/// Test application for ChooseMusic dialog.
/// Used in tests to host the ChooseMusicWindow.
/// </summary>
public class TestChooseMusicApp : Avalonia.Application
{
    public static Func<Avalonia.Application, IClassicDesktopStyleApplicationLifetime, Task>? OnSetupWindow;
    
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
    
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OnSetupWindow != null)
            {
                _ = OnSetupWindow.Invoke(this, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
