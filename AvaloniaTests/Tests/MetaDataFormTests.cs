using Avalonia.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Manual tests for MetaDataForm - an Avalonia equivalent of the WPF MetaDataForm
/// Tests viewing and editing PDF metadata (TOC entries, favorites, etc.)
/// </summary>
[TestClass]
[DoNotParallelize]
public class MetaDataFormTests : TestBase
{
    /// <summary>
    /// Path to the sample BMK file in TestAssets
    /// </summary>
    private static string GetSampleBmkPath()
    {
        // Find the TestAssets folder relative to the test execution directory
        var currentDir = AppContext.BaseDirectory;
        
        // Navigate up from bin\Debug\net8.0 to find the Tests folder
        var possiblePaths = new[]
        {
            Path.Combine(currentDir, "..", "..", "..", "..", "Tests", "TestAssets", "Sample59PianoSolosFull.bmk"),
            Path.Combine(currentDir, "..", "..", "..", "Tests", "TestAssets", "Sample59PianoSolosFull.bmk"),
            Path.Combine(currentDir, "TestAssets", "Sample59PianoSolosFull.bmk"),
            @"C:\Users\Calvinh\source\repos\SheetMusicViewer\Tests\TestAssets\Sample59PianoSolosFull.bmk"
        };

        foreach (var path in possiblePaths)
        {
            var normalizedPath = Path.GetFullPath(path);
            if (File.Exists(normalizedPath))
            {
                return normalizedPath;
            }
        }

        // Fallback: use the hardcoded path from the user's request
        return @"C:\Users\Calvinh\source\repos\SheetMusicViewer\Tests\TestAssets\Sample59PianoSolosFull.bmk";
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestMetaDataFormWithSampleBmk()
    {
        SkipIfCI("Manual test requires user interaction");

        var bmkPath = GetSampleBmkPath();
        
        if (!File.Exists(bmkPath))
        {
            Assert.Inconclusive($"Sample BMK file not found at: {bmkPath}");
        }

        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var viewModel = new MetaDataFormViewModel(bmkPath);
            var window = new MetaDataFormWindow(viewModel);
            
            lifetime.MainWindow = window;
            
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "? MetaDataFormWindow closed by user\n? TEST PASSED: MetaDataForm displayed and editable");
            
            window.Show();
            
            Trace.WriteLine("=== MetaDataForm Test with Sample59PianoSolosFull.bmk ===");
            Trace.WriteLine($"? Loaded BMK file: {bmkPath}");
            Trace.WriteLine($"? TOC Entries: {viewModel.TocEntries.Count}");
            Trace.WriteLine($"? Favorites: {viewModel.Favorites.Count}");
            Trace.WriteLine($"? Volumes: {viewModel.VolInfoDisplay.Count}");
            Trace.WriteLine("");
            Trace.WriteLine("Features to test:");
            Trace.WriteLine("  • View and edit TOC entries in DataGrid");
            Trace.WriteLine("  • Select a row to edit in the detail panel");
            Trace.WriteLine("  • Add new rows with 'Add Row' button");
            Trace.WriteLine("  • Delete rows with 'Delete Row' button");
            Trace.WriteLine("  • Export to clipboard (tab-separated)");
            Trace.WriteLine("  • Import from clipboard (tab-separated)");
            Trace.WriteLine("  • Edit PageNumberOffset");
            Trace.WriteLine("  • Edit Doc Notes");
            Trace.WriteLine("  • View Favorites list");
            Trace.WriteLine("  • View Volume Info");
            Trace.WriteLine("  • Sort columns by clicking headers");
            Trace.WriteLine("  • Resize columns by dragging");
            Trace.WriteLine("");
            Trace.WriteLine("Note: Cancel will close without saving.");
            Trace.WriteLine("      Save will write changes to the BMK file.");
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
    public async Task TestMetaDataFormWithEmptyData()
    {
        SkipIfCI("Manual test requires user interaction");

        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            // Create a temporary BMK file for testing new/empty data
            var tempBmkPath = Path.Combine(Path.GetTempPath(), $"TestMetaData_{Guid.NewGuid()}.bmk");
            
            try
            {
                var viewModel = new MetaDataFormViewModel();
                var window = new MetaDataFormWindow(viewModel);
                
                lifetime.MainWindow = window;
                
                window.Closed += (s, e) =>
                {
                    try
                    {
                        if (File.Exists(tempBmkPath))
                        {
                            File.Delete(tempBmkPath);
                        }
                    }
                    catch { }
                    
                    Trace.WriteLine("? MetaDataFormWindow closed by user");
                    Trace.WriteLine("? TEST PASSED: Empty MetaDataForm works correctly");
                    testCompleted.TrySetResult(true);
                    lifetime.Shutdown();
                };
                
                window.Show();
                
                Trace.WriteLine("=== MetaDataForm Test with Empty Data ===");
                Trace.WriteLine("? Created empty MetaDataForm");
                Trace.WriteLine("");
                Trace.WriteLine("Features to test:");
                Trace.WriteLine("  • Add new TOC entries with 'Add Row'");
                Trace.WriteLine("  • Edit the new entries");
                Trace.WriteLine("  • Delete entries");
                Trace.WriteLine("  • Import from clipboard");
                Trace.WriteLine("");
                Trace.WriteLine("Close the window when finished testing.");

                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error: {ex.Message}");
                testCompleted.TrySetException(ex);
            }
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
    public async Task TestMetaDataFormImportExport()
    {
        SkipIfCI("Manual test requires user interaction and clipboard");

        var bmkPath = GetSampleBmkPath();
        
        if (!File.Exists(bmkPath))
        {
            Assert.Inconclusive($"Sample BMK file not found at: {bmkPath}");
        }

        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var viewModel = new MetaDataFormViewModel(bmkPath);
            var window = new MetaDataFormWindow(viewModel);
            
            lifetime.MainWindow = window;
            
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "? Import/Export test completed");
            
            window.Show();
            
            Trace.WriteLine("=== MetaDataForm Import/Export Test ===");
            Trace.WriteLine("");
            Trace.WriteLine("Test procedure:");
            Trace.WriteLine("  1. Click 'Export to Clipboard'");
            Trace.WriteLine("  2. Open Excel or a text editor");
            Trace.WriteLine("  3. Paste - should see tab-separated data");
            Trace.WriteLine("  4. Modify some rows in Excel");
            Trace.WriteLine("  5. Copy the modified data");
            Trace.WriteLine("  6. Click 'Import from Clipboard'");
            Trace.WriteLine("  7. Verify the changes appear in the grid");
            Trace.WriteLine("");
            Trace.WriteLine("Expected clipboard format (tab-separated):");
            Trace.WriteLine("  PageNo<TAB>SongName<TAB>Composer<TAB>Date<TAB>Notes");
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
    public async Task TestMetaDataFormEditing()
    {
        SkipIfCI("Manual test requires user interaction");

        var bmkPath = GetSampleBmkPath();
        
        if (!File.Exists(bmkPath))
        {
            Assert.Inconclusive($"Sample BMK file not found at: {bmkPath}");
        }

        // Make a copy so we don't modify the original
        var tempBmkPath = Path.Combine(Path.GetTempPath(), $"TestMetaData_Edit_{Guid.NewGuid()}.bmk");
        File.Copy(bmkPath, tempBmkPath);

        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var viewModel = new MetaDataFormViewModel(tempBmkPath);
            var window = new MetaDataFormWindow(viewModel);
            
            lifetime.MainWindow = window;
            
            window.Closed += (s, e) =>
            {
                try
                {
                    if (File.Exists(tempBmkPath))
                    {
                        Trace.WriteLine($"Temp file exists after close: {tempBmkPath}");
                        // Check if it was modified
                        var fileInfo = new FileInfo(tempBmkPath);
                        Trace.WriteLine($"File last write: {fileInfo.LastWriteTime}");
                        File.Delete(tempBmkPath);
                    }
                }
                catch { }
                
                Trace.WriteLine("? MetaDataFormWindow closed");
                Trace.WriteLine("? TEST PASSED: Editing test completed");
                testCompleted.TrySetResult(true);
                lifetime.Shutdown();
            };
            
            window.Show();
            
            Trace.WriteLine("=== MetaDataForm Editing Test ===");
            Trace.WriteLine($"? Using temp copy: {tempBmkPath}");
            Trace.WriteLine("");
            Trace.WriteLine("Test procedure:");
            Trace.WriteLine("  1. Select a row in the DataGrid");
            Trace.WriteLine("  2. Edit fields in the detail panel (left side)");
            Trace.WriteLine("  3. Verify changes reflect in the DataGrid");
            Trace.WriteLine("  4. Double-click a cell to edit directly");
            Trace.WriteLine("  5. Add a new row, edit it");
            Trace.WriteLine("  6. Delete a row");
            Trace.WriteLine("  7. Click 'Save' to save changes");
            Trace.WriteLine("  8. Or click 'Cancel' to discard");
            Trace.WriteLine("");
            Trace.WriteLine("Two-way binding test:");
            Trace.WriteLine("  • Editing in detail panel should update grid");
            Trace.WriteLine("  • Editing in grid should update detail panel");
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
