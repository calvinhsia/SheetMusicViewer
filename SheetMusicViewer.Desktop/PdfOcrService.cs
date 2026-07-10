using PDFtoImage;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tesseract;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Per-page result from <see cref="PdfOcrService.ExtractAsync"/>.
/// </summary>
public sealed record PdfPageOcrResult(int PageIndex, string RawText);

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

    /// <summary>
    /// Fraction of the (rotated) page height kept for OCR.
    /// 0.20 = top 20 % — enough to capture the title block on most sheet-music layouts.
    /// </summary>
    public const double TopStripFraction = 0.20;

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
    /// Runs the full pipeline for every page in <paramref name="pdfPath"/>:
    ///   render → rotate → crop top strip → Tesseract OCR.
    /// Reports progress via <paramref name="progress"/> (current page, total pages).
    /// Returns one <see cref="PdfPageOcrResult"/> per page.
    /// </summary>
    public static async Task<List<PdfPageOcrResult>> ExtractAsync(
        string pdfPath,
        int rotation,
        IProgress<(int Page, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var tessDataDir = ResolveTessDataDir();

        int pageCount;
        using (var s = File.OpenRead(pdfPath))
            pageCount = Conversion.GetPageCount(s);

        var results = new List<PdfPageOcrResult>(pageCount);

        // TesseractEngine is not thread-safe; create once and reuse sequentially.
        using var engine = new TesseractEngine(tessDataDir, "eng", EngineMode.Default);

        for (int i = 0; i < pageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((i, pageCount));

            var rawText = await Task.Run(
                () => OcrPage(pdfPath, i, rotation, engine),
                cancellationToken);

            results.Add(new PdfPageOcrResult(i, rawText));
            Debug.WriteLine($"[OCR] Page {i}: {rawText[..Math.Min(80, rawText.Length)]}");
        }

        return results;
    }

    // -----------------------------------------------------------------
    // Formatting helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Builds a human-readable report: one line per page with its OCR text.
    /// </summary>
    public static string FormatRawText(IReadOnlyList<PdfPageOcrResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== OCR raw text — {results.Count} pages ===");
        sb.AppendLine();
        foreach (var r in results)
        {
            var preview = r.RawText.Replace("\r", "").Replace("\n", " ").Trim();
            if (preview.Length > 160) preview = preview[..160] + "…";
            sb.AppendLine($"Page {r.PageIndex,3}: {preview}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Heuristically identifies which pages are likely "title" pages (start of a new
    /// piece) and emits them as a suggested JSON TOC array for review.
    /// Strategy: pages whose OCR text is short (sparse = title page) and contains
    /// enough letter characters.
    /// </summary>
    public static string FormatSuggestedJson(IReadOnlyList<PdfPageOcrResult> results)
    {
        var candidates = new List<(int Page, string Name)>();
        foreach (var r in results)
        {
            var clean = CleanOcrText(r.RawText);
            if (string.IsNullOrWhiteSpace(clean)) continue;

            // Short total text on a page often signals a title / first page of a piece
            if (r.RawText.Length < 200 && HasEnoughLetters(clean))
                candidates.Add((r.PageIndex, TrimToTitle(clean)));
        }

        var tocArray = new List<object>();
        foreach (var (page, name) in candidates)
            tocArray.Add(new { songName = name, pageNo = page, composer = "" });

        var opts = new JsonSerializerOptions { WriteIndented = true };
        return "=== Suggested TOC (review and edit) ===\n\n" +
               JsonSerializer.Serialize(tocArray, opts);
    }

    // -----------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------

    private static string OcrPage(string pdfPath, int pageIndex, int rotation, TesseractEngine engine)
    {
        // 1. Render page to SKBitmap
        using var pdfStream = File.OpenRead(pdfPath);
        using var raw = Conversion.ToImage(
            pdfStream,
            page: (Index)pageIndex,
            options: new RenderOptions(Dpi: RenderDpi));

        // 2. Rotate
        using var rotated = ApplyRotation(raw, rotation);

        // 3. Crop top strip
        int cropH = Math.Max(1, (int)(rotated.Height * TopStripFraction));
        using var crop = new SKBitmap(rotated.Width, cropH);
        using (var canvas = new SKCanvas(crop))
            canvas.DrawBitmap(rotated,
                new SKRect(0, 0, rotated.Width, cropH),
                new SKRect(0, 0, rotated.Width, cropH));

        // 4. Encode to PNG bytes (Tesseract accepts Pix from memory)
        using var ms = new MemoryStream();
        using (var img = SKImage.FromBitmap(crop))
        using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
            data.SaveTo(ms);

        // 5. Tesseract OCR
        using var pix  = Pix.LoadFromMemory(ms.ToArray());
        using var page = engine.Process(pix);
        return page.GetText()?.Trim() ?? string.Empty;
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
        return letters >= 6;
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
