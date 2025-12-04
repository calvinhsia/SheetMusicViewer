using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SysDrawing = System.Drawing;
using SysDrawingImaging = System.Drawing.Imaging;

namespace AvaloniaSimpleApp;

public partial class MainWindow : Window
{
    private int _iterationCount = 0;
    private readonly Dictionary<ulong, int> _dictCheckSums = new();
    private CancellationTokenSource? _cts;
    private bool _isRunning = false;
    private string _pdfFileName = string.Empty;
    private readonly int _pageNo = 1;

    public MainWindow()
    {
        InitializeComponent();
        
        // Set window to maximized
        WindowState = WindowState.Maximized;
        
        // Get the PDF file path
        var username = Environment.UserName;
        var folder = $@"C:\Users\{username}\OneDrive";
        if (!Directory.Exists(folder))
        {
            folder = @"d:\OneDrive";
        }
        _pdfFileName = $@"{folder}\SheetMusic\Pop\PopSingles\Be Our Guest - G Major - MN0174098.pdf";
        
        // Log diagnostic info
        var exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        System.Diagnostics.Debug.WriteLine($"App location: {exePath}");
        System.Diagnostics.Debug.WriteLine($"PDF file exists: {File.Exists(_pdfFileName)}");
        if (!string.IsNullOrEmpty(exePath))
        {
            var x64Path = Path.Combine(exePath, "x64", "pdfium.dll");
            System.Diagnostics.Debug.WriteLine($"pdfium.dll path: {x64Path}");
            System.Diagnostics.Debug.WriteLine($"pdfium.dll exists: {File.Exists(x64Path)}");
        }
    }

    private async void OnStartButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            // Stop the stress test
            _cts?.Cancel();
            _isRunning = false;
            if (sender is Button btn)
            {
                btn.Content = "Start Stress Test";
            }
        }
        else
        {
            // Start the stress test
            _isRunning = true;
            if (sender is Button btn)
            {
                btn.Content = "Stop Stress Test";
            }
            
            _cts = new CancellationTokenSource();
            await RunStressTestAsync(_cts.Token);
        }
    }

    private async Task RunStressTestAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_pdfFileName))
            {
                var statusText = this.FindControl<TextBlock>("StatusText");
                if (statusText != null)
                {
                    statusText.Text = $"PDF file not found: {_pdfFileName}";
                }
                _isRunning = false;
                var btn = this.FindControl<Button>("StartButton");
                if (btn != null)
                {
                    btn.Content = "Start Stress Test";
                }
                return;
            }

            PdfiumViewer.PdfDocument? pdfDoc = null;
            try
            {
                pdfDoc = PdfiumViewer.PdfDocument.Load(_pdfFileName);
            }
            catch (DllNotFoundException dllEx)
            {
                var statusText = this.FindControl<TextBlock>("StatusText");
                if (statusText != null)
                {
                    var exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    var x64Path = exePath != null ? Path.Combine(exePath, "x64", "pdfium.dll") : "unknown";
                    statusText.Text = $"DLL Not Found: {dllEx.Message}. Looking for pdfium.dll at: {x64Path}. Exists: {File.Exists(x64Path)}";
                }
                _isRunning = false;
                var btn = this.FindControl<Button>("StartButton");
                if (btn != null)
                {
                    btn.Content = "Start Stress Test";
                }
                return;
            }
            
            using (pdfDoc)
            {
                if (_pageNo >= pdfDoc.PageCount)
                {
                    var statusText = this.FindControl<TextBlock>("StatusText");
                    if (statusText != null)
                    {
                        statusText.Text = $"Page {_pageNo} out of range. PDF has {pdfDoc.PageCount} pages";
                    }
                    _isRunning = false;
                    var btn = this.FindControl<Button>("StartButton");
                    if (btn != null)
                    {
                        btn.Content = "Start Stress Test";
                    }
                    return;
                }

                var pageSize = pdfDoc.PageSizes[_pageNo];
                var dpi = 96;
                var width = (int)(pageSize.Width * dpi / 72.0);
                var height = (int)(pageSize.Height * dpi / 72.0);

                while (!ct.IsCancellationRequested)
                {
                    // Render page to bitmap using PDFium
                    using var bitmap = pdfDoc.Render(_pageNo, width, height, dpi, dpi, false);
                    
                    // Calculate checksum on bitmap data
                    using var strm = new MemoryStream();
                    bitmap.Save(strm, SysDrawingImaging.ImageFormat.Png);
                    strm.Seek(0, SeekOrigin.Begin);
                    var bytes = strm.ToArray();
                    
                    var chksum = 0UL;
                    Array.ForEach(bytes, (b) => { chksum += b; });
                    _dictCheckSums[chksum] = _dictCheckSums.TryGetValue(chksum, out var val) ? val + 1 : 1;

                    // Convert to Avalonia Bitmap
                    strm.Seek(0, SeekOrigin.Begin);
                    var avaloniaBitmap = new Bitmap(strm);

                    // Update UI on UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var imageControl = this.FindControl<Avalonia.Controls.Image>("PdfImage");
                        if (imageControl != null)
                        {
                            imageControl.Source = avaloniaBitmap;
                        }

                        var statusText = this.FindControl<TextBlock>("StatusText");
                        if (statusText != null)
                        {
                            statusText.Text = $"PDFium: {Path.GetFileName(_pdfFileName)} Page:{_pageNo} Iter:{_iterationCount++,5}  # unique checksums = {_dictCheckSums.Count} CurChkSum {chksum:n0} StreamLen={bytes.Length:n0} Size:{width}x{height}";
                        }
                    });

                    await Task.Delay(10, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var statusText = this.FindControl<TextBlock>("StatusText");
                if (statusText != null)
                {
                    statusText.Text = $"Error: {ex.GetType().Name}: {ex.Message}";
                }
            });
        }
        finally
        {
            _isRunning = false;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var btn = this.FindControl<Button>("StartButton");
                if (btn != null)
                {
                    btn.Content = "Start Stress Test";
                }
            });
        }
    }
}
