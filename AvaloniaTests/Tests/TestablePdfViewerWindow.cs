using Avalonia.Controls;
using Avalonia.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Testable version of PdfViewerWindow that exposes internal properties for testing
/// </summary>
public class TestablePdfViewerWindow : Window
{
    private string? _pdfFileName;
    private int _maxPageNumberMinus1;
    private string _description0 = string.Empty;
    private string _description1 = string.Empty;
    private string _pdfTitle = string.Empty;
    private bool _pdfUIEnabled;

    public TestablePdfViewerWindow(string pdfFilePath)
    {
        _pdfFileName = pdfFilePath;
        Title = "Testable PDF Viewer Window";
        Width = 800;
        Height = 600;
        
        // Initialize properties
        _maxPageNumberMinus1 = 0;
        _description0 = "Test page 0";
        _description1 = "Test page 1";
        _pdfTitle = System.IO.Path.GetFileName(pdfFilePath);
        _pdfUIEnabled = true;
    }

    public string? GetPdfFileName() => _pdfFileName;
    
    public int MaxPageNumberMinus1 
    { 
        get => _maxPageNumberMinus1;
        set => _maxPageNumberMinus1 = value;
    }
    
    public string Description0 
    { 
        get => _description0;
        set => _description0 = value;
    }
    
    public string Description1 
    { 
        get => _description1;
        set => _description1 = value;
    }
    
    public string PdfTitle 
    { 
        get => _pdfTitle;
        set => _pdfTitle = value;
    }
    
    public bool PdfUIEnabled 
    { 
        get => _pdfUIEnabled;
        set => _pdfUIEnabled = value;
    }

    /// <summary>
    /// Simulates triggering the PDF load process for testing
    /// </summary>
    public async Task TriggerLoadAsync()
    {
        await Task.Delay(100); // Simulate async load
        
        if (!string.IsNullOrEmpty(_pdfFileName) && System.IO.File.Exists(_pdfFileName))
        {
            try
            {
                // Simulate PDF loading
                _maxPageNumberMinus1 = 1; // At least 2 pages for test PDF
                _description0 = $"Page 0 from {System.IO.Path.GetFileName(_pdfFileName)}";
                _description1 = $"Page 1 from {System.IO.Path.GetFileName(_pdfFileName)}";
                _pdfUIEnabled = true;
                
                Trace.WriteLine($"TestablePdfViewerWindow: Simulated load of {_pdfFileName}");
            }
            catch (System.Exception ex)
            {
                _description0 = $"Error loading PDF: {ex.Message}";
                _description1 = string.Empty;
                _pdfUIEnabled = false;
                Trace.WriteLine($"TestablePdfViewerWindow error: {ex}");
            }
        }
        else
        {
            _description0 = "Error: PDF file not found or path is empty";
            _pdfUIEnabled = false;
        }
    }

    /// <summary>
    /// Helper to find a control by name (simulates FindName in WPF)
    /// </summary>
    public Control? FindControl<T>(string name) where T : Control
    {
        // In Avalonia, we'd need to traverse the visual tree
        // For testing purposes, return a placeholder
        return null;
    }
}
