using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SheetMusicLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Dialog for exporting the current PDF (or a song/page range) through Audiveris
/// and opening the result in MuseScore Studio.
/// </summary>
public class ExportToMuseScoreWindow : Window
{
    private readonly PdfMetaDataReadResult _pdfMetaData;
    private readonly int _currentPageNo;

    // UI controls
    private RadioButton _rbCurrentSong = null!;
    private RadioButton _rbFullPdf = null!;
    private RadioButton _rbCustomRange = null!;
    private StackPanel _customRangeRow = null!;
    private ComboBox _cmbSong = null!;
    private NumericUpDown _nudStartPage = null!;
    private NumericUpDown _nudEndPage = null!;
    private TextBox _txtAudiverisPath = null!;
    private TextBox _txtMuseScorePath = null!;
    private NumericUpDown _nudSpinePadding = null!;
    private TextBox _txtGhostscriptPath = null!;
    private CheckBox _chkUseGhostscript = null!;
    private Control _gsPathRow = null!;
    private NumericUpDown _nudTempo = null!;
    private ProgressBar _progressBar = null!;
    private TextBox _txtStatus = null!;
    private ScrollViewer _statusScroll = null!;
    private Button _btnExport = null!;
    private Button _btnCancel = null!;
    private Button _btnClose = null!;

    // persist-output UI
    private CheckBox _chkPersistNextToPdf = null!;
    private TextBlock _txtPersistPath = null!;
    private TextBlock _txtCacheStatus = null!;
    private Button _btnDeleteCachedFile = null!;
    private string? _cachedMxlPath;

    private CancellationTokenSource? _cts;

    private record SongItem(string DisplayName, string SongName, int StartPage, int EndPage)
    {
        public override string ToString() => $"{DisplayName}  (pp. {StartPage}–{EndPage})";
    }

