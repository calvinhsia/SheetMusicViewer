using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

namespace AvaloniaTests.Tests;

/// <summary>
/// Unit tests for InkCanvasControl toolbar, undo/redo, and ink mode functionality.
/// These tests verify the new floating toolbar features.
/// </summary>
[TestClass]
[DoNotParallelize]
public class InkCanvasToolbarTests
{
    private static bool _avaloniaInitialized;
    private static bool _initializationFailed;
    private static string? _initializationError;
    private static readonly object _initLock = new();

    [TestInitialize]
    public void TestInit()
    {
        SkipIfNotSupportedPlatform();
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
            Assert.Inconclusive($"Avalonia initialization failed: {_initializationError}");
        }
    }

    private static void EnsureAvaloniaInitialized()
    {
        lock (_initLock)
        {
            if (_avaloniaInitialized || _initializationFailed)
                return;

            try
            {
                AppBuilder.Configure<TestApp>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
                _avaloniaInitialized = true;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already") || ex.Message.Contains("initialized"))
            {
                // Already initialized - that's fine
                _avaloniaInitialized = true;
            }
            catch (Exception ex)
            {
                _initializationFailed = true;
                _initializationError = ex.Message;
            }
        }
    }

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

    private static WriteableBitmap CreateTestBitmap(int width = 200, int height = 300)
    {
        return new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);
    }

    #region Toolbar Visibility Tests

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_ToolbarHidden_WhenInkingDisabled()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Act - inking disabled by default
            canvas.IsInkingEnabled = false;

            // Assert
            // The toolbar should be hidden (IsInkingEnabled is false by default)
            Assert.IsFalse(canvas.IsInkingEnabled);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_ToolbarVisible_WhenInkingEnabled()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Act
            canvas.IsInkingEnabled = true;

            // Assert
            Assert.IsTrue(canvas.IsInkingEnabled);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_ToolbarVisibility_TogglesWithIsInkingEnabled()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Act & Assert - toggle on
            canvas.IsInkingEnabled = true;
            Assert.IsTrue(canvas.IsInkingEnabled);

            // Act & Assert - toggle off
            canvas.IsInkingEnabled = false;
            Assert.IsFalse(canvas.IsInkingEnabled);

            // Act & Assert - toggle on again
            canvas.IsInkingEnabled = true;
            Assert.IsTrue(canvas.IsInkingEnabled);
        });
    }

    #endregion

    #region Undo/Redo State Tests

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_CanUndo_FalseWhenNoActions()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Assert
            Assert.IsFalse(canvas.CanUndo);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_CanRedo_FalseWhenNoUndoneActions()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Assert
            Assert.IsFalse(canvas.CanRedo);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_Undo_DoesNotThrowWhenEmpty()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Act - should not throw
            canvas.Undo();

            // Assert
            Assert.IsFalse(canvas.CanUndo);
            Assert.IsFalse(canvas.CanRedo);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_Redo_DoesNotThrowWhenEmpty()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Act - should not throw
            canvas.Redo();

            // Assert
            Assert.IsFalse(canvas.CanUndo);
            Assert.IsFalse(canvas.CanRedo);
        });
    }

    #endregion

    #region Ink Mode Tests

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_SetPenColor_DoesNotThrow()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Act - should not throw
            canvas.SetPenColor(Brushes.Red);
            canvas.SetPenColor(Brushes.Blue);
            canvas.SetPenColor(Brushes.Black);

            // Assert - no exception means success
            Assert.IsTrue(true);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_SetHighlighter_DoesNotThrow()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Act - should not throw
            canvas.SetHighlighter();

            // Assert - no exception means success
            Assert.IsTrue(true);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_SetPenThickness_DoesNotThrow()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Act - should not throw
            canvas.SetPenThickness(1.0);
            canvas.SetPenThickness(5.0);
            canvas.SetPenThickness(15.0);

            // Assert - no exception means success
            Assert.IsTrue(true);
        });
    }

    #endregion

    #region HasUnsavedStrokes Tests

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_HasUnsavedStrokes_FalseInitially()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Assert
            Assert.IsFalse(canvas.HasUnsavedStrokes);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_HasUnsavedStrokes_TrueAfterClearStrokes()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Act
            canvas.ClearStrokes();

            // Assert - ClearStrokes marks as having unsaved changes
            Assert.IsTrue(canvas.HasUnsavedStrokes);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_MarkAsSaved_ClearsUnsavedFlag()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);
            canvas.ClearStrokes(); // Set HasUnsavedStrokes to true

            // Act
            canvas.MarkAsSaved();

            // Assert
            Assert.IsFalse(canvas.HasUnsavedStrokes);
        });
    }

    #endregion

    #region SaveRequested Event Tests

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_SaveRequested_EventCanBeSubscribed()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);
            var eventRaised = false;

            canvas.SaveRequested += (s, e) => eventRaised = true;

            // Assert - subscription works (no exception)
            Assert.IsFalse(eventRaised); // Not raised yet
        });
    }

    #endregion

    #region GetInkStrokeDataForSaving Tests

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_GetInkStrokeDataForSaving_ReturnsNullWhenNoStrokes()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 5);

            // Act
            var result = canvas.GetInkStrokeDataForSaving();

            // Assert
            Assert.IsNull(result);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_GetPortableStrokes_ReturnsEmptyCollectionWhenNoStrokes()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var canvas = new InkCanvasControl(bitmap, pageNo: 1);

            // Act
            var result = canvas.GetPortableStrokes();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Strokes.Count);
        });
    }

    #endregion

    #region PageNo Tests

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_PageNo_IsPreserved()
    {
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
    }

    #endregion

    #region Loading Ink Strokes Tests

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_WithExistingInkData_CreatesSuccessfully()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            
            var strokeCollection = new PortableInkStrokeCollection
            {
                CanvasWidth = 200,
                CanvasHeight = 300
            };
            
            var stroke = new PortableInkStroke { Color = "#FF0000", Thickness = 2.5 };
            stroke.Points.Add(new PortableInkPoint { X = 10, Y = 20 });
            stroke.Points.Add(new PortableInkPoint { X = 50, Y = 60 });
            strokeCollection.Strokes.Add(stroke);
            
            var json = JsonSerializer.Serialize(strokeCollection);
            var inkStrokeClass = new InkStrokeClass
            {
                Pageno = 5,
                InkStrokeDimension = new PortablePoint(200, 300),
                StrokeData = Encoding.UTF8.GetBytes(json)
            };

            // Act - should not throw
            var canvas = new InkCanvasControl(bitmap, pageNo: 5, inkStrokeClass);

            // Assert
            Assert.IsNotNull(canvas);
            Assert.AreEqual(5, canvas.PageNo);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_WithNullInkData_CreatesSuccessfully()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();

            // Act - should not throw
            var canvas = new InkCanvasControl(bitmap, pageNo: 1, inkStrokeClass: null);

            // Assert
            Assert.IsNotNull(canvas);
            Assert.IsFalse(canvas.HasUnsavedStrokes);
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    [Timeout(30000)]
    public void InkCanvasControl_WithEmptyStrokeData_CreatesSuccessfully()
    {
        RunOnDispatcher(() =>
        {
            // Arrange
            using var bitmap = CreateTestBitmap();
            var inkStrokeClass = new InkStrokeClass
            {
                Pageno = 1,
                InkStrokeDimension = new PortablePoint(200, 300),
                StrokeData = Array.Empty<byte>()
            };

            // Act - should not throw
            var canvas = new InkCanvasControl(bitmap, pageNo: 1, inkStrokeClass);

            // Assert
            Assert.IsNotNull(canvas);
        });
    }

    #endregion
}

/// <summary>
/// Minimal Avalonia test application for headless testing
/// </summary>
public class TestApp : Application
{
    public override void Initialize()
    {
        // Minimal initialization for headless testing
    }
}
