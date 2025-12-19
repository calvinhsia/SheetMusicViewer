using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using PDFtoImage;
using SheetMusicLib;
using SkiaSharp;
using System.Threading;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Main PDF viewer window - Avalonia implementation of the WPF PdfViewerWindow.
/// Uses PDFtoImage for cross-platform PDF rendering.
/// </summary>
public partial class PdfViewerWindow : Window, INotifyPropertyChanged
{
    public const string MyAppName = "SheetMusicViewer";
    
    public new event PropertyChangedEventHandler? PropertyChanged;

    private int _currentPageNumber = 1;
    private bool _show2Pages = true;
    private bool _pdfUIEnabled;
    private string _pdfTitle = string.Empty;
    private string _description0 = string.Empty;
    private string _description1 = string.Empty;
    private int _maxPageNumberMinus1;
    private bool _disableSliderValueChanged;
    private bool _chkFavoriteEnabled;
    private bool _isThumbnailLoadingInProgress;
    private int _cacheLoadingCount;
    private string _cacheStatus = string.Empty;
    
    // PDF metadata
    private string _rootMusicFolder = string.Empty;
    private List<PdfMetaDataReadResult> _lstPdfMetaFileData = new();
    private List<string> _lstFolders = new();
    private PdfMetaDataReadResult? _currentPdfMetaData;
    
    // UI controls
    private InkCanvasControl? _inkCanvas0;
    private InkCanvasControl? _inkCanvas1;
    private GestureHandler? _gestureHandler;
    private Panel? _dpPage;
    private Slider? _slider;
    private Popup? _sliderPopup;
    private TextBlock? _tbSliderPopup;
    private CheckBox? _chkFullScreen;
    private CheckBox? _chkInk0;
    private CheckBox? _chkInk1;
    private Image? _imgThumb;
    private Menu? _mainMenu;

    // Page cache for performance - cache Tasks like WPF version for better parallelism
    private readonly Dictionary<int, PageCacheEntry> _pageCache = new();
    private const int MaxCacheSize = 50;
    private int _currentCacheAge;
    private int _lastNavigationDelta; // Track navigation direction for prefetch priority
    private bool _isShowingMetaDataForm; // Prevent showing multiple MetaDataForm dialogs

    private class PageCacheEntry
    {
        public CancellationTokenSource Cts { get; } = new();
        public int PageNo { get; init; }
        public required Task<Bitmap> Task { get; init; }
        public int Age { get; set; }
        
        public override string ToString() => $"{PageNo} age={Age} IsCompleted={Task.IsCompleted}";
    }

    public PdfViewerWindow()
    {
        InitializeComponent();
        DataContext = this;
        
        // Load settings
        var settings = AppSettings.Instance;
        _show2Pages = settings.Show2Pages;
        _rootMusicFolder = settings.RootFolderMRU.FirstOrDefault() ?? string.Empty;
        
        Trace.WriteLine($"PdfViewerWindow constructor: WindowMaximized={settings.WindowMaximized} from {AppSettings.SettingsPath}");
        
        // Apply window position/size from settings
        if (settings.WindowWidth > 0 && settings.WindowHeight > 0)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }
        if (settings.WindowLeft >= 0 && settings.WindowTop >= 0)
        {
            Position = new Avalonia.PixelPoint((int)settings.WindowLeft, (int)settings.WindowTop);
        }
        
        // Note: WindowState is set in Opened event because setting it in constructor 
        // doesn't work reliably in Avalonia
        
        WireUpEventHandlers();
        
        // Load and display the last opened PDF
        Loaded += (s, e) => _ = OnWindowLoadedAsync();
        
        // Apply maximized state after window opens
        Opened += (s, e) =>
        {
            Trace.WriteLine($"PdfViewerWindow Opened: Reading WindowMaximized={AppSettings.Instance.WindowMaximized}");
            if (AppSettings.Instance.WindowMaximized)
            {
                WindowState = WindowState.Maximized;
                Trace.WriteLine($"PdfViewerWindow Opened: Set WindowState to Maximized");
            }
        };
        
