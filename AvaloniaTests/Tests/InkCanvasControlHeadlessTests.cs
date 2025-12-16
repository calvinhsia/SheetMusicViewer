using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using SheetMusicViewer.Desktop;
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AvaloniaTests.Tests;

/// <summary>
/// Unit tests for InkCanvasControl.
/// These tests use Avalonia.Headless for testing UI components without a display.
/// </summary>
[TestClass]
public class InkCanvasControlTests : TestBase
{
    private static bool _avaloniaInitialized = false;
    private static readonly object _initLock = new();

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        lock (_initLock)
        {
            if (!_avaloniaInitialized)
            {
                try
                {
                    // Initialize Avalonia with headless platform for testing
                    AppBuilder.Configure<TestApp>()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                        .SetupWithoutStarting();
                    
                    _avaloniaInitialized = true;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("already"))
                {
                    // Avalonia already initialized by another test class
                    _avaloniaInitialized = true;
                }
            }
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
    /// </summary>
    private static Bitmap CreateTestBitmap(int width = 200, int height = 300)
    {
        // Create a simple bitmap using SkiaSharp
        using var skBitmap = new SkiaSharp.SKBitmap(width, height);
        using var canvas = new SkiaSharp.SKCanvas(skBitmap);
        
        // Fill with white background
        canvas.Clear(SkiaSharp.SKColors.White);
        
        // Draw a simple grid pattern for visual reference
        using var paint = new SkiaSharp.SKPaint
        {
            Color = SkiaSharp.SKColors.LightGray,
            StrokeWidth = 1,
            IsAntialias = true
        };
        
        for (int x = 0; x < width; x += 20)
        {
            canvas.DrawLine(x, 0, x, height, paint);
        }
        for (int y = 0; y < height; y += 20)
        {
            canvas.DrawLine(0, y, width, y, paint);
        }
        
        // Convert to Avalonia bitmap
        using var image = SkiaSharp.SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        
        return new Bitmap(stream);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_Creation_Succeeds()
    {
        // Arrange & Act
        using var bitmap = CreateTestBitmap();
        var inkCanvas = new InkCanvasControl(bitmap, pageNo: 5);

        // Assert
        Assert.IsNotNull(inkCanvas);
        Assert.AreEqual(5, inkCanvas.PageNo);
        Assert.IsFalse(inkCanvas.IsInkingEnabled, "Inking should be disabled by default");
        Assert.IsFalse(inkCanvas.HasUnsavedStrokes, "Should have no unsaved strokes initially");
        
        LogMessage("InkCanvasControl created successfully");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_IsInkingEnabled_CanBeToggled()
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
        
        LogMessage("IsInkingEnabled toggle verified");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_GetPortableStrokes_ReturnsEmptyWhenNoStrokes()
    {
        // Arrange
        using var bitmap = CreateTestBitmap();
        var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);

        // Act
        var strokes = inkCanvas.GetPortableStrokes();

        // Assert
        Assert.IsNotNull(strokes);
        Assert.AreEqual(0, strokes.Strokes.Count, "Should have no strokes initially");
        
        LogMessage("Empty stroke collection verified");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_SetPenColor_DoesNotThrow()
    {
        // Arrange
        using var bitmap = CreateTestBitmap();
        var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);

        // Act - These methods should not throw
        inkCanvas.SetPenColor(Avalonia.Media.Brushes.Red);
        inkCanvas.SetPenColor(Avalonia.Media.Brushes.Blue);
        inkCanvas.SetPenColor(Avalonia.Media.Brushes.Black);

        // Assert - No exception means success
        Assert.IsTrue(true);
        LogMessage("SetPenColor works without errors");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_SetPenThickness_DoesNotThrow()
    {
        // Arrange
        using var bitmap = CreateTestBitmap();
        var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);

        // Act
        inkCanvas.SetPenThickness(1.0);
        inkCanvas.SetPenThickness(5.0);
        inkCanvas.SetPenThickness(15.0);

        // Assert - No exception means success
        Assert.IsTrue(true);
        LogMessage("SetPenThickness works without errors");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_SetHighlighter_DoesNotThrow()
    {
        // Arrange
        using var bitmap = CreateTestBitmap();
        var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);

        // Act
        inkCanvas.SetHighlighter();

        // Assert - No exception means success
        Assert.IsTrue(true);
        LogMessage("SetHighlighter works without errors");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_ClearStrokes_SetsHasUnsavedStrokes()
    {
        // Arrange
        using var bitmap = CreateTestBitmap();
        var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0);
        
        Assert.IsFalse(inkCanvas.HasUnsavedStrokes, "Should start with no unsaved strokes");

        // Act
        inkCanvas.ClearStrokes();

        // Assert
        Assert.IsTrue(inkCanvas.HasUnsavedStrokes, "Clearing should mark as having unsaved changes");
        
        LogMessage("ClearStrokes sets HasUnsavedStrokes");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_MarkAsSaved_ClearsUnsavedFlag()
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
        
        LogMessage("MarkAsSaved clears HasUnsavedStrokes");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_PageNo_IsPreserved()
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
        
        LogMessage("PageNo is correctly preserved");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_WithNullInkData_CreatesSuccessfully()
    {
        // Arrange & Act
        using var bitmap = CreateTestBitmap();
        var inkCanvas = new InkCanvasControl(bitmap, pageNo: 0, inkStrokeClass: null);

        // Assert
        Assert.IsNotNull(inkCanvas);
        Assert.IsFalse(inkCanvas.HasUnsavedStrokes);
        
        var strokes = inkCanvas.GetPortableStrokes();
        Assert.AreEqual(0, strokes.Strokes.Count);
        
        LogMessage("InkCanvasControl with null ink data created successfully");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_WithEmptyStrokeData_CreatesSuccessfully()
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
        
        LogMessage("InkCanvasControl with empty stroke data created successfully");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_GetPortableStrokes_ReturnsValidStructure()
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
        
        LogMessage("GetPortableStrokes returns valid structure");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InkCanvasControl_MultipleBitmapSizes_AllSucceed()
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
        
        LogMessage($"Tested {sizes.Length} different bitmap sizes");
    }
}
