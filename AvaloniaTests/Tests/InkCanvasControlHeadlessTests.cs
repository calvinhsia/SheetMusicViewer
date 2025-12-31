using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using SheetMusicViewer.Desktop;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Unit tests for InkCanvasControl.
/// These tests use Avalonia.Headless for testing UI components without a display.
/// 
/// Note: These tests are Windows-only because Avalonia headless with WriteableBitmap
/// has platform-specific issues on macOS/Linux CI environments.
/// </summary>
[TestClass]
[DoNotParallelize] // Avalonia initialization is not thread-safe across test classes
public class InkCanvasControlTests : TestBase
{
    private static bool _avaloniaInitialized = false;
    private static bool _initializationFailed = false;
    private static string _initializationError = null;
    private static readonly object _initLock = new();

    /// <summary>
    /// Initialize Avalonia once for all tests.
    /// </summary>
    private static void EnsureAvaloniaInitialized()
    {
        lock (_initLock)
        {
            if (_avaloniaInitialized || _initializationFailed)
                return;

            try
            {
                // Initialize Avalonia with headless platform for testing
                AppBuilder.Configure<TestApp>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();

                _avaloniaInitialized = true;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already") || ex.Message.Contains("initialized"))
            {
                // Avalonia already initialized - that's fine
                _avaloniaInitialized = true;
            }
            catch (Exception ex)
            {
                _initializationFailed = true;
                _initializationError = ex.Message;
            }
        }
    }

    /// <summary>
    /// Skip test if not on Windows or if Avalonia initialization failed.
    /// Avalonia headless with WriteableBitmap has platform-specific issues on macOS/Linux CI.
    /// </summary>
    private void SkipIfNotSupportedPlatform()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("InkCanvasControl tests are Windows-only due to Avalonia headless platform limitations");
        }
        
        EnsureAvaloniaInitialized();
        
