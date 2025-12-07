using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaSimpleApp;

// Test app for simple DataGrid
public class TestSimpleDataGridApp : Application
{
    public static Func<Application, IClassicDesktopStyleApplicationLifetime, Task>? OnSetupWindow;
    
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
    
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OnSetupWindow != null)
            {
                _ = OnSetupWindow.Invoke(this, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

// Simplest possible DataGrid window with hardcoded everything
public class SimpleDataGridWindow : Window
{
    public SimpleDataGridWindow()
    {
        Title = "Simple Hardcoded DataGrid Test";
        Width = 800;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.LightGray;
        
        // Create the simplest possible DataGrid
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = true, // Let Avalonia generate columns automatically
            IsReadOnly = true,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.White,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(10),
            RowHeight = 30,
            // Try setting explicit Height instead of letting it stretch
            Height = 500
        };
        
        // Create simple hardcoded data
        var items = new List<TestItem>
        {
            new TestItem { Name = "Item 1", Value = "Value 1", Number = 100 },
            new TestItem { Name = "Item 2", Value = "Value 2", Number = 200 },
            new TestItem { Name = "Item 3", Value = "Value 3", Number = 300 },
            new TestItem { Name = "Item 4", Value = "Value 4", Number = 400 },
            new TestItem { Name = "Item 5", Value = "Value 5", Number = 500 }
        };
        
        Trace.WriteLine($"? Created {items.Count} hardcoded TestItem instances");
        
        // Set ItemsSource IMMEDIATELY to test if timing is really the issue
        dataGrid.ItemsSource = items;
        Trace.WriteLine($"? ItemsSource set IMMEDIATELY in constructor to {items.Count} items");
        
        // Also hook Loaded to check status
        dataGrid.Loaded += (s, e) =>
        {
            Trace.WriteLine($"DataGrid.Loaded event fired:");
            Trace.WriteLine($"  Bounds = {dataGrid.Bounds}");
            Trace.WriteLine($"  IsVisible = {dataGrid.IsVisible}");
            Trace.WriteLine($"  ItemsSource count = {items.Count}");
            Trace.WriteLine($"  AutoGenerateColumns = {dataGrid.AutoGenerateColumns}");
            Trace.WriteLine($"  Columns.Count = {dataGrid.Columns.Count}");
            Trace.WriteLine($"  Height = {dataGrid.Height}, RowHeight = {dataGrid.RowHeight}");
            
            // CRITICAL: Force another layout pass after ItemsSource is set
            Dispatcher.UIThread.Post(() =>
            {
                dataGrid.InvalidateMeasure();
                dataGrid.InvalidateArrange();
                dataGrid.UpdateLayout();
                
                Trace.WriteLine($"  After UpdateLayout: Columns.Count = {dataGrid.Columns.Count}");
                
                // Check visual children immediately after forced layout
                var hasVisualChildren = Avalonia.VisualTree.VisualExtensions.GetVisualChildren(dataGrid).Any();
                var rowCount = Avalonia.VisualTree.VisualExtensions.GetVisualChildren(dataGrid).OfType<DataGridRow>().Count();
                Trace.WriteLine($"  Immediately after layout: HasVisualChildren={hasVisualChildren}, DataGridRow count={rowCount}");
                
                // Try to enumerate ALL visual children to see what's actually there
                var allChildren = Avalonia.VisualTree.VisualExtensions.GetVisualChildren(dataGrid).ToList();
                Trace.WriteLine($"  Visual children types ({allChildren.Count} total):");
                foreach (var child in allChildren.Take(10))  // Show first 10
                {
                    Trace.WriteLine($"    - {child.GetType().Name}");
                }
                
                // Try GetVisualDescendants to go deeper
                var allDescendants = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dataGrid).ToList();
                Trace.WriteLine($"  Visual DESCENDANTS ({allDescendants.Count} total):");
                var descendantTypes = allDescendants.GroupBy(d => d.GetType().Name)
                    .Select(g => $"{g.Key} ({g.Count()})")
                    .ToList();
                foreach (var typeInfo in descendantTypes.Take(20))
                {
                    Trace.WriteLine($"    - {typeInfo}");
                }
            }, DispatcherPriority.Loaded);
            
            // Also check after a delay
            Task.Delay(2000).ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var hasVisualChildren = Avalonia.VisualTree.VisualExtensions.GetVisualChildren(dataGrid).Any();
                    var rowCount = Avalonia.VisualTree.VisualExtensions.GetVisualChildren(dataGrid).OfType<DataGridRow>().Count();
                    Trace.WriteLine($"DataGrid DIAGNOSTIC (after 2s): Columns={dataGrid.Columns.Count}, HasVisualChildren={hasVisualChildren}, DataGridRow count={rowCount}");
                    
                    // Check for any ScrollViewer
                    var scrollViewerCount = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dataGrid)
                        .Count(c => c.GetType().Name.Contains("Scroll"));
                    Trace.WriteLine($"  ScrollViewer-related controls = {scrollViewerCount}");
                });
            });
        };
        
        Content = dataGrid;
        
        Trace.WriteLine($"? SimpleDataGridWindow created with explicit Height=500");
    }
}

// Simple test class to verify DataGrid works with real classes
public class TestItem
{
    public string Name { get; set; }
    public string Value { get; set; }
    public int Number { get; set; }
}
