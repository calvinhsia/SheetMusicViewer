using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using SheetMusicViewer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Tests
{
    /// <summary>
    /// Tests for complete BMK JSON format conversion
    /// </summary>
    [TestClass]
    public class BmkJsonConversionTests : TestBase
    {
        private string testDirectory;
        private string testPdfPath;

        [TestInitialize]
        public async Task Setup()
        {
            testDirectory = Path.Combine(Path.GetTempPath(), $"BmkJsonTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(testDirectory);
            testPdfPath = Path.Combine(testDirectory, "test.pdf");
            await CreateMinimalTestPdfAsync(testPdfPath);
            AddLogEntry($"Test directory: {testDirectory}");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(testDirectory))
            {
                try
                {
                    Directory.Delete(testDirectory, recursive: true);
                }
                catch (Exception ex)
                {
                    AddLogEntry($"Cleanup failed: {ex.Message}");
                }
            }
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void TestBmkJsonConversion_CompleteMetadata_PreservesAllData()
        {
            // Arrange - Create complete metadata
            var metadata = new PdfMetaData
            {
                _FullPathFile = testPdfPath,
                PageNumberOffset = 5,
                LastPageNo = 10,
                Notes = "Test notes for the book",
                IsSinglesFolder = false
            };

            metadata.lstVolInfo.Add(new PdfVolumeInfo
            {
                FileNameVolume = Path.GetFileName(testPdfPath),
                NPagesInThisVolume = 20,
                Rotation = 1 // 90 degrees
            });

            metadata.lstTocEntries.Add(new TOCEntry
            {
                SongName = "Amazing Grace",
                Composer = "John Newton",
                Date = "1779",
                Notes = "Classic hymn",
                PageNo = 5
            });

            metadata.ToggleFavorite(7, IsFavorite: true, FavoriteName: "My favorite page");
            metadata.ToggleFavorite(12, IsFavorite: true, FavoriteName: "Another favorite");

            // Act - Convert to JSON
            var jsonData = BmkJsonConverter.ConvertToJson(metadata);
            var jsonText = BmkJsonConverter.SerializeToJson(jsonData);

            // Assert JSON properties
            Assert.AreEqual(1, jsonData.Version);
            Assert.AreEqual(5, jsonData.PageNumberOffset);
            Assert.AreEqual(10, jsonData.LastPageNo);
            Assert.AreEqual("Test notes for the book", jsonData.Notes);
            Assert.AreEqual(1, jsonData.Volumes.Count);
            Assert.AreEqual(20, jsonData.Volumes[0].PageCount);
            Assert.AreEqual(1, jsonData.Volumes[0].Rotation);
            Assert.AreEqual(1, jsonData.TableOfContents.Count);
            Assert.AreEqual("Amazing Grace", jsonData.TableOfContents[0].SongName);
            Assert.AreEqual("John Newton", jsonData.TableOfContents[0].Composer);
            Assert.AreEqual(2, jsonData.Favorites.Count);
            Assert.AreEqual("My favorite page", jsonData.Favorites[0].Name);

            // Act - Convert back from JSON
            var restored = BmkJsonConverter.ConvertFromJson(jsonData, testPdfPath, isSinglesFolder: false);

            // Assert restored data
            Assert.AreEqual(5, restored.PageNumberOffset);
            Assert.AreEqual(10, restored.LastPageNo);
            Assert.AreEqual("Test notes for the book", restored.Notes);
            Assert.AreEqual(1, restored.lstVolInfo.Count);
            Assert.AreEqual(20, restored.lstVolInfo[0].NPagesInThisVolume);
            Assert.AreEqual(1, restored.lstTocEntries.Count);
            Assert.AreEqual("Amazing Grace", restored.lstTocEntries[0].SongName);
            Assert.AreEqual(2, restored.dictFav.Count);
            Assert.IsTrue(restored.IsFavorite(7));
            Assert.IsTrue(restored.IsFavorite(12));

            AddLogEntry($"Successfully converted and restored complete BMK data");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TestBmkJsonConversion_SaveAndReload_PreservesData()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                await Task.Yield();

                // Arrange - Create metadata with all types of data
                var metadata = new PdfMetaData
                {
                    _FullPathFile = testPdfPath,
                    PageNumberOffset = 0,
                    Notes = "Integration test book"
                };

                metadata.lstVolInfo.Add(new PdfVolumeInfo
                {
                    FileNameVolume = Path.GetFileName(testPdfPath),
                    NPagesInThisVolume = 5,
                    Rotation = 0
                });

                metadata.lstTocEntries.Add(new TOCEntry
                {
                    SongName = "Test Song 1",
                    Composer = "Composer A",
                    PageNo = 0
                });

                metadata.lstTocEntries.Add(new TOCEntry
                {
                    SongName = "Test Song 2",
                    Composer = "Composer B",
                    PageNo = 2
                });

                metadata.ToggleFavorite(1, IsFavorite: true, FavoriteName: "Fav 1");

                // Add ink stroke
                var strokeCollection = new System.Windows.Ink.StrokeCollection();
                var points = new System.Windows.Input.StylusPointCollection
                {
                    new System.Windows.Input.StylusPoint(10, 10),
                    new System.Windows.Input.StylusPoint(100, 100)
                };
                var stroke = new System.Windows.Ink.Stroke(points);
                stroke.DrawingAttributes.Color = Colors.Blue;
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

                // Act - Save as JSON
                var bmkPath = Path.ChangeExtension(testPdfPath, ".bmk");
                BmkJsonConverter.SaveAsJson(metadata, bmkPath);

                // Assert - File exists and is JSON
                Assert.IsTrue(File.Exists(bmkPath));
                Assert.IsTrue(BmkJsonConverter.IsJsonFormat(bmkPath));

                // Act - Reload from JSON
                var reloaded = BmkJsonConverter.LoadFromJson(bmkPath, testPdfPath, isSinglesFolder: false);

                // Assert - All data preserved
                Assert.AreEqual(1, reloaded.lstVolInfo.Count);
                Assert.AreEqual(2, reloaded.lstTocEntries.Count);
                Assert.AreEqual("Test Song 1", reloaded.lstTocEntries[0].SongName);
                Assert.AreEqual("Test Song 2", reloaded.lstTocEntries[1].SongName);
                Assert.AreEqual(1, reloaded.dictFav.Count);
                Assert.IsTrue(reloaded.IsFavorite(1));
                Assert.AreEqual(1, reloaded.dictInkStrokes.Count);
                Assert.IsTrue(reloaded.dictInkStrokes.ContainsKey(0));

                // Verify ink is in JSON format
                var inkData = reloaded.dictInkStrokes[0].StrokeData;
                var firstByte = inkData[0];
                Assert.AreEqual((byte)'{', firstByte, "Ink should be in JSON format");

                AddLogEntry($"Successfully saved and reloaded complete BMK as JSON");
            });
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void TestBmkJsonConversion_IsJsonFormat_DetectsCorrectly()
        {
            // Arrange - Create XML BMK (old format)
            var xmlBmkPath = Path.Combine(testDirectory, "test_xml.bmk");
            File.WriteAllText(xmlBmkPath, "<?xml version=\"1.0\"?><PdfMetaData></PdfMetaData>");

            // Arrange - Create JSON BMK (new format)
            var jsonBmkPath = Path.Combine(testDirectory, "test_json.bmk");
            File.WriteAllText(jsonBmkPath, "{ \"Version\": 1 }");

            // Act & Assert
            Assert.IsFalse(BmkJsonConverter.IsJsonFormat(xmlBmkPath), "XML file should not be detected as JSON");
            Assert.IsTrue(BmkJsonConverter.IsJsonFormat(jsonBmkPath), "JSON file should be detected as JSON");

            AddLogEntry($"Format detection working correctly");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void TestBmkJsonConversion_ConvertAllBmks_CreatesBackups()
        {
            // Arrange - Create metadata and save as XML (old format)
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

            // Save in XML format (simulating old format)
            metadata.SaveIfDirty(ForceDirty: true);
            var bmkPath = Path.ChangeExtension(testPdfPath, ".bmk");
            var backupPath = bmkPath + ".xml.backup";

            // Act - Convert to JSON
            var metadataList = new List<PdfMetaData> { metadata };
            var (total, converted) = BmkJsonConverter.ConvertAllBmksToJson(metadataList);

            // Assert
            Assert.AreEqual(1, total);
            Assert.AreEqual(1, converted);
            Assert.IsTrue(File.Exists(backupPath), "Backup file should be created");
            Assert.IsTrue(BmkJsonConverter.IsJsonFormat(bmkPath), "Original file should now be JSON");

            AddLogEntry($"Backup created at: {backupPath}");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void TestBmkJsonConversion_MultiVolume_PreservesAllVolumes()
        {
            // Arrange
            var metadata = new PdfMetaData
            {
                _FullPathFile = testPdfPath,
                PageNumberOffset = 0
            };

            // Add 3 volumes
            for (int i = 0; i < 3; i++)
            {
                metadata.lstVolInfo.Add(new PdfVolumeInfo
                {
                    FileNameVolume = $"test{i}.pdf",
                    NPagesInThisVolume = 10 + i,
                    Rotation = i
                });
            }

            // Act
            var jsonData = BmkJsonConverter.ConvertToJson(metadata);
            var restored = BmkJsonConverter.ConvertFromJson(jsonData, testPdfPath, false);

            // Assert
            Assert.AreEqual(3, restored.lstVolInfo.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual($"test{i}.pdf", restored.lstVolInfo[i].FileNameVolume);
                Assert.AreEqual(10 + i, restored.lstVolInfo[i].NPagesInThisVolume);
                Assert.AreEqual(i, restored.lstVolInfo[i].Rotation);
            }

            AddLogEntry($"Multi-volume data preserved: {restored.lstVolInfo.Count} volumes");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void TestBmkJsonConversion_EmptyMetadata_HandlesGracefully()
        {
            // Arrange - Empty metadata
            var metadata = new PdfMetaData
            {
                _FullPathFile = testPdfPath,
                PageNumberOffset = 0
            };

            // Act
            var jsonData = BmkJsonConverter.ConvertToJson(metadata);
            var jsonText = BmkJsonConverter.SerializeToJson(jsonData);
            var restored = BmkJsonConverter.ConvertFromJson(jsonData, testPdfPath, false);

            // Assert
            Assert.AreEqual(0, restored.lstVolInfo.Count);
            Assert.AreEqual(0, restored.lstTocEntries.Count);
            Assert.AreEqual(0, restored.dictFav.Count);
            Assert.AreEqual(0, restored.dictInkStrokes.Count);

            AddLogEntry($"Empty metadata handled correctly");
        }

        [TestMethod]
        [TestCategory("Manual")]
        public async Task TestBmkJsonStats()
        {
            await RunInSTAExecutionContextAsync(async () =>
            {
                /*
    C:\Users\calvinh\OneDrive\SheetMusic\Classical\Everybodys Favorite Piano Pieces.pdf 1,2
    C:\Users\calvinh\OneDrive\SheetMusic\Classical\Piano Pieces for the Adult Student.pdf 44
    C:\Users\calvinh\OneDrive\SheetMusic\Classical\Tchaikovsky The Nutcracker Suite.pdf 2
    C:\Users\calvinh\OneDrive\SheetMusic\FakeBooks\The Best Fake Book Ever0.pdf 72,397,416
    C:\Users\calvinh\OneDrive\SheetMusic\FakeBooks\The Movie Fake Book0.pdf 363
    C:\Users\calvinh\OneDrive\SheetMusic\Pop\150 of the Most Beautiful Songs Ever 0.pdf 269
    C:\Users\calvinh\OneDrive\SheetMusic\Pop\Songs of the Sixties.pdf 16
    C:\Users\calvinh\OneDrive\SheetMusic\Pop\The American Treasury of Popular Movie Songs.pdf 17
    C:\Users\calvinh\OneDrive\SheetMusic\Ragtime\Collections\Ragtime & Early Blues Piano0.pdf 25,158
    C:\Users\calvinh\OneDrive\SheetMusic\Ragtime\Collections\Ragtime Jubilee.pdf 67
    C:\Users\calvinh\OneDrive\SheetMusic\Ragtime\Collections\The Music of James Scott.pdf 68,80,102
    C:\Users\calvinh\OneDrive\SheetMusic\Ragtime\Collections\CharlesJohnsonSingles 24,132,282,283,447
    C:\Users\calvinh\OneDrive\SheetMusic\Ragtime\Singles 21,705,1176,1545
                 */
                var folder = GetSheetMusicFolder();
                var res = await PdfMetaData.LoadAllPdfMetaDataFromDiskAsync(folder);
                var bmkswithink = res.Item1.Where(x => x.dictInkStrokes.Count > 0);
                foreach (var item in bmkswithink)
                {
                    Trace.WriteLine(item._FullPathFile + " " + string.Join(',', item.dictInkStrokes.Keys));

                }

            });
        }
    }
}
