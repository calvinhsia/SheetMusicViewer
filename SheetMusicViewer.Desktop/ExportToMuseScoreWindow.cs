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
    private ProgressBar _progressBar = null!;
    private TextBox _txtStatus = null!;
    private ScrollViewer _statusScroll = null!;
    private Button _btnExport = null!;
    private Button _btnCancel = null!;
    private Button _btnClose = null!;

    private CancellationTokenSource? _cts;

    private record SongItem(string DisplayName, int StartPage, int EndPage)
    {
        public override string ToString() => $"{DisplayName}  (pp. {StartPage}–{EndPage})";
    }

    public ExportToMuseScoreWindow(PdfMetaDataReadResult pdfMetaData, int currentPageNo)
    {
        _pdfMetaData = pdfMetaData;
        _currentPageNo = currentPageNo;

        Title = "Open in MuseScore";
        Width = 640;
        Height = 620;
        MinWidth = 520;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
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
        _rbCurrentSong.IsCheckedChanged += (_, _) => UpdateSongControlsEnabled();
        sourcePanel.Children.Add(_rbCurrentSong);

        // Song chooser combo
        var songRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(20, 0, 0, 0) };
        songRow.Children.Add(new TextBlock { Text = "Song:", VerticalAlignment = VerticalAlignment.Center, Width = 50 });
        _cmbSong = new ComboBox { Width = 380 };
        PopulateSongCombo(toc, totalPages);
        songRow.Children.Add(_cmbSong);
        sourcePanel.Children.Add(songRow);

        // --- Full PDF radio ---
        _rbFullPdf = new RadioButton
        {
            Content = $"Entire PDF ({totalPages} pages)",
            GroupName = "ExportRange",
            IsChecked = toc.Count == 0
        };
        _rbFullPdf.IsCheckedChanged += (_, _) => UpdateSongControlsEnabled();
        sourcePanel.Children.Add(_rbFullPdf);

        // --- Custom range radio ---
        _rbCustomRange = new RadioButton
        {
            Content = "Custom page range:",
            GroupName = "ExportRange"
        };
        _rbCustomRange.IsCheckedChanged += (_, _) => UpdateSongControlsEnabled();
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
        rangeRow.Children.Add(_nudStartPage);
        rangeRow.Children.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) });
        _nudEndPage = new NumericUpDown { Value = totalPages, Minimum = 1, Maximum = totalPages, Width = 120, FormatString = "0" };
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

        mainGrid.Children.Add(toolPanel);

        // === Progress Area ===
        var progressPanel = new DockPanel { LastChildFill = true };
        Grid.SetRow(progressPanel, 3);

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
        Grid.SetRow(buttonPanel, 4);

        _btnCancel = new Button { Content = "Cancel Conversion", Width = 140, IsVisible = false };
        _btnCancel.Click += (_, _) => { _cts?.Cancel(); _btnCancel.IsVisible = false; };
        buttonPanel.Children.Add(_btnCancel);

        _btnClose = new Button { Content = "Close", Width = 80 };
        _btnClose.Click += (_, _) => Close();
        buttonPanel.Children.Add(_btnClose);

        _btnExport = new Button { Content = "▶  Export & Open", Width = 130 };
        _btnExport.Click += (_, _) => _ = RunExportAsync();
        buttonPanel.Children.Add(_btnExport);

        mainGrid.Children.Add(buttonPanel);

        Content = mainGrid;

        // Select the current song by default if TOC is available
        SelectCurrentSong(toc, totalPages);
        UpdateSongControlsEnabled();
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
                : entry.SongName + (string.IsNullOrWhiteSpace(entry.Composer) ? "" : $" \u2013 {entry.Composer}");

            _cmbSong.Items.Add(new SongItem(display, startSheet, endSheet));
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
        // Validate tool paths
        var audiverisPath = _txtAudiverisPath.Text?.Trim() ?? "";
        var museScorePath = _txtMuseScorePath.Text?.Trim() ?? "";

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

        // Update UI for running state
        _btnExport.IsEnabled = false;
        _btnCancel.IsVisible = true;
        _progressBar.IsVisible = true;
        _txtStatus.Text = "";
        _cts = new CancellationTokenSource();

        var progress = new Progress<string>(msg => SetStatus(msg));

        try
        {
            bool isFullPdf = startPage == 1 && endPage == totalPages;
            if (isFullPdf)
                SetStatus($"Using entire PDF ({totalPages} pages): {System.IO.Path.GetFileName(pdfPath)}");
            else
                SetStatus($"Processing pages {startPage}–{endPage} of {System.IO.Path.GetFileName(pdfPath)}…");

            SetStatus("Running Audiveris (this may take several minutes)…");

            var outputFile = await MuseScoreExportService.RunAudiverisAsync(
                audiverisPath, pdfPath,
                isFullPdf ? 0 : startPage,
                isFullPdf ? 0 : endPage,
                progress, _cts.Token);

            SetStatus($"Conversion complete: {outputFile}");
            SetStatus("Launching MuseScore…");

            MuseScoreExportService.LaunchMuseScore(museScorePath, outputFile);

            SetStatus("MuseScore launched. You can close this dialog.");
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
        }
    }
}
