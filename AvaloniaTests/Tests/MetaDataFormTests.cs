using Avalonia;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using SheetMusicViewer.Desktop;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Tests for MetaDataFormViewModel and MetaDataFormWindow.
/// Unit/integration tests exercise ViewModel logic directly using in-memory PdfMetaDataReadResult
/// (JSON format), with no GetAwaiter().GetResult() deadlock risk.
/// Manual tests show the Avalonia window for interactive verification.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MetaDataFormTests : TestBase
{
    private string _tempFolder;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _tempFolder = Path.Combine(Path.GetTempPath(), $"MetaDataFormTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        base.TestCleanup();
        try
        {
            if (Directory.Exists(_tempFolder))
                Directory.Delete(_tempFolder, recursive: true);
        }
        catch { }
    }

    /// <summary>
    /// Creates a PdfMetaDataReadResult with representative piano-book sample data in the
    /// JSON-backed format, equivalent to what Sample59PianoSolosFull represented in the old BMK format.
    /// </summary>
    private static PdfMetaDataReadResult CreateSampleMetaData(string pdfPath)
    {
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = false,
            PageNumberOffset = 2,
            LastPageNo = 120,
            Notes = "Sample piano solos collection"
        };

        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume = Path.GetFileName(pdfPath),
            NPagesInThisVolume = 120
        });

        metadata.TocEntries.Add(new TOCEntry { PageNo = 2,  SongName = "Fur Elise",             Composer = "Beethoven", Date = "1810" });
        metadata.TocEntries.Add(new TOCEntry { PageNo = 6,  SongName = "Moonlight Sonata Mvt 1", Composer = "Beethoven", Date = "1801" });
        metadata.TocEntries.Add(new TOCEntry { PageNo = 12, SongName = "Prelude in C",           Composer = "Bach",      Date = "1722" });
        metadata.TocEntries.Add(new TOCEntry { PageNo = 16, SongName = "Gymnopedie No. 1",       Composer = "Satie",     Date = "1888" });
        metadata.TocEntries.Add(new TOCEntry { PageNo = 20, SongName = "Clair de Lune",          Composer = "Debussy",   Date = "1905", Notes = "Suite bergamasque" });

        metadata.Favorites.Add(new Favorite { Pageno = 6,  FavoriteName = "Moonlight Sonata" });
        metadata.Favorites.Add(new Favorite { Pageno = 20, FavoriteName = "Clair de Lune" });

        return metadata;
    }

    private static void AddDataGridStyles(Application app)
    {
        var styles = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Avalonia.Controls.DataGrid/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        };
        app.Styles.Add(styles);
    }

    // ===== Unit Tests =====

    [TestMethod]
    [TestCategory("Unit")]
    public void ViewModel_WithSampleMetaData_LoadsTocEntries()
    {
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "SamplePianoSolos.pdf"));
        var metadata = CreateSampleMetaData(pdfPath);

        var viewModel = new MetaDataFormViewModel(metadata, _tempFolder);

        Assert.AreEqual(5, viewModel.TocEntries.Count, "Should load 5 TOC entries");
        Assert.AreEqual("Fur Elise",          viewModel.TocEntries[0].SongName);
        Assert.AreEqual("Beethoven",          viewModel.TocEntries[0].Composer);
        Assert.AreEqual(2,                    viewModel.TocEntries[0].PageNo);
        Assert.AreEqual("Clair de Lune",      viewModel.TocEntries[4].SongName);
        Assert.AreEqual("Suite bergamasque",  viewModel.TocEntries[4].Notes);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ViewModel_WithSampleMetaData_LoadsFavorites()
    {
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "SamplePianoSolos.pdf"));
        var metadata = CreateSampleMetaData(pdfPath);

        var viewModel = new MetaDataFormViewModel(metadata, _tempFolder);

        Assert.AreEqual(2, viewModel.Favorites.Count, "Should load 2 favorites");
        Assert.AreEqual(6,  viewModel.Favorites[0].PageNo);
        Assert.AreEqual(20, viewModel.Favorites[1].PageNo);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ViewModel_WithSampleMetaData_LoadsVolumeInfo()
    {
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "SamplePianoSolos.pdf"));
        var metadata = CreateSampleMetaData(pdfPath);

        var viewModel = new MetaDataFormViewModel(metadata, _tempFolder);

        Assert.AreEqual(1, viewModel.VolInfoDisplay.Count, "Should produce 1 volume info line");
        StringAssert.Contains(viewModel.VolInfoDisplay[0], "Vol=0");
        StringAssert.Contains(viewModel.VolInfoDisplay[0], "SamplePianoSolos.pdf");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ViewModel_WithSampleMetaData_HasCorrectPageOffsetAndNotes()
    {
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "SamplePianoSolos.pdf"));
        var metadata = CreateSampleMetaData(pdfPath);

        var viewModel = new MetaDataFormViewModel(metadata, _tempFolder);

        Assert.AreEqual(2, viewModel.PageNumberOffset);
        Assert.AreEqual("Sample piano solos collection", viewModel.DocNotes);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ViewModel_WithSampleMetaData_IsNotDirtyInitially()
    {
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "SamplePianoSolos.pdf"));
        var metadata = CreateSampleMetaData(pdfPath);

        var viewModel = new MetaDataFormViewModel(metadata, _tempFolder);

        Assert.IsFalse(viewModel.IsDirty, "ViewModel should not be dirty after initialization");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ViewModel_EmptyConstructor_HasEmptyCollections()
    {
        var viewModel = new MetaDataFormViewModel();

        Assert.AreEqual(0, viewModel.TocEntries.Count);
        Assert.AreEqual(0, viewModel.Favorites.Count);
        Assert.AreEqual(0, viewModel.VolInfoDisplay.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ViewModel_AfterEditingTocEntry_IsDirty()
    {
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "SamplePianoSolos.pdf"));
        var metadata = CreateSampleMetaData(pdfPath);
        var viewModel = new MetaDataFormViewModel(metadata, _tempFolder);

        viewModel.TocEntries[0].SongName = "Modified Song";

        Assert.IsTrue(viewModel.IsDirty, "ViewModel should be dirty after editing a TOC entry");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ViewModel_AfterEditingPageOffset_IsDirty()
    {
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "SamplePianoSolos.pdf"));
        var metadata = CreateSampleMetaData(pdfPath);
        var viewModel = new MetaDataFormViewModel(metadata, _tempFolder);

        viewModel.PageNumberOffset = 5;

        Assert.IsTrue(viewModel.IsDirty, "ViewModel should be dirty after changing PageNumberOffset");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ViewModel_TocEntriesOrderedByPageNo()
    {
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "OrderTest.pdf"));
        var metadata = new PdfMetaDataReadResult
        {
            FullPathFile = pdfPath,
            IsDirty = false
        };
        metadata.VolumeInfoList.Add(new PdfVolumeInfoBase { FileNameVolume = Path.GetFileName(pdfPath), NPagesInThisVolume = 30 });
        // Add entries deliberately out of order
        metadata.TocEntries.Add(new TOCEntry { PageNo = 15, SongName = "Second" });
        metadata.TocEntries.Add(new TOCEntry { PageNo = 5,  SongName = "First" });
        metadata.TocEntries.Add(new TOCEntry { PageNo = 25, SongName = "Third" });

        var viewModel = new MetaDataFormViewModel(metadata, _tempFolder);

        Assert.AreEqual("First",  viewModel.TocEntries[0].SongName, "TOC entries should be sorted by page number");
        Assert.AreEqual("Second", viewModel.TocEntries[1].SongName);
        Assert.AreEqual("Third",  viewModel.TocEntries[2].SongName);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ViewModel_FavoriteTocEntries_MarkedAsFavorite()
    {
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "FavTest.pdf"));
        var metadata = CreateSampleMetaData(pdfPath);

        var viewModel = new MetaDataFormViewModel(metadata, _tempFolder);

        // Moonlight Sonata (page 6) is a favorite; Fur Elise (page 2) is not.
        var moonlight = viewModel.TocEntries.FirstOrDefault(e => e.PageNo == 6);
        var furElise  = viewModel.TocEntries.FirstOrDefault(e => e.PageNo == 2);
        Assert.IsNotNull(moonlight);
        Assert.IsTrue(moonlight.IsFavorite,   "Moonlight Sonata should be marked as favorite");
        Assert.IsNotNull(furElise);
        Assert.IsFalse(furElise.IsFavorite,   "Fur Elise should not be marked as favorite");
    }

    // ===== Integration Test =====

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ViewModel_LoadedFromJson_MatchesOriginalData()
    {
        // Arrange - build metadata in memory and persist it as JSON
        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "JsonRoundTrip.pdf"));
        var original = CreateSampleMetaData(pdfPath);
        original.IsDirty = true;
        PdfMetaDataCore.SaveToJson(original, forceSave: true);

        // Act - reload asynchronously (no GetAwaiter().GetResult())
        var provider = new PdfToImageDocumentProvider();
        var loaded = await PdfMetaDataCore.ReadPdfMetaDataAsync(pdfPath, isSingles: false, provider);
        var viewModel = new MetaDataFormViewModel(loaded, _tempFolder);

        // Assert
        Assert.AreEqual(5, viewModel.TocEntries.Count, "TOC entry count should survive JSON round-trip");
        Assert.AreEqual(2, viewModel.Favorites.Count,  "Favorites count should survive JSON round-trip");
        Assert.AreEqual(2, viewModel.PageNumberOffset, "PageNumberOffset should survive JSON round-trip");
        Assert.IsFalse(viewModel.IsDirty,              "ViewModel should not be dirty after clean load");
    }

    // ===== Manual Tests (window UI) =====

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestMetaDataFormWithSampleData()
    {
        SkipIfCI("Manual test requires user interaction");

        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "SamplePianoSolos.pdf"));
        var metadata = CreateSampleMetaData(pdfPath);

        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var viewModel = new MetaDataFormViewModel(metadata, _tempFolder);
            var window = new MetaDataFormWindow(viewModel);

            lifetime.MainWindow = window;

            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "MetaDataFormWindow closed - TEST PASSED");

            window.Show();

            Trace.WriteLine("=== MetaDataForm Test (JSON format, no deadlock) ===");
            Trace.WriteLine($"TOC Entries: {viewModel.TocEntries.Count}");
            Trace.WriteLine($"Favorites:   {viewModel.Favorites.Count}");
            Trace.WriteLine($"Volumes:     {viewModel.VolInfoDisplay.Count}");
            Trace.WriteLine("Close the window when finished testing.");

            await Task.Delay(100);
        }, configureApp: AddDataGridStyles);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestMetaDataFormWithEmptyData()
    {
        SkipIfCI("Manual test requires user interaction");

        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var viewModel = new MetaDataFormViewModel();
            var window = new MetaDataFormWindow(viewModel);

            lifetime.MainWindow = window;

            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "Empty MetaDataForm closed - TEST PASSED");

            window.Show();

            Trace.WriteLine("=== MetaDataForm Empty Data Test ===");
            Trace.WriteLine("Add TOC entries with 'Add Row', then close.");

            await Task.Delay(100);
        }, configureApp: AddDataGridStyles);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestMetaDataFormEditing()
    {
        SkipIfCI("Manual test requires user interaction");

        var pdfPath = CreateTestPdf(Path.Combine(_tempFolder, "EditTest.pdf"));
        var metadata = CreateSampleMetaData(pdfPath);

        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var viewModel = new MetaDataFormViewModel(metadata, _tempFolder);
            var window = new MetaDataFormWindow(viewModel);

            lifetime.MainWindow = window;

            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "Editing test completed - TEST PASSED");

            window.Show();

            Trace.WriteLine("=== MetaDataForm Editing Test ===");
            Trace.WriteLine("1. Select a row and edit fields in the detail panel.");
            Trace.WriteLine("2. Verify two-way binding: panel edits update the grid and vice versa.");
            Trace.WriteLine("3. Add a new row, edit it, then delete a row.");
            Trace.WriteLine("4. Click Save or Cancel, then close.");

            await Task.Delay(100);
        }, configureApp: AddDataGridStyles);
    }
}
