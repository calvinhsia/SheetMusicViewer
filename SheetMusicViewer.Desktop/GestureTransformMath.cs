using System;
using Avalonia;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Pure math for clamping the pinch/pan transform of a page viewport.
/// Free of UI controls so it can be unit tested on every platform.
/// </summary>
public static class GestureTransformMath
{
    public const double DefaultMinScale = 1.0;
    public const double DefaultMaxScale = 8.0;

    /// <summary>Clamps a scale factor to [minScale, maxScale]; invalid values become 1.</summary>
    public static double ClampScale(double scale, double minScale = DefaultMinScale, double maxScale = DefaultMaxScale)
    {
        if (minScale > maxScale)
        {
            (minScale, maxScale) = (maxScale, minScale);
        }

        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            return Math.Clamp(1.0, minScale, maxScale);

        return Math.Clamp(scale, minScale, maxScale);
    }

    /// <summary>
    /// Clamps a matrix assuming the visible content fills the whole viewport.
    /// </summary>
    public static Matrix Clamp(Matrix matrix, Size viewport, double minScale = DefaultMinScale, double maxScale = DefaultMaxScale)
        => Clamp(matrix, viewport, new Rect(viewport), minScale, maxScale);

    /// <summary>
    /// Clamps a uniform scale+translation matrix against the real content rect so
    /// the content cannot leave the viewport: covered axes stay covered, smaller
    /// axes are centered. Rotation/skew are discarded.
    /// </summary>
    public static Matrix Clamp(Matrix matrix, Size viewport, Rect content, double minScale = DefaultMinScale, double maxScale = DefaultMaxScale)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return matrix;

        if (content.Width <= 0 || content.Height <= 0 ||
            double.IsNaN(content.Width) || double.IsNaN(content.Height))
        {
            content = new Rect(viewport);
        }

        var scale = ClampScale(matrix.M11, minScale, maxScale);

        var scaledWidth  = content.Width  * scale;
        var scaledHeight = content.Height * scale;

        double tx;
        if (scaledWidth <= viewport.Width)
            tx = (viewport.Width - scaledWidth) / 2.0 - content.X * scale;   // center
        else
            tx = Math.Clamp(matrix.M31, viewport.Width - content.Right * scale, -content.X * scale);

        double ty;
        if (scaledHeight <= viewport.Height)
            ty = (viewport.Height - scaledHeight) / 2.0 - content.Y * scale; // center
        else
            ty = Math.Clamp(matrix.M32, viewport.Height - content.Bottom * scale, -content.Y * scale);

        return new Matrix(scale, 0, 0, scale, tx, ty);
    }

    /// <summary>Applies a screen-space pan (the delta is not scaled by the zoom).</summary>
    public static Matrix ApplyPan(Matrix current, double dx, double dy)
        => current * Matrix.CreateTranslation(dx, dy);

    /// <summary>Zooms about a screen-space point, keeping it stationary.</summary>
    public static Matrix ApplyZoom(Matrix current, Point center, double factor)
        => current *
           Matrix.CreateTranslation(-center.X, -center.Y) *
           Matrix.CreateScale(factor, factor) *
           Matrix.CreateTranslation(center.X, center.Y);

    /// <summary>True when the matrix is (within epsilon) the identity transform.</summary>
    public static bool IsIdentity(Matrix matrix)
    {
        const double epsilon = 0.001;
        return Math.Abs(matrix.M11 - 1) < epsilon &&
               Math.Abs(matrix.M12) < epsilon &&
               Math.Abs(matrix.M21) < epsilon &&
               Math.Abs(matrix.M22 - 1) < epsilon &&
               Math.Abs(matrix.M31) < epsilon &&
               Math.Abs(matrix.M32) < epsilon;
    }
}
