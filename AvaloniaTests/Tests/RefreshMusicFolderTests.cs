using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using SheetMusicViewer.Desktop;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Unit tests for the Refresh Music Folder functionality.
/// Tests the core logic of reloading PDF metadata from disk.
/// </summary>
[TestClass]
public class RefreshMusicFolderTests : TestBase
{
    private string _testFolder = null!;
    private string _testSettingsPath = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        
        // Create a temporary folder for test PDFs
        _testFolder = Path.Combine(Path.GetTempPath(), $"RefreshTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testFolder);
        
        // Create a temp settings file path for testing
        _testSettingsPath = Path.Combine(Path.GetTempPath(), $"RefreshSettingsTest_{Guid.NewGuid():N}.json");
        AppSettings.ResetForTesting(_testSettingsPath);
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        base.TestCleanup();
        
        // Clean up test folder
        try
        {
            if (Directory.Exists(_testFolder))
            {
                Directory.Delete(_testFolder, recursive: true);
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Warning: Could not clean up test folder: {ex.Message}");
        }
        
        // Clean up test settings file
        try
        {
            if (File.Exists(_testSettingsPath))
            {
                File.Delete(_testSettingsPath);
            }
        }
        catch { }
        
        // Reset the singleton for other tests
        AppSettings.ResetForTesting();
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAllPdfMetaData_EmptyFolder_ReturnsEmptyList()
    {
        // Arrange - empty folder (created in TestInitialize)
        var provider = new PdfToImageDocumentProvider();

        // Act
        var (metadata, _) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _testFolder,
            provider,
            useParallelLoading: false);

        // Assert
        Assert.IsNotNull(metadata);
        Assert.AreEqual(0, metadata.Count, "Empty folder should return empty metadata list");
        LogMessage($"Empty folder correctly returned 0 PDFs");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAllPdfMetaData_WithPdfFiles_ReturnsMetadata()
    {
        // Arrange - create test PDFs in separate subfolders to ensure they're treated as separate books
        // (PDFs in the same folder with similar names may be treated as multi-volume books)
        var subFolder1 = Path.Combine(_testFolder, "Book1");
        var subFolder2 = Path.Combine(_testFolder, "Book2");
        Directory.CreateDirectory(subFolder1);
        Directory.CreateDirectory(subFolder2);
        
        var pdfPath1 = Path.Combine(subFolder1, "TestBook1.pdf");
        var pdfPath2 = Path.Combine(subFolder2, "TestBook2.pdf");
        
        CreateMinimalPdf(pdfPath1);
        CreateMinimalPdf(pdfPath2);
        
        var provider = new PdfToImageDocumentProvider();

        // Act
        var (metadata, _) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _testFolder,
            provider,
            useParallelLoading: false);

        // Assert
        Assert.IsNotNull(metadata);
        Assert.AreEqual(2, metadata.Count, "Should find 2 PDF files in separate folders");
        
        var bookNames = metadata.Select(m => m.GetBookName(_testFolder)).ToList();
        LogMessage($"Found {metadata.Count} PDFs: {string.Join(", ", bookNames)}");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAllPdfMetaData_RefreshPicksUpNewFiles()
    {
        // Arrange - create initial PDF
        var pdfPath1 = Path.Combine(_testFolder, "InitialBook.pdf");
        CreateMinimalPdf(pdfPath1);
        
        var provider = new PdfToImageDocumentProvider();

        // First load
        var (metadata1, _) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _testFolder,
            provider,
            useParallelLoading: false);
        
        Assert.AreEqual(1, metadata1.Count, "Initial load should find 1 PDF");
        LogMessage($"Initial load found {metadata1.Count} PDF(s)");

        // Add new PDF (simulating file added to folder)
        var pdfPath2 = Path.Combine(_testFolder, "NewBook.pdf");
        CreateMinimalPdf(pdfPath2);

        // Act - Refresh (reload)
        var (metadata2, _) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _testFolder,
            provider,
            useParallelLoading: false);

        // Assert
        Assert.AreEqual(2, metadata2.Count, "Refresh should find 2 PDFs including the new one");
        
        var bookNames = metadata2.Select(m => m.GetBookName(_testFolder)).ToList();
        Assert.IsTrue(bookNames.Contains("NewBook"), "Should contain newly added NewBook");
        
        LogMessage($"After refresh found {metadata2.Count} PDFs: {string.Join(", ", bookNames)}");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAllPdfMetaData_RefreshPicksUpMetadataChanges()
    {
        // Arrange - create PDF with metadata
        var pdfPath = Path.Combine(_testFolder, "BookWithMetadata.pdf");
        CreateMinimalPdf(pdfPath);
        
        var provider = new PdfToImageDocumentProvider();

        // First load
        var (metadata1, _) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _testFolder,
            provider,
            useParallelLoading: false);
        
        Assert.AreEqual(1, metadata1.Count);
        var initialTocCount = metadata1[0].TocEntries.Count;
        LogMessage($"Initial load: TOC entries = {initialTocCount}");

        // Modify the metadata (add a TOC entry) and save
        var pdfMeta = metadata1[0];
        pdfMeta.TocEntries.Add(new TOCEntry { PageNo = 1, SongName = "Test Song" });
        pdfMeta.IsDirty = true;
        PdfMetaDataCore.SaveToJson(pdfMeta);
        LogMessage($"Added TOC entry to metadata and saved");

        // Act - Refresh (reload)
        var (metadata2, _) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _testFolder,
            provider,
            useParallelLoading: false);

