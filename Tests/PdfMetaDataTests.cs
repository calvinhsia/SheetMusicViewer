using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer;
using Windows.Data.Pdf;
using Windows.Storage;

namespace Tests
{
    [TestClass]
    public class PdfMetaDataTests : TestBase
    {
        private string testDirectory;

        [TestInitialize]
        public void Setup()
        {
            testDirectory = Path.Combine(Path.GetTempPath(), $"PdfMetaDataTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(testDirectory);
            AddLogEntry($"Test directory created: {testDirectory}");
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

        #region Basic Property Tests

        [TestMethod]
        public void TestPdfMetaData_Constructor_InitializesCollections()
        {
            var metadata = new PdfMetaData();

            Assert.IsNotNull(metadata.lstVolInfo);
            Assert.IsNotNull(metadata.LstInkStrokes);
            Assert.IsNotNull(metadata.Favorites);
            Assert.IsNotNull(metadata.lstTocEntries);
            Assert.IsNotNull(metadata.dictToc);
            Assert.IsNotNull(metadata.dictFav);
            Assert.IsNotNull(metadata.dictInkStrokes);
        }

        [TestMethod]
        public void TestPdfMetaData_NumPagesInSet_CalculatesCorrectly()
        {
            var metadata = new PdfMetaData();
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 10 });
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 15 });
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 20 });

            Assert.AreEqual(45, metadata.NumPagesInSet);
        }

        [TestMethod]
        public void TestPdfMetaData_MaxPageNum_CalculatesWithOffset()
        {
            var metadata = new PdfMetaData
            {
                PageNumberOffset = -5
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 100 });

            Assert.AreEqual(95, metadata.MaxPageNum);
        }

        [TestMethod]
        public void TestPdfMetaData_ToString_FormatsCorrectly()
        {
            var metadata = new PdfMetaData
            {
                _FullPathFile = @"C:\Music\TestBook.pdf"
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 50 });
            metadata.lstTocEntries.Add(new TOCEntry { SongName = "Song1", PageNo = 1 });
            metadata.dictFav[5] = new Favorite { Pageno = 5 };

            var result = metadata.ToString();

            StringAssert.Contains(result, "TestBook");
            StringAssert.Contains(result, "Vol=1");
            StringAssert.Contains(result, "Toc=1");
            StringAssert.Contains(result, "Fav=1");
        }

        #endregion

        #region Volume Management Tests

        [TestMethod]
        public void TestPdfMetaData_GetVolNumFromPageNum_SingleVolume()
        {
            var metadata = new PdfMetaData
            {
                PageNumberOffset = 0
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 100 });

            Assert.AreEqual(0, metadata.GetVolNumFromPageNum(0));
            Assert.AreEqual(0, metadata.GetVolNumFromPageNum(50));
            Assert.AreEqual(0, metadata.GetVolNumFromPageNum(99));
        }

        [TestMethod]
        public void TestPdfMetaData_GetVolNumFromPageNum_MultiVolume()
        {
            var metadata = new PdfMetaData
            {
                PageNumberOffset = 0
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 50 });
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 30 });
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 20 });

            Assert.AreEqual(0, metadata.GetVolNumFromPageNum(0));
            Assert.AreEqual(0, metadata.GetVolNumFromPageNum(49));
            Assert.AreEqual(1, metadata.GetVolNumFromPageNum(50));
            Assert.AreEqual(1, metadata.GetVolNumFromPageNum(79));
            Assert.AreEqual(2, metadata.GetVolNumFromPageNum(80));
            Assert.AreEqual(2, metadata.GetVolNumFromPageNum(99));
        }

        [TestMethod]
        public void TestPdfMetaData_GetVolNumFromPageNum_WithNegativeOffset()
        {
            var metadata = new PdfMetaData
            {
                PageNumberOffset = -10
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 50 });
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 30 });

            Assert.AreEqual(0, metadata.GetVolNumFromPageNum(-10));
            Assert.AreEqual(0, metadata.GetVolNumFromPageNum(0));
            Assert.AreEqual(0, metadata.GetVolNumFromPageNum(39));
            Assert.AreEqual(1, metadata.GetVolNumFromPageNum(40));
            Assert.AreEqual(1, metadata.GetVolNumFromPageNum(69));
        }

        [TestMethod]
        public void TestPdfMetaData_GetPagenoOfVolume_CalculatesCorrectly()
        {
            var metadata = new PdfMetaData
            {
                PageNumberOffset = 5
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 100 });
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 50 });
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 75 });

            Assert.AreEqual(5, metadata.GetPagenoOfVolume(0));
            Assert.AreEqual(105, metadata.GetPagenoOfVolume(1));
            Assert.AreEqual(155, metadata.GetPagenoOfVolume(2));
        }

        #endregion

        #region Favorite Management Tests

        [TestMethod]
        public void TestPdfMetaData_ToggleFavorite_AddsFavorite()
        {
            var metadata = new PdfMetaData();

            metadata.ToggleFavorite(10, IsFavorite: true);

            Assert.IsTrue(metadata.IsFavorite(10));
            Assert.IsTrue(metadata.dictFav.ContainsKey(10));
            Assert.IsTrue(metadata.IsDirty);
        }

        [TestMethod]
        public void TestPdfMetaData_ToggleFavorite_RemovesFavorite()
        {
            var metadata = new PdfMetaData();
            metadata.ToggleFavorite(10, IsFavorite: true);
            metadata.IsDirty = false;

            metadata.ToggleFavorite(10, IsFavorite: false);

            Assert.IsFalse(metadata.IsFavorite(10));
            Assert.IsFalse(metadata.dictFav.ContainsKey(10));
            Assert.IsTrue(metadata.IsDirty);
        }

        [TestMethod]
        public void TestPdfMetaData_ToggleFavorite_WithCustomName()
        {
            var metadata = new PdfMetaData();
            var customName = "My Favorite Page";

            metadata.ToggleFavorite(15, IsFavorite: true, FavoriteName: customName);

            Assert.IsTrue(metadata.IsFavorite(15));
            Assert.AreEqual(customName, metadata.dictFav[15].FavoriteName);
        }

        [TestMethod]
        public void TestPdfMetaData_IsFavorite_ReturnsFalseForNonExistent()
        {
            var metadata = new PdfMetaData();

            Assert.IsFalse(metadata.IsFavorite(99));
        }

        [TestMethod]
        public void TestPdfMetaData_InitializeFavList_PopulatesDictionary()
        {
            var metadata = new PdfMetaData();
            metadata.Favorites.Add(new Favorite { Pageno = 5, FavoriteName = "Page 5" });
            metadata.Favorites.Add(new Favorite { Pageno = 10, FavoriteName = "Page 10" });
            metadata.Favorites.Add(new Favorite { Pageno = 15, FavoriteName = "Page 15" });

            metadata.InitializeFavList();

            Assert.AreEqual(3, metadata.dictFav.Count);
            Assert.IsTrue(metadata.dictFav.ContainsKey(5));
            Assert.IsTrue(metadata.dictFav.ContainsKey(10));
            Assert.IsTrue(metadata.dictFav.ContainsKey(15));
            Assert.AreEqual(0, metadata.Favorites.Count); // Should be cleared
        }

        #endregion

        #region TOC Management Tests

        [TestMethod]
        public void TestPdfMetaData_InitializeDictToc_PopulatesDictionary()
        {
            var metadata = new PdfMetaData();
            var tocEntries = new List<TOCEntry>
            {
                new TOCEntry { SongName = "Song 1", PageNo = 1, Composer = "Composer A" },
                new TOCEntry { SongName = "Song 2", PageNo = 5, Composer = "Composer B" },
                new TOCEntry { SongName = "Song 3", PageNo = 10, Composer = "Composer C" }
            };

            metadata.InitializeDictToc(tocEntries);

            Assert.AreEqual(3, metadata.dictToc.Count);
            Assert.IsTrue(metadata.dictToc.ContainsKey(1));
            Assert.IsTrue(metadata.dictToc.ContainsKey(5));
            Assert.IsTrue(metadata.dictToc.ContainsKey(10));
        }

        [TestMethod]
        public void TestPdfMetaData_InitializeDictToc_HandlesMultipleSongsPerPage()
        {
            var metadata = new PdfMetaData();
            var tocEntries = new List<TOCEntry>
            {
                new TOCEntry { SongName = "Song 1", PageNo = 1 },
                new TOCEntry { SongName = "Song 2", PageNo = 1 },
                new TOCEntry { SongName = "Song 3", PageNo = 5 }
            };

            metadata.InitializeDictToc(tocEntries);

            Assert.AreEqual(2, metadata.dictToc.Count);
            Assert.AreEqual(2, metadata.dictToc[1].Count);
            Assert.AreEqual(1, metadata.dictToc[5].Count);
        }

        [TestMethod]
        public void TestPdfMetaData_InitializeDictToc_RemovesQuotes()
        {
            var metadata = new PdfMetaData();
            var tocEntries = new List<TOCEntry>
            {
                new TOCEntry { SongName = "\"Quoted Song\"", PageNo = 1, Composer = "\"Quoted Composer\"", Date = "\"1920\"" }
            };

            metadata.InitializeDictToc(tocEntries);

            var toc = metadata.dictToc[1][0];
            Assert.AreEqual("Quoted Song", toc.SongName);
            Assert.AreEqual("Quoted Composer", toc.Composer);
            Assert.AreEqual("1920", toc.Date);
        }

        [TestMethod]
        public void TestPdfMetaData_GetDescription_ReturnsExactMatch()
        {
            var metadata = new PdfMetaData
            {
                _FullPathFile = "TestBook.pdf"
            };
            var tocEntries = new List<TOCEntry>
            {
                new TOCEntry { SongName = "Amazing Song", PageNo = 5, Composer = "Great Composer", Date = "1925" }
            };
            metadata.InitializeDictToc(tocEntries);

            var description = metadata.GetDescription(5);

            StringAssert.Contains(description, "Amazing Song");
            StringAssert.Contains(description, "Great Composer");
            StringAssert.Contains(description, "1925");
        }

        [TestMethod]
        public void TestPdfMetaData_GetDescription_FindsNearestPreviousEntry()
        {
            var metadata = new PdfMetaData
            {
                _FullPathFile = "TestBook.pdf"
            };
            var tocEntries = new List<TOCEntry>
            {
                new TOCEntry { SongName = "Song 1", PageNo = 1 },
                new TOCEntry { SongName = "Song 2", PageNo = 10 },
                new TOCEntry { SongName = "Song 3", PageNo = 20 }
            };
            metadata.InitializeDictToc(tocEntries);

            var description = metadata.GetDescription(15);

            StringAssert.Contains(description, "Song 2");
        }

        [TestMethod]
        public void TestPdfMetaData_GetDescription_HandlesMultipleSongsOnPage()
        {
            var metadata = new PdfMetaData
            {
                _FullPathFile = "TestBook.pdf"
            };
            var tocEntries = new List<TOCEntry>
            {
                new TOCEntry { SongName = "Song A", PageNo = 5, Composer = "Composer A" },
                new TOCEntry { SongName = "Song B", PageNo = 5, Composer = "Composer B" }
            };
            metadata.InitializeDictToc(tocEntries);

            var description = metadata.GetDescription(5);

            StringAssert.Contains(description, "Song A");
            StringAssert.Contains(description, "Song B");
            StringAssert.Contains(description, "|"); // Should contain separator
        }

        [TestMethod]
        public void TestPdfMetaData_GetSongCount_ReturnsCorrectCount()
        {
            var metadata = new PdfMetaData();
            metadata.lstTocEntries.Add(new TOCEntry { SongName = "Song 1" });
            metadata.lstTocEntries.Add(new TOCEntry { SongName = "Song 2" });
            metadata.lstTocEntries.Add(new TOCEntry { SongName = "Song 3" });

            Assert.AreEqual(3, metadata.GetSongCount());
        }

        #endregion

        #region Rotation Tests

        [TestMethod]
        public void TestPdfMetaData_GetRotation_ReturnsVolumeRotation()
        {
            var metadata = new PdfMetaData
            {
                PageNumberOffset = 0
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 50, Rotation = 1 });
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 50, Rotation = 2 });

            Assert.AreEqual(Rotation.Rotate90, metadata.GetRotation(25));
            Assert.AreEqual(Rotation.Rotate180, metadata.GetRotation(75));
        }

        [TestMethod]
        public void TestPdfMetaData_Rotate_CyclesThroughRotations()
        {
            var metadata = new PdfMetaData
            {
                PageNumberOffset = 0
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 100, Rotation = 0 });

            metadata.Rotate(50); // 0 -> 1
            Assert.AreEqual(1, metadata.lstVolInfo[0].Rotation);

            metadata.Rotate(50); // 1 -> 2
            Assert.AreEqual(2, metadata.lstVolInfo[0].Rotation);

            metadata.Rotate(50); // 2 -> 3
            Assert.AreEqual(3, metadata.lstVolInfo[0].Rotation);

            metadata.Rotate(50); // 3 -> 0
            Assert.AreEqual(0, metadata.lstVolInfo[0].Rotation);
        }

        [TestMethod]
        public void TestPdfMetaData_Rotate_SetsDirty()
        {
            var metadata = new PdfMetaData
            {
                PageNumberOffset = 0,
                IsDirty = false
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 100, Rotation = 0 });

            metadata.Rotate(50);

            Assert.IsTrue(metadata.IsDirty);
        }

        #endregion

        #region Ink Strokes Tests

        [TestMethod]
        public void TestPdfMetaData_InitializeInkStrokes_PopulatesDictionary()
        {
            var metadata = new PdfMetaData();
            metadata.LstInkStrokes.Add(new InkStrokeClass { Pageno = 5, StrokeData = new byte[] { 1, 2, 3 } });
            metadata.LstInkStrokes.Add(new InkStrokeClass { Pageno = 10, StrokeData = new byte[] { 4, 5, 6 } });

            metadata.InitializeInkStrokes();

            Assert.AreEqual(2, metadata.dictInkStrokes.Count);
            Assert.IsTrue(metadata.dictInkStrokes.ContainsKey(5));
            Assert.IsTrue(metadata.dictInkStrokes.ContainsKey(10));
            Assert.AreEqual(0, metadata.LstInkStrokes.Count); // Should be cleared
        }

        #endregion

        #region Comparers Tests

        [TestMethod]
        public void TestPdfVolumeInfoComparer_ComparesAlphabetically()
        {
            var comparer = new PdfVolumeInfoComparer();
            var vol1 = new PdfVolumeInfo { FileNameVolume = "Book_A.pdf" };
            var vol2 = new PdfVolumeInfo { FileNameVolume = "Book_B.pdf" };

            Assert.IsTrue(comparer.Compare(vol1, vol2) < 0);
            Assert.IsTrue(comparer.Compare(vol2, vol1) > 0);
            Assert.AreEqual(0, comparer.Compare(vol1, vol1));
        }

        [TestMethod]
        public void TestTocEntryComparer_ComparesAlphabetically()
        {
            var comparer = new TocEntryComparer();
            var toc1 = new TOCEntry { SongName = "Amazing Grace" };
            var toc2 = new TOCEntry { SongName = "Blessed Assurance" };

            Assert.IsTrue(comparer.Compare(toc1, toc2) < 0);
            Assert.IsTrue(comparer.Compare(toc2, toc1) > 0);
            Assert.AreEqual(0, comparer.Compare(toc1, toc1));
        }

        [TestMethod]
        public void TestPageNoBaseClassComparer_ComparesByPageNumber()
        {
            var comparer = new PageNoBaseClassComparer();
            var page1 = new Favorite { Pageno = 5 };
            var page2 = new Favorite { Pageno = 10 };

            Assert.IsTrue(comparer.Compare(page1, page2) < 0);
            Assert.IsTrue(comparer.Compare(page2, page1) > 0);
            Assert.AreEqual(0, comparer.Compare(page1, page1));
        }

        #endregion

        #region Helper Class Tests

        [TestMethod]
        public void TestFavorite_ToString_FormatsCorrectly()
        {
            var favorite = new Favorite
            {
                Pageno = 42,
                FavoriteName = "My Favorite"
            };

            var result = favorite.ToString();

            Assert.AreEqual("My Favorite 42", result);
        }

        [TestMethod]
        public void TestTOCEntry_Clone_CreatesDeepCopy()
        {
            var original = new TOCEntry
            {
                SongName = "Original Song",
                Composer = "Original Composer",
                Notes = "Original Notes",
                Date = "1920",
                PageNo = 10
            };

            var clone = (TOCEntry)original.Clone();

            Assert.AreNotSame(original, clone);
            Assert.AreEqual(original.SongName, clone.SongName);
            Assert.AreEqual(original.Composer, clone.Composer);
            Assert.AreEqual(original.Notes, clone.Notes);
            Assert.AreEqual(original.Date, clone.Date);
            Assert.AreEqual(original.PageNo, clone.PageNo);

            // Verify changes to clone don't affect original
            clone.SongName = "Modified Song";
            Assert.AreEqual("Original Song", original.SongName);
        }

        [TestMethod]
        public void TestTOCEntry_ToString_FormatsCorrectly()
        {
            var toc = new TOCEntry
            {
                PageNo = 15,
                SongName = "Amazing Grace",
                Composer = "John Newton",
                Date = "1779",
                Notes = "Classic hymn"
            };

            var result = toc.ToString();

            Assert.AreEqual("15 Amazing Grace John Newton 1779 Classic hymn", result);
        }

        [TestMethod]
        public void TestPdfVolumeInfo_ToString_FormatsCorrectly()
        {
            var volInfo = new PdfVolumeInfo
            {
                FileNameVolume = "Book1.pdf",
                NPagesInThisVolume = 150,
                Rotation = 1
            };

            var result = volInfo.ToString();

            StringAssert.Contains(result, "Book1.pdf");
            StringAssert.Contains(result, "150");
            StringAssert.Contains(result, "Rotate90");
        }

        #endregion

        #region Page Calculation Edge Cases

        [TestMethod]
        public void TestPdfMetaData_GetTotalPageCount_ReturnsCorrectSum()
        {
            var metadata = new PdfMetaData();
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 100 });
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 50 });
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 75 });

            Assert.AreEqual(225, metadata.GetTotalPageCount());
        }

        [TestMethod]
        public void TestPdfMetaData_GetTotalPageCount_EmptyVolumes()
        {
            var metadata = new PdfMetaData();

            Assert.AreEqual(0, metadata.GetTotalPageCount());
        }

        [TestMethod]
        public void TestPdfMetaData_MaxPageNum_WithLargeNegativeOffset()
        {
            var metadata = new PdfMetaData
            {
                PageNumberOffset = -100
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 150 });

            Assert.AreEqual(50, metadata.MaxPageNum);
        }

        #endregion

        #region Dirty Flag Tests

        [TestMethod]
        public void TestPdfMetaData_IsDirty_InitiallyFalse()
        {
            var metadata = new PdfMetaData();

            Assert.IsFalse(metadata.IsDirty);
        }

        [TestMethod]
        public void TestPdfMetaData_ToggleFavorite_SetsDirtyFlag()
        {
            var metadata = new PdfMetaData
            {
                IsDirty = false
            };

            metadata.ToggleFavorite(5, IsFavorite: true);

            Assert.IsTrue(metadata.IsDirty);
        }

        [TestMethod]
        public void TestPdfMetaData_Rotate_SetsDirtyFlag()
        {
            var metadata = new PdfMetaData
            {
                PageNumberOffset = 0,
                IsDirty = false
            };
            metadata.lstVolInfo.Add(new PdfVolumeInfo { NPagesInThisVolume = 100 });

            metadata.Rotate(50);

            Assert.IsTrue(metadata.IsDirty);
        }

        #endregion
    }
}
