using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
    }

    public MetaDataFormWindow(MetaDataFormViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
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

    [ObservableProperty]
    private int _pageNumberOffset;

    [ObservableProperty]
    private string? _docNotes;

    [ObservableProperty]
    private TocEntryViewModel? _selectedItem;

    [ObservableProperty]
    private ObservableCollection<TocEntryViewModel> _tocEntries = new();

    [ObservableProperty]
    private ObservableCollection<FavoriteViewModel> _favorites = new();

    [ObservableProperty]
    private ObservableCollection<string> _volInfoDisplay = new();
    
    [ObservableProperty]
    private bool _songNameIsEnabled = true;

    public Action<bool>? CloseAction { get; set; }
    public Action<int>? CloseWithPageAction { get; set; }
    public Func<IClipboard?>? GetClipboardFunc { get; set; }

    public string Title { get; }

    public MetaDataFormViewModel()
    {
        // Design-time constructor
        Title = "MetaData Editor";
        _rootFolder = string.Empty;
    }

    /// <summary>
    /// Constructor for testing - loads metadata directly from a BMK file path
    /// </summary>
    public MetaDataFormViewModel(string bmkFilePath)
    {
        _rootFolder = Path.GetDirectoryName(bmkFilePath) ?? string.Empty;
        Title = $"MetaData Editor - {Path.GetFileName(bmkFilePath)}";

        // Load metadata from BMK file
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(SerializablePdfMetaData));
        SerializablePdfMetaData? data = null;
        
        if (File.Exists(bmkFilePath))
        {
            using var sr = new StreamReader(bmkFilePath);
            data = (SerializablePdfMetaData?)serializer.Deserialize(sr);
        }
        
        if (data != null)
        {
            // Create a minimal PdfMetaDataReadResult for the data
            _pdfMetaData = new PdfMetaDataReadResult
            {
                FullPathFile = bmkFilePath.Replace(".bmk", ".pdf"),
                IsSinglesFolder = false,
                PageNumberOffset = data.PageNumberOffset,
                Notes = data.Notes ?? string.Empty,
                TocEntries = data.lstTocEntries ?? new List<TOCEntry>(),
                Favorites = data.Favorites ?? new List<Favorite>(),
                VolumeInfoList = data.lstVolInfo?.Select(v => new PdfVolumeInfoBase
                {
                    FileNameVolume = v.FileNameVolume,
                    NPagesInThisVolume = v.NPagesInThisVolume,
                    Rotation = v.Rotation
                }).ToList() ?? new List<PdfVolumeInfoBase>()
            };
            
            InitializeFromPdfMetaData(_pdfMetaData, 0);
        }
    }

    public MetaDataFormViewModel(PdfMetaDataReadResult pdfMetaData, string rootFolder, int currentPageNo = 0)
    {
        _pdfMetaData = pdfMetaData;
        _rootFolder = rootFolder;
        Title = $"MetaData Editor - {pdfMetaData.GetBookName(rootFolder)}";

        InitializeFromPdfMetaData(pdfMetaData, currentPageNo);
    }

    private void InitializeFromPdfMetaData(PdfMetaDataReadResult data, int currentPageNo)
    {
        PageNumberOffset = data.PageNumberOffset;
        DocNotes = data.Notes;
        SongNameIsEnabled = !data.IsSinglesFolder;

        // Load TOC entries
        TocEntries.Clear();
        foreach (var toc in data.TocEntries.OrderBy(t => t.PageNo))
        {
            TocEntries.Add(new TocEntryViewModel(toc));
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

        var clipText = await clipboard.GetTextAsync();
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
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }

    [RelayCommand]
    private void Save()
    {
        if (_pdfMetaData == null) return;

        // Update the metadata with changes
        // Note: PdfMetaDataReadResult may need methods to update its data
        // For now, we'll update the BMK file directly
        
        var bmkPath = _pdfMetaData.BmkFilePath;
        if (string.IsNullOrEmpty(bmkPath) || !File.Exists(bmkPath))
        {
            CloseAction?.Invoke(false);
            return;
        }

        try
        {
            // Load the serializable data
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(SerializablePdfMetaData));
            SerializablePdfMetaData? data;
            
            using (var sr = new StreamReader(bmkPath))
            {
                data = (SerializablePdfMetaData?)serializer.Deserialize(sr);
            }
            
            if (data == null)
            {
                CloseAction?.Invoke(false);
                return;
            }

            // Update with changes
            var delta = data.PageNumberOffset - PageNumberOffset;
            data.PageNumberOffset = PageNumberOffset;
            data.Notes = DocNotes;
            data.lstTocEntries = TocEntries.Select(t => new TOCEntry
            {
                PageNo = t.PageNo,
                SongName = t.SongName,
                Composer = t.Composer,
                Date = t.Date,
                Notes = t.Notes
            }).ToList();
            data.dtLastWrite = DateTime.Now;
            
            // Adjust favorites if PageNumberOffset changed
            if (delta != 0)
            {
                foreach (var fav in data.Favorites)
                {
                    fav.Pageno -= delta;
                }
            }

            // Save to file
            var settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                IndentChars = " "
            };

            using var strm = File.Create(bmkPath);
            using var writer = System.Xml.XmlWriter.Create(strm, settings);
            serializer.Serialize(writer, data);

            CloseAction?.Invoke(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error saving BMK file: {ex}");
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

    public TocEntryViewModel()
    {
    }

    public TocEntryViewModel(TOCEntry entry)
    {
        _pageNo = entry.PageNo;
        _songName = entry.SongName;
        _composer = entry.Composer;
        _date = entry.Date;
        _notes = entry.Notes;
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
