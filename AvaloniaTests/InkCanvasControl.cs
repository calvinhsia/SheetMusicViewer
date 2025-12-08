using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniaTests;

public class InkCanvasControl : Panel
{
    private readonly List<List<Point>> _normalizedStrokes = new();
    private List<Point>? _currentNormalizedStroke;
    private readonly List<Polyline> _renderedPolylines = new();
    private Polyline? _currentPolyline;
    private bool _isDrawing;
    private IBrush _currentBrush = Brushes.Black;
    private double _strokeThickness = 2.0;
    private readonly Bitmap _backgroundImage;
    private readonly Image _bgImage;
    private Size _lastRenderSize;

    public bool IsInkingEnabled { get; set; }

    public InkCanvasControl(Bitmap backgroundImage)
    {
        _backgroundImage = backgroundImage;
        
        ClipToBounds = true;
        
        // Create an Image control for the background that stretches to fill
        _bgImage = new Image
        {
            Source = backgroundImage,
            Stretch = Stretch.Uniform
        };
        Children.Add(_bgImage);
        
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        
        // Add context menu
        InitializeContextMenu();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _bgImage.Measure(availableSize);
        return _bgImage.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _bgImage.Arrange(new Rect(finalSize));
        
        // Arrange all polylines to fill the same area
        foreach (var child in Children)
        {
            if (child != _bgImage)
            {
                child.Arrange(new Rect(finalSize));
            }
        }
        
        // Re-render strokes if size changed and we have strokes to render
        // Use a small tolerance to avoid re-rendering on tiny floating point differences
        if (_normalizedStrokes.Count > 0 && 
            (Math.Abs(finalSize.Width - _lastRenderSize.Width) > 0.5 || 
             Math.Abs(finalSize.Height - _lastRenderSize.Height) > 0.5))
        {
            _lastRenderSize = finalSize;
            RerenderStrokes(finalSize);
        }
        
        return finalSize;
    }

    private void RerenderStrokes()
    {
        RerenderStrokes(Bounds.Size);
    }

    private void RerenderStrokes(Size renderSize)
    {
        if (renderSize.Width <= 0 || renderSize.Height <= 0) return;

        // Remove old rendered polylines (except current drawing)
        foreach (var polyline in _renderedPolylines)
        {
            if (polyline != _currentPolyline)
            {
                Children.Remove(polyline);
            }
        }
        _renderedPolylines.Clear();

        // Re-render all strokes at current size using normalized coordinates
        foreach (var normalizedStroke in _normalizedStrokes)
        {
            var polyline = new Polyline
            {
                Stroke = _currentBrush,
                StrokeThickness = _strokeThickness,
                StrokeLineCap = PenLineCap.Round,
                Points = new Points(normalizedStroke.Select(p => new Point(
                    p.X * renderSize.Width,
                    p.Y * renderSize.Height
                )))
            };
            Children.Add(polyline);
            _renderedPolylines.Add(polyline);
        }
        
        // Add current polyline back if drawing
        if (_currentPolyline != null)
        {
            _renderedPolylines.Add(_currentPolyline);
        }
    }

    private Point ScreenToNormalized(Point screenPoint)
    {
        if (Bounds.Width == 0 || Bounds.Height == 0)
            return new Point(0, 0);
        
        return new Point(
            screenPoint.X / Bounds.Width,
            screenPoint.Y / Bounds.Height
        );
    }

    private void InitializeContextMenu()
    {
        var contextMenu = new ContextMenu();
        
        var redItem = new MenuItem { Header = "Red" };
        redItem.Click += (s, e) => SetPenColor(Brushes.Red);
        contextMenu.Items.Add(redItem);
        
        var blackItem = new MenuItem { Header = "Black" };
        blackItem.Click += (s, e) => SetPenColor(Brushes.Black);
        contextMenu.Items.Add(blackItem);
        
        var highlighterItem = new MenuItem { Header = "Highlighter" };
        highlighterItem.Click += (s, e) => SetHighlighter();
        contextMenu.Items.Add(highlighterItem);
        
        var clearItem = new MenuItem { Header = "Clear All" };
        clearItem.Click += (s, e) => ClearStrokes();
        contextMenu.Items.Add(clearItem);
        
        ContextMenu = contextMenu;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsInkingEnabled) return;

        var point = e.GetPosition(this);
        var normalizedPoint = ScreenToNormalized(point);
        
        _currentNormalizedStroke = new List<Point> { normalizedPoint };
        
        _currentPolyline = new Polyline
        {
            Stroke = _currentBrush,
            StrokeThickness = _strokeThickness,
            StrokeLineCap = PenLineCap.Round,
            Points = new Points { point }
        };
        
        Children.Add(_currentPolyline);
        _isDrawing = true;
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsInkingEnabled || !_isDrawing || _currentPolyline == null || _currentNormalizedStroke == null) return;

        var point = e.GetPosition(this);
        var normalizedPoint = ScreenToNormalized(point);
        
        _currentNormalizedStroke.Add(normalizedPoint);
        _currentPolyline.Points.Add(point);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsInkingEnabled || !_isDrawing) return;

        if (_currentNormalizedStroke != null && _currentNormalizedStroke.Count > 0)
        {
            _normalizedStrokes.Add(_currentNormalizedStroke);
        }
        
        if (_currentPolyline != null)
        {
            _renderedPolylines.Add(_currentPolyline);
        }
        
        _currentNormalizedStroke = null;
        _currentPolyline = null;
        _isDrawing = false;
        e.Pointer.Capture(null);
    }

    public void SetPenColor(IBrush brush)
    {
        _currentBrush = brush;
        _strokeThickness = 2.0;
    }

    public void SetPenThickness(double thickness)
    {
        _strokeThickness = thickness;
    }

    public void SetHighlighter()
    {
        _currentBrush = new SolidColorBrush(Colors.Yellow, 0.5);
        _strokeThickness = 15.0;
    }

    public void ClearStrokes()
    {
        foreach (var polyline in _renderedPolylines)
        {
            Children.Remove(polyline);
        }
        _renderedPolylines.Clear();
        _normalizedStrokes.Clear();
        _currentPolyline = null;
        _currentNormalizedStroke = null;
    }
}
