using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer.Desktop;
using System;
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
    /// Uses the same logic as SampleDataHelper.GetBundledSampleMusicFolder() to find the correct location.
    /// </summary>
    private static string GetProjectSampleMusicFolder()
    {
        // The bundled sample folder is at Assets/SampleMusic relative to the Desktop project
        // We can use the same approach as SampleDataHelper but navigate from test output to source
        var baseDir = AppContext.BaseDirectory;
        
        // From bin/Debug/net10.0 go up to solution root
        // baseDir = ...SheetMusicViewer\AvaloniaTests\bin\Debug\net10.0\
        var solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
        
        // Get the Desktop project folder name from the SampleDataHelper type's namespace
        // The namespace matches the project folder name (e.g., "SheetMusicViewer.Desktop" or "SheetMusicViewer")
        var desktopProjectFolder = typeof(SheetMusicViewer.Desktop.SampleDataHelper).Namespace!;
        
        return Path.Combine(solutionDir, desktopProjectFolder, "Assets", "SampleMusic");
    }

    /// <summary>
    /// Generates the GettingStarted.pdf sample file in the Assets/SampleMusic folder.
    /// This PDF contains comprehensive usage instructions from the README and is displayed
    /// when the app starts with no root folder specified.
    /// Also validates that the generated PDF is readable by PDFtoImage.
    /// </summary>
    [TestMethod]
    [TestCategory("Manual")]
    public async Task GenerateGettingStartedPdf()
    {
        var sampleFolder = GetProjectSampleMusicFolder();
        LogMessage($"Sample folder: {sampleFolder}");
        
        // Ensure the folder exists
        if (!Directory.Exists(sampleFolder))
        {
            Assert.Fail("Sample folder does not exist. Please ensure the project structure is correct.");
        }

        var pdfPath = Path.Combine(sampleFolder, "GettingStarted.pdf");
        LogMessage($"Generating PDF at: {pdfPath}");

        // Generate the comprehensive getting started PDF
        await CreateGettingStartedPdfAsync(pdfPath);
        
        Assert.IsTrue(File.Exists(pdfPath), $"PDF should exist at {pdfPath}");
        var fileInfo = new FileInfo(pdfPath);
        LogMessage($"PDF generated successfully: {fileInfo.Length:N0} bytes");
        
        // Also generate a simple metadata JSON file
        var jsonPath = Path.ChangeExtension(pdfPath, ".json");
        await CreateSampleMetadataAsync(jsonPath);
        LogMessage($"Metadata JSON generated at: {jsonPath}");
        
        // Validate the PDF is readable by PDFtoImage
        var pdfBytes = File.ReadAllBytes(pdfPath);
        var pageCount = PDFtoImage.Conversion.GetPageCount(pdfBytes);
        LogMessage($"PDF has {pageCount} pages");
        Assert.AreEqual(9, pageCount, "PDF should have 9 pages");
        
        // Render the first page to verify it's valid
        using var bitmap = PDFtoImage.Conversion.ToImage(pdfBytes, page: 0);
        LogMessage($"Page 0 rendered: {bitmap.Width}x{bitmap.Height} pixels");
        Assert.IsTrue(bitmap.Width > 0 && bitmap.Height > 0, "Rendered bitmap should have dimensions");
        
        LogMessage("PDF validation successful - file is readable by PDFtoImage");
    }

    /// <summary>
    /// Creates a comprehensive multi-page PDF with usage instructions.
    /// Includes content from README lines 102-201.
    /// </summary>
    private static async Task CreateGettingStartedPdfAsync(string path)
    {
        var sb = new StringBuilder();
        
        // PDF Header
        sb.AppendLine("%PDF-1.4");
        sb.AppendLine("%‚„œ”"); // Binary marker for PDF
        
        // Define the pages we want (9 pages total)
        var pageContents = new[]
        {
            // Page 1: Welcome
            CreatePageContent("Welcome to SheetMusicViewer!", new[]
            {
                "",
                "A cross-platform PDF sheet music viewer",
                "by Calvin Hsia (2019, updated 2025)",
                "",
                "This sample PDF will help you learn",
                "how to use the application.",
                "",
                "Features:",
                "  - Display 1 or 2 pages at a time",
                "  - Mark pages as Favorites",
                "  - Add ink annotations with stylus/finger",
                "  - Table of Contents support",
                "  - Supports multi-volume PDF sets",
                "",
                "Navigate to the next page to continue..."
            }),
            
            // Page 2: Getting Started
            CreatePageContent("Getting Started", new[]
            {
                "",
                "To use your own PDF sheet music:",
                "",
                "1. Run the program and choose a path to a",
                "   root folder containing PDF music files.",
                "",
                "2. PDFs can contain 1-N pages.",
                "",
                "3. The PDFs are never altered by the program.",
                "   All auxiliary data (TOC, Favorites, Inking,",
                "   LastPageNumberViewed) is stored in JSON files.",
                "",
                "4. The program needs write permission to write",
                "   the JSON metadata files alongside your PDFs.",
                "",
                "Press Alt+C to open the Music Chooser dialog."
            }),
            
            // Page 3: Multi-Volume PDFs
            CreatePageContent("Multi-Volume PDF Support", new[]
            {
                "",
                "Large books can be scanned to multiple PDFs:",
                "",
                "NAMING CONVENTION:",
                "  book.pdf  (or book0.pdf) - first volume",
                "  book1.pdf               - second volume",
                "  book1a.pdf              - insert after book1",
                "  book2.pdf               - third volume",
                "",
                "All volumes with same root name are treated",
                "as one continuous book!",
                "",
                "Example: SonatenI.pdf and SonatenI1.pdf form",
                "one book, while SonatenII.pdf is separate.",
                "",
                "This allows rescanning pages without",
                "renumbering subsequent volumes."
            }),
            
            // Page 4: Singles Folders
            CreatePageContent("Singles Folders", new[]
            {
                "",
                "A subfolder ending in 'Singles' (like",
                "'GershwinSingles') treats each PDF as a",
                "single song with possibly multiple pages.",
                "",
                "SINGLES FOLDER FEATURES:",
                "  - Maintained in alphabetical order",
                "  - First page of first song is the icon",
                "  - Dynamically updates as items are",
                "    added, removed, or renamed",
                "  - TOC entries auto-adjust",
                "",
                "A subfolder called 'Hidden' will not",
                "be searched.",
                "",
                "TIP: Create a custom PDF title page that",
                "sorts first alphabetically for the icon."
            }),
            
            // Page 5: Display and Navigation
            CreatePageContent("Display Modes & Navigation", new[]
            {
                "",
                "TWO DISPLAY MODES:",
                "  - Single page per screen",
                "  - Two pages (side by side) per screen",
                "",
                "NAVIGATION:",
                "  - Thumb arrows: Move by screenful",
                "    (or jump to next/prev favorite)",
                "  - Arrow keys: Move by 1 screenful",
                "  - Bottom quarter: Tap to turn pages",
                "",
                "IN 2-PAGE MODE:",
                "  Bottom is divided into 4 quarters.",
                "  Outer quarters: advance 2 pages",
                "  Inner quarters: advance 1 page",
                "",
                "Top 3/4: Zoom/pan with two fingers"
            }),
            
            // Page 6: Inking
            CreatePageContent("Inking & Annotations", new[]
            {
                "",
                "Inking is off by default.",
                "",
                "TO INK A PAGE:",
                "1. Click the Ink checkbox for the page",
                "   (in 2-page mode, each page has its own)",
                "2. Draw with mouse, pen, or finger",
                "3. Right-click for color options:",
                "   - Black pen",
                "   - Red pen",
                "   - Yellow highlighter",
                "4. Click Ink checkbox again to save",
                "",
                "TIP: Zoom in before inking for accuracy",
                "when correcting typos on the musical staff.",
                "",
                "All ink is stored in the JSON metadata file."
            }),
            
            // Page 7: Page Caching & Performance
            CreatePageContent("Performance & Caching", new[]
            {
                "",
                "Rendering PDF pages takes time. The app",
                "prefetches and caches pages for instant",
                "page turns!",
                "",
                "SINGLE PAGE MODE (showing page 5):",
                "  Prefetched: pages 4, 6, 7",
                "",
                "DOUBLE PAGE MODE (showing 5,6):",
                "  Prefetched: pages 3, 4, 7, 8",
                "",
                "For multi-volume sets, volumes are read",
                "asynchronously as needed.",
                "",
                "NOTE: Some PDFs consume lots of memory.",
                "If so, print to 'Microsoft Print to PDF'",
                "or use an online PDF resizer tool."
            }),
            
            // Page 8: Table of Contents
            CreatePageContent("PDF Table of Contents", new[]
            {
                "",
                "Edit the TOC by clicking the thumbnail",
                "to the right of the slider, or press Alt+E.",
                "",
                "TOC FEATURES:",
                "  - Import/export to clipboard (Excel format)",
                "  - Add composer, date, notes",
                "  - OCR a scanned TOC for import",
                "",
                "PAGE NUMBER OFFSET:",
                "Physical page numbers in scanned books may",
                "not match PDF page numbers (due to cover",
                "pages, intros, etc.).",
                "",
                "PageNumberOffset maps between them so the",
                "imported TOC doesn't need adjustment."
            }),
            
            // Page 9: About the Author
            CreatePageContent("About SheetMusicViewer", new[]
            {
                "",
                "Created by Calvin Hsia",
                "Email: calvin_hsia@alum.mit.edu",
                "Website: http://calvinhsia.com",
                "",
                "I have hundreds of piano music books",
                "collected over decades, kept on OneDrive.",
                "",
                "I digitized 30,000+ pages of music using",
                "a Xerox WorkCentre scanner.",
                "",
                "I really love Ragtime! There's something",
                "so binary about it: powers of 2, 16 measures",
                "per verse, 2/4 time, syncopation.",
                "",
                "Enjoy your music!",
                "- Calvin"
            })
        };

        // Catalog (object 1)
        sb.AppendLine("1 0 obj");
        sb.AppendLine("<< /Type /Catalog /Pages 2 0 R >>");
        sb.AppendLine("endobj");
        
        // Pages (object 2)
        var pageCount = pageContents.Length;
        var kidsArray = string.Join(" ", Enumerable.Range(3, pageCount).Select(n => $"{n} 0 R"));
        sb.AppendLine("2 0 obj");
        sb.AppendLine($"<< /Type /Pages /Kids [{kidsArray}] /Count {pageCount} >>");
        sb.AppendLine("endobj");
        
        // Font resources (objects after pages)
        var fontObjNum = 3 + pageCount * 2; // After all page and content objects
        sb.AppendLine($"{fontObjNum} 0 obj");
        sb.AppendLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        sb.AppendLine("endobj");
        
        sb.AppendLine($"{fontObjNum + 1} 0 obj");
        sb.AppendLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");
        sb.AppendLine("endobj");
        
        // Generate page objects (3 to 3+pageCount-1) and content objects
        for (int i = 0; i < pageCount; i++)
        {
            int pageObjNum = 3 + i;
            int contentObjNum = 3 + pageCount + i;
            
            // Page object
            sb.AppendLine($"{pageObjNum} 0 obj");
            sb.AppendLine("<< /Type /Page /Parent 2 0 R");
            sb.AppendLine($"/Resources << /Font << /F1 {fontObjNum} 0 R /F2 {fontObjNum + 1} 0 R >> >>");
            sb.AppendLine("/MediaBox [0 0 612 792]");
            sb.AppendLine($"/Contents {contentObjNum} 0 R >>");
            sb.AppendLine("endobj");
            
            // Content stream
            var contentBytes = Encoding.ASCII.GetBytes(pageContents[i]);
            sb.AppendLine($"{contentObjNum} 0 obj");
            sb.AppendLine($"<< /Length {contentBytes.Length} >>");
            sb.AppendLine("stream");
            sb.Append(pageContents[i]);
            sb.AppendLine("endstream");
            sb.AppendLine("endobj");
        }
        
        // Cross-reference table (simplified)
        var totalObjects = 3 + pageCount * 2 + 2; // catalog + pages + page objs + content objs + 2 fonts
        sb.AppendLine("xref");
        sb.AppendLine($"0 {totalObjects}");
        sb.AppendLine("0000000000 65535 f ");
        
        int offset = 9; // After %PDF-1.4
        for (int i = 1; i < totalObjects; i++)
        {
            sb.AppendLine($"{offset:D10} 00000 n ");
            offset += 150; // Approximate offset increment
        }
        
        // Trailer
        sb.AppendLine("trailer");
        sb.AppendLine($"<< /Size {totalObjects} /Root 1 0 R >>");
        sb.AppendLine("startxref");
        sb.AppendLine($"{offset}");
        sb.AppendLine("%%EOF");
        
        await File.WriteAllTextAsync(path, sb.ToString());
    }

    private static string CreatePageContent(string title, string[] lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BT");
        
        // Title - bold, larger
        sb.AppendLine("/F2 22 Tf");
        sb.AppendLine("72 720 Td");
        sb.AppendLine($"({EscapePdfString(title)}) Tj");
        
        // Body text
        sb.AppendLine("/F1 11 Tf");
        sb.AppendLine("0 -28 Td");
        
        foreach (var line in lines)
        {
            sb.AppendLine($"({EscapePdfString(line)}) Tj");
            sb.AppendLine("0 -15 Td");
        }
        
        sb.AppendLine("ET");
        return sb.ToString();
    }

    private static string EscapePdfString(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)");
    }

    /// <summary>
    /// Creates the JSON metadata file for the sample PDF.
    /// </summary>
    private static async Task CreateSampleMetadataAsync(string path)
    {
        var metadata = @"{
  ""version"": 1,
  ""lastWrite"": """ + DateTime.UtcNow.ToString("o") + @""",
  ""lastPageNo"": 0,
  ""pageNumberOffset"": 0,
  ""notes"": ""Sample PDF with comprehensive instructions for using SheetMusicViewer"",
  ""volumes"": [
    { ""fileName"": ""GettingStarted.pdf"", ""pageCount"": 9, ""rotation"": 0 }
  ],
  ""tableOfContents"": [
    { ""pageNo"": 0, ""songName"": ""Welcome"", ""composer"": ""Introduction"" },
    { ""pageNo"": 1, ""songName"": ""Getting Started"", ""composer"": ""Setup Guide"" },
    { ""pageNo"": 2, ""songName"": ""Multi-Volume PDFs"", ""composer"": ""Feature Guide"" },
    { ""pageNo"": 3, ""songName"": ""Singles Folders"", ""composer"": ""Feature Guide"" },
    { ""pageNo"": 4, ""songName"": ""Display & Navigation"", ""composer"": ""User Guide"" },
    { ""pageNo"": 5, ""songName"": ""Inking & Annotations"", ""composer"": ""User Guide"" },
    { ""pageNo"": 6, ""songName"": ""Performance & Caching"", ""composer"": ""Technical Info"" },
    { ""pageNo"": 7, ""songName"": ""PDF Table of Contents"", ""composer"": ""Feature Guide"" },
    { ""pageNo"": 8, ""songName"": ""About"", ""composer"": ""Calvin Hsia"" }
  ],
  ""favorites"": [],
  ""inkStrokes"": {}
}";
        
        await File.WriteAllTextAsync(path, metadata);
    }

    /// <summary>
    /// Integration test that copies sample music to a temp folder and verifies
    /// that the metadata loading process creates JSON files for PDFs without existing metadata.
    /// GettingStarted.pdf has pre-existing JSON, other PDFs do not.
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestMetadataCreationForSamplePdfs()
    {
        var sampleFolder = GetProjectSampleMusicFolder();
        Assert.IsTrue(Directory.Exists(sampleFolder), $"Sample folder should exist at {sampleFolder}");
        
        // Create a temp folder for the test
        var tempFolder = Path.Combine(Path.GetTempPath(), $"SheetMusicViewerTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
        LogMessage($"Created temp folder: {tempFolder}");
        
        try
        {
            // Copy all PDF files from sample folder to temp folder
            var pdfFiles = Directory.GetFiles(sampleFolder, "*.pdf");
            Assert.IsTrue(pdfFiles.Length > 0, "Sample folder should contain at least one PDF");
            
            foreach (var pdfFile in pdfFiles)
            {
                var destPath = Path.Combine(tempFolder, Path.GetFileName(pdfFile));
                File.Copy(pdfFile, destPath);
                LogMessage($"Copied: {Path.GetFileName(pdfFile)}");
            }
            
            // Copy the GettingStarted.json (it's bundled with the app)
            var gettingStartedJson = Path.Combine(sampleFolder, "GettingStarted.json");
            if (File.Exists(gettingStartedJson))
            {
                File.Copy(gettingStartedJson, Path.Combine(tempFolder, "GettingStarted.json"));
                LogMessage("Copied: GettingStarted.json (pre-existing metadata)");
            }
            
            // Count JSON files before loading - should be 1 (GettingStarted.json)
            var jsonFilesBefore = Directory.GetFiles(tempFolder, "*.json");
            Assert.AreEqual(1, jsonFilesBefore.Length, "Should have 1 JSON file before loading (GettingStarted.json)");
            
            // Load metadata using the same provider the Avalonia app uses
            var provider = new PdfToImageDocumentProvider();
            var (metadataList, folders) = await SheetMusicLib.PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
                tempFolder,
                provider,
                exceptionHandler: null,
                useParallelLoading: true);
            
            LogMessage($"Loaded {metadataList.Count} metadata entries");
            Assert.IsTrue(metadataList.Count > 0, "Should load at least one metadata entry");
            
            // Verify metadata was created correctly
            foreach (var metadata in metadataList)
            {
                var fileName = Path.GetFileName(metadata.FullPathFile);
                var hasPreExistingJson = fileName.Equals("GettingStarted.pdf", StringComparison.OrdinalIgnoreCase);
                
                if (hasPreExistingJson)
                {
                    Assert.IsFalse(metadata.IsDirty, $"GettingStarted.pdf should NOT be dirty (has pre-existing JSON)");
                }
                else
                {
                    Assert.IsTrue(metadata.IsDirty, $"{fileName} should be dirty (no pre-existing JSON)");
                }
                
                Assert.IsTrue(metadata.VolumeInfoList.Count > 0, "Should have at least one volume");
                
                var totalPages = metadata.VolumeInfoList.Sum(v => v.NPagesInThisVolume);
                Assert.IsTrue(totalPages > 0, $"Should have pages: {fileName}");
                
                LogMessage($"  {fileName}: {totalPages} pages, {metadata.TocEntries.Count} TOC entries, IsDirty={metadata.IsDirty}");
            }
            
            // Save dirty metadata to JSON
            int savedCount = 0;
            foreach (var metadata in metadataList.Where(m => m.IsDirty))
            {
                var saved = SheetMusicLib.PdfMetaDataCore.SaveToJson(metadata, forceSave: true);
                Assert.IsTrue(saved, $"Should save metadata for {Path.GetFileName(metadata.FullPathFile)}");
                savedCount++;
            }
            LogMessage($"Saved {savedCount} new JSON metadata files");
            
            // Verify JSON files exist for all PDFs
            var jsonFilesAfter = Directory.GetFiles(tempFolder, "*.json");
            Assert.AreEqual(metadataList.Count, jsonFilesAfter.Length, "Should have one JSON file per PDF");
            
            foreach (var jsonFile in jsonFilesAfter)
            {
                var content = await File.ReadAllTextAsync(jsonFile);
                Assert.IsTrue(content.StartsWith("{"), $"JSON file should be valid JSON: {Path.GetFileName(jsonFile)}");
                Assert.IsTrue(content.Contains("\"volumes\""), "JSON should contain volumes array");
                Assert.IsTrue(content.Contains("\"tableOfContents\""), "JSON should contain tableOfContents array");
                LogMessage($"  Verified: {Path.GetFileName(jsonFile)} ({content.Length:N0} bytes)");
            }
            
            // Reload metadata and verify it reads from JSON correctly
            var (reloadedList, _) = await SheetMusicLib.PdfMetaDataCore.LoadAllPdfMetaDataFromDiskAsync(
                tempFolder,
                provider,
                exceptionHandler: null,
                useParallelLoading: true);
            
            Assert.AreEqual(metadataList.Count, reloadedList.Count, "Should reload same number of entries");
            
            foreach (var reloaded in reloadedList)
            {
                Assert.IsFalse(reloaded.IsDirty, $"Reloaded metadata should not be dirty: {Path.GetFileName(reloaded.FullPathFile)}");
                LogMessage($"  Reloaded: {Path.GetFileName(reloaded.FullPathFile)} - IsDirty={reloaded.IsDirty}");
            }
            
            LogMessage("Integration test passed: JSON metadata files created and reloaded successfully");
        }
        finally
        {
            // Cleanup temp folder
            try
            {
                if (Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, recursive: true);
                    LogMessage($"Cleaned up temp folder: {tempFolder}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Warning: Could not delete temp folder: {ex.Message}");
            }
        }
    }
}
