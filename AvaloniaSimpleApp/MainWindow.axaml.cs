using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PDFtoImage;
using SkiaSharp;

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
        
        // Get the PDF file path - cross-platform
        var username = Environment.UserName;
        var homeFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Path.Combine(homeFolder, "OneDrive");
        if (!Directory.Exists(folder))
        {
            folder = Path.Combine(homeFolder, "Documents");
        }
        _pdfFileName = Path.Combine(folder, "SheetMusic", "Pop", "PopSingles", "Be Our Guest - G Major - MN0174098.pdf");
        
        // Log diagnostic info
        var exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        System.Diagnostics.Debug.WriteLine($"App location: {exePath}");
        System.Diagnostics.Debug.WriteLine($"PDF file exists: {File.Exists(_pdfFileName)}");
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

            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            {
                var statusText = this.FindControl<TextBlock>("StatusText");
                if (statusText != null)
                {
                    statusText.Text = "PDF rendering is not supported on this platform";
                }
                _isRunning = false;
                var btn = this.FindControl<Button>("StartButton");
                if (btn != null)
                {
                    btn.Content = "Start Stress Test";
                }
                return;
            }

            int pageCount;
            try
            {
                using var pdfStream = File.OpenRead(_pdfFileName);
                pageCount = PDFtoImage.Conversion.GetPageCount(pdfStream);
            }
            catch (Exception ex)
            {
                var statusText = this.FindControl<TextBlock>("StatusText");
                if (statusText != null)
                {
                    statusText.Text = $"Error loading PDF: {ex.Message}";
                    Trace.WriteLine(statusText.Text);
                }
                _isRunning = false;
                var btn = this.FindControl<Button>("StartButton");
                if (btn != null)
                {
                    btn.Content = "Start Stress Test";
                }
                return;
            }
            
            if (_pageNo >= pageCount)
            {
                var statusText = this.FindControl<TextBlock>("StatusText");
                if (statusText != null)
                {
                    statusText.Text = $"Page {_pageNo} out of range. PDF has {pageCount} pages";
                }
                _isRunning = false;
                var btn = this.FindControl<Button>("StartButton");
                if (btn != null)
                {
                    btn.Content = "Start Stress Test";
                }
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                // Render page to bitmap using PDFtoImage
                using var pdfStream = File.OpenRead(_pdfFileName);
                using var skBitmap = PDFtoImage.Conversion.ToImage(pdfStream, page: _pageNo, options: new(Dpi: 96));
                
                var width = skBitmap.Width;
                var height = skBitmap.Height;
                
                // Calculate checksum on bitmap data
                using var image = SKImage.FromBitmap(skBitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var strm = new MemoryStream();
                data.SaveTo(strm);
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
                        statusText.Text = $"PDFtoImage: {Path.GetFileName(_pdfFileName)} Page:{_pageNo} Iter:{_iterationCount++,5}  # unique checksums = {_dictCheckSums.Count} CurChkSum {chksum:n0} StreamLen={bytes.Length:n0} Size:{width}x{height}";
                    }
                });

                await Task.Delay(10, ct);
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
