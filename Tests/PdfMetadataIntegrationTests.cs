using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Windows.Data.Pdf;
using Windows.Storage;

namespace Tests
{
    /// <summary>
    /// Integration tests for PdfMetaData that involve file I/O and Windows.Data.Pdf API
    /// </summary>
    [TestClass]
    public class PdfMetadataIntegrationTests : TestBase
    {
        private string testDirectory;
        private string testPdfPath;

        [TestInitialize]
        public async Task Setup()
        {
            testDirectory = Path.Combine(Path.GetTempPath(), $"PdfIntegrationTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(testDirectory);

            // Create a minimal test PDF programmatically for testing
            testPdfPath = Path.Combine(testDirectory, "test.pdf");
            await CreateMinimalTestPdfAsync(testPdfPath);
            
            AddLogEntry($"Test directory created: {testDirectory}");
            AddLogEntry($"Test PDF created: {testPdfPath}");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(testDirectory))
            {
                try
                {
                    Directory.Delete(testDirectory, recursive: true);
                    AddLogEntry($"Test directory cleaned up: {testDirectory}");
                }
                catch (Exception ex)
                {
                    AddLogEntry($"Failed to cleanup test directory: {ex.Message}");
                }
            }
        }

        #region Test PDF Creation Helper

        /// <summary>
        /// Creates a minimal valid PDF file for testing purposes
        /// This is a simple PDF 1.4 structure with one blank page
        /// </summary>
        private async Task CreateMinimalTestPdfAsync(string path)
        {
            // Minimal PDF 1.4 structure with one page
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
/Resources <<
/Font <<
/F1 <<
/Type /Font
/Subtype /Type1
/BaseFont /Helvetica
>>
>>
>>
/MediaBox [0 0 612 792]
/Contents 4 0 R
>>
endobj
4 0 obj
<<
/Length 44
>>
stream
BT
/F1 12 Tf
100 700 Td
(Test PDF) Tj
ET
endstream
endobj
xref
0 5
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
0000000317 00000 n 
trailer
<<
/Size 5
/Root 1 0 R
>>
startxref
410
%%EOF";

            await File.WriteAllTextAsync(path, pdfContent);
        }

        #endregion

