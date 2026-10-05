using Avalonia.Media.Imaging;
using PDFtoImage;
using SkiaSharp;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Renders the cover thumbnail of a PDF and caches it on disk, keyed by file path,
/// file version and render size, so a cover is only rendered once.
/// </summary>
internal static class PdfThumbnailLoader
{
    private static readonly string CacheFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SheetMusicViewer",
        "ThumbnailCache");

    private static int _cleanupStarted;

    public static Task<Bitmap> GetOrCreateAsync(string pdfPath, int width, int height, int rotation, bool skipCloudOnlyFiles)
    {
        return Task.Run(() =>
        {
            var cachePath = GetCachePath(pdfPath, width, height, rotation);
            if (File.Exists(cachePath))
            {
                try
                {
                    return new Bitmap(cachePath);
                }
                catch
                {
                    // Corrupt cache entry: render the cover again below
                }
            }

            StartCleanupOnce();

            if (skipCloudOnlyFiles && IsCloudOnly(pdfPath))
            {
                throw new IOException($"Cloud-only file, skipping: {pdfPath}");
            }

            var pdfRotation = rotation switch
            {
                1 => PdfRotation.Rotate90,
                2 => PdfRotation.Rotate180,
                3 => PdfRotation.Rotate270,
                _ => PdfRotation.Rotate0
            };

            using var pdfStream = File.OpenRead(pdfPath);
            using var skBitmap = Conversion.ToImage(pdfStream, page: (Index)0, options: new RenderOptions(
                Width: width,
                Height: height,
                Rotation: pdfRotation));

            using var data = skBitmap.Encode(SKEncodedImageFormat.Png, 100);
            var bytes = data.ToArray();
            TryWriteCache(cachePath, bytes);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        });
    }

    private static string GetCachePath(string pdfPath, int width, int height, int rotation)
    {
        string identity;
        try
        {
            var info = new FileInfo(pdfPath);
            identity = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            identity = pdfPath;
        }

        var key = $"{identity}|{width}x{height}|r{rotation}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
        return Path.Combine(CacheFolder, hash + ".png");
    }

    private static void TryWriteCache(string cachePath, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);
            var tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch
        {
            // The disk cache is best effort
        }
    }

    private static bool IsCloudOnly(string pdfPath)
    {
        const FileAttributes recallOnDataAccess = (FileAttributes)0x00400000;
        const FileAttributes recallOnOpen = (FileAttributes)0x00040000;
        var attributes = new FileInfo(pdfPath).Attributes;
        return (attributes & recallOnDataAccess) == recallOnDataAccess
            || (attributes & recallOnOpen) == recallOnOpen
            || (attributes & FileAttributes.Offline) == FileAttributes.Offline;
    }

    /// <summary>
    /// Deletes cache entries that have not been written for 60 days. Runs once per process.
    /// </summary>
    private static void StartCleanupOnce()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0)
        {
            return;
        }

        try
        {
            if (!Directory.Exists(CacheFolder))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-60);
            foreach (var file in Directory.EnumerateFiles(CacheFolder, "*.png"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Ignore entries that cannot be inspected
                }
            }
        }
        catch
        {
            // Cleanup is best effort
        }
    }
}
