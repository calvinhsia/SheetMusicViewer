using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer.Desktop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Manual tests that render each page of a PDF, run Windows OCR on the top strip of
/// each page, and output:
///   • raw OCR text per page (useful for spotting title pages)
///   • a heuristic-generated TOC JSON array to paste into the JSON sidecar
///
/// These tests intentionally do NOT modify any files on disk; all output goes to
/// Trace / the test output window so the user can inspect and copy.
///
/// Run with:
///   dotnet test --filter "TestCategory=Manual&amp;ClassName=PdfOcrTocManualTests"
/// </summary>
[TestClass]
[TestCategory("Manual")]
public class PdfOcrTocManualTests : TestBase
{
    // ---------------------------------------------------------------
    //  Ad-hoc entry point — edit _pdfPath and _rotation then run
    // ---------------------------------------------------------------

    /// <summary>
    /// Ad-hoc test: set <see cref="_pdfPath"/> and <see cref="_rotation"/> to point at
    /// any scanned PDF, then run this test.  Output appears in the test output window.
    /// rotation: 0 = normal, 1 = 90° CW, 2 = 180° (upside-down), 3 = 270° CW.
    /// </summary>
    [TestMethod]
    public async Task ExtractTocFromAdhocPdf()
    {
        // ── Edit these two lines before running ──────────────────────
        var pdfPath  = @"C:\Users\Calvi\OneDrive\SheetMusic\Classical\Chopin Complete Waltzes.pdf";
        var rotation = 2;   // 0=normal 1=90CW 2=180 3=270CW
        // ─────────────────────────────────────────────────────────────

        await RunExtractionAsync(pdfPath, rotation);
    }

    // ---------------------------------------------------------------
    //  Named / repeatable tests for specific books
    // ---------------------------------------------------------------

    [TestMethod]
    public async Task ExtractToc_ChopinCompleteWaltzes()
    {
        const string pdf = @"C:\Users\Calvi\OneDrive\SheetMusic\Classical\Chopin Complete Waltzes.pdf";
        await RunExtractionAsync(pdf, rotation: 2);
    }

    // ---------------------------------------------------------------
    //  Shared extraction logic
    // ---------------------------------------------------------------

    /// <summary>
    /// Renders each page of <paramref name="pdfPath"/>, OCRs the top strip, and writes
    /// the raw text and a suggested TOC JSON to Trace output.
    /// </summary>
    private static async Task RunExtractionAsync(string pdfPath, int rotation)
    {
        if (!File.Exists(pdfPath))
            Assert.Inconclusive($"PDF not found: {pdfPath}");

        Trace.WriteLine($"=== PdfOcrToc: {Path.GetFileName(pdfPath)}  rotation={rotation} ===");
        Trace.WriteLine(string.Empty);

        var sw = Stopwatch.StartNew();

        int lastReported = -1;
        var progressHandler = new Progress<(int Page, int Total)>(t =>
        {
            if (t.Page != lastReported)
            {
                lastReported = t.Page;
                Trace.WriteLine($"  [OCR] processing page {t.Page + 1} / {t.Total}…");
            }
        });

        List<PdfPageOcrResult> results =
            await PdfOcrService.ExtractAsync(pdfPath, rotation, progressHandler);

        sw.Stop();
        Trace.WriteLine($"Extraction complete in {sw.Elapsed.TotalSeconds:F1}s  ({results.Count} pages)");
        Trace.WriteLine(string.Empty);

        // ── raw text ──────────────────────────────────────────────────
        var rawText = PdfOcrService.FormatRawText(results);
        Trace.WriteLine(rawText);
        Trace.WriteLine(string.Empty);

        // ── suggested JSON TOC ────────────────────────────────────────
        var json = PdfOcrService.FormatSuggestedJson(results);
        Trace.WriteLine(json);

        // Test passes if extraction ran without throwing.
        Assert.IsTrue(results.Count > 0, "Expected at least one page in the PDF.");
    }
}
