using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer.Desktop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Manual tests for generating sample PDF files used by the application.
/// These tests generate the GettingStarted.pdf that is displayed when no root folder is specified.
/// Run these tests manually to regenerate or update the sample PDF.
/// </summary>
[TestClass]
[TestCategory("Manual")]
public class SamplePdfGeneratorTests : TestBase
{
    /// <summary>
    /// Gets the path to the sample music folder in the project assets.
    /// </summary>
    private static string GetProjectSampleMusicFolder()
    {
        var baseDir = AppContext.BaseDirectory;
        var solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        var desktopProjectFolder = typeof(SheetMusicViewer.Desktop.SampleDataHelper).Namespace!;
        return Path.Combine(solutionDir, desktopProjectFolder, "Assets", "SampleMusic");
    }

    /// <summary>
    /// Generates the GettingStarted.pdf sample file in the Assets/SampleMusic folder.
    /// The test self-validates that PDF page count matches JSON metadata.
    /// </summary>
    [TestMethod]
    [TestCategory("Manual")]
    public async Task GenerateGettingStartedPdf()
    {
        var sampleFolder = GetProjectSampleMusicFolder();
        LogMessage($"Sample folder: {sampleFolder}");
        
        if (!Directory.Exists(sampleFolder))
        {
            Assert.Fail("Sample folder does not exist.");
        }

        var pdfPath = Path.Combine(sampleFolder, "GettingStarted.pdf");
        LogMessage($"Generating PDF at: {pdfPath}");

        // Generate PDF and get expected page count
        var expectedPageCount = await CreateGettingStartedPdfAsync(pdfPath);
        
        Assert.IsTrue(File.Exists(pdfPath), $"PDF should exist at {pdfPath}");
        LogMessage($"PDF generated successfully: {new FileInfo(pdfPath).Length:N0} bytes");
        
        // Validate PDF page count matches what we intended
        var pdfBytes = File.ReadAllBytes(pdfPath);
        var actualPageCount = PDFtoImage.Conversion.GetPageCount(pdfBytes);
        LogMessage($"PDF has {actualPageCount} pages (expected {expectedPageCount})");
        
        Assert.AreEqual(expectedPageCount, actualPageCount, 
            $"PDF generation bug: expected {expectedPageCount} pages but PDFtoImage reads {actualPageCount}");
        
        // Render each page to verify all are valid
        for (int i = 0; i < actualPageCount; i++)
        {
            using var bitmap = PDFtoImage.Conversion.ToImage(pdfBytes, page: i);
            Assert.IsTrue(bitmap.Width > 0 && bitmap.Height > 0, $"Page {i} should render");
            LogMessage($"  Page {i} rendered: {bitmap.Width}x{bitmap.Height}");
        }
        
        // Generate JSON metadata with matching page count
        var jsonPath = Path.ChangeExtension(pdfPath, ".json");
        await CreateSampleMetadataAsync(jsonPath, actualPageCount);
        LogMessage($"Metadata JSON generated at: {jsonPath}");
        
        // Validate JSON matches PDF
        var jsonContent = await File.ReadAllTextAsync(jsonPath);
        Assert.IsTrue(jsonContent.Contains($"\"pageCount\": {actualPageCount}"), 
            $"JSON should have pageCount: {actualPageCount}");
        
        var tocCount = jsonContent.Split("\"pageNo\":").Length - 1;
        Assert.AreEqual(actualPageCount, tocCount, 
            $"JSON should have {actualPageCount} TOC entries");
        
        LogMessage($"Success: {actualPageCount} pages in PDF match JSON metadata");
    }

