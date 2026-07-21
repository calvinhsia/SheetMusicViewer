using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Tests;

/// <summary>
/// Manual tests that run Windows.Media.Ocr on a PDF and print the results so they
/// can be compared against the Tesseract output from
/// <c>AvaloniaTests.Tests.PdfOcrTocManualTests</c>.
///
/// Nothing is written to disk; all output goes to Trace / the test-output window.
///
/// Run with:
///   dotnet test --filter "TestCategory=Manual&amp;ClassName=Tests.PdfWindowsOcrManualTests"
/// </summary>
[TestClass]
[TestCategory("Manual")]
public class PdfWindowsOcrManualTests : TestBase
{
    // -----------------------------------------------------------------------
    //  Ad-hoc entry point â€” edit pdfPath and rotation then run
    // -----------------------------------------------------------------------

    /// <summary>
    /// Ad-hoc test: edit the path and rotation variables in this method to point at
    /// any scanned PDF, then run this test.
    /// rotation: 0 = normal, 1 = 90Â° CW, 2 = 180Â° (upside-down), 3 = 270Â° CW.
    /// </summary>
    [TestMethod]
    public async Task ExtractTocFromAdhocPdf_WindowsOcr()
    {
        // â”€â”€ Edit these two lines before running â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var pdfPath  = Path.Combine(GetSheetMusicFolder(), @"Classical\Chopin Complete Waltzes.pdf");
        var rotation = 2;   // 0=normal  1=90CW  2=180  3=270CW
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        await RunWindowsOcrAsync(pdfPath, rotation);
    }

    // -----------------------------------------------------------------------
    //  Named / repeatable tests for specific books
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ExtractToc_ChopinCompleteWaltzes_WindowsOcr()
    {
        var pdf = Path.Combine(GetSheetMusicFolder(), @"Classical\Chopin Complete Waltzes.pdf");
        await RunWindowsOcrAsync(pdf, rotation: 2);
    }

    // -----------------------------------------------------------------------
    //  Shared extraction logic
    // -----------------------------------------------------------------------

    private static async Task RunWindowsOcrAsync(string pdfPath, int rotation)
    {
        if (!File.Exists(pdfPath))
            Assert.Inconclusive($"PDF not found: {pdfPath}");

        Trace.WriteLine($"=== Windows OCR: {Path.GetFileName(pdfPath)}  rotation={rotation} ===");
        Trace.WriteLine(string.Empty);

        var sw = Stopwatch.StartNew();
        int lastPage = -1;

        List<PdfPageWinOcrResult> results;
        try
        {
            results = await PdfWindowsOcrService.ExtractAsync(
                pdfPath, rotation,
                progress: new Progress<(int Page, int Total)>(t =>
                {
                    if (t.Page != lastPage)
                    {
                        lastPage = t.Page;
                        Trace.WriteLine($"  [WindowsOCR] page {t.Page + 1} / {t.Total}â€¦");
                    }
                }));
        }
        catch (Exception ex)
        {
            Assert.Fail($"Windows OCR failed: {ex.Message}");
            return;
        }

        sw.Stop();
        Trace.WriteLine($"Extraction complete in {sw.Elapsed.TotalSeconds:F1}s  ({results.Count} pages)");
        Trace.WriteLine(string.Empty);

        // â”€â”€ raw text â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        Trace.WriteLine(PdfWindowsOcrService.FormatRawText(results));
        Trace.WriteLine(string.Empty);

        // â”€â”€ suggested JSON TOC â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        Trace.WriteLine(PdfWindowsOcrService.FormatSuggestedJson(results));

        Assert.IsTrue(results.Count > 0, "Expected at least one page in the PDF.");
    }
}