    public ExportToMuseScoreWindow(PdfMetaDataReadResult pdfMetaData, int currentPageNo)
    {
        _pdfMetaData = pdfMetaData;
        _currentPageNo = currentPageNo;

        Title = "Open in MuseScore";
        Width = 640;
        Height = 820;
        MinWidth = 520;
        MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = true;

        BuildUI();

        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (_cts == null || _cts.IsCancellationRequested))
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _btnExport.IsVisible && _btnExport.IsEnabled)
        {
            _ = RunExportAsync();
            e.Handled = true;
        }
    }

    private void BuildUI()
    {
        var mainGrid = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),  // title
                new RowDefinition(GridLength.Auto),  // source section
                new RowDefinition(GridLength.Auto),  // tool paths section
                new RowDefinition(GridLength.Auto),  // persist / cache section
                new RowDefinition(new GridLength(1, GridUnitType.Star)), // progress area
                new RowDefinition(GridLength.Auto)   // buttons
            }
        };

        // Title
        var titleBlock = new TextBlock
        {
            Text = "Open in MuseScore",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(titleBlock, 0);
        mainGrid.Children.Add(titleBlock);

        // === Source Section ===
        var sourcePanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(sourcePanel, 1);

        sourcePanel.Children.Add(new TextBlock
        {
            Text = "Pages to export:",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13
        });

        var totalPages = _pdfMetaData.NumPagesInSet;
        var toc = _pdfMetaData.TocEntries?.OrderBy(t => t.PageNo).ToList() ?? new List<TOCEntry>();

        // --- Current song radio ---
        _rbCurrentSong = new RadioButton
        {
            Content = "Current song (from table of contents)",
            GroupName = "ExportRange",
            IsChecked = toc.Count > 0
        };
        _rbCurrentSong.IsCheckedChanged += (_, _) => { UpdateSongControlsEnabled(); UpdateCacheStatus(); };
        sourcePanel.Children.Add(_rbCurrentSong);

        // Song chooser combo
        var songRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(20, 0, 0, 0) };
        songRow.Children.Add(new TextBlock { Text = "Song:", VerticalAlignment = VerticalAlignment.Center, Width = 50 });
        _cmbSong = new ComboBox { Width = 480 };
        PopulateSongCombo(toc, totalPages);
        _cmbSong.SelectionChanged += (_, _) => UpdateCacheStatus();
        songRow.Children.Add(_cmbSong);
        sourcePanel.Children.Add(songRow);

        // --- Full PDF radio ---
        _rbFullPdf = new RadioButton
        {
            Content = $"Entire PDF ({totalPages} pages)",
            GroupName = "ExportRange",
            IsChecked = toc.Count == 0
        };
        _rbFullPdf.IsCheckedChanged += (_, _) => { UpdateSongControlsEnabled(); UpdateCacheStatus(); };
        sourcePanel.Children.Add(_rbFullPdf);

        // --- Custom range radio ---
        _rbCustomRange = new RadioButton
        {
            Content = "Custom page range:",
            GroupName = "ExportRange"
        };
        _rbCustomRange.IsCheckedChanged += (_, _) => { UpdateSongControlsEnabled(); UpdateCacheStatus(); };
        sourcePanel.Children.Add(_rbCustomRange);

        var rangeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(20, 2, 0, 4),
            Opacity = 0.45   // dimmed until Custom radio is selected
        };
        rangeRow.Children.Add(new TextBlock { Text = "From page:", VerticalAlignment = VerticalAlignment.Center, Width = 80 });
        _nudStartPage = new NumericUpDown { Value = 1, Minimum = 1, Maximum = totalPages, Width = 120, FormatString = "0" };
        _nudStartPage.ValueChanged += (_, _) => UpdateCacheStatus();
        rangeRow.Children.Add(_nudStartPage);
        rangeRow.Children.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) });
        _nudEndPage = new NumericUpDown { Value = totalPages, Minimum = 1, Maximum = totalPages, Width = 120, FormatString = "0" };
        _nudEndPage.ValueChanged += (_, _) => UpdateCacheStatus();
        rangeRow.Children.Add(_nudEndPage);
        rangeRow.Children.Add(new TextBlock { Text = $"(1–{totalPages})", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11 });
        sourcePanel.Children.Add(rangeRow);

        // Keep a reference to the row so we can enable/disable it with the radio
        _customRangeRow = rangeRow;

        mainGrid.Children.Add(sourcePanel);

        // === Tool Paths Section ===
        var toolPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(toolPanel, 2);

        toolPanel.Children.Add(new TextBlock
        {
            Text = "External tools:",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13
        });

        toolPanel.Children.Add(CreatePathRow("Audiveris:", ref _txtAudiverisPath,
            AppSettings.Instance.AudiverisPath,
            "Audiveris OMR engine (converts sheet music PDF to MusicXML)",
            () => MuseScoreExportService.AutoDetectAudiveris(),
            "AudiverisPath"));

        toolPanel.Children.Add(CreatePathRow("MuseScore:", ref _txtMuseScorePath,
            AppSettings.Instance.MuseScorePath,
            "MuseScore Studio 4 (opens the converted MusicXML)",
            () => MuseScoreExportService.AutoDetectMuseScore(),
            "MuseScorePath"));

        // Ghostscript — optional, off by default
        _chkUseGhostscript = new CheckBox
        {
            Content = "Use Ghostscript to rasterize PDF before Audiveris  (helps when Audiveris sees fewer pages than expected)",
            IsChecked = AppSettings.Instance.UseGhostscript,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 12
        };
        _chkUseGhostscript.IsCheckedChanged += (_, _) => { };
        toolPanel.Children.Add(_chkUseGhostscript);

        TextBox gsTextBox = null!;
        var gsRowControl = CreatePathRow("Ghostscript:", ref gsTextBox,
            AppSettings.Instance.GhostscriptPath,
            "Ghostscript (gswin64c / gs) — rasterizes PDFs at 300 DPI so Audiveris can detect all pages.",
            () => MuseScoreExportService.AutoDetectGhostscript(),
            "GhostscriptPath");
        _txtGhostscriptPath = gsTextBox;
        _gsPathRow = gsRowControl;
        _gsPathRow.IsVisible = true;
        toolPanel.Children.Add(_gsPathRow);

        // Spine-padding row
        var spineRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        spineRow.Children.Add(new TextBlock { Text = "Spine padding:", Width = 110, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        _nudSpinePadding = new NumericUpDown { Value = AppSettings.Instance.SpinePaddingPx, Minimum = 0, Maximum = 300, Width = 110, FormatString = "0" };
        spineRow.Children.Add(_nudSpinePadding);
        spineRow.Children.Add(new TextBlock { Text = "px  (0 = off)  — adds white margin on spine/gutter edge to recover clipped clefs", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        toolPanel.Children.Add(spineRow);

        // Tempo row
        var tempoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        tempoRow.Children.Add(new TextBlock { Text = "Playback tempo:", Width = 110, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        _nudTempo = new NumericUpDown { Value = 120, Minimum = 20, Maximum = 300, Width = 110, FormatString = "0" };
        tempoRow.Children.Add(_nudTempo);
        tempoRow.Children.Add(new TextBlock { Text = "BPM  (quarter note)", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 11 });
        toolPanel.Children.Add(tempoRow);

        mainGrid.Children.Add(toolPanel);

        // === Persist / Cache Section ===
        var persistPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(persistPanel, 3);

        _chkPersistNextToPdf = new CheckBox
        {
            Content = "Save output next to source PDF (enables skip-if-unchanged)",
            IsChecked = AppSettings.Instance.PersistMxlNextToPdf,
            FontSize = 12
        };
        _chkPersistNextToPdf.IsCheckedChanged += (_, _) =>
        {
            AppSettings.Instance.PersistMxlNextToPdf = _chkPersistNextToPdf.IsChecked == true;
            AppSettings.Instance.Save();
            UpdateCacheStatus();
        };
        persistPanel.Children.Add(_chkPersistNextToPdf);

        _txtPersistPath = new TextBlock
        {
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 0, 0, 0)
        };
        persistPanel.Children.Add(_txtPersistPath);

        _txtCacheStatus = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        _btnDeleteCachedFile = new Button
        {
            Content = "🗑 Delete cached file",
            FontSize = 11,
            Padding = new Thickness(6, 2),
            IsVisible = false
        };
        _btnDeleteCachedFile.Click += (_, _) =>
        {
            if (_cachedMxlPath != null && System.IO.File.Exists(_cachedMxlPath))
                System.IO.File.Delete(_cachedMxlPath);
            UpdateCacheStatus();
        };
        var cacheStatusRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(22, 0, 0, 0)
        };
        cacheStatusRow.Children.Add(_txtCacheStatus);
        cacheStatusRow.Children.Add(_btnDeleteCachedFile);
        persistPanel.Children.Add(cacheStatusRow);

        mainGrid.Children.Add(persistPanel);

        // === Progress Area ===
        var progressPanel = new DockPanel { LastChildFill = true };
        Grid.SetRow(progressPanel, 4);

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 20,
            IsIndeterminate = true,
            IsVisible = false
        };
        DockPanel.SetDock(_progressBar, Dock.Top);
        progressPanel.Children.Add(_progressBar);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 4, 0, 0)
        };
        _statusScroll = scrollViewer;
        _txtStatus = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2)
        };
        scrollViewer.Content = _txtStatus;
        progressPanel.Children.Add(scrollViewer);  // LastChildFill — takes remaining height

        mainGrid.Children.Add(progressPanel);

        // === Buttons ===
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(buttonPanel, 5);

        _btnCancel = new Button { Content = "Cancel Conversion", Width = 140, IsVisible = false };
        _btnCancel.Click += (_, _) => { _cts?.Cancel(); _btnCancel.IsVisible = false; };
        buttonPanel.Children.Add(_btnCancel);

        _btnClose = new Button { Content = "Close", Width = 80 };
        _btnClose.Click += (_, _) => Close();
        buttonPanel.Children.Add(_btnClose);

        _btnExport = new Button { Content = "▶  Export & Open", Width = 130, IsDefault = true };
        _btnExport.Click += (_, _) => _ = RunExportAsync();
        buttonPanel.Children.Add(_btnExport);

        mainGrid.Children.Add(buttonPanel);

        Content = mainGrid;

        // Select the current song by default if TOC is available
        SelectCurrentSong(toc, totalPages);
        UpdateSongControlsEnabled();
        UpdateCacheStatus();
    }

    private void PopulateSongCombo(List<TOCEntry> toc, int totalPages)
    {
        if (toc.Count == 0) return;

        // TOC PageNo is offset-based (PageNumberOffset can be 0).
        // Audiveris -sheets uses 1-based physical sheet numbers.
        // Physical sheet = PageNo - PageNumberOffset + 1
        int offset = _pdfMetaData.PageNumberOffset;

        for (int i = 0; i < toc.Count; i++)
        {
            var entry = toc[i];
            // 1-based physical start sheet
            int startSheet = entry.PageNo - offset + 1;
            // end sheet: page before the next song's start, or last page
            int endSheet = (i + 1 < toc.Count)
                ? toc[i + 1].PageNo - offset   // one sheet before next song (still 1-based)
                : totalPages;                    // last physical sheet
            endSheet = Math.Max(startSheet, endSheet);

            var display = string.IsNullOrWhiteSpace(entry.SongName)
                ? $"Sheet {startSheet}"
                : entry.SongName + (string.IsNullOrWhiteSpace(entry.Composer) ? "" : $" - {entry.Composer}");

            _cmbSong.Items.Add(new SongItem(display, entry.SongName ?? $"Sheet {startSheet}", startSheet, endSheet));
        }

        if (_cmbSong.ItemCount > 0)
            _cmbSong.SelectedIndex = 0;
    }

    private void SelectCurrentSong(List<TOCEntry> toc, int totalPages)
    {
        if (toc.Count == 0 || _cmbSong.ItemCount == 0) return;

        int offset = _pdfMetaData.PageNumberOffset;
        // _currentPageNo is the viewer's current page (offset-based), convert to 1-based sheet
        int currentSheet = _currentPageNo - offset + 1;

        // Find the TOC entry whose 1-based start sheet <= currentSheet
        for (int i = toc.Count - 1; i >= 0; i--)
        {
            int entrySheet = toc[i].PageNo - offset + 1;
            if (entrySheet <= currentSheet)
            {
                _cmbSong.SelectedIndex = i;
                return;
            }
        }
    }

    private void UpdateSongControlsEnabled()
    {
        bool isCustom = _rbCustomRange.IsChecked == true;
        _cmbSong.IsEnabled = _rbCurrentSong.IsChecked == true;
        _customRangeRow.IsEnabled = isCustom;
        _customRangeRow.Opacity = isCustom ? 1.0 : 0.45;
    }

    private (int startPage, int endPage) GetEffectiveRange()
    {
        var totalPages = _pdfMetaData.NumPagesInSet;
        if (_rbCurrentSong.IsChecked == true && _cmbSong.SelectedItem is SongItem song)
            return (song.StartPage, song.EndPage);
        if (_rbCustomRange.IsChecked == true)
            return ((int)(_nudStartPage.Value ?? 1), (int)(_nudEndPage.Value ?? totalPages));
        return (1, totalPages);
    }

    /// <summary>Returns the raw song name (no composer) when "Current song" is active AND the book
    /// contains multiple songs (i.e. this is a song-book, not a single-song PDF). Returns null for
    /// singles and for the Entire PDF / Custom Range selections — in those cases the PDF basename alone
    /// is the canonical cache key.</summary>
    private string? GetSelectedSongName() =>
        _rbCurrentSong.IsChecked == true && _cmbSong.SelectedItem is SongItem s && _cmbSong.ItemCount > 1
            ? s.SongName
            : null;

    private string? GetPersistDir()
    {
        if (_chkPersistNextToPdf?.IsChecked != true) return null;
        var pdfPath = _pdfMetaData.GetFullPathFileFromVolno(0);
        if (string.IsNullOrEmpty(pdfPath)) return null;
        return System.IO.Path.GetDirectoryName(pdfPath);
    }

    private void UpdateCacheStatus()
    {
        if (_txtPersistPath == null || _txtCacheStatus == null || _btnDeleteCachedFile == null) return;

        var persistDir = GetPersistDir();
        if (persistDir == null)
        {
            _txtPersistPath.Text = "";
            _txtCacheStatus.Text = "";
            _btnExport.Content = "▶  Export & Open";
            _cachedMxlPath = null;
            _btnDeleteCachedFile.IsVisible = false;
            return;
        }

        var (startPage, endPage) = GetEffectiveRange();
        bool useGs = _chkUseGhostscript.IsChecked == true;
        // Use 0/0 to mean "all pages" (full PDF radio), matching RunAudiverisAsync convention
        int bookStart = (_rbFullPdf.IsChecked == true) ? 0 : startPage;
        int bookEnd   = (_rbFullPdf.IsChecked == true) ? 0 : endPage;
        var songName = GetSelectedSongName();
        var expectedPath = MuseScoreExportService.ComputeExpectedMxlPath(_pdfMetaData, bookStart, bookEnd, useGs, persistDir, songName);
        var cachedPath   = MuseScoreExportService.FindCachedMxlPath(_pdfMetaData, bookStart, bookEnd, useGs, persistDir, songName);

        _txtPersistPath.Text = expectedPath ?? cachedPath ?? "(spans multiple volumes — no cache)";

        _cachedMxlPath = cachedPath;
        _btnDeleteCachedFile.IsVisible = cachedPath != null;

        if (cachedPath != null)
        {
            var age = DateTime.Now - System.IO.File.GetLastWriteTime(cachedPath);
            var ageStr = age.TotalDays >= 1 ? $"{(int)age.TotalDays}d ago" :
                         age.TotalHours >= 1 ? $"{(int)age.TotalHours}h ago" :
                         $"{(int)age.TotalMinutes}m ago";
            // Show the actual cached file name (may differ from expectedPath on other machines)
            _txtPersistPath.Text = cachedPath;
            _txtCacheStatus.Foreground = Brushes.Green;
            _txtCacheStatus.Text = $"✔ Cached ({ageStr}) — conversion will be skipped";
            _btnExport.Content = "▶  Open in MuseScore";
        }
        else
        {
            _txtCacheStatus.Foreground = Brushes.Gray;
            _txtCacheStatus.Text = expectedPath != null ? "No cached file — conversion will run" : "Multi-volume range — cache not supported";
            _btnExport.Content = "▶  Export & Open";
        }
    }

    private Control CreatePathRow(string label, ref TextBox textBoxField, string currentValue,
        string tooltip, Func<string?> autoDetect, string settingKey)
    {
        var panel = new StackPanel { Spacing = 4 };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = label, Width = 75, VerticalAlignment = VerticalAlignment.Center });

        // Capture ref for lambda use
        TextBox? textBox = null;
        textBox = new TextBox
        {
            Text = currentValue,
            Width = 340,
            [ToolTip.TipProperty] = tooltip
        };
        textBoxField = textBox;
        row.Children.Add(textBox);

        var btnDetect = new Button { Content = "Auto-detect", Padding = new Thickness(6, 2) };
        btnDetect.Click += (_, _) =>
        {
            var detected = autoDetect();
            if (detected != null)
            {
                textBox.Text = detected;
                SetStatus($"Auto-detected: {detected}");
            }
            else
            {
                SetStatus($"Could not auto-detect {label.TrimEnd(':')}. Please browse for it manually.");
            }
        };
        row.Children.Add(btnDetect);

        var btnBrowse = new Button { Content = "…", Width = 30, Padding = new Thickness(2) };
        btnBrowse.Click += async (_, _) =>
        {
            var filters = new List<FilePickerFileType>
            {
                new FilePickerFileType("Executables") { Patterns = new[] { "*.exe", "*.bat", "*.sh" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
            };
            var result = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = $"Locate {label.TrimEnd(':')}",
                AllowMultiple = false,
                FileTypeFilter = filters
            });

            if (result.Count > 0)
            {
                textBox.Text = result[0].Path.LocalPath;
            }
        };
        row.Children.Add(btnBrowse);

        panel.Children.Add(row);
        return panel;
    }

    private void SetStatus(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _txtStatus.Text = (_txtStatus.Text?.Length > 0 ? _txtStatus.Text + "\n" : "") + message;
            // Scroll to bottom after layout updates
            Dispatcher.UIThread.Post(() =>
            {
                _statusScroll.Offset = new Avalonia.Vector(0, _statusScroll.ScrollBarMaximum.Y);
            }, DispatcherPriority.Render);
        });
    }

    private async Task RunExportAsync()
    {
        // Validate tool paths — auto-detect if fields are still empty
        var audiverisPath = _txtAudiverisPath.Text?.Trim() ?? "";
        var museScorePath = _txtMuseScorePath.Text?.Trim() ?? "";
        var gsPath        = _txtGhostscriptPath.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(audiverisPath))
        {
            audiverisPath = MuseScoreExportService.AutoDetectAudiveris() ?? "";
            if (!string.IsNullOrEmpty(audiverisPath))
            {
                _txtAudiverisPath.Text = audiverisPath;
                SetStatus($"Auto-detected Audiveris: {audiverisPath}");
            }
        }
        if (string.IsNullOrEmpty(museScorePath))
        {
            museScorePath = MuseScoreExportService.AutoDetectMuseScore() ?? "";
            if (!string.IsNullOrEmpty(museScorePath))
            {
                _txtMuseScorePath.Text = museScorePath;
                SetStatus($"Auto-detected MuseScore: {museScorePath}");
            }
        }
        if (string.IsNullOrEmpty(gsPath))
        {
            gsPath = MuseScoreExportService.AutoDetectGhostscript() ?? "";
            if (!string.IsNullOrEmpty(gsPath))
            {
                _txtGhostscriptPath.Text = gsPath;
                SetStatus($"Auto-detected Ghostscript: {gsPath}");
            }
        }

        if (string.IsNullOrEmpty(audiverisPath))
        {
            SetStatus("Please specify the path to Audiveris.");
            return;
        }
        if (string.IsNullOrEmpty(museScorePath))
        {
            SetStatus("Please specify the path to MuseScore.");
            return;
        }

        // Persist paths to settings
        AppSettings.Instance.AudiverisPath = audiverisPath;
        AppSettings.Instance.MuseScorePath = museScorePath;
        AppSettings.Instance.GhostscriptPath = _txtGhostscriptPath.Text?.Trim() ?? "";
        AppSettings.Instance.UseGhostscript = _chkUseGhostscript.IsChecked == true;
        AppSettings.Instance.SpinePaddingPx = (int)(_nudSpinePadding.Value ?? 0);
        AppSettings.Instance.PersistMxlNextToPdf = _chkPersistNextToPdf.IsChecked == true;
        AppSettings.Instance.Save();

        // Determine page range
        int startPage, endPage;
        var totalPages = _pdfMetaData.NumPagesInSet;

        if (_rbCurrentSong.IsChecked == true && _cmbSong.SelectedItem is SongItem song)
        {
            startPage = song.StartPage;
            endPage = song.EndPage;
        }
        else if (_rbCustomRange.IsChecked == true)
        {
            startPage = (int)(_nudStartPage.Value ?? 1);
            endPage = (int)(_nudEndPage.Value ?? totalPages);
            if (startPage > endPage)
            {
                SetStatus("Start page must be ≤ end page.");
                return;
            }
        }
        else
        {
            // Full PDF
            startPage = 1;
            endPage = totalPages;
        }

        var pdfPath = _pdfMetaData.GetFullPathFileFromVolno(0);
        if (string.IsNullOrEmpty(pdfPath) || !System.IO.File.Exists(pdfPath))
        {
            SetStatus($"PDF file not found: {pdfPath}");
            return;
        }

        // Check for cached output — skip conversion if unchanged
        var persistDir = GetPersistDir();
        if (persistDir != null)
        {
            bool useGs = _chkUseGhostscript.IsChecked == true;
            int bookStart = (startPage == 1 && endPage == totalPages) ? 0 : startPage;
            int bookEnd   = (startPage == 1 && endPage == totalPages) ? 0 : endPage;
            var songName = GetSelectedSongName();
            var cachedPath = MuseScoreExportService.FindCachedMxlPath(_pdfMetaData, bookStart, bookEnd, useGs, persistDir, songName);
            if (cachedPath != null)
            {
                var bpmQuick = (int)(_nudTempo.Value ?? 120);
                SetStatus($"Using cached output: {cachedPath}");
                SetStatus($"Setting playback tempo to {bpmQuick} BPM…");
                MuseScoreExportService.SetTempoInMusicXml(cachedPath, bpmQuick, new Progress<string>(msg => SetStatus(msg)));
                SetStatus("Launching MuseScore…");
                MuseScoreExportService.LaunchMuseScore(museScorePath, cachedPath);
                SetStatus("MuseScore launched. You can close this dialog.");
                return;
            }
        }

        // Update UI for running state
        _btnExport.IsEnabled = false;
        _btnCancel.IsVisible = true;
        _progressBar.IsVisible = true;
        _txtStatus.Text = "";
        _cts = new CancellationTokenSource();

        var progress = new Progress<string>(msg => SetStatus(msg));

        try
        {
            int volumeCount = _pdfMetaData.VolumeInfoList.Count;
            bool isFullSet = startPage == 1 && endPage == totalPages;
            if (isFullSet)
                SetStatus($"Using all {totalPages} pages across {volumeCount} volume(s).");
            else
                SetStatus($"Processing pages {startPage}–{endPage} of {totalPages} (across {volumeCount} volume(s))…");

            SetStatus("Running Audiveris (this may take several minutes)…");

            var outputFile = await MuseScoreExportService.RunAudiverisAsync(
                audiverisPath, _pdfMetaData,
                isFullSet ? 0 : startPage,
                isFullSet ? 0 : endPage,
                progress, _cts.Token,
                persistDir,
                GetSelectedSongName());

            SetStatus($"Conversion complete: {outputFile}");

            var bpm = (int)(_nudTempo.Value ?? 120);
            if (bpm != 120 || true)  // always inject so the file carries an explicit tempo
            {
                SetStatus($"Setting playback tempo to {bpm} BPM…");
                MuseScoreExportService.SetTempoInMusicXml(outputFile, bpm, progress);
            }

            SetStatus("Launching MuseScore…");

            MuseScoreExportService.LaunchMuseScore(museScorePath, outputFile);

            SetStatus("MuseScore launched. You can close this dialog.");
            UpdateCacheStatus();
        }
        catch (OperationCanceledException)
        {
            SetStatus("Conversion cancelled.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            Logger.LogException("ExportToMuseScore error", ex);
        }
        finally
        {
            _btnExport.IsEnabled = true;
            _btnCancel.IsVisible = false;
            _progressBar.IsVisible = false;
            _cts?.Dispose();
            _cts = null;
            UpdateCacheStatus();
        }
    }
}