    /// <summary>
    /// Creates a 10-page PDF with proper xref byte offsets.
    /// Returns the number of pages for validation.
    /// </summary>
    private static async Task<int> CreateGettingStartedPdfAsync(string path)
    {
        var pageContents = new[]
        {
            CreatePageContent("Welcome to SheetMusicViewer!", new[]
            {
                "", "A cross-platform PDF sheet music viewer",
                "by Calvin Hsia (2019, updated 2025)", "",
                "This sample PDF will help you learn", "how to use the application.", "",
                "Features:", "  - Display 1 or 2 pages at a time",
                "  - Mark pages as Favorites", "  - Add ink annotations with stylus/finger",
                "  - Table of Contents support", "  - Supports multi-volume PDF sets", "",
                "Navigate to the next page to continue..."
            }),
            CreatePageContent("Getting Started", new[]
            {
                "", "To use your own PDF sheet music:", "",
                "1. Run the program and choose a path to a", "   root folder containing PDF music files.", "",
                "2. PDFs can contain 1-N pages.", "",
                "3. The PDFs are never altered by the program.",
                "   All auxiliary data (TOC, Favorites, Inking,", "   LastPageNumberViewed) is stored in JSON files.", "",
                "4. The program needs write permission to write", "   the JSON metadata files alongside your PDFs.", "",
                "Press Alt+C to open the Music Chooser dialog."
            }),
            CreatePageContent("Multi-Volume PDF Support", new[]
            {
                "", "Large books can be scanned to multiple PDFs:", "", "NAMING CONVENTION:",
                "  book.pdf  (or book0.pdf) - first volume", "  book1.pdf               - second volume",
                "  book1a.pdf              - insert after book1", "  book2.pdf               - third volume", "",
                "All volumes with same root name are treated",
                "as one continuous book!", "",
                "Example: SonatenI.pdf and SonatenI1.pdf form",
                "one book, while SonatenII.pdf is separate.", "",
                "This allows rescanning pages without",
                "renumbering subsequent volumes."
            }),
            CreatePageContent("Singles Folders", new[]
            {
                "", "A subfolder ending in 'Singles' (like", "'GershwinSingles') treats each PDF as a",
                "single song with possibly multiple pages.", "", "SINGLES FOLDER FEATURES:",
                "  - Maintained in alphabetical order", "  - First page of first song is the icon",
                "  - Dynamically updates as items are", "    added, removed, or renamed",
                "  - TOC entries auto-adjust", "", "A subfolder called 'Hidden' will not",
                "be searched.", "",
                "TIP: Create a custom PDF title page that",
                "sorts first alphabetically for the icon."
            }),
            CreatePageContent("Display Modes & Navigation", new[]
            {
                "", "TWO DISPLAY MODES:", "  - Single page per screen",
                "  - Two pages (side by side) per screen", "", "NAVIGATION:",
                "  - Thumb arrows: Move by screenful", "    (or jump to next/prev favorite)",
                "  - Arrow keys: Move by 1 screenful", "  - Bottom quarter: Tap to turn pages", "",
                "IN 2-PAGE MODE:", "  Bottom is divided into 4 quarters.",
                "  Outer quarters: advance 2 pages", "  Inner quarters: advance 1 page", "",
                "Top 3/4: Zoom/pan with two fingers"
            }),
            CreatePageContent("Instant Page Turns", new[]
            {
                "", "Musicians need INSTANT page turns!", "You can't pause mid-performance to wait.", "",
                "THE CHALLENGE:", "PDF rendering takes 100-500ms per page.",
                "That's too slow for live performance.", "", "THE SOLUTION: Predictive Caching", "",
                "SINGLE PAGE MODE (showing page 5):", "  Already cached: pages 4, 6, 7",
                "  Turn to page 6 -> page 8 starts loading", "",
                "TWO PAGE MODE (showing pages 5-6):", "  Already cached: pages 3, 4, 7, 8",
                "  All page turns are instant!", "", "Result: Page turns feel like real paper."
            }),
            CreatePageContent("Inking & Annotations", new[]
            {
                "", "Inking is off by default.", "", "TO INK A PAGE:",
                "1. Click the Ink checkbox for the page", "   (in 2-page mode, each page has its own)",
                "2. Draw with mouse, pen, or finger", "3. Right-click for color options:",
                "   - Black pen", "   - Red pen", "   - Yellow highlighter",
                "4. Click Ink checkbox again to save", "", "TIP: Zoom in before inking for accuracy",
                "when correcting typos on the musical staff.", "", "All ink is stored in the JSON metadata file."
            }),
            CreatePageContent("Performance & Memory", new[]
            {
                "", "CACHE MANAGEMENT:", "  - Automatic based on available memory",
                "  - Least-recently-used pages evicted first", "  - Multi-volume: volumes load as needed", "",
                "MULTI-VOLUME BOOKS:", "For volumes with 100+ pages each, boundary", "pages are pre-cached:",
                "  - Last pages of current volume", "  - First pages of next volume", "",
                "MEMORY-HUNGRY PDFs:", "Some high-resolution scans use lots of RAM.", "To reduce size:",
                "  1. Print to 'Microsoft Print to PDF'", "  2. Use an online PDF resizer tool", "",
                "This creates a smaller, optimized PDF."
            }),
            CreatePageContent("PDF Table of Contents", new[]
            {
                "", "Edit the TOC by clicking the thumbnail", "to the right of the slider, or press Alt+E.", "",
                "TOC FEATURES:", "  - Import/export to clipboard (Excel format)",
                "  - Add composer, date, notes", "  - OCR a scanned TOC for import", "",
                "PAGE NUMBER OFFSET:", "Physical page numbers in scanned books may",
                "not match PDF page numbers (due to cover", "pages, intros, etc.).", "",
                "PageNumberOffset maps between them so the", "imported TOC doesn't need adjustment."
            }),
            CreatePageContent("About SheetMusicViewer", new[]
            {
                "", "Created by Calvin Hsia", "Email: calvin_hsia@alum.mit.edu",
                "Website: http://calvinhsia.com", "", "I have hundreds of piano music books",
                "collected over decades, kept on OneDrive.", "",
                "I digitized 30,000+ pages of music using", "a Xerox WorkCentre scanner.", "",
                "I really love Ragtime! There's something", "so binary about it: powers of 2, 16 measures",
                "per verse, 2/4 time, syncopation.", "", "Enjoy your music!", "- Calvin"
            })
        };

        var pageCount = pageContents.Length;
        var fontObjNum = 3 + pageCount * 2;
        
        // Build objects list
        var objects = new List<(int objNum, string content)>
        {
            (1, "<< /Type /Catalog /Pages 2 0 R >>"),
            (2, $"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(3, pageCount).Select(n => $"{n} 0 R"))}] /Count {pageCount} >>")
        };
        
        for (int i = 0; i < pageCount; i++)
        {
            objects.Add((3 + i, $"<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 {fontObjNum} 0 R /F2 {fontObjNum + 1} 0 R >> >> /MediaBox [0 0 612 792] /Contents {3 + pageCount + i} 0 R >>"));
        }
        
        for (int i = 0; i < pageCount; i++)
        {
            var content = pageContents[i];
            objects.Add((3 + pageCount + i, $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream"));
        }
        
        objects.Add((fontObjNum, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
        objects.Add((fontObjNum + 1, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"));
        
        objects = objects.OrderBy(o => o.objNum).ToList();
        
        // Build PDF with correct byte offsets
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n%\xe2\xe3\xcf\xd3\n");
        
        var offsets = new List<int>();
        foreach (var (objNum, content) in objects)
        {
            offsets.Add(sb.Length);
            sb.Append($"{objNum} 0 obj\n{content}\nendobj\n");
        }
        
        var xrefOffset = sb.Length;
        sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            sb.Append($"{offset:D10} 00000 n \n");
        
        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.ASCII);
        return pageCount;
    }

    private static string CreatePageContent(string title, string[] lines)
    {
        var sb = new StringBuilder();
        sb.Append("BT\n/F2 22 Tf\n72 720 Td\n");
        sb.Append($"({EscapePdfString(title)}) Tj\n/F1 11 Tf\n0 -28 Td\n");
        foreach (var line in lines)
            sb.Append($"({EscapePdfString(line)}) Tj\n0 -15 Td\n");
        sb.Append("ET\n");
        return sb.ToString();
    }

    private static string EscapePdfString(string text) =>
        text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>
    /// Creates JSON metadata with page count passed as parameter to ensure sync.
    /// </summary>
    private static async Task CreateSampleMetadataAsync(string path, int pageCount)
    {
        var tocEntries = new[]
        {
            ("Welcome", "Introduction"),
            ("Getting Started", "Setup Guide"),
            ("Multi-Volume PDFs", "Feature Guide"),
            ("Singles Folders", "Feature Guide"),
            ("Display & Navigation", "User Guide"),
            ("Instant Page Turns", "Performance Guide"),
            ("Inking & Annotations", "User Guide"),
            ("Performance & Memory", "Technical Info"),
            ("PDF Table of Contents", "Feature Guide"),
            ("About", "Calvin Hsia")
        };
        
        if (tocEntries.Length != pageCount)
            throw new InvalidOperationException($"TOC entries ({tocEntries.Length}) must match page count ({pageCount})");
        
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"version\": 1,");
        sb.AppendLine($"  \"lastWrite\": \"{DateTime.UtcNow:o}\",");
        sb.AppendLine($"  \"lastPageNo\": 0,");
        sb.AppendLine($"  \"pageNumberOffset\": 0,");
        sb.AppendLine($"  \"notes\": \"Sample PDF with instructions for using SheetMusicViewer\",");
        sb.AppendLine($"  \"volumes\": [");
        sb.AppendLine($"    {{ \"fileName\": \"GettingStarted.pdf\", \"pageCount\": {pageCount}, \"rotation\": 0 }}");
        sb.AppendLine($"  ],");
        sb.AppendLine($"  \"tableOfContents\": [");
        for (int i = 0; i < pageCount; i++)
        {
            var comma = i < pageCount - 1 ? "," : "";
            sb.AppendLine($"    {{ \"pageNo\": {i}, \"songName\": \"{tocEntries[i].Item1}\", \"composer\": \"{tocEntries[i].Item2}\" }}{comma}");
        }
        sb.AppendLine($"  ],");
        sb.AppendLine($"  \"favorites\": [],");
        sb.AppendLine($"  \"inkStrokes\": {{}}");
        sb.AppendLine("}");
        
        await File.WriteAllTextAsync(path, sb.ToString());
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestMetadataCreationForSamplePdfs()
    {
        var sampleFolder = GetProjectSampleMusicFolder();
        Assert.IsTrue(Directory.Exists(sampleFolder), $"Sample folder should exist at {sampleFolder}");
        
        var tempFolder = Path.Combine(Path.GetTempPath(), $"SheetMusicViewerTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
        LogMessage($"Created temp folder: {tempFolder}");
        
        try
        {
            var pdfFiles = Directory.GetFiles(sampleFolder, "*.pdf");
            Assert.IsTrue(pdfFiles.Length > 0, "Sample folder should contain at least one PDF");
            
            foreach (var pdfFile in pdfFiles)
            {
                File.Copy(pdfFile, Path.Combine(tempFolder, Path.GetFileName(pdfFile)));
                LogMessage($"Copied: {Path.GetFileName(pdfFile)}");
            }
            
            var gettingStartedJson = Path.Combine(sampleFolder, "GettingStarted.json");
            if (File.Exists(gettingStartedJson))
            {
                File.Copy(gettingStartedJson, Path.Combine(tempFolder, "GettingStarted.json"));
                LogMessage("Copied: GettingStarted.json");
            }
            
            var jsonFilesBefore = Directory.GetFiles(tempFolder, "*.json");
            Assert.AreEqual(1, jsonFilesBefore.Length, "Should have 1 JSON file before loading");
            
            var provider = new PdfToImageDocumentProvider();
            var (metadataList, _) = await SheetMusicLib.PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
                tempFolder, provider, exceptionHandler: null, useParallelLoading: true);
            
            LogMessage($"Loaded {metadataList.Count} metadata entries");
            Assert.IsTrue(metadataList.Count > 0, "Should load at least one metadata entry");
            
            foreach (var metadata in metadataList)
            {
                var fileName = Path.GetFileName(metadata.FullPathFile);
                var hasPreExistingJson = fileName.Equals("GettingStarted.pdf", StringComparison.OrdinalIgnoreCase);
                
                if (hasPreExistingJson)
                    Assert.IsFalse(metadata.IsDirty, $"GettingStarted.pdf should NOT be dirty");
                else
                    Assert.IsTrue(metadata.IsDirty, $"{fileName} should be dirty");
                
                var totalPages = metadata.VolumeInfoList.Sum(v => v.NPagesInThisVolume);
                LogMessage($"  {fileName}: {totalPages} pages, IsDirty={metadata.IsDirty}");
            }
            
            int savedCount = 0;
            foreach (var metadata in metadataList.Where(m => m.IsDirty))
            {
                SheetMusicLib.PdfMetaDataCore.SaveToJson(metadata, forceSave: true);
                savedCount++;
            }
            LogMessage($"Saved {savedCount} new JSON files");
            
            var jsonFilesAfter = Directory.GetFiles(tempFolder, "*.json");
            Assert.AreEqual(metadataList.Count, jsonFilesAfter.Length, "Should have one JSON per PDF");
            
            LogMessage("Integration test passed");
        }
        finally
        {
            try { Directory.Delete(tempFolder, true); }
            catch { }
        }
    }
}
