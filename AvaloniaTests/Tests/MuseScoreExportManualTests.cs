using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer.Desktop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Manual end-to-end test for the MuseScore export pipeline.
/// Exercises Audiveris OMR conversion and MuseScore launch on a real PDF.
/// Run manually: dotnet test --filter "TestCategory=Manual&amp;MethodName=ExportPatriciaRagToMuseScore"
/// </summary>
[TestClass]
[TestCategory("Manual")]
public class MuseScoreExportManualTests : TestBase
{
    private const string PatriciaRagPdf =
        @"C:\Users\Calvi\OneDrive\SheetMusic\Ragtime\Collections\PatriciaRag.pdf";

    /// <summary>
    /// Runs the full Audiveris → MusicXML pipeline on PatriciaRag.pdf (all pages),
    /// prints the paths of every intermediate temp file to the test output,
    /// and verifies that a non-empty .mxl file was produced.
    /// MuseScore is NOT launched automatically so the test remains headless.
    /// </summary>
    [TestMethod]
    public async Task ExportPatriciaRagToMuseScore()
    {
        // ── Prerequisites ─────────────────────────────────────────────────────
        if (!File.Exists(PatriciaRagPdf))
            Assert.Inconclusive($"Source PDF not found: {PatriciaRagPdf}");

        var audiverisPath = MuseScoreExportService.AutoDetectAudiveris();
        if (audiverisPath is null)
            Assert.Inconclusive(
                $"Audiveris executable not found. Checked paths:\n  " +
                string.Join("\n  ", MuseScoreExportService.AudiverisDefaultPaths));

        var museScorePath = MuseScoreExportService.AutoDetectMuseScore();

        LogMessage("=== MuseScore Export Manual Test: PatriciaRag ===");
        LogMessage($"Source PDF     : {PatriciaRagPdf}");
        LogMessage($"Audiveris      : {audiverisPath}");
        LogMessage($"MuseScore      : {museScorePath ?? "(not found – launch skipped)"}");

        // ── Temp directory (same as production code) ──────────────────────────
        var outputDir = Path.Combine(Path.GetTempPath(), "SheetMusicViewer_Audiveris");
        var baseName  = Path.GetFileNameWithoutExtension(PatriciaRagPdf);
        var omrFile   = Path.Combine(outputDir, baseName + ".omr");
        var mxlFile   = Path.Combine(outputDir, baseName + ".mxl");

        LogMessage($"\nTemp output dir: {outputDir}");
        LogMessage($"Expected .omr  : {omrFile}");
        LogMessage($"Expected .mxl  : {mxlFile}");

        // Delete stale artifacts so the run is fresh
        foreach (var stale in new[] { omrFile, mxlFile })
        {
            if (File.Exists(stale))
            {
                File.Delete(stale);
                LogMessage($"Deleted stale  : {stale}");
            }
        }

        // ── Run the pipeline ──────────────────────────────────────────────────
        var log = new List<string>();
        var progress = new Progress<string>(msg =>
        {
            log.Add(msg);
            LogMessage($"  [progress] {msg}");
        });

        LogMessage("\n--- Starting Audiveris pipeline ---");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        string mxlResult;
        try
        {
            mxlResult = await MuseScoreExportService.RunAudiverisAsync(
                audiverisPath!,
                PatriciaRagPdf,
                startPage: 3,   // process all pages
                endPage:   3,
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

        // ── Report intermediate artifacts ──────────────────────────────────────
        LogMessage("\n=== Intermediate / output files ===");
        ReportFile(omrFile,    ".omr (Audiveris book)");
        ReportFile(mxlFile,    ".mxl (MusicXML result)");
        ReportFile(mxlResult,  "returned mxl path");

        // Also list all files in the output dir for easy inspection
        LogMessage($"\nAll files in {outputDir}:");
        if (Directory.Exists(outputDir))
        {
            foreach (var f in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(f);
                LogMessage($"  {info.Length,10:N0} bytes  {f}");
            }
        }

        // ── Assertions ────────────────────────────────────────────────────────
        Assert.IsTrue(File.Exists(mxlResult),
            $"MusicXML output file not found: {mxlResult}");
        Assert.IsTrue(new FileInfo(mxlResult).Length > 0,
            $"MusicXML output file is empty: {mxlResult}");

        LogMessage($"\n✓ MusicXML produced: {mxlResult}");
        LogMessage($"  Size: {new FileInfo(mxlResult).Length:N0} bytes");

        if (museScorePath is not null)
            LogMessage($"\nTo open in MuseScore run:\n  \"{museScorePath}\" \"{mxlResult}\"");
        MuseScoreExportService.SetTempoInMusicXml(mxlResult, bpm: 90, progress: progress);
        MuseScoreExportService.LaunchMuseScore(museScorePath ?? string.Empty, mxlResult);
    }

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
