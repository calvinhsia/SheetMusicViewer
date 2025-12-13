using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer.Desktop;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

[TestClass]
[DoNotParallelize]
public class PdfViewerTests : TestBase
{
    private static bool _avaloniaInitialized = false;
    private static readonly object _initLock = new object();

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestAvaloniaPdfStressTest()
    {
        await Task.Run(() =>
        {
            try
            {
                var app = Program.BuildAvaloniaApp();
                app.StartWithClassicDesktopLifetime(new string[0]);
            }
            catch (Exception ex)
            {
                LogMessage($"Exception: {ex.Message}");
            }
        });
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestAvaloniaPdfViewerUI()
    {
        var pdfPath = Environment.GetEnvironmentVariable("PDF_TEST_PATH");
        if (string.IsNullOrEmpty(pdfPath))
        {
            pdfPath = TestHelpers.CreateTestPdf(pageCount: 50);
        }

        var testCompleted = new TaskCompletionSource<bool>();
        PdfWindow? window = null;

        var uiThread = new Thread(() =>
        {
            try
            {
                var app = TestHelpers.BuildAvaloniaAppForPdfViewer();
                app.StartWithClassicDesktopLifetime(new string[0]);
            }
            catch (Exception ex)
            {
                LogMessage($"UI thread error: {ex.Message}");
                testCompleted.TrySetException(ex);
            }
        });

        PdfViewerApp.OnSetupWindow = (app, lifetime) =>
        {
            window = new PdfWindow();
            
            if (!string.IsNullOrEmpty(pdfPath))
            {
                var field = typeof(PdfWindow).GetField("_pdfFileName", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(window, pdfPath);
            }
            
            lifetime.MainWindow = window;
            
            window.Closed += (s, e) =>
            {
                LogMessage("Window closed by user or timer - completing test");
                testCompleted.TrySetResult(true);
                lifetime.Shutdown();
            };
            
            window.Show();
            
            LogMessage($"✓ PdfWindow created and shown");
        };

        if (OperatingSystem.IsWindows())
        {
            uiThread.SetApartmentState(ApartmentState.STA);
        }
        uiThread.Start();

        var completedTask = await Task.WhenAny(testCompleted.Task);
        if (completedTask != testCompleted.Task)
        {
            LogMessage("Test timed out - manually close the window to complete");
        }
        else
        {
            await testCompleted.Task;
        }

        uiThread.Join(2000);
        
        SafeDeleteFile(pdfPath);
        
        lock (_initLock)
        {
            _avaloniaInitialized = true;
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestPdfViewerWindowLoadsAndDisplaysPdf()
    {
        // Skip this test in CI environments where SkiaSharp native libraries may not be compatible
        SkipIfCI("SkiaSharp native library compatibility issues. Run locally or as Manual test.");
        
        // Check if Avalonia has already been initialized by a previous test
        lock (_initLock)
        {
            if (_avaloniaInitialized)
            {
                Assert.Inconclusive("Test skipped - Avalonia already initialized by another test. Run this test individually.");
                return;
            }
        }
        
        var testPdfPath = TestHelpers.CreateTestPdf();
        
        try
        {
            var testCompleted = new TaskCompletionSource<bool>();
            var window = default(TestablePdfWindow);
            
            var uiThread = new Thread(() =>
            {
                try
                {
                    // Build app configuration
                    var builder = AppBuilder.Configure<TestPdfViewerApp>()
                        .UsePlatformDetect()
                        .WithInterFont()
                        .LogToTrace();
                    
                    // Try to start with classic desktop lifetime
                    try
                    {
                        builder.StartWithClassicDesktopLifetime(Array.Empty<string>());
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("Setup was already called"))
                    {
                        LogMessage("Avalonia already initialized - test cannot continue");
                        testCompleted.TrySetResult(false);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"UI thread error: {ex.Message}");
                    testCompleted.TrySetException(ex);
                }
            });
            
            TestPdfViewerApp.OnSetupWindow = async (app, lifetime) =>
            {
                window = new TestablePdfWindow(testPdfPath);
                lifetime.MainWindow = window;
                window.Show();
                
                try
                {
                    await window.TriggerLoadAsync();
                    LogMessage("PDF load triggered successfully");
                }
                catch (Exception ex)
                {
                    LogMessage($"Error triggering PDF load: {ex.Message}");
                }

                var delay = 3000;
                LogMessage($"Waiting {delay} ms before verification...");
                var timer = new System.Timers.Timer(delay);
                timer.Elapsed += (s, e) =>
                {
                    timer.Stop();
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            Assert.IsNotNull(window, "Window should be created");
                            Assert.AreEqual(testPdfPath, window.GetPdfFileName(), "PDF file name should match");
                            Assert.IsTrue(window.PdfUIEnabled, "PDF UI should be enabled");
                            Assert.AreNotEqual(string.Empty, window.PdfTitle, "PDF title should be set");
                            
                            LogMessage($"✓ Window created and shown");
                            LogMessage($"✓ PDF file: {window.GetPdfFileName()}");
                            LogMessage($"✓ Page count: {window.MaxPageNumberMinus1}");
                            LogMessage($"✓ Page 0 description: {window.Description0}");
                            LogMessage($"✓ Page 1 description: {window.Description1}");
                            
                            if (!window.Description0.Contains("Error"))
                            {
                                LogMessage($"✓ PDF loaded successfully");
                            }
                            else
                            {
                                LogMessage($"⚠ PDF loading had errors: {window.Description0}");
                            }
                            
                            testCompleted.SetResult(true);
                            lifetime.Shutdown();
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Verification error: {ex.Message}");
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
            
            var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(15000));
            if (completedTask != testCompleted.Task)
            {
                Assert.Fail("Test timed out - window may still be showing");
            }

            var result = await testCompleted.Task;
            if (!result)
            {
                Assert.Inconclusive("Test could not run - Avalonia already initialized");
            }
            
            uiThread.Join(2000);
            
            // Mark Avalonia as initialized
            lock (_initLock)
            {
                _avaloniaInitialized = true;
            }
        }
        finally
        {
            SafeDeleteFile(testPdfPath);
        }
    }
}
