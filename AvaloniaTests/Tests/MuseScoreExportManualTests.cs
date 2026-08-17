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
/// Manual end-to-end tests for the MuseScore export pipeline.
/// Run manually: dotnet test --filter "TestCategory=Manual"
/// </summary>
[TestClass]
[TestCategory("Manual")]
public class MuseScoreExportManualTests : TestBase
{
    // -- Test entry points ----------------------------------------------------

    /// <summary>Single-volume PDF: PatriciaRag (one file, multiple pages).</summary>
    [TestMethod]
    public async Task ExportSingleVolume_PatriciaRag()
    {
        var pdf = Path.Combine(GetSheetMusicFolder(), @"Ragtime\Collections\PatriciaRag.pdf");
        await RunExportPipelineAsync(pdf, bookStart: 0, bookEnd: 0);
    }

    /// <summary>Multi-volume PDF: Alley Cat (base file + numbered siblings, combined via Ghostscript).</summary>
    [TestMethod]
    public async Task ExportMultiVolume_AlleyCat()
    {
        var pdf = Path.Combine(GetSheetMusicFolder(), @"Pop\Frank Bjorn Alley Cat.pdf");
        await RunExportPipelineAsync(pdf, bookStart: 0, bookEnd: 0);
    }

    /// <summary>
    /// Ad-hoc test: edit <see cref="_adhocPdf"/>, <see cref="_adhocBookStart"/>, and
    /// <see cref="_adhocBookEnd"/> then run this test to try any PDF + page range through
    /// the export pipeline without modifying the other tests.
    /// bookStart/bookEnd are 1-based physical page numbers inside the PDF (after applying
    /// the PageNumberOffset stored in the JSON sidecar).  Set both to 0 to export all pages.
    /// </summary>
    // ---------------------------------------------------------------------

    [TestMethod]
    public async Task ExportAdhoc_PdfAndPageRange()
    {
        // -- Edit these three constants before running -------------------------
        var adhocBookStart = 0;
        var adhocBookEnd = 0;
        var rotation = 0;
        var spinePaddingPx = 0;   // px of white padding on spine/gutter edge (0 = off)
        var adhocPdf =
             Path.Combine(GetSheetMusicFolder(), @"Ragtime\Collections\Scott Joplin Complete Piano Works3.pdf"); adhocBookStart = 34; adhocBookEnd = 35; rotation = 2; spinePaddingPx = 0;// Something Doing
        //Path.Combine(GetSheetMusicFolder(), @"Pop\PopSingles\Quarantine Rag - Bb Major - MN0212813.pdf");
        await RunExportPipelineAsync(adhocPdf, adhocBookStart, adhocBookEnd, rotation, spinePaddingPx);
    }

    /// <summary>
    /// Multi-volume, rotated PDF: "Something Doing" from Scott Joplin Complete Piano Works.
    /// Metadata (volumes, page counts, rotation=2) is loaded from the JSON sidecar on disk.
    /// "Something Doing" is at TOC pageNo=251; with pageNumberOffset=-45:
    ///   bookStart = 251 - (-45) + 1 = 297
    ///   bookEnd   = 255 - (-45) + 1 = 301   (user-specified display pp. 251-255)
    /// </summary>
    [TestMethod]
    public async Task ExportMultiVolume_ScottJoplin_SomethingDoing()
    {
        var pdf = Path.Combine(GetSheetMusicFolder(), @"Ragtime\Collections\Scott Joplin Complete Piano Works1.pdf");
        var spinePaddingPx = 60;   // px of white padding on spine/gutter edge (0 = off)
        // with 0, PDF file size is 527k. With 60, it's 3k: 19:36:39.362   [progress] Input to Audiveris: Scott Joplin Complete Piano Works3_p31-35_gs.pdf  page size = 596.0 × 842.0 pts  (8.28 × 11.69 in)

        await RunExportPipelineAsync(pdf, bookStart: 297, bookEnd: 301, rotation: 2, spinePaddingPx: spinePaddingPx);
    }

    // -- Shared pipeline ------------------------------------------------------

