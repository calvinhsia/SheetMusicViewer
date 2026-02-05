using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SheetMusicLib;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SheetMusicViewer.Desktop;

public partial class MetaDataFormWindow : Window
{
    private MetaDataFormViewModel? _viewModel;
    private DataGrid? _tocDataGrid;
    private DataGrid? _favoritesDataGrid;
    private readonly DoubleTapHelper _doubleTapHelper = new();
    
    /// <summary>
    /// Page number to navigate to after closing (set when user double-clicks a TOC entry or favorite)
    /// </summary>
    public int? PageNumberResult { get; private set; }
    
    /// <summary>
    /// True if user clicked Save, false if Cancel
    /// </summary>
    public bool WasSaved { get; private set; }

    public MetaDataFormWindow()
    {
        InitializeComponent();
        ApplyWindowSettings();
    }

    public MetaDataFormWindow(MetaDataFormViewModel viewModel)
    {
        InitializeComponent();
        ApplyWindowSettings();
        
        _viewModel = viewModel;
        DataContext = viewModel;
        _tocDataGrid = this.FindControl<DataGrid>("TocDataGrid");
        _favoritesDataGrid = this.FindControl<DataGrid>("FavoritesDataGrid");
        
        // Wire up custom double-tap handling for better touch sensitivity
        if (_tocDataGrid != null)
        {
            _tocDataGrid.PointerPressed += TocDataGrid_PointerPressed;
        }
        if (_favoritesDataGrid != null)
        {
            _favoritesDataGrid.PointerPressed += FavoritesDataGrid_PointerPressed;
        }
        
        viewModel.CloseAction = (saved) =>
        {
            WasSaved = saved;
            Close();
        };
        viewModel.CloseWithPageAction = (pageNo) =>
        {
            PageNumberResult = pageNo;
            WasSaved = true;
            Close();
        };
        viewModel.GetClipboardFunc = () => Clipboard;
    }
    
    private void ApplyWindowSettings()
    {
        // Restore window size/position from settings
        var settings = AppSettings.Instance;
        Width = settings.MetaDataWindowWidth > 0 ? settings.MetaDataWindowWidth : 1200;
        Height = settings.MetaDataWindowHeight > 0 ? settings.MetaDataWindowHeight : 700;
        
        if (settings.MetaDataWindowLeft >= 0 && settings.MetaDataWindowTop >= 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new Avalonia.PixelPoint((int)settings.MetaDataWindowLeft, (int)settings.MetaDataWindowTop);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        
        // Note: WindowState is set in Opened event because setting it in constructor 
        // doesn't work reliably in Avalonia
        this.Opened += (s, e) =>
        {
            if (AppSettings.Instance.MetaDataWindowMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        };
        
        this.Closing += OnWindowClosing;
        this.KeyDown += OnKeyDown;
    }
    
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            // Commit any pending DataGrid edits before checking dirty state
            _tocDataGrid?.CommitEdit();
            await TryCloseAsync();
        }
    }
    
