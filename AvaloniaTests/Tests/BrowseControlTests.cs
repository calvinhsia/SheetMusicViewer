using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PDFtoImage;
using SheetMusicLib;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Avalonia implementation of IPdfDocumentProvider using PDFtoImage library
/// </summary>
public class AvaloniaPdfDocumentProvider : IPdfDocumentProvider
{
    public async Task<int> GetPageCountAsync(string pdfFilePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(pdfFilePath))
                    return 0;
                    
                return Conversion.GetPageCount(pdfFilePath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AvaloniaPdfDocumentProvider: Error getting page count for {pdfFilePath}: {ex.Message}");
                return 0;
            }
        });
    }
}

/// <summary>
/// Simple exception handler that logs to Trace
/// </summary>
public class TraceExceptionHandler : IExceptionHandler
{
    public void OnException(string context, Exception ex)
    {
        Trace.WriteLine($"Exception in {context}: {ex.Message}");
    }
}

[TestClass]
[DoNotParallelize]
public class BrowseControlTests : TestBase
{
    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestAvaloniaChooseMusicDialog()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = new ChooseMusicWindow();
            lifetime.MainWindow = window;
            
            var timer = new System.Timers.Timer(30000);
            
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "ChooseMusicWindow closed");
            
            window.Closed += (s, e) => timer.Stop();
            
            timer.Elapsed += (s, e) =>
            {
                timer.Stop();
                Dispatcher.UIThread.Post(() =>
                {
                    Trace.WriteLine("Auto-closing ChooseMusicWindow after 30 seconds");
                    window?.Close();
                });
            };
            
            window.Show();
            
            Trace.WriteLine($"? ChooseMusicWindow created and shown");
            Trace.WriteLine($"? Generating bitmaps for books...");
            Trace.WriteLine($"? Window will auto-close after 30 seconds, or close manually");

            await Task.Delay(1000);
            timer.Start();
        }, timeoutMs: 35000);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestAvaloniaDataGridBrowseList()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = new BrowseListWindow();
            lifetime.MainWindow = window;
            
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "BrowseListWindow closed by user");
            
            window.Show();
            
            Trace.WriteLine($"? BrowseListWindow created and shown");
            Trace.WriteLine($"? Loading types from Avalonia assemblies...");

            await Task.Delay(100);
        });
    }
    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestBrowseControlWithRealPDFMetadata()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var username = Environment.UserName;
            var folder = $@"C:\Users\{username}\OneDrive\SheetMusic";

            if (!Directory.Exists(folder))
            {
                Trace.WriteLine($"? Folder not found: {folder}");
                testCompleted.TrySetResult(true);
                return;
            }

            Trace.WriteLine($"? Scanning folder: {folder}");

            var pdfDocumentProvider = new AvaloniaPdfDocumentProvider();
            var exceptionHandler = new TraceExceptionHandler();

            var sw = Stopwatch.StartNew();
            
            // Use the portable LoadAllPdfMetaDataFromDiskAsync from SheetMusicLib
            var (results, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
                folder,
                pdfDocumentProvider,
                exceptionHandler);
            
            sw.Stop();
            Trace.WriteLine($"? Loaded {results.Count} metadata files from {folders.Count} folders in {sw.ElapsedMilliseconds}ms");

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                // Create a query with displayable properties
                var query = results.Select(r => new
                {
                    Name = Path.GetFileNameWithoutExtension(r.FullPathFile),
                    Pages = r.VolumeInfoList.Sum(v => v.NPagesInThisVolume),
                    TOC = r.TocEntries.Count,
                    Favorites = r.Favorites.Count,
                    InkStrokes = r.InkStrokes.Count,
                    Volumes = r.VolumeInfoList.Count,
                    LastPage = r.LastPageNo,
                    IsSingles = r.IsSinglesFolder,
                    LastModified = r.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                    Path = r.FullPathFile
                });

                var browseControl = new BrowseControl(query, colWidths: new[] { 250, 60, 50, 70, 80, 70, 70, 70, 130, 400 });

                var window = new Window
                {
                    Title = $"PDF Metadata Browser - {results.Count:n0} Items (SheetMusicLib)",
                    Width = 1400,
                    Height = 800,
                    Content = browseControl,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                    testCompleted,
                    lifetime,
                    "PDF Metadata Browser closed");

                window.Show();

                var memoryMB = GC.GetTotalMemory(false) / 1024 / 1024;
                Trace.WriteLine($"? Window shown with {results.Count} items from {folders.Count} folders");
                Trace.WriteLine($"? Memory: ~{memoryMB:n0} MB");
                Trace.WriteLine("Close the window when finished testing.");

                await Task.Delay(100);
            });
        }, timeoutMs: 120000);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestListBoxBrowseControl()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            await Task.Delay(1000);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                Trace.WriteLine("=== BrowseControl with 10,000 Items ===");
                
                var itemCount = 10000;
                var query = Enumerable.Range(0, itemCount)
                    .Select(i => new
                    {
                        Id = i,
                        Name = $"Item {i:D5}",
                        Category = $"Category {i % 20}",
                        Type = $"Type {i % 5}",
                        Value = i * 2.5,
                        Status = i % 3 == 0 ? "Active" : i % 3 == 1 ? "Pending" : "Inactive",
                        Description = $"Description for test item number {i} with additional details"
                    });
                
                var sw = Stopwatch.StartNew();
                var browseControl = new BrowseControl(query, colWidths: new[] { 80, 150, 120, 80, 100, 100, 350 });
                sw.Stop();
                
                var window = new Window
                {
                    Title = $"BrowseControl - {itemCount:n0} Items (Virtualized)",
                    Width = 1200,
                    Height = 800,
                    Content = browseControl,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                
                window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                    testCompleted,
                    lifetime,
                    "BrowseControl window closed");
                
                window.Show();
                await Task.Delay(1000);
                
                var memoryMB = GC.GetTotalMemory(false) / 1024 / 1024;
                
                Trace.WriteLine($"? Creation time: {sw.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"? Memory: ~{memoryMB:n0} MB");
                Trace.WriteLine("Close the window when finished testing.");
            });
        });
    }
}
