using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AvaloniaTests;

/// <summary>
/// Handles multi-touch gestures for Avalonia controls including:
/// - Pinch-to-zoom
/// - Two-finger pan
/// - Two-finger rotation
/// - Touch navigation (tap left/right to navigate)
/// - Double-tap detection
/// 
/// Avalonia doesn't have built-in ManipulationDelta events like WPF,
/// so this class implements gesture recognition using PointerPressed/Moved/Released.
/// </summary>
public class GestureHandler
{
    private readonly Control _target;
    private readonly Dictionary<int, PointerPoint> _activePointers = new();
    
    // Gesture state
    private double _initialDistance;
    private double _initialAngle;
    private Point _initialCenter;
    private Matrix _initialTransform;
    private bool _isGesturing;
    
    // For double-tap detection
    private readonly Stopwatch _doubleTapStopwatch = new();
    private Point _lastTapLocation;
    private const double DoubleTapDistanceThreshold = 40;
    private const int DoubleTapTimeThreshold = 500; // milliseconds
    
    // For touch navigation debouncing
    private int _lastTouchTimestamp;
    private const int TouchDebounceMs = 300;
    
    /// <summary>
    /// Fired when the user taps to navigate (left side = previous, right side = next)
    /// </summary>
    public event EventHandler<NavigationEventArgs>? NavigationRequested;
    
    /// <summary>
    /// Fired when the user double-taps
    /// </summary>
    public event EventHandler<Point>? DoubleTapped;
    
    /// <summary>
    /// If true, gestures are disabled (e.g., when inking is active)
    /// </summary>
    public bool IsDisabled { get; set; }
    
    /// <summary>
    /// Minimum number of pages to navigate. Usually 1 or 2.
    /// </summary>
    public int NumPagesPerView { get; set; } = 2;

    public GestureHandler(Control target)
    {
        _target = target;
        
        // Ensure the target has a transform we can manipulate
        if (_target.RenderTransform == null)
        {
            _target.RenderTransform = new MatrixTransform(Matrix.Identity);
        }
        
        // Wire up pointer events
        _target.PointerPressed += OnPointerPressed;
        _target.PointerMoved += OnPointerMoved;
        _target.PointerReleased += OnPointerReleased;
        _target.PointerCaptureLost += OnPointerCaptureLost;
        
        // Wire up mouse wheel for Ctrl+scroll zoom
        _target.PointerWheelChanged += OnPointerWheelChanged;
    }

    /// <summary>
    /// Detach all event handlers (call when disposing)
    /// </summary>
    public void Detach()
    {
        _target.PointerPressed -= OnPointerPressed;
        _target.PointerMoved -= OnPointerMoved;
        _target.PointerReleased -= OnPointerReleased;
        _target.PointerCaptureLost -= OnPointerCaptureLost;
        _target.PointerWheelChanged -= OnPointerWheelChanged;
    }

    /// <summary>
    /// Reset the transform to identity (no zoom/pan/rotate)
    /// </summary>
    public void ResetTransform()
    {
        _target.RenderTransform = new MatrixTransform(Matrix.Identity);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsDisabled) return;
        
        var pointer = e.GetCurrentPoint(_target);
        var pointerId = (int)e.Pointer.Id;
        
        _activePointers[pointerId] = pointer;
        
        // Capture pointer for multi-touch
        e.Pointer.Capture(_target);
        
