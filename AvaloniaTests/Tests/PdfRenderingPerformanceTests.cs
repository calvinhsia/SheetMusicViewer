using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PDFtoImage;
using SheetMusicViewer.Desktop;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Performance tests comparing PDF rendering approaches:
/// 1. PNG encoding/decoding (old approach - slower)
/// 2. Direct pixel copy to WriteableBitmap (new approach - faster)
/// 
/// These tests call the actual ConvertSkBitmapToAvaloniaBitmap method from PdfViewerWindow.
/// Run with [TestCategory("Manual")] to see the performance difference.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PdfRenderingPerformanceTests : TestBase
{
    private const int WarmupIterations = 2;
    private const int TestIterations = 10;
    private const int DpiForTest = 150;

    /// <summary>
    /// Gets the path to a sample PDF in the Assets/SampleMusic folder.
    /// </summary>
    private string GetSamplePdfPath()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation)!;
        
        var possiblePaths = new[]
        {
            Path.Combine(assemblyDir, "..", "..", "..", "..", "SheetMusicViewer.Desktop", "Assets", "SampleMusic", "MapleLeafRag.pdf"),
            Path.Combine(assemblyDir, "..", "..", "..", "..", "..", "SheetMusicViewer.Desktop", "Assets", "SampleMusic", "MapleLeafRag.pdf"),
            @"C:\Users\Calvinh\source\repos\SheetMusicViewer\SheetMusicViewer.Desktop\Assets\SampleMusic\MapleLeafRag.pdf"
        };

        foreach (var path in possiblePaths)
        {
            var normalizedPath = Path.GetFullPath(path);
            if (File.Exists(normalizedPath))
            {
                LogMessage($"Found sample PDF at: {normalizedPath}");
                return normalizedPath;
            }
        }

        LogMessage("Sample PDF not found, creating test PDF");
        return CreateTestPdf();
    }

    /// <summary>
    /// Compares PNG encoding approach vs direct pixel copy using actual PdfViewerWindow method.
    /// Uses full Avalonia runtime (not headless) like other manual tests.
    /// </summary>
    [TestMethod]
    [TestCategory("Manual")]
    [Timeout(120000)]
    public async Task TestRenderingPerformance_PngVsDirectCopy_UsingActualMethod()
    {
        var pdfPath = GetSamplePdfPath();
        Assert.IsTrue(File.Exists(pdfPath), $"PDF file not found: {pdfPath}");

        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
        var pageCount = Conversion.GetPageCount(pdfBytes);
        
        LogMessage($"PDF: {Path.GetFileName(pdfPath)}");
        LogMessage($"Page count: {pageCount}");
        LogMessage($"File size: {pdfBytes.Length / 1024.0:F1} KB");
        LogMessage($"DPI: {DpiForTest}");
        LogMessage("---");

        var testCompleted = new TaskCompletionSource<bool>();
        var pagesToTest = Math.Min(3, pageCount);

        var uiThread = new Thread(() =>
        {
            try
            {
                PerfTestApp.OnReady = async (app, lifetime) =>
                {
                    try
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await RunPerformanceTestAsync(pdfBytes, pagesToTest);
                            testCompleted.TrySetResult(true);
                            lifetime.Shutdown();
                        });
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Test error: {ex.Message}");
                        testCompleted.TrySetException(ex);
                        lifetime.Shutdown();
                    }
                };

                AppBuilder.Configure<PerfTestApp>()
                    .UsePlatformDetect()
                    .WithInterFont()
                    .LogToTrace()
                    .StartWithClassicDesktopLifetime(Array.Empty<string>());
            }
            catch (Exception ex)
            {
                LogMessage($"UI thread error: {ex.Message}");
                testCompleted.TrySetException(ex);
            }
        });

        if (OperatingSystem.IsWindows())
        {
            uiThread.SetApartmentState(ApartmentState.STA);
        }
        uiThread.Start();

        var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(60000));
        if (completedTask != testCompleted.Task)
        {
            Assert.Fail("Test timed out after 60 seconds");
        }

        await testCompleted.Task;
        uiThread.Join(2000);
    }

    private async Task RunPerformanceTestAsync(byte[] pdfBytes, int pagesToTest)
    {
        // Warmup
        LogMessage("Warming up...");
        for (int i = 0; i < WarmupIterations; i++)
        {
            for (int page = 0; page < pagesToTest; page++)
            {
                using var b1 = RenderWithPngEncoding(pdfBytes, page);
                using var b2 = RenderWithDirectCopy(pdfBytes, page);
            }
        }

        // Test PNG encoding approach (old)
        LogMessage($"\n=== PNG Encoding Approach (old) ===");
        var pngTimes = new double[TestIterations];
        
        for (int iter = 0; iter < TestIterations; iter++)
        {
            var sw = Stopwatch.StartNew();
            for (int page = 0; page < pagesToTest; page++)
            {
                using var bitmap = RenderWithPngEncoding(pdfBytes, page);
            }
            sw.Stop();
            pngTimes[iter] = sw.Elapsed.TotalMilliseconds;
            LogMessage($"  Iteration {iter + 1}: {pngTimes[iter]:F1}ms ({pngTimes[iter] / pagesToTest:F1}ms/page)");
        }

        // Test direct copy approach (new) - calls actual PdfViewerWindow method
        LogMessage($"\n=== Direct Pixel Copy Approach (new) ===");
        var directTimes = new double[TestIterations];
        
        for (int iter = 0; iter < TestIterations; iter++)
        {
            var sw = Stopwatch.StartNew();
            for (int page = 0; page < pagesToTest; page++)
            {
                using var bitmap = RenderWithDirectCopy(pdfBytes, page);
            }
            sw.Stop();
            directTimes[iter] = sw.Elapsed.TotalMilliseconds;
            LogMessage($"  Iteration {iter + 1}: {directTimes[iter]:F1}ms ({directTimes[iter] / pagesToTest:F1}ms/page)");
        }

        // Results
        var pngAvg = pngTimes.Average();
        var directAvg = directTimes.Average();
        var speedup = pngAvg / directAvg;

        LogMessage("\n========================================");
        LogMessage("RESULTS");
        LogMessage("========================================");
        LogMessage($"PNG Encoding:     {pngAvg:F1}ms avg ({pngAvg / pagesToTest:F1}ms/page)");
        LogMessage($"Direct Copy:      {directAvg:F1}ms avg ({directAvg / pagesToTest:F1}ms/page)");
        LogMessage($"Speedup:          {speedup:F2}x faster");
        LogMessage($"Time saved/page:  {(pngAvg - directAvg) / pagesToTest:F1}ms");
        LogMessage("========================================");

        Assert.IsTrue(speedup > 1.0, $"Direct copy should be faster. Speedup: {speedup:F2}x");
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Measures conversion overhead only (without Avalonia runtime).
    /// Useful for CI environments or quick checks.
    /// </summary>
    [TestMethod]
    [TestCategory("Manual")]
    [Timeout(60000)]
    public async Task TestConversionOverhead_PngEncodingVsMemoryCopy()
    {
        var pdfPath = GetSamplePdfPath();
        Assert.IsTrue(File.Exists(pdfPath), $"PDF file not found: {pdfPath}");

        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
        var pageCount = Conversion.GetPageCount(pdfBytes);
        
        LogMessage($"PDF: {Path.GetFileName(pdfPath)}");
        LogMessage($"Page count: {pageCount}");
        LogMessage("---");
        LogMessage("Measuring conversion overhead only (no Avalonia runtime needed)");
        LogMessage("");

        const int iterations = 5;
        var pngEncodeTimes = new double[iterations];
        var memoryCopyTimes = new double[iterations];
        long pixelDataSize = 0;

        for (int i = 0; i < iterations; i++)
        {
            using var skBitmap = Conversion.ToImage(pdfBytes, page: (Index)0,
                options: new RenderOptions(Dpi: DpiForTest));
            
            pixelDataSize = skBitmap.Info.BytesSize;

            // PNG encode timing
            var sw = Stopwatch.StartNew();
            using var image = SKImage.FromBitmap(skBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            sw.Stop();
            pngEncodeTimes[i] = sw.Elapsed.TotalMilliseconds;

            // Memory copy timing (what direct copy does)
            sw.Restart();
            var pixels = skBitmap.GetPixels();
            var destBuffer = new byte[pixelDataSize];
            Marshal.Copy(pixels, destBuffer, 0, (int)pixelDataSize);
            sw.Stop();
            memoryCopyTimes[i] = sw.Elapsed.TotalMilliseconds;
        }

        var avgPngEncode = pngEncodeTimes.Average();
        var avgMemoryCopy = memoryCopyTimes.Average();
        var speedup = avgPngEncode / avgMemoryCopy;

        LogMessage($"Pixel data size: {pixelDataSize / 1024.0:F1} KB");
        LogMessage($"PNG encode:      {avgPngEncode:F1}ms avg");
        LogMessage($"Memory copy:     {avgMemoryCopy:F1}ms avg");
        LogMessage($"Speedup:         {speedup:F1}x faster");
        LogMessage($"Overhead saved:  {avgPngEncode - avgMemoryCopy:F1}ms per page");
    }

    /// <summary>
    /// Renders using PNG encoding (old approach)
    /// </summary>
    private Bitmap RenderWithPngEncoding(byte[] pdfBytes, int pageIndex)
    {
        using var skBitmap = Conversion.ToImage(pdfBytes, page: (Index)pageIndex,
            options: new RenderOptions(Dpi: DpiForTest));
        
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        
        return new Bitmap(stream);
    }

    /// <summary>
    /// Renders using direct pixel copy - calls the actual PdfViewerWindow method
    /// </summary>
    private Bitmap RenderWithDirectCopy(byte[] pdfBytes, int pageIndex)
    {
        using var skBitmap = Conversion.ToImage(pdfBytes, page: (Index)pageIndex,
            options: new RenderOptions(Dpi: DpiForTest));
        
        // Call the actual method from PdfViewerWindow
        return PdfViewerWindow.ConvertSkBitmapToAvaloniaBitmap(skBitmap);
    }

    /// <summary>
    /// Minimal Avalonia app for performance testing
    /// </summary>
    private class PerfTestApp : Application
    {
        public static Action<Application, IClassicDesktopStyleApplicationLifetime>? OnReady;

        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                OnReady?.Invoke(this, desktop);
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
