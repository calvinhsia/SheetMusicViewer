using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System;
using System.IO;
using System.Text;

namespace AvaloniaTests.Tests;

/// <summary>
/// Shared helper methods and utilities for Avalonia tests
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Creates a minimal valid PDF file for testing purposes
    /// </summary>
    /// <param name="pageCount">Number of pages to generate (default: 2)</param>
    /// <returns>Path to the created PDF file</returns>
    public static string CreateTestPdf(int pageCount = 2)
    {
        if (pageCount < 1) pageCount = 1;
        
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");
        
        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.4");
        
        // Object 1: Catalog
        sb.AppendLine("1 0 obj");
        sb.AppendLine("<<");
        sb.AppendLine("/Type /Catalog");
        sb.AppendLine("/Pages 2 0 R");
        sb.AppendLine(">>");
        sb.AppendLine("endobj");
        
        // Object 2: Pages (parent of all page objects)
        sb.AppendLine("2 0 obj");
        sb.AppendLine("<<");
        sb.AppendLine("/Type /Pages");
        
        // Build Kids array - page objects start at object 3
        var kids = new StringBuilder("/Kids [");
        for (int i = 0; i < pageCount; i++)
        {
            kids.Append($"{3 + i} 0 R ");
        }
        kids.Append("]");
        sb.AppendLine(kids.ToString());
        sb.AppendLine($"/Count {pageCount}");
        sb.AppendLine(">>");
        sb.AppendLine("endobj");
        
        // Content objects start after all page objects
        int contentObjStart = 3 + pageCount;
        
        // Generate page objects (objects 3 to 3+pageCount-1)
        for (int i = 0; i < pageCount; i++)
        {
            int pageObjNum = 3 + i;
            int contentObjNum = contentObjStart + i;
            
            sb.AppendLine($"{pageObjNum} 0 obj");
            sb.AppendLine("<<");
            sb.AppendLine("/Type /Page");
            sb.AppendLine("/Parent 2 0 R");
            sb.AppendLine("/MediaBox [0 0 612 792]");
            sb.AppendLine($"/Contents {contentObjNum} 0 R");
            sb.AppendLine("/Resources <<");
            sb.AppendLine("/Font <<");
            sb.AppendLine("/F1 <<");
            sb.AppendLine("/Type /Font");
            sb.AppendLine("/Subtype /Type1");
            sb.AppendLine("/BaseFont /Helvetica");
            sb.AppendLine(">>");
            sb.AppendLine(">>");
            sb.AppendLine(">>");
            sb.AppendLine(">>");
            sb.AppendLine("endobj");
        }
        
        // Generate content stream objects
        for (int i = 0; i < pageCount; i++)
        {
            int contentObjNum = contentObjStart + i;
            int pageNum = i + 1;
            
            // Content stream with page text
            string streamContent = $"BT\n/F1 24 Tf\n100 700 Td\n(Test Page {pageNum}) Tj\nET";
            
            sb.AppendLine($"{contentObjNum} 0 obj");
            sb.AppendLine("<<");
            sb.AppendLine($"/Length {streamContent.Length}");
            sb.AppendLine(">>");
            sb.AppendLine("stream");
            sb.Append(streamContent);
            sb.AppendLine();
            sb.AppendLine("endstream");
            sb.AppendLine("endobj");
        }
        
        // xref table (simplified - not calculating exact offsets)
        int totalObjects = 2 + pageCount * 2; // catalog + pages + page objects + content objects
        sb.AppendLine("xref");
        sb.AppendLine($"0 {totalObjects + 1}");
        sb.AppendLine("0000000000 65535 f ");
        
        // Approximate offsets (PDF readers are generally forgiving)
        int offset = 9; // After %PDF-1.4
        for (int i = 1; i <= totalObjects; i++)
        {
            sb.AppendLine($"{offset:D10} 00000 n ");
            offset += 100; // Approximate offset increment
        }
        
        // Trailer
        sb.AppendLine("trailer");
        sb.AppendLine("<<");
        sb.AppendLine($"/Size {totalObjects + 1}");
        sb.AppendLine("/Root 1 0 R");
        sb.AppendLine(">>");
        sb.AppendLine("startxref");
        sb.AppendLine("0"); // Simplified - some readers will rebuild xref
        sb.AppendLine("%%EOF");
        
        File.WriteAllText(tempPath, sb.ToString());
        return tempPath;
    }

    /// <summary>
    /// Builds an Avalonia app configured for testing
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestHeadlessApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Builds an Avalonia app configured for PDF viewer testing
    /// </summary>
    public static AppBuilder BuildAvaloniaAppForPdfViewer()
        => AppBuilder.Configure<PdfViewerApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Counts DataGridRow elements in the visual tree of a DataGrid
    /// </summary>
    public static int CountDataGridRows(DataGrid grid)
    {
        int count = 0;
        try
        {
            void CountRows(Visual visual)
            {
                if (visual != null)
                {
                    if (visual.GetType().Name.Contains("DataGridRow"))
                    {
                        count++;
                    }
                    
                    foreach (var child in visual.GetVisualChildren())
                    {
                        if (child is Visual childVisual)
                        {
                            CountRows(childVisual);
                        }
                    }
                }
            }
            
            CountRows(grid);
        }
        catch
        {
            // If we can't inspect the visual tree, return 0
        }
        return count;
    }
}

/// <summary>
/// Test data class for virtualization and performance tests
/// </summary>
public class TestDataItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    
    public override string ToString() => Name;
}

/// <summary>
/// Simplified version of PdfMetaData for testing DataGrid with real class
/// </summary>
public class PdfMetaDataSimple
{
    public string FileName { get; set; } = string.Empty;
    public int NumPages { get; set; }
    public int NumSongs { get; set; }
    public int NumFavorites { get; set; }
    public int LastPageNo { get; set; }
    public string Notes { get; set; } = string.Empty;
    
    public override string ToString() => FileName;
}
