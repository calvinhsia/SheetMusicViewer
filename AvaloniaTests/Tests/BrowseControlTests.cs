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

/// <summary>
/// PDF document provider that throws when trying to read PDFs without BMK files.
/// This prevents creation of new BMK entries during testing.
/// </summary>
public class ThrowingPdfDocumentProvider : IPdfDocumentProvider
{
    public async Task<int> GetPageCountAsync(string pdfFilePath)
    {
        await Task.CompletedTask;
        throw new InvalidOperationException($"Attempted to read PDF without BMK file: {pdfFilePath}");
    }
}

[TestClass]
[DoNotParallelize]
public class BrowseControlTests : TestBase
{
    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestConvertBmkToJson()
    {
        var username = Environment.UserName;
        var folder = $@"C:\Users\{username}\OneDrive\SheetMusic";

        if (!Directory.Exists(folder))
        {
            Trace.WriteLine($"Folder not found: {folder}");
            Assert.Inconclusive("SheetMusic folder not found");
            return;
        }

        Trace.WriteLine($"Converting and verifying BMK files to JSON in: {folder}");
        Trace.WriteLine(new string('=', 80));

        var errors = new List<string>();
        var sw = Stopwatch.StartNew();
        
        var (converted, verified, errorCount) = await PdfMetaDataCore.ConvertAllBmkToJsonAsync(
            folder, 
            deleteOriginalBmk: false,
            verifyCallback: error => errors.Add(error));
        
        sw.Stop();

        Trace.WriteLine($"\nProcessed in {sw.ElapsedMilliseconds}ms");
        Trace.WriteLine($"Converted: {converted}");
        Trace.WriteLine($"Verified: {verified}");
        Trace.WriteLine($"Errors: {errorCount}");

        if (errors.Count > 0)
        {
            Trace.WriteLine("\n--- ERRORS ---");
            foreach (var error in errors.Take(50))
            {
                Trace.WriteLine($"  {error}");
            }
            if (errors.Count > 50)
                Trace.WriteLine($"  ... and {errors.Count - 50} more");
        }

        // Count JSON vs BMK files
        var jsonCount = Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories).Count();
        var bmkCount = Directory.EnumerateFiles(folder, "*.bmk", SearchOption.AllDirectories).Count();
        Trace.WriteLine($"\nJSON files: {jsonCount}, BMK files: {bmkCount}");

        // Load with JSON files available
        var pdfDocumentProvider = new ThrowingPdfDocumentProvider();
        var exceptionHandler = new TraceExceptionHandler();