    private async Task TryCloseAsync()
    {
        if (_viewModel?.IsDirty == true)
        {
            // Show confirmation dialog
            var dialog = new Window
            {
                Title = "Unsaved Changes",
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };
            
            var result = false;
            var save = false;
            
            var panel = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 20
            };
            
            panel.Children.Add(new TextBlock
            {
                Text = "You have unsaved changes. Do you want to save before closing?",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
            
            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 10
            };
            
            var saveButton = new Button { Content = "_Save" };
            saveButton.Click += (s, e) => { result = true; save = true; dialog.Close(); };
            
            var discardButton = new Button { Content = "_Discard" };
            discardButton.Click += (s, e) => { result = true; save = false; dialog.Close(); };
            
            var cancelButton = new Button { Content = "_Cancel" };
            cancelButton.Click += (s, e) => { result = false; dialog.Close(); };
            
            buttonPanel.Children.Add(saveButton);
            buttonPanel.Children.Add(discardButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(buttonPanel);
            
            dialog.Content = panel;
            
            await dialog.ShowDialog(this);
            
            if (!result)
            {
                return; // User cancelled
            }
            
            if (save)
            {
                _viewModel.SaveCommand.Execute(null);
                return;
            }
        }
        
        // Close without saving
        WasSaved = false;
        Close();
    }
    
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Save window state
        var settings = AppSettings.Instance;
        settings.MetaDataWindowMaximized = WindowState == WindowState.Maximized;
        
        // Only save position/size if not maximized
        if (WindowState != WindowState.Maximized)
        {
            settings.MetaDataWindowWidth = Width;
            settings.MetaDataWindowHeight = Height;
            settings.MetaDataWindowLeft = Position.X;
            settings.MetaDataWindowTop = Position.Y;
        }
        
        settings.Save();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    private void TocDataGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_doubleTapHelper.IsDoubleTap(sender, e))
        {
            if (_viewModel?.SelectedItem != null)
            {
                PageNumberResult = _viewModel.SelectedItem.PageNo;
                WasSaved = true;
                Close();
                e.Handled = true;
            }
        }
    }
    
    private void FavoritesDataGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_doubleTapHelper.IsDoubleTap(sender, e))
        {
            if (_favoritesDataGrid?.SelectedItem is FavoriteViewModel fav)
            {
                PageNumberResult = fav.PageNo;
                WasSaved = true;
                Close();
                e.Handled = true;
            }
        }
    }
    
    // Keep the existing DoubleTapped handlers as fallback for mouse double-click
    private void TocDataGrid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedItem != null)
        {
            PageNumberResult = _viewModel.SelectedItem.PageNo;
            WasSaved = true;
            Close();
        }
    }
    
    private void FavoritesDataGrid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        var dataGrid = sender as DataGrid;
        if (dataGrid?.SelectedItem is FavoriteViewModel fav)
        {
            PageNumberResult = fav.PageNo;
            WasSaved = true;
            Close();
        }
    }
}

/// <summary>
/// ViewModel for editing PDF metadata (TOC entries, favorites, etc.)
/// </summary>
public partial class MetaDataFormViewModel : ObservableObject
{
    private readonly PdfMetaDataReadResult? _pdfMetaData;
    private readonly string _rootFolder;

    // Original values to track dirty state
    private int _originalPageNumberOffset;
    private string? _originalDocNotes;
    private List<(int PageNo, string? SongName, string? Composer, string? Date, string? Notes, string? Link)> _originalTocEntries = new();

    [ObservableProperty]
    private int _pageNumberOffset;

