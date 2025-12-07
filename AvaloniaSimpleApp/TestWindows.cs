using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace AvaloniaSimpleApp;

// Testable version of PdfViewerWindow that exposes internal state and allows custom PDF path
public class TestablePdfViewerWindow : PdfViewerWindow
{
    private readonly string _customPdfPath;

    public TestablePdfViewerWindow(string pdfPath) : base()
    {
        _customPdfPath = pdfPath;
        
        // Override the PDF file path using reflection BEFORE any initialization
        var field = typeof(PdfViewerWindow).GetField("_pdfFileName", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(this, pdfPath);
        }
        
        // Update the title to reflect the test PDF
        PdfTitle = Path.GetFileName(pdfPath);
        
        // Override the initial values that the constructor set
        CurrentPageNumber = 1;
        MaxPageNumberMinus1 = 1; // Will be updated after PDF loads
        PdfUIEnabled = true;
        
        Trace.WriteLine($"TestablePdfViewerWindow created with path: {pdfPath}");
    }

    public string GetPdfFileName()
    {
        // Access the private field through reflection
        var field = typeof(PdfViewerWindow).GetField("_pdfFileName", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var fileName = field?.GetValue(this) as string ?? string.Empty;
        Trace.WriteLine($"GetPdfFileName returning: {fileName}");
        return fileName;
    }

    public async Task TriggerLoadAsync()
    {
        Trace.WriteLine($"TriggerLoadAsync called, file exists: {File.Exists(_customPdfPath)}");
        
        // Manually call the load method that would normally be triggered by Loaded event
        var method = typeof(PdfViewerWindow).GetMethod("LoadAndDisplayPagesAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        if (method != null)
        {
            try
            {
                var task = method.Invoke(this, null) as Task;
                if (task != null)
                {
                    await task;
                    Trace.WriteLine($"LoadAndDisplayPagesAsync completed successfully");
                    Trace.WriteLine($"After load: Description0='{Description0}', Description1='{Description1}'");
                    Trace.WriteLine($"After load: MaxPageNumberMinus1={MaxPageNumberMinus1}, CurrentPageNumber={CurrentPageNumber}");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exception in TriggerLoadAsync: {ex}");
                throw;
            }
        }
        else
        {
            Trace.WriteLine("LoadAndDisplayPagesAsync method not found!");
        }
    }
}

// BrowseList-style window with DataGrid populated from reflection
public class BrowseListWindow : Window
{
    private BrowseControl _browseControl;

    public BrowseListWindow()
    {
        Title = "Browse Avalonia Types - Reflection Query Test";
        Width = 1400;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        
        // Create a LINQ reflection query over Avalonia types
        // This demonstrates the real-world usage: browsing types from assemblies with automatic column generation
        var query = from type in typeof(Avalonia.Controls.Button).Assembly.GetTypes()
                    where type.IsClass && type.IsPublic
                    select new
                    {
                        TypeName = type.Name,
                        Namespace = type.Namespace ?? string.Empty,
                        IsAbstract = type.IsAbstract,
                        MethodCount = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length,
                        PropertyCount = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length
                    };
        
        var typeCount = query.Count();

        Trace.WriteLine($"✓ Creating BrowseControl with LINQ reflection query");
        Trace.WriteLine($"✓ Query returns {typeCount} public classes from Avalonia.Controls assembly");
        Trace.WriteLine($"✓ Query uses anonymous type with computed properties (MethodCount, PropertyCount)");
        
        // Create the browse control with the reflection query
        // Columns will be automatically generated: TypeName, Namespace, IsAbstract, MethodCount, PropertyCount
        _browseControl = new BrowseControl(query, colWidths: new[] { 250, 350, 100, 120, 120 });
        
        Content = _browseControl;
        
        Trace.WriteLine($"✓ BrowseListWindow created with reflection-based query");
        Trace.WriteLine($"✓ Columns: TypeName, Namespace, IsAbstract, MethodCount, PropertyCount");
        Trace.WriteLine($"✓ Try filtering by type name (e.g., 'Button', 'Panel', 'Control')");
    }
}
