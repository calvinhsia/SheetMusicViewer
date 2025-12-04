using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SysDrawing = System.Drawing;
using SysDrawingImaging = System.Drawing.Imaging;

namespace AvaloniaSimpleApp;

public partial class PdfViewerWindow : Window, INotifyPropertyChanged
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
    private readonly int _pageNo = 1; // Start at page 1 (0-indexed)
    
    private InkCanvasControl? _inkCanvas0;
    private InkCanvasControl? _inkCanvas1;

    public PdfViewerWindow()
    {
        InitializeComponent();
        DataContext = this;
        
        // Get the PDF file path
        var username = Environment.UserName;
        var folder = $@"C:\Users\{username}\OneDrive";
        if (!Directory.Exists(folder))
        {
            folder = @"d:\OneDrive";
        }
        _pdfFileName = $@"{folder}\SheetMusic\Pop\PopSingles\Be Our Guest - G Major - MN0174098.pdf";
        
        // Initialize with PDF info
        PdfTitle = System.IO.Path.GetFileName(_pdfFileName);
        CurrentPageNumber = 1;
        MaxPageNumberMinus1 = 3; // Assume 4 pages for now
        PdfUIEnabled = true;
        Description0 = "Page 1";
        Description1 = "Page 2";
        
        // Wire up ink checkbox events
        var chkInk0 = this.FindControl<CheckBox>("chkInk0");
        var chkInk1 = this.FindControl<CheckBox>("chkInk1");
        
        if (chkInk0 != null)
        {
            chkInk0.Checked += (s, e) => { if (_inkCanvas0 != null) _inkCanvas0.IsInkingEnabled = true; };
            chkInk0.Unchecked += (s, e) => { if (_inkCanvas0 != null) _inkCanvas0.IsInkingEnabled = false; };
        }
        
        if (chkInk1 != null)
        {
            chkInk1.Checked += (s, e) => { if (_inkCanvas1 != null) _inkCanvas1.IsInkingEnabled = true; };
            chkInk1.Unchecked += (s, e) => { if (_inkCanvas1 != null) _inkCanvas1.IsInkingEnabled = false; };
        }
        
        // Load and display the PDF pages
        Loaded += async (s, e) => await LoadAndDisplayPagesAsync();
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

            using var pdfDoc = PdfiumViewer.PdfDocument.Load(_pdfFileName);
            MaxPageNumberMinus1 = pdfDoc.PageCount - 1;
            
            var page0Image = await RenderPageAsync(pdfDoc, 1);
            
            Bitmap? page1Image = null;
            if (pdfDoc.PageCount > 1)
            {
                page1Image = await RenderPageAsync(pdfDoc, 2);
            }
            
            // Update UI on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var dpPage = this.FindControl<Panel>("dpPage");
                if (dpPage != null)
                {
                    dpPage.Children.Clear();
                    
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    
                    // Left page (page 1) with ink annotation
                    _inkCanvas0 = new InkCanvasControl(page0Image)
                    {
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        IsInkingEnabled = false
                    };
                    Grid.SetColumn(_inkCanvas0, 0);
                    grid.Children.Add(_inkCanvas0);
                    
                    // Right page (page 2) with ink annotation
                    if (page1Image != null)
                    {
                        _inkCanvas1 = new InkCanvasControl(page1Image)
                        {
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                            IsInkingEnabled = false
                        };
                        Grid.SetColumn(_inkCanvas1, 1);
                        grid.Children.Add(_inkCanvas1);
                    }
                    
                    dpPage.Children.Add(grid);
                }
                
                Description0 = $"Page 1 of {pdfDoc.PageCount}";
                Description1 = pdfDoc.PageCount > 1 ? $"Page 2 of {pdfDoc.PageCount}" : "";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Description0 = $"Error loading PDF: {ex.Message}";
            });
        }
    }

    private async Task<Bitmap> RenderPageAsync(PdfiumViewer.PdfDocument pdfDoc, int pageIndex)
    {
        return await Task.Run(() =>
        {
            var pageSize = pdfDoc.PageSizes[pageIndex];
            var dpi = 96;
            var width = (int)(pageSize.Width * dpi / 72.0);
            var height = (int)(pageSize.Height * dpi / 72.0);
            
            using var bitmap = pdfDoc.Render(pageIndex, width, height, dpi, dpi, false);
            
            using var strm = new MemoryStream();
            bitmap.Save(strm, SysDrawingImaging.ImageFormat.Png);
            strm.Seek(0, SeekOrigin.Begin);
            
            return new Bitmap(strm);
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

    public int MaxPageNumberMinus1 { get; set; }

    public int NumPagesPerView => _show2Pages ? 2 : 1;

    public bool Show2Pages
    {
        get => _show2Pages;
        set
        {
            _show2Pages = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NumPagesPerView));
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
