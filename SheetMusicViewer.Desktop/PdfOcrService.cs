using PDFtoImage;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tesseract;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Per-page result from <see cref="PdfOcrService.ExtractAsync"/>.
/// <para><see cref="RawText"/> is the full OCR dump of the top strip (for inspection).</para>
/// <para><see cref="LargeFontText"/> contains only words whose bounding-box height is at or near
/// the tallest word on the page — i.e. the dominant (title-sized) font — used for TOC heuristics.</para>
/// <para><see cref="VolumeFileName"/> is the file name (not full path) of the PDF volume this page belongs to.
/// For single-PDF books this is always the same; for multi-volume sets it changes at each volume boundary.</para>
/// </summary>
public sealed record PdfPageOcrResult(int PageIndex, string RawText, string LargeFontText, string VolumeFileName = "");

/// <summary>
/// Renders each page of a PDF, applies rotation, crops the top strip, and runs
/// Tesseract OCR to produce raw text per page.  Then generates a suggested JSON TOC.
///
/// Cross-platform: works on Windows, Linux, and macOS.
///
/// PREREQUISITE: Tesseract language data must be present.
/// Place the eng.traineddata file (download from
/// https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata)
/// into a folder called "tessdata" next to the application executable,
/// or set the TESSDATA_PREFIX environment variable to point at your tessdata directory.
/// </summary>
public static class PdfOcrService
{
    /// <summary>
    /// Render-DPI used for each page.  150 DPI gives a good OCR hit-rate without
    /// being too slow.
    /// </summary>
    public const int RenderDpi = 150;

    // -----------------------------------------------------------------
    // Tessdata resolution
    // -----------------------------------------------------------------