        if (_activePointers.Count == 2)
        {
            // Two fingers down - start pinch/zoom/rotate gesture
            StartGesture();
        }
        else if (_activePointers.Count == 1)
        {
            // Single finger - check for double-tap or navigation
            var pos = pointer.Position;
            
            // Check for double-tap
            if (IsDoubleTap(pos))
            {
                DoubleTapped?.Invoke(this, pos);
                e.Handled = true;
                return;
            }
            
            // Store for potential navigation on release
            _lastTapLocation = pos;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (IsDisabled) return;
        
        var pointer = e.GetCurrentPoint(_target);
        var pointerId = (int)e.Pointer.Id;
        
        if (!_activePointers.ContainsKey(pointerId)) return;
        
        _activePointers[pointerId] = pointer;
        
        if (_isGesturing && _activePointers.Count == 2)
        {
            // Update the transform based on gesture
            ApplyGestureTransform();
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pointerId = (int)e.Pointer.Id;
        
        if (_activePointers.Count == 1 && !_isGesturing && !IsDisabled)
        {
            // Single tap released - check for navigation
            var now = Environment.TickCount;
            var diff = Math.Abs(now - _lastTouchTimestamp);
            
            if (diff > TouchDebounceMs)
            {
                var pos = e.GetCurrentPoint(_target).Position;
                HandleTapNavigation(pos, e);
                _lastTouchTimestamp = now;
            }
        }
        
        _activePointers.Remove(pointerId);
        
        if (_activePointers.Count < 2)
        {
            _isGesturing = false;
        }
        
        e.Pointer.Capture(null);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        var pointerId = (int)e.Pointer.Id;
        _activePointers.Remove(pointerId);
        
        if (_activePointers.Count < 2)
        {
            _isGesturing = false;
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (IsDisabled) return;
        
        // Ctrl+scroll to zoom
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var pos = e.GetPosition(_target);
            var matrix = GetCurrentMatrix();
            pos = matrix.Transform(pos);
            
            var scaleFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
            
            matrix = matrix * Matrix.CreateScale(scaleFactor, scaleFactor);
            // Adjust for zoom center
            var newMatrix = Matrix.CreateTranslation(-pos.X, -pos.Y) *
                           Matrix.CreateScale(scaleFactor, scaleFactor) *
                           Matrix.CreateTranslation(pos.X, pos.Y) *
                           GetCurrentMatrix();
            
            _target.RenderTransform = new MatrixTransform(newMatrix);
            e.Handled = true;
        }
        else
        {
            // Without Ctrl, reset transform
            ResetTransform();
            e.Handled = true;
        }
    }

    private void StartGesture()
    {
        if (_activePointers.Count != 2) return;
        
        var points = new List<Point>();
        foreach (var p in _activePointers.Values)
        {
            points.Add(p.Position);
        }
        
        _initialDistance = GetDistance(points[0], points[1]);
        _initialAngle = GetAngle(points[0], points[1]);
        _initialCenter = GetCenter(points[0], points[1]);
        _initialTransform = GetCurrentMatrix();
        _isGesturing = true;
    }

    private void ApplyGestureTransform()
    {
        if (_activePointers.Count != 2) return;
        
        var points = new List<Point>();
        foreach (var p in _activePointers.Values)
        {
            points.Add(p.Position);
        }
        
        var currentDistance = GetDistance(points[0], points[1]);
        var currentAngle = GetAngle(points[0], points[1]);
        var currentCenter = GetCenter(points[0], points[1]);
        
        // Calculate scale
        var scale = currentDistance / _initialDistance;
        if (double.IsNaN(scale) || double.IsInfinity(scale)) scale = 1;
        
        // Calculate rotation
        var rotation = currentAngle - _initialAngle;
        
        // Calculate translation
        var translateX = currentCenter.X - _initialCenter.X;
        var translateY = currentCenter.Y - _initialCenter.Y;
        
        // Build the new transform
        var matrix = _initialTransform;
        
        // Transform the center point
        var center = matrix.Transform(_initialCenter);
        
        // Apply scale at center
        matrix = Matrix.CreateTranslation(-center.X, -center.Y) *
                Matrix.CreateScale(scale, scale) *
                Matrix.CreateRotation(rotation * Math.PI / 180) *
                Matrix.CreateTranslation(center.X, center.Y) *
                Matrix.CreateTranslation(translateX, translateY) *
                _initialTransform;
        
        // Simplified approach: just apply scale and translate
        var newMatrix = Matrix.CreateTranslation(-_initialCenter.X, -_initialCenter.Y) *
                       Matrix.CreateScale(scale, scale) *
                       Matrix.CreateTranslation(_initialCenter.X, _initialCenter.Y) *
                       Matrix.CreateTranslation(translateX, translateY) *
                       _initialTransform;
        
        _target.RenderTransform = new MatrixTransform(newMatrix);
    }

    private void HandleTapNavigation(Point pos, PointerReleasedEventArgs e)
    {
        // Only handle navigation for touch or mouse in bottom portion of screen
        var isTouch = e.Pointer.Type == PointerType.Touch;
        var isInNavigationZone = isTouch ? 
            pos.Y > 0.75 * _target.Bounds.Height : // Touch: bottom 25%
            true; // Mouse: anywhere
        
        if (!isInNavigationZone) return;
        
        var delta = NumPagesPerView;
        
        // If showing 2 pages and tap is near center, navigate by 1 page
        if (NumPagesPerView > 1)
        {
            var distToMiddle = Math.Abs(_target.Bounds.Width / 2 - pos.X);
            if (distToMiddle < _target.Bounds.Width / 4)
            {
                delta = 1;
            }
        }
        
        // Left side = previous, right side = next
        var isLeftSide = pos.X < _target.Bounds.Width / 2;
        if (isLeftSide)
        {
            delta = -delta;
        }
        
        NavigationRequested?.Invoke(this, new NavigationEventArgs(delta));
    }

    private bool IsDoubleTap(Point currentPosition)
    {
        var tapsAreCloseInDistance = GetDistance(currentPosition, _lastTapLocation) < DoubleTapDistanceThreshold;
        
        var elapsed = _doubleTapStopwatch.Elapsed;
        _doubleTapStopwatch.Restart();
        
        var tapsAreCloseInTime = elapsed != TimeSpan.Zero && 
                                 elapsed < TimeSpan.FromMilliseconds(DoubleTapTimeThreshold);
        
        _lastTapLocation = currentPosition;
        
        return tapsAreCloseInDistance && tapsAreCloseInTime;
    }

    private Matrix GetCurrentMatrix()
    {
        if (_target.RenderTransform is MatrixTransform mt)
        {
            return mt.Matrix;
        }
        return Matrix.Identity;
    }

    private static double GetDistance(Point p1, Point p2)
    {
        var dx = p1.X - p2.X;
        var dy = p1.Y - p2.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double GetAngle(Point p1, Point p2)
    {
        return Math.Atan2(p2.Y - p1.Y, p2.X - p1.X) * 180 / Math.PI;
    }

    private static Point GetCenter(Point p1, Point p2)
    {
        return new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
    }
}

/// <summary>
/// Event args for navigation requests
/// </summary>
public class NavigationEventArgs : EventArgs
{
    /// <summary>
    /// Number of pages to navigate. Positive = forward, negative = backward.
    /// </summary>
    public int Delta { get; }
    
    public NavigationEventArgs(int delta)
    {
        Delta = delta;
    }
}