        #region File System Integration Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestLoadAllPdfMetaDataFromDisk_EmptyDirectory_ReturnsEmptyList()
        {
            // Arrange
            var emptyDir = Path.Combine(testDirectory, "empty");
            Directory.CreateDirectory(emptyDir);

            // Act
            var (metadataList, folderList) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(emptyDir);

            // Assert
            Assert.IsNotNull(metadataList);
            Assert.AreEqual(0, metadataList.Count);
            AddLogEntry($"Empty directory returned {metadataList.Count} metadata items");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestLoadAllPdfMetaDataFromDisk_WithPdf_CreatesMetadata()
        {
            // Arrange
            var testFolder = Path.Combine(testDirectory, "pdfs");
            Directory.CreateDirectory(testFolder);
            var pdfPath = Path.Combine(testFolder, "test.pdf");
            await CreateMinimalTestPdfAsync(pdfPath);

            // Act
            var (metadataList, folderList) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(testFolder);

            // Assert
            Assert.IsNotNull(metadataList);
            Assert.AreEqual(1, metadataList.Count);
            Assert.IsTrue(metadataList[0]._FullPathFile.Contains("test.pdf"));
            AddLogEntry($"Found {metadataList.Count} PDF(s): {metadataList[0]}");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestSavePdfMetaFileData_CreatesBmkFile()
        {
            // Arrange
            var metadata = new PdfMetaData
            {
                _FullPathFile = testPdfPath,
                PageNumberOffset = 0
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo
            {
                FileNameVolume = Path.GetFileName(testPdfPath),
                NPagesInThisVolume = 1,
                Rotation = 0
            });

            // Act
            metadata.SaveIfDirty(ForceDirty: true);

            // Assert
            var bmkPath = Path.ChangeExtension(testPdfPath, ".bmk");
            Assert.IsTrue(File.Exists(bmkPath), $"BMK file should exist at {bmkPath}");
            AddLogEntry($"BMK file created at: {bmkPath}");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestMetadataPersistence_SaveAndReload_PreservesData()
        {
            // Arrange
            var metadata = new PdfMetaData
            {
                _FullPathFile = testPdfPath,
                PageNumberOffset = 0
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo
            {
                FileNameVolume = Path.GetFileName(testPdfPath),
                NPagesInThisVolume = 1,
                Rotation = 0
            });
            metadata.ToggleFavorite(0, IsFavorite: true, FavoriteName: "Test Favorite");
            metadata.lstTocEntries.Add(new TOCEntry
            {
                SongName = "Test Song",
                Composer = "Test Composer",
                PageNo = 0
            });

            // Act - Save
            metadata.SaveIfDirty(ForceDirty: true);

            // Act - Reload
            var (reloadedList, _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(testDirectory);

            // Assert
            Assert.AreEqual(1, reloadedList.Count);
            var reloaded = reloadedList[0];
            Assert.AreEqual(1, reloaded.lstVolInfo.Count);
            Assert.IsTrue(reloaded.IsFavorite(0));
            Assert.AreEqual(1, reloaded.lstTocEntries.Count);
            Assert.AreEqual("Test Song", reloaded.lstTocEntries[0].SongName);
            AddLogEntry($"Metadata persisted successfully: {reloaded.lstTocEntries.Count} TOC entries, {reloaded.dictFav.Count} favorites");
        }

        #endregion

        #region Windows.Data.Pdf API Integration Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestGetPdfDocumentForFileAsync_ValidPdf_ReturnsPdfDocument()
        {
            // Act
            var pdfDoc = await PdfMetaData.GetPdfDocumentForFileAsync(testPdfPath);

            // Assert
            Assert.IsNotNull(pdfDoc);
            Assert.IsTrue(pdfDoc.PageCount > 0);
            AddLogEntry($"PDF loaded successfully: {pdfDoc.PageCount} page(s)");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestGetPdfDocumentForFileAsync_NonExistentFile_ThrowsException()
        {
            // Arrange
            var nonExistentPath = Path.Combine(testDirectory, "nonexistent.pdf");

            // Act & Assert
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(async () =>
            {
                await PdfMetaData.GetPdfDocumentForFileAsync(nonExistentPath);
            });
            AddLogEntry($"Non-existent PDF correctly threw FileNotFoundException");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestPdfMetaData_InitializeListPdfDocuments_LoadsDocuments()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange
                var metadata = new PdfMetaData
                {
                    _FullPathFile = testPdfPath,
                    PageNumberOffset = 0
                };
                metadata.lstVolInfo.Add(new PdfVolumeInfo
                {
                    FileNameVolume = Path.GetFileName(testPdfPath),
                    NPagesInThisVolume = 1,
                    Rotation = 0
                });

                // Act
                metadata.InitializeListPdfDocuments();

                // Assert
                Assert.IsNotNull(metadata.lstVolInfo);
                Assert.AreEqual(1, metadata.lstVolInfo.Count);
                AddLogEntry($"Initialized {metadata.lstVolInfo.Count} PDF document(s)");
            });
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestGetPdfDocumentForPageno_ValidPage_ReturnsDocument()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange
                var metadata = new PdfMetaData
                {
                    _FullPathFile = testPdfPath,
                    PageNumberOffset = 0
                };
                metadata.lstVolInfo.Add(new PdfVolumeInfo
                {
                    FileNameVolume = Path.GetFileName(testPdfPath),
                    NPagesInThisVolume = 1,
                    Rotation = 0
                });
                metadata.InitializeListPdfDocuments();

                // Act
                var (pdfDoc, pageNo) = await metadata.GetPdfDocumentForPageno(0);

                // Assert
                Assert.IsNotNull(pdfDoc);
                Assert.AreEqual(0, (int)pageNo);
                AddLogEntry($"Retrieved PDF document for page 0");
            });
        }

        #endregion

        #region Multi-Volume Integration Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestMultiVolume_TwoPdfs_CreatesSeparateMetadata()
        {
            // Arrange - Create two separate PDFs without continuation naming pattern
            var vol1Path = Path.Combine(testDirectory, "book1.pdf");
            var vol2Path = Path.Combine(testDirectory, "book2.pdf");
            await CreateMinimalTestPdfAsync(vol1Path);
            await CreateMinimalTestPdfAsync(vol2Path);

            // Act
            var (metadataList, _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(testDirectory);

            // Assert - Without continuation naming (e.g., test0.pdf, test1.pdf), these are separate PDFs
            Assert.AreEqual(2, metadataList.Count, "Should create separate metadata for each PDF");
            AddLogEntry($"Separate PDFs created {metadataList.Count} metadata entries");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestMultiVolume_FavoritePersistence_AcrossVolumes()
        {
            // Arrange
            var vol1Path = Path.Combine(testDirectory, "vol1.pdf");
            var vol2Path = Path.Combine(testDirectory, "vol2.pdf");
            await CreateMinimalTestPdfAsync(vol1Path);
            await CreateMinimalTestPdfAsync(vol2Path);

            var (metadataList, _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(testDirectory);
            var metadata = metadataList[0];
            
            // Add favorites across volumes
            metadata.ToggleFavorite(0, IsFavorite: true, FavoriteName: "Volume 1 Page 0");
            metadata.ToggleFavorite(1, IsFavorite: true, FavoriteName: "Volume 2 Page 0");
            metadata.SaveIfDirty(ForceDirty: true);

            // Act - Reload
            var (reloadedList, _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(testDirectory);
            var reloaded = reloadedList[0];

            // Assert
            Assert.AreEqual(2, reloaded.dictFav.Count);
            Assert.IsTrue(reloaded.IsFavorite(0));
            Assert.IsTrue(reloaded.IsFavorite(1));
            AddLogEntry($"Favorites persisted across volumes: {reloaded.dictFav.Count} favorites");
        }

        #endregion

        #region Rotation Integration Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestRotation_SaveAndReload_PreservesRotation()
        {
            // Arrange
            var metadata = new PdfMetaData
            {
                _FullPathFile = testPdfPath,
                PageNumberOffset = 0
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo
            {
                FileNameVolume = Path.GetFileName(testPdfPath),
                NPagesInThisVolume = 1,
                Rotation = 0
            });

            // Rotate the page
            metadata.Rotate(0);
            Assert.AreEqual(1, metadata.lstVolInfo[0].Rotation);
            
            metadata.SaveIfDirty(ForceDirty: true);

            // Act - Reload
            var (reloadedList, _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(testDirectory);

            // Assert
            var reloaded = reloadedList[0];
            Assert.AreEqual(1, reloaded.lstVolInfo[0].Rotation);
            AddLogEntry($"Rotation persisted: {reloaded.lstVolInfo[0].Rotation}");
        }

        #endregion

        #region Error Handling Integration Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestLoadFromDisk_CorruptedBmkFile_HandlesGracefully()
        {
            // Arrange
            var pdfPath = Path.Combine(testDirectory, "test_corrupt.pdf");
            await CreateMinimalTestPdfAsync(pdfPath);
            
            // Create a corrupted BMK file
            var bmkPath = Path.ChangeExtension(pdfPath, ".bmk");
            await File.WriteAllTextAsync(bmkPath, "This is not valid XML!");

            // Act
            var (metadataList, _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(testDirectory);

            // Assert - Should still load, just without the corrupted metadata
            Assert.IsNotNull(metadataList);
            // The system should either skip the corrupted file or create fresh metadata
            AddLogEntry($"Handled corrupted BMK file gracefully: {metadataList.Count} metadata items");
        }

        #endregion

        #region TOC Integration Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestTocEntries_SaveAndReload_PreservesComposerInfo()
        {
            // Arrange
            var metadata = new PdfMetaData
            {
                _FullPathFile = testPdfPath,
                PageNumberOffset = 0
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo
            {
                FileNameVolume = Path.GetFileName(testPdfPath),
                NPagesInThisVolume = 1,
                Rotation = 0
            });

            var tocEntry = new TOCEntry
            {
                SongName = "Amazing Grace",
                Composer = "John Newton",
                Date = "1779",
                Notes = "Classic hymn",
                PageNo = 0
            };
            metadata.lstTocEntries.Add(tocEntry);
            metadata.SaveIfDirty(ForceDirty: true);

            // Act
            var (reloadedList, _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(testDirectory);

            // Assert
            var reloaded = reloadedList[0];
            Assert.AreEqual(1, reloaded.lstTocEntries.Count);
            var reloadedToc = reloaded.lstTocEntries[0];
            Assert.AreEqual("Amazing Grace", reloadedToc.SongName);
            Assert.AreEqual("John Newton", reloadedToc.Composer);
            Assert.AreEqual("1779", reloadedToc.Date);
            Assert.AreEqual("Classic hymn", reloadedToc.Notes);
            AddLogEntry($"TOC entry persisted: {reloadedToc}");
        }

        #endregion
    }
}
