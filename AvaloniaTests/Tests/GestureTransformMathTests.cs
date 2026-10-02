using Avalonia;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer.Desktop;

namespace AvaloniaTests.Tests;

/// <summary>Unit tests for the pinch/pan clamping math used by GestureHandler.</summary>
[TestClass]
[TestCategory("Unit")]
public class GestureTransformMathTests
{
    private static readonly Size Viewport = new(800, 600);

    private static Matrix ScaleAndTranslate(double scale, double tx, double ty)
        => new(scale, 0, 0, scale, tx, ty);

    [TestMethod]
    public void ClampScale_NormalisesInvalidValues()
    {
        Assert.AreEqual(1.0, GestureTransformMath.ClampScale(double.NaN));
        Assert.AreEqual(1.0, GestureTransformMath.ClampScale(double.PositiveInfinity));
        Assert.AreEqual(1.0, GestureTransformMath.ClampScale(0));
        Assert.AreEqual(1.0, GestureTransformMath.ClampScale(-3));
    }

    [TestMethod]
    public void ClampScale_RespectsBounds()
    {
        Assert.AreEqual(1.0, GestureTransformMath.ClampScale(0.5));
        Assert.AreEqual(8.0, GestureTransformMath.ClampScale(20));
        Assert.AreEqual(2.5, GestureTransformMath.ClampScale(2.5));
        Assert.AreEqual(0.5, GestureTransformMath.ClampScale(0.25, minScale: 0.5, maxScale: 4));
    }

    [TestMethod]
    public void Clamp_IdentityStaysIdentity()
    {
        var result = GestureTransformMath.Clamp(Matrix.Identity, Viewport);
        Assert.IsTrue(GestureTransformMath.IsIdentity(result));
    }

    [TestMethod]
    public void Clamp_ZoomedContentCannotBePannedPastEdges()
    {
        // 2x zoom on an 800x600 viewport = 1600x1200 content.
        // Translation must stay within [-800, 0] x [-600, 0] or a gap appears.
        var tooFarRight = GestureTransformMath.Clamp(ScaleAndTranslate(2, 300, 200), Viewport);
        Assert.AreEqual(0, tooFarRight.M31, 0.001);
        Assert.AreEqual(0, tooFarRight.M32, 0.001);

        var tooFarLeft = GestureTransformMath.Clamp(ScaleAndTranslate(2, -5000, -5000), Viewport);
        Assert.AreEqual(-800, tooFarLeft.M31, 0.001);
        Assert.AreEqual(-600, tooFarLeft.M32, 0.001);

        var inside = GestureTransformMath.Clamp(ScaleAndTranslate(2, -400, -300), Viewport);
        Assert.AreEqual(-400, inside.M31, 0.001);
        Assert.AreEqual(-300, inside.M32, 0.001);
    }

    [TestMethod]
    public void Clamp_ContentSmallerThanViewportIsCentered()
    {
        var result = GestureTransformMath.Clamp(
            ScaleAndTranslate(0.5, 0, 0), Viewport, minScale: 0.5);

        Assert.AreEqual(0.5, result.M11, 0.001);
        Assert.AreEqual(200, result.M31, 0.001); // (800 - 400) / 2
        Assert.AreEqual(150, result.M32, 0.001); // (600 - 300) / 2
    }

    [TestMethod]
    public void Clamp_ScaleIsClampedToMaximum()
    {
        var result = GestureTransformMath.Clamp(ScaleAndTranslate(100, 0, 0), Viewport);
        Assert.AreEqual(GestureTransformMath.DefaultMaxScale, result.M11, 0.001);
        Assert.AreEqual(GestureTransformMath.DefaultMaxScale, result.M22, 0.001);
    }

    [TestMethod]
    public void Clamp_DegenerateViewportReturnsInputUnchanged()
    {
        var matrix = ScaleAndTranslate(3, 10, 20);
        Assert.AreEqual(matrix, GestureTransformMath.Clamp(matrix, new Size(0, 0)));
        Assert.AreEqual(matrix, GestureTransformMath.Clamp(matrix, new Size(800, 0)));
    }

    [TestMethod]
    public void IsIdentity_DetectsTransformedMatrices()
    {
        Assert.IsTrue(GestureTransformMath.IsIdentity(Matrix.Identity));
        Assert.IsFalse(GestureTransformMath.IsIdentity(ScaleAndTranslate(1.1, 0, 0)));
        Assert.IsFalse(GestureTransformMath.IsIdentity(ScaleAndTranslate(1, 5, 0)));
        Assert.IsFalse(GestureTransformMath.IsIdentity(new Matrix(1, 0.2, 0, 1, 0, 0)));
    }

