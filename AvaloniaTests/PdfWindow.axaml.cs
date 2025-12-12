using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using PDFtoImage;
using SkiaSharp;

namespace AvaloniaTests;

public partial class PdfWindow : Window, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    private int _currentPageNumber = 1;
    private int _touchCount;
    private bool _show2Pages = true;
    private bool _pdfUIEnabled;
    private string _pdfFileName = string.Empty;
    private string _pdfTitle = string.Empty;
    private string _description0 = string.Empty;
    private string _description1 = string.Empty;
    private int _pageCount = 0;
    private bool _disableSliderValueChanged;
    
    private InkCanvasControl? _inkCanvas0;
    private InkCanvasControl? _inkCanvas1;
    private GestureHandler? _gestureHandler;
    private Panel? _dpPage;
    private Slider? _slider;
    private Popup? _sliderPopup;
    private TextBlock? _tbSliderPopup;
    private int _maxPageNumberMinus1;

    public PdfWindow()
    {
        InitializeComponent();
        DataContext = this;
        
        // Get the PDF file path - cross-platform friendly
        var homeFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Path.Combine(homeFolder, "OneDrive");
        if (!Directory.Exists(folder))
        {
            folder = Path.Combine(homeFolder, "Documents");
        }
        _pdfFileName = Path.Combine(folder, "SheetMusic", "Pop", "PopSingles", "Be Our Guest - G Major - MN0174098.pdf");
        
        // Initialize with PDF info
        PdfTitle = Path.GetFileName(_pdfFileName);
        CurrentPageNumber = 1;
        MaxPageNumberMinus1 = 3;
        PdfUIEnabled = true;
        Description0 = "Page 1";
        Description1 = "Page 2";
        
        // Wire up ink checkbox events
        var chkInk0 = this.FindControl<CheckBox>("chkInk0");
        var chkInk1 = this.FindControl<CheckBox>("chkInk1");
        
        if (chkInk0 != null)
        {
            chkInk0.IsCheckedChanged += (s, e) => 
            { 
                if (_inkCanvas0 != null) 
                    _inkCanvas0.IsInkingEnabled = chkInk0.IsChecked == true;
                UpdateGestureHandlerState();
            };
        }
        
        if (chkInk1 != null)
        {
            chkInk1.IsCheckedChanged += (s, e) => 
            { 
                if (_inkCanvas1 != null) 
                    _inkCanvas1.IsInkingEnabled = chkInk1.IsChecked == true;
                UpdateGestureHandlerState();
            };
        }
        
        // Wire up favorite checkbox events
        var chkFav0 = this.FindControl<CheckBox>("chkFav0");
        var chkFav1 = this.FindControl<CheckBox>("chkFav1");
        
        if (chkFav0 != null)
        {
            chkFav0.IsCheckedChanged += ChkFav_Toggled;
        }
        
        if (chkFav1 != null)
        {
            chkFav1.IsCheckedChanged += ChkFav_Toggled;
        }
        
        // Wire up rotate button
        var btnRotate = this.FindControl<Button>("btnRotate");
        if (btnRotate != null)
        {
            btnRotate.Click += BtnRotate_Click;
        }
        
        // Wire up full screen checkbox event and set it as default
        var chkFullScreen = this.FindControl<CheckBox>("chkFullScreen");
        if (chkFullScreen != null)
        {
            chkFullScreen.IsCheckedChanged += (s, e) =>
            {
                ChkFullScreenToggled(chkFullScreen.IsChecked == true);
            };
            
            // Set full screen as default
            chkFullScreen.IsChecked = true;
        }
        
        // Wire up full screen menu item
        var mnuFullScreen = this.FindControl<MenuItem>("mnuFullScreen");
        if (mnuFullScreen != null)
        {
            mnuFullScreen.Click += MnuFullScreen_Click;
        }
        
        // Wire up quit menu item
        var mnuQuit = this.FindControl<MenuItem>("mnuQuit");
        if (mnuQuit != null)
        {
            mnuQuit.Click += MnuQuit_Click;
        }
        
        // Wire up navigation buttons
        var btnPrev = this.FindControl<Button>("btnPrev");
        var btnNext = this.FindControl<Button>("btnNext");
        if (btnPrev != null)
        {
            btnPrev.Click += (s, e) => NavigateAsync(-NumPagesPerView);
        }
        if (btnNext != null)
        {
            btnNext.Click += (s, e) => NavigateAsync(NumPagesPerView);
        }
        
        // Wire up slider events
        _slider = this.FindControl<Slider>("slider");
        _sliderPopup = this.FindControl<Popup>("SliderPopup");
        _tbSliderPopup = this.FindControl<TextBlock>("tbSliderPopup");
        
        if (_slider != null)
        {
            // Hook into template applied to get the thumb
            _slider.TemplateApplied += Slider_TemplateApplied;
            _slider.AddHandler(RangeBase.ValueChangedEvent, Slider_ValueChanged);
        }
        
        // Add keyboard handler for navigation and Alt-Q
        this.KeyDown += Window_KeyDown;
        
        // Load and display the PDF pages
        Loaded += async (s, e) => await LoadAndDisplayPagesAsync();
        
        // Clean up on close
        Closed += (s, e) => _gestureHandler?.Detach();
    }
    
    private void Slider_TemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        // Try to find the thumb in the slider's template
        if (_slider != null)
        {
            var thumb = e.NameScope.Find<Thumb>("thumb");
            if (thumb == null)
            {
                // Try to find the track and get its thumb
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
        
        if (!_disableSliderValueChanged && _pageCount > 0)
        {
            _ = ShowPageAsync(CurrentPageNumber);
        }
    }
    
    private void UpdateSliderPopupText()
    {
        if (_tbSliderPopup != null)
        {
            // Show page number and song name like WPF version
            // Format: "47 Song Title Here" or just "47" if no description available
            var description = GetDescription(CurrentPageNumber);
            _tbSliderPopup.Text = string.IsNullOrEmpty(description) 
                ? $"{CurrentPageNumber}" 
                : $"{CurrentPageNumber} {description}";
        }
    }
    
    private string GetDescription(int pageNo)
    {
        // In the full implementation, this would query the TOC for the song name
        // like the WPF version does with currentPdfMetaData?.GetDescription(pageNo)
        // For now, return empty - the page number is already shown separately
        return string.Empty;
    }
    
    private void ChkFav_Toggled(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox chk)
        {
            var isPage0 = chk.Name == "chkFav0";
            var pageNo = CurrentPageNumber + (isPage0 ? 0 : 1);
            var isFavorite = chk.IsChecked == true;
            
            // In the full implementation, this would toggle the favorite in metadata
            // For now, just update the touch count to show feedback
            TouchCount++;
            
            System.Diagnostics.Debug.WriteLine($"Favorite toggled: Page {pageNo}, IsFavorite: {isFavorite}");
        }
    }
    
    private async void BtnRotate_Click(object? sender, RoutedEventArgs e)
    {
        // In the full implementation, this would rotate the page in metadata
        // For now, just reload the page to show it works
        TouchCount++;
        
        System.Diagnostics.Debug.WriteLine($"Rotate clicked for page {CurrentPageNumber}");
        
        // Reload the current page
        await ShowPageAsync(CurrentPageNumber);
    }

    private void SetupGestureHandler()
    {
        if (_dpPage == null) return;
        
        // Detach old handler if exists
        _gestureHandler?.Detach();
        
        // Create new gesture handler (logging goes to Debug output window)
        _gestureHandler = new GestureHandler(_dpPage, enableLogging: true)
        {
            NumPagesPerView = NumPagesPerView
        };
        
        // Wire up navigation
        _gestureHandler.NavigationRequested += (s, e) =>
        {
            NavigateAsync(e.Delta);
        };
        
        // Wire up double-tap to reset zoom
        _gestureHandler.DoubleTapped += (s, e) =>
        {
            _gestureHandler.ResetTransform();
        };
        
        // Update disabled state based on inking
        UpdateGestureHandlerState();
    }

    private void UpdateGestureHandlerState()
    {
        if (_gestureHandler != null)
        {
            var chkInk0 = this.FindControl<CheckBox>("chkInk0");
            var chkInk1 = this.FindControl<CheckBox>("chkInk1");
            
            // Disable gestures when inking is enabled
            _gestureHandler.IsDisabled = 
                (chkInk0?.IsChecked == true) || 
                (chkInk1?.IsChecked == true);
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        // Handle Alt-Q for quit
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Q)
        {
            Close();
            e.Handled = true;
            return;
        }
        
        // Handle Alt-F for full screen toggle
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.F)
        {
            var chkFullScreen = this.FindControl<CheckBox>("chkFullScreen");
            if (chkFullScreen != null)
            {
                chkFullScreen.IsChecked = !chkFullScreen.IsChecked;
            }
            e.Handled = true;
            return;
        }
        
        // Handle navigation keys
        switch (e.Key)
        {
            case Key.Home:
                CurrentPageNumber = 1;
                _ = ShowPageAsync(1);
                e.Handled = true;
                break;
            case Key.End:
                CurrentPageNumber = _pageCount;
                _ = ShowPageAsync(_pageCount);
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

    private void NavigateAsync(int delta)
    {
        TouchCount++;
        var newPage = CurrentPageNumber + delta;
        newPage = Math.Max(1, Math.Min(newPage, _pageCount));
        
        if (newPage != CurrentPageNumber)
        {
            CurrentPageNumber = newPage;
            _ = ShowPageAsync(newPage);
        }
    }

    private void MnuQuit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MnuFullScreen_Click(object? sender, RoutedEventArgs e)
    {
        var chkFullScreen = this.FindControl<CheckBox>("chkFullScreen");
        if (chkFullScreen != null)
        {
            chkFullScreen.IsChecked = !chkFullScreen.IsChecked;
        }
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
            this.WindowState = WindowState.Normal;
        }
    }

    private async Task LoadAndDisplayPagesAsync()
    {
        try
        {
            if (!File.Exists(_pdfFileName))
            {
                Description0 = $"PDF file not found: {_pdfFileName}";
                return;
            }

            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            {
                Description0 = "PDF rendering is not supported on this platform";
                return;
            }

            using (var pdfStream = File.OpenRead(_pdfFileName))
            {
                _pageCount = PDFtoImage.Conversion.GetPageCount(pdfStream);
            }
            MaxPageNumberMinus1 = _pageCount - 1;
            
            await ShowPageAsync(1);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Description0 = $"Error loading PDF: {ex.Message}";
            });
        }
    }

    private async Task ShowPageAsync(int pageNumber)
    {
        try
        {
            var page0Image = await RenderPageAsync(_pdfFileName, pageNumber);
            
            Bitmap? page1Image = null;
            if (_pageCount > pageNumber && Show2Pages)
            {
                page1Image = await RenderPageAsync(_pdfFileName, pageNumber + 1);
            }
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _dpPage = this.FindControl<Panel>("dpPage");
                if (_dpPage != null)
                {
                    _dpPage.Children.Clear();
                    _dpPage.Background = Avalonia.Media.Brushes.LightGreen;
                    
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

                        _inkCanvas0 = new InkCanvasControl(page0Image)
                        {
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                            IsInkingEnabled = false
                        };
                        Grid.SetColumn(_inkCanvas0, 0);
                        grid.Children.Add(_inkCanvas0);

                        var divider = new Border
                        {
                            Background = Avalonia.Media.Brushes.Gray,
                            Width = 1,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
                        };
                        Grid.SetColumn(divider, 1);
                        grid.Children.Add(divider);

                        _inkCanvas1 = new InkCanvasControl(page1Image)
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
                        _inkCanvas0 = new InkCanvasControl(page0Image)
                        {
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                            IsInkingEnabled = false
                        };
                        grid.Children.Add(_inkCanvas0);
                        _inkCanvas1 = null;
                    }

                    _dpPage.Children.Add(grid);
                    SetupGestureHandler();
                }
                
                Description0 = $"Page {pageNumber} of {_pageCount}";
                Description1 = (page1Image != null && pageNumber + 1 <= _pageCount) ? 
                    $"Page {pageNumber + 1} of {_pageCount}" : "";
                    
                var chkInk0 = this.FindControl<CheckBox>("chkInk0");
                var chkInk1 = this.FindControl<CheckBox>("chkInk1");
                if (chkInk0 != null && _inkCanvas0 != null)
                {
                    _inkCanvas0.IsInkingEnabled = chkInk0.IsChecked == true;
                }
                if (chkInk1 != null && _inkCanvas1 != null)
                {
                    _inkCanvas1.IsInkingEnabled = chkInk1.IsChecked == true;
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Description0 = $"Error showing page {pageNumber}: {ex.Message}";
            });
        }
    }

    private async Task<Bitmap> RenderPageAsync(string pdfFilePath, int pageIndex)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("PDF rendering is not supported on this platform");
        }

        return await Task.Run(() =>
        {
            using var pdfStream = File.OpenRead(pdfFilePath);
            var zeroBasedPageIndex = pageIndex - 1;
            using var skBitmap = PDFtoImage.Conversion.ToImage(pdfStream, page: zeroBasedPageIndex, options: new(Dpi: 96));
            using var image = SKImage.FromBitmap(skBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Seek(0, SeekOrigin.Begin);
            return new Bitmap(stream);
        });
    }

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

    public int TouchCount
    {
        get => _touchCount;
        set
        {
            _touchCount = value;
            OnPropertyChanged();
        }
    }
}
