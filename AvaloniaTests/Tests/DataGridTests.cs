using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

[TestClass]
[DoNotParallelize]
public class DataGridTests : TestBase
{
    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestDataGridWorking()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = new DataGridTestWindow();
            lifetime.MainWindow = window;
            
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "✓ DataGridTestWindow closed by user\n✓ TEST PASSED: DataGrid displayed rows successfully");
            
            window.Show();
            
            Trace.WriteLine("=== DataGrid Working Test ===");
            Trace.WriteLine("✓ DataGridTestWindow created and shown");
            Trace.WriteLine("✓ DataGrid with 15 people displayed");
            Trace.WriteLine("");
            Trace.WriteLine("Features to test:");
            Trace.WriteLine("  • Edit cells (double-click)");
            Trace.WriteLine("  • Toggle checkboxes");
            Trace.WriteLine("  • Sort by clicking column headers");
            Trace.WriteLine("  • Resize columns by dragging headers");
            Trace.WriteLine("  • Reorder columns by dragging headers");
            Trace.WriteLine("");
            Trace.WriteLine("Close the window when finished testing.");

            await Task.Delay(100);
        }, configureApp: app =>
        {
            var dataGridStyles = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Avalonia.Controls.DataGrid/"))
            {
                Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
            };
            app.Styles.Add(dataGridStyles);
        });
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestDataGridWithRealClass()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var items = new ObservableCollection<PdfMetaDataSimple>();
            for (int i = 1; i <= 20; i++)
            {
                items.Add(new PdfMetaDataSimple
                {
                    FileName = $"Document_{i:D2}.pdf",
                    NumPages = 10 + i * 5,
                    NumSongs = i % 7,
                    NumFavorites = i % 3,
                    LastPageNo = i * 2,
                    Notes = $"Test notes for document {i}"
                });
            }

            var dataGrid = new DataGrid
            {
                ItemsSource = items,
                AutoGenerateColumns = true,
                CanUserReorderColumns = true,
                CanUserResizeColumns = true,
                CanUserSortColumns = true,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                IsReadOnly = false
            };

            var window = new Window
            {
                Title = "DataGrid with AutoGenerateColumns (Programmatic)",
                Width = 1000,
                Height = 600,
                Content = dataGrid
            };
            
            lifetime.MainWindow = window;
            
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "✓ DataGrid window closed by user\n✓ TEST PASSED: Programmatic DataGrid with AutoGenerateColumns works!");
            
            window.Show();
            
            Trace.WriteLine("=== DataGrid with PdfMetaDataSimple - Programmatic Solution ===");
            Trace.WriteLine("✓ DataGrid created programmatically using AutoGenerateColumns");
            Trace.WriteLine("✓ Works with compiled bindings enabled");
            Trace.WriteLine("✓ DataGrid with 20 PdfMetaDataSimple items");
            Trace.WriteLine("");
            Trace.WriteLine("Features:");
            Trace.WriteLine("  • All 6 properties auto-generated as columns");
            Trace.WriteLine("  • Sortable, resizable, reorderable columns");
            Trace.WriteLine("  • Editable cells");
            Trace.WriteLine("");
            Trace.WriteLine("Note: AutoGenerateColumns=true bypasses need for x:DataType");
            Trace.WriteLine("For custom columns, use XAML with x:DataType (see TestDataGridWorking)");
            Trace.WriteLine("");
            Trace.WriteLine("Close the window when finished testing.");

            await Task.Delay(100);
        }, configureApp: app =>
        {
            var dataGridStyles = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Avalonia.Controls.DataGrid/"))
            {
                Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
            };
            app.Styles.Add(dataGridStyles);
        });
    }
}