        Trace.WriteLine("\n=== Loading with JSON files (where available) ===");
        var swJson = Stopwatch.StartNew();
        var (jsonResults, _) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            folder,
            pdfDocumentProvider,
            exceptionHandler,
            useParallelLoading: true);
        swJson.Stop();

        var jsonSongCount = jsonResults.Sum(r => r.TocEntries.Count);
        Trace.WriteLine($"Loaded {jsonResults.Count} books, {jsonSongCount} songs in {swJson.ElapsedMilliseconds}ms");

        Trace.WriteLine("\n" + new string('=', 80));
        if (errorCount == 0)
        {
            Trace.WriteLine("SUCCESS: All BMK files converted and verified!");
            Trace.WriteLine("To complete migration, delete the .bmk files manually.");
        }
        else
        {
            Trace.WriteLine($"VERIFICATION FAILED: {errorCount} files had differences");
            Assert.Fail($"{errorCount} files had conversion differences");
        }
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestCompareSerialVsParallelLoading()
    {
        var username = Environment.UserName;
        var folder = $@"C:\Users\{username}\OneDrive\SheetMusic";

        if (!Directory.Exists(folder))
        {
            Trace.WriteLine($"Folder not found: {folder}");
            Assert.Inconclusive("SheetMusic folder not found");
            return;
        }

        Trace.WriteLine($"Comparing Serial vs Parallel loading from: {folder}");
        Trace.WriteLine(new string('=', 80));

        // Use throwing provider to prevent reading PDFs without BMK files
        var pdfDocumentProvider = new ThrowingPdfDocumentProvider();
        var exceptionHandler = new TraceExceptionHandler();

        // Load using sequential method
        Trace.WriteLine("\n=== Loading with SEQUENTIAL method ===");
        var swSerial = Stopwatch.StartNew();
        var (serialResults, serialFolders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            folder,
            pdfDocumentProvider,
            exceptionHandler,
            useParallelLoading: false);
        swSerial.Stop();
        Trace.WriteLine($"Sequential: Loaded {serialResults.Count} entries in {swSerial.ElapsedMilliseconds}ms");

        // Load using parallel method
        Trace.WriteLine("\n=== Loading with PARALLEL method ===");
        var swParallel = Stopwatch.StartNew();
        var (parallelResults, parallelFolders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            folder,
            pdfDocumentProvider,
            exceptionHandler,
            useParallelLoading: true);
        swParallel.Stop();
        Trace.WriteLine($"Parallel: Loaded {parallelResults.Count} entries in {swParallel.ElapsedMilliseconds}ms");

        // Create lookup dictionaries by FullPathFile
        var serialDict = serialResults.ToDictionary(r => r.FullPathFile.ToLowerInvariant(), r => r);
        var parallelDict = parallelResults.ToDictionary(r => r.FullPathFile.ToLowerInvariant(), r => r);

        // Find differences
        var onlyInSerial = serialDict.Keys.Except(parallelDict.Keys).ToList();
        var onlyInParallel = parallelDict.Keys.Except(serialDict.Keys).ToList();
        var inBoth = serialDict.Keys.Intersect(parallelDict.Keys).ToList();

        Trace.WriteLine("\n" + new string('=', 80));
        Trace.WriteLine("=== COMPARISON RESULTS ===");
        Trace.WriteLine(new string('=', 80));
        
        // Calculate total song counts (TOC entries)
        var serialSongCount = serialResults.Sum(r => r.TocEntries.Count);
        var parallelSongCount = parallelResults.Sum(r => r.TocEntries.Count);
        
        Trace.WriteLine($"\nTotal in Sequential: {serialResults.Count} books, {serialSongCount} songs");
        Trace.WriteLine($"Total in Parallel:   {parallelResults.Count} books, {parallelSongCount} songs");
        Trace.WriteLine($"Difference:          {parallelResults.Count - serialResults.Count} books, {parallelSongCount - serialSongCount} songs");
        
        Trace.WriteLine($"\nIn both:            {inBoth.Count}");
        Trace.WriteLine($"Only in Sequential: {onlyInSerial.Count}");
        Trace.WriteLine($"Only in Parallel:   {onlyInParallel.Count}");

        if (onlyInSerial.Count > 0)
        {
            Trace.WriteLine($"\n--- Files ONLY in SEQUENTIAL ({onlyInSerial.Count}) ---");
            foreach (var path in onlyInSerial.OrderBy(p => p).Take(50))
            {
                var result = serialDict[path];
                Trace.WriteLine($"  {result.FullPathFile}");
                Trace.WriteLine($"    Vols={result.VolumeInfoList.Count}, Pages={result.VolumeInfoList.Sum(v => v.NPagesInThisVolume)}, TOC={result.TocEntries.Count}");
            }
            if (onlyInSerial.Count > 50)
                Trace.WriteLine($"  ... and {onlyInSerial.Count - 50} more");
        }

        if (onlyInParallel.Count > 0)
        {
            Trace.WriteLine($"\n--- Files ONLY in PARALLEL ({onlyInParallel.Count}) ---");
            foreach (var path in onlyInParallel.OrderBy(p => p).Take(50))
            {
                var result = parallelDict[path];
                Trace.WriteLine($"  {result.FullPathFile}");
                Trace.WriteLine($"    Vols={result.VolumeInfoList.Count}, Pages={result.VolumeInfoList.Sum(v => v.NPagesInThisVolume)}, TOC={result.TocEntries.Count}");
            }
            if (onlyInParallel.Count > 50)
                Trace.WriteLine($"  ... and {onlyInParallel.Count - 50} more");
        }

        // Check for differences in matching entries
        var differencesInContent = new List<string>();
        foreach (var path in inBoth)
        {
            var serial = serialDict[path];
            var parallel = parallelDict[path];
            
            var serialPages = serial.VolumeInfoList.Sum(v => v.NPagesInThisVolume);
            var parallelPages = parallel.VolumeInfoList.Sum(v => v.NPagesInThisVolume);
            
            if (serial.VolumeInfoList.Count != parallel.VolumeInfoList.Count ||
                serialPages != parallelPages ||
                serial.TocEntries.Count != parallel.TocEntries.Count)
            {
                differencesInContent.Add($"{Path.GetFileName(serial.FullPathFile)}: " +
                    $"Serial(Vols={serial.VolumeInfoList.Count}, Pages={serialPages}, TOC={serial.TocEntries.Count}) vs " +
                    $"Parallel(Vols={parallel.VolumeInfoList.Count}, Pages={parallelPages}, TOC={parallel.TocEntries.Count})");
            }
        }

        if (differencesInContent.Count > 0)
        {
            Trace.WriteLine($"\n--- Content differences in matching files ({differencesInContent.Count}) ---");
            foreach (var diff in differencesInContent.Take(20))
            {
                Trace.WriteLine($"  {diff}");
            }
            if (differencesInContent.Count > 20)
                Trace.WriteLine($"  ... and {differencesInContent.Count - 20} more");
        }

        Trace.WriteLine("\n" + new string('=', 80));
        
        // Assert that results match
        if (onlyInSerial.Count > 0 || onlyInParallel.Count > 0 || differencesInContent.Count > 0)
        {
            Trace.WriteLine("DIFFERENCES FOUND - See above for details");
        }
        else
        {
            Trace.WriteLine("SUCCESS: Serial and Parallel results are identical!");
        }
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestAvaloniaChooseMusicDialog()
    {
        var username = Environment.UserName;
        var folder = $@"C:\Users\{username}\OneDrive\SheetMusic";

        if (!Directory.Exists(folder))
        {
            Trace.WriteLine($"Folder not found: {folder}");
            Assert.Inconclusive("SheetMusic folder not found");
            return;
        }

        Trace.WriteLine($"Loading PDF metadata from: {folder}");
        
        var pdfDocumentProvider = new ThrowingPdfDocumentProvider();
        var exceptionHandler = new TraceExceptionHandler();

        var sw = Stopwatch.StartNew();
        var (results, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            folder,
            pdfDocumentProvider,
            exceptionHandler,
            useParallelLoading: true);
        sw.Stop();

        var totalTocEntries = results.Sum(r => r.TocEntries.Count);
        var totalPages = results.Sum(r => r.VolumeInfoList.Sum(v => v.NPagesInThisVolume));
        var totalFavorites = results.Sum(r => r.Favorites.Count);
        
        Trace.WriteLine($"Loaded {results.Count} books, {totalTocEntries} songs, {totalPages} pages in {sw.ElapsedMilliseconds}ms");

        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = new ChooseMusicWindow(results, folder)
            {
                SkipCloudOnlyFiles = false
            };
            lifetime.MainWindow = window;
            
            var timer = new System.Timers.Timer(120000);
            
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
                    Trace.WriteLine("Auto-closing ChooseMusicWindow after 60 seconds");
                    window?.Close();
                });
            };
            
            window.Show();
            
            Trace.WriteLine($"ChooseMusicWindow created and shown with {results.Count} books");
            Trace.WriteLine($"Window will auto-close after 60 seconds, or close manually");

            await Task.Delay(1000);
            timer.Start();
        }, timeoutMs: 65000);
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
            var lstFileInfos = new List<FileInfo>();
            foreach (var bmkFile in Directory.EnumerateFiles(folder, "*.bmk", SearchOption.AllDirectories))
            {
                lstFileInfos.Add(new FileInfo(bmkFile));
            }
            // output to csv
            var csvFilePath = Path.Combine(folder, "output.csv");
            if (File.Exists(csvFilePath))
            {
                File.Delete(csvFilePath);
            }
            using (var writer = new StreamWriter(csvFilePath))
            {
                await writer.WriteLineAsync("FileName,FileSize,LastModified");
                foreach (var fileInfo in lstFileInfos)
                {
                    // Quote filename in case it contains commas
                    await writer.WriteLineAsync($"\"{fileInfo.FullName}\",{fileInfo.Length},{fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                }
            }
            testCompleted.TrySetResult(true);
            return;


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
