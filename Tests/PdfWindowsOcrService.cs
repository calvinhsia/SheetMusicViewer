using PDFtoImage;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Tests;

/// <summary>
/// Per-page OCR result produced by <see cref="PdfWindowsOcrService"/>.
/// </summary>
public sealed record PdfPageWinOcrResult(int PageIndex, string RawText);

/// <summary>
/// Windows-only OCR service that mirrors <c>PdfOcrService</c> from
/// SheetMusicViewer.Desktop but uses <see cref="OcrEngine"/> from
/// <c>Windows.Media.Ocr</c> instead of Tesseract.
///
/// Render pipeline (shared with the Tesseract version):
///   PDFtoImage → SkiaSharp rotate → crop top strip → SoftwareBitmap → Windows OCR.
/// </summary>
public static class PdfWindowsOcrService
{
    /// <summary>Render DPI — same as the Tesseract pipeline for a fair comparison.</summary>
    public const int RenderDpi = 150;

    /// <summary>Fraction of the (rotated) page height kept for OCR (top 20 %).</summary>
    public const double TopStripFraction = 0.20;

    // -------------------------------------------------------------------------
    // Main entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs the Windows OCR pipeline for every page in <paramref name="pdfPath"/>.
    /// Must be called from an MTA thread (the WinRT OCR engine is agile).
    /// Reports progress via <paramref name="progress"/> as (currentPage, totalPages).
    /// </summary>
    public static async Task<List<PdfPageWinOcrResult>> ExtractAsync(
        string pdfPath,
        int rotation,
        IProgress<(int Page, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                  ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
                  ?? throw new InvalidOperationException(
                      "Windows OCR engine could not be created. " +
                      "Make sure the English OCR language pack is installed.");

        int pageCount;
        using (var s = File.OpenRead(pdfPath))
            pageCount = Conversion.GetPageCount(s);

        var results = new List<PdfPageWinOcrResult>(pageCount);

        for (int i = 0; i < pageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((i, pageCount));

            var rawText = await OcrPageAsync(pdfPath, i, rotation, engine);
            results.Add(new PdfPageWinOcrResult(i, rawText));
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // Formatting helpers  (mirrors PdfOcrService)
    // -------------------------------------------------------------------------

    public static string FormatRawText(IReadOnlyList<PdfPageWinOcrResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Windows OCR raw text — {results.Count} pages ===");
        sb.AppendLine();
        foreach (var r in results)
        {
            var preview = r.RawText.Replace("\r", "").Replace("\n", " ").Trim();
            if (preview.Length > 160) preview = preview[..160] + "…";
            sb.AppendLine($"Page {r.PageIndex,3}: {preview}");
        }
        return sb.ToString();
    }

    public static string FormatSuggestedJson(IReadOnlyList<PdfPageWinOcrResult> results)
    {
        var candidates = new List<(int Page, string Name)>();
        foreach (var r in results)
        {
            var clean = r.RawText.Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrWhiteSpace(clean)) continue;

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

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static async Task<string> OcrPageAsync(
        string pdfPath, int pageIndex, int rotation, OcrEngine engine)
    {
        // 1. Render page to SKBitmap via PDFtoImage
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

        // 4. Convert to SoftwareBitmap (Bgra8, premultiplied alpha) for WinRT OCR
        using var softwareBitmap = ToSoftwareBitmap(crop);

        // 5. Windows OCR
        var ocrResult = await engine.RecognizeAsync(softwareBitmap);
        return ocrResult.Text?.Trim() ?? string.Empty;
    }

    /// <summary>Converts an <see cref="SKBitmap"/> to a <see cref="SoftwareBitmap"/> (Bgra8).</summary>
    private static SoftwareBitmap ToSoftwareBitmap(SKBitmap src)
    {
        // Re-encode as BGRA8888 if needed
        SKBitmap bgra;
        if (src.ColorType == SKColorType.Bgra8888)
        {
            bgra = src;
        }
        else
        {
            bgra = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bgra);
            canvas.DrawBitmap(src, 0, 0);
        }

        var pixelBytes = bgra.Bytes;
        var swBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, bgra.Width, bgra.Height, BitmapAlphaMode.Premultiplied);
        swBitmap.CopyFromBuffer(pixelBytes.AsBuffer());

        if (!ReferenceEquals(bgra, src))
            bgra.Dispose();

        return swBitmap;
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
        int i = 0;
        while (i < clean.Length && (char.IsDigit(clean[i]) || char.IsWhiteSpace(clean[i])))
            i++;
        return clean[i..].Trim();
    }
}
