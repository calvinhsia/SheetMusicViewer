using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SheetMusicLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Custom ink canvas control that supports drawing on top of a PDF page image.
/// Uses SkiaSharp-compatible rendering for cross-platform support.
/// </summary>
public class InkCanvasControl : Panel
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly List<List<Point>> _normalizedStrokes = new();
    private List<Point>? _currentNormalizedStroke;
    private readonly List<Polyline> _renderedPolylines = new();
    private Polyline? _currentPolyline;
    private bool _isDrawing;
    private IPointer? _drawingPointer; // Track which pointer is drawing
    private IBrush _currentBrush = Brushes.Black;
    private double _strokeThickness = 2.0;
    private readonly Bitmap _backgroundImage;
    private readonly Image _bgImage;
    private Size _lastRenderSize;
    private readonly int _pageNo;
    private InkStrokeClass? _inkStrokeClass;
    private bool _inkLoaded;
    private bool _hasUnsavedStrokes;

    // Store stroke metadata (color, thickness, opacity) for each stroke
    private readonly List<(IBrush Brush, double Thickness)> _strokeMetadata = new();
    private (IBrush Brush, double Thickness)? _currentStrokeMetadata;

    public bool IsInkingEnabled { get; set; }

    /// <summary>
    /// Returns true if there are strokes that haven't been saved yet
    /// </summary>
    public bool HasUnsavedStrokes => _hasUnsavedStrokes;
    
    /// <summary>
    /// The page number this canvas represents
    /// </summary>
    public int PageNo => _pageNo;

    public InkCanvasControl(Bitmap backgroundImage, int pageNo = 0, InkStrokeClass? inkStrokeClass = null)
    {
        _backgroundImage = backgroundImage;
        _pageNo = pageNo;
        _inkStrokeClass = inkStrokeClass;
        
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
        
        // Load ink strokes on first valid size
        if (!_inkLoaded && finalSize.Width > 0 && finalSize.Height > 0)
        {
            _inkLoaded = true;
            LoadInk();
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

    /// <summary>
    /// Load ink strokes from the InkStrokeClass data
    /// </summary>
    private void LoadInk()
    {
        if (_inkStrokeClass == null || _inkStrokeClass.StrokeData == null || _inkStrokeClass.StrokeData.Length == 0)
            return;
            
        if (_inkStrokeClass.InkStrokeDimension.X <= 0 || _inkStrokeClass.InkStrokeDimension.Y <= 0)
            return;

        try
        {
            // StrokeData should be JSON-encoded PortableInkStrokeCollection
            // (converted from ISF to JSON by PdfMetaDataCore when loading from BmkJsonFormat)
            var strokeCollection = ParseStrokeData(_inkStrokeClass.StrokeData);
            
            if (strokeCollection == null || strokeCollection.Strokes.Count == 0)
            {
                Trace.WriteLine($"No strokes parsed for page {_pageNo}");
                return;
            }
            
            // Get canvas dimensions for scaling
            var canvasWidth = strokeCollection.CanvasWidth;
            var canvasHeight = strokeCollection.CanvasHeight;
            
            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                canvasWidth = _inkStrokeClass.InkStrokeDimension.X;
                canvasHeight = _inkStrokeClass.InkStrokeDimension.Y;
            }
            
            foreach (var stroke in strokeCollection.Strokes)
            {
                if (stroke.Points.Count < 2)
                    continue;
                
                // Convert stroke points to normalized points (0-1 range)
                var normalizedPoints = stroke.Points
                    .Select(p => new Point(p.X / canvasWidth, p.Y / canvasHeight))
                    .ToList();
                
                _normalizedStrokes.Add(normalizedPoints);
                
                // Parse stroke color and properties
                IBrush brush = Brushes.Black;
                double thickness = stroke.Thickness;
                
                if (!string.IsNullOrEmpty(stroke.Color))
                {
                    try
                    {
                        var color = Color.Parse(stroke.Color);
                        var opacity = stroke.Opacity > 0 ? stroke.Opacity : (stroke.IsHighlighter ? 0.5 : 1.0);
                        brush = new SolidColorBrush(color, opacity);
                    }
                    catch
                    {
                        // Use default black
                    }
                }
                
                _strokeMetadata.Add((brush, thickness));
            }
            
            if (_normalizedStrokes.Count > 0)
            {
                Trace.WriteLine($"Loaded {_normalizedStrokes.Count} ink strokes for page {_pageNo}");
                RerenderStrokes(Bounds.Size);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error loading ink strokes for page {_pageNo}: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse stroke data from JSON bytes
    /// </summary>
    private static PortableInkStrokeCollection? ParseStrokeData(byte[] data)
    {
        if (data == null || data.Length < 2)
            return null;

        // Check if it's JSON format (starts with '{')
        if (data[0] != '{')
        {
            // Not JSON - likely ISF binary which can't be parsed without Windows APIs
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<PortableInkStrokeCollection>(json, JsonReadOptions);
        }
        catch
        {
            return null;
        }
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
        for (int i = 0; i < _normalizedStrokes.Count; i++)
        {
            var normalizedStroke = _normalizedStrokes[i];
            
            // Get stroke metadata if available
            IBrush brush = _currentBrush;
            double thickness = _strokeThickness;
            
            if (i < _strokeMetadata.Count)
            {
                brush = _strokeMetadata[i].Brush;
                thickness = _strokeMetadata[i].Thickness;
            }
            
            var polyline = new Polyline
            {
                Stroke = brush,
                StrokeThickness = thickness,
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
        
        var clearItem = new MenuItem { Header = "Clear All on this page" };
        clearItem.Click += (s, e) => ClearStrokes();
        contextMenu.Items.Add(clearItem);
        
        ContextMenu = contextMenu;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsInkingEnabled) return;
        
        // Only draw with left mouse button (or primary touch/pen)
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
            return;

        var point = e.GetPosition(this);
        
        // Only start drawing if pointer is within bounds
        if (point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > Bounds.Height)
            return;
        
        var normalizedPoint = ScreenToNormalized(point);
        
        _currentNormalizedStroke = new List<Point> { normalizedPoint };
        _currentStrokeMetadata = (_currentBrush, _strokeThickness);
        
        _currentPolyline = new Polyline
        {
            Stroke = _currentBrush,
            StrokeThickness = _strokeThickness,
            StrokeLineCap = PenLineCap.Round,
            Points = new Points { point }
        };
        
        Children.Add(_currentPolyline);
        _isDrawing = true;
        _drawingPointer = e.Pointer; // Track the pointer that started drawing
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsInkingEnabled || !_isDrawing || _currentPolyline == null || _currentNormalizedStroke == null) return;
        
        // Only process moves from the pointer that started the drawing
        if (_drawingPointer == null || e.Pointer.Id != _drawingPointer.Id) return;
        
        // For pen/stylus, check if it's actually in contact (not just hovering)
        var properties = e.GetCurrentPoint(this).Properties;
        if (e.Pointer.Type == PointerType.Pen && !properties.IsLeftButtonPressed)
            return;

        var point = e.GetPosition(this);
        var normalizedPoint = ScreenToNormalized(point);
        
        _currentNormalizedStroke.Add(normalizedPoint);
        _currentPolyline.Points.Add(point);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsInkingEnabled || !_isDrawing) return;
        
        // Only handle release from the pointer that was drawing
        if (_drawingPointer == null || e.Pointer.Id != _drawingPointer.Id) return;

        if (_currentNormalizedStroke != null && _currentNormalizedStroke.Count > 0)
        {
            _normalizedStrokes.Add(_currentNormalizedStroke);
            if (_currentStrokeMetadata.HasValue)
            {
                _strokeMetadata.Add(_currentStrokeMetadata.Value);
            }
            _hasUnsavedStrokes = true; // Mark as having unsaved changes
        }
        
        if (_currentPolyline != null)
        {
            _renderedPolylines.Add(_currentPolyline);
        }
        
        _currentNormalizedStroke = null;
        _currentStrokeMetadata = null;
        _currentPolyline = null;
        _isDrawing = false;
        _drawingPointer = null;
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
        _strokeMetadata.Clear();
        _currentPolyline = null;
        _currentNormalizedStroke = null;
        _currentStrokeMetadata = null;
        _hasUnsavedStrokes = true; // Clearing strokes is also a change
    }

    /// <summary>
    /// Saves ink strokes to the provided metadata. Returns an InkStrokeClass if there are strokes to save, null otherwise.
    /// </summary>
    public InkStrokeClass? GetInkStrokeDataForSaving()
    {
        if (_normalizedStrokes.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            // No strokes or invalid bounds - return null to indicate deletion
            return null;
        }
        
        var portableStrokes = GetPortableStrokes();
        var json = System.Text.Json.JsonSerializer.Serialize(portableStrokes);
        var strokeData = System.Text.Encoding.UTF8.GetBytes(json);
        
        return new InkStrokeClass
        {
            Pageno = _pageNo,
            InkStrokeDimension = new PortablePoint(Bounds.Width, Bounds.Height),
            StrokeData = strokeData
        };
    }
    
    /// <summary>
    /// Marks the strokes as saved
    /// </summary>
    public void MarkAsSaved()
    {
        _hasUnsavedStrokes = false;
    }

    /// <summary>
    /// Gets the strokes in portable format for saving
    /// </summary>
    public PortableInkStrokeCollection GetPortableStrokes()
    {
        var result = new PortableInkStrokeCollection
        {
            CanvasWidth = Bounds.Width,
            CanvasHeight = Bounds.Height
        };
        
        for (int i = 0; i < _normalizedStrokes.Count; i++)
        {
            var normalizedPoints = _normalizedStrokes[i];
            var stroke = new PortableInkStroke();
            
            foreach (var point in normalizedPoints)
            {
                // Convert normalized points back to canvas coordinates
                stroke.Points.Add(new PortableInkPoint 
                { 
                    X = point.X * Bounds.Width, 
                    Y = point.Y * Bounds.Height 
                });
            }
            
            if (i < _strokeMetadata.Count)
            {
                var (brush, thickness) = _strokeMetadata[i];
                stroke.Thickness = thickness;
                
                if (brush is SolidColorBrush solidBrush)
                {
                    stroke.Color = $"#{solidBrush.Color.R:X2}{solidBrush.Color.G:X2}{solidBrush.Color.B:X2}";
                    stroke.Opacity = solidBrush.Opacity;
                    stroke.IsHighlighter = solidBrush.Opacity < 1.0;
                }
            }
            
            result.Strokes.Add(stroke);
        }
        
        return result;
    }
}
