using PDFtoImage;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
public sealed record PdfPageWinOcrResult(int PageIndex, string RawText, string VolumeFileName = "");

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
    // Volume spec (mirrors PdfOcrService)
    // -------------------------------------------------------------------------

    /// <summary>
    /// One volume in a multi-volume PDF set.
    /// <para><see cref="PageCount"/> is taken from the JSON sidecar when available
    /// (<c>-1</c> = unknown, derive from PDF bytes at runtime).</para>
    /// </summary>
    private sealed record VolumeSpec(string PdfPath, int Rotation, int PageCount = -1);

    // -------------------------------------------------------------------------
    // Main entry point
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs the Windows OCR pipeline across one or more PDF volumes.
    ///
    /// <para>Multi-volume detection (checked in order):</para>
    /// <list type="number">
    ///   <item>If <paramref name="pathOrJson"/> ends with <c>.json</c>, load it directly.</item>
    ///   <item>If a sibling <c>.json</c> exists next to a PDF, load that.</item>
    ///   <item>Otherwise treat <paramref name="pathOrJson"/> as a single PDF.</item>
    /// </list>
    ///
    /// <para>Each PDF is read into memory once; page counts come from the JSON sidecar
    /// where available so no extra file I/O is needed just to count pages.</para>
    /// </summary>
    public static async Task<List<PdfPageWinOcrResult>> ExtractAsync(
        string pathOrJson,
        int defaultRotation = 0,
        IProgress<(int Page, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                  ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
                  ?? throw new InvalidOperationException(
                      "Windows OCR engine could not be created. " +
                      "Make sure the English OCR language pack is installed.");

        var volumes = ResolveVolumes(pathOrJson, defaultRotation);

        // Use page counts from JSON sidecar where available — no PDF opens just to count.
        int totalPages = volumes.All(v => v.PageCount >= 0)
            ? volumes.Sum(v => v.PageCount)
            : 0;

        var results = new List<PdfPageWinOcrResult>();

        int globalIndex = 0;
        foreach (var vol in volumes)
        {
            // Read each PDF once; wrap in MemoryStream per page (no extra allocation).
            var pdfBytes = File.ReadAllBytes(vol.PdfPath);
            int volPageCount = vol.PageCount >= 0
                ? vol.PageCount
                : Conversion.GetPageCount(new MemoryStream(pdfBytes));

            var volFileName = Path.GetFileName(vol.PdfPath);

            for (int i = 0; i < volPageCount; i++, globalIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((globalIndex, totalPages));

                var rawText = await OcrPageAsync(pdfBytes, i, vol.Rotation, engine);
                results.Add(new PdfPageWinOcrResult(globalIndex, rawText, volFileName));
            }
        }

        return results;
    }

    // -------------------------------------------------------------------------
    // Volume resolution (mirrors PdfOcrService.ResolveVolumes)
    // -------------------------------------------------------------------------

    private static List<VolumeSpec> ResolveVolumes(string pathOrJson, int defaultRotation)
    {
        string? jsonPath = null;

        if (pathOrJson.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            jsonPath = pathOrJson;
        }
        else
        {
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
                        System.Diagnostics.Debug.WriteLine($"[WinOCR] Volume not found, skipping: {fullPath}");
                }
                if (list.Count > 0) return list;
            }
        }

        return [new VolumeSpec(pathOrJson, defaultRotation)];
    }

    // -------------------------------------------------------------------------
    // Formatting helpers  (mirrors PdfOcrService)
    // -------------------------------------------------------------------------

    public static string FormatRawText(IReadOnlyList<PdfPageWinOcrResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Windows OCR raw text — {results.Count} pages ===");
        string lastVolume = string.Empty;
        foreach (var r in results)
        {
            if (r.VolumeFileName != lastVolume)
            {
                lastVolume = r.VolumeFileName;
                sb.AppendLine();
                sb.AppendLine($"=== Volume: {lastVolume} ===");
            }
            var preview = r.RawText.Replace("\r", "").Replace("\n", " ").Trim();
            if (preview.Length > 160) preview = preview[..160] + "…";
            sb.AppendLine($"Page {r.PageIndex,3}: {preview}");
        }
        return sb.ToString();
    }

    public static string FormatSuggestedJson(
        IReadOnlyList<PdfPageWinOcrResult> results,
        int pageNumberOffset = 0)
    {
        var candidates = new List<(int Page, string Name)>();
        foreach (var r in results)
        {
            var clean = r.RawText.Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrWhiteSpace(clean)) continue;

            if (r.RawText.Length < 200 && HasEnoughLetters(clean))
                candidates.Add((r.PageIndex + pageNumberOffset, TrimToTitle(clean)));
        }

        var tocArray = new List<object>();
        foreach (var (page, name) in candidates)
            tocArray.Add(new { songName = name, pageNo = page, composer = "" });

        var opts = new JsonSerializerOptions { WriteIndented = true };
        return $"=== Suggested TOC (review and edit \u2014 PageNumberOffset={pageNumberOffset}) ===\n\n" +
               JsonSerializer.Serialize(tocArray, opts);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static async Task<string> OcrPageAsync(
        byte[] pdfBytes, int pageIndex, int rotation, OcrEngine engine)
    {
        // 1. Render page to SKBitmap via PDFtoImage (no file I/O — bytes already loaded)
        using var pdfStream = new MemoryStream(pdfBytes);
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
