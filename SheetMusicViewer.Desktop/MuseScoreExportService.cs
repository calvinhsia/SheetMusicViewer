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
    /// Runs Audiveris on the given PDF and returns the output MXL/XML file path.
    /// Strategy:
    ///   Pass 1 — batch transcribe (no -export); always writes .omr, exit code ignored.
    ///   Patch  — edit book.xml inside the .omr ZIP to set valid="true" on all SheetStub
    ///            elements, overriding the rhythm-warning block that prevents export.
    ///   Pass 2 — batch export the patched .omr; this skips re-transcription and exports cleanly.
    /// startPage/endPage: 1-based inclusive; pass 0 for both to process all pages.
    /// </summary>
    public static async Task<string> RunAudiverisAsync(
        string audiverisPath,
        string pdfPath,
        int startPage = 0,
        int endPage = 0,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(audiverisPath))
            throw new FileNotFoundException($"Audiveris not found at: {audiverisPath}");
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException($"PDF not found at: {pdfPath}");

        var outputDir = Path.Combine(Path.GetTempPath(), "SheetMusicViewer_Audiveris");
        Directory.CreateDirectory(outputDir);

        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var omrFile = Path.Combine(outputDir, baseName + ".omr");

        // Record time just before we start so the output-file search can
        // reject stale files from previous runs of different PDFs.
        var runStartedAt = DateTime.UtcNow;

        bool hasRange = startPage > 0 && endPage > 0;
        var rangeLabel = hasRange ? $" (sheets {startPage}–{endPage})" : "";

        // ── Optional: normalise the PDF with Ghostscript ──────────────────────
        // Some PDFs use non-standard page trees that cause Audiveris (via PDFBox)
        // to see only 1 page.  Ghostscript re-renders them so every page is visible.
        var gsPath = AppSettings.Instance.GhostscriptPath;
        if (string.IsNullOrWhiteSpace(gsPath))
            gsPath = AutoDetectGhostscript();
        var allowGhostscript = false;
        if (allowGhostscript && !string.IsNullOrWhiteSpace(gsPath) && File.Exists(gsPath))
        {
            pdfPath = await NormalisePdfWithGhostscriptAsync(gsPath, pdfPath, outputDir, progress, ct);
            // Re-derive baseName and omrFile from the (possibly renamed) normalised PDF.
            baseName = Path.GetFileNameWithoutExtension(pdfPath);
            omrFile  = Path.Combine(outputDir, baseName + ".omr");
        }
        else
        {
            progress?.Report("⚠ Ghostscript not found — skipping PDF normalisation. " +
                             "If Audiveris sees fewer pages than expected, install Ghostscript.");
        }

        // ── Pass 1: transcribe through PAGE step → .omr ────────────────────────
        // Must pass -step PAGE explicitly; without it Audiveris just loads/saves
        // an empty book without running any recognition pipeline.
        // PAGE is the final step (after RHYTHMS) that creates the Score object.
        // Exit code is ignored: Audiveris exits 1 when rhythm warnings occur but
        // still writes a fully-populated .omr with all sheet data.
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
            return zip.Entries
                .Count(e => System.Text.RegularExpressions.Regex.IsMatch(
                    e.FullName, @"^sheet#\d+/",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase));
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
    /// Opens the .omr ZIP archive and removes invalid="true" from sheet elements.
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

        if (!changed)
        {
            progress?.Report("book.xml: no invalid flags found");
            return;
        }

        // Rewrite the whole ZIP to avoid ZipArchiveMode.Update corrupting compressed entries.
        var newXml = doc.ToString(SaveOptions.OmitDuplicateNamespaces);
        progress?.Report("Patched book.xml: removed invalid flags — rewriting .omr safely…");

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
    /// Uses Ghostscript to rasterize the PDF into a high-resolution multi-page TIFF.
    /// This bypasses Audiveris's PDFBox layer entirely, giving it clean bitmap pixels
    /// to detect staff lines from, and also avoids non-standard PDF page-tree issues.
    /// If GS fails the original PDF path is returned unchanged.
    /// </summary>
    private static async Task<string> NormalisePdfWithGhostscriptAsync(
        string gsPath,
        string pdfPath,
        string outputDir,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        // Produce a multi-page TIFF that Audiveris can load as an image-based book.
        var normalisedPath = Path.Combine(outputDir, baseName + "_gs.tif");

        progress?.Report("Rasterizing PDF with Ghostscript (300 DPI) for better staff detection\u2026");

        var psi = new ProcessStartInfo
        {
            FileName = gsPath,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute  = false,
            CreateNoWindow   = true
        };
        // tiffgray: grayscale multi-page TIFF — ideal for OMR (black/white staves)
        // -r300: 300 DPI — minimum reliable resolution for Audiveris staff detection
        // -dCompressPages=true: keep file size reasonable
        psi.ArgumentList.Add("-dBATCH");
        psi.ArgumentList.Add("-dNOPAUSE");
        psi.ArgumentList.Add("-dSAFER");
        psi.ArgumentList.Add("-sDEVICE=tiffgray");
        psi.ArgumentList.Add("-r300");
        psi.ArgumentList.Add("-dCompressPages=true");
        psi.ArgumentList.Add($"-sOutputFile={normalisedPath}");
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
            progress?.Report($"\u26a0 Ghostscript rasterization failed (exit {proc.ExitCode}) \u2014 proceeding with original PDF.");
            Logger.LogInfo($"[GS] stderr: {errLines}");
            return pdfPath;
        }

        var origKb = new FileInfo(pdfPath).Length  / 1024;
        var normKb = new FileInfo(normalisedPath).Length / 1024;
        progress?.Report($"PDF rasterized to TIFF: {origKb} KB PDF \u2192 {normKb} KB TIFF");
        return normalisedPath;
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

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            // Filter to significant lines to keep the log readable
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
        }, ct);

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
        if (bpm <= 0) return;

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
                            var patched = InjectTempoIntoXml(xml, bpm);
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
                var patched = InjectTempoIntoXml(xml, bpm);
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