    /// <summary>
    /// Loads metadata for <paramref name="inputPdf"/> from its JSON sidecar on disk (volumes,
    /// page counts, rotations, PageNumberOffset) and runs the full Audiveris pipeline.
    /// Falls back to auto-discovery using the same filename rule as
    /// <c>LoadAllPdfMetaDataParallelAsync</c> when no JSON/BMK exists.
    /// </summary>
    private async Task RunExportPipelineAsync(string inputPdf, int bookStart, int bookEnd, int rotation = -1, int spinePaddingPx = 0)
    {
        // rotation: -1 = use value from JSON sidecar (default); 0=normal, 1=90°CW, 2=180°, 3=270°CW
        // Prerequisites
        if (!File.Exists(inputPdf))
            Assert.Inconclusive($"Source PDF not found: {inputPdf}");

        var audiverisPath = MuseScoreExportService.AutoDetectAudiveris();
        if (audiverisPath is null)
            Assert.Inconclusive(
                $"Audiveris executable not found. Checked paths:\n  " +
                string.Join("\n  ", MuseScoreExportService.AudiverisDefaultPaths));

        var museScorePath = MuseScoreExportService.AutoDetectMuseScore();

        LogMessage($"=== MuseScore Export Manual Test: {Path.GetFileNameWithoutExtension(inputPdf)} ===");
        LogMessage($"Source PDF     : {inputPdf}");
        LogMessage($"Audiveris      : {audiverisPath}");
        LogMessage($"MuseScore      : {museScorePath ?? "(not found - launch skipped)"}");

        // Load metadata from the JSON sidecar (exactly as the production loader does).
        // This gives the correct VolumeInfoList: real filenames, page counts, rotations
        // and PageNumberOffset without re-implementing the filename-detection rules here.
        var provider = new AvaloniaPdfDocumentProvider { SkipCloudOnlyFiles = false, VerboseLogging = false };
        var pdfMeta = await PdfMetaDataCore.ReadPdfMetaDataAsync(
            inputPdf, isSingles: false, pdfDocumentProvider: provider);

        if (pdfMeta == null)
        {
            LogMessage($"No PDFMetaData found...");
            // No JSON/BMK sidecar: replicate the production filename rule.
            // If vol-0 stem ends with '0' or '1', strip that digit to get the base;
            // then discover base+2.pdf, base+3.pdf, ... (mirrors LoadAllPdfMetaDataParallelAsync).
            pdfMeta = new PdfMetaDataReadResult { FullPathFile = inputPdf, IsSinglesFolder = false };

            var dir = Path.GetDirectoryName(inputPdf)!;
            var stem = Path.GetFileNameWithoutExtension(inputPdf);
            var last = stem.Last();
            var baseStem = "01".Contains(last) ? stem[..^1] : stem;

            pdfMeta.VolumeInfoList.Add(new PdfVolumeInfoBase
            {
                FileNameVolume = Path.GetFileName(inputPdf),
                NPagesInThisVolume = 0
            });

            for (int suffix = 2; ; suffix++)
            {
                var candidate = baseStem + suffix + ".pdf";
                if (!File.Exists(Path.Combine(dir, candidate)))
                    break;
                pdfMeta.VolumeInfoList.Add(new PdfVolumeInfoBase
                {
                    FileNameVolume = candidate,
                    NPagesInThisVolume = 0
                });
            }
        }

        // If caller supplied an explicit rotation, override whatever the sidecar says.
        if (rotation >= 0)
            foreach (var v in pdfMeta.VolumeInfoList)
                v.Rotation = rotation;

        LogMessage($"PageNumberOff  : {pdfMeta.PageNumberOffset}");
        LogMessage($"Volumes        : {pdfMeta.VolumeInfoList.Count}");
        foreach (var v in pdfMeta.VolumeInfoList)
            LogMessage($"  {v.FileNameVolume,-60} pages={v.NPagesInThisVolume,4}  rotation={v.Rotation}");
        var rangeDesc = (bookStart == 0 && bookEnd == 0) ? "all pages" : $"book pages {bookStart}–{bookEnd}";
        LogMessage($"Page range     : {rangeDesc}");

        // Temp directory
        var outputDir = Path.Combine(Path.GetTempPath(), "SheetMusicViewer_Audiveris");
        var baseName = Path.GetFileNameWithoutExtension(inputPdf);
        var omrFile = Path.Combine(outputDir, baseName + ".omr");
        var mxlFile = Path.Combine(outputDir, baseName + ".mxl");

        LogMessage($"\nTemp output dir: {outputDir}");
        LogMessage($"Expected .omr  : {omrFile}");
        LogMessage($"Expected .mxl  : {mxlFile}");

        foreach (var stale in new[] { omrFile, mxlFile })
        {
            if (File.Exists(stale))
            {
                File.Delete(stale);
                LogMessage($"Deleted stale  : {stale}");
            }
        }

        // Run the pipeline
        var progress = new Progress<string>(msg => LogMessage($"  [progress] {msg}"));

        LogMessage($"Spine padding  : {spinePaddingPx} px");
        AppSettings.Instance.SpinePaddingPx = spinePaddingPx;

        LogMessage("\n--- Starting Audiveris pipeline ---");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        string mxlResult;
        try
        {
            mxlResult = await MuseScoreExportService.RunAudiverisAsync(
                audiverisPath!,
                pdfMeta,
                bookStart: bookStart,
                bookEnd: bookEnd,
                progress: progress,
                ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogMessage($"\n[EXCEPTION] {ex}");
            Assert.Fail($"RunAudiverisAsync threw: {ex.Message}");
            return;
        }

        sw.Stop();
        LogMessage($"--- Pipeline finished in {sw.Elapsed.TotalSeconds:F1}s ---");

        // Report artifacts
        LogMessage("\n=== Intermediate / output files ===");
        ReportFile(omrFile, ".omr (Audiveris book)");
        ReportFile(mxlFile, ".mxl (MusicXML result)");
        ReportFile(mxlResult, "returned mxl path");

        LogMessage($"\nAll files in {outputDir}:");
        if (Directory.Exists(outputDir))
        {
            foreach (var f in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(f);
                LogMessage($"  {info.Length,10:N0} bytes  {f}");
            }
        }

        // Assertions
        Assert.IsTrue(File.Exists(mxlResult),
            $"MusicXML output file not found: {mxlResult}");
        Assert.IsTrue(new FileInfo(mxlResult).Length > 0,
            $"MusicXML output file is empty: {mxlResult}");

        LogMessage($"\nMusicXML produced: {mxlResult}");
        LogMessage($"  Size: {new FileInfo(mxlResult).Length:N0} bytes");

        if (museScorePath is not null)
            LogMessage($"\nTo open in MuseScore run:\n  \"{museScorePath}\" \"{mxlResult}\"");

        MuseScoreExportService.SetTempoInMusicXml(mxlResult, bpm: 90, progress: progress);
        MuseScoreExportService.LaunchMuseScore(museScorePath ?? string.Empty, mxlResult);
    }

    // -- Helpers --------------------------------------------------------------

    private void ReportFile(string path, string label)
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            LogMessage($"  {label,-30} {info.Length,10:N0} bytes  {path}");
        }
        else
        {
            LogMessage($"  {label,-30} (not found) {path}");
        }
    }
}