        // Assert
        Assert.AreEqual(1, metadata2.Count);
        var newTocCount = metadata2[0].TocEntries.Count;
        Assert.IsTrue(newTocCount > initialTocCount, 
            $"Refresh should pick up added TOC entry. Initial={initialTocCount}, After={newTocCount}");
        
        LogMessage($"After refresh: TOC entries = {newTocCount}");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAllPdfMetaData_NonExistentFolder_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentFolder = Path.Combine(Path.GetTempPath(), $"NonExistent_{Guid.NewGuid():N}");
        var provider = new PdfToImageDocumentProvider();

        // Act
        var (metadata, _) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            nonExistentFolder,
            provider,
            useParallelLoading: false);

        // Assert
        Assert.IsNotNull(metadata);
        Assert.AreEqual(0, metadata.Count, "Non-existent folder should return empty list");
        LogMessage($"Non-existent folder correctly returned 0 PDFs");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAllPdfMetaData_SubfolderPdfs_AreFound()
    {
        // Arrange - create PDFs in subfolders
        var subFolder1 = Path.Combine(_testFolder, "Classical");
        var subFolder2 = Path.Combine(_testFolder, "Jazz");
        Directory.CreateDirectory(subFolder1);
        Directory.CreateDirectory(subFolder2);
        
        CreateMinimalPdf(Path.Combine(subFolder1, "Beethoven.pdf"));
        CreateMinimalPdf(Path.Combine(subFolder2, "Coltrane.pdf"));
        
        var provider = new PdfToImageDocumentProvider();

        // Act
        var (metadata, _) = await PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
            _testFolder,
            provider,
            useParallelLoading: false);

        // Assert
        Assert.IsNotNull(metadata);
        Assert.AreEqual(2, metadata.Count, "Should find PDFs in subfolders");
        
        var bookNames = metadata.Select(m => m.GetBookName(_testFolder)).ToList();
        LogMessage($"Found PDFs: {string.Join(", ", bookNames)}");
    }

    /// <summary>
    /// Creates a minimal valid PDF file for testing
    /// </summary>
    private void CreateMinimalPdf(string path)
    {
        var pdfContent = @"%PDF-1.4
1 0 obj
<<
/Type /Catalog
/Pages 2 0 R
>>
endobj
2 0 obj
<<
/Type /Pages
/Kids [3 0 R]
/Count 1
>>
endobj
3 0 obj
<<
/Type /Page
/Parent 2 0 R
/MediaBox [0 0 612 792]
/Resources <<>>
>>
endobj
xref
0 4
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
trailer
<<
/Size 4
/Root 1 0 R
>>
startxref
210
%%EOF";
        
        File.WriteAllText(path, pdfContent);
        LogMessage($"Created test PDF: {Path.GetFileName(path)}");
    }
}
