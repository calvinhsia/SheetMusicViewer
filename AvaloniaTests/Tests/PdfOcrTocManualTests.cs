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

    [TestMethod]
    public async Task ExtractToc_ChopinCompleteWaltzes()
    {
        const string pdf = @"C:\Users\Calvi\OneDrive\SheetMusic\Classical\Chopin Complete Waltzes.pdf";
        await RunExtractionAsync(pdf, defaultRotation: 2);
    }

    [TestMethod]
    public async Task ExtractToc_MultiVolumePdf()
    {
        const string pdf = @"C:\Users\Calvi\OneDrive\SheetMusic\FakeBooks\The Ultimate Pop Rock Fake Book.json";
        await RunExtractionAsync(pdf);
    }

    // ---------------------------------------------------------------
    //  Shared extraction logic
    // ---------------------------------------------------------------

    /// <summary>
    /// Renders each page of <paramref name="pathOrJson"/>, OCRs it, and writes
    /// the raw text and a suggested TOC JSON to Trace output.
    /// Pass a .json sidecar (or a PDF with a sibling .json) for multi-volume sets.
    /// </summary>
    private async Task RunExtractionAsync(string pathOrJson, int defaultRotation = 0)
    {
        if (!File.Exists(pathOrJson))
            Assert.Inconclusive($"File not found: {pathOrJson}");

        Trace.WriteLine($"=== PdfOcrToc: {Path.GetFileName(pathOrJson)} ===");
        Trace.WriteLine(string.Empty);

        var sw = Stopwatch.StartNew();

        int lastReported = -1;
        var progressHandler = new Progress<(int Page, int Total)>(t =>
        {
            if (t.Page != lastReported)
            {
                lastReported = t.Page;
                TestContext.WriteLine($"  [OCR] processing page {t.Page + 1} / {t.Total}…");
            }
        });

        List<PdfPageOcrResult> results =
            await PdfOcrService.ExtractAsync(pathOrJson, defaultRotation, progressHandler,
                logger: Console.WriteLine);

        sw.Stop();
        Trace.WriteLine($"Extraction complete in {sw.Elapsed.TotalSeconds:F1}s  ({results.Count} pages)");
        Trace.WriteLine(string.Empty);

        // ── suggested JSON TOC (before raw text for visibility) ───────
        var json = PdfOcrService.FormatSuggestedJson(results, logger: Console.WriteLine);
        Trace.WriteLine(json);
        Trace.WriteLine(string.Empty);

        // ── raw text ──────────────────────────────────────────────────
        var rawText = PdfOcrService.FormatRawText(results);
        Trace.WriteLine(rawText);

        // Test passes if extraction ran without throwing.
        Assert.IsTrue(results.Count > 0, "Expected at least one page in the PDF.");
    }
}
