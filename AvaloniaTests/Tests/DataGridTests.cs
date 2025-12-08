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
public class DataGridTests
{
    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestDataGridWithRealClass()
    {
        if (Environment.GetEnvironmentVariable("CI") == "true" || 
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Assert.Inconclusive("Test skipped in headless CI environment - requires display");
            return;
        }

        var testCompleted = new TaskCompletionSource<bool>();
        var uiThread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<TestHeadlessApp>()
                    .UsePlatformDetect()
                    .WithInterFont()
                    .LogToTrace()
                    .StartWithClassicDesktopLifetime(Array.Empty<string>());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"UI thread error: {ex.Message}");
                testCompleted.TrySetException(ex);
            }
        });

        if (OperatingSystem.IsWindows())
        {
            uiThread.SetApartmentState(ApartmentState.STA);
        }
        uiThread.Start();

        await Task.Delay(1000);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                Trace.WriteLine("=== AGGRESSIVE DataGrid Troubleshooting ===");
                Trace.WriteLine("Trying multiple approaches to force DataGrid to display data...");
                Trace.WriteLine("");
                
                var items = new ObservableCollection<PdfMetaDataSimple>();
                for (int i = 0; i < 20; i++)
                {
                    items.Add(new PdfMetaDataSimple
                    {
                        FileName = $"Book{i:D3}.pdf",
                        NumPages = 50 + i * 10,
                        NumSongs = 10 + i,
                        NumFavorites = i % 5,
                        LastPageNo = i * 3,
                        Notes = i % 3 == 0 ? "Has TOC" : (i % 3 == 1 ? "Classical" : "Jazz")
                    });
                }
                
                Trace.WriteLine($"? Created {items.Count} PdfMetaDataSimple items");
                Trace.WriteLine("");
                
                Trace.WriteLine("APPROACH 1: Manual columns with explicit bindings");
                var dataGrid1 = CreateDataGridWithManualColumns(items);
                
                Trace.WriteLine("APPROACH 2: Auto-generate columns");
                var dataGrid2 = new DataGrid
                {
                    ItemsSource = items,
                    AutoGenerateColumns = true,
                    Width = 900,
                    Height = 200
                };
                
                Trace.WriteLine("APPROACH 3: Set ItemsSource after window shown");
                var dataGrid3 = new DataGrid
                {
                    AutoGenerateColumns = true,
                    Width = 900,
                    Height = 200
                };
                
                var stackPanel = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Vertical,
                    Spacing = 20
                };
                
                AddGridToStack(stackPanel, "APPROACH 1: Manual columns with explicit bindings", dataGrid1);
                AddGridToStack(stackPanel, "APPROACH 2: Auto-generate columns", dataGrid2);
                AddGridToStack(stackPanel, "APPROACH 3: ItemsSource set AFTER window shown", dataGrid3);
                
                var scrollViewer = new ScrollViewer { Content = stackPanel };
                
                var window = new Window
                {
                    Title = "DataGrid Troubleshooting - 3 Approaches",
                    Width = 1000,
                    Height = 800,
                    Content = scrollViewer,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                
                window.Closed += (s, e) =>
                {
                    Trace.WriteLine("Window closed by user");
                    testCompleted.TrySetResult(true);
                    
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                    {
                        lifetime.Shutdown();
                    }
                };
                
                window.Show();
                
                await Task.Delay(500);
                dataGrid3.ItemsSource = items;
                
                await Task.Delay(500);
                
                Trace.WriteLine("");
                Trace.WriteLine("=== DIAGNOSTIC RESULTS ===");
                Trace.WriteLine($"Approach 1 columns: {dataGrid1.Columns.Count}");
                Trace.WriteLine($"Approach 2 columns: {dataGrid2.Columns.Count}");
                Trace.WriteLine($"Approach 3 columns: {dataGrid3.Columns.Count}");
                
                Trace.WriteLine("");
                Trace.WriteLine("Checking visual tree for DataGridRow elements...");
                
                CheckDataGridRows(dataGrid1, 1);
                CheckDataGridRows(dataGrid2, 2);
                CheckDataGridRows(dataGrid3, 3);
                
                Trace.WriteLine("");
                Trace.WriteLine("? VISUAL CHECK: Do you see DATA in ANY of the 3 grids?");
                Trace.WriteLine("Close the window when done checking.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Test error: {ex}");
                testCompleted.SetException(ex);
                
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.Shutdown();
                }
            }
        });

        await testCompleted.Task;
        uiThread.Join(2000);
        
        Trace.WriteLine("? Test completed");
    }

    private static DataGrid CreateDataGridWithManualColumns(ObservableCollection<PdfMetaDataSimple> items)
    {
        var dataGrid = new DataGrid
        {
            ItemsSource = items,
            AutoGenerateColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            SelectionMode = DataGridSelectionMode.Extended,
            Width = 900,
            Height = 200
        };
        
        dataGrid.Columns.Add(new DataGridTextColumn 
        { 
            Header = "File Name", 
            Binding = new Avalonia.Data.Binding("FileName"),
            Width = new DataGridLength(200)
        });
        dataGrid.Columns.Add(new DataGridTextColumn 
        { 
            Header = "Pages", 
            Binding = new Avalonia.Data.Binding("NumPages"),
            Width = new DataGridLength(80)
        });
        dataGrid.Columns.Add(new DataGridTextColumn 
        { 
            Header = "Songs", 
            Binding = new Avalonia.Data.Binding("NumSongs"),
            Width = new DataGridLength(80)
        });
        dataGrid.Columns.Add(new DataGridTextColumn 
        { 
            Header = "Favorites", 
            Binding = new Avalonia.Data.Binding("NumFavorites"),
            Width = new DataGridLength(80)
        });
        dataGrid.Columns.Add(new DataGridTextColumn 
        { 
            Header = "Last Page", 
            Binding = new Avalonia.Data.Binding("LastPageNo"),
            Width = new DataGridLength(80)
        });
        dataGrid.Columns.Add(new DataGridTextColumn 
        { 
            Header = "Notes", 
            Binding = new Avalonia.Data.Binding("Notes"),
            Width = new DataGridLength(150)
        });
        
        return dataGrid;
    }

    private static void AddGridToStack(StackPanel stackPanel, string title, DataGrid dataGrid)
    {
        stackPanel.Children.Add(new TextBlock 
        { 
            Text = title,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(10)
        });
        stackPanel.Children.Add(dataGrid);
    }

    private static void CheckDataGridRows(DataGrid grid, int approachNumber)
    {
        var rowCount = TestHelpers.CountDataGridRows(grid);
        Trace.WriteLine($"  Approach {approachNumber}: {rowCount} DataGridRow elements found");
        
        if (rowCount > 0)
        {
            Trace.WriteLine($"  ? APPROACH {approachNumber} MIGHT BE WORKING!");
        }
        else
        {
            Trace.WriteLine($"  ? Approach {approachNumber} has no rows");
        }
    }
}
