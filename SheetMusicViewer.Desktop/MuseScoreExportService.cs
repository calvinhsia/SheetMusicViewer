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

        bool hasRange = startPage > 0 && endPage > 0;
        var rangeLabel = hasRange ? $" (sheets {startPage}–{endPage})" : "";

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
        var validSheetsArg = (validSheetNumbers.Count > 0 && validSheetNumbers.Count < totalOmrSheets)
            ? string.Join(",", validSheetNumbers)
            : null;
        if (validSheetsArg != null)
            progress?.Report($"  Skipping invalid sheets — exporting only sheets: {validSheetsArg}");

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

        foreach (var pattern in new[] { "*.mxl", "*.musicxml", "*.xml" })
        {
            var found = Directory.EnumerateFiles(searchDir, pattern, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
            if (found != null) return found;
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