    [TestMethod]
    public void ClampScale_InvalidValueRespectsCustomRange()
    {
        Assert.AreEqual(2.0, GestureTransformMath.ClampScale(double.NaN, minScale: 2, maxScale: 5));
    }

    [TestMethod]
    public void ClampScale_SwappedBoundsDoNotThrow()
    {
        Assert.AreEqual(2.0, GestureTransformMath.ClampScale(2, minScale: 5, maxScale: 1));
    }

    [TestMethod]
    public void Clamp_LetterboxedContentCannotBePannedOffScreen()
    {
        // Portrait page (300x600) centered in a landscape viewport (800x600)
        var content = new Rect(250, 0, 300, 600);

        // At 3x zoom the page (900x1800) is larger than the viewport in both axes.
        // tx range: [800 - 550*3, -250*3] = [-850, -750]
        var pannedRight = GestureTransformMath.Clamp(
            ScaleAndTranslate(3, 0, 0), Viewport, content);
        Assert.AreEqual(-750, pannedRight.M31, 0.001);

        var pannedLeft = GestureTransformMath.Clamp(
            ScaleAndTranslate(3, -9999, -9999), Viewport, content);
        Assert.AreEqual(-850, pannedLeft.M31, 0.001);
        Assert.AreEqual(-1200, pannedLeft.M32, 0.001); // [600 - 600*3, 0]

        // The page must still cover the viewport horizontally at the extremes
        var pageRight = content.Right * 3 + pannedLeft.M31;
        var pageLeft = content.X * 3 + pannedLeft.M31;
        Assert.IsTrue(pageRight >= Viewport.Width - 0.001);
        Assert.IsTrue(pageLeft <= 0.001);
    }

    [TestMethod]
    public void Clamp_LetterboxedContentAtFitIsCentered()
    {
        // Page off-center; at fit it should be centered in the viewport
        var content = new Rect(100, 0, 300, 600);
        var result = GestureTransformMath.Clamp(ScaleAndTranslate(1, 0, 0), Viewport, content);

        Assert.AreEqual(1, result.M11, 0.001);
        Assert.AreEqual(150, result.M31, 0.001); // content left becomes (800-300)/2 = 250
        Assert.AreEqual(250, content.X + result.M31, 0.001);
    }

    [TestMethod]
    public void ApplyPan_IsNotScaledByCurrentZoom()
    {
        var current = ScaleAndTranslate(2, -40, -30);
        var result = GestureTransformMath.ApplyPan(current, 10, 20);

        // The pan delta is a screen-space delta and must pass through unchanged
        Assert.AreEqual(-30, result.M31, 0.001);
        Assert.AreEqual(-10, result.M32, 0.001);
        Assert.AreEqual(2, result.M11, 0.001);
    }

    [TestMethod]
    public void ApplyZoom_KeepsAnchorPointFixed()
    {
        // Content point (50,50) is displayed at screen (100,100) under 2x scale
        var current = ScaleAndTranslate(2, 0, 0);
        var anchor = new Point(100, 100);

        var zoomed = GestureTransformMath.ApplyZoom(current, anchor, 1.5);

        var anchorAfter = new Point(50, 50) * zoomed;
        Assert.AreEqual(100, anchorAfter.X, 0.001);
        Assert.AreEqual(100, anchorAfter.Y, 0.001);
        Assert.AreEqual(3, zoomed.M11, 0.001);
    }

    [TestMethod]
    public void ApplyZoom_ScalesOtherPointsAboutTheAnchor()
    {
        var current = ScaleAndTranslate(2, 0, 0);
        var anchor = new Point(100, 100);

        var zoomed = GestureTransformMath.ApplyZoom(current, anchor, 1.5);

        // Screen (300,100) is 200px right of the anchor → 300px right after 1.5x
        var pointAfter = new Point(150, 50) * zoomed;
        Assert.AreEqual(400, pointAfter.X, 0.001);
        Assert.AreEqual(100, pointAfter.Y, 0.001);
    }

    [TestMethod]
    public void ApplyZoom_AndPan_ComposeInScreenSpace()
    {
        var current = ScaleAndTranslate(4, -100, -50);
        var anchor = new Point(300, 200);

        var result = GestureTransformMath.ApplyPan(
            GestureTransformMath.ApplyZoom(current, anchor, 2), 25, -15);

        var anchorAfter = new Point((300 + 100) / 4.0, (200 + 50) / 4.0) * result;
        Assert.AreEqual(325, anchorAfter.X, 0.001);
        Assert.AreEqual(185, anchorAfter.Y, 0.001);
    }
}