/// <summary>
/// Unit tests for <see cref="MuseScoreExportService.SanitizeFileName"/>.
/// These are fast, isolated, and safe to run in any environment.
/// </summary>
[TestClass]
public class SanitizeFileNameTests : TestBase
{
    // Delegate to the service's own IsZipSafe so tests use exactly the same
    // CP1252 check as production code (no duplicated encoding setup here).

    [TestMethod]
    [TestCategory("Unit")]
    public void SanitizeFileName_PlainAscii_Unchanged()
    {
        Assert.AreEqual("Hello World", MuseScoreExportService.SanitizeFileName("Hello World"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SanitizeFileName_EnDash_ReplacedWithHyphen()
    {
        var input = "Be Our Guest - G Major - MN0174098 \u2013 Kristen Mosca";
        var result = MuseScoreExportService.SanitizeFileName(input);
        Assert.IsFalse(result.Contains('\u2013'), "en-dash should be replaced");
        Assert.IsTrue(result.Contains('-'), "should contain a plain hyphen instead");
        Assert.IsTrue(MuseScoreExportService.IsZipSafe(result), "result must be CP1252-safe for Windows ZIP");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SanitizeFileName_EmDash_ReplacedWithHyphen()
    {
        var result = MuseScoreExportService.SanitizeFileName("Title \u2014 Subtitle");
        Assert.IsFalse(result.Contains('\u2014'), "em-dash should be replaced");
        Assert.IsTrue(MuseScoreExportService.IsZipSafe(result), "result must be CP1252-safe for Windows ZIP");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SanitizeFileName_CurlyQuotes_ReplacedWithApostrophe()
    {
        var result = MuseScoreExportService.SanitizeFileName("\u2018It\u2019s a Test\u201D");
        Assert.IsFalse(result.Any(c => c is '\u2018' or '\u2019' or '\u201C' or '\u201D'),
            "curly quotes should be replaced");
        Assert.IsTrue(MuseScoreExportService.IsZipSafe(result), "result must be CP1252-safe for Windows ZIP");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SanitizeFileName_Ellipsis_ReplacedWithUnderscore()
    {
        var result = MuseScoreExportService.SanitizeFileName("Wait\u2026 What");
        Assert.IsFalse(result.Contains('\u2026'), "ellipsis should be replaced");
        Assert.IsTrue(MuseScoreExportService.IsZipSafe(result), "result must be CP1252-safe for Windows ZIP");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SanitizeFileName_IllegalFileNameChars_ReplacedWithUnderscore()
    {
        // Colon and asterisk are illegal Windows filename chars
        var result = MuseScoreExportService.SanitizeFileName("Song: A*B");
        Assert.IsFalse(result.Contains(':'), "colon should be replaced");
        Assert.IsFalse(result.Contains('*'), "asterisk should be replaced");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SanitizeFileName_RealWorldEnDashFilename_ProducesCompressibleName()
    {
        // Filenames found by FindFilesWithIllegalCompressedFolderChars.
        // Validation uses CP1252 encoding (not a char list or CP437) — same codepage Windows ZIP uses.
        var inputs = new[]
        {
            "Be Our Guest - G Major - MN0174098 \u2013 Kristen Mosca",
            "Friend Like Me - D Minor - MN0174116 \u2013 Kristen Mosca",
            "Sleigh Ride - Bb Major - MN0180143 \u2013 Kristen Mosca",
            "SEARCHLIGHT RAG-A Syncopated March and Two Step \u2013 Scott Joplin",
        };

        foreach (var input in inputs)
        {
            var result = MuseScoreExportService.SanitizeFileName(input);
            Assert.IsTrue(MuseScoreExportService.IsZipSafe(result),
                $"SanitizeFileName(\"{input}\") produced a name not safe for Windows ZIP: \"{result}\"");
        }
    }
}
