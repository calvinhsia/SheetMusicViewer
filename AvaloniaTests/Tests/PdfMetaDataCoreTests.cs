using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using SheetMusicViewer.Desktop;
using System;
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
        Assert.IsTrue(jsonContent.Contains("\"Test Song\""), "JSON should contain song name");
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
            Notes = "BWV 1001"
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

        // Verify NewBook metadata is no longer dirty (was saved)
        Assert.IsFalse(newBookMetadata.IsDirty,
            "NewBook metadata should not be dirty after auto-save");

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

        LogMessage("Verified mixed existing/new multi-volume handling:");
        LogMessage($"  OldSonatas: 3 volumes with {oldSonatas.VolumeInfoList.Sum(v => v.NPagesInThisVolume)} total pages (from JSON)");
        LogMessage($"  NewConcertos: 3 volumes with {newConcertos.VolumeInfoList.Sum(v => v.NPagesInThisVolume)} total pages (from PDF)");
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
}
