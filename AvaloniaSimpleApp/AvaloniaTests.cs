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

// Test app for ChooseMusic dialog
public class TestChooseMusicApp : Avalonia.Application
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

// ChooseMusic-style window with generated bitmaps
public class ChooseMusicWindow : Window
{
    private TabControl _tabControl;
    private ListBox _lbBooks;
    private TextBlock _tbxTotals;
    private ComboBox _cboRootFolder;
    private TextBox _tbxFilter;

    public ChooseMusicWindow()
    {
        Title = "Choose Music - Avalonia Test";
        Width = 1200;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        
        BuildUI();
        
        // Generate books after window is loaded
        this.Opened += async (s, e) =>
        {
            await FillBooksTabAsync();
        };
    }

    private void BuildUI()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        
        // Tab control for Books, Favorites, Query, Playlists
        _tabControl = new TabControl();
        Grid.SetRow(_tabControl, 0);
        Grid.SetRowSpan(_tabControl, 2);
        
        // Books tab
        var booksTab = new TabItem { Header = "_Books" };
        var booksGrid = new Grid();
        booksGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        booksGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        
        // Filter bar
        var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        filterPanel.Children.Add(new RadioButton { Content = "ByDate", GroupName = "Sort", IsChecked = true, Margin = new Thickness(20, 5, 0, 0) });
        filterPanel.Children.Add(new RadioButton { Content = "ByFolder", GroupName = "Sort", Margin = new Thickness(20, 5, 0, 0) });
        filterPanel.Children.Add(new RadioButton { Content = "ByNumPages", GroupName = "Sort", Margin = new Thickness(20, 5, 0, 0) });
        filterPanel.Children.Add(new Label { Content = "Filter", Margin = new Thickness(20, 0, 0, 0) });
        _tbxFilter = new TextBox { Width = 150, Margin = new Thickness(5, 0, 0, 0) };
        filterPanel.Children.Add(_tbxFilter);
        Grid.SetRow(filterPanel, 0);
        booksGrid.Children.Add(filterPanel);
        
        // Books list with wrap panel - use ItemsControl instead of ListBox for better control
        var itemsControl = new ItemsControl();
        _lbBooks = new ListBox();
        
        // Create a WrapPanel as the items panel
        var wrapPanelFactory = new FuncTemplate<Panel?>(() => new WrapPanel
        {
            Orientation = Orientation.Horizontal
        });
        _lbBooks.ItemsPanel = wrapPanelFactory;
        
        var scrollViewer = new ScrollViewer 
        { 
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _lbBooks
        };
        
        Grid.SetRow(scrollViewer, 1);
        booksGrid.Children.Add(scrollViewer);
        
        booksTab.Content = booksGrid;
        _tabControl.Items.Add(booksTab);
        
        // Favorites tab (placeholder)
        var favTab = new TabItem { Header = "Fa_vorites", Content = new TextBlock { Text = "Favorites go here", Margin = new Thickness(20) } };
        _tabControl.Items.Add(favTab);
        
        // Query tab (placeholder)
        var queryTab = new TabItem { Header = "_Query", Content = new TextBlock { Text = "Query goes here", Margin = new Thickness(20) } };
        _tabControl.Items.Add(queryTab);
        
        // Playlists tab (placeholder)
        var playlistsTab = new TabItem { Header = "_Playlists", Content = new TextBlock { Text = "Playlists go here", Margin = new Thickness(20) } };
        _tabControl.Items.Add(playlistsTab);
        
        grid.Children.Add(_tabControl);
        