        // Save settings and cleanup on close
        Closing += OnWindowClosing;
        Closed += (s, e) => _gestureHandler?.Detach();
    }
    
    private void WireUpEventHandlers()
    {
        // Cache UI controls that are accessed frequently
        _dpPage = this.GetControl<Panel>("dpPage");
        _chkFullScreen = this.GetControl<CheckBox>("chkFullScreen");
        _chkInk0 = this.GetControl<CheckBox>("chkInk0");
        _chkInk1 = this.GetControl<CheckBox>("chkInk1");
        _imgThumb = this.GetControl<Image>("ImgThumb");
        _mainMenu = this.GetControl<Menu>("mainMenu");
        
        // Wire up ink checkbox events
        _chkInk0.IsCheckedChanged += (s, e) => 
        { 
            if (_inkCanvas0 != null) 
                _inkCanvas0.IsInkingEnabled = _chkInk0.IsChecked == true;
            UpdateGestureHandlerState();
        };
        
        _chkInk1.IsCheckedChanged += (s, e) => 
        { 
            if (_inkCanvas1 != null) 
                _inkCanvas1.IsInkingEnabled = _chkInk1.IsChecked == true;
            UpdateGestureHandlerState();
        };
        
        // Wire up favorite checkbox events
        var chkFav0 = this.GetControl<CheckBox>("chkFav0");
        var chkFav1 = this.GetControl<CheckBox>("chkFav1");
        
        chkFav0.IsCheckedChanged += ChkFav_Toggled;
        chkFav1.IsCheckedChanged += ChkFav_Toggled;
        
        // Wire up rotate button
        var btnRotate = this.GetControl<Button>("btnRotate");
        btnRotate.Click += BtnRotate_Click;
        
        // Wire up full screen checkbox
        _chkFullScreen.IsCheckedChanged += (s, e) =>
        {
            ChkFullScreenToggled(_chkFullScreen.IsChecked == true);
        };
        
        // Set full screen based on settings
        _chkFullScreen.IsChecked = AppSettings.Instance.IsFullScreen;
        
        // Wire up menu items
        var mnuChooser = this.GetControl<MenuItem>("mnuChooser");
        mnuChooser.Click += (s, e) => _ = ChooseMusicAsync();
        
        var mnuFullScreen = this.GetControl<MenuItem>("mnuFullScreen");
        mnuFullScreen.Click += (s, e) =>
        {
            _chkFullScreen.IsChecked = !_chkFullScreen.IsChecked;
        };
        
        var mnuAbout = this.GetControl<MenuItem>("mnuAbout");
        mnuAbout.Click += BtnAbout_Click;
        
        var mnuShowLogs = this.GetControl<MenuItem>("mnuShowLogs");
        mnuShowLogs.Click += MnuShowLogs_Click;
        
        var mnuQuit = this.GetControl<MenuItem>("mnuQuit");
        mnuQuit.Click += (s, e) => Close();
        
        // Wire up navigation buttons
        var btnPrev = this.GetControl<Button>("btnPrev");
        var btnNext = this.GetControl<Button>("btnNext");
        btnPrev.Click += (s, e) => BtnPrevNext_Click(isPrevious: true);
        btnNext.Click += (s, e) => BtnPrevNext_Click(isPrevious: false);
        
        // Wire up thumbnail button for metadata editor (Alt-E)
        var btnThumb = this.GetControl<Button>("btnThumb");
        btnThumb.Click += (s, e) => _ = ShowMetaDataFormAsync();
        
        // Wire up slider events
        _slider = this.GetControl<Slider>("slider");
        _sliderPopup = this.GetControl<Popup>("SliderPopup");
        _tbSliderPopup = this.GetControl<TextBlock>("tbSliderPopup");
        
        _slider.TemplateApplied += Slider_TemplateApplied;
        _slider.AddHandler(RangeBase.ValueChangedEvent, Slider_ValueChanged);
        
        // Add keyboard handler
        this.KeyDown += Window_KeyDown;
    }
    
    private async Task OnWindowLoadedAsync()
    {
        try
        {
            Title = MyAppName;
            var settings = AppSettings.Instance;
            
            // Apply full screen setting
            ChkFullScreenToggled(_chkFullScreen?.IsChecked == true);
            
            if (string.IsNullOrEmpty(_rootMusicFolder) || !Directory.Exists(_rootMusicFolder))
            {
                // First time user - create and use sample data
                _rootMusicFolder = await SampleDataHelper.EnsureSampleDataExistsAsync();
                settings.AddToMRU(_rootMusicFolder);
                settings.Save();
                
                // Load the sample PDF metadata
                var provider = new PdfToImageDocumentProvider();
                (_lstPdfMetaFileData, _lstFolders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
                    _rootMusicFolder, 
                    provider,
                    useParallelLoading: true);
                
                // Find and open the GettingStarted PDF specifically (for first-time users)
                if (_lstPdfMetaFileData.Count > 0)
                {
                    // Look for GettingStarted.pdf first
                    var gettingStartedPdf = _lstPdfMetaFileData.FirstOrDefault(p => 
                        p.GetFullPathFileFromVolno(0)?.Contains("GettingStarted", StringComparison.OrdinalIgnoreCase) == true);
                    
                    // Fall back to first PDF if GettingStarted not found
                    var pdfToLoad = gettingStartedPdf ?? _lstPdfMetaFileData[0];
                    await LoadPdfFileAndShowAsync(pdfToLoad, 0);
                }
            }
            else
            {
                // Load all PDF metadata
                var provider = new PdfToImageDocumentProvider();
                (_lstPdfMetaFileData, _lstFolders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
                    _rootMusicFolder, 
                    provider,
                    useParallelLoading: true);
                
                // Try to load the last opened PDF
                var lastPdfOpen = settings.LastPDFOpen;
                var lastPdfMetaData = _lstPdfMetaFileData.FirstOrDefault(p => 
                    p.GetFullPathFileFromVolno(0)?.EndsWith(lastPdfOpen ?? "", StringComparison.OrdinalIgnoreCase) == true);
                
                if (lastPdfMetaData != null)
                {
                    await LoadPdfFileAndShowAsync(lastPdfMetaData, lastPdfMetaData.LastPageNo);
                    
                    // Load all thumbnails in the background while showing the doc
                    _ = LoadAllThumbnailsAsync();
                }
                else
                {
                    await ChooseMusicAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Description0 = Logger.LogExceptionAndGetUserMessage("Error loading application", ex);
        }
    }
    
    /// <summary>
    /// Load all PDF thumbnails in the background for faster ChooseMusic display later
    /// </summary>
    private async Task LoadAllThumbnailsAsync()
    {
        IsThumbnailLoadingInProgress = true;
        try
        {
            await Parallel.ForEachAsync(
                _lstPdfMetaFileData,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (pdfMetaData, cancellationToken) =>
                {
                    try
                    {
                        await pdfMetaData.GetOrCreateThumbnailAsync(async () =>
                        {
                            return await GetThumbnailForMetadataAsync(pdfMetaData);
                        });
                    }
                    catch (Exception ex)
                    {
                        // Log thumbnail errors but don't show to user - this is background work
                        Logger.LogWarning($"Thumbnail load failed for {pdfMetaData.GetBookName(_rootMusicFolder)}: {ex.Message}");
                    }
                });
        }
        finally
        {
            IsThumbnailLoadingInProgress = false;
        }
    }
    
    /// <summary>
    /// Generate a thumbnail for a PDF metadata item
    /// </summary>
    private async Task<Bitmap> GetThumbnailForMetadataAsync(PdfMetaDataReadResult pdfMetaData)
    {
        return await Task.Run(() =>
        {
            if (pdfMetaData.VolumeInfoList.Count == 0)
            {
                throw new InvalidOperationException("No volumes in metadata");
            }
            
            var pdfPath = pdfMetaData.GetFullPathFileFromVolno(0);
            
            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
            {
                throw new FileNotFoundException($"PDF file not found: {pdfPath}");
            }
            
            // Note: Cloud-only files (OneDrive, etc.) will be downloaded automatically when accessed
            // Future: Could add a config setting to skip cloud-only files if desired
            
            var rotation = pdfMetaData.VolumeInfoList[0].Rotation;
            var pdfRotation = rotation switch
            {
                1 => PdfRotation.Rotate90,
                2 => PdfRotation.Rotate180,
                3 => PdfRotation.Rotate270,
                _ => PdfRotation.Rotate0
            };
            
            using var pdfStream = File.OpenRead(pdfPath);
            using var skBitmap = Conversion.ToImage(pdfStream, page: (Index)0, options: new PDFtoImage.RenderOptions(
                Width: 150,
                Height: 225,
                Rotation: pdfRotation));
            
            using var data = skBitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Seek(0, SeekOrigin.Begin);
            
            return new Bitmap(stream);
        });
    }
    
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Save settings
        var settings = AppSettings.Instance;
        settings.Show2Pages = Show2Pages;
        settings.IsFullScreen = _chkFullScreen?.IsChecked == true;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        
        // Only save position/size if not maximized
        if (WindowState != WindowState.Maximized)
        {
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            settings.WindowLeft = Position.X;
            settings.WindowTop = Position.Y;
        }
        
        if (_currentPdfMetaData != null)
        {
            settings.LastPDFOpen = _currentPdfMetaData.GetFullPathFileFromVolno(0) ?? string.Empty;
        }
        
        settings.Save();
        
        // CloseCurrentPdfFile will save the lastPageNo to the PDF metadata JSON
        CloseCurrentPdfFile();
    }
    
    private async Task ChooseMusicAsync()
    {
        try
        {
            var chooser = new ChooseMusicWindow(_lstPdfMetaFileData, _rootMusicFolder);
            await chooser.ShowDialog(this);
            
            // Update root folder and metadata if they changed in the chooser
            if (!string.IsNullOrEmpty(chooser.CurrentRootFolder) && chooser.CurrentRootFolder != _rootMusicFolder)
            {
                _rootMusicFolder = chooser.CurrentRootFolder;
                _lstPdfMetaFileData = chooser.CurrentPdfMetadata;
            }
            
            if (chooser.ChosenPdfMetaData != null)
            {
                await LoadPdfFileAndShowAsync(chooser.ChosenPdfMetaData, chooser.ChosenPageNo);
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("ChooseMusic dialog error", ex);
        }
    }
    
    private async Task ShowMetaDataFormAsync()
    {
        if (_isShowingMetaDataForm || _currentPdfMetaData == null)
            return;
            
        try
        {
            _isShowingMetaDataForm = true;
            
            var viewModel = new MetaDataFormViewModel(_currentPdfMetaData, _rootMusicFolder, CurrentPageNumber);
            var metaDataForm = new MetaDataFormWindow(viewModel);
            
            await metaDataForm.ShowDialog(this);
            
            // Handle navigation to a specific page if user double-clicked a TOC entry or favorite
            if (metaDataForm.PageNumberResult.HasValue)
            {
                var targetPage = metaDataForm.PageNumberResult.Value;
                if (targetPage >= _currentPdfMetaData.PageNumberOffset && 
                    targetPage < _currentPdfMetaData.MaxPageNum)
                {
                    await ShowPageAsync(targetPage);
                }
            }
            else if (metaDataForm.WasSaved)
            {
                // Reload the PDF metadata if it was saved (PageNumberOffset might have changed)
                ClearCache();
                await LoadPdfFileAndShowAsync(_currentPdfMetaData, CurrentPageNumber);
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("MetaData form error", ex);
        }
        finally
        {
            _isShowingMetaDataForm = false;
        }
    }
    
    internal async Task LoadPdfFileAndShowAsync(PdfMetaDataReadResult pdfMetaData, int pageNo)
    {
        CloseCurrentPdfFile();
        _currentPdfMetaData = pdfMetaData;
        
        _disableSliderValueChanged = true;
        var pageNumberOffset = pdfMetaData.PageNumberOffset;
        var maxPageNum = pdfMetaData.MaxPageNum;
        
        if (_slider != null)
        {
            _slider.Minimum = pageNumberOffset;
            _slider.Maximum = maxPageNum - 1;
            _slider.Value = pageNo;
        }
        _disableSliderValueChanged = false;
        
        MaxPageNumberMinus1 = maxPageNum - 1;
        PdfUIEnabled = true;
        PdfTitle = pdfMetaData.GetBookName(_rootMusicFolder);
        
        // Pre-load PDF bytes for all volumes in the background
        _ = pdfMetaData.PreloadAllVolumeBytesAsync();
        
        // Update thumbnail - load it if not cached
        var thumbnail = pdfMetaData.GetCachedThumbnail<Bitmap>();
        if (thumbnail != null && _imgThumb != null)
        {
            _imgThumb.Source = thumbnail;
        }
        else
        {
            // Load thumbnail in background and update when ready
            _ = Task.Run(async () =>
            {
                try
                {
                    var newThumb = await pdfMetaData.GetOrCreateThumbnailAsync(async () =>
                    {
                        return await GetThumbnailForMetadataAsync(pdfMetaData);
                    });
                    
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_currentPdfMetaData == pdfMetaData && newThumb is Bitmap bmp && _imgThumb != null)
                        {
                            _imgThumb.Source = bmp;
                        }
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Thumbnail load failed: {ex.Message}");
                }
            });
        }
        
        Title = $"{MyAppName} - {PdfTitle}";
        
        await ShowPageAsync(pageNo);
    }
    
    internal void CloseCurrentPdfFile()
    {
        // Save any unsaved ink strokes before closing
        SaveInkFromCurrentCanvases();
        
        _dpPage?.Children.Clear();
        ClearCache();
        
        if (_currentPdfMetaData != null)
        {
            _currentPdfMetaData.LastPageNo = CurrentPageNumber;
            _currentPdfMetaData.IsDirty = true;
            PdfMetaDataCore.SaveToJson(_currentPdfMetaData);
            
            _currentPdfMetaData = null;
            CurrentPageNumber = 0;
        }

        _inkCanvas0 = null;
        _inkCanvas1 = null;
        
        Title = MyAppName;
        MaxPageNumberMinus1 = 0;
        PdfUIEnabled = false;
        PdfTitle = string.Empty;
        Description0 = string.Empty;
        Description1 = string.Empty;
    }
    
    private async Task ShowPageAsync(int pageNo)
    {
        try
        {
            if (_currentPdfMetaData == null)
            {
                _dpPage?.Children.Clear();
                return;
            }
            
            // Save any unsaved ink strokes from the current page before navigating
            SaveInkFromCurrentCanvases();
            
            // Clamp page number
            var pageNumberOffset = _currentPdfMetaData.PageNumberOffset;
            var maxPageNum = _currentPdfMetaData.MaxPageNum;
            
            if (pageNo < pageNumberOffset)
            {
                pageNo = pageNumberOffset;
            }
            if (pageNo >= maxPageNum)
            {
                pageNo = maxPageNum - NumPagesPerView;
                if (pageNo < pageNumberOffset)
                {
                    pageNo = pageNumberOffset;
                }
            }
            
            _disableSliderValueChanged = true;
            CurrentPageNumber = pageNo;
            _disableSliderValueChanged = false;
            
            // Start cache entries for current and adjacent pages immediately (parallel prefetch like WPF)
            var cacheEntry0 = TryAddCacheEntry(pageNo);
            if (cacheEntry0 == null)
            {
                return;
            }

            var cacheEntry1 = Show2Pages && pageNo + 1 < maxPageNum ? TryAddCacheEntry(pageNo + 1) : null;
            
            // Start prefetch of adjacent pages (these run in background, in parallel)
            if (NumPagesPerView == 1)
            {
                TryAddCacheEntry(pageNo + 1);
                TryAddCacheEntry(pageNo + 2);
                TryAddCacheEntry(pageNo - 1);
            }
            else
            {
                TryAddCacheEntry(pageNo + 2);
                TryAddCacheEntry(pageNo + 3);
                TryAddCacheEntry(pageNo + 4);
                TryAddCacheEntry(pageNo + 5);
                TryAddCacheEntry(pageNo - 1);
                TryAddCacheEntry(pageNo - 2);
            }
            
            // Now await the current page(s)
            Bitmap page0Image;
            try
            {
                page0Image = await cacheEntry0.Task;
            }
            catch (OperationCanceledException)
            {
                // Task was cancelled, try again
                cacheEntry0 = TryAddCacheEntry(pageNo);
                if (cacheEntry0 == null) return;
                page0Image = await cacheEntry0.Task;
            }
            
            Bitmap? page1Image = null;
            if (cacheEntry1 != null)
            {
                try
                {
                    page1Image = await cacheEntry1.Task;
                }
                catch (OperationCanceledException)
                {
                    cacheEntry1 = TryAddCacheEntry(pageNo + 1);
                    if (cacheEntry1 != null)
                    {
                        page1Image = await cacheEntry1.Task;
                    }
                }
            }
            
            // Check if user navigated away while we were rendering (type-ahead detection)
            if (CurrentPageNumber != pageNo)
            {
                PurgeIfNecessary(CurrentPageNumber);
                return;
            }
            
            // Get ink stroke data for the pages
            var inkStroke0 = GetInkStrokeForPage(pageNo);
            var inkStroke1 = page1Image != null ? GetInkStrokeForPage(pageNo + 1) : null;
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _dpPage?.Children.Clear();
                if (_dpPage != null)
                {
                    _dpPage.Background = Brushes.LightGray;
                }
                
                _gestureHandler?.ResetTransform();

                var grid = new Grid
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
                };

                if (Show2Pages && page1Image != null)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Pixel) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    _inkCanvas0 = new InkCanvasControl(page0Image, pageNo, inkStroke0)
                    {
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                        IsInkingEnabled = false
                    };
                    Grid.SetColumn(_inkCanvas0, 0);
                    grid.Children.Add(_inkCanvas0);

                    var divider = new Border
                    {
                        Background = Brushes.Gray,
                        Width = 1,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
                    };
                    Grid.SetColumn(divider, 1);
                    grid.Children.Add(divider);

                    _inkCanvas1 = new InkCanvasControl(page1Image, pageNo + 1, inkStroke1)
                    {
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                        IsInkingEnabled = false
                    };
                    Grid.SetColumn(_inkCanvas1, 2);
                    grid.Children.Add(_inkCanvas1);
                }
                else
                {
                    _inkCanvas0 = new InkCanvasControl(page0Image, pageNo, inkStroke0)
                    {
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                        IsInkingEnabled = false
                    };
                    grid.Children.Add(_inkCanvas0);
                    _inkCanvas1 = null;
                }

                _dpPage?.Children.Add(grid);
                SetupGestureHandler();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Description0 = $"Error showing page {pageNo}: {ex.Message}";
            });
            Logger.LogException($"ShowPageAsync page {pageNo}", ex);
        }
    }
    
    private PageCacheEntry? TryAddCacheEntry(int pageNo)
    {
        if (_currentPdfMetaData == null)
            return null;
            
        var pageNumberOffset = _currentPdfMetaData.PageNumberOffset;
        var maxPageNum = _currentPdfMetaData.MaxPageNum;
        
        if (pageNo < pageNumberOffset || pageNo >= maxPageNum)
            return null;
        
        // Check if we already have a valid entry
        if (_pageCache.TryGetValue(pageNo, out var existing) && 
            !existing.Cts.IsCancellationRequested && 
            !existing.Task.IsCanceled)
        {
            existing.Age = _currentCacheAge++; // Update age on access
            return existing;
        }
        
        // Create new entry with tracking wrapper
        var entry = new PageCacheEntry
        {
            PageNo = pageNo,
            Age = _currentCacheAge++,
            Task = RenderPageWithTrackingAsync(pageNo)
        };
        
        // Evict old entries if cache is full
        if (_pageCache.Count >= MaxCacheSize)
        {
            var toRemove = _pageCache.Values
                .OrderBy(e => e.Age)
                .Take(_pageCache.Count - MaxCacheSize + 1)
                .ToList();
            foreach (var old in toRemove)
            {
                _pageCache.Remove(old.PageNo);
            }
        }
        
        _pageCache[pageNo] = entry;
        UpdateCacheStatus();
        return entry;
    }
    
    private void PurgeIfNecessary(int currentPageNo)
    {
        // Cancel tasks for pages that are far from current page (user typed ahead)
        var toDelete = new List<int>();
        foreach (var entry in _pageCache.Values.Where(v => !v.Task.IsCompleted))
        {
            if (entry.PageNo != currentPageNo && 
                entry.PageNo != currentPageNo + 1 &&
                _currentCacheAge - entry.Age > 5)
            {
                toDelete.Add(entry.PageNo);
                entry.Cts.Cancel();
            }
        }
        foreach (var pageNo in toDelete)
        {
            _pageCache.Remove(pageNo);
        }
        UpdateCacheStatus();
    }
    
    private async Task<Bitmap> RenderPageWithTrackingAsync(int pageNo)
    {
        Interlocked.Increment(ref _cacheLoadingCount);
        UpdateCacheStatus();
        try
        {
            return await RenderPageInternalAsync(pageNo);
        }
        finally
        {
            Interlocked.Decrement(ref _cacheLoadingCount);
            UpdateCacheStatus();
        }
    }
    
    private async Task<Bitmap> RenderPageInternalAsync(int pageNo)
    {
        if (_currentPdfMetaData == null)
        {
            throw new InvalidOperationException("No PDF loaded");
        }
        
        return await Task.Run(() =>
        {
            // Get the PDF file path for this page
            var volNo = _currentPdfMetaData.GetVolNumFromPageNum(pageNo);
            var pdfPath = _currentPdfMetaData.GetFullPathFileFromVolno(volNo);
            
            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
            {
                throw new FileNotFoundException($"PDF file not found for page {pageNo}");
            }
            
            // Calculate the page index within this volume
            var pagesInPreviousVolumes = _currentPdfMetaData.VolumeInfoList
                .Take(volNo)
                .Sum(v => v.NPagesInThisVolume);
            var pageIndexInVolume = pageNo - _currentPdfMetaData.PageNumberOffset - pagesInPreviousVolumes;
            
            // Get rotation
            var rotation = _currentPdfMetaData.VolumeInfoList[volNo].Rotation;
            var pdfRotation = rotation switch
            {
                1 => PdfRotation.Rotate90,
                2 => PdfRotation.Rotate180,
                3 => PdfRotation.Rotate270,
                _ => PdfRotation.Rotate0
            };
            
            // Get PDF bytes from metadata cache (loads from disk if not cached)
            var pdfBytes = _currentPdfMetaData.GetOrLoadVolumeBytes(volNo);
            if (pdfBytes == null)
            {
                throw new FileNotFoundException($"PDF file not found for page {pageNo}: {pdfPath}");
            }
            
            // Validate page index against actual PDF page count for better error messages
            var actualPageCount = Conversion.GetPageCount(pdfBytes);
            
            if (pageIndexInVolume < 0 || pageIndexInVolume >= actualPageCount)
            {
                var metadataPageCount = _currentPdfMetaData.VolumeInfoList[volNo].NPagesInThisVolume;
                throw new ArgumentOutOfRangeException(nameof(pageIndexInVolume),
                    $"Page index {pageIndexInVolume} is invalid for PDF '{Path.GetFileName(pdfPath)}' " +
                    $"which has {actualPageCount} pages (metadata claims {metadataPageCount}). " +
                    $"Delete the .json metadata file to regenerate it.");
            }
            
            using var skBitmap = Conversion.ToImage(pdfBytes, page: (Index)pageIndexInVolume, 
                options: new PDFtoImage.RenderOptions(Dpi: 150, Rotation: pdfRotation));
            using var image = SKImage.FromBitmap(skBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Seek(0, SeekOrigin.Begin);
            
            return new Bitmap(stream);
        });
    }
    
    private void ClearCache()
    {
        foreach (var entry in _pageCache.Values)
        {
            entry.Cts.Cancel();
        }
        _pageCache.Clear();
        _currentCacheAge = 0;
        
        // Clear PDF bytes cache on the current metadata to free memory
        _currentPdfMetaData?.ClearPdfBytesCache();
        
        UpdateCacheStatus();
    }
    
    private void UpdateCacheStatus()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var pendingCount = _cacheLoadingCount;
            var cachedCount = _pageCache.Count;
            
            // Always show cache count; add loading indicator if busy
            if (pendingCount > 0)
            {
                CacheStatus = $"C:{cachedCount} ⏳{pendingCount}";
            }
            else if (cachedCount > 0)
            {
                CacheStatus = $"C:{cachedCount}";
            }
            else
            {
                CacheStatus = string.Empty;
            }
        });
    }
    
    private string GetDescription(int pageNo)
    {
        if (_currentPdfMetaData == null) return string.Empty;
        
        // Find TOC entry for this page or nearest one before it
        var tocEntry = _currentPdfMetaData.TocEntries.FirstOrDefault(t => t.PageNo == pageNo);
        
        if (tocEntry == null)
        {
            tocEntry = _currentPdfMetaData.TocEntries
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
        
        return $"Page {pageNo}";
    }
    
    /// <summary>
    /// Gets ink stroke data for a specific page, if available
    /// </summary>
    private InkStrokeClass? GetInkStrokeForPage(int pageNo)
    {
        if (_currentPdfMetaData == null)
            return null;
        
        return _currentPdfMetaData.InkStrokes.FirstOrDefault(ink => ink.Pageno == pageNo);
    }
    
    /// <summary>
    /// Saves ink strokes from the current ink canvases to the PDF metadata
    /// </summary>
    private void SaveInkFromCurrentCanvases()
    {
        if (_currentPdfMetaData == null)
            return;
            
        bool anyChanges = false;
        
        // Save ink from canvas 0
        if (_inkCanvas0 != null && _inkCanvas0.HasUnsavedStrokes)
        {
            var inkData = _inkCanvas0.GetInkStrokeDataForSaving();
            UpdateInkStrokeInMetadata(_inkCanvas0.PageNo, inkData);
            _inkCanvas0.MarkAsSaved();
            anyChanges = true;
        }
        
        // Save ink from canvas 1
        if (_inkCanvas1 != null && _inkCanvas1.HasUnsavedStrokes)
        {
            var inkData = _inkCanvas1.GetInkStrokeDataForSaving();
            UpdateInkStrokeInMetadata(_inkCanvas1.PageNo, inkData);
            _inkCanvas1.MarkAsSaved();
            anyChanges = true;
        }
        
        // Save metadata if there were changes
        if (anyChanges)
        {
            _currentPdfMetaData.IsDirty = true;
            PdfMetaDataCore.SaveToJson(_currentPdfMetaData);
            Trace.WriteLine($"Saved ink strokes for current pages");
        }
    }
    
    /// <summary>
    /// Updates or removes ink stroke data for a specific page in the metadata
    /// </summary>
    private void UpdateInkStrokeInMetadata(int pageNo, InkStrokeClass? inkData)
    {
        if (_currentPdfMetaData == null)
            return;
            
        // Remove existing ink stroke for this page
        var existingIndex = _currentPdfMetaData.InkStrokes.FindIndex(ink => ink.Pageno == pageNo);
        if (existingIndex >= 0)
        {
            _currentPdfMetaData.InkStrokes.RemoveAt(existingIndex);
        }
        
        // Add new ink stroke if there's data
        if (inkData != null)
        {
            _currentPdfMetaData.InkStrokes.Add(inkData);
        }
    }
    
    private void SetupGestureHandler()
    {
        if (_dpPage == null) return;
        
        _gestureHandler?.Detach();
        
        _gestureHandler = new GestureHandler(_dpPage, enableLogging: false)
        {
            NumPagesPerView = NumPagesPerView
        };
        
        _gestureHandler.NavigationRequested += (s, e) =>
        {
            NavigateAsync(e.Delta);
        };
        
        _gestureHandler.DoubleTapped += (s, e) =>
        {
            _gestureHandler.ResetTransform();
        };
        
        UpdateGestureHandlerState();
    }

    private void UpdateGestureHandlerState()
    {
        if (_gestureHandler != null)
        {
            _gestureHandler.IsDisabled = 
                (_chkInk0?.IsChecked == true) || 
                (_chkInk1?.IsChecked == true);
        }
    }
    
    private void NavigateAsync(int delta)
    {
        _lastNavigationDelta = delta; // Track navigation direction
        var newPage = CurrentPageNumber + delta;
        
        if (_currentPdfMetaData != null)
        {
            var pageNumberOffset = _currentPdfMetaData.PageNumberOffset;
            var maxPageNum = _currentPdfMetaData.MaxPageNum;
            newPage = Math.Max(pageNumberOffset, Math.Min(newPage, maxPageNum - 1));
        }
        
        if (newPage != CurrentPageNumber)
        {
            CurrentPageNumber = newPage;
            _ = ShowPageAsync(newPage);
        }
    }
    
    private void BtnPrevNext_Click(bool isPrevious)
    {
        
        if (_currentPdfMetaData != null && _currentPdfMetaData.Favorites.Count > 0)
        {
            // Navigate to favorites
            var favPages = _currentPdfMetaData.Favorites.Select(f => f.Pageno).OrderBy(p => p).ToList();
            
            if (isPrevious)
            {
                var prevFav = favPages.Where(p => p < CurrentPageNumber).LastOrDefault();
                if (prevFav > 0)
                {
                    _ = ShowPageAsync(prevFav);
                    return;
                }
            }
            else
            {
                var nextFav = favPages.Where(p => p > CurrentPageNumber).FirstOrDefault();
                if(nextFav > 0)
                {
                    _ = ShowPageAsync(nextFav);
                    return;
                }
            }
        }
        
        // Regular navigation
        var delta = isPrevious ? -NumPagesPerView : NumPagesPerView;
        NavigateAsync(delta);
    }
    
    #region Event Handlers
    
    private void Slider_TemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (_slider != null)
        {
            var thumb = e.NameScope.Find<Thumb>("thumb");
            if (thumb == null)
            {
                var track = e.NameScope.Find<Track>("PART_Track");
                thumb = track?.Thumb;
            }
            if (thumb != null)
            {
                thumb.DragStarted += OnSliderThumbDragStarted;
                thumb.DragCompleted += OnSliderThumbDragCompleted;
            }
        }
    }
    
    private void Slider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        OnSliderValueChanged();
    }
    
    private void OnSliderThumbDragStarted(object? sender, VectorEventArgs e)
    {
        _disableSliderValueChanged = true;
        if (_sliderPopup != null)
        {
            _sliderPopup.IsOpen = true;
        }
        UpdateSliderPopupText();
    }
    
    private void OnSliderThumbDragCompleted(object? sender, VectorEventArgs e)
    {
        _disableSliderValueChanged = false;
        if (_sliderPopup != null)
        {
            _sliderPopup.IsOpen = false;
        }
        OnSliderValueChanged();
    }
    
    private void OnSliderValueChanged()
    {
        UpdateSliderPopupText();
        
        if (!_disableSliderValueChanged && _currentPdfMetaData != null)
        {
            _ = ShowPageAsync(CurrentPageNumber);
        }
    }
    
    private void UpdateSliderPopupText()
    {
        if (_tbSliderPopup != null && _slider != null)
        {
            var pageNumber = (int)_slider.Value;
            var description = GetDescription(pageNumber);
            _tbSliderPopup.Text = string.IsNullOrEmpty(description) 
                ? $"{pageNumber}" 
                : $"{pageNumber} {description}";
        }
    }
    
    private void ChkFav_Toggled(object? sender, RoutedEventArgs e)
    {
        if (!_chkFavoriteEnabled || _currentPdfMetaData == null) return;
        
        if (sender is CheckBox chk)
        {
            var isPage0 = chk.Name == "chkFav0";
            var pageNo = CurrentPageNumber + (isPage0 ? 0 : 1);
            var isFavorite = chk.IsChecked == true;
            
            // Toggle favorite in metadata
            // TODO: Update the favorites list and save using PdfMetaDataCore.SaveToJson()
            
            Trace.WriteLine($"Favorite toggled: Page {pageNo}, IsFavorite: {isFavorite}");
        }
    }
    
    private async void BtnRotate_Click(object? sender, RoutedEventArgs e)
    {
        
        // TODO: Rotate the page in metadata and save using PdfMetaDataCore.SaveToJson()
        
        Trace.WriteLine($"Rotate clicked for page {CurrentPageNumber}");
        
        ClearCache();
        await ShowPageAsync(CurrentPageNumber);
    }
    
    private void ChkFullScreenToggled(bool isChecked)
    {
        if (isChecked)
        {
            this.WindowState = WindowState.Maximized;
            this.SystemDecorations = SystemDecorations.None;
        }
        else
        {
            this.SystemDecorations = SystemDecorations.Full;
            // Only reset to Normal if we're coming_FROM_ full screen, not on startup
            // Check if we should restore maximized state from settings
            if (!AppSettings.Instance.WindowMaximized)
            {
                this.WindowState = WindowState.Normal;
            }
        }
    }
    
    private void BtnAbout_Click(object? sender, RoutedEventArgs e)
    {
        var aboutMessage = $"{MyAppName}\n\n" +
                          $"Version: {BuildInfo.Version}\n" +
                          $"Branch: {BuildInfo.GitBranch}\n" +
                          $"Commit: {BuildInfo.GitCommit}\n" +
                          $"Build Time: {BuildInfo.BuildTime}\n\n" +
                          $"Cross-platform PDF sheet music viewer\n" +
                          $"Built with Avalonia UI and PDFtoImage\n\n" +
                          $".NET Runtime: {Environment.Version}";
        
        // Simple dialog using a window with keyboard support
        var dialog = new Window
        {
            Title = "About (Ctrl+C to copy)",
            Width = 380,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new TextBlock
            {
                Text = aboutMessage,
                Margin = new Avalonia.Thickness(20),
                TextWrapping = TextWrapping.Wrap
            }
        };
        
        // Handle keyboard shortcuts
        dialog.KeyDown += async (s, args) =>
        {
            if (args.Key == Key.Escape)
            {
                dialog.Close();
                args.Handled = true;
            }
            else if (args.Key == Key.C && args.KeyModifiers == KeyModifiers.Control)
            {
                // Copy about info to clipboard
                var clipboard = TopLevel.GetTopLevel(dialog)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(aboutMessage);
                    // Brief visual feedback - update title temporarily
                    dialog.Title = "About - Copied!";
                    await Task.Delay(1000);
                    dialog.Title = "About (Ctrl+C to copy)";
                }
                args.Handled = true;
            }
        };
        
        dialog.ShowDialog(this);
    }
    
    private async void MnuShowLogs_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var logContent = Logger.ReadLog();
            var logPath = Logger.LogFilePath;
            
            if (string.IsNullOrEmpty(logContent))
            {
                logContent = "(No log entries yet)";
            }
            else
            {
                // Show last 100 lines max to keep dialog manageable
                var lines = logContent.Split('\n');
                if (lines.Length > 100)
                {
                    logContent = $"(Showing last 100 of {lines.Length} lines)\n\n" +
                                 string.Join("\n", lines.TakeLast(100));
                }
            }
            
            var dialog = new Window
            {
                Title = $"Application Logs - {logPath}",
                Width = 900,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            var textBox = new TextBox
            {
                Text = logContent,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontSize = 11,
                Margin = new Avalonia.Thickness(10),
                CaretIndex = logContent.Length // Scroll to end
            };
            
            var scrollViewer = new ScrollViewer
            {
                Content = textBox,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            };
            Grid.SetRow(scrollViewer, 0);
            grid.Children.Add(scrollViewer);
            
            var buttonPanel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Avalonia.Thickness(10),
                Spacing = 10
            };
            
            var btnOpenFolder = new Button { Content = "Open Log Folder" };
            btnOpenFolder.Click += (s, args) => Logger.OpenLogFolder();
            buttonPanel.Children.Add(btnOpenFolder);
            
            var btnCopy = new Button { Content = "Copy All" };
            btnCopy.Click += async (s, args) =>
            {
                var clipboard = TopLevel.GetTopLevel(dialog)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(logContent);
                    btnCopy.Content = "Copied!";
                    await Task.Delay(1000);
                    btnCopy.Content = "Copy All";
                }
            };
            buttonPanel.Children.Add(btnCopy);
            
            var btnClose = new Button { Content = "Close" };
            btnClose.Click += (s, args) => dialog.Close();
            buttonPanel.Children.Add(btnClose);
            
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);
            
            dialog.Content = grid;
            
            // Handle keyboard shortcuts
            dialog.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    dialog.Close();
                    args.Handled = true;
                }
            };
            
            // Scroll to end after dialog opens
            dialog.Opened += (s, args) =>
            {
                textBox.CaretIndex = logContent.Length;
            };
            
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Logger.LogException("Error showing logs", ex);
        }
    }
    
    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        // Handle Alt+key combinations
        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            switch (e.Key)
            {
                case Key.Q:
                    Close();
                    e.Handled = true;
                    return;
                case Key.C:
                    _ = ChooseMusicAsync();
                    e.Handled = true;
                    return;
                case Key.E:
                    _ = ShowMetaDataFormAsync();
                    e.Handled = true;
                    return;
                case Key.F:
                    if (_chkFullScreen != null)
                    {
                        _chkFullScreen.IsChecked = !_chkFullScreen.IsChecked;
                    }
                    e.Handled = true;
                    return;
                case Key.M:
                    // Open the menu - find and open the MenuItem
                    if (_mainMenu?.Items.Count > 0 && _mainMenu.Items[0] is MenuItem menuItem)
                    {
                        menuItem.Open();
                    }
                    e.Handled = true;
                    return;
                default:
                    // Don't handle other Alt+key combos - let them pass through for menu access keys
                    return;
            }
        }
        
        // Handle navigation keys (no modifiers)
        if (e.KeyModifiers == KeyModifiers.None)
        {
            switch (e.Key)
            {
                case Key.Home:
                    if (_currentPdfMetaData != null)
                    {
                        CurrentPageNumber = _currentPdfMetaData.PageNumberOffset;
                        _ = ShowPageAsync(CurrentPageNumber);
                    }
                    e.Handled = true;
                    break;
                case Key.End:
                    if (_currentPdfMetaData != null)
                    {
                        CurrentPageNumber = _currentPdfMetaData.MaxPageNum - 1;
                        _ = ShowPageAsync(CurrentPageNumber);
                    }
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.Up:
                case Key.PageUp:
                    NavigateAsync(-NumPagesPerView);
                    e.Handled = true;
                    break;
                case Key.Right:
                case Key.Down:
                case Key.PageDown:
                    NavigateAsync(NumPagesPerView);
                    e.Handled = true;
                    break;
            }
        }
    }
    
    #endregion
    
    #region Properties
    
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public int CurrentPageNumber
    {
        get => _currentPageNumber;
        set
        {
            _currentPageNumber = value;
            OnPropertyChanged();
        }
    }

    public int MaxPageNumberMinus1
    {
        get => _maxPageNumberMinus1;
        set
        {
            _maxPageNumberMinus1 = value;
            OnPropertyChanged();
        }
    }

    public int NumPagesPerView => _show2Pages ? 2 : 1;

    public bool Show2Pages
    {
        get => _show2Pages;
        set
        {
            _show2Pages = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NumPagesPerView));
            
            if (_gestureHandler != null)
            {
                _gestureHandler.NumPagesPerView = NumPagesPerView;
            }
            
            ClearCache();
            _ = ShowPageAsync(CurrentPageNumber);
        }
    }

    public bool PdfUIEnabled
    {
        get => _pdfUIEnabled;
        set
        {
            _pdfUIEnabled = value;
            OnPropertyChanged();
        }
    }

    public string PdfTitle
    {
        get => _pdfTitle;
        set
        {
            _pdfTitle = value;
            OnPropertyChanged();
        }
    }

    public string Description0
    {
        get => _description0;
        set
        {
            _description0 = value;
            OnPropertyChanged();
        }
    }

    public string Description1
    {
        get => _description1;
        set
        {
            _description1 = value;
            OnPropertyChanged();
        }
    }

    public bool IsThumbnailLoadingInProgress
    {
        get => _isThumbnailLoadingInProgress;
        set
        {
            _isThumbnailLoadingInProgress = value;
            OnPropertyChanged();
        }
    }

    public string CacheStatus
    {
        get => _cacheStatus;
        set
        {
            _cacheStatus = value;
            OnPropertyChanged();
        }
    }

   
    public int CacheLoadingCount
    {
        get => _cacheLoadingCount;
        set
        {
            _cacheLoadingCount = value;
            OnPropertyChanged();
        }
    }
    
    #endregion
}