    /// <summary>
    /// Returns the tessdata directory to use, searching (in order):
    ///   1. TESSDATA_PREFIX environment variable
    ///   2. "tessdata" folder next to the running assembly
    ///   3. Current working directory / tessdata
    /// Throws <see cref="DirectoryNotFoundException"/> with a helpful message if none found.
    /// </summary>
    public static string ResolveTessDataDir()
    {
        var envVar = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (!string.IsNullOrEmpty(envVar) && Directory.Exists(envVar))
            return envVar;

        var nextToExe = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath ?? ".") ?? ".",
            "tessdata");
        if (Directory.Exists(nextToExe))
            return nextToExe;

        var cwd = Path.Combine(Directory.GetCurrentDirectory(), "tessdata");
        if (Directory.Exists(cwd))
            return cwd;

        throw new DirectoryNotFoundException(
            "Tesseract language data not found.\n" +
            "Create a 'tessdata' folder next to the application executable and " +
            "place eng.traineddata inside it.\n" +
            "Download from: https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata\n" +
            $"Searched: {nextToExe}");
    }

    // -----------------------------------------------------------------
    // Main entry point
    // -----------------------------------------------------------------

    /// <summary>
    /// Represents one volume in a multi-volume PDF set.
    /// <para><see cref="PageCount"/> is taken from the JSON sidecar when available so
    /// <c>GetPageCount</c> does not need to be called.  A value of <c>-1</c> means
    /// unknown — the page count will be derived from the PDF bytes at runtime.</para>
    /// </summary>
    private sealed record VolumeSpec(string PdfPath, int Rotation, int PageCount = -1);

    /// <summary>
    /// Runs the full OCR pipeline across one or more PDF volumes.
    ///
    /// <para>Multi-volume detection (checked in order):</para>
    /// <list type="number">
    ///   <item>If <paramref name="pathOrJson"/> ends with <c>.json</c>, load it directly.</item>
    ///   <item>If a sibling <c>.json</c> file exists next to the given PDF, load that.</item>
    ///   <item>Otherwise treat <paramref name="pathOrJson"/> as a single PDF and use the
    ///       supplied <paramref name="defaultRotation"/>.</item>
    /// </list>
    ///
    /// <para>When a JSON sidecar is found its <c>volumes</c> array drives the list of PDFs
    /// and per-volume rotations.  All volumes are concatenated into one result list with a
    /// monotonically increasing global <see cref="PdfPageOcrResult.PageIndex"/>.</para>
    ///
    /// <para>Progress is reported as (globalPageIndex, totalPages).</para>
    /// </summary>
    public static async Task<List<PdfPageOcrResult>> ExtractAsync(
        string pathOrJson,
        int defaultRotation = 0,
        IProgress<(int Page, int Total)>? progress = null,
        CancellationToken cancellationToken = default,
        Action<string>? logger = null)
    {
        var volumes = ResolveVolumes(pathOrJson, defaultRotation);

        var tessDataDir = ResolveTessDataDir();

        // TesseractEngine is not thread-safe; create once and reuse sequentially.
        using var engine = new TesseractEngine(tessDataDir, "eng", EngineMode.Default);

        // Sum page counts from the JSON sidecar where available so we never need to
        // open a PDF just to count pages.  Any volume whose count is unknown (-1)
        // will be resolved after ReadAllBytes below.
        int totalPages = volumes.All(v => v.PageCount >= 0)
            ? volumes.Sum(v => v.PageCount)
            : 0;

        // ── Phase 1: OCR every page, collect raw text + word boxes ────────────
        // We do NOT apply the title-font threshold yet — it must be computed
        // document-wide so a title font seen on page 1 is recognised on page 50.
        var rawData   = new List<(PageOcrData Data, string VolumeFileName, int GlobalIndex)>();
        int globalIndex = 0;
        foreach (var vol in volumes)
        {
            var pdfBytes = File.ReadAllBytes(vol.PdfPath);
            int volPageCount = vol.PageCount >= 0
                ? vol.PageCount
                : Conversion.GetPageCount(new MemoryStream(pdfBytes));

            var volFileName = Path.GetFileName(vol.PdfPath);

            for (int i = 0; i < volPageCount; i++, globalIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((globalIndex, totalPages));

                var data = await Task.Run(
                    () => OcrPage(pdfBytes, i, vol.Rotation, engine),
                    cancellationToken);

                rawData.Add((data, volFileName, globalIndex));
            }
        }

        // ── Phase 2: compute ONE title threshold across the whole document ─────
        int titleThreshold = ComputeDocumentTitleThreshold(
            rawData.Select(r => (r.Data.Words, r.GlobalIndex)), logger);
        logger?.Invoke($"[OCR] Title-font threshold: {titleThreshold}px");

        // ── Phase 3: apply threshold to each page's word boxes ────────────────
        var results = new List<PdfPageOcrResult>(rawData.Count);
        foreach (var (data, volFileName, idx) in rawData)
        {
            var largeFontText = AssembleLargeFontText(data.Words, titleThreshold);
            results.Add(new PdfPageOcrResult(idx, data.RawText, largeFontText, volFileName));
            logger?.Invoke($"[OCR] Page {idx} large-font: {largeFontText.Replace("\n", " | ")}");
        }

        return results;
    }

    /// <summary>
    /// Resolves the list of PDF volumes to process from a path that may be:
    ///   • a <c>.json</c> sidecar file (multi-volume aware),
    ///   • a <c>.pdf</c> with a sibling <c>.json</c> sidecar,
    ///   • a plain <c>.pdf</c> with no sidecar.
    /// </summary>
    private static List<VolumeSpec> ResolveVolumes(string pathOrJson, int defaultRotation)
    {
        string? jsonPath = null;

        if (pathOrJson.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            jsonPath = pathOrJson;
        }
        else
        {
            // Look for a sibling JSON next to the PDF.
            var sibling = Path.ChangeExtension(pathOrJson, ".json");
            if (File.Exists(sibling))
                jsonPath = sibling;
        }

        if (jsonPath != null && File.Exists(jsonPath))
        {
            var baseDir = Path.GetDirectoryName(jsonPath) ?? ".";
            using var stream = File.OpenRead(jsonPath);
            var bmk = JsonSerializer.Deserialize<SheetMusicLib.BmkJsonFormat>(stream);

            if (bmk?.Volumes is { Count: > 0 } vols)
            {
                var list = new List<VolumeSpec>(vols.Count);
                foreach (var v in vols)
                {
                    if (string.IsNullOrWhiteSpace(v.FileName)) continue;
                    var fullPath = Path.IsPathRooted(v.FileName)
                        ? v.FileName
                        : Path.Combine(baseDir, v.FileName);
                    if (File.Exists(fullPath))
                        list.Add(new VolumeSpec(fullPath, v.Rotation, v.PageCount));
                    else
                        Debug.WriteLine($"[OCR] Volume not found, skipping: {fullPath}");
                }
                if (list.Count > 0) return list;
            }
        }

        // Fall back: single PDF with the provided default rotation.
        return [new VolumeSpec(pathOrJson, defaultRotation)];
    }

    // -----------------------------------------------------------------
    // Formatting helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Builds a human-readable report showing the full OCR text for every page,
    /// followed by the large-font words extracted for TOC heuristics.
    /// </summary>
    public static string FormatRawText(IReadOnlyList<PdfPageOcrResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== OCR raw text — {results.Count} pages ===");
        string lastVolume = string.Empty;
        foreach (var r in results)
        {
            if (r.VolumeFileName != lastVolume)
            {
                lastVolume = r.VolumeFileName;
                sb.AppendLine();
                sb.AppendLine($"=== Volume: {lastVolume} ===");
            }
            sb.AppendLine();
            sb.AppendLine($"--- Page {r.PageIndex} ---");
            sb.AppendLine(r.RawText);
            if (!string.IsNullOrWhiteSpace(r.LargeFontText))
                sb.AppendLine($"  [large-font] {r.LargeFontText.Replace("\n", " | ")}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Heuristically identifies which pages are likely "title" pages (start of a new
    /// piece) and emits them as a suggested JSON TOC array for review.
    /// Strategy: pages whose OCR text is short (sparse = title page) and contains
    /// enough letter characters.
    /// </summary>
    /// <param name="pageNumberOffset">
    /// The book's <c>PageNumberOffset</c>: the printed page number of the first physical
    /// page (0 when not set).  Added to every raw page index so the generated JSON uses
    /// the same page-number space as the rest of the TOC.
    /// </param>
    public static string FormatSuggestedJson(
        IReadOnlyList<PdfPageOcrResult> results,
        int pageNumberOffset = 0,
        Action<string>? logger = null)
    {
        var candidates = new List<(int Page, string Name)>();
        int totalPages = results.Count;
        // How many front-matter and back-matter pages to skip.
        // Front: cover + table-of-contents pages are typically within the first ~5% of pages.
        // Back: back-cover ads are typically the last 1-2 pages.
        int skipFront = Math.Max(3, (int)Math.Ceiling(totalPages * 0.05));
        int skipBack  = Math.Max(1, (int)Math.Ceiling(totalPages * 0.02));

        foreach (var r in results)
        {
            // Skip front-matter (covers, TOC pages) and back-matter (ads).
            if (r.PageIndex < skipFront || r.PageIndex >= totalPages - skipBack)
            {
                logger?.Invoke($"[TOC] Page {r.PageIndex,3} SKIP (front/back matter)");
                continue;
            }

            // Use the large-font words only — filters out small musical annotations.
            // Only consider the FIRST line: the song title always appears on the first
            // large-font line; subsequent lines are continuation score glyphs at the
            // same height tier (brackets, clefs, ornaments) and should be ignored.
            var text = r.LargeFontText;
            if (string.IsNullOrWhiteSpace(text)) continue;

            var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
            var clean = CleanOcrText(firstLine);
            string? rejectReason = TitleRejectReason(clean);
            if (rejectReason is not null)
            {
                logger?.Invoke($"[TOC] Page {r.PageIndex,3} REJECT ({rejectReason}): {clean}");
                continue;
            }
            var title = TrimToTitle(clean);
            logger?.Invoke($"[TOC] Page {r.PageIndex,3} ACCEPT: {title}");
            candidates.Add((r.PageIndex + pageNumberOffset, title));
        }

        var tocArray = new List<object>();
        foreach (var (page, name) in candidates)
            tocArray.Add(new { songName = name, pageNo = page, composer = "" });

        var opts = new JsonSerializerOptions { WriteIndented = true };
        return $"=== Suggested TOC (review and edit — PageNumberOffset={pageNumberOffset}) ===\n\n" +
               JsonSerializer.Serialize(tocArray, opts);
    }

    // -----------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------

    /// <summary>A single OCR word with its bounding-box position and height.</summary>
    private readonly record struct WordBox(string Text, int Height, int Top, int Left);

    /// <summary>
    /// Raw OCR output for one page: full text and every word box (≥ 2 letters).
    /// The title threshold is computed document-wide after all pages are processed.
    /// </summary>
    private sealed record PageOcrData(int pageNo, int heightMax, string RawText, List<WordBox> Words);

    private static PageOcrData OcrPage(
        byte[] pdfBytes, int pageIndex, int rotation, TesseractEngine engine)
    {
        // 1. Render the full page to SKBitmap — wrap pre-loaded bytes in a MemoryStream
        //    (no file I/O here; the PDF was read once per volume by the caller).
        using var pdfStream = new MemoryStream(pdfBytes);
        using var raw = Conversion.ToImage(
            pdfStream,
            page: (Index)pageIndex,
            options: new RenderOptions(Dpi: RenderDpi));

        // 2. Rotate
        using var rotated = ApplyRotation(raw, rotation);

        // 3. Encode the full page to PNG bytes
        using var ms = new MemoryStream();
        using (var img = SKImage.FromBitmap(rotated))
        using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            data.SaveTo(ms);

        // 4. Tesseract OCR on full page
        using var pix  = Pix.LoadFromMemory(ms.ToArray());
        using var page = engine.Process(pix);
        var rawText = page.GetText()?.Trim() ?? string.Empty;

        // 5. Collect word boxes — title threshold is applied document-wide later.
        var words = CollectWordBoxes(pageIndex, page);
        int heightMax = words.Count > 0 ? words.Max(w => w.Height) : 0;
        return new PageOcrData(pageIndex, heightMax, rawText, words);
    }

    /// <summary>
    /// Collects every OCR word on the page that has ≥ 2 letters, returning its
    /// bounding-box dimensions.  Pure-symbol glyphs and single-char noise are excluded.
    /// </summary>
    private static List<WordBox> CollectWordBoxes(int pageIndex, Page tessPage)
    {
        var words = new List<WordBox>();
        using var iter = tessPage.GetIterator();
        iter.Begin();
        do
        {
            if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out var bbox)) continue;
            var text = iter.GetText(PageIteratorLevel.Word)?.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            int h = bbox.Y2 - bbox.Y1;
            if (h > 0 && LetterCount(text) >= 2)
                words.Add(new WordBox(text, h, bbox.Y1, bbox.X1));
        }
        while (iter.Next(PageIteratorLevel.Word));
        words = words.OrderBy(w => w.Height).ToList();
        return words;
    }

    /// <summary>
    /// Computes a single title-font height threshold from ALL word boxes across ALL
    /// pages in the document using the modal-body-text heuristic.
    ///
    /// Algorithm:
    ///  1. Collect all word heights document-wide, grouped by distinct page count.
    ///  2. Collapse heights into ±3px bands (OCR measurement variance).
    ///  3. The modal band (most pages) is the body-text tier.
    ///  4. The title threshold is 1.4× the body-text band height.
    ///  5. Return -1 if no band above the threshold appears on ≥ 2 pages.
    ///
    /// Using document-wide data means a title font seen only on the first page of each
    /// piece is correctly recognised on continuation pages where no title appears.
    /// </summary>
    private static int ComputeDocumentTitleThreshold(
        IEnumerable<(List<WordBox> Words, int PageIndex)> allPageWords,
        Action<string>? logger = null)
    {
        // Build height → distinct-page-count map.
        var heightPages = new Dictionary<int, HashSet<int>>();
        foreach (var (words, pageIdx) in allPageWords)
            foreach (var w in words)
            {
                if (!heightPages.TryGetValue(w.Height, out var set))
                    heightPages[w.Height] = set = [];
                set.Add(pageIdx);
            }

        // Step 1: Collapse nearby heights into bands (±BandTolerance px).
        // OCR bounding-box measurements vary by 1-3px for the same physical font
        // across pages due to image quality, compression and rendering differences.
        const int BandTolerance = 3;
        var allHeights = heightPages.Keys.OrderByDescending(h => h).ToList();
        var bands = new List<(int MaxH, int PageCount)>();

        for (int i = 0; i < allHeights.Count; )
        {
            int bandTop = allHeights[i];
            var pagesInBand = new HashSet<int>(heightPages[bandTop]);
            int j = i + 1;
            while (j < allHeights.Count && bandTop - allHeights[j] <= BandTolerance)
            {
                foreach (var p in heightPages[allHeights[j]]) pagesInBand.Add(p);
                j++;
            }
            bands.Add((bandTop, pagesInBand.Count));
            i = j;
        }

        // Step 2: Find the dominant body-text band — the one with the highest page count.
        // On a typical music score, small body text (fingerings, dynamics, lyrics)
        // appears on nearly every page, while title text appears only on the first
        // page of each piece.  The modal band is almost always the body-text tier.
        if (bands.Count == 0) return -1;
        var bodyBand = bands.MaxBy(b => b.PageCount);

        // Diagnostic: log the band histogram so the threshold choice is traceable.
        logger?.Invoke($"[OCR] Band histogram ({bands.Count} bands); body-text band = {bodyBand.MaxH}px on {bodyBand.PageCount} pages:");
        foreach (var (maxH, pageCount) in bands.OrderByDescending(b => b.MaxH))
            logger?.Invoke($"  {maxH,4}px  {pageCount,3} pages" + (maxH == bodyBand.MaxH ? "  \u2190 body-text (modal)" : string.Empty));

        // Step 3: The title threshold is TitleRatio × body-text height.
        // Any word significantly taller than the body-text tier is a candidate title.
        // A ratio of 1.4 (40% taller) gives a clean separation for typical sheet-music
        // layouts where titles are set in a noticeably larger font than dynamics/fingering.
        const double TitleRatio = 1.4;
        int threshold = (int)Math.Ceiling(bodyBand.MaxH * TitleRatio);

        // Sanity check: if no band above the threshold has at least 2 distinct pages,
        // there is no consistent title font in this document.
        bool anyTitle = bands.Any(b => b.MaxH >= threshold && b.PageCount >= 2);
        int result = anyTitle ? threshold : -1;
        logger?.Invoke($"[OCR] Title threshold = {result}px (TitleRatio={TitleRatio}, bodyBand={bodyBand.MaxH}px)");
        return result;
    }

    /// <summary>
    /// Given a set of word boxes for one page and a pre-computed document-level title
    /// threshold, returns the assembled title-tier text for that page (or empty string
    /// if no words meet the threshold).
    /// Words are grouped into lines by Y proximity and sorted left-to-right within each line.
    /// </summary>
    private static string AssembleLargeFontText(List<WordBox> words, int titleThreshold)
    {
        if (titleThreshold < 0) return string.Empty;

        var large = words
            .Where(w => w.Height >= titleThreshold)
            .OrderBy(w => w.Top).ThenBy(w => w.Left)
            .ToList();

        if (large.Count == 0) return string.Empty;

        // Group into lines: words within half a line-height of each other share a line.
        var lines = new List<List<(string Text, int Left)>>();
        var currentLine = new List<(string Text, int Left)> { (large[0].Text, large[0].Left) };
        int currentTop  = large[0].Top;
        int currentH    = large[0].Height;

        for (int i = 1; i < large.Count; i++)
        {
            var w = large[i];
            if (Math.Abs(w.Top - currentTop) <= currentH / 2)
            {
                currentLine.Add((w.Text, w.Left));
            }
            else
            {
                currentLine.Sort((a, b) => a.Left.CompareTo(b.Left));
                lines.Add(currentLine);
                currentLine = new List<(string Text, int Left)> { (w.Text, w.Left) };
                currentTop  = w.Top;
                currentH    = w.Height;
            }
        }
        if (currentLine.Count > 0)
        {
            currentLine.Sort((a, b) => a.Left.CompareTo(b.Left));
            lines.Add(currentLine);
        }

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(string.Join(" ", line.Select(x => x.Text)));
        }
        return sb.ToString();
    }

    private static SKBitmap ApplyRotation(SKBitmap src, int rotation)
    {
        if (rotation == 0) return src.Copy();
        bool swap = rotation is 1 or 3;
        int w = swap ? src.Height : src.Width;
        int h = swap ? src.Width  : src.Height;
        var dst = new SKBitmap(w, h);
        using var canvas = new SKCanvas(dst);
        canvas.Translate(w / 2f, h / 2f);
        canvas.RotateDegrees(rotation * 90f);
        canvas.Translate(-src.Width / 2f, -src.Height / 2f);
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }

    private static string CleanOcrText(string raw) =>
        raw.Replace("\r", " ").Replace("\n", " ").Trim();

    private static bool HasEnoughLetters(string text)
    {
        int letters = 0;
        foreach (char c in text)
            if (char.IsLetter(c)) letters++;
        return letters >= 5;
    }

    /// <summary>
    /// Returns a short rejection reason string if <paramref name="text"/> is NOT a
    /// plausible song/piece title, or <see langword="null"/> if it is a good candidate.
    ///
    /// Quality rules (all must pass):
    ///  • At least 5 letter characters total (basic noise gate; "Valse" = 5 letters).
    ///  • At least one token with ≥ 5 letters (guards against fragments like "BVa", "ap").
    ///  • Not purely parenthesised — rejects tempo/style markings like "(Vivace)",
    ///    "(Posthumous)" regardless of token count.
    ///  • ≥ 50% of space-separated tokens are "real words" (≥ 4 letters), OR the text
    ///    has exactly 1–2 tokens and the longest is ≥ 5 letters (single-word titles like
    ///    "Valse" or "Walzer" are valid).
    /// </summary>
    private static string? TitleRejectReason(string text)
    {
        // Total-letter gate (≥ 5 so "Valse" passes).
        int totalLetters = 0;
        foreach (char c in text) if (char.IsLetter(c)) totalLetters++;
        if (totalLetters < 5)
            return "<5 letters";

        // Reject strings that are entirely parenthesised (tempo/style markings).
        // Check this before the short-token fast-path so "(Vivace)" is caught.
        var trimmed = text.Trim();
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
            return "parenthesised tempo/style marking";

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Must have at least one token with ≥ 5 letters.
        int maxWordLen = tokens.Max(t => LetterCount(t));
        if (maxWordLen < 5)
            return $"longest word only {maxWordLen} letters";

        // Single/double-token path: fine as long as the longest token qualifies
        // AND the first token starts with an uppercase letter.
        // This guards against lowercase score-notation fragments like "gosten",
        // "datas", "ines" that pass the letter-count gate but are clearly not titles.
        if (tokens.Length <= 2)
        {
            char firstChar = tokens[0].FirstOrDefault(char.IsLetter);
            if (firstChar != '\0' && char.IsLower(firstChar))
                return $"starts lowercase ('{tokens[0]}')";
            return null;
        }

        // Multi-token: at least half must be "real" (≥ 4 letters).
        int realWords = tokens.Count(t => LetterCount(t) >= 4);
        double ratio = (double)realWords / tokens.Length;
        if (ratio < 0.5)
            return $"only {realWords}/{tokens.Length} real words ({ratio:P0})";

        return null;
    }

    private static int LetterCount(string text)
    {
        int n = 0;
        foreach (char c in text) if (char.IsLetter(c)) n++;
        return n;
    }

    private static string TrimToTitle(string clean)
    {
        if (clean.Length > 80) clean = clean[..80];
        // Strip leading page-number artefacts like "4 " or "12 "
        int i = 0;
        while (i < clean.Length && (char.IsDigit(clean[i]) || char.IsWhiteSpace(clean[i])))
            i++;
        return clean[i..].Trim();
    }
}
