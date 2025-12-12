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
/// - Single-finger pan (when zoomed)
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
    private Point _initialCenter;
    private Matrix _initialTransform;
    private bool _isGesturing;
    private bool _gestureWasPerformed;
    private bool _hasMoved;
    private Point _pointerDownPosition;
    private Point _lastDragPosition;
    private const double MoveThreshold = 10;
    
    // For double-tap detection
    private readonly Stopwatch _doubleTapStopwatch = new();
    private Point _lastTapLocation;
    private const double DoubleTapDistanceThreshold = 40;
    private const int DoubleTapTimeThreshold = 400;
    
    // For touch navigation debouncing
    private int _lastTouchTimestamp;
    private const int TouchDebounceMs = 300;
    
    // Diagnostic logging
    public bool EnableLogging { get; set; }
    public event EventHandler<string>? LogMessage;
    
    private void Log(string message)
    {
        if (!EnableLogging) return;
        var msg = $"[Gesture] {DateTime.Now:HH:mm:ss.fff} {message}";
        Debug.WriteLine(msg);
        LogMessage?.Invoke(this, msg);
    }
    
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

    public GestureHandler(Control target, bool enableLogging = false)
    {
        _target = target;
        EnableLogging = enableLogging;
        Log($"GestureHandler created for {target.GetType().Name}");
        
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
        Log("Detaching gesture handler");
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
        Log("ResetTransform called");
        _target.RenderTransform = new MatrixTransform(Matrix.Identity);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pointerId = (int)e.Pointer.Id;
        var pointerType = e.Pointer.Type;
        
        // Get position relative to parent (unaffected by our transform)
        var pos = e.GetPosition(_target.Parent as Control ?? _target);
        
        Log($"PRESSED: id={pointerId} type={pointerType} pos=({pos.X:F0},{pos.Y:F0}) disabled={IsDisabled} count={_activePointers.Count}");
        
        if (IsDisabled) return;
        
        _activePointers[pointerId] = e.GetCurrentPoint(_target.Parent as Control ?? _target);
        _hasMoved = false;
        _pointerDownPosition = pos;
        _lastDragPosition = pos;
        
        if (_activePointers.Count == 2)
        {
            _gestureWasPerformed = true;
            Log("  -> 2 pointers - START GESTURE");
            StartGesture();
            e.Handled = true;
        }
        else if (_activePointers.Count == 1)
        {
            _gestureWasPerformed = false;
            _initialTransform = GetCurrentMatrix();
            Log("  -> 1 pointer - ready for tap or pan");
            _lastTapLocation = pos;
        }
        else if (_activePointers.Count > 2)
        {
            _gestureWasPerformed = true;
            Log($"  -> {_activePointers.Count} pointers - suppress nav");
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (IsDisabled) return;
        
        var pointerId = (int)e.Pointer.Id;
        
        if (!_activePointers.ContainsKey(pointerId)) return;
        
        var pos = e.GetPosition(_target.Parent as Control ?? _target);
        
        var distance = GetDistance(pos, _pointerDownPosition);
        if (distance > MoveThreshold && !_hasMoved)
        {
            _hasMoved = true;
            Log($"MOVED: id={pointerId} dist={distance:F1}px (threshold crossed)");
        }
        
        _activePointers[pointerId] = e.GetCurrentPoint(_target.Parent as Control ?? _target);
        
        if (_isGesturing && _activePointers.Count == 2)
        {
            ApplyGestureTransform();
            e.Handled = true;
        }
        else if (_activePointers.Count == 1 && _hasMoved && !_gestureWasPerformed)
        {
            var currentMatrix = GetCurrentMatrix();
            if (!IsIdentityMatrix(currentMatrix))
            {
                ApplySingleFingerPan(pos);
                e.Handled = true;
            }
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pointerId = (int)e.Pointer.Id;
        var wasGesturing = _isGesturing || _gestureWasPerformed;
        
        var pos = e.GetPosition(_target.Parent as Control ?? _target);
        
        Log($"RELEASED: id={pointerId} pos=({pos.X:F0},{pos.Y:F0}) count={_activePointers.Count} wasGest={wasGesturing} moved={_hasMoved}");
        
        if (_activePointers.Count == 1 && !wasGesturing && !IsDisabled && !_hasMoved)
        {
            var now = Environment.TickCount;
            var diff = Math.Abs(now - _lastTouchTimestamp);
            
            Log($"  -> TAP candidate: timeDiff={diff}ms");
            
            if (diff > TouchDebounceMs)
            {
                if (IsDoubleTap(pos))
                {
                    Log("  -> DOUBLE-TAP!");
                    DoubleTapped?.Invoke(this, pos);
                }
                else
                {
                    HandleTapNavigation(pos, e);
                }
                _lastTouchTimestamp = now;
            }
        }
        
        _activePointers.Remove(pointerId);
        
        if (_activePointers.Count < 2)
        {
            if (_isGesturing) Log("  -> Gesture ENDED");
            _isGesturing = false;
        }
        
        if (_activePointers.Count == 0)
        {
            Log("  -> All released, reset state");
            _gestureWasPerformed = false;
            _hasMoved = false;
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        var pointerId = (int)e.Pointer.Id;
        Log($"CAPTURE_LOST: id={pointerId}");
        _activePointers.Remove(pointerId);
        
        if (_activePointers.Count < 2)
        {
            _isGesturing = false;
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (IsDisabled) return;
        
        Log($"WHEEL: delta={e.Delta.Y:F1} ctrl={e.KeyModifiers.HasFlag(KeyModifiers.Control)}");
        
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var pos = e.GetPosition(_target.Parent as Control ?? _target);
            var currentMatrix = GetCurrentMatrix();
            var scaleFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
            
            var newMatrix = Matrix.CreateTranslation(-pos.X, -pos.Y) *
                           Matrix.CreateScale(scaleFactor, scaleFactor) *
                           Matrix.CreateTranslation(pos.X, pos.Y) *
                           currentMatrix;
            
            _target.RenderTransform = new MatrixTransform(newMatrix);
            e.Handled = true;
        }
        else
        {
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
        _initialCenter = GetCenter(points[0], points[1]);
        _initialTransform = GetCurrentMatrix();
        _isGesturing = true;
        
        Log($"  StartGesture: dist={_initialDistance:F1} center=({_initialCenter.X:F0},{_initialCenter.Y:F0})");
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
        var currentCenter = GetCenter(points[0], points[1]);
        
        var scale = currentDistance / _initialDistance;
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) 
            scale = 1;
        
        scale = Math.Max(0.1, Math.Min(scale, 10));
        
        var translateX = currentCenter.X - _initialCenter.X;
        var translateY = currentCenter.Y - _initialCenter.Y;
        
        var newMatrix = Matrix.CreateTranslation(-_initialCenter.X, -_initialCenter.Y) *
                       Matrix.CreateScale(scale, scale) *
                       Matrix.CreateTranslation(_initialCenter.X, _initialCenter.Y) *
                       Matrix.CreateTranslation(translateX, translateY) *
                       _initialTransform;
        
        _target.RenderTransform = new MatrixTransform(newMatrix);
    }

    private void ApplySingleFingerPan(Point currentPos)
    {
        var deltaX = currentPos.X - _lastDragPosition.X;
        var deltaY = currentPos.Y - _lastDragPosition.Y;
        
        if (Math.Abs(deltaX) > 0.5 || Math.Abs(deltaY) > 0.5)
        {
            var currentMatrix = GetCurrentMatrix();
            var newMatrix = Matrix.CreateTranslation(deltaX, deltaY) * currentMatrix;
            _target.RenderTransform = new MatrixTransform(newMatrix);
            _lastDragPosition = currentPos;
        }
    }

    private void HandleTapNavigation(Point pos, PointerReleasedEventArgs e)
    {
        var isTouch = e.Pointer.Type == PointerType.Touch;
        var parent = _target.Parent as Control ?? _target;
        var boundsHeight = parent.Bounds.Height;
        var boundsWidth = parent.Bounds.Width;
        var isInNavigationZone = isTouch ? 
            pos.Y > 0.25 * boundsHeight : true;
        
        Log($"  -> NavCheck: touch={isTouch} bounds=({boundsWidth:F0}x{boundsHeight:F0}) inZone={isInNavigationZone}");
        
        if (!isInNavigationZone)
        {
            Log("  -> In ZOOM zone - no nav");
            return;
        }
        
        var delta = NumPagesPerView;
        
        if (NumPagesPerView > 1)
        {
            var distToMiddle = Math.Abs(boundsWidth / 2 - pos.X);
            if (distToMiddle < boundsWidth / 4)
            {
                delta = 1;
            }
        }
        
        var isLeftSide = pos.X < boundsWidth / 2;
        if (isLeftSide)
        {
            delta = -delta;
        }
        
        Log($"  -> NAVIGATE: delta={delta} left={isLeftSide}");
        NavigationRequested?.Invoke(this, new NavigationEventArgs(delta));
    }

    private bool IsDoubleTap(Point currentPosition)
    {
        var distance = GetDistance(currentPosition, _lastTapLocation);
        var tapsAreCloseInDistance = distance < DoubleTapDistanceThreshold;
        
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

    private static Point GetCenter(Point p1, Point p2)
    {
        return new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
    }

    private static bool IsIdentityMatrix(Matrix m)
    {
        const double epsilon = 0.001;
        return Math.Abs(m.M11 - 1) < epsilon &&
               Math.Abs(m.M12) < epsilon &&
               Math.Abs(m.M21) < epsilon &&
               Math.Abs(m.M22 - 1) < epsilon &&
               Math.Abs(m.M31) < epsilon &&
               Math.Abs(m.M32) < epsilon;
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
