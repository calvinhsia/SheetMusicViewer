using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SheetMusicLib;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Handles PDF extraction, Audiveris OMR conversion, and launching MuseScore Studio.
/// </summary>
public static class MuseScoreExportService
{
    /// <summary>
    /// Default candidate paths for the Audiveris executable (Windows, macOS, Linux).
    /// </summary>
    public static IReadOnlyList<string> AudiverisDefaultPaths { get; } = BuildAudiverisPaths();

    /// <summary>
    /// Default candidate paths for MuseScore Studio (Windows, macOS, Linux).
    /// </summary>
    public static IReadOnlyList<string> MuseScoreDefaultPaths { get; } = BuildMuseScorePaths();

    private static string[] BuildAudiverisPaths()
    {
        var list = new List<string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pf  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            list.Add(Path.Combine(pf,  "Audiveris", "bin", "Audiveris.bat"));
            list.Add(Path.Combine(pf,  "Audiveris", "Audiveris.bat"));
            list.Add(Path.Combine(pf,  "Audiveris", "bin", "Audiveris.exe"));
            list.Add(Path.Combine(pf,  "Audiveris", "Audiveris.exe"));
            list.Add(Path.Combine(lad, "Audiveris", "bin", "Audiveris.bat"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            list.Add("/Applications/Audiveris.app/Contents/MacOS/Audiveris");
            list.Add("/usr/local/bin/audiveris");
            list.Add("/opt/homebrew/bin/audiveris");
            list.Add(Path.Combine(home, "Applications", "Audiveris.app", "Contents", "MacOS", "Audiveris"));
        }
        else // Linux
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            list.Add("/usr/bin/audiveris");
            list.Add("/usr/local/bin/audiveris");
            list.Add("/opt/audiveris/bin/audiveris");
            list.Add(Path.Combine(home, ".local", "bin", "audiveris"));
        }
        return list.ToArray();
    }

    private static string[] BuildMuseScorePaths()
    {
        var list = new List<string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pf  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            list.Add(Path.Combine(pf,  "MuseScore 4", "bin", "MuseScore4.exe"));
            list.Add(Path.Combine(pf,  "MuseScore 4", "MuseScore4.exe"));
            list.Add(Path.Combine(pfx, "MuseScore 4", "bin", "MuseScore4.exe"));
            // MuseScore 3 fallback
            list.Add(Path.Combine(pf,  "MuseScore 3", "bin", "MuseScore3.exe"));
            list.Add(Path.Combine(pfx, "MuseScore 3", "bin", "MuseScore3.exe"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            list.Add("/Applications/MuseScore 4.app/Contents/MacOS/mscore");
            list.Add("/Applications/MuseScore4.app/Contents/MacOS/mscore");
            list.Add("/Applications/MuseScore 3.app/Contents/MacOS/mscore");
            list.Add(Path.Combine(home, "Applications", "MuseScore 4.app", "Contents", "MacOS", "mscore"));
        }
        else // Linux
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            list.Add("/usr/bin/musescore4");
            list.Add("/usr/bin/mscore4");
            list.Add("/usr/local/bin/musescore4");
            list.Add("/opt/musescore4/bin/mscore4");
            // AppImage typical location
            list.Add(Path.Combine(home, "Applications", "MuseScore-4.x86_64.AppImage"));
            // MuseScore 3 fallback
            list.Add("/usr/bin/musescore3");
            list.Add("/usr/bin/mscore3");
        }
        return list.ToArray();
    }

    /// <summary>
    /// Default candidate paths for Ghostscript (used to normalise PDFs before Audiveris).
    /// </summary>
    public static IReadOnlyList<string> GhostscriptDefaultPaths { get; } = BuildGhostscriptPaths();

    private static string[] BuildGhostscriptPaths()
    {
        var list = new List<string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pf  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            // Ghostscript installers put versioned dirs under Program Files\gs\gsX.XX\bin
            foreach (var root in new[] { pf, pfx })
            {
                var gsRoot = Path.Combine(root, "gs");
                if (Directory.Exists(gsRoot))
                {
                    foreach (var ver in Directory.GetDirectories(gsRoot).OrderByDescending(d => d))
                    {
                        var exe = Path.Combine(ver, "bin", "gswin64c.exe");
                        if (File.Exists(exe)) list.Add(exe);
                        exe = Path.Combine(ver, "bin", "gswin32c.exe");
                        if (File.Exists(exe)) list.Add(exe);
                    }
                }
            }
            // Also check common fixed paths
            list.Add(Path.Combine(pf,  "gs", "bin", "gswin64c.exe"));
            list.Add(Path.Combine(pfx, "gs", "bin", "gswin32c.exe"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            list.Add("/usr/local/bin/gs");
            list.Add("/opt/homebrew/bin/gs");
        }
        else
        {
            list.Add("/usr/bin/gs");
            list.Add("/usr/local/bin/gs");
        }
        return list.ToArray();
    }

    /// <summary>
    /// Returns the first default path that exists on disk, or null if none found.
    /// </summary>
    public static string? AutoDetectAudiveris() =>
        AudiverisDefaultPaths.FirstOrDefault(File.Exists);

    /// <summary>
    /// Returns the first default MuseScore path that exists on disk, or null if none found.
    /// </summary>
    public static string? AutoDetectMuseScore() =>
        MuseScoreDefaultPaths.FirstOrDefault(File.Exists);

    /// <summary>
    /// Returns the first Ghostscript executable found on disk, or null.
    /// </summary>
    public static string? AutoDetectGhostscript() =>
        GhostscriptDefaultPaths.FirstOrDefault(File.Exists);

    /// <summary>
    /// Extracts a subset of PDF pages to a temporary file using PdfPig (iText-style byte copy).
    /// If the entire PDF is requested, simply copies it.
    /// Returns the path to the extracted temp PDF.
    /// </summary>
    public static async Task<string> ExtractPdfPagesAsync(
        string sourcePdfPath,
        int startPage,   // 1-based
        int endPage,     // 1-based, inclusive
        int totalPages,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SheetMusicViewer_Export");
        Directory.CreateDirectory(tempDir);

        var baseName = Path.GetFileNameWithoutExtension(sourcePdfPath);
        var tempPdfPath = Path.Combine(tempDir, $"{baseName}_p{startPage}-{endPage}.pdf");

#if DEBUG
        // Keep extracted PDFs in debug builds so they can be inspected
        progress?.Report($"Extracting to: {tempPdfPath}");
#endif

        progress?.Report($"Extracting pages {startPage}–{endPage}…");

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // If the range is the whole document, just copy
            if (startPage == 1 && endPage == totalPages)
            {
                File.Copy(sourcePdfPath, tempPdfPath, overwrite: true);
                return;
            }

            // Use PDFtoImage's underlying PdfDocument to get page count, then
            // use itext/PDFsharp if available. Since neither is in the project,
            // fall back to the GhostScript approach via a temporary copy and
            // let Audiveris page-range arguments do the filtering when possible.
            // For now: extract using the PDFBox approach via pdfium/skia:
            // We build a minimal page-subset PDF using raw byte manipulation.
            // The most portable cross-dependency way: just copy the full PDF
            // and pass -pages argument to Audiveris.
            File.Copy(sourcePdfPath, tempPdfPath, overwrite: true);
        }, ct);

