using Avalonia.Controls;
using Avalonia.Input.Platform;
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
using System.Xml.Serialization;

namespace AvaloniaTests;

public partial class MetaDataFormWindow : Window
{
    public MetaDataFormWindow()
    {
        InitializeComponent();
    }

    public MetaDataFormWindow(MetaDataFormViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = () => Close();
        viewModel.GetClipboardFunc = () => Clipboard;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>
/// ViewModel for editing PDF metadata (TOC entries, favorites, etc.)
/// </summary>
public partial class MetaDataFormViewModel : ObservableObject
{
    private readonly string _bmkFilePath;
    private readonly SerializablePdfMetaData _originalData;

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

    public Action? CloseAction { get; set; }
    public Func<IClipboard?>? GetClipboardFunc { get; set; }

    public string Title { get; }

    public MetaDataFormViewModel()
    {
        // Design-time constructor
        Title = "MetaData Editor";
        _bmkFilePath = string.Empty;
        _originalData = new SerializablePdfMetaData();
    }

    public MetaDataFormViewModel(string bmkFilePath)
    {
        _bmkFilePath = bmkFilePath;
        Title = $"MetaData Editor - {Path.GetFileName(bmkFilePath)}";

        _originalData = LoadBmkFile(bmkFilePath);
        InitializeFromData(_originalData);
    }

    private SerializablePdfMetaData LoadBmkFile(string bmkFilePath)
    {
        if (!File.Exists(bmkFilePath))
        {
            return new SerializablePdfMetaData();
        }

        var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
        using var sr = new StreamReader(bmkFilePath);
        return (SerializablePdfMetaData)serializer.Deserialize(sr)!;
    }

    private void InitializeFromData(SerializablePdfMetaData data)
    {
        PageNumberOffset = data.PageNumberOffset;
        DocNotes = data.Notes;

        // Load TOC entries
        TocEntries.Clear();
        foreach (var toc in data.lstTocEntries.OrderBy(t => t.PageNo))
        {
            TocEntries.Add(new TocEntryViewModel(toc));
        }

        // Load volume info display
        VolInfoDisplay.Clear();
        int volno = 0;
        int pgno = data.PageNumberOffset;
        foreach (var vol in data.lstVolInfo)
        {
            VolInfoDisplay.Add($"Vol={volno++} Pg={pgno,3} {vol}");
            pgno += vol.NPagesInThisVolume;
        }

        // Load favorites with descriptions
        Favorites.Clear();
        var dictToc = BuildTocDictionary(data.lstTocEntries);
        foreach (var fav in data.Favorites.OrderBy(f => f.Pageno))
        {
            var description = GetDescription(fav.Pageno, dictToc);
            Favorites.Add(new FavoriteViewModel(fav.Pageno, description));
        }

        if (TocEntries.Count > 0)
        {
            SelectedItem = TocEntries[0];
        }
    }

    private static SortedList<int, List<TOCEntry>> BuildTocDictionary(List<TOCEntry> tocEntries)
    {
        var dict = new SortedList<int, List<TOCEntry>>();
        foreach (var toc in tocEntries)
        {
            if (!dict.TryGetValue(toc.PageNo, out var list))
            {
                list = new List<TOCEntry>();
                dict[toc.PageNo] = list;
            }
            list.Add(toc);
        }
        return dict;
    }

    private static string GetDescription(int pageNo, SortedList<int, List<TOCEntry>> dictToc)
    {
        if (dictToc.TryGetValue(pageNo, out var lstTocs))
        {
            return string.Join(" | ", lstTocs.Select(t => $"{t.SongName} {t.Composer} {t.Date} {t.Notes}".Trim()));
        }

        // Find nearest TOC entry before this page
        var nearestKey = dictToc.Keys.LastOrDefault(k => k <= pageNo);
        if (nearestKey != 0 || dictToc.ContainsKey(0))
        {
            if (dictToc.TryGetValue(nearestKey, out var nearestTocs))
            {
                return string.Join(" | ", nearestTocs.Select(t => $"{t.SongName} {t.Composer} {t.Date} {t.Notes}".Trim()));
            }
        }

        return string.Empty;
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var clipboard = GetClipboardFunc?.Invoke();
        if (clipboard == null) return;

        var clipText = await clipboard.TryGetTextAsync();
        if (clipText is not { } text || string.IsNullOrEmpty(text)) return;

        var importedEntries = new List<TocEntryViewModel>();
        try
        {
            var lines = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
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
            PageNo = TocEntries.Count > 0 ? TocEntries.Max(e => e.PageNo) + 1 : 0
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
        CloseAction?.Invoke();
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrEmpty(_bmkFilePath)) return;

        // Update original data with changes
        _originalData.PageNumberOffset = PageNumberOffset;
        _originalData.Notes = DocNotes;
        _originalData.lstTocEntries = TocEntries.Select(t => new TOCEntry
        {
            PageNo = t.PageNo,
            SongName = t.SongName,
            Composer = t.Composer,
            Date = t.Date,
            Notes = t.Notes
        }).ToList();
        _originalData.dtLastWrite = DateTime.Now;

        // Save to file
        var serializer = new XmlSerializer(typeof(SerializablePdfMetaData));
        var settings = new System.Xml.XmlWriterSettings
        {
            Indent = true,
            IndentChars = " "
        };

        using var strm = File.Create(_bmkFilePath);
        using var writer = System.Xml.XmlWriter.Create(strm, settings);
        serializer.Serialize(writer, _originalData);

        CloseAction?.Invoke();
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
