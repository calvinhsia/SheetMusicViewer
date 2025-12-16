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
