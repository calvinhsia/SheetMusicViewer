using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Handles multi-touch gestures for Avalonia controls including:
/// - Pinch-to-zoom
/// - Two-finger pan
/// - Single-finger pan (when zoomed)
/// - Touch navigation (tap left/right to navigate, only when fit-to-window)
/// - Double-tap to reset the zoom while zoomed/panned
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
    
    // For double-tap detection (thresholds configurable from user options)
    private Point _lastTapLocation;
    private long _lastTapTimeMs = long.MinValue;

    public double DoubleTapTimeMs { get; set; } = 400;

    public double DoubleTapDistancePx { get; set; } = 40;

    // Zoom relative to the fit-to-window scale
    public double MinScale { get; set; } = GestureTransformMath.DefaultMinScale;
    public double MaxScale { get; set; } = GestureTransformMath.DefaultMaxScale;
    
    // Diagnostic logging
    public bool EnableLogging { get; set; }
    public event EventHandler<string>? LogMessage;

    // Cached content rect for the clamp; invalidated on layout/page changes
    private Rect? _cachedContentBounds;
    
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

    // True when zoomed in beyond fit (panning at fit does not count)
    public bool IsTransformed => GetCurrentMatrix().M11 > 1.001;

    public void InvalidateContentBounds() => _cachedContentBounds = null;
    
    /// <summary>
    /// Minimum number of pages to navigate. Usually 1 or 2.
    /// </summary>
    public int NumPagesPerView { get; set; } = 2;

    // Optional provider for the visible content rect (letterboxed page area)
    public Func<Rect>? ContentBoundsProvider { get; set; }

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

        // The clamp math assumes a top-left origin; Avalonia defaults to Center,
        // which would shift every transform and defeat the clamping.
        _target.RenderTransformOrigin = RelativePoint.TopLeft;
        
        // Wire up pointer events using AddHandler to properly see handled events
        // We need to check e.Handled ourselves since child controls (like InkCanvas) 
        // may have already handled the event
        _target.AddHandler(Control.PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        _target.AddHandler(Control.PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        _target.AddHandler(Control.PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        _target.AddHandler(Control.PointerCaptureLostEvent, OnPointerCaptureLost, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        
        // Wire up mouse wheel for Ctrl+scroll zoom
        _target.PointerWheelChanged += OnPointerWheelChanged;

        // Re-clamp the zoom/pan when the window is resized or the tablet is rotated
        _target.PropertyChanged += OnTargetPropertyChanged;
    }

    /// <summary>
    /// Detach all event handlers (call when disposing)
    /// </summary>
    public void Detach()
    {
        Log("Detaching gesture handler");
        _target.RemoveHandler(Control.PointerPressedEvent, OnPointerPressed);
        _target.RemoveHandler(Control.PointerMovedEvent, OnPointerMoved);
        _target.RemoveHandler(Control.PointerReleasedEvent, OnPointerReleased);
        _target.RemoveHandler(Control.PointerCaptureLostEvent, OnPointerCaptureLost);
        _target.PointerWheelChanged -= OnPointerWheelChanged;
        _target.PropertyChanged -= OnTargetPropertyChanged;
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
        
        Log($"PRESSED: id={pointerId} type={pointerType} pos=({pos.X:F0},{pos.Y:F0}) disabled={IsDisabled} count={_activePointers.Count} handled={e.Handled}");
        
        // Skip if disabled or already handled by a child control (e.g., InkCanvas)
        if (IsDisabled || e.Handled) return;
        
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
            if (!GestureTransformMath.IsIdentity(currentMatrix))
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
        
        Log($"RELEASED: id={pointerId} pos=({pos.X:F0},{pos.Y:F0}) count={_activePointers.Count} wasGest={wasGesturing} moved={_hasMoved} handled={e.Handled}");
        
        // Skip tap/navigation processing if the event was already handled (e.g., by InkCanvas eraser).
        // At fit a one finger drag has no other meaning, so movement only disqualifies a tap while zoomed.
        if (_activePointers.Count == 1 && !wasGesturing && !IsDisabled && !e.Handled &&
            (!IsTransformed || !_hasMoved))
        {
            if (IsTransformed)
            {
                // While zoomed, taps never navigate; a double tap resets to fit-to-window
                if (IsDoubleTap(pos))
                {
                    _lastTapTimeMs = long.MinValue;
                    Log("  -> DOUBLE-TAP (reset to fit)");
                    DoubleTapped?.Invoke(this, pos);
                }
                else
                {
                    Log("  -> Tap while transformed - no navigation");
                }
            }
            else
            {
                // Fit-to-window: navigate on every tap; page turns must stay instant.
                // Pairing fit taps for double-tap detection would swallow page turns.
                _lastTapTimeMs = long.MinValue;
                Log("  -> NAVIGATE (tap)");
                HandleTapNavigation(pos, e);
            }
        }
        
        _activePointers.Remove(pointerId);

        if (_activePointers.Count == 2)
        {
            // Dropped from 3+ fingers back to 2: restart with a fresh baseline
            StartGesture();
        }
        else if (_activePointers.Count < 2)
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
        
        if (_activePointers.Count == 2)
        {
            // A shifting pair needs a fresh baseline too
            StartGesture();
        }
        else if (_activePointers.Count < 2)
        {
            _isGesturing = false;
        }

        if (_activePointers.Count == 0)
        {
            _gestureWasPerformed = false;
            _hasMoved = false;
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (IsDisabled) return;
        
        Log($"WHEEL: delta={e.Delta.Y:F1} ctrl={e.KeyModifiers.HasFlag(KeyModifiers.Control)}");
        
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Delta.Y == 0) return;
            var pos = e.GetPosition(_target.Parent as Control ?? _target);
            var currentMatrix = GetCurrentMatrix();
            var scaleFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
            
            var newMatrix = GestureTransformMath.ApplyZoom(currentMatrix, pos, scaleFactor);
            
            SetTransform(newMatrix);
            e.Handled = true;
        }
        // Non-Ctrl wheel scroll leaves the zoom alone (two-finger scrolling used to clear it)
    }

    private void StartGesture()
    {
        if (_activePointers.Count != 2) return;

        // A multi-touch gesture must not be mistaken for a double-tap afterwards
        _lastTapTimeMs = long.MinValue;

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
        
        var newMatrix = GestureTransformMath.ApplyPan(
            GestureTransformMath.ApplyZoom(_initialTransform, _initialCenter, scale),
            translateX, translateY);

        SetTransform(newMatrix);
    }

    private void ApplySingleFingerPan(Point currentPos)
    {
        var deltaX = currentPos.X - _lastDragPosition.X;
        var deltaY = currentPos.Y - _lastDragPosition.Y;
        
        if (Math.Abs(deltaX) > 0.5 || Math.Abs(deltaY) > 0.5)
        {
            var currentMatrix = GetCurrentMatrix();
            SetTransform(GestureTransformMath.ApplyPan(currentMatrix, deltaX, deltaY));
            _lastDragPosition = currentPos;
            _lastTapTimeMs = long.MinValue; // a pan is not part of a double-tap
        }
    }

    // Applies a matrix after clamping it to the viewport
    private void SetTransform(Matrix matrix)
    {
        _target.RenderTransform = new MatrixTransform(ClampMatrix(matrix));
    }

    // Re-clamps the current transform (called on resize/rotation)
    public void ClampTransform()
    {
        var current = GetCurrentMatrix();
        if (IsDisabled || GestureTransformMath.IsIdentity(current)) return;

        var clamped = ClampMatrix(current);
        if (!GestureTransformMath.IsIdentity(clamped))
        {
            _target.RenderTransform = new MatrixTransform(clamped);
        }
        else
        {
            ResetTransform();
        }
    }

    private Matrix ClampMatrix(Matrix matrix)
    {
        var viewport = _target.Bounds.Size;
        if (viewport.Width <= 0 || viewport.Height <= 0) return matrix;

        var content = _cachedContentBounds ??= (ContentBoundsProvider?.Invoke() ?? default);
        return GestureTransformMath.Clamp(matrix, viewport, content, MinScale, MaxScale);
    }

    private void OnTargetPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty)
        {
            // Bounds change implies a (re)layout: the content rect may have moved
            _cachedContentBounds = null;
            ClampTransform();
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
        var now = Environment.TickCount64;
        var distance = GetDistance(currentPosition, _lastTapLocation);
        var tapsAreCloseInDistance = distance < DoubleTapDistancePx;
        var tapsAreCloseInTime = _lastTapTimeMs != long.MinValue &&
                                 now - _lastTapTimeMs <= DoubleTapTimeMs;

        _lastTapTimeMs = now;
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
