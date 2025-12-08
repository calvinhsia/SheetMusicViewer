using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System;
using System.IO;

namespace AvaloniaTests.Tests;

/// <summary>
/// Shared helper methods and utilities for Avalonia tests
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Creates a minimal valid PDF file for testing purposes with 2 pages
    /// </summary>
    public static string CreateTestPdf()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");
        
        var pdfContent = @"%PDF-1.4
1 0 obj
<<
/Type /Catalog
/Pages 2 0 R
>>
endobj
2 0 obj
<<
/Type /Pages
/Kids [3 0 R 4 0 R]
/Count 2
>>
endobj
3 0 obj
<<
/Type /Page
/Parent 2 0 R
/MediaBox [0 0 612 792]
/Contents 5 0 R
/Resources <<
/Font <<
/F1 <<
/Type /Font
/Subtype /Type1
/BaseFont /Helvetica
>>
>>
>>
endobj
4 0 obj
<<
/Type /Page
/Parent 2 0 R
/MediaBox [0 0 612 792]
/Contents 6 0 R
/Resources <<
/Font <<
/F1 <<
/Type /Font
/Subtype /Type1
/BaseFont /Helvetica
>>
>>
>>
endobj
5 0 obj
<<
/Length 44
>>
stream
BT
/F1 24 Tf
100 700 Td
(Test Page 1) Tj
ET
endstream
endobj
6 0 obj
<<
/Length 44
>>
stream
BT
/F1 24 Tf
100 700 Td
(Test Page 2) Tj
ET
endstream
endobj
xref
0 7
0000000000 65535 f 
0000000009 00000 n 
0000000058 00000 n 
0000000115 00000 n 
0000000299 00000 n 
0000000483 00000 n 
0000000576 00000 n 
trailer
<<
/Size 7
/Root 1 0 R
>>
startxref
669
%%EOF";
        
        File.WriteAllText(tempPath, pdfContent);
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