        return tempPdfPath;
    }

    /// <summary>
    /// A segment of pages within a single volume PDF.
    /// </summary>
    /// <summary>
    /// PortableRotation value from the JSON metadata (0=normal, 1=90°CW, 2=180°, 3=270°CW).
    /// Passed to Ghostscript so upside-down books (e.g. Scott Joplin) are rotated before OMR.
    /// </summary>
    public record VolumeSegment(string PdfPath, int LocalStart, int LocalEnd, int Rotation = 0);

    /// <summary>
    /// Maps a book-page range (1-based, across all volumes) to a list of per-volume
    /// segments, each carrying the PDF path and the 1-based sheet range within that PDF.
    /// bookStart/bookEnd are 1-based page numbers in the full set (1 = first page of vol 0).
    /// Pass bookStart=0/bookEnd=0 to include all pages of all volumes.
    /// </summary>
    public static List<VolumeSegment> ResolveVolumeSegments(
        PdfMetaDataReadResult meta,
        int bookStart,  // 1-based in the full set, or 0 for "all"
        int bookEnd)    // 1-based inclusive in the full set, or 0 for "all"
    {
        var volumes = meta.VolumeInfoList;

        // When page counts are unknown (NPagesInThisVolume==0) or "all" requested,
        // emit each volume as a full-volume segment (localStart=0, localEnd=0).
        bool allPages = bookStart <= 0 || bookEnd <= 0;
        bool unknownPageCounts = volumes.All(v => v.NPagesInThisVolume == 0);

        if (allPages || unknownPageCounts)
        {
            return volumes.Select((v, i) =>
                new VolumeSegment(meta.GetFullPathFileFromVolno(i), 0, 0, v.Rotation)).ToList();
        }

        var segments = new List<VolumeSegment>();
        int offset = 0;  // cumulative page count before current volume (0-based)

        for (int v = 0; v < volumes.Count; v++)
        {
            int volPages = volumes[v].NPagesInThisVolume;
            // 1-based range of this volume in book-page space
            int volBookStart = offset + 1;
            int volBookEnd   = offset + volPages;

            // Intersection with requested range
            int intersectStart = Math.Max(bookStart, volBookStart);
            int intersectEnd   = Math.Min(bookEnd,   volBookEnd);

            if (intersectStart <= intersectEnd)
            {
                // Convert to 1-based local sheet numbers within this volume's PDF.
                // Use 0/0 when the whole volume is covered (lets Audiveris skip -sheets).
                int localStart = intersectStart - offset;
                int localEnd   = intersectEnd   - offset;
                if (localStart == 1 && localEnd == volPages) { localStart = 0; localEnd = 0; }
                var pdfPath = meta.GetFullPathFileFromVolno(v);
                segments.Add(new VolumeSegment(pdfPath, localStart, localEnd, volumes[v].Rotation));
            }

            offset += volPages;
        }

        return segments;
    }

    /// <summary>
    /// Runs Audiveris on the given PDF and returns the output MXL/XML file path.
    /// Strategy:
    ///   Pass 1 — batch transcribe (no -export); always writes .omr, exit code ignored.
    ///   Patch  — edit book.xml inside the .omr ZIP to set valid="true" on all SheetStub
    ///            elements, overriding the rhythm-warning block that prevents export.
    ///   Pass 2 — batch export the patched .omr; this skips re-transcription and exports cleanly.
    /// bookStart/bookEnd: 1-based page numbers in the full multi-volume set; pass 0/0 for all pages.
    /// </summary>
    public static async Task<string> RunAudiverisAsync(
        string audiverisPath,
        PdfMetaDataReadResult pdfMetaData,
        int bookStart = 0,
        int bookEnd = 0,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var segments = ResolveVolumeSegments(pdfMetaData, bookStart, bookEnd);

        if (segments.Count == 0)
            throw new InvalidOperationException(
                "The requested page range does not overlap any volume in this book.");

        if (segments.Count == 1)
        {
            var s = segments[0];
            return await RunAudiverisAsync(audiverisPath, s.PdfPath, s.LocalStart, s.LocalEnd, progress, ct, s.Rotation);
        }

        // Multiple volumes — use Ghostscript to concatenate all volume PDFs (or page slices)
        // into a single multi-page TIFF, then run one Audiveris pass on that combined file.
        // This avoids having to merge MusicXML files later and gives Audiveris a clean
        // continuous image sequence covering the full requested range.
        var gsPath = AppSettings.Instance.GhostscriptPath;
        if (string.IsNullOrWhiteSpace(gsPath))
            gsPath = AutoDetectGhostscript();

        if (!string.IsNullOrWhiteSpace(gsPath) && File.Exists(gsPath))
        {
            progress?.Report($"Range spans {segments.Count} volumes — combining with Ghostscript into a single TIFF…");
            var outputDir = Path.Combine(Path.GetTempPath(), "SheetMusicViewer_Audiveris");
            Directory.CreateDirectory(outputDir);

            // Build a combined name from the base of the first and last PDF
            var firstName = Path.GetFileNameWithoutExtension(segments[0].PdfPath);
            var lastName  = Path.GetFileNameWithoutExtension(segments[^1].PdfPath);
            var combinedBaseName = firstName == lastName ? firstName : $"{firstName}_to_{lastName}";
            var combinedTiff = Path.Combine(outputDir, combinedBaseName + "_combined.tif");

            var combined = await CombineVolumesWithGhostscriptAsync(
                gsPath, segments, combinedTiff, outputDir, progress, ct);

            // Run one Audiveris pass on the combined TIFF (all pages = 0,0)
            return await RunAudiverisAsync(audiverisPath, combined, 0, 0, progress, ct);
        }

        // GS not available — fall back to processing each volume separately and warn.
        progress?.Report($"⚠ Ghostscript not found — processing {segments.Count} volumes separately. " +
                         "Install Ghostscript for seamless multi-volume export.");
        var results = new List<string>();
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            progress?.Report($"Volume {i + 1}/{segments.Count}: {Path.GetFileName(s.PdfPath)} sheets {s.LocalStart}–{s.LocalEnd}");
            var mxl = await RunAudiverisAsync(audiverisPath, s.PdfPath, s.LocalStart, s.LocalEnd, progress, ct);
            results.Add(mxl);
        }

        if (results.Count > 1)
            progress?.Report($"⚠ {results.Count} MusicXML files produced (one per volume). Opening volume 1; remaining files are in the same temp folder.");

        return results[0];
    }

    /// <summary>
    /// Runs Audiveris on the given PDF and returns the output MXL/XML file path.
    /// Strategy:
    ///   Pass 1 — batch transcribe (no -export); always writes .omr, exit code ignored.
    ///   Patch  — edit book.xml inside the .omr ZIP to set valid="true" on all SheetStub
    ///            elements, overriding the rhythm-warning block that prevents export.
    ///   Pass 2 — batch export the patched .omr; this skips re-transcription and exports cleanly.
    /// startPage/endPage: 1-based inclusive within the single PDF; pass 0 for both to process all pages.
    /// </summary>
    public static async Task<string> RunAudiverisAsync(
        string audiverisPath,
        string pdfPath,
        int startPage = 0,
        int endPage = 0,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        int rotation = 0)
    {
        if (!File.Exists(audiverisPath))
            throw new FileNotFoundException($"Audiveris not found at: {audiverisPath}");
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException($"PDF not found at: {pdfPath}");

        var outputDir = Path.Combine(Path.GetTempPath(), "SheetMusicViewer_Audiveris");
        Directory.CreateDirectory(outputDir);

        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var omrFile = Path.Combine(outputDir, baseName + ".omr");

        // Overall stopwatch — covers all steps (GS, pass 1, patch, pass 2, tempo inject).
        var overallSw = Stopwatch.StartNew();

        // Record time just before we start so the output-file search can
        // reject stale files from previous runs of different PDFs.
        var runStartedAt = DateTime.UtcNow;

        bool hasRange = startPage > 0 && endPage > 0;
        var rangeLabel = hasRange ? $" (sheets {startPage}–{endPage})" : "";

        // ── Optional: rasterize the PDF with Ghostscript ─────────────────────
        // When enabled, Ghostscript rasterizes the PDF to a TIFF at 300 DPI, completely
        // bypassing Audiveris's PDFBox reader. This fixes PDFs with non-standard page
        // trees that PDFBox expands to only 1 page.
        var gsPath = AppSettings.Instance.GhostscriptPath;
        if (string.IsNullOrWhiteSpace(gsPath))
            gsPath = AutoDetectGhostscript();
        // Apply GS normalization when explicitly enabled OR when rotation correction is needed.
        // A rotated PDF (rotation != 0) produces an upside-down score if fed raw to Audiveris.
        bool needsGsForRotation = rotation != 0;
        // When GS produces a range-sliced TIFF, Audiveris sees pages 1..N not startPage..endPage.
        bool gsSlicedRange = false;
        if ((AppSettings.Instance.UseGhostscript || needsGsForRotation) && !string.IsNullOrWhiteSpace(gsPath) && File.Exists(gsPath))
        {
            pdfPath = await NormalisePdfWithGhostscriptAsync(gsPath, pdfPath, outputDir, progress, ct, rotation,
                hasRange ? startPage : 0, hasRange ? endPage : 0,
                spinePaddingPx: AppSettings.Instance.SpinePaddingPx);
            // Re-derive baseName and omrFile from the (possibly renamed) normalised file.
            baseName = Path.GetFileNameWithoutExtension(pdfPath);
            omrFile  = Path.Combine(outputDir, baseName + ".omr");
            // The TIFF contains only the requested pages renumbered 1..N, so tell
            // Audiveris to process sheets 1..(endPage-startPage+1) instead.
            if (hasRange)
            {
                gsSlicedRange = true;
                int sliceCount = endPage - startPage + 1;
                startPage = 1;
                endPage   = sliceCount;
                rangeLabel = $" (sheets 1–{sliceCount} of sliced TIFF)";
            }
        }

        // ── Pass 1: transcribe through PAGE step → .omr ────────────────────────
        // Must pass -step PAGE explicitly; without it Audiveris just loads/saves
        // an empty book without running any recognition pipeline.
        // PAGE is the final step (after RHYTHMS) that creates the Score object.
        // Exit code is ignored: Audiveris exits 1 when rhythm warnings occur but
        // still writes a fully-populated .omr with all sheet data.
        // Log the page dimensions of the file being fed to Audiveris so the user can
        // diagnose issues such as spine-padding making pages appear blank.
        var pdfPageSize = TryReadPdfFirstPageSize(pdfPath);
        if (pdfPageSize.HasValue)
            progress?.Report($"Input to Audiveris: {Path.GetFileName(pdfPath)}  page size = {pdfPageSize.Value.Width:F1} × {pdfPageSize.Value.Height:F1} pts  ({pdfPageSize.Value.Width / 72.0:F2}" + $" × {pdfPageSize.Value.Height / 72.0:F2} in)");
        else
            progress?.Report($"Input to Audiveris: {Path.GetFileName(pdfPath)}  (page size unavailable)");

        progress?.Report($"Pass 1: Transcribing with Audiveris{rangeLabel} (this may take several minutes)…");
        await RunAudiverisProcessAsync(audiverisPath, args =>
        {
            args.Add("-batch");
            args.Add("-step");
            args.Add("PAGE");
            if (hasRange)
            {
                args.Add("-sheets");
                args.Add($"{startPage}-{endPage}");
            }
            args.Add("-output");
            args.Add(outputDir);
            args.Add(pdfPath);
        }, progress, ct, ignoreExitCode: true);

        if (!File.Exists(omrFile))
            throw new FileNotFoundException(
                $"Audiveris pass 1 did not produce an .omr file in: {outputDir}");

        // ── Detect out-of-range / completely empty pass 1 early ─────────────────
        // Check whether Audiveris actually loaded any sheet images into the .omr.
        // We look for "sheet#N/" directories in the ZIP rather than <steps/> in
        // book.xml, which Audiveris writes as empty even when LOAD/BINARY ran.
        var processedSheetCount = GetOmrZipSheetCount(omrFile);
        if (processedSheetCount == 0)
        {
            var totalInOmr = GetOmrSheetCount(omrFile);
            if (hasRange)
            {
                throw new InvalidOperationException(
                    $"Audiveris did not load any sheets for the requested range ({startPage}–{endPage})." +
                    $" The PDF has {totalInOmr} sheet(s) according to its page tree.\n\n" +
                    "The requested page range may be outside the bounds of this PDF.\n" +
                    "Try a smaller range or switch to \"Entire PDF\".");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Audiveris could not load any pages from this PDF" +
                    $" ({totalInOmr} sheet(s) reported in page tree).\n\n" +
                    "The PDF may use a non-standard page structure that PDFBox cannot read.\n" +
                    "Install Ghostscript to enable automatic PDF normalisation before conversion.");
            }
        }

        // ── Collect valid sheet numbers BEFORE patching (invalid flags still present) ──
        var validSheetNumbers = GetValidSheetNumbers(omrFile);
        var totalOmrSheets = GetOmrSheetCount(omrFile);

        // ── Patch: remove invalid flags so Audiveris will export ────────────────
        progress?.Report("Patching .omr to allow export despite transcription warnings…");
        PatchOmrValid(omrFile, progress);

        // ── Pass 2: run PAGE step + export on the patched .omr ─────────────────
        // Limit to valid sheets so Audiveris does not re-process invalid title/cover
        // pages that caused pass 1 to fail (those sheets have no staff lines).
        // Exit code is ignored: Audiveris still exits 1 on rhythm warnings even when
        // export succeeds; we check for the actual output file ourselves.
        progress?.Report("Pass 2: Running PAGE step and exporting MusicXML…");

        // When a page range was given, only those sheets were transcribed in pass 1.
        // Always restrict pass 2 to that same range so Audiveris does not try to
        // process un-transcribed sheets (e.g. a title page) that will be flagged
        // invalid and cause the export run to abort.
        IEnumerable<int> exportSheets;
        if (hasRange)
        {
            // Intersect the requested range with the sheets that were actually valid.
            var rangeSet = Enumerable.Range(startPage, endPage - startPage + 1).ToHashSet();
            exportSheets = validSheetNumbers.Count > 0
                ? validSheetNumbers.Where(n => rangeSet.Contains(n))
                : rangeSet.Order();
        }
        else
        {
            exportSheets = (validSheetNumbers.Count > 0 && validSheetNumbers.Count < totalOmrSheets)
                ? validSheetNumbers
                : Enumerable.Empty<int>();
        }

        var exportList = exportSheets.ToList();
        var validSheetsArg = exportList.Count > 0 ? string.Join(",", exportList) : null;
        if (validSheetsArg != null)
            progress?.Report($"  Exporting sheets: {validSheetsArg}");

        await RunAudiverisProcessAsync(audiverisPath, args =>
        {
            args.Add("-batch");
            args.Add("-step");
            args.Add("PAGE");
            args.Add("-export");
            if (validSheetsArg != null)
            {
                args.Add("-sheets");
                args.Add(validSheetsArg);
            }
            args.Add("-output");
            args.Add(outputDir);
            args.Add(omrFile);
        }, progress, ct, ignoreExitCode: true);

        progress?.Report("Export complete. Locating output file…");

        var subDir = Path.Combine(outputDir, baseName);
        var searchDir = Directory.Exists(subDir) ? subDir : outputDir;

        // Only accept files that (a) belong to this export (name starts with baseName)
        // and (b) were written after this run started, to avoid returning stale
        // output files left over from previous exports of other PDFs.
        foreach (var pattern in new[] { "*.mxl", "*.musicxml", "*.xml" })
        {
            var found = Directory.EnumerateFiles(searchDir, pattern, SearchOption.AllDirectories)
                .Where(f =>
                    Path.GetFileNameWithoutExtension(f)
                        .StartsWith(baseName, StringComparison.OrdinalIgnoreCase) &&
                    File.GetLastWriteTimeUtc(f) >= runStartedAt)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
            if (found != null)
            {
                ValidateMusicXmlHasNotes(found);
                overallSw.Stop();
                // Use processedSheetCount as the best page count available.
                int reportedPages = processedSheetCount > 0 ? processedSheetCount : (hasRange ? endPage - startPage + 1 : 1);
                double secsPerPage = reportedPages > 0 ? overallSw.Elapsed.TotalSeconds / reportedPages : overallSw.Elapsed.TotalSeconds;
                progress?.Report($"Overall conversion: {overallSw.Elapsed:m\\:ss\\.f} for {reportedPages} page(s) — {secsPerPage:F1} sec/page");
                return found;
            }
        }

        // Check if Audiveris flagged every sheet as invalid (no staff lines recognized)
        if (validSheetNumbers.Count == 0 && totalOmrSheets > 0)
        {
            var hint = totalOmrSheets == 1
                ? "The exported page appears to be a title/cover page with no music staves.\n" +
                  "Try using Custom Range to select only the pages that contain music notation (e.g. skip page 1)."
                : $"All {totalOmrSheets} page(s) were flagged as invalid — no staff lines were detected on any of them.\n" +
                  "Try using Custom Range to select only the pages that contain music notation.";
            throw new InvalidOperationException(
                $"Audiveris could not recognize any music notation in the selected pages.\n\n{hint}");
        }

        throw new FileNotFoundException(
            $"Audiveris finished but no MusicXML output found in: {searchDir}\n\n" +
            $"The .omr project file is at: {omrFile}\n" +
            $"Open it in Audiveris manually, then use File > Export As…");
    }

    private static int GetOmrSheetCount(string omrPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(omrPath);
            var entry = zip.GetEntry("book.xml");
            if (entry == null) return 0;
            using var reader = new StreamReader(entry.Open());
            var doc = XDocument.Parse(reader.ReadToEnd());
            return doc.Descendants("sheet").Count();
        }
        catch { return 0; }
    }

    /// <summary>
    /// Returns the number of sheet data folders in the .omr ZIP (entries like "sheet#1/").
    /// Audiveris creates these when it actually loads a sheet image, regardless of whether
    /// recognition succeeded.  book.xml &lt;steps/&gt; is unreliable for this purpose.
    /// </summary>
    private static int GetOmrZipSheetCount(string omrPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(omrPath);
            var sheetRegex = new System.Text.RegularExpressions.Regex(
                @"^sheet#(\d+)/",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return zip.Entries
                .Select(e => sheetRegex.Match(e.FullName))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .Count();
        }
        catch { return 0; }
    }

    private static int GetOmrValidSheetCount(string omrPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(omrPath);
            var entry = zip.GetEntry("book.xml");
            if (entry == null) return 0;
            using var reader = new StreamReader(entry.Open());
            var doc = XDocument.Parse(reader.ReadToEnd());
            // A sheet is valid if it has no invalid="true" attribute
            return doc.Descendants("sheet")
                .Count(s => s.Attribute("invalid") == null);
        }
        catch { return 0; }
    }

    /// <summary>
    /// Returns the 1-based sheet numbers that are NOT flagged invalid in book.xml.
    /// </summary>
    private static List<int> GetValidSheetNumbers(string omrPath)
    {
        var result = new List<int>();
        try
        {
            using var zip = ZipFile.OpenRead(omrPath);
            var entry = zip.GetEntry("book.xml");
            if (entry == null) return result;
            using var reader = new StreamReader(entry.Open());
            var doc = XDocument.Parse(reader.ReadToEnd());
            int index = 1;
            foreach (var sheet in doc.Descendants("sheet"))
            {
                if (sheet.Attribute("invalid") == null)
                    result.Add(index);
                index++;
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Opens the .omr ZIP archive and removes:
    ///   • invalid="true" from sheet elements (so pass 2 will export them), and
    ///   • movement-start="true" from page elements (so Audiveris produces one Score,
    ///     not multiple scores that overwrite each other in the .mxl output).
    /// Uses a safe copy-then-replace approach to avoid ZipArchiveMode.Update
    /// corrupting other compressed entries in the archive.
    /// Does NOT fake any step completion — pass 2 will run -step PAGE itself.
    /// </summary>
    private static void PatchOmrValid(string omrPath, IProgress<string>? progress)
    {
        // Read book.xml
        string rawXml;
        using (var zipRead = ZipFile.OpenRead(omrPath))
        {
            var bookEntry = zipRead.GetEntry("book.xml");
            if (bookEntry == null)
            {
                progress?.Report("⚠ book.xml not found in .omr — skipping patch");
                return;
            }
            using var stream = bookEntry.Open();
            using var reader = new StreamReader(stream);
            rawXml = reader.ReadToEnd();
        }

        progress?.Report($"book.xml: {rawXml}");

        var doc = XDocument.Parse(rawXml);
        var changed = false;

        foreach (var sheet in doc.Descendants("sheet"))
        {
            var invalidAttr = sheet.Attribute("invalid");
            if (invalidAttr != null)
            {
                invalidAttr.Remove();
                changed = true;
            }
        }

        // Remove movement-start="true" from all <page> elements.
        // When present, Audiveris splits the book into multiple Score objects that all
        // export to the same .mvtnull.mxl filename, so only the last (shortest) one
        // survives on disk — causing MuseScore to show only a few measures.
        foreach (var page in doc.Descendants("page"))
        {
            var mvAttr = page.Attribute("movement-start");
            if (mvAttr != null)
            {
                mvAttr.Remove();
                changed = true;
            }
        }

        if (!changed)
        {
            progress?.Report("book.xml: no invalid flags or movement-start attributes found");
            return;
        }

        // Rewrite the whole ZIP to avoid ZipArchiveMode.Update corrupting compressed entries.
        var newXml = doc.ToString(SaveOptions.OmitDuplicateNamespaces);
        progress?.Report("Patched book.xml: removed invalid flags and movement-start attributes — rewriting .omr safely…");

        var tempPath = omrPath + ".tmp";
        using (var zipRead = ZipFile.OpenRead(omrPath))
        using (var zipWrite = ZipFile.Open(tempPath, ZipArchiveMode.Create))
        {
            foreach (var entry in zipRead.Entries)
            {
                var newEntry = zipWrite.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                using var src = entry.Open();
                using var dst = newEntry.Open();

                if (entry.FullName == "book.xml")
                {
                    using var writer = new StreamWriter(dst);
                    writer.Write(newXml);
                }
                else
                {
                    src.CopyTo(dst);
                }
            }
        }

        File.Delete(omrPath);
        File.Move(tempPath, omrPath);
        progress?.Report("Patched .omr written successfully.");
    }

    /// <summary>
    /// Uses Ghostscript to concatenate the page slices from multiple volume PDFs into a
    /// single normalised PDF.  Each segment's LocalStart/LocalEnd selects which pages
    /// are taken from that PDF (0/0 = all pages of that PDF).
    /// Rotation is burned into the page content so Audiveris PDFBox gives correct part names.
    /// Returns the path to the combined PDF (or the first PDF path on failure).
    /// </summary>
    private static async Task<string> CombineVolumesWithGhostscriptAsync(
        string gsPath,
        IReadOnlyList<VolumeSegment> segments,
        string outputTiff,   // parameter name kept for call-site compat; .tif ext is replaced with .pdf
        string outputDir,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        // Derive the output PDF path from the caller-supplied tiff path.
        var outputPdf = Path.ChangeExtension(outputTiff, ".pdf");

        // For segments that are partial (LocalStart > 0), we first extract just those
        // pages to a temp PDF using GS pdfwrite, then feed that temp PDF into the
        // final pdfwrite run.  Full-volume segments (0/0) are fed directly.
        // Track (filePath, rotation) so per-volume orientation can be applied.
        var inputFiles = new List<(string Path, int Rotation)>();

        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            bool isFullVolume = seg.LocalStart == 0 && seg.LocalEnd == 0;

            if (isFullVolume)
            {
                inputFiles.Add((seg.PdfPath, seg.Rotation));
            }
            else
            {
                // Extract the requested page range from this volume to a temp PDF
                var sliceName = $"{Path.GetFileNameWithoutExtension(seg.PdfPath)}_p{seg.LocalStart}-{seg.LocalEnd}.pdf";
                var slicePath = Path.Combine(outputDir, sliceName);

                progress?.Report($"  Extracting pages {seg.LocalStart}–{seg.LocalEnd} from {Path.GetFileName(seg.PdfPath)}…");

                var psiSlice = new ProcessStartInfo
                {
                    FileName = gsPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute  = false,
                    CreateNoWindow   = true
                };
                psiSlice.ArgumentList.Add("-dBATCH");
                psiSlice.ArgumentList.Add("-dNOPAUSE");
                psiSlice.ArgumentList.Add("-dSAFER");
                psiSlice.ArgumentList.Add("-sDEVICE=pdfwrite");
                psiSlice.ArgumentList.Add($"-dFirstPage={seg.LocalStart}");
                psiSlice.ArgumentList.Add($"-dLastPage={seg.LocalEnd}");
                psiSlice.ArgumentList.Add($"-sOutputFile={slicePath}");
                psiSlice.ArgumentList.Add(seg.PdfPath);

                using var sliceProc = new Process { StartInfo = psiSlice };
                sliceProc.Start();
                sliceProc.BeginOutputReadLine();
                sliceProc.BeginErrorReadLine();
                await Task.Run(() => { while (!sliceProc.WaitForExit(200)) ct.ThrowIfCancellationRequested(); }, ct);

                inputFiles.Add((File.Exists(slicePath) ? slicePath : seg.PdfPath, seg.Rotation));
            }
        }

        // Combine all input PDFs in order into one normalised PDF.
        // pdfwrite burns rotation into page content so Audiveris PDFBox gives correct part names.
        bool uniformRotation = inputFiles.All(f => f.Rotation == inputFiles[0].Rotation);
        int commonRotation   = inputFiles[0].Rotation;

        progress?.Report($"Combining {inputFiles.Count} PDF(s) into a single normalised PDF for Audiveris…");

        var psi = new ProcessStartInfo
        {
            FileName = gsPath,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute  = false,
            CreateNoWindow   = true
        };
        psi.ArgumentList.Add("-dBATCH");
        psi.ArgumentList.Add("-dNOPAUSE");
        psi.ArgumentList.Add("-dSAFER");
        psi.ArgumentList.Add("-sDEVICE=pdfwrite");
        psi.ArgumentList.Add("-dCompressPages=true");
        psi.ArgumentList.Add("-dAutoRotatePages=/None");
        psi.ArgumentList.Add($"-sOutputFile={outputPdf}");
        if (uniformRotation && commonRotation != 0)
        {
            // Apply once before all input files
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"<</Orientation {commonRotation}>> setpagedevice");
            psi.ArgumentList.Add("-f");
            foreach (var (f, _) in inputFiles)
                psi.ArgumentList.Add(f);
        }
        else if (uniformRotation)
        {
            foreach (var (f, _) in inputFiles)
                psi.ArgumentList.Add(f);
        }
        else
        {
            // Mixed rotations: interleave per-file orientation snippets
            foreach (var (f, rot) in inputFiles)
            {
                if (rot != 0)
                {
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add($"<</Orientation {rot}>> setpagedevice");
                    psi.ArgumentList.Add("-f");
                }
                psi.ArgumentList.Add(f);
            }
        }

        Logger.LogInfo($"Ghostscript combine: {string.Join(" + ", inputFiles.Select(f => Path.GetFileName(f.Path)))} -> {Path.GetFileName(outputPdf)}");

        using var proc = new Process { StartInfo = psi };
        var errLines = new System.Text.StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) errLines.AppendLine(e.Data); };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                Logger.LogInfo($"[GS] {e.Data}");
                if (e.Data.Contains("Processing pages") || e.Data.Contains("Page "))
                    progress?.Report(e.Data.Trim());
            }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await Task.Run(() => { while (!proc.WaitForExit(200)) ct.ThrowIfCancellationRequested(); }, ct);

        if (proc.ExitCode != 0 || !File.Exists(outputPdf))
        {
            progress?.Report($"⚠ Ghostscript combine failed (exit {proc.ExitCode}) — falling back to first volume only.");
            Logger.LogInfo($"[GS] stderr: {errLines}");
            return segments[0].PdfPath;
        }

        var kb = new FileInfo(outputPdf).Length / 1024;
        progress?.Report($"Combined PDF: {kb:N0} KB  ({inputFiles.Count} volume(s), {Path.GetFileName(outputPdf)})");
        return outputPdf;
    }

    /// <summary>
    /// Uses Ghostscript to produce a normalised, page-range-sliced PDF from the input.
    /// Rotation is burned into the page content (pdfwrite + setpagedevice), so Audiveris's
    /// PDFBox reader sees a clean upright PDF with correct instrument names (Piano, not Voice).
    /// If GS fails the original PDF path is returned unchanged.
    /// </summary>
    private static async Task<string> NormalisePdfWithGhostscriptAsync(
        string gsPath,
        string pdfPath,
        string outputDir,
        IProgress<string>? progress,
        CancellationToken ct,
        int rotation = 0,
        int firstPage = 0,
        int lastPage = 0,
        int spinePaddingPx = 0)
    {
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        bool hasPageRange = firstPage > 0 && lastPage > 0;
        // Include the page range in the filename so different ranges don't collide in the cache.
        var rangeSuffix = hasPageRange ? $"_p{firstPage}-{lastPage}" : "";
        // Produce a normalised PDF — keeps Audiveris in its PDF codepath so part names are "Piano".
        var normalisedPath = Path.Combine(outputDir, baseName + rangeSuffix + "_gs.pdf");

        // rotation: 0=normal, 1=90°CW, 2=180°, 3=270°CW (matches PortableRotation enum)
        // Using pdfwrite burns the rotation into the page transform so no /Rotate entries survive.
        int gsOrientation = rotation;
        var rotationLabel = rotation == 0 ? "normal" : $"{rotation * 90}°";
        progress?.Report($"Normalising PDF with Ghostscript (rotation={rotationLabel}) for Audiveris\u2026");

        var psi = new ProcessStartInfo
        {
            FileName = gsPath,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute  = false,
            CreateNoWindow   = true
        };
        // pdfwrite: re-render each page into a clean PDF — preserves vector quality for Audiveris PDFBox.
        // -dAutoRotatePages=/None: prevents GS from overriding our explicit orientation.
        // -dCompressPages=true: keep file size reasonable.
        psi.ArgumentList.Add("-dBATCH");
        psi.ArgumentList.Add("-dNOPAUSE");
        psi.ArgumentList.Add("-dSAFER");
        psi.ArgumentList.Add("-sDEVICE=pdfwrite");
        psi.ArgumentList.Add("-dCompressPages=true");
        psi.ArgumentList.Add("-dAutoRotatePages=/None");
        if (hasPageRange)
        {
            // Slice only the requested pages — Audiveris then sees pages 1..N, not 31..35 in a 108-page file.
            psi.ArgumentList.Add($"-dFirstPage={firstPage}");
            psi.ArgumentList.Add($"-dLastPage={lastPage}");
        }
        psi.ArgumentList.Add($"-sOutputFile={normalisedPath}");

        // Build PostScript preamble combining rotation and/or spine padding.
        // We always use -c / -f when there is *any* PS to inject so GS processes it before the file.
        bool hasRotation = gsOrientation != 0;
        bool hasPadding  = spinePaddingPx > 0;
        if (hasRotation || hasPadding)
        {
            var ps = new System.Text.StringBuilder();

            if (hasRotation)
            {
                // Burn the rotation into each page's content stream via a PostScript snippet.
                ps.Append($"<</Orientation {gsOrientation}>> setpagedevice ");
            }

            if (hasPadding)
            {
                // Add white padding on the spine-side edge of each page to compensate for
                // gutter clipping from book-feeder scans.
                //   Even pages → right edge clipped → pad right  (content stays at x=0, white extends right)
                //   Odd  pages → left  edge clipped → pad left   (translate content right by SpinePad)
                //
                // KEY: We must NOT call setpagedevice inside a BeginPage callback — doing so
                // re-triggers BeginPage recursively causing /execstackoverflow.
                // Instead, set the widened media size upfront via GS command-line flags so
                // BeginPage only needs a translate for odd pages.
                var pageSize = TryReadPdfFirstPageSize(pdfPath);
                float origW = pageSize.HasValue ? pageSize.Value.Width  : 595f;
                float origH = pageSize.HasValue ? pageSize.Value.Height : 842f;
                float newW  = origW + spinePaddingPx;

                // Tell GS the output canvas is wider than the source pages.
                // -dFIXEDMEDIA prevents GS from resizing the canvas per page.
                psi.ArgumentList.Add($"-dDEVICEWIDTHPOINTS={newW:F4}");
                psi.ArgumentList.Add($"-dDEVICEHEIGHTPOINTS={origH:F4}");
                psi.ArgumentList.Add("-dFIXEDMEDIA");

                // BeginPage: for odd pages (right-hand page, gutter on left), shift content
                // right by SpinePad so whitespace appears on the left (binding) edge.
                // For even pages (left-hand, gutter on right) no translate needed — the
                // wider canvas already leaves white on the right.
                // PageCount in pdfwrite is 1-based at BeginPage time (already incremented),
                // so use it directly without adding 1.
                ps.Append(
                    $"/SpinePad {spinePaddingPx} def " +
                    $"<< /BeginPage {{ " +
                    $"  currentpagedevice /PageCount known " +
                    $"    {{ currentpagedevice /PageCount get }} " +
                    $"    {{ 1 }} " +
                    $"  ifelse " +
                    $"  2 mod 1 eq " + // true = odd page (right-hand) → shift content right
                    $"  {{ SpinePad 0 translate }} " +
                    $"  if " +
                    $"}} >> setpagedevice ");

                progress?.Report($"Spine padding enabled: {spinePaddingPx} pts on gutter edge per page (canvas widened from {origW:F0} to {newW:F0} pts).");
            }

            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(ps.ToString().Trim());
            psi.ArgumentList.Add("-f");
        }
        psi.ArgumentList.Add(pdfPath);

        Logger.LogInfo($"Ghostscript rasterize: {gsPath} -> {normalisedPath}");

        using var proc = new Process { StartInfo = psi };
        var errLines = new System.Text.StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) errLines.AppendLine(e.Data); };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                Logger.LogInfo($"[GS] {e.Data}");
                // Surface "Processing pages N through M" so the user sees page count
                if (e.Data.Contains("Processing pages") || e.Data.Contains("Page "))
                    progress?.Report(e.Data.Trim());
            }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await Task.Run(() =>
        {
            while (!proc.WaitForExit(200))
                ct.ThrowIfCancellationRequested();
        }, ct);

        if (proc.ExitCode != 0 || !File.Exists(normalisedPath))
        {
            progress?.Report($"\u26a0 Ghostscript normalisation failed (exit {proc.ExitCode}) \u2014 proceeding with original PDF.");
            var errText = errLines.ToString().Trim();
            if (!string.IsNullOrEmpty(errText))
                progress?.Report($"[GS stderr] {errText}");
            Logger.LogInfo($"[GS] stderr: {errLines}");
            return pdfPath;
        }

        var origKb = new FileInfo(pdfPath).Length  / 1024;
        var normKb = new FileInfo(normalisedPath).Length / 1024;
        progress?.Report($"PDF normalised: {origKb} KB \u2192 {normKb} KB (_gs.pdf, {(hasPageRange ? $"pages {firstPage}\u2013{lastPage}" : "all pages")})");
        return normalisedPath;
    }

    /// <summary>
    /// Reads the MediaBox of the first page from a PDF file without any external library.
    /// Returns width and height in PDF points (1/72 inch). Returns null if parsing fails.
    /// </summary>
    private static (float Width, float Height)? TryReadPdfFirstPageSize(string pdfPath)
    {
        try
        {
            // Read up to 128 KB — enough to find the first /MediaBox and /Rotate in most PDFs.
            const int maxBytes = 131072;
            using var fs = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int readLen = (int)Math.Min(fs.Length, maxBytes);
            var buf = new byte[readLen];
            _ = fs.Read(buf, 0, readLen);
            var text = System.Text.Encoding.Latin1.GetString(buf);

            // Match /MediaBox [ llx lly urx ury ]
            var m = System.Text.RegularExpressions.Regex.Match(
                text,
                @"/MediaBox\s*\[\s*([\d.+-]+)\s+([\d.+-]+)\s+([\d.+-]+)\s+([\d.+-]+)\s*\]");
            if (!m.Success) return null;

            float llx = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            float lly = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            float urx = float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            float ury = float.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
            float rawW = Math.Abs(urx - llx);
            float rawH = Math.Abs(ury - lly);

            // /Rotate 90 or 270 means the page is displayed with width and height swapped.
            // Only 90 and 270 swap dimensions; 0 and 180 do not.
            var rotMatch = System.Text.RegularExpressions.Regex.Match(text, @"/Rotate\s+(\d+)");
            int rotate = rotMatch.Success ? int.Parse(rotMatch.Groups[1].Value) : 0;
            rotate = ((rotate % 360) + 360) % 360; // normalise to 0-359
            bool swapAxes = rotate == 90 || rotate == 270;
            return swapAxes ? (rawH, rawW) : (rawW, rawH);
        }
        catch
        {
            return null;
        }
    }

    private static async Task RunAudiverisProcessAsync(
        string audiverisPath,
        Action<IList<string>> addArgs,
        IProgress<string>? progress,
        CancellationToken ct,
        bool ignoreExitCode)
    {
        ProcessStartInfo psi;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            audiverisPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(audiverisPath);
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = audiverisPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        addArgs(psi.ArgumentList);

        Logger.LogInfo($"Audiveris: {psi.FileName} {string.Join(" ", psi.ArgumentList)}");

        using var process = new Process { StartInfo = psi };
        var errorLines = new System.Text.StringBuilder();
        var sw = Stopwatch.StartNew();

        // Audiveris log format: "LEVEL  [BookName#N]  ClassName lineNum | message"
        // Each sheet gets its own thread name like [BookName#1], [BookName#2], etc.
        // We detect sheet transitions by watching for a new #N in the bracket.
        var bracketSheetRegex = new System.Text.RegularExpressions.Regex(
            @"\[.*?#(\d+)\]",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        int lastSheetSeen = 0;
        int pagesCompleted = 0;
        var sheetStartTimes = new System.Collections.Generic.Dictionary<int, TimeSpan>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;

            // Detect sheet number from bracket before trimming
            var bracketMatch = bracketSheetRegex.Match(e.Data);
            if (bracketMatch.Success && int.TryParse(bracketMatch.Groups[1].Value, out int sheetNum))
            {
                if (sheetNum != lastSheetSeen)
                {
                    // A new sheet has started processing
                    if (lastSheetSeen > 0 && sheetStartTimes.TryGetValue(lastSheetSeen, out var start))
                    {
                        // Previous sheet just finished (new sheet starting = previous done)
                        var sheetElapsed = sw.Elapsed - start;
                        pagesCompleted++;
                        progress?.Report($"  ✓ Sheet #{lastSheetSeen} done in {sheetElapsed.TotalSeconds:F1}s — {pagesCompleted} page(s) total, {sw.Elapsed:m\\:ss} elapsed");
                    }
                    lastSheetSeen = sheetNum;
                    sheetStartTimes[sheetNum] = sw.Elapsed;
                    progress?.Report($"  → Sheet #{sheetNum} started [{sw.Elapsed:m\\:ss} elapsed]");
                }
            }

            if (e.Data.Contains("INFO") || e.Data.Contains("WARN") || e.Data.Contains("ERROR"))
            {
                var trimmed = System.Text.RegularExpressions.Regex.Replace(e.Data, @"^\S+\s+\[\S*\]\s+\S+\s+\|\s*", "").Trim();
                if (trimmed.Length > 0) progress?.Report(trimmed);
            }
            Logger.LogInfo($"[Audiveris] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            errorLines.AppendLine(e.Data);
            Logger.LogInfo($"[Audiveris ERR] {e.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await Task.Run(() =>
        {
            while (!process.WaitForExit(200))
                ct.ThrowIfCancellationRequested();
            // Call no-arg WaitForExit to flush all pending OutputDataReceived callbacks
            // before we report the summary (avoids the summary appearing before the last lines).
            process.WaitForExit();
        }, ct);

        sw.Stop();

        // Count the last sheet if it was never followed by a new one
        if (lastSheetSeen > 0 && sheetStartTimes.TryGetValue(lastSheetSeen, out var lastStart))
        {
            var sheetElapsed = sw.Elapsed - lastStart;
            pagesCompleted++;
            progress?.Report($"  ✓ Sheet #{lastSheetSeen} done in {sheetElapsed.TotalSeconds:F1}s");
        }

        if (pagesCompleted > 0)
        {
            var pps = sw.Elapsed.TotalSeconds > 0 ? pagesCompleted / sw.Elapsed.TotalSeconds : 0;
            progress?.Report($"Pass complete — {pagesCompleted} page(s) in {sw.Elapsed:m\\:ss\\.f} ({pps:F2} pages/sec)");
        }
        else
        {
            progress?.Report($"Pass complete — {sw.Elapsed:m\\:ss\\.f} elapsed");
        }

        if (!ignoreExitCode && process.ExitCode != 0)
        {
            var err = errorLines.Length > 0 ? errorLines.ToString().Trim() : "(no stderr)";
            throw new InvalidOperationException(
                $"Audiveris exited with code {process.ExitCode}.\n{err}");
        }
    }

    /// <summary>
    /// Reads the exported MusicXML (plain or .mxl ZIP) and verifies it contains at least
    /// one &lt;note&gt; element.  Throws <see cref="InvalidOperationException"/> with an
    /// actionable message when the file exists but has no musical content.
    /// </summary>
    private static void ValidateMusicXmlHasNotes(string filePath)
    {
        try
        {
            string xml;

            if (filePath.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase))
            {
                // .mxl is a ZIP; pick the first score .xml entry (skip META-INF)
                using var zip = ZipFile.OpenRead(filePath);
                var scoreEntry = zip.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    !e.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase));
                if (scoreEntry == null) return; // can't validate, let MuseScore try
                using var reader = new StreamReader(scoreEntry.Open());
                xml = reader.ReadToEnd();
            }
            else
            {
                xml = File.ReadAllText(filePath);
            }

            var doc = XDocument.Parse(xml);
            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            bool hasNotes = doc.Descendants(ns + "note").Any()
                         || doc.Descendants("note").Any();

            if (!hasNotes)
            {
                int measureCount = doc.Descendants(ns + "measure").Count()
                                 + doc.Descendants("measure").Count();

                var detail = measureCount > 0
                    ? $"The file contains {measureCount} measure(s) but no notes — " +
                      "Audiveris may have recognised the page layout but could not decode the notation."
                    : "The file contains no measures or notes — " +
                      "the selected pages may be a title/cover page, or the scan quality may be too low for Audiveris to read.";

                throw new InvalidOperationException(
                    $"The exported MusicXML has no music content.\n\n{detail}\n\n" +
                    "Suggestions:\n" +
                    "• Make sure the selected page range covers pages with printed music staves.\n" +
                    "• Try a wider page range or use the full PDF.\n" +
                    "• Open the .omr file in Audiveris and check the transcription manually.");
            }
        }
        catch (InvalidOperationException)
        {
            throw; // re-throw our own validation error
        }
        catch (Exception ex)
        {
            // Parsing/IO errors: log and let MuseScore decide — don't block the user
            Logger.LogInfo($"[ValidateMusicXml] Could not validate {filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Injects (or replaces) a &lt;sound tempo="bpm"/&gt; element into the first measure of the
    /// given MusicXML file so that MuseScore opens it at the requested tempo instead of the
    /// default 120 BPM.  Handles both plain .xml/.musicxml and zipped .mxl files.
    /// </summary>
    public static void SetTempoInMusicXml(string filePath, int bpm, IProgress<string>? progress = null)
    {
        // bpm <= 0: skip tempo injection but still patch instrument names

        bool isMxl = filePath.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase);

        if (isMxl)
        {
            // .mxl is a ZIP; the score is the first .xml entry that is NOT META-INF/container.xml
            var tempPath = filePath + ".tmp";
            try
            {
                using (var zipRead = ZipFile.OpenRead(filePath))
                using (var zipWrite = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    foreach (var entry in zipRead.Entries)
                    {
                        var newEntry = zipWrite.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                        using var src = entry.Open();
                        using var dst = newEntry.Open();

                        bool isScoreXml = entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                                          && !entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase);
                        if (isScoreXml)
                        {
                            using var reader = new StreamReader(src);
                            var xml = reader.ReadToEnd();
                            var patched = bpm > 0 ? InjectTempoIntoXml(xml, bpm) : xml;
                            patched = PatchInstrumentNames(patched);
                            using var writer = new StreamWriter(dst);
                            writer.Write(patched);
                        }
                        else
                        {
                            src.CopyTo(dst);
                        }
                    }
                }
                File.Delete(filePath);
                File.Move(tempPath, filePath);
                progress?.Report($"Tempo set to {bpm} BPM in exported score.");
            }
            catch (Exception ex)
            {
                progress?.Report($"⚠ Could not set tempo: {ex.Message}");
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        else
        {
            try
            {
                var xml = File.ReadAllText(filePath);
                var patched = bpm > 0 ? InjectTempoIntoXml(xml, bpm) : xml;
                patched = PatchInstrumentNames(patched);
                File.WriteAllText(filePath, patched);
                progress?.Report($"Tempo set to {bpm} BPM in exported score.");
            }
            catch (Exception ex)
            {
                progress?.Report($"⚠ Could not set tempo: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patches part names in MusicXML: any part whose name is generic ("Voice", "voice", empty,
    /// or a bare "Part N" string) is renamed to "Piano" and its MIDI program is set to 1 (Acoustic Grand Piano).
    /// Audiveris uses "Voice" when processing image-based input (TIFF) instead of PDF.
    /// </summary>
    public static string PatchInstrumentNames(string xml)
    {
        var doc = XDocument.Parse(xml);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        bool IsGenericName(string? name) =>
            string.IsNullOrWhiteSpace(name) ||
            string.Equals(name, "Voice", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "voice", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(name!.Trim(), @"^Part\s*\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var partName in doc.Descendants(ns + "part-name").Concat(doc.Descendants("part-name")))
        {
            if (IsGenericName(partName.Value))
                partName.Value = "Piano";
        }
        foreach (var partAbbr in doc.Descendants(ns + "part-abbreviation").Concat(doc.Descendants("part-abbreviation")))
        {
            if (IsGenericName(partAbbr.Value))
                partAbbr.Value = "Pno.";
        }
        foreach (var instName in doc.Descendants(ns + "instrument-name").Concat(doc.Descendants("instrument-name")))
        {
            if (IsGenericName(instName.Value))
                instName.Value = "Piano";
        }
        // Ensure MIDI program is set to 1 (Acoustic Grand Piano) for all parts
        foreach (var midiProg in doc.Descendants(ns + "midi-program").Concat(doc.Descendants("midi-program")))
            midiProg.Value = "1";

        return doc.ToString(SaveOptions.OmitDuplicateNamespaces);
    }

    /// <summary>
    /// Injects &lt;sound tempo="bpm"/&gt; as the first child of the first &lt;measure&gt; element.
    /// Removes any existing &lt;sound tempo=...&gt; elements in that measure first.
    /// </summary>
    private static string InjectTempoIntoXml(string xml, int bpm)
    {
        var doc = XDocument.Parse(xml);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        // Find the first <measure> across any <part>
        var firstMeasure = doc.Descendants(ns + "measure").FirstOrDefault()
                        ?? doc.Descendants("measure").FirstOrDefault();
        if (firstMeasure == null) return xml;

        // Remove any existing <sound tempo=...> in this measure
        firstMeasure.Elements(ns + "sound")
            .Where(e => e.Attribute("tempo") != null)
            .ToList()
            .ForEach(e => e.Remove());
        firstMeasure.Elements("sound")
            .Where(e => e.Attribute("tempo") != null)
            .ToList()
            .ForEach(e => e.Remove());

        // Insert <sound tempo="bpm"/> as first child
        var soundEl = new XElement(ns + "sound", new XAttribute("tempo", bpm.ToString()));
        firstMeasure.AddFirst(soundEl);

        return doc.ToString(SaveOptions.OmitDuplicateNamespaces);
    }

    /// <summary>
    /// Launches MuseScore Studio with the given file.
    /// </summary>
    public static void LaunchMuseScore(string museScorePath, string filePath)
    {
        if (!File.Exists(museScorePath))
            throw new FileNotFoundException($"MuseScore not found at: {museScorePath}");
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Output file not found: {filePath}");

        var psi = new ProcessStartInfo
        {
            FileName = museScorePath,
            Arguments = $"\"{filePath}\"",
            UseShellExecute = true
        };

        Logger.LogInfo($"Launching MuseScore: {museScorePath} \"{filePath}\"");
        Process.Start(psi);
    }
}