    [ObservableProperty]
    private string? _docNotes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedItemHasLink))]
    private TocEntryViewModel? _selectedItem;

    /// <summary>
    /// Whether the selected item has a link - used for binding Open button.
    /// Updated when SelectedItem changes or when SelectedItem.Link changes.
    /// </summary>
    public bool SelectedItemHasLink => SelectedItem?.HasLink ?? false;

    /// <summary>
    /// Called when SelectedItem changes - subscribe to property changes on the new item
    /// </summary>
    partial void OnSelectedItemChanged(TocEntryViewModel? oldValue, TocEntryViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= SelectedItem_PropertyChanged;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged += SelectedItem_PropertyChanged;
        }
    }

    private void SelectedItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // When the Link property changes on the selected item, notify that SelectedItemHasLink changed
        if (e.PropertyName == nameof(TocEntryViewModel.Link) || e.PropertyName == nameof(TocEntryViewModel.HasLink))
        {
            OnPropertyChanged(nameof(SelectedItemHasLink));
        }
    }

    [ObservableProperty]
    private ObservableCollection<TocEntryViewModel> _tocEntries = new();

    [ObservableProperty]
    private ObservableCollection<FavoriteViewModel> _favorites = new();

    [ObservableProperty]
    private ObservableCollection<string> _volInfoDisplay = new();

    [ObservableProperty]
    private bool _songNameIsEnabled = true;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    public Action<bool>? CloseAction { get; set; }
    public Action<int>? CloseWithPageAction { get; set; }
    public Func<IClipboard?>? GetClipboardFunc { get; set; }

    public string Title { get; }

    /// <summary>
    /// Returns true if any changes have been made since loading
    /// </summary>
    public bool IsDirty
    {
        get
        {
            if (PageNumberOffset != _originalPageNumberOffset) return true;
            if (DocNotes != _originalDocNotes) return true;

            if (TocEntries.Count != _originalTocEntries.Count) return true;

            for (int i = 0; i < TocEntries.Count; i++)
            {
                var current = TocEntries[i];
                var original = _originalTocEntries[i];

                if (current.PageNo != original.PageNo ||
                    current.SongName != original.SongName ||
                    current.Composer != original.Composer ||
                    current.Date != original.Date ||
                    current.Notes != original.Notes ||
                    current.Link != original.Link)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public MetaDataFormViewModel()
    {
        // Design-time constructor
        Title = "MetaData Editor";
        _rootFolder = string.Empty;
    }

    /// <summary>
    /// Constructor for testing - loads metadata directly from a JSON file path
    /// </summary>
    public MetaDataFormViewModel(string jsonFilePath)
    {
        _rootFolder = Path.GetDirectoryName(jsonFilePath) ?? string.Empty;
        Title = $"MetaData Editor - {Path.GetFileName(jsonFilePath)}";

        // Load metadata from JSON file using the centralized reader
        var pdfPath = Path.ChangeExtension(jsonFilePath, ".pdf");
        var provider = new DummyPdfDocumentProvider(); // For testing only
        
        try
        {
            _pdfMetaData = PdfMetaDataCore.ReadPdfMetaDataAsync(
                pdfPath,
                isSingles: false,
                provider,
                preferJsonOverBmk: true).GetAwaiter().GetResult();
            
            if (_pdfMetaData != null)
            {
                InitializeFromPdfMetaData(_pdfMetaData, 0);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error loading metadata: {ex.Message}");
        }
    }

    public MetaDataFormViewModel(PdfMetaDataReadResult pdfMetaData, string rootFolder, int currentPageNo = 0)
    {
        _pdfMetaData = pdfMetaData;
        _rootFolder = rootFolder;
        Title = $"MetaData Editor - {pdfMetaData.GetBookName(rootFolder)}";

        // Get the cached thumbnail
        Thumbnail = pdfMetaData.GetCachedThumbnail<Bitmap>();

        InitializeFromPdfMetaData(pdfMetaData, currentPageNo);
    }

    private void InitializeFromPdfMetaData(PdfMetaDataReadResult data, int currentPageNo)
    {
        PageNumberOffset = data.PageNumberOffset;
        DocNotes = data.Notes;
        SongNameIsEnabled = !data.IsSinglesFolder;

        // Create a HashSet of favorite page numbers for quick lookup
        var favoritePages = new HashSet<int>(data.Favorites.Select(f => f.Pageno));

        // Load TOC entries
        TocEntries.Clear();
        foreach (var toc in data.TocEntries.OrderBy(t => t.PageNo))
        {
            TocEntries.Add(new TocEntryViewModel(toc, favoritePages.Contains(toc.PageNo)));
        }

        // Load volume info display
        VolInfoDisplay.Clear();
        int volno = 0;
        int pgno = data.PageNumberOffset;
        foreach (var vol in data.VolumeInfoList)
        {
            VolInfoDisplay.Add($"Vol={volno++} Pg={pgno,3} NPg={vol.NPagesInThisVolume} {vol.FileNameVolume}");
            pgno += vol.NPagesInThisVolume;
        }

        // Load favorites with descriptions
        Favorites.Clear();
        foreach (var fav in data.Favorites.OrderBy(f => f.Pageno))
        {
            var description = GetDescription(fav.Pageno, data);
            Favorites.Add(new FavoriteViewModel(fav.Pageno, description));
        }

        // Select the TOC entry closest to the current page
        if (TocEntries.Count > 0)
        {
            var closestEntry = TocEntries.LastOrDefault(t => t.PageNo <= currentPageNo) ?? TocEntries[0];
            SelectedItem = closestEntry;
        }

        // Store original values for dirty tracking
        _originalPageNumberOffset = PageNumberOffset;
        _originalDocNotes = DocNotes;
        _originalTocEntries = TocEntries.Select(t => (t.PageNo, t.SongName, t.Composer, t.Date, t.Notes, t.Link)).ToList();
    }

    private static string GetDescription(int pageNo, PdfMetaDataReadResult data)
    {
        var tocEntry = data.TocEntries.FirstOrDefault(t => t.PageNo == pageNo);

        if (tocEntry == null)
        {
            tocEntry = data.TocEntries
                .Where(t => t.PageNo <= pageNo)
                .OrderByDescending(t => t.PageNo)
                .FirstOrDefault();
        }
        
        if (tocEntry != null)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(tocEntry.SongName)) parts.Add(tocEntry.SongName);
            if (!string.IsNullOrEmpty(tocEntry.Composer)) parts.Add(tocEntry.Composer);
            return string.Join(" - ", parts);
        }
        
        return string.Empty;
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var clipboard = GetClipboardFunc?.Invoke();
        if (clipboard == null) return;

        var clipText = await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(clipText)) return;

        var importedEntries = new List<TocEntryViewModel>();
        try
        {
            var lines = clipText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Trim().Split('\t');
                var entry = new TOCEntry();

                if (parts.Length > 0 && int.TryParse(parts[0], out var pageNo))
                {
                    entry.PageNo = pageNo;
                }
                if (parts.Length > 1) entry.SongName = parts[1].Trim();
                if (parts.Length > 2) entry.Composer = parts[2].Trim();
                if (parts.Length > 3) entry.Date = parts[3].Trim();
                if (parts.Length > 4) entry.Notes = parts[4].Trim();

                importedEntries.Add(new TocEntryViewModel(entry));
            }

            TocEntries.Clear();
            foreach (var entry in importedEntries.OrderBy(e => e.PageNo))
            {
                TocEntries.Add(entry);
            }
        }
        catch (Exception)
        {
            // Handle parse error - could show dialog
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var clipboard = GetClipboardFunc?.Invoke();
        if (clipboard == null || TocEntries.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var entry in TocEntries)
        {
            sb.AppendLine($"{entry.PageNo}\t{entry.SongName}\t{entry.Composer}\t{entry.Date}\t{entry.Notes}");
        }

        await clipboard.SetTextAsync(sb.ToString());
    }

    [RelayCommand]
    private void AddRow()
    {
        var newEntry = new TocEntryViewModel(new TOCEntry
        {
            PageNo = TocEntries.Count > 0 ? TocEntries.Max(e => e.PageNo) + 1 : PageNumberOffset
        });
        TocEntries.Add(newEntry);
        SelectedItem = newEntry;
    }

    [RelayCommand]
    private void DeleteRow()
    {
        if (SelectedItem != null)
        {
            var index = TocEntries.IndexOf(SelectedItem);
            TocEntries.Remove(SelectedItem);
            if (TocEntries.Count > 0)
            {
                SelectedItem = TocEntries[Math.Min(index, TocEntries.Count - 1)];
            }
        }
    }

    [RelayCommand]
    private void OpenLink()
    {
        if (SelectedItem != null && !string.IsNullOrEmpty(SelectedItem.Link))
        {
            OpenUrl(SelectedItem.Link);
        }
    }

    /// <summary>
    /// Validates that a URL is safe to open (http/https only)
    /// </summary>
    private static bool IsValidUrl(string url, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            errorMessage = "URL is empty";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            errorMessage = "Invalid URL format";
            return false;
        }

        // Only allow http and https schemes for security
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            errorMessage = $"Only http:// and https:// URLs are allowed (got {uri.Scheme}://)";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Opens a URL in the default browser after validation
    /// </summary>
    private static void OpenUrl(string url)
    {
        try
        {
            if (!IsValidUrl(url, out var errorMessage))
            {
                System.Diagnostics.Debug.WriteLine($"Invalid URL: {errorMessage}");
                return;
            }

            // Use Process.Start with UseShellExecute for cross-platform URL opening
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open URL: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }

    [RelayCommand]
    private void Save()
    {
        if (_pdfMetaData == null) return;

        try
        {
            // Update the PdfMetaDataReadResult directly
            var delta = _pdfMetaData.PageNumberOffset - PageNumberOffset;
            _pdfMetaData.PageNumberOffset = PageNumberOffset;
            _pdfMetaData.Notes = DocNotes;
            _pdfMetaData.TocEntries = TocEntries.Select(t => new TOCEntry
            {
                PageNo = t.PageNo,
                SongName = t.SongName,
                Composer = t.Composer,
                Date = t.Date,
                Notes = t.Notes,
                Link = t.Link
            }).ToList();

            // Adjust favorites if PageNumberOffset changed
            if (delta != 0)
            {
                foreach (var fav in _pdfMetaData.Favorites)
                {
                    fav.Pageno -= delta;
                }
            }

            // Mark as dirty so it will be saved
            _pdfMetaData.IsDirty = true;

            // Use the centralized save method from PdfMetaDataCore
            var success = PdfMetaDataCore.SaveToJson(_pdfMetaData, forceSave: true);

            CloseAction?.Invoke(success);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error saving metadata file: {ex}");
            CloseAction?.Invoke(false);
        }
    }
}

/// <summary>
/// ViewModel wrapper for TOCEntry to support property change notification
/// </summary>
public partial class TocEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private int _pageNo;

    [ObservableProperty]
    private string? _songName;

    [ObservableProperty]
    private string? _composer;

    [ObservableProperty]
    private string? _date;

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLink))]
    [NotifyPropertyChangedFor(nameof(LinkDisplay))]
    private string? _link;

    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>
    /// Display string for favorite column - shows star if favorite
    /// </summary>
    public string FavDisplay => IsFavorite ? "★" : "";

    /// <summary>
    /// Display string for link column - shows link icon if link exists
    /// </summary>
    public string LinkDisplay => !string.IsNullOrEmpty(Link) ? "🔗" : "";

    /// <summary>
    /// Whether this entry has a link
    /// </summary>
    public bool HasLink => !string.IsNullOrEmpty(Link);

    public TocEntryViewModel()
    {
    }

    public TocEntryViewModel(TOCEntry entry, bool isFavorite = false)
    {
        _pageNo = entry.PageNo;
        _songName = entry.SongName;
        _composer = entry.Composer;
        _date = entry.Date;
        _notes = entry.Notes;
        _link = entry.Link;
        _isFavorite = isFavorite;
    }
}

/// <summary>
/// ViewModel for displaying favorites
/// </summary>
public partial class FavoriteViewModel : ObservableObject
{
    [ObservableProperty]
    private int _pageNo;

    [ObservableProperty]
    private string? _description;

    public FavoriteViewModel()
    {
    }

    public FavoriteViewModel(int pageNo, string description)
    {
        _pageNo = pageNo;
        _description = description;
    }
}

/// <summary>
/// Dummy PDF document provider for testing when PDF files may not exist
/// </summary>
internal class DummyPdfDocumentProvider : IPdfDocumentProvider
{
    public Task<int> GetPageCountAsync(string pdfFilePath)
    {
        // Return a default page count for testing
        return Task.FromResult(1);
    }
}
