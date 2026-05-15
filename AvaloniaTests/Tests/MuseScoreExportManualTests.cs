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
    // ── Test entry points ────────────────────────────────────────────────────

    /// <summary>Single-volume PDF: PatriciaRag (one file, multiple pages).</summary>
    [TestMethod]
    public async Task ExportSingleVolume()
    {
        const string pdf = @"C:\Users\Calvi\OneDrive\SheetMusic\Ragtime\Collections\PatriciaRag.pdf";
        await RunExportPipelineAsync(pdf, bookStart: 0, bookEnd: 0);
    }

    /// <summary>Multi-volume PDF: Alley Cat (base file + numbered siblings, combined via Ghostscript).</summary>
    [TestMethod]
    public async Task ExportMultiVolume_AlleyCat()
    {
        const string pdf = @"C:\Users\Calvi\OneDrive\SheetMusic\Pop\Frank Bjorn Alley Cat.pdf";
        await RunExportPipelineAsync(pdf, bookStart: 0, bookEnd: 0);
    }

    /// <summary>
    /// Multi-volume, rotated PDF: "Something Doing" from Scott Joplin Complete Piano Works.
    /// TOC display page 251 with pageNumberOffset=-45 → bookStart = 251-(-45)+1 = 297.
    /// The song ends before "Lily Queen" at display page 257 → bookEnd = 257-(-45) = 301.
    /// All three volumes have rotation=2 (180° / upside-down), handled by Ghostscript.
    /// </summary>
    [TestMethod]
    public async Task ExportMultiVolume_ScottJoplin_SomethingDoing()
    {
        // The JSON file is "Scott Joplin Complete Piano Works1.json" and the base PDF
        // is "Scott Joplin Complete Piano Works1.pdf".  Volumes: ...1.pdf, ...2.pdf, ...3.pdf.
        const string pdf = @"C:\Users\Calvi\OneDrive\SheetMusic\Ragtime\Collections\Scott Joplin Complete Piano Works1.pdf";

        // Rotation from JSON: all three volumes have "rotation": 2 (180 degrees).
        const int rotation = 2;

        // Page range for "Something Doing" (TOC pageNo=251, pageNumberOffset=-45):
        //   bookStart = 251 - (-45) + 1 = 297
        //   bookEnd   = 257 - (-45)     = 302  (one sheet before "Lily Queen" at display 257)
        // The user specified pp. 251-255 (display), i.e. physical 297-301.
        const int bookStart = 297;
        const int bookEnd   = 301;

        await RunExportPipelineAsync(pdf, bookStart, bookEnd, rotation);
    }

    // ── Shared pipeline ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds metadata for <paramref name="inputPdf"/>, auto-detecting sibling volume
    /// files by counting numeric suffixes (1, 2, 3 ...) until one is not found on disk.
    /// Then runs the full Audiveris to MusicXML pipeline and launches MuseScore.
    /// </summary>
    private async Task RunExportPipelineAsync(string inputPdf, int bookStart, int bookEnd, int rotation = 0)
    {
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

        // Build volume metadata.
        // Naming convention: BaseName.pdf, BaseName1.pdf, BaseName2.pdf, ...
        // vol 0 is always the base file; then increment suffix until no file found.
        var pdfMeta = new PdfMetaDataReadResult
        {
            FullPathFile    = inputPdf,
            IsSinglesFolder = false,
        };

        var dir  = Path.GetDirectoryName(inputPdf)!;
        var stem = Path.GetFileNameWithoutExtension(inputPdf);

        // vol 0 - base file
        pdfMeta.VolumeInfoList.Add(new PdfVolumeInfoBase
        {
            FileNameVolume     = Path.GetFileName(inputPdf),
            NPagesInThisVolume = 0,
            Rotation           = rotation
        });

        // vol 1, 2, 3 ... - numbered siblings
        for (int suffix = 1; ; suffix++)
        {
            var candidate = stem + suffix + ".pdf";
            if (!File.Exists(Path.Combine(dir, candidate)))
                break;
            pdfMeta.VolumeInfoList.Add(new PdfVolumeInfoBase
            {
                FileNameVolume     = candidate,
                NPagesInThisVolume = 0,
                Rotation           = rotation
            });
        }

        LogMessage($"Volumes        : {pdfMeta.VolumeInfoList.Count} ({string.Join(", ", pdfMeta.VolumeInfoList.Select(v => v.FileNameVolume))})");
        LogMessage($"Rotation       : {rotation} ({rotation * 90} degrees)");
        var rangeDesc = (bookStart == 0 && bookEnd == 0) ? "all pages" : $"book pages {bookStart}–{bookEnd}";
        LogMessage($"Page range     : {rangeDesc}");

        // Temp directory
        var outputDir = Path.Combine(Path.GetTempPath(), "SheetMusicViewer_Audiveris");
        var baseName  = Path.GetFileNameWithoutExtension(inputPdf);
        var omrFile   = Path.Combine(outputDir, baseName + ".omr");
        var mxlFile   = Path.Combine(outputDir, baseName + ".mxl");

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

        LogMessage("\n--- Starting Audiveris pipeline ---");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        string mxlResult;
        try
        {
            mxlResult = await MuseScoreExportService.RunAudiverisAsync(
                audiverisPath!,
                pdfMeta,
                bookStart: bookStart,
                bookEnd:   bookEnd,
                progress:  progress,
                ct:        CancellationToken.None);
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
        ReportFile(omrFile,   ".omr (Audiveris book)");
        ReportFile(mxlFile,   ".mxl (MusicXML result)");
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

    // ── Helpers ──────────────────────────────────────────────────────────────

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
