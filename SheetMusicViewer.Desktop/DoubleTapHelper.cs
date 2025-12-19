using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Helper class for detecting double-tap gestures with more generous thresholds
/// than Avalonia's built-in DoubleTapped event. This improves touch sensitivity
/// and reliability across different input devices.
/// </summary>
public class DoubleTapHelper
{
    private DateTime _lastTapTime = DateTime.MinValue;
    private Point _lastTapPosition;
    private object? _lastTapTarget;
    
    /// <summary>
    /// Maximum time between taps to count as a double-tap (in milliseconds).
    /// Default is 500ms which is more generous than typical OS defaults (~300ms).
    /// </summary>
    public int TimeThresholdMs { get; set; } = 500;
    
    /// <summary>
    /// Maximum distance between taps to count as a double-tap (in pixels).
    /// Default is 50px which is more forgiving for touch input.
    /// </summary>
    public double DistanceThreshold { get; set; } = 50;
    
    /// <summary>
    /// Checks if the current pointer press event constitutes a double-tap.
    /// Call this from a PointerPressed event handler.
    /// </summary>
    /// <param name="sender">The control that was tapped (used for target matching)</param>
    /// <param name="e">The pointer pressed event args</param>
    /// <returns>True if this is a double-tap, false if it's a first tap</returns>
    public bool IsDoubleTap(object? sender, PointerPressedEventArgs e)
    {
        var control = sender as Control;
        if (control == null) return false;
        
        var pos = e.GetPosition(control);
        return IsDoubleTapCore(sender, pos);
    }
    
    /// <summary>
    /// Checks if the current position constitutes a double-tap on the given target.
    /// Use this overload when you need more control over position calculation.
    /// </summary>
    /// <param name="target">The target object (for matching consecutive taps on same item)</param>
    /// <param name="position">The tap position</param>
    /// <returns>True if this is a double-tap, false if it's a first tap</returns>
    public bool IsDoubleTap(object? target, Point position)
    {
        return IsDoubleTapCore(target, position);
    }
    
    private bool IsDoubleTapCore(object? target, Point position)
    {
        var now = DateTime.Now;
        var timeSinceLastTap = (now - _lastTapTime).TotalMilliseconds;
        var distanceFromLastTap = GetDistance(position, _lastTapPosition);
        
        // Check if this is a double-tap on the same target
        if (_lastTapTarget == target && 
            timeSinceLastTap < TimeThresholdMs && 
            distanceFromLastTap < DistanceThreshold)
        {
            // Double-tap detected - reset state to prevent triple-tap
            Reset();
            return true;
        }
        
        // First tap - record it
        _lastTapTime = now;
        _lastTapPosition = position;
        _lastTapTarget = target;
        return false;
    }
    
    /// <summary>
    /// Resets the double-tap state. Call this after handling a double-tap
    /// or when changing contexts.
    /// </summary>
    public void Reset()
    {
        _lastTapTime = DateTime.MinValue;
        _lastTapTarget = null;
    }
    
    private static double GetDistance(Point p1, Point p2)
    {
        var dx = p1.X - p2.X;
        var dy = p1.Y - p2.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
    
    /// <summary>
    /// Attaches double-tap handling to a control using PointerPressed events.
    /// This provides more reliable double-tap detection than Avalonia's built-in DoubleTapped.
    /// </summary>
    /// <param name="control">The control to attach to</param>
    /// <param name="onDoubleTap">Action to execute on double-tap. Return true to mark event as handled.</param>
    /// <returns>The DoubleTapHelper instance for further configuration</returns>
    public static DoubleTapHelper Attach(Control control, Func<PointerPressedEventArgs, bool> onDoubleTap)
    {
        var helper = new DoubleTapHelper();
        
        control.PointerPressed += (sender, e) =>
        {
            if (helper.IsDoubleTap(sender, e))
            {
                if (onDoubleTap(e))
                {
                    e.Handled = true;
                }
            }
        };
        
        return helper;
    }
    
    /// <summary>
    /// Attaches double-tap handling to a control with a simple action (no return value needed).
    /// </summary>
    /// <param name="control">The control to attach to</param>
    /// <param name="onDoubleTap">Action to execute on double-tap</param>
    /// <returns>The DoubleTapHelper instance for further configuration</returns>
    public static DoubleTapHelper Attach(Control control, Action onDoubleTap)
    {
        return Attach(control, (e) =>
        {
            onDoubleTap();
            return true;
        });
    }
}
