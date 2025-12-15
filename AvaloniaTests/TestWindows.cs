using Avalonia.Controls;
using Avalonia.Layout;
using SheetMusicViewer.Desktop;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace AvaloniaTests;

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
        
        // Create the browse control with the reflection query (using virtualized ListBox)
        // Columns will be automatically generated: TypeName, Namespace, IsAbstract, MethodCount, PropertyCount
        _browseControl = new BrowseControl(query, colWidths: new[] { 250, 350, 100, 120, 120 });
        
        Content = _browseControl;
        
        Trace.WriteLine($"✓ BrowseListWindow created with reflection-based query using virtualized ListBox");
        Trace.WriteLine($"✓ Columns: TypeName, Namespace, IsAbstract, MethodCount, PropertyCount");
        Trace.WriteLine($"✓ Try filtering by type name (e.g., 'Button', 'Panel', 'Control')");
    }
}