        if (_initializationFailed)
        {
            Assert.Inconclusive($"Avalonia headless initialization failed: {_initializationError}");
        }
        if (!_avaloniaInitialized)
        {
            Assert.Inconclusive("Avalonia headless not initialized - skipping test");
        }
    }

    /// <summary>
    /// Execute an action on the Avalonia dispatcher thread.
    /// This ensures thread affinity for Avalonia UI objects.
    /// </summary>
    private void RunOnDispatcher(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Invoke(action);
        }
    }

    /// <summary>
    /// Execute an async function on the Avalonia dispatcher thread.
    /// </summary>
    private T RunOnDispatcher<T>(Func<T> func)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return func();
        }
        else
        {
            return Dispatcher.UIThread.Invoke(func);
        }
    }

    /// <summary>
    /// Minimal Avalonia app for headless testing
    /// </summary>
    private class TestApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }
    }

    /// <summary>
    /// Creates a simple test bitmap for use in InkCanvasControl tests.
    /// Uses Avalonia's WriteableBitmap to avoid SkiaSharp version conflicts in CI.
    /// </summary>
    private static WriteableBitmap CreateTestBitmap(int width = 200, int height = 300)
    {
        // Create a WriteableBitmap using Avalonia's API (avoids direct SkiaSharp usage)
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        // Fill with a simple pattern using the frame buffer
        using (var frameBuffer = bitmap.Lock())
        {
            var ptr = frameBuffer.Address;
            var stride = frameBuffer.RowBytes;
            var pixelData = new byte[stride * height];
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = y * stride + x * 4; // 4 bytes per pixel (BGRA)
                    
                    // Create a simple grid pattern: white background with light gray grid lines
                    bool isGridLine = (x % 20 == 0) || (y % 20 == 0);
                    byte grayValue = isGridLine ? (byte)0xD3 : (byte)0xFF;
                    
                    // BGRA format
                    pixelData[offset + 0] = grayValue; // Blue
                    pixelData[offset + 1] = grayValue; // Green
                    pixelData[offset + 2] = grayValue; // Red
                    pixelData[offset + 3] = 0xFF;      // Alpha
                }
            }
            
            Marshal.Copy(pixelData, 0, ptr, pixelData.Length);
        }

        return bitmap;
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)] // 30 second timeout to prevent CI hangs
    public void InkCanvasControl_Creation_Succeeds()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange & Act
            using var bitmap = CreateTestBitmap();
            var inkCanvas = new InkCanvasControl(bitmap, pageNo: 5);

            // Assert
            Assert.IsNotNull(inkCanvas);
            Assert.AreEqual(5, inkCanvas.PageNo);
            Assert.IsFalse(inkCanvas.IsInkingEnabled, "Inking should be disabled by default");
            Assert.IsFalse(inkCanvas.HasUnsavedStrokes, "Should have no unsaved strokes initially");
        });
        
        LogMessage("InkCanvasControl created successfully");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_IsInkingEnabled_CanBeToggled()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);

            // Act & Assert
            Assert.IsFalse(inkCanvas.IsInkingEnabled, "Should start disabled");
            
            inkCanvas.IsInkingEnabled = true;
            Assert.IsTrue(inkCanvas.IsInkingEnabled, "Should be enabled after setting true");
            
            inkCanvas.IsInkingEnabled = false;
            Assert.IsFalse(inkCanvas.IsInkingEnabled, "Should be disabled after setting false");
        });
        
        LogMessage("IsInkingEnabled toggle verified");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_GetPortableStrokes_ReturnsEmptyWhenNoStrokes()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);

            // Act
            var strokes = inkCanvas.GetPortableStrokes();

            // Assert
            Assert.IsNotNull(strokes);
            Assert.AreEqual(0, strokes.Strokes.Count, "Should have no strokes initially");
        });
        
        LogMessage("Empty stroke collection verified");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_SetPenColor_DoesNotThrow()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);

            // Act - These methods should not throw
            inkCanvas.SetPenColor(Brushes.Red);
            inkCanvas.SetPenColor(Brushes.Blue);
            inkCanvas.SetPenColor(Brushes.Black);
        });

        // Assert - No exception means success
        Assert.IsTrue(true);
        LogMessage("SetPenColor works without errors");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_SetPenThickness_DoesNotThrow()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);

            // Act
            inkCanvas.SetPenThickness(1.0);
            inkCanvas.SetPenThickness(5.0);
            inkCanvas.SetPenThickness(15.0);
        });

        // Assert - No exception means success
        Assert.IsTrue(true);
        LogMessage("SetPenThickness works without errors");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_SetHighlighter_DoesNotThrow()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);

            // Act
            inkCanvas.SetHighlighter();
        });

        // Assert - No exception means success
        Assert.IsTrue(true);
        LogMessage("SetHighlighter works without errors");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_ClearStrokes_SetsHasUnsavedStrokes()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);
            
            Assert.IsFalse(inkCanvas.HasUnsavedStrokes, "Should start with no unsaved strokes");

            // Act
            inkCanvas.ClearStrokes();

            // Assert
            Assert.IsTrue(inkCanvas.HasUnsavedStrokes, "Clearing should mark as having unsaved changes");
        });
        
        LogMessage("ClearStrokes sets HasUnsavedStrokes");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_MarkAsSaved_ClearsUnsavedFlag()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);
            
            // Set the unsaved flag by clearing
            inkCanvas.ClearStrokes();
            Assert.IsTrue(inkCanvas.HasUnsavedStrokes);

            // Act
            inkCanvas.MarkAsSaved();

            // Assert
            Assert.IsFalse(inkCanvas.HasUnsavedStrokes, "Should clear unsaved flag after marking as saved");
        });
        
        LogMessage("MarkAsSaved clears HasUnsavedStrokes");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_PageNo_IsPreserved()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange & Act
            using var bitmap = CreateTestBitmap();
            
            var canvas1 = new InkCanvasControl(bitmap, pageNo: 0);
            var canvas2 = new InkCanvasControl(bitmap, pageNo: 42);
            var canvas3 = new InkCanvasControl(bitmap, pageNo: 999);

            // Assert
            Assert.AreEqual(0, canvas1.PageNo);
            Assert.AreEqual(42, canvas2.PageNo);
            Assert.AreEqual(999, canvas3.PageNo);
        });
        
        LogMessage("PageNo is correctly preserved");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_WithNullInkData_CreatesSuccessfully()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange & Act
            using var bitmap = CreateTestBitmap();
            var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0, inkStrokeClass: null);

            // Assert
            Assert.IsNotNull(inkCanvas);
            Assert.IsFalse(inkCanvas.HasUnsavedStrokes);
            
            var strokes = inkCanvas.GetPortableStrokes();
            Assert.AreEqual(0, strokes.Strokes.Count);
        });
        
        LogMessage("InkCanvasControl with null ink data created successfully");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_WithEmptyStrokeData_CreatesSuccessfully()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange
            var inkStrokeClass = new InkStrokeClass
            {
                Pageno = 0,
                InkStrokeDimension = new PortablePoint(200, 300),
                StrokeData = Array.Empty<byte>()
            };
            
            using var bitmap = CreateTestBitmap();

            // Act
            var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0, inkStrokeClass: inkStrokeClass);

            // Assert
            Assert.IsNotNull(inkCanvas);
        });
        
        LogMessage("InkCanvasControl with empty stroke data created successfully");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_GetPortableStrokes_ReturnsValidStructure()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap(400, 600);
            var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);

            // Act
            var strokes = inkCanvas.GetPortableStrokes();

            // Assert
            Assert.IsNotNull(strokes);
            Assert.IsNotNull(strokes.Strokes);
            // CanvasWidth/Height are 0 until the control is laid out
            Assert.AreEqual(0, strokes.CanvasWidth, "CanvasWidth is 0 before layout");
            Assert.AreEqual(0, strokes.CanvasHeight, "CanvasHeight is 0 before layout");
        });
        
        LogMessage("GetPortableStrokes returns valid structure");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_MultipleBitmapSizes_AllSucceed()
    {
        SkipIfNotSupportedPlatform();
        
        RunOnDispatcher(() =>
        {
            // Arrange & Act - Test various bitmap sizes
            var sizes = new[] { (100, 100), (200, 300), (800, 600), (1920, 1080) };
            
            foreach (var (width, height) in sizes)
            {
                using var bitmap = CreateTestBitmap(width, height);
                var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);
                
                // Assert
                Assert.IsNotNull(inkCanvas, $"Failed for size {width}x{height}");
            }
        });
        
        LogMessage($"Tested different bitmap sizes successfully");
    }
}
