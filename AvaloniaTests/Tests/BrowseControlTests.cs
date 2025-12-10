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
    // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000
    // This is set by Windows for cloud-only files that need to be downloaded when accessed
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    
    // FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000  
    // This is set for files that need to be recalled even for metadata access
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    /// <summary>
    /// If true, skip cloud-only files instead of triggering download
    /// </summary>
    public bool SkipCloudOnlyFiles { get; set; } = true;
    
    /// <summary>
    /// Enable verbose logging of file attributes for debugging
    /// </summary>
    public bool VerboseLogging { get; set; } = true;

    public async Task<int> GetPageCountAsync(string pdfFilePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(pdfFilePath))
                    return 0;

                var fileInfo = new FileInfo(pdfFilePath);
                var attrs = fileInfo.Attributes;
                
                // Check for OneDrive cloud-only placeholder files
                bool hasRecallOnDataAccess = (attrs & RecallOnDataAccess) == RecallOnDataAccess;
                bool hasRecallOnOpen = (attrs & RecallOnOpen) == RecallOnOpen;
                bool hasOffline = (attrs & FileAttributes.Offline) == FileAttributes.Offline;
                bool hasReparsePoint = (attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                bool hasSparseFile = (attrs & FileAttributes.SparseFile) == FileAttributes.SparseFile;
                
                bool isCloudOnly = hasRecallOnDataAccess || hasRecallOnOpen || hasOffline;
                                   
                // ReparsePoint alone doesn't mean cloud-only (could be symlink, junction, etc.)
                // Small reparse point file is likely a cloud placeholder
                if (!isCloudOnly && hasReparsePoint && fileInfo.Length < 1024)
                {
                    isCloudOnly = true;
                }
                
                if (VerboseLogging)
                {
                    var attrStr = $"Attrs=0x{(int)attrs:X8} " +
                        $"RecallOnDataAccess={hasRecallOnDataAccess} " +
                        $"RecallOnOpen={hasRecallOnOpen} " +
                        $"Offline={hasOffline} " +
                        $"ReparsePoint={hasReparsePoint} " +
                        $"SparseFile={hasSparseFile} " +
                        $"Size={fileInfo.Length} " +
                        $"-> isCloudOnly={isCloudOnly}";
                    Trace.WriteLine($"AvaloniaPdfDocumentProvider: {Path.GetFileName(pdfFilePath)}: {attrStr}");
                }

                if (isCloudOnly && SkipCloudOnlyFiles)
                {
                    return 0;
                }

                // Read file - this triggers OneDrive download if cloud-only
                byte[] pdfBytes;
                try
                {
                    pdfBytes = File.ReadAllBytes(pdfFilePath);
                }
                catch (IOException ex) when (ex.HResult == unchecked((int)0x80070185) || 
                                              ex.HResult == unchecked((int)0x80070186))
                {
                    // ERROR_CLOUD_FILE_NETWORK_UNAVAILABLE (0x80070185)
                    // ERROR_CLOUD_FILE_IN_USE (0x80070186)
                    Trace.WriteLine($"AvaloniaPdfDocumentProvider: Cloud file unavailable: {pdfFilePath}");
                    return 0;
                }

                if (pdfBytes.Length == 0)
                    return 0;

                // Validate PDF signature
                if (pdfBytes.Length < 5 || pdfBytes[0] != '%' || pdfBytes[1] != 'P' || pdfBytes[2] != 'D' || pdfBytes[3] != 'F')
                {
                    Trace.WriteLine($"AvaloniaPdfDocumentProvider: Not a valid PDF: {pdfFilePath}");
                    return 0;
                }

                return Conversion.GetPageCount(pdfBytes);
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
            
            // Diagnostic: count TOC entries per result
            var totalTocEntries = results.Sum(r => r.TocEntries.Count);
            var resultsWithToc = results.Count(r => r.TocEntries.Count > 0);
            var resultsWithoutToc = results.Count(r => r.TocEntries.Count == 0);
            
            Trace.WriteLine($"? Loaded {results.Count} metadata files from {folders.Count} folders in {sw.ElapsedMilliseconds}ms");
            Trace.WriteLine($"? Total TOC entries: {totalTocEntries}");
            Trace.WriteLine($"? Results with TOC: {resultsWithToc}, without TOC: {resultsWithoutToc}");
            
            // Show some samples
            foreach (var r in results.Take(10))
            {
                Trace.WriteLine($"   {Path.GetFileName(r.FullPathFile)}: {r.TocEntries.Count} TOC entries, {r.VolumeInfoList.Count} volumes");
            }

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                // Create UberToc - flattened list of all TOC entries across all PDFs
                var uberToc = new List<Tuple<PdfMetaDataReadResult, TOCEntry>>();
                foreach (var pdfMetaDataItem in results)
                {
                    foreach (var tentry in pdfMetaDataItem.TocEntries)
                    {
                        uberToc.Add(Tuple.Create(pdfMetaDataItem, tentry));
                    }
                }

                Trace.WriteLine($"? UberToc has {uberToc.Count} entries");

                var query = from tup in uberToc
                            let itm = tup.Item2
                            let pdfPath = tup.Item1.GetFullPathFileFromPageNo(itm.PageNo)
                            let fileInfo = File.Exists(pdfPath) ? new FileInfo(pdfPath) : null
                            orderby itm.SongName
                            select new
                            {
                                itm.SongName,
                                Page = itm.PageNo,
                                Vol = tup.Item1.GetVolNumFromPageNum(itm.PageNo),
                                itm.Composer,
                                CompositionDate = itm.Date,
                                Fav = tup.Item1.IsFavorite(itm.PageNo) ? "Fav" : string.Empty,
                                BookName = tup.Item1.GetBookName(folder),
                                itm.Notes,
                                Acquisition = fileInfo?.LastWriteTime,
                                Access = fileInfo?.LastAccessTime,
                                Created = fileInfo?.CreationTime,
                                _Tup = tup
                            };

                var browseControl = new BrowseControl(query, colWidths: new[] { 250, 50, 40, 150, 100, 40, 200, 150, 130, 130, 130, 50 });

                var window = new Window
                {
                    Title = $"UberTOC Browser - {uberToc.Count:n0} Songs from {results.Count:n0} Books (SheetMusicLib)",
                    Width = 1600,
                    Height = 800,
                    Content = browseControl,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                    testCompleted,
                    lifetime,
                    "UberTOC Browser closed");

                window.Show();

                var memoryMB = GC.GetTotalMemory(false) / 1024 / 1024;
                Trace.WriteLine($"? Window shown with {uberToc.Count} songs from {results.Count} books");
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
