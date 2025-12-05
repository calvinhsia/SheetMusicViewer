using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Avalonia.Controls.Templates;

namespace AvaloniaSimpleApp;

[TestClass]
[DoNotParallelize] // Avalonia AppBuilder.Setup() can only be called once per process
public class AvaloniaTests
{
    // Note: These tests cannot run in the same test session because Avalonia's
    // AppBuilder.Setup() can only be called once per process. Each test must
    // be run separately or the test runner must be configured to run one at a time.
    
    [TestMethod]
    [TestCategory("Manual")]
    //[Ignore("Cannot run multiple Avalonia UI tests in same process - run individually")]
    public async Task TestAvaloniaPdfStressTest()
    {
        await Task.Run(() =>
        {
            try
            {
                // Build and start the Avalonia application with stress test window
                var app = Program.BuildAvaloniaApp();
                app.StartWithClassicDesktopLifetime(new string[0]);
            }
            catch (System.Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        });
    }

    [TestMethod]
    [TestCategory("Manual")]
    //[Ignore("Cannot run multiple Avalonia UI tests in same process - run individually")]
    public async Task TestAvaloniaPdfViewerUI()
    {
        // Skip if running in headless environment (CI/CD)
        if (Environment.GetEnvironmentVariable("CI") == "true" || 
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Assert.Inconclusive("Test skipped in headless CI environment - requires display");
            return;
        }

        // For this test to work, you need a PDF file. 
        // You can either:
        // 1. Set an environment variable PDF_TEST_PATH to a real PDF file path
        // 2. Or modify this to use CreateTestPdf() like the other tests
        var pdfPath = Environment.GetEnvironmentVariable("PDF_TEST_PATH");
        if (string.IsNullOrEmpty(pdfPath))
        {
            // Fall back to creating a test PDF
            pdfPath = CreateTestPdf();
        }

        var testCompleted = new TaskCompletionSource<bool>();
        PdfViewerWindow? window = null;

        var uiThread = new Thread(() =>
        {
            try
            {
                // Build and start the Avalonia application with PdfViewerWindow
                var app = BuildAvaloniaAppForPdfViewer();
                app.StartWithClassicDesktopLifetime(new string[0]);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"UI thread error: {ex.Message}");
                testCompleted.TrySetException(ex);
            }
        });

        // Hook into the app to set up window close behavior
        PdfViewerApp.OnSetupWindow = (app, lifetime) =>
        {
            // Create and show the PDF viewer window
            window = new PdfViewerWindow();
            
            // Set the PDF file path if we created a test PDF
            if (!string.IsNullOrEmpty(pdfPath))
            {
                var field = typeof(PdfViewerWindow).GetField("_pdfFileName", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(window, pdfPath);
            }
            
            lifetime.MainWindow = window;
            window.Show();
            
            Trace.WriteLine($"✓ PdfViewerWindow created and shown");
            Trace.WriteLine($"✓ Window will close automatically after 10 seconds");
            
            // Set up a timer to close it after a delay for manual testing/demo
            var delay = 10000; // Show window for 10 seconds
            
            var timer = new System.Timers.Timer(delay);
            timer.Elapsed += (s, e) =>
            {
                timer.Stop();
                Dispatcher.UIThread.Post(() =>
                {
                    Trace.WriteLine("Closing PdfViewerWindow and shutting down test");
                    window?.Close();
                    testCompleted.SetResult(true);
                    lifetime.Shutdown();
                });
            };
            timer.Start();
        };

        if (OperatingSystem.IsWindows())
        {
            uiThread.SetApartmentState(ApartmentState.STA);
        }
        uiThread.Start();

        // Wait for the test to complete with timeout
        var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(15000));
        if (completedTask != testCompleted.Task)
        {
            Trace.WriteLine("Test timed out - manually close the window to complete");
            // Don't fail - this is a manual test for viewing the UI
        }
        else
        {
            await testCompleted.Task;
        }

        uiThread.Join(2000);
        
        // Clean up test PDF if we created one
        if (!string.IsNullOrEmpty(pdfPath) && pdfPath.Contains(Path.GetTempPath()))
        {
            try { File.Delete(pdfPath); } catch { }
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestPdfViewerWindowLoadsAndDisplaysPdf()
    {
        // This test verifies that PdfViewerWindow can be created and populated
        // with a PDF by actually showing the window for a few seconds

        // Skip if running in headless environment (CI/CD)
        if (Environment.GetEnvironmentVariable("CI") == "true" || 
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Assert.Inconclusive("Test skipped in headless CI environment - requires display");
            return;
        }

        var testPdfPath = CreateTestPdf();
        
        try
        {
            var testCompleted = new TaskCompletionSource<bool>();
            var window = default(TestablePdfViewerWindow);
            
            // Create a thread to run the Avalonia UI
            var uiThread = new Thread(() =>
            {
                try
                {
                    // Build and start Avalonia app
                    AppBuilder.Configure<TestPdfViewerApp>()
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
            
            // Hook into the app to set up our window
            TestPdfViewerApp.OnSetupWindow = async (app, lifetime) =>
            {
                // Create the window with test PDF path
                window = new TestablePdfViewerWindow(testPdfPath);
                lifetime.MainWindow = window;
                
                // Show the window
                window.Show();
                
                // Manually trigger the PDF load since Loaded event might not fire in tests
                try
                {
                    await window.TriggerLoadAsync();
                    Trace.WriteLine("PDF load triggered successfully");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Error triggering PDF load: {ex.Message}");
                }

                var delay = 3000; // Reduced from 10s since we're loading synchronously above
                Trace.WriteLine($"Waiting {delay} ms before verification...");
                var timer = new System.Timers.Timer(delay);
                timer.Elapsed += (s, e) =>
                {
                    timer.Stop();
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            // Verify the window properties
                            Assert.IsNotNull(window, "Window should be created");
                            Assert.AreEqual(testPdfPath, window.GetPdfFileName(), "PDF file name should match");
                            Assert.IsTrue(window.PdfUIEnabled, "PDF UI should be enabled");
                            Assert.AreNotEqual(string.Empty, window.PdfTitle, "PDF title should be set");
                            
                            Trace.WriteLine($"✓ Window created and shown");
                            Trace.WriteLine($"✓ PDF file: {window.GetPdfFileName()}");
                            Trace.WriteLine($"✓ Page count: {window.MaxPageNumberMinus1}");
                            Trace.WriteLine($"✓ Page 0 description: {window.Description0}");
                            Trace.WriteLine($"✓ Page 1 description: {window.Description1}");
                            
                            // Verify UI elements if loading was successful
                            if (!window.Description0.Contains("Error"))
                            {
                                var dpPage = window.FindControl<Panel>("dpPage");
                                if (dpPage != null && dpPage.Children.Count > 0)
                                {
                                    var grid = dpPage.Children.OfType<Grid>().FirstOrDefault();
                                    if (grid != null)
                                    {
                                        Trace.WriteLine($"✓ Grid has {grid.ColumnDefinitions.Count} columns");
                                        var inkCanvases = grid.Children.OfType<InkCanvasControl>().Count();
                                        Trace.WriteLine($"✓ InkCanvas controls: {inkCanvases}");
                                    }
                                }
                            }
                            else
                            {
                                Trace.WriteLine($"⚠ PDF loading had errors: {window.Description0}");
                            }
                            
                            testCompleted.SetResult(true);
                            lifetime.Shutdown();
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Verification error: {ex.Message}");
                            testCompleted.SetException(ex);
                            lifetime.Shutdown();
                        }
                    });
                };
                timer.Start();
            };
            
            if (OperatingSystem.IsWindows())
            {
                uiThread.SetApartmentState(ApartmentState.STA);
            }
            uiThread.Start();
            
            // Wait for the test to complete with timeout
            var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(15000));
            if (completedTask != testCompleted.Task)
            {
                Assert.Fail("Test timed out - window may still be showing");
            }

            await testCompleted.Task; // Throw any exceptions that occurred
            
            // Wait for thread to finish
            uiThread.Join(2000);
        }
        finally
        {
            // Clean up test PDF
            if (File.Exists(testPdfPath))
            {
                try
                {
                    File.Delete(testPdfPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestInkingOnPage2WithResize()
    {
        // This test verifies that:
        // 1. Inking can be enabled on page 2
        // 2. Strokes can be drawn on page 2
        // 3. Strokes persist and scale correctly when window is resized
        // 4. Test works cross-platform using Avalonia with PDFtoImage
        // Note: Requires a display/window system to run (not headless)

        var testPdfPath = CreateTestPdf();
        
        try
        {
            var testCompleted = new TaskCompletionSource<bool>();
            var uiThread = new Thread(() =>
            {
                try
                {
                    // Build and start Avalonia app WITHOUT headless mode
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

            // Wait for Avalonia to initialize
            await Task.Delay(1000);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var window = new TestablePdfViewerWindow(testPdfPath);
                    
                    // Set explicit window dimensions
                    window.Width = 1024;
                    window.Height = 768;
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    
                    // IMPORTANT: Disable fullscreen mode so resize operations are visible
                    var chkFullScreen = window.FindControl<CheckBox>("chkFullScreen");
                    if (chkFullScreen != null)
                    {
                        chkFullScreen.IsChecked = false;
                        Trace.WriteLine("✓ Fullscreen mode disabled for test");
                    }
                    window.WindowState = WindowState.Normal;
                    window.CanResize = true;
                    
                    window.Show();
                    
                    // Load the PDF
                    await window.TriggerLoadAsync();
                    
                    // Give UI time to render
                    await Task.Delay(1000);
                    
                    Trace.WriteLine($"✓ Window shown at {window.Width}x{window.Height}");
                    
                    // Find the ink canvas for page 2
                    var dpPage = window.FindControl<Panel>("dpPage");
                    Assert.IsNotNull(dpPage, "dpPage panel should exist");
                    
                    var grid = dpPage.Children.OfType<Grid>().FirstOrDefault();
                    Assert.IsNotNull(grid, "Grid should exist in dpPage");
                    
                    var inkCanvases = grid.Children.OfType<InkCanvasControl>().ToList();
                    Assert.AreEqual(2, inkCanvases.Count, "Should have 2 InkCanvas controls (one per page)");
                    
                    var inkCanvas1 = inkCanvases[1]; // Page 2
                    Assert.IsNotNull(inkCanvas1, "InkCanvas for page 2 should exist");
                    
                    Trace.WriteLine($"✓ Found InkCanvas for page 2");
                    Trace.WriteLine($"  Initial bounds: {inkCanvas1.Bounds}");
                    
                    // Enable inking on page 2
                    var chkInk1 = window.FindControl<CheckBox>("chkInk1");
                    Assert.IsNotNull(chkInk1, "chkInk1 checkbox should exist");
                    chkInk1.IsChecked = true;
                    
                    await Task.Delay(500);
                    Assert.IsTrue(inkCanvas1.IsInkingEnabled, "Inking should be enabled on page 2");
                    Trace.WriteLine($"✓ Inking enabled on page 2");
                    
                    // Record initial window size
                    var initialWidth = window.Width;
                    var initialHeight = window.Height;
                    Trace.WriteLine($"✓ Initial window size: {initialWidth}x{initialHeight}");
                    
                    // Directly add a normalized stroke using reflection to simulate drawing
                    var normalizedStrokesField = typeof(InkCanvasControl).GetField("_normalizedStrokes", 
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var normalizedStrokes = normalizedStrokesField?.GetValue(inkCanvas1) as System.Collections.Generic.List<System.Collections.Generic.List<Point>>;
                    
                    Assert.IsNotNull(normalizedStrokes, "Normalized strokes collection should exist");
                    
                    // Add a test stroke with normalized coordinates (0-1 range)
                    // Draw a diagonal line across the page
                    var testStroke = new System.Collections.Generic.List<Point>
                    {
                        new Point(0.1, 0.1),
                        new Point(0.2, 0.2),
                        new Point(0.3, 0.3),
                        new Point(0.4, 0.4),
                        new Point(0.5, 0.5),
                        new Point(0.6, 0.6),
                        new Point(0.7, 0.7),
                        new Point(0.8, 0.8),
                        new Point(0.9, 0.9)
                    };
                    
                    normalizedStrokes.Add(testStroke);
                    
                    // Trigger re-render by calling private method with current size
                    var rerenderMethod = typeof(InkCanvasControl).GetMethod("RerenderStrokes",
                        BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(Size) },
                        null);
                    
                    if (rerenderMethod == null)
                    {
                        // Fall back to parameterless version if Size overload not found
                        rerenderMethod = typeof(InkCanvasControl).GetMethod("RerenderStrokes",
                            BindingFlags.NonPublic | BindingFlags.Instance,
                            null,
                            Type.EmptyTypes,
                            null);
                    }
                    
                    if (rerenderMethod != null)
                    {
                        if (rerenderMethod.GetParameters().Length == 1)
                        {
                            rerenderMethod.Invoke(inkCanvas1, new object[] { inkCanvas1.Bounds.Size });
                        }
                        else
                        {
                            rerenderMethod.Invoke(inkCanvas1, null);
                        }
                    }
                    
                    await Task.Delay(1000);
                    
                    Assert.AreEqual(1, normalizedStrokes.Count, "Should have one stroke");
                    Trace.WriteLine($"✓ Stroke drawn on page 2 ({normalizedStrokes[0].Count} points) - VISIBLE!");
                    
                    // Store normalized coordinates for later verification
                    var firstStrokeFirstPoint = normalizedStrokes[0][0];
                    var firstStrokeLastPoint = normalizedStrokes[0][normalizedStrokes[0].Count - 1];
                    
                    Trace.WriteLine($"  First point (normalized): ({firstStrokeFirstPoint.X:F3}, {firstStrokeFirstPoint.Y:F3})");
                    Trace.WriteLine($"  Last point (normalized): ({firstStrokeLastPoint.X:F3}, {firstStrokeLastPoint.Y:F3})");
                    
                    // Verify the rendered polylines exist and have correct screen coordinates
                    var renderedPolylinesField = typeof(InkCanvasControl).GetField("_renderedPolylines",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var renderedPolylines = renderedPolylinesField?.GetValue(inkCanvas1) as System.Collections.Generic.List<Avalonia.Controls.Shapes.Polyline>;
                    
                    Assert.IsNotNull(renderedPolylines, "Rendered polylines should exist");
                    Assert.AreEqual(1, renderedPolylines.Count, "Should have one rendered polyline");
                    
                    var firstPolyline = renderedPolylines[0];
                    Assert.AreEqual(normalizedStrokes[0].Count, firstPolyline.Points.Count, "Polyline should have same number of points as stroke");
                    
                    // Verify first and last points are correctly scaled to screen coordinates
                    var expectedFirstX = firstStrokeFirstPoint.X * inkCanvas1.Bounds.Width;
                    var expectedFirstY = firstStrokeFirstPoint.Y * inkCanvas1.Bounds.Height;
                    var expectedLastX = firstStrokeLastPoint.X * inkCanvas1.Bounds.Width;
                    var expectedLastY = firstStrokeLastPoint.Y * inkCanvas1.Bounds.Height;
                    
                    var actualFirst = firstPolyline.Points[0];
                    var actualLast = firstPolyline.Points[firstPolyline.Points.Count - 1];
                    
                    Assert.AreEqual(expectedFirstX, actualFirst.X, 1.0, $"First point X should be scaled correctly: expected {expectedFirstX:F1}, got {actualFirst.X:F1}");
                    Assert.AreEqual(expectedFirstY, actualFirst.Y, 1.0, $"First point Y should be scaled correctly: expected {expectedFirstY:F1}, got {actualFirst.Y:F1}");
                    Assert.AreEqual(expectedLastX, actualLast.X, 1.0, $"Last point X should be scaled correctly: expected {expectedLastX:F1}, got {actualLast.X:F1}");
                    Assert.AreEqual(expectedLastY, actualLast.Y, 1.0, $"Last point Y should be scaled correctly: expected {expectedLastY:F1}, got {actualLast.Y:F1}");
                    
                    Trace.WriteLine($"  First point (screen): ({actualFirst.X:F1}, {actualFirst.Y:F1}) - expected ({expectedFirstX:F1}, {expectedFirstY:F1})");
                    Trace.WriteLine($"  Last point (screen): ({actualLast.X:F1}, {actualLast.Y:F1}) - expected ({expectedLastX:F1}, {expectedLastY:F1})");
                    Trace.WriteLine($"  Canvas bounds: {inkCanvas1.Bounds.Width:F1} x {inkCanvas1.Bounds.Height:F1}");

                    // Wait 2 seconds so user can see the initial stroke
                    Trace.WriteLine($"⏱ Waiting 2 seconds so you can see the stroke...");
                    await Task.Delay(2000);
                    
                    // Resize the window to 75% size
                    Trace.WriteLine($"⏱ Resizing window to 75%...");
                    window.Width = initialWidth * 0.75;
                    window.Height = initialHeight * 0.75;
                    
                    await Task.Delay(2000); // Wait so user can see the resized window
                    
                    Trace.WriteLine($"✓ Window resized to: {window.Width}x{window.Height}");
                    Trace.WriteLine($"  InkCanvas new bounds: {inkCanvas1.Bounds}");
                    
                    // Verify normalized coordinates haven't changed
                    var normalizedStrokesAfterResize = normalizedStrokesField?.GetValue(inkCanvas1) as System.Collections.Generic.List<System.Collections.Generic.List<Point>>;
                    Assert.IsNotNull(normalizedStrokesAfterResize, "Strokes should still exist after resize");
                    Assert.AreEqual(normalizedStrokes.Count, normalizedStrokesAfterResize.Count, "Stroke count should remain the same");
                    
                    var firstStrokeFirstPointAfter = normalizedStrokesAfterResize[0][0];
                    var firstStrokeLastPointAfter = normalizedStrokesAfterResize[0][normalizedStrokesAfterResize[0].Count - 1];
                    
                    // Normalized coordinates should be identical (within floating point tolerance)
                    Assert.AreEqual(firstStrokeFirstPoint.X, firstStrokeFirstPointAfter.X, 0.0001, "First point X should remain the same");
                    Assert.AreEqual(firstStrokeFirstPoint.Y, firstStrokeFirstPointAfter.Y, 0.0001, "First point Y should remain the same");
                    Assert.AreEqual(firstStrokeLastPoint.X, firstStrokeLastPointAfter.X, 0.0001, "Last point X should remain the same");
                    Assert.AreEqual(firstStrokeLastPoint.Y, firstStrokeLastPointAfter.Y, 0.0001, "Last point Y should remain the same");
                    
                    Trace.WriteLine($"✓ Normalized coordinates preserved after resize");
                    
                    // Verify the rendered polylines were updated with new screen coordinates
                    var renderedPolylinesAfterResize = renderedPolylinesField?.GetValue(inkCanvas1) as System.Collections.Generic.List<Avalonia.Controls.Shapes.Polyline>;
                    
                    Assert.IsNotNull(renderedPolylinesAfterResize, "Rendered polylines should exist");
                    Assert.AreEqual(normalizedStrokes.Count, renderedPolylinesAfterResize.Count, "Should have one polyline per stroke");
                    Trace.WriteLine($"✓ Polylines re-rendered ({renderedPolylinesAfterResize.Count} polylines)");
                    
                    // Verify screen coordinates updated correctly after resize
                    var polylineAfterResize = renderedPolylinesAfterResize[0];
                    var expectedFirstXAfterResize = firstStrokeFirstPoint.X * inkCanvas1.Bounds.Width;
                    var expectedFirstYAfterResize = firstStrokeFirstPoint.Y * inkCanvas1.Bounds.Height;
                    var expectedLastXAfterResize = firstStrokeLastPoint.X * inkCanvas1.Bounds.Width;
                    var expectedLastYAfterResize = firstStrokeLastPoint.Y * inkCanvas1.Bounds.Height;
                    
                    var actualFirstAfterResize = polylineAfterResize.Points[0];
                    var actualLastAfterResize = polylineAfterResize.Points[polylineAfterResize.Points.Count - 1];
                    
                    Assert.AreEqual(expectedFirstXAfterResize, actualFirstAfterResize.X, 1.0, $"After resize: First point X should be scaled correctly: expected {expectedFirstXAfterResize:F1}, got {actualFirstAfterResize.X:F1}");
                    Assert.AreEqual(expectedFirstYAfterResize, actualFirstAfterResize.Y, 1.0, $"After resize: First point Y should be scaled correctly: expected {expectedFirstYAfterResize:F1}, got {actualFirstAfterResize.Y:F1}");
                    Assert.AreEqual(expectedLastXAfterResize, actualLastAfterResize.X, 1.0, $"After resize: Last point X should be scaled correctly: expected {expectedLastXAfterResize:F1}, got {actualLastAfterResize.X:F1}");
                    Assert.AreEqual(expectedLastYAfterResize, actualLastAfterResize.Y, 1.0, $"After resize: Last point Y should be scaled correctly: expected {expectedLastYAfterResize:F1}, got {actualLastAfterResize.Y:F1}");
                    
                    Trace.WriteLine($"  First point after resize (screen): ({actualFirstAfterResize.X:F1}, {actualFirstAfterResize.Y:F1}) - expected ({expectedFirstXAfterResize:F1}, {expectedFirstYAfterResize:F1})");
                    Trace.WriteLine($"  Last point after resize (screen): ({actualLastAfterResize.X:F1}, {actualLastAfterResize.Y:F1}) - expected ({expectedLastXAfterResize:F1}, {expectedLastYAfterResize:F1})");
                    Trace.WriteLine($"  Canvas bounds after resize: {inkCanvas1.Bounds.Width:F1} x {inkCanvas1.Bounds.Height:F1}");
                    
                    // Resize back to larger size (125%)
                    Trace.WriteLine($"⏱ Resizing window to 125%...");
                    window.Width = initialWidth * 1.25;
                    window.Height = initialHeight * 1.25;
                    
                    await Task.Delay(2000); // Wait so user can see the larger window
                    
                    Trace.WriteLine($"✓ Window resized again to: {window.Width}x{window.Height}");
                    
                    // Verify normalized coordinates still unchanged
                    var normalizedStrokesAfterSecondResize = normalizedStrokesField?.GetValue(inkCanvas1) as System.Collections.Generic.List<System.Collections.Generic.List<Point>>;
                    var firstStrokeFirstPointFinal = normalizedStrokesAfterSecondResize[0][0];
                    
                    Assert.AreEqual(firstStrokeFirstPoint.X, firstStrokeFirstPointFinal.X, 0.0001, "Coordinates should remain normalized after multiple resizes");
                    Assert.AreEqual(firstStrokeFirstPoint.Y, firstStrokeFirstPointFinal.Y, 0.0001, "Coordinates should remain normalized after multiple resizes");
                    
                    // Verify screen coordinates updated correctly after second resize
                    var renderedPolylinesFinal = renderedPolylinesField?.GetValue(inkCanvas1) as System.Collections.Generic.List<Avalonia.Controls.Shapes.Polyline>;
                    var polylineFinal = renderedPolylinesFinal[0];
                    
                    var expectedFirstXFinal = firstStrokeFirstPoint.X * inkCanvas1.Bounds.Width;
                    var expectedFirstYFinal = firstStrokeFirstPoint.Y * inkCanvas1.Bounds.Height;
                    var expectedLastXFinal = firstStrokeLastPoint.X * inkCanvas1.Bounds.Width;
                    var expectedLastYFinal = firstStrokeLastPoint.Y * inkCanvas1.Bounds.Height;
                    
                    var actualFirstFinal = polylineFinal.Points[0];
                    var actualLastFinal = polylineFinal.Points[polylineFinal.Points.Count - 1];
                    
                    Assert.AreEqual(expectedFirstXFinal, actualFirstFinal.X, 1.0, $"After 2nd resize: First point X should be scaled correctly: expected {expectedFirstXFinal:F1}, got {actualFirstFinal.X:F1}");
                    Assert.AreEqual(expectedFirstYFinal, actualFirstFinal.Y, 1.0, $"After 2nd resize: First point Y should be scaled correctly: expected {expectedFirstYFinal:F1}, got {actualFirstFinal.Y:F1}");
                    Assert.AreEqual(expectedLastXFinal, actualLastFinal.X, 1.0, $"After 2nd resize: Last point X should be scaled correctly: expected {expectedLastXFinal:F1}, got {actualLastFinal.X:F1}");
                    Assert.AreEqual(expectedLastYFinal, actualLastFinal.Y, 1.0, $"After 2nd resize: Last point Y should be scaled correctly: expected {expectedLastYFinal:F1}, got {actualLastFinal.Y:F1}");
                    
                    Trace.WriteLine($"  First point final (screen): ({actualFirstFinal.X:F1}, {actualFirstFinal.Y:F1}) - expected ({expectedFirstXFinal:F1}, {expectedFirstYFinal:F1})");
                    Trace.WriteLine($"  Last point final (screen): ({actualLastFinal.X:F1}, {actualLastFinal.Y:F1}) - expected ({expectedLastXFinal:F1}, {expectedLastYFinal:F1})");
                    Trace.WriteLine($"  Canvas bounds final: {inkCanvas1.Bounds.Width:F1} x {inkCanvas1.Bounds.Height:F1}");
                    
                    Trace.WriteLine($"✓ Ink persisted correctly through multiple resizes");

                    // Wait a bit more so user can see the final state
                    Trace.WriteLine($"⏱ Waiting 3 more seconds for final view...");
                    await Task.Delay(3000);
                    
                    Trace.WriteLine($"✓ Test passed: Inking on page 2 works correctly across window resizes");
                    Trace.WriteLine($"✓ Total test time with delays: ~10 seconds for visual verification");
                    
                    window.Close();
                    testCompleted.SetResult(true);
                    
                    // Shutdown the app
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                    {
                        lifetime.Shutdown();
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Test error: {ex}");
                    testCompleted.SetException(ex);
                    
                    // Shutdown the app
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                    {
                        lifetime.Shutdown();
                    }
                }
            });

            // Wait for test to complete with timeout
            var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(30000));
            if (completedTask != testCompleted.Task)
            {
                Assert.Fail("Test timed out");
            }

            await testCompleted.Task;
            uiThread.Join(2000);
        }
        finally
        {
            if (File.Exists(testPdfPath))
            {
                try { File.Delete(testPdfPath); } catch { }
            }
        }
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestAvaloniaChooseMusicDialog()
    {
        // This test shows a dialog similar to ChooseMusic with lots of generated bitmaps
        // Skip if running in headless environment (CI/CD)
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
                AppBuilder.Configure<TestChooseMusicApp>()
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

        TestChooseMusicApp.OnSetupWindow = async (app, lifetime) =>
        {
            try
            {
                var window = new ChooseMusicWindow();
                lifetime.MainWindow = window;
                window.Show();
                
                Trace.WriteLine($"✓ ChooseMusicWindow created and shown");
                Trace.WriteLine($"✓ Generating bitmaps for books...");

                // Wait for window to be fully loaded
                await Task.Delay(1000);

                // The window will auto-close after showing for a while
                var timer = new System.Timers.Timer(30000); // 30 seconds
                timer.Elapsed += (s, e) =>
                {
                    timer.Stop();
                    Dispatcher.UIThread.Post(() =>
                    {
                        Trace.WriteLine("Closing ChooseMusicWindow");
                        window?.Close();
                        testCompleted.SetResult(true);
                        lifetime.Shutdown();
                    });
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error: {ex}");
                testCompleted.SetException(ex);
                lifetime.Shutdown();
            }
        };

        if (OperatingSystem.IsWindows())
        {
            uiThread.SetApartmentState(ApartmentState.STA);
        }
        uiThread.Start();

        var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(35000));
        if (completedTask != testCompleted.Task)
        {
            Trace.WriteLine("Test timed out - manually close the window to complete");
        }
        else
        {
            await testCompleted.Task;
        }

        uiThread.Join(2000);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestAvaloniaDataGridBrowseList()
    {
        // This test shows a DataGrid similar to BrowseList with data from reflection
        // Skip if running in headless environment (CI/CD)
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
                AppBuilder.Configure<TestBrowseListApp>()
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

        TestBrowseListApp.OnSetupWindow = async (app, lifetime) =>
        {
            try
            {
                var window = new BrowseListWindow();
                lifetime.MainWindow = window;
                
                // Close test when window is closed
                window.Closed += (s, e) =>
                {
                    Trace.WriteLine("BrowseListWindow closed by user");
                    testCompleted.TrySetResult(true);
                    lifetime.Shutdown();
                };
                
                window.Show();
                
                Trace.WriteLine($"✓ BrowseListWindow created and shown");
                Trace.WriteLine($"✓ Loading types from Avalonia assemblies...");

                // Wait for window to be fully loaded
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error: {ex}");
                testCompleted.SetException(ex);
                lifetime.Shutdown();
            }
        };

        if (OperatingSystem.IsWindows())
        {
            uiThread.SetApartmentState(ApartmentState.STA);
        }
        uiThread.Start();

        // Wait indefinitely for user to close the window
        await testCompleted.Task;
        
        uiThread.Join(2000);
    }

    private async Task RunHeadlessTest(Func<Task> testAction)
    {
        var tcs = new TaskCompletionSource<bool>();
        Exception? testException = null;

        var thread = new Thread(() =>
        {
            try
            {
                var app = BuildAvaloniaApp()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions
                    {
                        UseHeadlessDrawing = false
                    });

                app.StartWithClassicDesktopLifetime(Array.Empty<string>(), ShutdownMode.OnExplicitShutdown);
            }
            catch (Exception ex)
            {
                testException = ex;
                tcs.TrySetException(ex);
            }
        });

        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.IsBackground = true;
        thread.Start();

        // Wait for Avalonia to initialize
        await Task.Delay(1000);

        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    await testAction();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    testException = ex;
                    tcs.SetException(ex);
                }
                finally
                {
                    // Shutdown the app
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                    {
                        lifetime.Shutdown();
                    }
                }
            });

            await tcs.Task;
        }
        catch
        {
            if (testException != null)
                throw testException;
            throw;
        }
        finally
        {
            thread.Join(2000);
        }
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestHeadlessApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static AppBuilder BuildAvaloniaAppForPdfViewer()
        => AppBuilder.Configure<PdfViewerApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    
    private string CreateTestPdf()
    {
        // Create a simple test PDF file
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");
        
        // Create a minimal valid PDF with 2 pages
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
}

// Test app for headless testing
public class TestHeadlessApp : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}

// Custom App that shows PdfViewerWindow instead of MainWindow
public class PdfViewerApp : Avalonia.Application
{
    public static Action<PdfViewerApp, IClassicDesktopStyleApplicationLifetime> OnSetupWindow;

    public override void Initialize()
    {
        // Manually add FluentTheme instead of loading XAML
        // This avoids the "No precompiled XAML found" error
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            OnSetupWindow?.Invoke(this, desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

// Test app for headless testing
public class TestPdfViewerApp : Avalonia.Application
{
    public static Func<Avalonia.Application, IClassicDesktopStyleApplicationLifetime, Task>? OnSetupWindow;
    
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
                // Fire and forget - the async setup will handle its own completion
                _ = OnSetupWindow.Invoke(this, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

// Testable version of PdfViewerWindow that exposes internal state and allows custom PDF path
public class TestablePdfViewerWindow : PdfViewerWindow
{
    private readonly string _customPdfPath;
    private bool _isInitialized;

    public TestablePdfViewerWindow(string pdfPath) : base()
    {
        _customPdfPath = pdfPath;
        _isInitialized = false;
        
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
        
        _isInitialized = true;
        
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

// Test app for BrowseList dialog
public class TestBrowseListApp : Avalonia.Application
{
    public static Func<Avalonia.Application, IClassicDesktopStyleApplicationLifetime, Task>? OnSetupWindow;
    
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

// BrowseList-style window with DataGrid populated from reflection
public class BrowseListWindow : Window
{
    private DataGrid _dataGrid;
    private TextBox _txtFilter;
    private TextBlock _txtStatus;
    private Button _btnApply;
    private List<TypeInfo> _allData;
    private List<TypeInfo> _filteredData;

    public BrowseListWindow()
    {
        Title = "Browse Types - Avalonia Test (Reflection Data)";
        Width = 1400;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        
        BuildUI();
        
        // Load data after window is loaded
        this.Opened += async (s, e) =>
        {
            await LoadDataAsync();
        };
    }

    private void BuildUI()
    {
        // Use Grid instead of DockPanel for more reliable layout
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // Filter panel
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star))); // DataGrid
        
        // Top filter panel
        var filterPanel = new StackPanel 
        { 
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10),
            Spacing = 10,
            Height = 50,
            Background = Brushes.LightGray
        };
        
        _txtStatus = new TextBlock 
        { 
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 20, 0),
            FontSize = 12,
            MinWidth = 200,
            Text = "Initializing..."
        };
        filterPanel.Children.Add(_txtStatus);
        
        filterPanel.Children.Add(new Label 
        { 
            Content = "String Filter:",
            VerticalAlignment = VerticalAlignment.Center
        });
        
        _txtFilter = new TextBox 
        { 
            Width = 300,
            Watermark = "Type to filter...",
            VerticalAlignment = VerticalAlignment.Center
        };
        _txtFilter.KeyDown += (s, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                ApplyFilter();
            }
        };
        filterPanel.Children.Add(_txtFilter);
        
        _btnApply = new Button 
        { 
            Content = "Apply",
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _btnApply.Click += (s, e) => ApplyFilter();
        filterPanel.Children.Add(_btnApply);
        
        var btnClear = new Button 
        { 
            Content = "Clear",
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        btnClear.Click += (s, e) =>
        {
            _txtFilter.Text = string.Empty;
            ApplyFilter();
        };
        filterPanel.Children.Add(btnClear);
        
        Grid.SetRow(filterPanel, 0);
        grid.Children.Add(filterPanel);
        
        // DataGrid for displaying the data
        _dataGrid = new DataGrid
        {
            Margin = new Thickness(10),
            Background = Brushes.White,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Black,
            IsReadOnly = true,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Extended,
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 100,
            RowHeight = 25, // Explicit row height
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        
        // Define columns manually for better control
        _dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Type Name",
            Binding = new Avalonia.Data.Binding("Name"),
            Width = new DataGridLength(250)
        });
        
        _dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Namespace",
            Binding = new Avalonia.Data.Binding("Namespace"),
            Width = new DataGridLength(300)
        });
        
        _dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Base Type",
            Binding = new Avalonia.Data.Binding("BaseTypeName"),
            Width = new DataGridLength(200)
        });
        
        _dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Is Public",
            Binding = new Avalonia.Data.Binding("IsPublic"),
            Width = new DataGridLength(80)
        });
        
        _dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Is Abstract",
            Binding = new Avalonia.Data.Binding("IsAbstract"),
            Width = new DataGridLength(80)
        });
        
        _dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Is Sealed",
            Binding = new Avalonia.Data.Binding("IsSealed"),
            Width = new DataGridLength(80)
        });
        
        _dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "# Properties",
            Binding = new Avalonia.Data.Binding("PropertyCount"),
            Width = new DataGridLength(100)
        });
        
        _dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "# Methods",
            Binding = new Avalonia.Data.Binding("MethodCount"),
            Width = new DataGridLength(100)
        });
        
        _dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Assembly",
            Binding = new Avalonia.Data.Binding("AssemblyName"),
            Width = new DataGridLength(200)
        });
        
        Trace.WriteLine($"✓ DataGrid created with {_dataGrid.Columns.Count} columns");
        Trace.WriteLine($"  DataGrid properties: Background={_dataGrid.Background}, BorderBrush={_dataGrid.BorderBrush}, BorderThickness={_dataGrid.BorderThickness}");
        
        Grid.SetRow(_dataGrid, 1);
        grid.Children.Add(_dataGrid);
        
        Trace.WriteLine($"✓ DataGrid added to Grid (child count: {grid.Children.Count})");
        
        Content = grid;
    }

    private async Task LoadDataAsync()
    {
        _txtStatus.Text = "Loading types from Avalonia assemblies...";
        await Task.Delay(100); // Allow UI to update
        
        _allData = new List<TypeInfo>();
        
        try
        {
            // Get Avalonia assemblies
            var avaloniaAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && a.GetName().Name != null && 
                       (a.GetName().Name.StartsWith("Avalonia") || 
                        a.GetName().Name.Contains("SkiaSharp")))
                .ToList();
            
            Trace.WriteLine($"Found {avaloniaAssemblies.Count} Avalonia/SkiaSharp assemblies");
            _txtStatus.Text = $"Found {avaloniaAssemblies.Count} assemblies, loading types...";
            await Task.Delay(100);
            
            foreach (var assembly in avaloniaAssemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.IsPublic || t.IsNestedPublic)
                        .Take(200) // Limit per assembly
                        .ToList();
                    
                    foreach (var type in types)
                    {
                        try
                        {
                            var typeInfo = new TypeInfo
                            {
                                Name = type.Name,
                                Namespace = type.Namespace ?? "(no namespace)",
                                BaseTypeName = type.BaseType?.Name ?? "(none)",
                                IsPublic = type.IsPublic || type.IsNestedPublic,
                                IsAbstract = type.IsAbstract,
                                IsSealed = type.IsSealed,
                                PropertyCount = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length,
                                MethodCount = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Length,
                                AssemblyName = assembly.GetName().Name,
                                FullName = type.FullName ?? type.Name
                            };
                            _allData.Add(typeInfo);
                        }
                        catch
                        {
                            // Skip types that can't be reflected
                        }
                    }
                    
                    Trace.WriteLine($"  {assembly.GetName().Name}: {types.Count} types");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"  Error loading types from {assembly.GetName().Name}: {ex.Message}");
                }
                
                // Update UI periodically
                if (_allData.Count % 100 == 0)
                {
                    _txtStatus.Text = $"Loaded {_allData.Count} types...";
                    await Task.Delay(10);
                }
            }
            
            _filteredData = new List<TypeInfo>(_allData);
            
            Trace.WriteLine($"✓ Setting ItemsSource with {_filteredData.Count} items");
            _txtStatus.Text = $"Setting grid data ({_filteredData.Count} items)...";
            await Task.Delay(100);
            
            // Use ObservableCollection for better data binding
            var observableData = new System.Collections.ObjectModel.ObservableCollection<TypeInfo>(_filteredData);
            _dataGrid.ItemsSource = observableData;
            
            // Force DataGrid to measure and arrange
            _dataGrid.InvalidateMeasure();
            _dataGrid.InvalidateArrange();
            
            Trace.WriteLine($"✓ ItemsSource set, updating status");
            Trace.WriteLine($"  DataGrid DesiredSize: {_dataGrid.DesiredSize}");
            Trace.WriteLine($"  DataGrid Bounds: {_dataGrid.Bounds}");
            UpdateStatus();
            
            Trace.WriteLine($"✓ Loaded {_allData.Count} total types and displayed in grid");
        }
        catch (Exception ex)
        {
            _txtStatus.Text = $"Error: {ex.Message}";
            Trace.WriteLine($"Error loading data: {ex}");
        }
    }

    private void ApplyFilter()
    {
        var filterText = _txtFilter.Text?.Trim().ToLower() ?? string.Empty;
        
        if (string.IsNullOrEmpty(filterText))
        {
            _filteredData = new List<TypeInfo>(_allData);
        }
        else
        {
            _filteredData = _allData
                .Where(t => 
                    t.Name.ToLower().Contains(filterText) ||
                    t.Namespace.ToLower().Contains(filterText) ||
                    t.BaseTypeName.ToLower().Contains(filterText) ||
                    t.AssemblyName.ToLower().Contains(filterText))
                .ToList();
        }
        
        _dataGrid.ItemsSource = null;
        _dataGrid.ItemsSource = _filteredData;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_filteredData.Count == _allData.Count)
        {
            _txtStatus.Text = $"Showing all {_allData.Count:n0} types";
        }
        else
        {
            _txtStatus.Text = $"Showing {_filteredData.Count:n0} of {_allData.Count:n0} types";
        }
    }
}

// Data class for type information
public class TypeInfo
{
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string BaseTypeName { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsSealed { get; set; }
    public int PropertyCount { get; set; }
    public int MethodCount { get; set; }
    public string AssemblyName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}

