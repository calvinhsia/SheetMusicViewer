using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;

namespace AvaloniaSimpleApp;

public class InkCanvasControl : Canvas
{
    private readonly List<Polyline> _strokePolylines = new();
    private Polyline? _currentPolyline;
    private bool _isDrawing;
    private IBrush _currentBrush = Brushes.Black;
    private double _strokeThickness = 2.0;
    private readonly Bitmap _backgroundImage;

    public bool IsInkingEnabled { get; set; }

    public InkCanvasControl(Bitmap backgroundImage)
    {
        _backgroundImage = backgroundImage;
        
        // Set explicit size to match the image
        Width = backgroundImage.PixelSize.Width;
        Height = backgroundImage.PixelSize.Height;
        
        // Create an Image control for the background
        var bgImage = new Image
        {
            Source = backgroundImage,
            Stretch = Stretch.Fill,
            Width = Width,
            Height = Height
        };
        Children.Add(bgImage);
        
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        
        // Add context menu
        InitializeContextMenu();
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
        _currentPolyline = new Polyline
        {
            Stroke = _currentBrush,
            StrokeThickness = _strokeThickness,
            StrokeLineCap = PenLineCap.Round,
            Points = new Points { point }
        };
        
        Children.Add(_currentPolyline);
        _strokePolylines.Add(_currentPolyline);
        _isDrawing = true;
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsInkingEnabled || !_isDrawing || _currentPolyline == null) return;

        var point = e.GetPosition(this);
        _currentPolyline.Points.Add(point);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsInkingEnabled || !_isDrawing) return;

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
        foreach (var polyline in _strokePolylines)
        {
            Children.Remove(polyline);
        }
        _strokePolylines.Clear();
    }
}
