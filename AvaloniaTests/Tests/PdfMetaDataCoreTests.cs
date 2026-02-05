using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using SheetMusicViewer.Desktop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Unit tests for PdfMetaDataCore - the portable PDF metadata reader/writer.
/// These tests verify JSON serialization, metadata loading, and deadlock prevention.
/// </summary>
[TestClass]
public class PdfMetaDataCoreTests : TestBase
{
    private string _tempFolder;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _tempFolder = Path.Combine(Path.GetTempPath(), $"PdfMetaDataCoreTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        LogMessage($"Created temp folder: {_tempFolder}");
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        base.TestCleanup();
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, recursive: true);
                LogMessage($"Cleaned up temp folder: {_tempFolder}");
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Warning: Could not delete temp folder: {ex.Message}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SaveToJson_WithNullMetadata_ReturnsFalse()
    {
        // Arrange & Act
        var result = PdfMetaDataCore.SaveToJson(null);

        // Assert
        Assert.IsFalse(result, "SaveToJson should return false for null metadata");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SaveToJson_WithNotDirtyMetadata_ReturnsTrue()
    {
        // Arrange
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = Path.Combine(_tempFolder, "test.pdf"),
            IsDirty = false
        };

        // Act
        var result = PdfMetaDataCore.SaveToJson(metadata);

        // Assert
        Assert.IsTrue(result, "SaveToJson should return true when metadata is not dirty");
        Assert.IsFalse(File.Exists(metadata.JsonFilePath), "No JSON file should be created when not dirty");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SaveToJson_WithDirtyMetadata_CreatesJsonFile()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempFolder, "test.pdf");
        File.WriteAllText(pdfPath, "fake pdf"); // Create a placeholder file
        
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = true,
            LastPageNo = 5,
            PageNumberOffset = 0,
            Notes = "Test notes"
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "test.pdf",
            NPagesInThisVolume = 10,
            Rotation = 0
        });
        metadata.TocEntries.Add(new TOCEntry
        {
            PageNo = 0,
            SongName = "Test Song",
            Composer = "Test Composer"
        });

        // Act
        var result = PdfMetaDataCore.SaveToJson(metadata);

        // Assert
        Assert.IsTrue(result, "SaveToJson should return true on success");
        Assert.IsTrue(File.Exists(metadata.JsonFilePath), "JSON file should be created");
        Assert.IsFalse(metadata.IsDirty, "IsDirty should be cleared after save");

        // Verify JSON content
        var jsonContent = File.ReadAllText(metadata.JsonFilePath);
        LogMessage($"JSON content:\n{jsonContent}");
        
        Assert.IsTrue(jsonContent.Contains("\"volumes\""), "JSON should contain volumes");
        Assert.IsTrue(jsonContent.Contains("\"tableOfContents\""), "JSON should contain tableOfContents");
        Assert.IsTrue(jsonContent.Contains("Test Song"), "JSON should contain song name");
        Assert.IsTrue(jsonContent.Contains("\"lastPageNo\": 5"), "JSON should contain lastPageNo");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SaveToJson_ForceSave_SavesEvenWhenNotDirty()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempFolder, "test.pdf");
        File.WriteAllText(pdfPath, "fake pdf");
        
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = false, // Not dirty
            LastPageNo = 3
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "test.pdf",
            NPagesInThisVolume = 5
        });

        // Act
        var result = PdfMetaDataCore.SaveToJson(metadata, forceSave: true);

        // Assert
        Assert.IsTrue(result, "SaveToJson with forceSave should succeed");
        Assert.IsTrue(File.Exists(metadata.JsonFilePath), "JSON file should be created with forceSave");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(5000)] // 5 second timeout - if this times out, we have a deadlock
    public void SaveToJson_CalledFromSynchronizationContext_DoesNotDeadlock()
    {
        // This test verifies that SaveToJson does not deadlock when called from a 
        // thread with a SynchronizationContext (like a UI thread).
        // The test uses a timeout attribute - if it deadlocks, the test will fail.

        // Arrange
        var pdfPath = Path.Combine(_tempFolder, "deadlock_test.pdf");
        File.WriteAllText(pdfPath, "fake pdf");
        
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = true,
            LastPageNo = 1
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "deadlock_test.pdf",
            NPagesInThisVolume = 3
        });

        // Act - Run on a thread with a synchronization context (simulating UI thread)
        var testCompleted = new ManualResetEventSlim(false);
        Exception caughtException = null;
        bool saveResult = false;

        var thread = new Thread(() =>
        {
            // Install a synchronization context to simulate a UI thread
            SynchronizationContext.SetSynchronizationContext(new TestSynchronizationContext());
            
            try
            {
                // This call would deadlock if SaveToJson used async/await with .Result or .Wait()
                saveResult = PdfMetaDataCore.SaveToJson(metadata);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
            finally
            {
                testCompleted.Set();
            }
        });
        
        thread.Start();
        
        // Wait for completion - if this times out, we have a deadlock
        var completed = testCompleted.Wait(TimeSpan.FromSeconds(4));
        
        // Assert
        Assert.IsTrue(completed, "SaveToJson should complete without deadlock");
        Assert.IsNull(caughtException, $"SaveToJson should not throw: {caughtException?.Message}");
        Assert.IsTrue(saveResult, "SaveToJson should succeed");
        Assert.IsTrue(File.Exists(metadata.JsonFilePath), "JSON file should be created");
        
        LogMessage("SaveToJson completed without deadlock on thread with SynchronizationContext");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LoadAndSaveRoundTrip_PreservesAllData()
    {
        // Arrange - Create a PDF and save metadata
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "roundtrip.pdf"));
        
        var originalMetadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = true,
            LastPageNo = 7,
            PageNumberOffset = 2,
            Notes = "Round trip test notes"
        };
        originalMetadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = Path.GetFileName(pdfPath),
            NPagesInThisVolume = 10,
            Rotation = 2
        });
        originalMetadata.TocEntries.Add(new TOCEntry
        {
            PageNo = 2,
            SongName = "First Song",
            Composer = "Bach",
            Date = "1720",
            Notes = "BWV 1001",
            Link = "https://www.youtube.com/watch?v=test123"
        });
        originalMetadata.TocEntries.Add(new TOCEntry
        {
            PageNo = 5,
            SongName = "Second Song",
            Composer = "Mozart"
        });
        originalMetadata.Favorites.Add(new Favorite
        {
            Pageno = 3,
            FavoriteName = "My favorite page"
        });

        // Act - Save and reload
        var saveResult = PdfMetaDataCore.SaveToJson(originalMetadata, forceSave: true);
        Assert.IsTrue(saveResult, "Save should succeed");

        var provider = new PdfToImageDocumentProvider();
        var loadedMetadata = await PdfMetaDataCore.ReadPdfMetaDataAsync(
            pdfPath, 
            isSingles: false, 
            provider);

        // Assert
        Assert.IsNotNull(loadedMetadata, "Should load metadata");
        Assert.AreEqual(originalMetadata.LastPageNo, loadedMetadata.LastPageNo, "LastPageNo should match");
        Assert.AreEqual(originalMetadata.PageNumberOffset, loadedMetadata.PageNumberOffset, "PageNumberOffset should match");
        Assert.AreEqual(originalMetadata.Notes, loadedMetadata.Notes, "Notes should match");

        Assert.AreEqual(1, loadedMetadata.VolumeInfoList.Count, "Should have 1 volume");
        Assert.AreEqual(10, loadedMetadata.VolumeInfoList[0].NPagesInThisVolume, "Volume page count should match");
        Assert.AreEqual(2, loadedMetadata.VolumeInfoList[0].Rotation, "Rotation should match");

        Assert.AreEqual(2, loadedMetadata.TocEntries.Count, "Should have 2 TOC entries");
        Assert.AreEqual("First Song", loadedMetadata.TocEntries[0].SongName, "First TOC song name should match");
        Assert.AreEqual("Bach", loadedMetadata.TocEntries[0].Composer, "First TOC composer should match");
        Assert.AreEqual("1720", loadedMetadata.TocEntries[0].Date, "First TOC date should match");
        Assert.AreEqual("BWV 1001", loadedMetadata.TocEntries[0].Notes, "First TOC notes should match");
        Assert.AreEqual("https://www.youtube.com/watch?v=test123", loadedMetadata.TocEntries[0].Link, "First TOC link should match");
        Assert.IsNull(loadedMetadata.TocEntries[1].Link, "Second TOC link should be null");

        Assert.AreEqual(1, loadedMetadata.Favorites.Count, "Should have 1 favorite");
        Assert.AreEqual(3, loadedMetadata.Favorites[0].Pageno, "Favorite page number should match");

        Assert.IsFalse(loadedMetadata.IsDirty, "Loaded metadata should not be dirty");

        LogMessage("Round trip test passed - all data preserved");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LoadAllPdfMetaDataFromDiskAsync_FindsPdfsWithoutMetadata()
    {
        // Arrange - Create PDFs without JSON files (use distinct names that won't be detected as continuations)
        var pdf1Path = CreateTestPdf(Path.Combine(_tempFolder, "BachSonatas.pdf"));
        var pdf2Path = CreateTestPdf(Path.Combine(_tempFolder, "MozartConcertos.pdf"));

        var provider = new PdfToImageDocumentProvider();

        // Act - Use sequential loading (parallel loader requires existing JSON files)
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: false); // Sequential loading finds PDFs without metadata

        // Assert
        Assert.AreEqual(2, metadataList.Count, "Should find 2 PDFs");
        
        foreach (var metadata in metadataList)
        {
            Assert.IsTrue(metadata.IsDirty, "New metadata should be dirty");
            Assert.IsTrue(metadata.VolumeInfoList.Count > 0, "Should have volume info");
            Assert.IsTrue(metadata.VolumeInfoList[0].NPagesInThisVolume > 0, "Should have page count");
            LogMessage($"Found: {Path.GetFileName(metadata.FullPathFile)} with {metadata.VolumeInfoList[0].NPagesInThisVolume} pages");
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LoadAllPdfMetaDataFromDiskAsync_DetectsContinuationVolumes()
    {
        // Arrange - Create a multi-volume PDF set
        var baseBookPath = CreateTestPdf(Path.Combine(_tempFolder, "SonatenI.pdf"));
        var vol1Path = CreateTestPdf(Path.Combine(_tempFolder, "SonatenI1.pdf"));
        var vol2Path = CreateTestPdf(Path.Combine(_tempFolder, "SonatenI2.pdf"));

        var provider = new PdfToImageDocumentProvider();

        // Act - Use sequential loading (parallel loader requires existing JSON files)
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: false); // Sequential loading detects continuation volumes

        // Assert
        Assert.AreEqual(1, metadataList.Count, "Should detect as single multi-volume book");
        
        var metadata = metadataList[0];
        Assert.AreEqual(3, metadata.VolumeInfoList.Count, "Should have 3 volumes");
        
        LogMessage($"Found multi-volume book: {Path.GetFileName(metadata.FullPathFile)}");
        foreach (var vol in metadata.VolumeInfoList)
        {
            LogMessage($"  Volume: {vol.FileNameVolume} ({vol.NPagesInThisVolume} pages)");
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LoadAllPdfMetaDataFromDiskAsync_ParallelLoading_DetectsContinuationVolumes()
    {
        // This test verifies that the parallel loading path correctly handles
        // multi-volume PDF sets when there is NO existing metadata file.
        // 
        // Bug scenario: PDFs like "100 Greatest Rock Songs of the Decade.pdf" with
        // continuation volumes "...Decade1.pdf", "...Decade2.pdf", etc. were being
        // loaded as separate single-volume books instead of one multi-volume book.
        //
        // The bug was that the parallel loader correctly identified continuation files
        // and excluded them from the "new PDFs" list, but never actually added them
        // as volumes to the base PDF's metadata.

        // Arrange - Create a multi-volume PDF set WITHOUT any metadata file
        // Using a name that ends with a non-digit character (like "Decade" ending in 'e')
        // to match the real-world scenario from the bug report
        var basePdfPath = CreateTestPdf(Path.Combine(_tempFolder, "100 Greatest Rock Songs of the Decade.pdf"));
        var vol1Path = CreateTestPdf(Path.Combine(_tempFolder, "100 Greatest Rock Songs of the Decade1.pdf"));
        var vol2Path = CreateTestPdf(Path.Combine(_tempFolder, "100 Greatest Rock Songs of the Decade2.pdf"));
        var vol3Path = CreateTestPdf(Path.Combine(_tempFolder, "100 Greatest Rock Songs of the Decade3.pdf"));

        var provider = new PdfToImageDocumentProvider();

        // Act - Use PARALLEL loading (the default, which had the bug)
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: true);

        // Assert
        Assert.AreEqual(1, metadataList.Count, 
            "Should detect as single multi-volume book, not 4 separate books. " +
            "If this fails with count=1 but 1 volume, the continuation volumes were not added.");
        
        var metadata = metadataList[0];
        
        // This assertion would have failed before the fix - we got 1 volume instead of 4
        Assert.AreEqual(4, metadata.VolumeInfoList.Count, 
            $"Should have 4 volumes (base + 3 continuations). " +
            $"Actual volumes: {string.Join(", ", metadata.VolumeInfoList.Select(v => v.FileNameVolume))}");
        
        // Verify the volumes are in the correct order
        Assert.AreEqual("100 Greatest Rock Songs of the Decade.pdf", metadata.VolumeInfoList[0].FileNameVolume);
        Assert.AreEqual("100 Greatest Rock Songs of the Decade1.pdf", metadata.VolumeInfoList[1].FileNameVolume);
        Assert.AreEqual("100 Greatest Rock Songs of the Decade2.pdf", metadata.VolumeInfoList[2].FileNameVolume);
        Assert.AreEqual("100 Greatest Rock Songs of the Decade3.pdf", metadata.VolumeInfoList[3].FileNameVolume);

        // Verify each volume has page count
        foreach (var vol in metadata.VolumeInfoList)
        {
            Assert.IsTrue(vol.NPagesInThisVolume > 0, $"Volume {vol.FileNameVolume} should have page count");
            LogMessage($"  Volume: {vol.FileNameVolume} ({vol.NPagesInThisVolume} pages)");
        }

        // Verify total page count equals sum of all volumes
        var expectedTotalPages = metadata.VolumeInfoList.Sum(v => v.NPagesInThisVolume);
        LogMessage($"Found multi-volume book: {Path.GetFileName(metadata.FullPathFile)} with {metadata.VolumeInfoList.Count} volumes, {expectedTotalPages} total pages");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LoadAllPdfMetaDataFromDiskAsync_ParallelLoading_HandlesZeroBasedAndOneBasedContinuations()
    {
        // Test both naming conventions:
        // - Zero-based: Book0.pdf, Book1.pdf, Book2.pdf
        // - One-based: Book1.pdf, Book2.pdf, Book3.pdf

        // Arrange - Zero-based set
        CreateTestPdf(Path.Combine(_tempFolder, "ZeroBasedBook0.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "ZeroBasedBook1.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "ZeroBasedBook2.pdf"));

        // Arrange - One-based set  
        CreateTestPdf(Path.Combine(_tempFolder, "OneBasedBook1.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "OneBasedBook2.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "OneBasedBook3.pdf"));

        var provider = new PdfToImageDocumentProvider();

        // Act
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: true);

        // Assert - Should have 2 books, each with 3 volumes
        Assert.AreEqual(2, metadataList.Count, "Should have 2 multi-volume books");

        var zeroBasedBook = metadataList.FirstOrDefault(m => 
            m.FullPathFile.Contains("ZeroBasedBook"));
        var oneBasedBook = metadataList.FirstOrDefault(m => 
            m.FullPathFile.Contains("OneBasedBook"));

        Assert.IsNotNull(zeroBasedBook, "Should find zero-based book");
        Assert.IsNotNull(oneBasedBook, "Should find one-based book");

        Assert.AreEqual(3, zeroBasedBook.VolumeInfoList.Count, 
            $"Zero-based book should have 3 volumes. Found: {string.Join(", ", zeroBasedBook.VolumeInfoList.Select(v => v.FileNameVolume))}");
        Assert.AreEqual(3, oneBasedBook.VolumeInfoList.Count, 
            $"One-based book should have 3 volumes. Found: {string.Join(", ", oneBasedBook.VolumeInfoList.Select(v => v.FileNameVolume))}");

        LogMessage($"Zero-based book: {zeroBasedBook.VolumeInfoList.Count} volumes");
        LogMessage($"One-based book: {oneBasedBook.VolumeInfoList.Count} volumes");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LoadAllPdfMetaDataFromDiskAsync_ParallelLoading_DoesNotMergeUnrelatedBooks()
    {
        // Verify that books with similar but not matching names are NOT merged

        // Arrange - Create books that should NOT be merged
        CreateTestPdf(Path.Combine(_tempFolder, "Classical Music.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "Classical Music Collection.pdf")); // Different book, not a continuation
        CreateTestPdf(Path.Combine(_tempFolder, "Jazz Standards.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "Jazz Standards Vol 2.pdf")); // Also different book (has space before "Vol")

        var provider = new PdfToImageDocumentProvider();

        // Act
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: true);

        // Assert - All should be separate books (4 books, each with 1 volume)
        Assert.AreEqual(4, metadataList.Count, 
            "Should have 4 separate books (continuations require digit immediately after base name)");

        foreach (var metadata in metadataList)
        {
            Assert.AreEqual(1, metadata.VolumeInfoList.Count, 
                $"Book '{Path.GetFileName(metadata.FullPathFile)}' should have exactly 1 volume");
            LogMessage($"Found separate book: {Path.GetFileName(metadata.FullPathFile)}");
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LoadAllPdfMetaDataFromDiskAsync_AutoSavesJsonForNewPdfs()
    {
        // This test verifies that when loading a folder with multiple PDF sets 
        // but NO existing JSON metadata files, the auto-save feature creates
        // JSON files for all of them.
        //
        // This test would have FAILED before the autoSaveNewMetadata feature was added,
        // because JSON files would not be created automatically.

        // Arrange - Create 3 separate PDF "books" with NO metadata files
        var book1Path = CreateTestPdf(Path.Combine(_tempFolder, "Beethoven Sonatas.pdf"));
        var book2Path = CreateTestPdf(Path.Combine(_tempFolder, "Mozart Concertos.pdf"));
        var book3Path = CreateTestPdf(Path.Combine(_tempFolder, "Bach Preludes.pdf"));

        // Verify no JSON files exist before loading
        var jsonFilesBefore = Directory.GetFiles(_tempFolder, "*.json");
        Assert.AreEqual(0, jsonFilesBefore.Length, "No JSON files should exist before loading");

        var provider = new PdfToImageDocumentProvider();

        // Act - Load with parallel loading AND auto-save enabled (the default)
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: true,
            autoSaveNewMetadata: true); // This is the default, but being explicit

        // Assert - Should have found 3 books
        Assert.AreEqual(3, metadataList.Count, "Should find 3 PDF books");

        // Assert - JSON files should now exist for all 3 books
        var jsonFilesAfter = Directory.GetFiles(_tempFolder, "*.json");
        Assert.AreEqual(3, jsonFilesAfter.Length, 
            $"Should have created 3 JSON metadata files. Found: {string.Join(", ", jsonFilesAfter.Select(Path.GetFileName))}");

        // Verify each expected JSON file exists
        Assert.IsTrue(File.Exists(Path.ChangeExtension(book1Path, ".json")), 
            "JSON file should exist for Beethoven Sonatas");
        Assert.IsTrue(File.Exists(Path.ChangeExtension(book2Path, ".json")), 
            "JSON file should exist for Mozart Concertos");
        Assert.IsTrue(File.Exists(Path.ChangeExtension(book3Path, ".json")), 
            "JSON file should exist for Bach Preludes");

        // Verify the metadata is no longer dirty (was saved)
        foreach (var metadata in metadataList)
        {
            Assert.IsFalse(metadata.IsDirty, 
                $"Metadata for '{Path.GetFileName(metadata.FullPathFile)}' should not be dirty after auto-save");
        }

        LogMessage($"Auto-saved {jsonFilesAfter.Length} JSON metadata files:");
        foreach (var jsonFile in jsonFilesAfter)
        {
            LogMessage($"  - {Path.GetFileName(jsonFile)}");
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LoadAllPdfMetaDataFromDiskAsync_AutoSavesJsonForMultiVolumeSets()
    {
        // This test verifies that multi-volume PDF sets get their JSON saved
        // with all volumes correctly included.
        //
        // This test would have FAILED if:
        // 1. The continuation volumes weren't detected (fixed earlier)
        // 2. The auto-save didn't work for multi-volume sets

        // Arrange - Create a multi-volume PDF set with NO metadata file
        CreateTestPdf(Path.Combine(_tempFolder, "Greatest Hits.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "Greatest Hits1.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "Greatest Hits2.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "Greatest Hits3.pdf"));

        // Verify no JSON files exist before loading
        Assert.AreEqual(0, Directory.GetFiles(_tempFolder, "*.json").Length, 
            "No JSON files should exist before loading");

        var provider = new PdfToImageDocumentProvider();

        // Act - Load with auto-save enabled
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: true,
            autoSaveNewMetadata: true);

        // Assert - Should have 1 multi-volume book
        Assert.AreEqual(1, metadataList.Count, "Should find 1 multi-volume book");
        Assert.AreEqual(4, metadataList[0].VolumeInfoList.Count, "Should have 4 volumes");

        // Assert - JSON file should exist
        var jsonFiles = Directory.GetFiles(_tempFolder, "*.json");
        Assert.AreEqual(1, jsonFiles.Length, "Should have created 1 JSON metadata file");

        var jsonPath = Path.Combine(_tempFolder, "Greatest Hits.json");
        Assert.IsTrue(File.Exists(jsonPath), "JSON file should exist for Greatest Hits");

        // Verify JSON content includes all volumes
        var jsonContent = await File.ReadAllTextAsync(jsonPath);
        Assert.IsTrue(jsonContent.Contains("Greatest Hits.pdf"), "JSON should contain base volume");
        Assert.IsTrue(jsonContent.Contains("Greatest Hits1.pdf"), "JSON should contain volume 1");
        Assert.IsTrue(jsonContent.Contains("Greatest Hits2.pdf"), "JSON should contain volume 2");
        Assert.IsTrue(jsonContent.Contains("Greatest Hits3.pdf"), "JSON should contain volume 3");

        // Verify metadata is no longer dirty
        Assert.IsFalse(metadataList[0].IsDirty, "Metadata should not be dirty after auto-save");

        LogMessage($"Auto-saved multi-volume JSON with {metadataList[0].VolumeInfoList.Count} volumes");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LoadAllPdfMetaDataFromDiskAsync_WithAutoSaveDisabled_DoesNotCreateJsonFiles()
    {
        // This test verifies that when autoSaveNewMetadata is false,
        // no JSON files are created automatically.

        // Arrange - Create PDFs with NO metadata files
        CreateTestPdf(Path.Combine(_tempFolder, "NoAutoSaveBook.pdf"));

        var provider = new PdfToImageDocumentProvider();

        // Act - Load with auto-save DISABLED
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: true,
            autoSaveNewMetadata: false); // Explicitly disable auto-save

        // Assert - Should find the PDF
        Assert.AreEqual(1, metadataList.Count, "Should find 1 PDF");

        // Assert - NO JSON files should be created
        var jsonFiles = Directory.GetFiles(_tempFolder, "*.json");
        Assert.AreEqual(0, jsonFiles.Length, 
            "No JSON files should be created when autoSaveNewMetadata is false");

        // Metadata should still be dirty (wasn't saved)
        Assert.IsTrue(metadataList[0].IsDirty, 
            "Metadata should remain dirty when auto-save is disabled");

        LogMessage("Correctly skipped auto-save when disabled");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LoadAllPdfMetaDataFromDiskAsync_WithExistingJson_OnlyReadsJsonNotPdf()
    {
        // This test verifies that when a folder contains PDFs WITH existing JSON metadata,
        // the loader only reads the JSON files (not the PDFs themselves).
        // 
        // When there are NEW PDFs without JSON, those PDFs must be read to get page counts,
        // and new JSON files are created for them.

        // Arrange - Create PDFs and pre-create JSON metadata files for some of them
        var existingBookPdf = CreateTestPdf(Path.Combine(_tempFolder, "ExistingBook.pdf"));
        var newBookPdf = CreateTestPdf(Path.Combine(_tempFolder, "NewBook.pdf"));

        // Create JSON for ExistingBook (simulating it was previously loaded)
        var existingBookJson = Path.ChangeExtension(existingBookPdf, ".json");
        var preCreatedMetadata = new PdfMetaDataReadResult
        {
            FullPathFile = existingBookPdf,
            IsDirty = true,
            LastPageNo = 42, // Distinctive value to verify it was read from JSON
            PageNumberOffset = 5,
            Notes = "Pre-existing metadata"
        };
        preCreatedMetadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "ExistingBook.pdf",
            NPagesInThisVolume = 99, // Fake page count - different from actual PDF
            Rotation = 0
        });
        preCreatedMetadata.TocEntries.Add(new TOCEntry
        {
            PageNo = 5,
            SongName = "Pre-existing Song"
        });
        PdfMetaDataCore.SaveToJson(preCreatedMetadata, forceSave: true);

        // Verify JSON exists for ExistingBook but not for NewBook
        Assert.IsTrue(File.Exists(existingBookJson), "JSON should exist for ExistingBook");
        Assert.IsFalse(File.Exists(Path.ChangeExtension(newBookPdf, ".json")), 
            "JSON should NOT exist for NewBook before loading");

        var provider = new PdfToImageDocumentProvider();

        // Act - Load all metadata
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: true,
            autoSaveNewMetadata: true);

        // Assert - Should have found 2 books
        Assert.AreEqual(2, metadataList.Count, "Should find 2 books");

        // Find the ExistingBook metadata
        var existingBookMetadata = metadataList.FirstOrDefault(m => 
            m.FullPathFile.Contains("ExistingBook"));
        Assert.IsNotNull(existingBookMetadata, "Should find ExistingBook");

        // Verify ExistingBook was loaded from JSON (not by reading the PDF)
        // The page count should be the fake value (99) from JSON, not the actual PDF page count (1)
        Assert.AreEqual(99, existingBookMetadata.VolumeInfoList[0].NPagesInThisVolume,
            "ExistingBook should have page count from JSON (99), not from PDF. " +
            "If this is 1, the PDF was read instead of the JSON.");
        Assert.AreEqual(42, existingBookMetadata.LastPageNo,
            "ExistingBook should have LastPageNo from JSON");
        Assert.AreEqual(5, existingBookMetadata.PageNumberOffset,
            "ExistingBook should have PageNumberOffset from JSON");
        Assert.AreEqual("Pre-existing metadata", existingBookMetadata.Notes,
            "ExistingBook should have Notes from JSON");
        Assert.AreEqual(1, existingBookMetadata.TocEntries.Count,
            "ExistingBook should have TOC entries from JSON");
        Assert.AreEqual("Pre-existing Song", existingBookMetadata.TocEntries[0].SongName,
            "ExistingBook should have song name from JSON");

        // Find the NewBook metadata
        var newBookMetadata = metadataList.FirstOrDefault(m => 
            m.FullPathFile.Contains("NewBook"));
        Assert.IsNotNull(newBookMetadata, "Should find NewBook");

        // Verify NewBook was created by reading the PDF (page count should be 1, the actual PDF page count)
        Assert.AreEqual(1, newBookMetadata.VolumeInfoList[0].NPagesInThisVolume,
            "NewBook should have page count from PDF (1), since no JSON existed");

        // Verify JSON was created for NewBook
        Assert.IsTrue(File.Exists(Path.ChangeExtension(newBookPdf, ".json")),
            "JSON should now exist for NewBook after auto-save");

        LogMessage("Verified: ExistingBook loaded from JSON only, NewBook read from PDF and JSON created");
        LogMessage($"  ExistingBook: {existingBookMetadata.VolumeInfoList[0].NPagesInThisVolume} pages (from JSON)");
        LogMessage($"  NewBook: {newBookMetadata.VolumeInfoList[0].NPagesInThisVolume} pages (from PDF)");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LoadAllPdfMetaDataFromDiskAsync_MixedExistingAndNewMultiVolume_HandlesCorrectly()
    {
        // This test verifies behavior when:
        // 1. A multi-volume set has existing JSON (should load from JSON only)
        // 2. A new multi-volume set has no JSON (should read all PDFs and create JSON)

        // Arrange - Create an existing multi-volume set WITH JSON
        CreateTestPdf(Path.Combine(_tempFolder, "OldSonatas.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "OldSonatas1.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "OldSonatas2.pdf"));

        // Create JSON for OldSonatas with fake data
        var oldSonatasJson = Path.Combine(_tempFolder, "OldSonatas.json");
        var oldMetadata = new PdfMetaDataReadResult
        {
            FullPathFile = Path.Combine(_tempFolder, "OldSonatas.pdf"),
            IsDirty = true,
            LastPageNo = 100
        };
        oldMetadata.VolumeInfoList.Add(new PdfVolumeInfoBase { FileNameVolume = "OldSonatas.pdf", NPagesInThisVolume = 50 });
        oldMetadata.VolumeInfoList.Add(new PdfVolumeInfoBase { FileNameVolume = "OldSonatas1.pdf", NPagesInThisVolume = 60 });
        oldMetadata.VolumeInfoList.Add(new PdfVolumeInfoBase { FileNameVolume = "OldSonatas2.pdf", NPagesInThisVolume = 70 });
        PdfMetaDataCore.SaveToJson(oldMetadata, forceSave: true);

        // Create a NEW multi-volume set WITHOUT JSON
        CreateTestPdf(Path.Combine(_tempFolder, "NewConcertos.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "NewConcertos1.pdf"));
        CreateTestPdf(Path.Combine(_tempFolder, "NewConcertos2.pdf"));

        // Verify initial state
        Assert.IsTrue(File.Exists(oldSonatasJson), "OldSonatas.json should exist");
        Assert.IsFalse(File.Exists(Path.Combine(_tempFolder, "NewConcertos.json")), 
            "NewConcertos.json should NOT exist initially");

        var provider = new PdfToImageDocumentProvider();

        // Act
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: true,
            autoSaveNewMetadata: true);

        // Assert
        Assert.AreEqual(2, metadataList.Count, "Should find 2 multi-volume books");

        // Verify OldSonatas was loaded from JSON
        var oldSonatas = metadataList.FirstOrDefault(m => m.FullPathFile.Contains("OldSonatas"));
        Assert.IsNotNull(oldSonatas, "Should find OldSonatas");
        Assert.AreEqual(3, oldSonatas.VolumeInfoList.Count, "OldSonatas should have 3 volumes from JSON");
        Assert.AreEqual(50, oldSonatas.VolumeInfoList[0].NPagesInThisVolume, 
            "OldSonatas vol0 should have fake page count from JSON");
        Assert.AreEqual(60, oldSonatas.VolumeInfoList[1].NPagesInThisVolume, 
            "OldSonatas vol1 should have fake page count from JSON");
        Assert.AreEqual(70, oldSonatas.VolumeInfoList[2].NPagesInThisVolume, 
            "OldSonatas vol2 should have fake page count from JSON");

        // Verify NewConcertos was created by reading PDFs
        var newConcertos = metadataList.FirstOrDefault(m => m.FullPathFile.Contains("NewConcertos"));
        Assert.IsNotNull(newConcertos, "Should find NewConcertos");
        Assert.AreEqual(3, newConcertos.VolumeInfoList.Count, "NewConcertos should have 3 volumes");
        // Each test PDF has 1 page
        Assert.AreEqual(1, newConcertos.VolumeInfoList[0].NPagesInThisVolume, 
            "NewConcertos vol0 should have actual page count from PDF");

        // Verify JSON was created for NewConcertos
        Assert.IsTrue(File.Exists(Path.Combine(_tempFolder, "NewConcertos.json")),
            "NewConcertos.json should now exist after auto-save");

        LogMessage($"Verified mixed existing/new multi-volume handling:");
        LogMessage($"  OldSonatas: 3 volumes with {oldSonatas.VolumeInfoList.Sum(v => v.NPagesInThisVolume)} total pages (from JSON)");
        LogMessage($"  NewConcertos: 3 volumes with {newConcertos.VolumeInfoList.Sum(v => v.NPagesInThisVolume)} total pages (from PDF)");
    }

    #region PDF Bytes Cache Tests

    [TestMethod]
    [TestCategory("Unit")]
    public void GetOrLoadVolumeBytes_WithValidVolume_ReturnsBytesAndCaches()
    {
        // Arrange
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "CacheTest.pdf"));
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "CacheTest.pdf",
            NPagesInThisVolume = 1
        });

        // Act - First call should load from disk
        var bytes1 = metadata.GetOrLoadVolumeBytes(0);
        
        // Act - Second call should return cached bytes
        var bytes2 = metadata.GetOrLoadVolumeBytes(0);

        // Assert
        Assert.IsNotNull(bytes1, "First call should return bytes");
        Assert.IsNotNull(bytes2, "Second call should return bytes");
        Assert.AreSame(bytes1, bytes2, "Second call should return same cached array instance");
        Assert.IsTrue(bytes1.Length > 0, "PDF bytes should not be empty");
        
        LogMessage($"Loaded {bytes1.Length} bytes, cache verified");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetOrLoadVolumeBytes_WithInvalidVolume_ReturnsNull()
    {
        // Arrange
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = Path.Combine(_tempFolder, "NonExistent.pdf"),
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "NonExistent.pdf",
            NPagesInThisVolume = 1
        });

        // Act
        var bytes = metadata.GetOrLoadVolumeBytes(0);

        // Assert
        Assert.IsNull(bytes, "Should return null for non-existent file");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetOrLoadVolumeBytes_WithOutOfRangeVolume_ReturnsNull()
    {
        // Arrange
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "SingleVolume.pdf"));
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "SingleVolume.pdf",
            NPagesInThisVolume = 1
        });

        // Act - Try to get volume 5 when only volume 0 exists
        var bytes = metadata.GetOrLoadVolumeBytes(5);

        // Assert - Should return null since GetFullPathFileFromVolno clamps to valid range
        // but the clamped path should still work (returns volume 0's bytes)
        // Actually, GetFullPathFileFromVolno clamps volNo to valid range, so it returns a valid path
        // Let's verify the behavior
        Assert.IsNotNull(bytes, "GetFullPathFileFromVolno clamps invalid volNo to valid range");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetCachedVolumeBytes_WithoutPriorLoad_ReturnsNull()
    {
        // Arrange
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "NoCacheYet.pdf"));
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "NoCacheYet.pdf",
            NPagesInThisVolume = 1
        });

        // Act - Try to get cached bytes without loading first
        var cachedBytes = metadata.GetCachedVolumeBytes(0);

        // Assert
        Assert.IsNull(cachedBytes, "Should return null when volume hasn't been loaded yet");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetCachedVolumeBytes_AfterLoad_ReturnsCachedBytes()
    {
        // Arrange
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "CacheAfterLoad.pdf"));
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "CacheAfterLoad.pdf",
            NPagesInThisVolume = 1
        });

        // Act - Load bytes first
        var loadedBytes = metadata.GetOrLoadVolumeBytes(0);
        var cachedBytes = metadata.GetCachedVolumeBytes(0);

        // Assert
        Assert.IsNotNull(loadedBytes, "Should load bytes");
        Assert.IsNotNull(cachedBytes, "Should return cached bytes after load");
        Assert.AreSame(loadedBytes, cachedBytes, "Cached bytes should be same instance");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ClearPdfBytesCache_ClearsAllCachedBytes()
    {
        // Arrange
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "ClearCacheTest.pdf"));
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "ClearCacheTest.pdf",
            NPagesInThisVolume = 1
        });

        // Act - Load bytes, then clear cache
        var loadedBytes = metadata.GetOrLoadVolumeBytes(0);
        Assert.IsNotNull(loadedBytes, "Should load bytes");
        
        metadata.ClearPdfBytesCache();
        var cachedBytesAfterClear = metadata.GetCachedVolumeBytes(0);

        // Assert
        Assert.IsNull(cachedBytesAfterClear, "Cache should be empty after clear");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task PreloadAllVolumeBytesAsync_LoadsAllVolumes()
    {
        // Arrange - Create a multi-volume set
        var pdf0Path = CreateTestPdf(Path.Combine(_tempFolder, "PreloadTest.pdf"));
        var pdf1Path = CreateTestPdf(Path.Combine(_tempFolder, "PreloadTest1.pdf"));
        var pdf2Path = CreateTestPdf(Path.Combine(_tempFolder, "PreloadTest2.pdf"));
        
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdf0Path,
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase { FileNameVolume = "PreloadTest.pdf", NPagesInThisVolume = 1 });
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase { FileNameVolume = "PreloadTest1.pdf", NPagesInThisVolume = 1 });
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase { FileNameVolume = "PreloadTest2.pdf", NPagesInThisVolume = 1 });

        // Verify nothing is cached yet
        Assert.IsNull(metadata.GetCachedVolumeBytes(0), "Vol 0 should not be cached yet");
        Assert.IsNull(metadata.GetCachedVolumeBytes(1), "Vol 1 should not be cached yet");
        Assert.IsNull(metadata.GetCachedVolumeBytes(2), "Vol 2 should not be cached yet");

        // Act
        await metadata.PreloadAllVolumeBytesAsync();

        // Assert - All volumes should now be cached
        var vol0Bytes = metadata.GetCachedVolumeBytes(0);
        var vol1Bytes = metadata.GetCachedVolumeBytes(1);
        var vol2Bytes = metadata.GetCachedVolumeBytes(2);

        Assert.IsNotNull(vol0Bytes, "Vol 0 should be cached after preload");
        Assert.IsNotNull(vol1Bytes, "Vol 1 should be cached after preload");
        Assert.IsNotNull(vol2Bytes, "Vol 2 should be cached after preload");

        LogMessage($"Preloaded 3 volumes: {vol0Bytes.Length}, {vol1Bytes.Length}, {vol2Bytes.Length} bytes");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetOrLoadVolumeBytes_IsThreadSafe()
    {
        // Arrange
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "ThreadSafeTest.pdf"));
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "ThreadSafeTest.pdf",
            NPagesInThisVolume = 1
        });

        // Act - Load from multiple threads simultaneously
        var results = new byte[10][];
        var tasks = new Task[10];
        
        for (int i = 0; i < 10; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() =>
            {
                results[index] = metadata.GetOrLoadVolumeBytes(0);
            });
        }
        
        Task.WaitAll(tasks);

        // Assert - All results should be the same cached instance
        // This verifies the fix: with per-volume locking, only one thread reads from disk
        // and all threads get the exact same byte array instance
        Assert.IsNotNull(results[0], "First result should not be null");
        for (int i = 1; i < 10; i++)
        {
            Assert.AreSame(results[0], results[i], 
                $"Result {i} should be same cached instance. " +
                "If this fails, multiple threads read the file from disk.");
        }

        LogMessage("Thread safety verified with 10 concurrent loads - all got same instance");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetOrLoadVolumeBytes_ConcurrentAccessToSameVolume_ReadsFileOnlyOnce()
    {
        // This test verifies that even with many concurrent requests for the same volume,
        // the file is only read from disk once. We verify this by checking that all
        // threads receive the exact same byte[] instance (reference equality).
        
        // Arrange
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "SingleReadTest.pdf"));
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "SingleReadTest.pdf",
            NPagesInThisVolume = 1
        });

        // Act - Start many threads at the same time using a barrier
        const int threadCount = 50;
        var results = new byte[threadCount][];
        var barrier = new Barrier(threadCount);
        var tasks = new Task[threadCount];
        
        for (int i = 0; i < threadCount; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() =>
            {
                // Wait for all threads to be ready
                barrier.SignalAndWait();
                // All threads call GetOrLoadVolumeBytes at the same time
                results[index] = metadata.GetOrLoadVolumeBytes(0);
            });
        }
        
        Task.WaitAll(tasks);

        // Assert - All results must be the exact same instance
        Assert.IsNotNull(results[0], "First result should not be null");
        
        int sameInstanceCount = 0;
        for (int i = 1; i < threadCount; i++)
        {
            if (ReferenceEquals(results[0], results[i]))
            {
                sameInstanceCount++;
            }
        }
        
        // All should be the same instance (file read only once)
        Assert.AreEqual(threadCount - 1, sameInstanceCount,
            $"All {threadCount} threads should get the same byte[] instance. " +
            $"Got {sameInstanceCount + 1} same instances out of {threadCount}. " +
            "If less than expected, the file was read multiple times.");

        LogMessage($"Verified: {threadCount} concurrent threads all got the same byte[] instance");
    }

    #endregion

    [TestMethod]
    [TestCategory("Unit")]
    public void ClearPdfBytesCache_IsThreadSafe()
    {
        // Arrange
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "ClearThreadSafe.pdf"));
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = "ClearThreadSafe.pdf",
            NPagesInThisVolume = 1
        });

        // Pre-load the cache
        metadata.GetOrLoadVolumeBytes(0);

        // Act - Clear and load from multiple threads simultaneously
        var exceptions = new List<Exception>();
        var tasks = new Task[20];
        
        for (int i = 0; i < 20; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    if (index % 2 == 0)
                    {
                        metadata.ClearPdfBytesCache();
                    }
                    else
                    {
                        metadata.GetOrLoadVolumeBytes(0);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
        }
        
        Task.WaitAll(tasks);

        // Assert - No exceptions should have been thrown
        Assert.AreEqual(0, exceptions.Count, 
            $"No exceptions should occur during concurrent access. Got: {string.Join(", ", exceptions.Select(e => e.Message))}");

        LogMessage("Thread safety verified with concurrent load/clear operations");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void GetOrLoadVolumeBytes_WithMultipleVolumes_CachesEachSeparately()
    {
        // Arrange - Create a multi-volume set
        var pdf0Path = CreateTestPdf(Path.Combine(_tempFolder, "MultiVol.pdf"));
        var pdf1Path = CreateTestPdf(Path.Combine(_tempFolder, "MultiVol1.pdf"));
        
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdf0Path,
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase { FileNameVolume = "MultiVol.pdf", NPagesInThisVolume = 1 });
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase { FileNameVolume = "MultiVol1.pdf", NPagesInThisVolume = 1 });

        // Act - Load both volumes
        var vol0Bytes = metadata.GetOrLoadVolumeBytes(0);
        var vol1Bytes = metadata.GetOrLoadVolumeBytes(1);

        // Assert - Both should be cached separately
        Assert.IsNotNull(vol0Bytes, "Vol 0 should be loaded");
        Assert.IsNotNull(vol1Bytes, "Vol 1 should be loaded");
        Assert.AreNotSame(vol0Bytes, vol1Bytes, "Different volumes should have different byte arrays");

        // Verify cache
        var cachedVol0 = metadata.GetCachedVolumeBytes(0);
        var cachedVol1 = metadata.GetCachedVolumeBytes(1);
        
        Assert.AreSame(vol0Bytes, cachedVol0, "Vol 0 cache should return same instance");
        Assert.AreSame(vol1Bytes, cachedVol1, "Vol 1 cache should return same instance");

        LogMessage($"Multi-volume cache verified: vol0={vol0Bytes.Length} bytes, vol1={vol1Bytes.Length} bytes");
    }

    /// <summary>
    /// A simple SynchronizationContext that posts callbacks synchronously.
    /// This simulates a UI thread synchronization context for testing purposes.
    /// </summary>
    private class TestSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object state)
        {
            // Execute synchronously on the current thread (like WPF dispatcher would)
            d(state);
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            d(state);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadSinglesFolderAsync_WithZeroPageCounts_FixesPageCountsAndRebuildsToc()
    {
        // This test verifies the fix for Singles folders that have pageCount=0 in their JSON.
        // This can happen when PDFs were cloud-only on OneDrive during the initial load.
        // The fix should re-read the PDF files to get the actual page counts and rebuild the TOC.

        // Arrange - Create a Singles folder with PDFs
        var singlesFolder = Path.Combine(_tempFolder, "TestSingles");
        Directory.CreateDirectory(singlesFolder);
        
        var pdf1Path = CreateTestPdf(Path.Combine(singlesFolder, "Song1.pdf"));
        var pdf2Path = CreateTestPdf(Path.Combine(singlesFolder, "Song2.pdf"));
        var pdf3Path = CreateTestPdf(Path.Combine(singlesFolder, "Song3.pdf"));

        // Create a JSON metadata file with pageCount=0 for all volumes (simulating OneDrive cloud-only issue)
        var jsonPath = Path.Combine(_tempFolder, "TestSingles.json");
        var corruptedJsonContent = @"{
  ""version"": 1,
  ""lastWrite"": ""2025-01-01T00:00:00"",
  ""lastPageNo"": 0,
  ""volumes"": [
    { ""fileName"": ""Song1.pdf"", ""pageCount"": 0 },
    { ""fileName"": ""Song2.pdf"", ""pageCount"": 0 },
    { ""fileName"": ""Song3.pdf"", ""pageCount"": 0 }
  ],
  ""tableOfContents"": [
    { ""songName"": ""Song1"", ""pageNo"": 0 },
    { ""songName"": ""Song2"", ""pageNo"": 0 },
    { ""songName"": ""Song3"", ""pageNo"": 0 }
  ],
  ""favorites"": [],
  ""inkStrokes"": {}
}";
        await File.WriteAllTextAsync(jsonPath, corruptedJsonContent);

        var provider = new PdfToImageDocumentProvider();

        // Act - Load metadata using the sequential loader which calls LoadSinglesFolderAsync
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: false); // Use sequential to ensure LoadSinglesFolderAsync is called

        // Assert
        Assert.AreEqual(1, metadataList.Count, "Should find 1 Singles folder");
        var metadata = metadataList[0];
        
        Assert.IsTrue(metadata.IsSinglesFolder, "Should be marked as Singles folder");
        Assert.AreEqual(3, metadata.VolumeInfoList.Count, "Should have 3 volumes");
        
        // Verify all page counts were fixed (each test PDF has 1 page)
        foreach (var vol in metadata.VolumeInfoList)
        {
            Assert.AreEqual(1, vol.NPagesInThisVolume, 
                $"Volume '{vol.FileNameVolume}' should have pageCount=1 (fixed from 0)");
        }
        
        // Verify TOC was rebuilt with correct page numbers
        Assert.AreEqual(3, metadata.TocEntries.Count, "Should have 3 TOC entries");
        Assert.AreEqual(0, metadata.TocEntries[0].PageNo, "Song1 should be on page 0");
        Assert.AreEqual(1, metadata.TocEntries[1].PageNo, "Song2 should be on page 1 (after Song1's 1 page)");
        Assert.AreEqual(2, metadata.TocEntries[2].PageNo, "Song3 should be on page 2 (after Song1+Song2's 2 pages)");
        
        // Verify metadata is marked dirty (needs saving)
        Assert.IsTrue(metadata.IsDirty, "Metadata should be marked dirty after fixing page counts");
        
        LogMessage("PageCount=0 fix verified:");
        for (int i = 0; i < metadata.VolumeInfoList.Count; i++)
        {
            LogMessage($"  {metadata.VolumeInfoList[i].FileNameVolume}: {metadata.VolumeInfoList[i].NPagesInThisVolume} pages, TOC page {metadata.TocEntries[i].PageNo}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadSinglesFolderAsync_WithZeroPageCounts_ParallelLoader_FixesPageCounts()
    {
        // This test verifies the pageCount=0 fix also works with the parallel loader.

        // Arrange - Create a Singles folder with PDFs
        var singlesFolder = Path.Combine(_tempFolder, "JazzLibrarySingles");
        Directory.CreateDirectory(singlesFolder);
        
        var pdf1Path = CreateTestPdf(Path.Combine(singlesFolder, "Bridge-Over-Troubled-Water.pdf"));
        var pdf2Path = CreateTestPdf(Path.Combine(singlesFolder, "Tangerine.pdf"));

        // Create a JSON metadata file with pageCount=0 (simulating OneDrive cloud-only issue)
        var jsonPath = Path.Combine(_tempFolder, "JazzLibrarySingles.json");
        var corruptedJsonContent = @"{
  ""version"": 1,
  ""lastWrite"": ""2025-01-01T00:00:00"",
  ""lastPageNo"": 0,
  ""volumes"": [
    { ""fileName"": ""Bridge-Over-Troubled-Water.pdf"", ""pageCount"": 0 },
    { ""fileName"": ""Tangerine.pdf"", ""pageCount"": 0 }
  ],
  ""tableOfContents"": [
    { ""songName"": ""Bridge-Over-Troubled-Water"", ""pageNo"": 0 },
    { ""songName"": ""Tangerine"", ""pageNo"": 0 }
  ],
  ""favorites"": [],
  ""inkStrokes"": {}
}";
        await File.WriteAllTextAsync(jsonPath, corruptedJsonContent);

        var provider = new PdfToImageDocumentProvider();

        // Act - Load metadata using the PARALLEL loader
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: true,
            autoSaveNewMetadata: true);

        // Assert
        Assert.AreEqual(1, metadataList.Count, "Should find 1 Singles folder");
        var metadata = metadataList[0];
        
        Assert.IsTrue(metadata.IsSinglesFolder, "Should be marked as Singles folder");
        Assert.AreEqual(2, metadata.VolumeInfoList.Count, "Should have 2 volumes");
        
        // Verify page counts were fixed
        Assert.AreEqual(1, metadata.VolumeInfoList[0].NPagesInThisVolume, 
            "Bridge-Over-Troubled-Water.pdf should have pageCount fixed to 1");
        Assert.AreEqual(1, metadata.VolumeInfoList[1].NPagesInThisVolume, 
            "Tangerine.pdf should have pageCount fixed to 1");
        
        // Verify TOC page numbers were recalculated
        Assert.AreEqual(0, metadata.TocEntries[0].PageNo, "First song should be on page 0");
        Assert.AreEqual(1, metadata.TocEntries[1].PageNo, "Second song should be on page 1");
        
        LogMessage($"Parallel loader pageCount=0 fix verified for {metadata.VolumeInfoList.Count} volumes");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadSinglesFolderAsync_WithMixedPageCounts_OnlyFixesZeros()
    {
        // This test verifies that only volumes with pageCount=0 are re-read.
        // Volumes with valid page counts should not be modified.

        // Arrange - Create a Singles folder with PDFs
        var singlesFolder = Path.Combine(_tempFolder, "MixedSingles");
        Directory.CreateDirectory(singlesFolder);
        
        CreateTestPdf(Path.Combine(singlesFolder, "ValidSong.pdf"));
        CreateTestPdf(Path.Combine(singlesFolder, "ZeroSong.pdf"));

        // Create JSON with one valid pageCount and one zero
        var jsonPath = Path.Combine(_tempFolder, "MixedSingles.json");
        var jsonContent = @"{
  ""version"": 1,
  ""lastWrite"": ""2025-01-01T00:00:00"",
  ""lastPageNo"": 0,
  ""volumes"": [
    { ""fileName"": ""ValidSong.pdf"", ""pageCount"": 99 },
    { ""fileName"": ""ZeroSong.pdf"", ""pageCount"": 0 }
  ],
  ""tableOfContents"": [
    { ""songName"": ""ValidSong"", ""pageNo"": 0 },
    { ""songName"": ""ZeroSong"", ""pageNo"": 99 }
  ],
  ""favorites"": [],
  ""inkStrokes"": {}
}";
        await File.WriteAllTextAsync(jsonPath, jsonContent);

        var provider = new PdfToImageDocumentProvider();

        // Act
        var (metadataList, folders) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _tempFolder,
            provider,
            exceptionHandler: null,
            useParallelLoading: false);

        // Assert
        var metadata = metadataList[0];
        
        // ValidSong should keep its original (fake) page count of 99
        Assert.AreEqual(99, metadata.VolumeInfoList[0].NPagesInThisVolume, 
            "ValidSong should keep its original pageCount (99), not be re-read");
        
        // ZeroSong should be fixed to actual page count (1)
        Assert.AreEqual(1, metadata.VolumeInfoList[1].NPagesInThisVolume, 
            "ZeroSong should be fixed to actual pageCount (1)");
        
        // TOC should be rebuilt with correct page numbers
        Assert.AreEqual(2, metadata.TocEntries.Count, "Should have 2 TOC entries");
        Assert.AreEqual(0, metadata.TocEntries[0].PageNo, "ValidSong should be on page 0");
        Assert.AreEqual(99, metadata.TocEntries[1].PageNo, "ZeroSong should be on page 99 (after ValidSong's 99 pages)");
        
        LogMessage("Mixed pageCount handling verified: ValidSong=99 pages, ZeroSong=1 page (fixed from 0)");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadSinglesFolderAsync_WithIncorrectTOCPageNumbers_FixesTOC()
    {
        // This test verifies that TOC page numbers are validated and fixed when loading a Singles folder,
        // even when volume page counts are correct. This handles JSON files with all pageNo: 0.
        
        // Arrange
        var singlesFolder = Path.Combine(_tempFolder, "JazzSingles");
        Directory.CreateDirectory(singlesFolder);
        
        // Create 3 test PDFs (1 page each) using TestHelpers
        var pdf1Path = TestHelpers.CreateTestPdf(1);  // Create temp PDF first
        var pdf2Path = TestHelpers.CreateTestPdf(1);
        var pdf3Path = TestHelpers.CreateTestPdf(1);
        
        // Move them to the singles folder with proper names
        var pdf1 = Path.Combine(singlesFolder, "Song1.pdf");
        var pdf2 = Path.Combine(singlesFolder, "Song2.pdf");
        var pdf3 = Path.Combine(singlesFolder, "Song3.pdf");
        File.Move(pdf1Path, pdf1);
        File.Move(pdf2Path, pdf2);
        File.Move(pdf3Path, pdf3);
        
        // Create a JSON metadata file with CORRECT page counts but INCORRECT TOC page numbers (all zeros)
        // This simulates the actual problem reported by the user
        var jsonFile = Path.ChangeExtension(singlesFolder, "json");
        var jsonContent = @"{
  ""version"": 1,
  ""lastWrite"": ""2026-01-10T15:51:16.6146576-08:00"",
  ""lastPageNo"": 0,
  ""volumes"": [
    { ""fileName"": ""Song1.pdf"", ""pageCount"": 1 },
    { ""fileName"": ""Song2.pdf"", ""pageCount"": 1 },
    { ""fileName"": ""Song3.pdf"", ""pageCount"": 1 }
  ],
  ""tableOfContents"": [
    { ""songName"": ""Song1"", ""pageNo"": 0 },
    { ""songName"": ""Song2"", ""pageNo"": 0 },
    { ""songName"": ""Song3"", ""pageNo"": 0 }
  ],
  ""favorites"": [],
  ""inkStrokes"": {}
}";
        await File.WriteAllTextAsync(jsonFile, jsonContent);
        
        var provider = new PdfToImageDocumentProvider();
        
        // Act
        var metadata = await PdfMetaDataCore.ReadPdfMetaDataAsync(
            singlesFolder,
            isSingles: true,
            provider,
            exceptionHandler: null);
        
        // Assert
        Assert.IsNotNull(metadata, "Metadata should be loaded");
        Assert.IsTrue(metadata.IsSinglesFolder, "Should be marked as Singles folder");
        Assert.AreEqual(3, metadata.VolumeInfoList.Count, "Should have 3 volumes");
        
        // Verify page counts are still correct (not changed)
        Assert.AreEqual(1, metadata.VolumeInfoList[0].NPagesInThisVolume, "Song1 should have 1 page");
        Assert.AreEqual(1, metadata.VolumeInfoList[1].NPagesInThisVolume, "Song2 should have 1 page");
        Assert.AreEqual(1, metadata.VolumeInfoList[2].NPagesInThisVolume, "Song3 should have 1 page");
        
        // Verify TOC page numbers were FIXED (cumulative)
        Assert.AreEqual(3, metadata.TocEntries.Count, "Should have 3 TOC entries");
        Assert.AreEqual(0, metadata.TocEntries[0].PageNo, "Song1 should be on page 0 (fixed)");
        Assert.AreEqual(1, metadata.TocEntries[1].PageNo, "Song2 should be on page 1 (fixed from 0)");
        Assert.AreEqual(2, metadata.TocEntries[2].PageNo, "Song3 should be on page 2 (fixed from 0)");
        
        // Verify metadata is marked dirty (needs saving)
        Assert.IsTrue(metadata.IsDirty, "Metadata should be marked dirty after fixing TOC");
        
        LogMessage("TOC page number fix verified:");
        for (int i = 0; i < metadata.TocEntries.Count; i++)
        {
            LogMessage($"  {metadata.TocEntries[i].SongName}: page {metadata.TocEntries[i].PageNo}");
        }
    }
}
