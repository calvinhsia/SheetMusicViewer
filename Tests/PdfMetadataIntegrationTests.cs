using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using SheetMusicViewer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
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

        #region Ink Conversion Tests

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestConvertInkToJson_WithWpfStrokes_ConvertsSuccessfully()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange - Create metadata with WPF ISF ink data
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

                // Create WPF ink strokes
                var strokeCollection = new System.Windows.Ink.StrokeCollection();
                var points = new System.Windows.Input.StylusPointCollection
                {
                    new System.Windows.Input.StylusPoint(10, 10),
                    new System.Windows.Input.StylusPoint(100, 100)
                };
                var stroke = new System.Windows.Ink.Stroke(points);
                stroke.DrawingAttributes.Color = Colors.Red;
                stroke.DrawingAttributes.Width = 3.0;
                strokeCollection.Add(stroke);

                // Save as ISF
                using (var ms = new MemoryStream())
                {
                    strokeCollection.Save(ms, compress: true);
                    var inkData = new InkStrokeClass
                    {
                        Pageno = 0,
                        InkStrokeDimension = new PortablePoint(800, 600),
                        StrokeData = ms.ToArray()
                    };
                    metadata.dictInkStrokes[0] = inkData;
                }

                // Act - Convert to JSON
                int convertedCount = BmkJsonConverter.ConvertPdfMetadataInkToJson(metadata);

                // Assert
                Assert.AreEqual(1, convertedCount, "Should convert 1 page");
                Assert.IsTrue(metadata.IsDirty, "Metadata should be marked dirty");
                
                // Verify it's now JSON format
                var convertedInk = metadata.dictInkStrokes[0];
                var jsonText = System.Text.Encoding.UTF8.GetString(convertedInk.StrokeData);
                Assert.IsTrue(jsonText.StartsWith("{"), "Should be JSON format");
                Assert.IsTrue(jsonText.Contains("strokes"), "Should contain strokes property"); // camelCase now
                
                // Verify data is preserved using JsonSerializer.Deserialize
                var portable = JsonSerializer.Deserialize<PortableInkStrokeCollection>(jsonText);
                Assert.IsNotNull(portable);
                Assert.AreEqual(1, portable.Strokes.Count);
                Assert.AreEqual(2, portable.Strokes[0].Points.Count);
                Assert.AreEqual("#FF0000", portable.Strokes[0].Color); // Red
                Assert.AreEqual(3.0, portable.Strokes[0].Thickness);
                
                AddLogEntry($"Successfully converted ISF to JSON: {portable.Strokes.Count} strokes");
            });
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestConvertInkToJson_AlreadyJson_SkipsConversion()
        {
            // Arrange - Create metadata with JSON ink data
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

            var portableStrokes = new PortableInkStrokeCollection
            {
                CanvasWidth = 800,
                CanvasHeight = 600
            };
            portableStrokes.Strokes.Add(new PortableInkStroke
            {
                Color = "#0000FF",
                Thickness = 2.0,
                Points = new List<PortableInkPoint>
                {
                    new PortableInkPoint { X = 20, Y = 20 },
                    new PortableInkPoint { X = 200, Y = 200 }
                }
            });

            var json = JsonSerializer.Serialize(portableStrokes, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            var inkData = new InkStrokeClass
            {
                Pageno = 0,
                InkStrokeDimension = new PortablePoint(800, 600),
                StrokeData = System.Text.Encoding.UTF8.GetBytes(json)
            };
            metadata.dictInkStrokes[0] = inkData;

            // Act
            int convertedCount = BmkJsonConverter.ConvertPdfMetadataInkToJson(metadata);

            // Assert
            Assert.AreEqual(0, convertedCount, "Should not convert already-JSON data");
            AddLogEntry($"Correctly skipped JSON data: {convertedCount} converted");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestConvertInkToJson_SaveAndReload_PreservesJsonFormat()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();
                
                // Arrange - Create and convert
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

                var strokeCollection = new System.Windows.Ink.StrokeCollection();
                var points = new System.Windows.Input.StylusPointCollection
                {
                    new System.Windows.Input.StylusPoint(50, 50),
                    new System.Windows.Input.StylusPoint(150, 150)
                };
                var stroke = new System.Windows.Ink.Stroke(points);
                stroke.DrawingAttributes.Color = Colors.Green;
                strokeCollection.Add(stroke);

                using (var ms = new MemoryStream())
                {
                    strokeCollection.Save(ms, compress: true);
                    metadata.dictInkStrokes[0] = new InkStrokeClass
                    {
                        Pageno = 0,
                        InkStrokeDimension = new PortablePoint(800, 600),
                        StrokeData = ms.ToArray()
                    };
                }

                BmkJsonConverter.ConvertPdfMetadataInkToJson(metadata);
                metadata.SaveIfDirty(ForceDirty: true);

                // Act - Reload
                var (reloadedList, _) = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(testDirectory);

                // Assert
                Assert.AreEqual(1, reloadedList.Count);
                var reloaded = reloadedList[0];
                Assert.AreEqual(1, reloaded.dictInkStrokes.Count);
                
                var reloadedInk = reloaded.dictInkStrokes[0];
                var jsonText = System.Text.Encoding.UTF8.GetString(reloadedInk.StrokeData);
                Assert.IsTrue(jsonText.StartsWith("{"), "Should remain JSON after save/reload");
                
                var portable = JsonSerializer.Deserialize<PortableInkStrokeCollection>(jsonText);
                Assert.AreEqual(1, portable.Strokes.Count);
                Assert.AreEqual(2, portable.Strokes[0].Points.Count);
                Assert.AreEqual("#008000", portable.Strokes[0].Color); // Green (WPF Colors.Green is #008000)
                
                AddLogEntry($"JSON format preserved after save/reload");
            });
        }

        #endregion
    }
}