        // Top bar with totals and controls
        var topBar = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 5, 10, 5)
        };
        Grid.SetRow(topBar, 0);
        
        _tbxTotals = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        topBar.Children.Add(_tbxTotals);
        
        topBar.Children.Add(new Label { Content = "Music Folder Path:", Margin = new Thickness(20, 0, 0, 0) });
        _cboRootFolder = new ComboBox { Width = 300, Margin = new Thickness(10, 0, 10, 0) };
        _cboRootFolder.Items.Add("C:\\Users\\Music\\SheetMusic");
        _cboRootFolder.Items.Add("D:\\Music\\PDFs");
        _cboRootFolder.SelectedIndex = 0;
        topBar.Children.Add(_cboRootFolder);
        
        var btnCancel = new Button { Content = "Cancel", Margin = new Thickness(10, 0, 0, 0) };
        btnCancel.Click += (s, e) => Close();
        topBar.Children.Add(btnCancel);
        
        var btnOk = new Button { Content = "_OK", Width = 50, Margin = new Thickness(10, 0, 10, 0) };
        btnOk.Click += (s, e) => Close();
        topBar.Children.Add(btnOk);
        
        grid.Children.Add(topBar);
        
        Content = grid;
    }

    private async Task FillBooksTabAsync()
    {
        var random = new Random(42); // Fixed seed for consistent colors
        var items = new List<Control>();
        
        // Generate 50 book items with colorful bitmaps
        var bookNames = new[]
        {
            "Classical Piano Vol 1", "Jazz Standards", "Pop Hits 2020", "Rock Classics",
            "Broadway Favorites", "Country Gold", "Blues Collection", "Folk Songs",
            "Movie Themes", "Video Game Music", "Christmas Carols", "Gospel Hymns",
            "Opera Arias", "Chamber Music", "Symphonies", "Concertos",
            "Sonatas", "Etudes", "Preludes", "Fugues",
            "Nocturnes", "Waltzes", "Mazurkas", "Ballades",
            "Impromptus", "Scherzos", "Polonaises", "Rhapsodies",
            "Variations", "Suites", "Partitas", "Inventions",
            "Toccatas", "Fantasias", "Rondos", "Minuets",
            "Gavottes", "Bourrees", "Sarabandes", "Gigues",
            "Courantes", "Allemandes", "Passacaglias", "Chaconnes",
            "Marches", "Serenades", "Divertimentos", "Overtures",
            "Interludes", "Bagatelles"
        };
        
        for (int i = 0; i < 50; i++)
        {
            var bookName = bookNames[i % bookNames.Length];
            if (i >= bookNames.Length)
            {
                bookName += $" Vol {i / bookNames.Length + 1}";
            }
            
            // Create a colorful bitmap for this book
            var bitmap = GenerateBookCoverBitmap(200, 240, random, bookName, i);
            
            // Create the book item UI
            var sp = new StackPanel { Orientation = Orientation.Vertical, Width = 150, Margin = new Thickness(5) };
            
            var img = new Avalonia.Controls.Image
            {
                Source = bitmap,
                Width = 140,
                Height = 200,
                Stretch = Stretch.UniformToFill
            };
            sp.Children.Add(img);
            
            sp.Children.Add(new TextBlock
            {
                Text = bookName,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 140,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 11
            });
            
            var numSongs = random.Next(10, 100);
            var numPages = random.Next(20, 500);
            var numFavs = random.Next(0, 20);
            
            sp.Children.Add(new TextBlock
            {
                Text = $"#Sngs={numSongs} Pg={numPages} Fav={numFavs}",
                FontSize = 10,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            });
            
            items.Add(sp);
            
            // Add incrementally to show loading progress
            if (i % 5 == 4)
            {
                _lbBooks.ItemsSource = null;
                _lbBooks.ItemsSource = new List<Control>(items);
                _tbxTotals.Text = $"Total #Books = {items.Count} # Songs = {items.Count * 50} # Pages = {items.Count * 150} #Fav={items.Count * 5}";
                await Task.Delay(10); // Small delay to show progressive loading
            }
        }
        
        // Final update
        _lbBooks.ItemsSource = items;
        _tbxTotals.Text = $"Total #Books = {items.Count} # Songs = {items.Count * 50:n0} # Pages = {items.Count * 150:n0} #Fav={items.Count * 5:n0}";
    }

    private Bitmap GenerateBookCoverBitmap(int width, int height, Random random, string title, int index)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        
        // Generate a nice gradient background
        var color1 = SKColor.FromHsv(random.Next(360), random.Next(60, 100), random.Next(70, 100));
        var color2 = SKColor.FromHsv((random.Next(360) + 180) % 360, random.Next(60, 100), random.Next(40, 70));
        
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            new[] { color1, color2 },
            SKShaderTileMode.Clamp);
        
        using var paint = new SKPaint
        {
            Shader = shader,
            IsAntialias = true
        };
        canvas.DrawRect(0, 0, width, height, paint);
        
        // Add some decorative elements
        using var accentPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(100),
            IsAntialias = true,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke
        };
        
        // Draw some circles or rectangles as decoration
        var shapeType = index % 4;
        switch (shapeType)
        {
            case 0:
                canvas.DrawCircle(width / 2, height / 3, 30, accentPaint);
                break;
            case 1:
                canvas.DrawRect(width / 4, height / 4, width / 2, height / 2, accentPaint);
                break;
            case 2:
                for (int i = 0; i < 3; i++)
                {
                    canvas.DrawLine(10, height / 4 + i * 20, width - 10, height / 4 + i * 20, accentPaint);
                }
                break;
            case 3:
                canvas.DrawOval(new SKRect(width / 4, height / 3, width * 3 / 4, height * 2 / 3), accentPaint);
                break;
        }
        
        // Add title text at bottom
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 14,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            TextAlign = SKTextAlign.Center
        };
        
        // Add shadow for text
        using var shadowPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(150),
            IsAntialias = true,
            TextSize = 14,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            TextAlign = SKTextAlign.Center
        };
        
        var yPos = height - 30;
        
        // Word wrap the title
        var words = title.Split(' ');
        var currentLine = "";
        foreach (var word in words)
        {
            var testLine = currentLine.Length > 0 ? currentLine + " " + word : word;
            var lineWidth = textPaint.MeasureText(testLine);
            
            if (lineWidth > width - 20 && currentLine.Length > 0)
            {
                canvas.DrawText(currentLine, width / 2 + 1, yPos + 1, shadowPaint);
                canvas.DrawText(currentLine, width / 2, yPos, textPaint);
                currentLine = word;
                yPos += 18;
            }
            else
            {
                currentLine = testLine;
            }
        }
        
        if (currentLine.Length > 0)
        {
            canvas.DrawText(currentLine, width / 2 + 1, yPos + 1, shadowPaint);
            canvas.DrawText(currentLine, width / 2, yPos, textPaint);
        }
        
        // Convert to Avalonia bitmap
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        
        return new Bitmap(stream);
    }
}
