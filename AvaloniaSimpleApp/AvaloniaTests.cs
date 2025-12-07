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
                    var normalizedStrokes = normalizedStrokesField?.GetValue(inkCanvas1) as List<List<Point>>;
                    
                    Assert.IsNotNull(normalizedStrokes, "Normalized strokes collection should exist");
                    
                    // Add a test stroke with normalized coordinates (0-1 range)
                    // Draw a diagonal line across the page
                    var testStroke = new List<Point>
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
                    var renderedPolylines = renderedPolylinesField?.GetValue(inkCanvas1) as List<Avalonia.Controls.Shapes.Polyline>;
                    
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
                    var normalizedStrokesAfterResize = normalizedStrokesField?.GetValue(inkCanvas1) as List<List<Point>>;
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
                    var renderedPolylinesAfterResize = renderedPolylinesField?.GetValue(inkCanvas1) as List<Avalonia.Controls.Shapes.Polyline>;
                    
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
                    var normalizedStrokesAfterSecondResize = normalizedStrokesField?.GetValue(inkCanvas1) as List<List<Point>>;
                    var firstStrokeFirstPointFinal = normalizedStrokesAfterSecondResize[0][0];
                    
                    Assert.AreEqual(firstStrokeFirstPoint.X, firstStrokeFirstPointFinal.X, 0.0001, "Coordinates should remain normalized after multiple resizes");
                    Assert.AreEqual(firstStrokeFirstPoint.Y, firstStrokeFirstPointFinal.Y, 0.0001, "Coordinates should remain normalized after multiple resizes");
                    
                    // Verify screen coordinates updated correctly after second resize
                    var renderedPolylinesFinal = renderedPolylinesField?.GetValue(inkCanvas1) as List<Avalonia.Controls.Shapes.Polyline>;
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

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestItemContainerGeneratorDirect()
    {
        // This test investigates whether Avalonia's ItemContainerGenerator can work
        // without ItemTemplate, potentially providing an alternative to manual rendering
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
                Trace.WriteLine("=== ItemContainerGenerator Diagnostic Test ===");
                Trace.WriteLine("");
                
                // Create test data
                var items = new[] { "Item 1", "Item 2", "Item 3", "Item 4", "Item 5" };
                
                // Test 1: ItemsControl without ItemTemplate
                Trace.WriteLine("TEST 1: ItemsControl without ItemTemplate");
                Trace.WriteLine("------------------------------------------");
                var itemsControl = new ItemsControl
                {
                    Width = 400,
                    Height = 300,
                    ItemsSource = items
                };
                
                var window = new Window
                {
                    Title = "ItemContainerGenerator Test",
                    Width = 500,
                    Height = 400,
                    Content = itemsControl
                };
                window.Show();
                
                // Wait for layout
                await Task.Delay(1000);
                
                // Check ItemContainerGenerator
                var generator = itemsControl.ItemContainerGenerator;
                Trace.WriteLine($"✓ Generator exists: {generator != null}");
                Trace.WriteLine($"✓ Items count: {items.Length}");
                Trace.WriteLine("");
                
                // Try to get containers
                Trace.WriteLine("Attempting to retrieve containers from generator:");
                for (int i = 0; i < items.Length; i++)
                {
                    var container = generator.ContainerFromIndex(i);
                    Trace.WriteLine($"  Index {i}: Container={container?.GetType().Name ?? "NULL"}");
                }
                Trace.WriteLine("");
                
                // Check visual tree
                Trace.WriteLine("Checking visual tree:");
                var presenter = itemsControl.Presenter;
                Trace.WriteLine($"  Presenter exists: {presenter != null}");
                
                if (presenter != null)
                {
                    var panel = presenter.Panel;
                    Trace.WriteLine($"  Panel exists: {panel != null}");
                    Trace.WriteLine($"  Panel type: {panel?.GetType().Name ?? "NULL"}");
                    Trace.WriteLine($"  Panel children count: {panel?.Children.Count ?? 0}");
                    
                    if (panel != null && panel.Children.Count > 0)
                    {
                        Trace.WriteLine("  Panel children:");
                        foreach (var child in panel.Children)
                        {
                            Trace.WriteLine($"    - {child.GetType().Name}: {child}");
                        }
                    }
                }
                Trace.WriteLine("");
                
                // Test 2: ListBox (which should use ItemContainerGenerator internally)
                Trace.WriteLine("TEST 2: ListBox with items");
                Trace.WriteLine("---------------------------");
                
                var listBox = new ListBox
                {
                    Width = 400,
                    Height = 300,
                    ItemsSource = items
                };
                
                window.Content = listBox;
                await Task.Delay(1000);
                
                var generator3 = listBox.ItemContainerGenerator;
                Trace.WriteLine($"✓ Generator exists: {generator3 != null}");
                
                Trace.WriteLine("Attempting to retrieve ListBoxItem containers:");
                for (int i = 0; i < items.Length; i++)
                {
                    var container = generator3.ContainerFromIndex(i);
                    Trace.WriteLine($"  Index {i}: Container={container?.GetType().Name ?? "NULL"}");
                    
                    if (container is ListBoxItem listBoxItem)
                    {
                        Trace.WriteLine($"    Content: {listBoxItem.Content}");
                    }
                }
                
                var presenter3 = listBox.Presenter;
                if (presenter3?.Panel != null)
                {
                    Trace.WriteLine($"  Panel children count: {presenter3.Panel.Children.Count}");
                    Trace.WriteLine("  Visual children:");
                    foreach (var child in presenter3.Panel.Children)
                    {
                        Trace.WriteLine($"    - {child.GetType().Name}");
                        if (child is ListBoxItem lbi)
                        {
                            Trace.WriteLine($"      Content: {lbi.Content}");
                        }
                    }
                }
                Trace.WriteLine("");
                
                // Summary
                Trace.WriteLine("=== SUMMARY ===");
                Trace.WriteLine($"ItemsControl without template: Panel has {itemsControl.Presenter?.Panel?.Children.Count ?? 0} children");
                Trace.WriteLine($"ListBox: Panel has {listBox.Presenter?.Panel?.Children.Count ?? 0} children");
                Trace.WriteLine("");
                Trace.WriteLine("EXPECTED RESULTS:");
                Trace.WriteLine("  - ItemsControl should create default TextBlock containers automatically");
                Trace.WriteLine("  - ListBox should create ListBoxItem containers");
                Trace.WriteLine("  - ContainerFromIndex should return non-null containers");
                Trace.WriteLine("");
                Trace.WriteLine("If containers are NULL, then ItemContainerGenerator doesn't help with manual rendering.");
                Trace.WriteLine("Window will close in 10 seconds...");
                
                await Task.Delay(10000);
                
                window.Close();
                testCompleted.SetResult(true);
                
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.Shutdown();
                }
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

        var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(20000));
        if (completedTask != testCompleted.Task)
        {
            Assert.Fail("Test timed out");
        }

        await testCompleted.Task;
        uiThread.Join(2000);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestListBoxVirtualization()
    {
        // This test verifies whether ListBox virtualizes large datasets
        // If Panel.Children.Count << ItemsSource.Count, virtualization is working!
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
                Trace.WriteLine("=== ListBox Virtualization Test ===");
                Trace.WriteLine("");
                
                // Create a LARGE dataset - 10,000 items
                var itemCount = 10000;
                var items = Enumerable.Range(0, itemCount)
                    .Select(i => new TestDataItem 
                    { 
                        Id = i, 
                        Name = $"Item {i}",
                        Description = $"This is test item number {i} with some description text",
                        Value = i * 1.5
                    })
                    .ToList();
                
                Trace.WriteLine($"Created {itemCount:n0} test items");
                Trace.WriteLine("");
                
                var window = new Window
                {
                    Title = "ListBox Virtualization Test",
                    Width = 800,
                    Height = 600
                };
                
                // Test 1: ListBox with default panel (StackPanel - no virtualization)
                Trace.WriteLine("TEST 1: ListBox with default StackPanel");
                Trace.WriteLine("------------------------------------------");
                
                var listBox1 = new ListBox
                {
                    Width = 750,
                    Height = 500,
                    ItemsSource = items
                };
                
                window.Content = listBox1;
                window.Show();
                
                await Task.Delay(2000); // Wait for rendering
                
                var presenter1 = listBox1.Presenter;
                var panel1 = presenter1?.Panel;
                
                Trace.WriteLine($"ItemsSource count: {items.Count:n0}");
                Trace.WriteLine($"Panel type: {panel1?.GetType().Name ?? "NULL"}");
                Trace.WriteLine($"Panel.Children.Count: {panel1?.Children.Count ?? 0:n0}");
                Trace.WriteLine($"Memory: ~{GC.GetTotalMemory(false) / 1024 / 1024:n0} MB");
                
                if (panel1?.Children.Count == items.Count)
                {
                    Trace.WriteLine("⚠️ NO VIRTUALIZATION - All items rendered!");
                }
                else
                {
                    Trace.WriteLine("✅ VIRTUALIZATION WORKING - Only visible items rendered!");
                }
                Trace.WriteLine("");
                
                // Summary
                Trace.WriteLine("=== SUMMARY ===");
                Trace.WriteLine($"Total items: {itemCount:n0}");
                Trace.WriteLine($"Visual tree items: {panel1?.Children.Count ?? 0:n0}");
                
                if (panel1?.Children.Count < itemCount * 0.1) // Less than 10% of items
                {
                    Trace.WriteLine("✅ EXCELLENT: Strong virtualization detected!");
                    Trace.WriteLine("   ListBox could replace manual rendering for large datasets!");
                }
                else if (panel1?.Children.Count < itemCount * 0.5) // Less than 50%
                {
                    Trace.WriteLine("⚠️ PARTIAL: Some virtualization working");
                    Trace.WriteLine("   ListBox might work for medium datasets (1,000-5,000 items)");
                }
                else
                {
                    Trace.WriteLine("❌ POOR: No meaningful virtualization");
                    Trace.WriteLine("   Stick with manual rendering for BrowseControl");
                    Trace.WriteLine("   Current solution already handles 200 items efficiently");
                }
                Trace.WriteLine("");
                Trace.WriteLine("Window will close in 10 seconds...");
                
                await Task.Delay(10000);
                
                window.Close();
                testCompleted.SetResult(true);
                
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.Shutdown();
                }
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

        var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(25000));
        if (completedTask != testCompleted.Task)
        {
            Assert.Fail("Test timed out");
        }

        await testCompleted.Task;
        uiThread.Join(2000);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestBrowseControlComparison()
    {
        // This test compares the original BrowseControl (manual rendering)
        // with the new ListBoxBrowseControl (virtualized ListBox)
        // to evaluate performance and functionality differences
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
                Trace.WriteLine("=== BrowseControl Comparison Test ===");
                Trace.WriteLine("");
                
                // Create test data - 1,000 items (medium dataset)
                var itemCount = 1000;
                var query = Enumerable.Range(0, itemCount)
                    .Select(i => new
                    {
                        Id = i,
                        Name = $"Item {i:D4}",
                        Category = $"Category {i % 10}",
                        Value = i * 1.5,
                        Description = $"This is test item number {i}"
                    });
                
                Trace.WriteLine($"Created query with {itemCount:n0} items");
                Trace.WriteLine("");
                
                // Test 1: Original BrowseControl (manual rendering)
                Trace.WriteLine("TEST 1: BrowseControl (Manual Rendering)");
                Trace.WriteLine("------------------------------------------");
                
                var sw1 = Stopwatch.StartNew();
                var browseControl1 = new BrowseControl(query, colWidths: new[] { 100, 200, 150, 100, 300 });
                sw1.Stop();
                
                var window1 = new Window
                {
                    Title = "BrowseControl (Manual) - 1,000 items",
                    Width = 1000,
                    Height = 600,
                    Content = browseControl1
                };
                
                window1.Show();
                await Task.Delay(500);
                
                var memoryBefore1 = GC.GetTotalMemory(false);
                
                // Get panel children count using reflection
                var panelField1 = browseControl1.ListView.GetType().GetField("_itemsPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var panel1 = panelField1?.GetValue(browseControl1.ListView) as StackPanel;
                var panelChildCount1 = panel1?.Children.Count ?? 0;
                
                Trace.WriteLine($"Creation time: {sw1.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"ListView panel children: {panelChildCount1:n0}");
                Trace.WriteLine($"Memory footprint: ~{memoryBefore1 / 1024 / 1024:n0} MB");
                Trace.WriteLine("");
                
                // Test 2: New ListBoxBrowseControl (virtualized)
                Trace.WriteLine("TEST 2: ListBoxBrowseControl (Virtualized ListBox)");
                Trace.WriteLine("---------------------------------------------------");
                
                var sw2 = Stopwatch.StartNew();
                var browseControl2 = new ListBoxBrowseControl(query, colWidths: new[] { 100, 200, 150, 100, 300 });
                sw2.Stop();
                
                var window2 = new Window
                {
                    Title = "ListBoxBrowseControl (Virtualized) - 1,000 items",
                    Width = 1000,
                    Height = 600,
                    Content = browseControl2,
                    Position = new PixelPoint(window1.Position.X + 50, window1.Position.Y + 50)
                };
                
                window2.Show();
                await Task.Delay(500);
                
                var memoryBefore2 = GC.GetTotalMemory(false);
                
                // Get ListBox panel info
                var listBoxView = browseControl2.ListView;
                var listBoxField = listBoxView.GetType().GetField("_listBox", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var listBox = listBoxField?.GetValue(listBoxView) as ListBox;
                var presenter = listBox?.Presenter;
                var panel = presenter?.Panel;
                
                Trace.WriteLine($"Creation time: {sw2.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"ListBox panel type: {panel?.GetType().Name ?? "N/A"}");
                Trace.WriteLine($"ListBox panel children: {panel?.Children.Count.ToString("n0") ?? "N/A"}");
                Trace.WriteLine($"Memory footprint: ~{memoryBefore2 / 1024 / 1024:n0} MB");
                Trace.WriteLine("");
                
                // Comparison
                Trace.WriteLine("=== COMPARISON ===");
                Trace.WriteLine($"Dataset: {itemCount:n0} items");
                Trace.WriteLine("");
                Trace.WriteLine($"Manual Rendering:");
                Trace.WriteLine($"  - All {itemCount:n0} rows in visual tree");
                Trace.WriteLine($"  - Creation: {sw1.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"  - Memory: ~{memoryBefore1 / 1024 / 1024:n0} MB");
                Trace.WriteLine("");
                Trace.WriteLine($"ListBox Virtualization:");
                Trace.WriteLine($"  - ~{panel?.Children.Count ?? 0} rows in visual tree");
                Trace.WriteLine($"  - Creation: {sw2.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"  - Memory: ~{memoryBefore2 / 1024 / 1024:n0} MB");
                Trace.WriteLine("");
                
                if (panel?.Children.Count < itemCount * 0.1)
                {
                    Trace.WriteLine("✅ VIRTUALIZATION WORKING!");
                    Trace.WriteLine($"   Only {panel.Children.Count:n0} / {itemCount:n0} items in visual tree");
                }
                Trace.WriteLine("");
                
                Trace.WriteLine("Both windows will remain open for manual comparison.");
                Trace.WriteLine("Test features: sorting, filtering, selection, scrolling.");
                Trace.WriteLine("Windows will close in 30 seconds...");
                
                await Task.Delay(30000);
                
                window1.Close();
                window2.Close();
                testCompleted.SetResult(true);
                
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.Shutdown();
                }
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

        var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(40000));
        if (completedTask != testCompleted.Task)
        {
            Assert.Fail("Test timed out");
        }

        await testCompleted.Task;
        uiThread.Join(2000);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestListBoxBrowseControl()
    {
        // This test demonstrates the ListBoxBrowseControl with a large dataset (10,000 items)
        // and remains open until the user closes the window.
        // This is useful for manual testing of virtualization, sorting, filtering, and UI responsiveness.
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
                Trace.WriteLine("=== ListBoxBrowseControl with 10,000 Items ===");
                Trace.WriteLine("");
                
                // Create a large dataset - 10,000 items
                var itemCount = 10000;
                var query = Enumerable.Range(0, itemCount)
                    .Select(i => new
                    {
                        Id = i,
                        Name = $"Item {i:D5}",
                        Category = $"Category {i % 20}",
                        Type = $"Type {i % 5}",
                        Value = i * 2.5,
                        Status = i % 3 == 0 ? "Active" : i % 3 == 1 ? "Pending" : "Inactive",
                        Description = $"Description for test item number {i} with additional details"
                    });
                
                Trace.WriteLine($"✓ Created query with {itemCount:n0} items");
                Trace.WriteLine("");
                
                // Measure creation time
                var sw = Stopwatch.StartNew();
                var browseControl = new ListBoxBrowseControl(query, colWidths: new[] { 80, 150, 120, 80, 100, 100, 350 });
                sw.Stop();
                
                // Create window
                var window = new Window
                {
                    Title = $"ListBoxBrowseControl - {itemCount:n0} Items (Virtualized)",
                    Width = 1200,
                    Height = 800,
                    Content = browseControl,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                
                // Handle window closed event
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
                
                await Task.Delay(1000);
                
                // Get metrics
                var memoryMB = GC.GetTotalMemory(false) / 1024 / 1024;
                
                // Get ListBox panel info for virtualization verification
                var listBoxView = browseControl.ListView;
                var listBoxField = listBoxView.GetType().GetField("_listBox", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var listBox = listBoxField?.GetValue(listBoxView) as ListBox;
                var presenter = listBox?.Presenter;
                var panel = presenter?.Panel;
                
                Trace.WriteLine("=== PERFORMANCE METRICS ===");
                Trace.WriteLine($"✓ Creation time: {sw.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"✓ Dataset size: {itemCount:n0}");
                Trace.WriteLine($"✓ Panel type: {panel?.GetType().Name ?? "N/A"}");
                Trace.WriteLine($"✓ Visual items in tree: {panel?.Children.Count.ToString("n0") ?? "N/A"}");
                Trace.WriteLine($"✓ Memory footprint: ~{memoryMB:n0} MB");
                Trace.WriteLine("");
                
                if (panel != null && panel.Children.Count < itemCount * 0.1)
                {
                    var virtualizationPercent = (double)panel.Children.Count / itemCount * 100;
                    Trace.WriteLine("✅ EXCELLENT VIRTUALIZATION!");
                    Trace.WriteLine($"   Only {virtualizationPercent:F2}% of items in visual tree");
                    Trace.WriteLine($"   Ratio: {panel.Children.Count:n0} / {itemCount:n0}");
                }
                Trace.WriteLine("");
                
                Trace.WriteLine("=== INSTRUCTIONS ===");
                Trace.WriteLine("The window is now open with 10,000 items.");
                Trace.WriteLine("Try these features:");
                Trace.WriteLine("  - Scroll through the list (should be smooth due to virtualization)");
                Trace.WriteLine("  - Click column headers to sort");
                Trace.WriteLine("  - Use the filter textbox to search");
                Trace.WriteLine("  - Select multiple items (Ctrl+Click or Shift+Click)");
                Trace.WriteLine("  - Right-click for context menu (Copy, Export to CSV, Export to Notepad)");
                Trace.WriteLine("");
                Trace.WriteLine("Close the window when finished testing.");
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

        // Wait indefinitely for user to close the window
        await testCompleted.Task;
        
        uiThread.Join(2000);
        
        Trace.WriteLine("✓ Test completed successfully");
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestDataGridWithRealClass()
    {
        // This test uses PdfMetaData class from WPF project (real class with get/set properties)
        // to verify if DataGrid works with strongly-typed classes vs anonymous types
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
                Trace.WriteLine("=== DataGrid Test with Real PdfMetaData Class ===");
                Trace.WriteLine("");
                
                // Create sample PdfMetaData items (simplified - no actual PDF files needed)
                var items = new System.Collections.ObjectModel.ObservableCollection<PdfMetaDataSimple>();
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
                
                Trace.WriteLine($"✓ Created {items.Count} PdfMetaDataSimple items");
                Trace.WriteLine($"✓ Item type: {items[0].GetType().FullName}");
                Trace.WriteLine("");
                
                var dataGrid = new DataGrid
                {
                    ItemsSource = items,
                    AutoGenerateColumns = true,
                    CanUserReorderColumns = true,
                    CanUserResizeColumns = true,
                    CanUserSortColumns = true,
                    GridLinesVisibility = DataGridGridLinesVisibility.All,
                    SelectionMode = DataGridSelectionMode.Extended,
                    Width = 900,
                    Height = 500
                };
                
                var window = new Window
                {
                    Title = "DataGrid with Real Class (PdfMetaDataSimple) - 20 Items",
                    Width = 1000,
                    Height = 600,
                    Content = dataGrid,
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
                
                await Task.Delay(1000);
                
                // Diagnostic output
                Trace.WriteLine("=== DIAGNOSTIC INFO ===");
                Trace.WriteLine($"DataGrid.ItemsSource type: {dataGrid.ItemsSource?.GetType().Name}");
                Trace.WriteLine($"DataGrid.ItemsSource count: {items.Count}");
                Trace.WriteLine($"DataGrid columns count: {dataGrid.Columns.Count}");
                
                if (dataGrid.Columns.Count > 0)
                {
                    Trace.WriteLine("✅ DataGrid AUTO-GENERATED columns from PdfMetaDataSimple:");
                    foreach (var col in dataGrid.Columns)
                    {
                        Trace.WriteLine($"  - {col.Header}");
                    }
                    Trace.WriteLine("");
                    Trace.WriteLine("❓ CRITICAL QUESTION: Do you see DATA in the grid?");
                    Trace.WriteLine("   If YES: DataGrid works with real classes (strongly-typed)");
                    Trace.WriteLine("   If NO: DataGrid is fundamentally broken in Avalonia 11.3.9");
                }
                else
                {
                    Trace.WriteLine("❌ NO COLUMNS - DataGrid failed to discover properties!");
                }
                
                Trace.WriteLine("");
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
        
        Trace.WriteLine("✓ Test completed");
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

// Test data class for virtualization tests
public class TestDataItem
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public double Value { get; set; }
    public string Category { get; set; }
    public string Type { get; set; }
    public string Status { get; set; }
    
    public override string ToString() => Name;
}

// Simplified version of PdfMetaData for testing DataGrid with real class
public class PdfMetaDataSimple
{
    public string FileName { get; set; }
    public int NumPages { get; set; }
    public int NumSongs { get; set; }
    public int NumFavorites { get; set; }
    public int LastPageNo { get; set; }
    public string Notes { get; set; }
    
    public override string ToString() => FileName;
}

