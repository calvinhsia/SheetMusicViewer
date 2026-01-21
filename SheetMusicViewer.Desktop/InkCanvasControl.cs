using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
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
    
    // Undo/Redo stacks
    private readonly Stack<UndoAction> _undoStack = new();
    private readonly Stack<UndoAction> _redoStack = new();
    
    // Floating toolbar
    private Border? _toolbarBorder;
    private StackPanel? _toolbar;
    private Button? _undoButton;
    private Button? _redoButton;
    
    // Tool selection buttons (to update visual state)
    private Button? _blackPenButton;
    private Button? _redPenButton;
    private Button? _bluePenButton;
    private Button? _highlighterButton;
    private Button? _eraserButton;
    
    // Pen mode tracking
    private InkMode _currentMode = InkMode.Pen;
    
    // Event for save request
    public event EventHandler? SaveRequested;

    private bool _isInkingEnabled;
    public bool IsInkingEnabled 
    { 
        get => _isInkingEnabled;
        set
        {
            if (_isInkingEnabled != value)
            {
                _isInkingEnabled = value;
                Trace.WriteLine($"[InkCanvas] Page {_pageNo}: IsInkingEnabled changed to {value}");
                UpdateToolbarVisibility();
                UpdateInkingEventHandlers();
            }
        }
    }

    /// <summary>
    /// Returns true if there are strokes that haven't been saved yet
    /// </summary>
    public bool HasUnsavedStrokes => _hasUnsavedStrokes;
    
    /// <summary>
    /// The page number this canvas represents
    /// </summary>
    public int PageNo => _pageNo;
    
    /// <summary>
    /// Returns true if undo is available
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;
    
    /// <summary>
    /// Returns true if redo is available
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

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
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false // Initially not inking, so allow events to pass through
        };
        Children.Add(_bgImage);
        
        // Note: Pointer event handlers are attached/detached in UpdateInkingEventHandlers()
        // based on IsInkingEnabled state
        
        // Add context menu (kept for mouse right-click)
        InitializeContextMenu();
        
        // Create the floating toolbar (hidden by default)
        CreateFloatingToolbar();
        
        Trace.WriteLine($"[InkCanvas] Created for page {pageNo}, hasInkData={inkStrokeClass != null}");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _bgImage.Measure(availableSize);
        _toolbarBorder?.Measure(availableSize);
        return _bgImage.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _bgImage.Arrange(new Rect(finalSize));
        
        // Arrange all polylines to fill the same area
        foreach (var child in Children)
        {
            if (child != _bgImage && child != _toolbarBorder)
            {
                child.Arrange(new Rect(finalSize));
            }
        }
        
        // Arrange toolbar at left edge, centered vertically
        if (_toolbarBorder != null)
        {
            _toolbarBorder.Measure(finalSize);
            var toolbarSize = _toolbarBorder.DesiredSize;
            var toolbarX = 5; // 5px margin from left edge
            var toolbarY = (finalSize.Height - toolbarSize.Height) / 2; // Center vertically
            if (toolbarY < 5) toolbarY = 5; // Minimum margin from top
            _toolbarBorder.Arrange(new Rect(toolbarX, toolbarY, toolbarSize.Width, toolbarSize.Height));
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
    /// Creates the floating toolbar with ink controls
    /// </summary>
    private void CreateFloatingToolbar()
    {
        _toolbar = new StackPanel
        {
            Orientation = Orientation.Vertical, // Vertical layout
            Spacing = 4
        };
        
        // Black pen button (default selected) - use colored squares
        _blackPenButton = CreateColorButton(Brushes.Black, "Black Pen", () => 
        {
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Black pen selected");
            SetPenColor(Brushes.Black);
            UpdateToolSelection(_blackPenButton);
        });
        _toolbar.Children.Add(_blackPenButton);
        
        // Red pen button
        _redPenButton = CreateColorButton(Brushes.Red, "Red Pen", () => 
        {
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Red pen selected");
            SetPenColor(Brushes.Red);
            UpdateToolSelection(_redPenButton);
        });
        _toolbar.Children.Add(_redPenButton);
        
        // Blue pen button
        _bluePenButton = CreateColorButton(Brushes.Blue, "Blue Pen", () => 
        {
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Blue pen selected");
            SetPenColor(Brushes.Blue);
            UpdateToolSelection(_bluePenButton);
        });
        _toolbar.Children.Add(_bluePenButton);
        
        // Highlighter button (yellow)
        _highlighterButton = CreateColorButton(new SolidColorBrush(Colors.Yellow), "Highlighter", () => 
        {
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Highlighter selected");
            SetHighlighter();
            UpdateToolSelection(_highlighterButton);
        });
        _toolbar.Children.Add(_highlighterButton);
        
        // Separator
        _toolbar.Children.Add(new Border { Height = 8 });
        
        // Line thickness buttons
        _toolbar.Children.Add(CreateThicknessButton(1, "Thin (1px)"));
        _toolbar.Children.Add(CreateThicknessButton(2, "Normal (2px)"));
        _toolbar.Children.Add(CreateThicknessButton(4, "Medium (4px)"));
        _toolbar.Children.Add(CreateThicknessButton(6, "Thick (6px)"));
        
        // Separator
        _toolbar.Children.Add(new Border { Height = 8 });
        
        // Eraser button (white with X)
        _eraserButton = CreateToolbarButton("X", "Eraser (touch strokes to delete)", () =>
        {
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Eraser selected");
            _currentMode = InkMode.Eraser;
            UpdateToolSelection(_eraserButton);
        });
        _eraserButton.Background = Brushes.White;
        _eraserButton.Foreground = Brushes.Red;
        _toolbar.Children.Add(_eraserButton);
        
        // Separator
        _toolbar.Children.Add(new Border { Height = 8 });
        
        // Undo button (not a tool, so no selection state)
        _undoButton = CreateToolbarButton("↶", "Undo (Ctrl+Z)", () =>
        {
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Undo clicked, stack count={_undoStack.Count}");
            Undo();
        }, isToolButton: false);
        _toolbar.Children.Add(_undoButton);
        
        // Redo button
        _redoButton = CreateToolbarButton("↷", "Redo (Ctrl+Y)", () =>
        {
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Redo clicked, stack count={_redoStack.Count}");
            Redo();
        }, isToolButton: false);
        _toolbar.Children.Add(_redoButton);
        
        // Separator
        _toolbar.Children.Add(new Border { Height = 8 });
        
        // Clear button
        var clearBtn = CreateToolbarButton("🗑", "Clear All Strokes", () =>
        {
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Clear clicked, stroke count={_normalizedStrokes.Count}");
            ClearStrokes();
        }, isToolButton: false);
        _toolbar.Children.Add(clearBtn);
        
        // Save button
        var saveBtn = CreateToolbarButton("💾", "Save Ink", () =>
        {
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Save clicked");
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }, isToolButton: false);
        _toolbar.Children.Add(saveBtn);
        
        // Wrap in a border for styling - dock at left edge
        _toolbarBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 240, 240, 240)),
            BorderBrush = Brushes.DarkGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6),
            Child = _toolbar,
            IsVisible = false, // Hidden by default
            IsHitTestVisible = true,
            ZIndex = 1000
        };
        
        // Handle all pointer events on the toolbar to prevent them from 
        // bubbling up to the gesture handler and triggering navigation
        _toolbarBorder.PointerPressed += (s, e) => e.Handled = true;
        _toolbarBorder.PointerReleased += (s, e) => e.Handled = true;
        _toolbarBorder.PointerMoved += (s, e) => { if (e.GetCurrentPoint(_toolbarBorder).Properties.IsLeftButtonPressed) e.Handled = true; };
        
        Children.Add(_toolbarBorder);
        UpdateUndoRedoButtons();
        
        // Set initial selection to black pen
        UpdateToolSelection(_blackPenButton);
    }
    
    /// <summary>
    /// Creates a thickness button showing a horizontal line
    /// </summary>
    private Button CreateThicknessButton(double thickness, string tooltip)
    {
        // Create a line to show the thickness visually
        var line = new Border
        {
            Width = 24,
            Height = thickness,
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(thickness / 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        
        var btn = new Button
        {
            Content = line,
            MinWidth = 36,
            MinHeight = 28,
            Padding = new Thickness(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            [ToolTip.TipProperty] = tooltip,
            Focusable = true,
            Background = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray
        };
        
        btn.Click += (s, e) => 
        {
            Trace.WriteLine($"[InkCanvas] Thickness button '{thickness}px' clicked");
            SetPenThickness(thickness);
            e.Handled = true;
        };
        
        return btn;
    }
    
    /// <summary>
    /// Creates a color button with a colored square
    /// </summary>
    private Button CreateColorButton(IBrush color, string tooltip, Action onClick)
    {
        var colorSquare = new Border
        {
            Width = 20,
            Height = 20,
            Background = color,
            BorderBrush = Brushes.DarkGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            IsHitTestVisible = false // Let clicks pass through to button
        };
        
        var btn = new Button
        {
            Content = colorSquare,
            MinWidth = 32,
            MinHeight = 32,
            Padding = new Thickness(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            [ToolTip.TipProperty] = tooltip,
            Focusable = true,
            Background = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray
        };
        
        btn.Click += (s, e) => 
        {
            Trace.WriteLine($"[InkCanvas] Toolbar color button '{tooltip}' Click event fired");
            onClick();
            e.Handled = true;
        };
        
        return btn;
    }
    
    private Button CreateToolbarButton(string content, string tooltip, Action onClick, bool isToolButton = true)
    {
        var btn = new Button
        {
            Content = content,
            FontSize = 16,
            MinWidth = 36,
            MinHeight = 36,
            Padding = new Thickness(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            [ToolTip.TipProperty] = tooltip,
            Focusable = true,
            Background = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray
        };
        
        btn.Click += (s, e) => 
        {
            Trace.WriteLine($"[InkCanvas] Toolbar button '{content}' Click event fired");
            onClick();
            e.Handled = true;
        };
        
        return btn;
    }
    
    /// <summary>
    /// Updates the visual selection state of tool buttons
    /// </summary>
    private void UpdateToolSelection(Button? selectedButton)
    {
        // Define the selected and unselected backgrounds
        var selectedBg = new SolidColorBrush(Color.FromRgb(180, 200, 220)); // Light blue tint
        var unselectedBg = Brushes.Transparent;
        var selectedBorder = new SolidColorBrush(Color.FromRgb(100, 140, 180)); // Darker blue border
        var unselectedBorder = Brushes.Transparent;
        
        // Update all tool buttons
        var toolButtons = new[] { _blackPenButton, _redPenButton, _bluePenButton, _highlighterButton, _eraserButton };
        
        foreach (var btn in toolButtons)
        {
            if (btn == null) continue;
            
            if (btn == selectedButton)
            {
                btn.Background = selectedBg;
                btn.BorderBrush = selectedBorder;
                btn.BorderThickness = new Thickness(2);
            }
            else
            {
                btn.Background = unselectedBg;
                btn.BorderBrush = unselectedBorder;
                btn.BorderThickness = new Thickness(0);
            }
        }
    }
    
    private void UpdateToolbarVisibility()
    {
        if (_toolbarBorder != null)
        {
            _toolbarBorder.IsVisible = _isInkingEnabled;
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Toolbar visibility set to {_isInkingEnabled}, Bounds={_toolbarBorder.Bounds}");
        }
    }
    
    /// <summary>
    /// Attach or detach pointer event handlers based on inking state.
    /// When inking is disabled, we don't want to capture any pointer events
    /// so they can pass through to the parent for navigation.
    /// </summary>
    private void UpdateInkingEventHandlers()
    {
        if (_isInkingEnabled)
        {
            // Enable hit testing and attach event handlers
            _bgImage.IsHitTestVisible = true;
            _bgImage.PointerPressed += OnPointerPressed;
            _bgImage.PointerMoved += OnPointerMoved;
            _bgImage.PointerReleased += OnPointerReleased;
            _bgImage.PointerCaptureLost += OnPointerCaptureLost;
        }
        else
        {
            // Disable hit testing and detach event handlers
            _bgImage.IsHitTestVisible = false;
            _bgImage.PointerPressed -= OnPointerPressed;
            _bgImage.PointerMoved -= OnPointerMoved;
            _bgImage.PointerReleased -= OnPointerReleased;
            _bgImage.PointerCaptureLost -= OnPointerCaptureLost;
        }
    }
    
    private void UpdateUndoRedoButtons()
    {
        if (_undoButton != null)
        {
            _undoButton.IsEnabled = CanUndo;
        }
        if (_redoButton != null)
        {
            _redoButton.IsEnabled = CanRedo;
        }
        Trace.WriteLine($"[InkCanvas] Page {_pageNo}: UndoRedo buttons updated - CanUndo={CanUndo}, CanRedo={CanRedo}");
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
            // Not JSON - likely ISF binary which can't be parsed without Windows API
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
        redItem.Click += (s, e) => { SetPenColor(Brushes.Red); _currentMode = InkMode.Pen; };
        contextMenu.Items.Add(redItem);
        
        var blackItem = new MenuItem { Header = "Black" };
        blackItem.Click += (s, e) => { SetPenColor(Brushes.Black); _currentMode = InkMode.Pen; };
        contextMenu.Items.Add(blackItem);
        
        var highlighterItem = new MenuItem { Header = "Highlighter" };
        highlighterItem.Click += (s, e) => { SetHighlighter(); _currentMode = InkMode.Highlighter; };
        contextMenu.Items.Add(highlighterItem);
        
        contextMenu.Items.Add(new Separator());
        
        var undoItem = new MenuItem { Header = "Undo", InputGesture = new KeyGesture(Key.Z, KeyModifiers.Control) };
        undoItem.Click += (s, e) => Undo();
        contextMenu.Items.Add(undoItem);
        
        var redoItem = new MenuItem { Header = "Redo", InputGesture = new KeyGesture(Key.Y, KeyModifiers.Control) };
        redoItem.Click += (s, e) => Redo();
        contextMenu.Items.Add(redoItem);
        
        contextMenu.Items.Add(new Separator());
        
        var clearItem = new MenuItem { Header = "Clear All on this page" };
        clearItem.Click += (s, e) => ClearStrokes();
        contextMenu.Items.Add(clearItem);
        
        ContextMenu = contextMenu;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Trace.WriteLine($"[InkCanvas] Page {_pageNo}: OnPointerPressed - IsInkingEnabled={IsInkingEnabled}");
        
        if (!IsInkingEnabled) return;
        
        var properties = e.GetCurrentPoint(_bgImage).Properties;
        
        Trace.WriteLine($"[InkCanvas] Page {_pageNo}: PointerPressed for drawing - Mode={_currentMode}, IsEraser={properties.IsEraser}, IsLeft={properties.IsLeftButtonPressed}");
        
        // Check for pen eraser
        if (properties.IsEraser)
        {
            _currentMode = InkMode.Eraser;
        }
        
        // Handle eraser mode - erase strokes that are touched
        if (_currentMode == InkMode.Eraser)
        {
            var point = e.GetPosition(this);
            TryEraseStrokeAt(point);
            e.Pointer.Capture(_bgImage);
            _isDrawing = true;
            _drawingPointer = e.Pointer;
            e.Handled = true;
            return;
        }
        
        // Only draw with left mouse button (or primary touch/pen)
        if (!properties.IsLeftButtonPressed)
            return;

        var drawPoint = e.GetPosition(this);
        
        // Only start drawing if pointer is within bounds
        if (drawPoint.X < 0 || drawPoint.Y < 0 || drawPoint.X > Bounds.Width || drawPoint.Y > Bounds.Height)
            return;
        
        var normalizedPoint = ScreenToNormalized(drawPoint);
        
        _currentNormalizedStroke = new List<Point> { normalizedPoint };
        _currentStrokeMetadata = (_currentBrush, _strokeThickness);
        
        _currentPolyline = new Polyline
        {
            Stroke = _currentBrush,
            StrokeThickness = _strokeThickness,
            StrokeLineCap = PenLineCap.Round,
            Points = new Points { drawPoint }
        };
        
        // Log the brush being used
        if (_currentBrush is ISolidColorBrush scb)
        {
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Started drawing with color=#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}, thickness={_strokeThickness}");
        }
        
        Children.Add(_currentPolyline);
        _isDrawing = true;
        _drawingPointer = e.Pointer;
        e.Pointer.Capture(_bgImage);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsInkingEnabled || !_isDrawing) return;
        
        if (_drawingPointer == null || e.Pointer.Id != _drawingPointer.Id) return;
        
        var properties = e.GetCurrentPoint(_bgImage).Properties;
        
        // Handle eraser mode during drag
        if (_currentMode == InkMode.Eraser || properties.IsEraser)
        {
            var point = e.GetPosition(this);
            TryEraseStrokeAt(point);
            return;
        }
        
        if (_currentPolyline == null || _currentNormalizedStroke == null) return;

        var drawPoint = e.GetPosition(this);
        var normalizedPoint = ScreenToNormalized(drawPoint);
        
        _currentNormalizedStroke.Add(normalizedPoint);
        _currentPolyline.Points.Add(drawPoint);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsInkingEnabled || !_isDrawing) return;
        
        if (_drawingPointer == null || e.Pointer.Id != _drawingPointer.Id) return;
        
        Trace.WriteLine($"[InkCanvas] Page {_pageNo}: OnPointerReleased");
        
        // Check if pen eraser was released
        var properties = e.GetCurrentPoint(_bgImage).Properties;
        if (properties.IsEraser || _currentMode == InkMode.Eraser)
        {
            _isDrawing = false;
            _drawingPointer = null;
            e.Pointer.Capture(null);
            return;
        }

        FinalizeCurrentStroke();
        e.Pointer.Capture(null);
    }
    
    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // If we lose capture while drawing, finalize the stroke
        if (_isDrawing && _currentNormalizedStroke != null)
        {
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: PointerCaptureLost while drawing");
            FinalizeCurrentStroke();
        }
    }
    
    /// <summary>
    /// Finalize the current stroke and add it to the stroke collection
    /// </summary>
    private void FinalizeCurrentStroke()
    {
        if (_currentNormalizedStroke != null && _currentNormalizedStroke.Count > 0)
        {
            _normalizedStrokes.Add(_currentNormalizedStroke);
            if (_currentStrokeMetadata.HasValue)
            {
                _strokeMetadata.Add(_currentStrokeMetadata.Value);
            }
            _hasUnsavedStrokes = true;
            
            // Add to undo stack
            var strokeIndex = _normalizedStrokes.Count - 1;
            _undoStack.Push(new UndoAction(
                UndoActionType.Add, 
                _currentNormalizedStroke, 
                _currentStrokeMetadata ?? (_currentBrush, _strokeThickness),
                strokeIndex));
            _redoStack.Clear();
            
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: Stroke completed with {_currentNormalizedStroke.Count} points, total strokes={_normalizedStrokes.Count}");
            
            UpdateUndoRedoButtons();
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
    }
    
    /// <summary>
    /// Try to erase a stroke at the given screen position
    /// </summary>
    private void TryEraseStrokeAt(Point screenPoint)
    {
        var normalizedPoint = ScreenToNormalized(screenPoint);
        const double hitThreshold = 0.02; // 2% of canvas size
        
        // Find stroke near this point
        for (int i = _normalizedStrokes.Count - 1; i >= 0; i--)
        {
            var stroke = _normalizedStrokes[i];
            foreach (var pt in stroke)
            {
                var dx = pt.X - normalizedPoint.X;
                var dy = pt.Y - normalizedPoint.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                
                if (dist < hitThreshold)
                {
                    // Found a stroke to erase
                    var removedStroke = _normalizedStrokes[i];
                    var removedMetadata = i < _strokeMetadata.Count ? _strokeMetadata[i] : (_currentBrush, _strokeThickness);
                    
                    // Push to undo stack before removing
                    _undoStack.Push(new UndoAction(UndoActionType.Remove, removedStroke, removedMetadata, i));
                    _redoStack.Clear();
                    
                    // Remove stroke
                    _normalizedStrokes.RemoveAt(i);
                    if (i < _strokeMetadata.Count)
                    {
                        _strokeMetadata.RemoveAt(i);
                    }
                    
                    _hasUnsavedStrokes = true;
                    RerenderStrokes();
                    UpdateUndoRedoButtons();
                    return; // Only erase one stroke per call
                }
            }
        }
    }
    
    /// <summary>
    /// Undo the last stroke action
    /// </summary>
    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        
        var action = _undoStack.Pop();
        
        if (action.Type == UndoActionType.Add)
        {
            // Undo an add = remove the stroke
            if (action.Index < _normalizedStrokes.Count)
            {
                _normalizedStrokes.RemoveAt(action.Index);
                if (action.Index < _strokeMetadata.Count)
                {
                    _strokeMetadata.RemoveAt(action.Index);
                }
            }
        }
        else // UndoActionType.Remove
        {
            // Undo a remove = re-add the stroke
            if (action.Index <= _normalizedStrokes.Count)
            {
                _normalizedStrokes.Insert(action.Index, action.Stroke);
                _strokeMetadata.Insert(action.Index, action.Metadata);
            }
        }
        
        _redoStack.Push(action);
        _hasUnsavedStrokes = true;
        RerenderStrokes();
        UpdateUndoRedoButtons();
    }
    
    /// <summary>
    /// Redo the last undone action
    /// </summary>
    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        
        var action = _redoStack.Pop();
        
        if (action.Type == UndoActionType.Add)
        {
            // Redo an add = re-add the stroke
            if (action.Index <= _normalizedStrokes.Count)
            {
                _normalizedStrokes.Insert(action.Index, action.Stroke);
                _strokeMetadata.Insert(action.Index, action.Metadata);
            }
        }
        else // UndoActionType.Remove
        {
            // Redo a remove = remove the stroke again
            if (action.Index < _normalizedStrokes.Count)
            {
                _normalizedStrokes.RemoveAt(action.Index);
                if (action.Index < _strokeMetadata.Count)
                {
                    _strokeMetadata.RemoveAt(action.Index);
                }
            }
        }
        
        _undoStack.Push(action);
        _hasUnsavedStrokes = true;
        RerenderStrokes();
        UpdateUndoRedoButtons();
    }

    public void SetPenColor(IBrush brush)
    {
        var oldBrush = _currentBrush;
        var oldMode = _currentMode;
        _currentBrush = brush;
        _strokeThickness = 2.0;
        _currentMode = InkMode.Pen;
        
        // Log the color change and mode change
        if (brush is ISolidColorBrush scb)
        {
            var oldColor = oldBrush is ISolidColorBrush oscb ? $"#{oscb.Color.R:X2}{oscb.Color.G:X2}{oscb.Color.B:X2}" : "unknown";
            Trace.WriteLine($"[InkCanvas] Page {_pageNo}: SetPenColor - mode changed from {oldMode} to {_currentMode}, color from {oldColor} to #{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}");
        }
    }

    public void SetPenThickness(double thickness)
    {
        _strokeThickness = thickness;
    }

    public void SetHighlighter()
    {
        _currentBrush = new SolidColorBrush(Colors.Yellow, 0.5);
        _strokeThickness = 15.0;
        _currentMode = InkMode.Highlighter;
    }

    public void ClearStrokes()
    {
        // Add all current strokes to undo stack as a batch
        // For simplicity, we add them individually in reverse order
        for (int i = _normalizedStrokes.Count - 1; i >= 0; i--)
        {
            var stroke = _normalizedStrokes[i];
            var metadata = i < _strokeMetadata.Count ? _strokeMetadata[i] : (_currentBrush, _strokeThickness);
            _undoStack.Push(new UndoAction(UndoActionType.Remove, stroke, metadata, i));
        }
        _redoStack.Clear();
        
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
        _hasUnsavedStrokes = true;
        UpdateUndoRedoButtons();
    }

    /// <summary>
    /// Saves ink strokes to the provided metadata. Returns an InkStrokeClass if there are strokes to save, null otherwise.
    /// </summary>
    public InkStrokeClass? GetInkStrokeDataForSaving()
    {
        if (_normalizedStrokes.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
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
                
                // Use ISolidColorBrush interface to handle both SolidColorBrush and ImmutableSolidColorBrush
                if (brush is ISolidColorBrush solidBrush)
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
    
    /// <summary>
    /// Represents an action that can be undone/redone
    /// </summary>
    private record UndoAction(
        UndoActionType Type,
        List<Point> Stroke,
        (IBrush Brush, double Thickness) Metadata,
        int Index);
    
    /// <summary>
    /// Type of undo action
    /// </summary>
    private enum UndoActionType
    {
        Add,    // A stroke was added
        Remove  // A stroke was removed
    }
    
    /// <summary>
    /// Current ink mode
    /// </summary>
    private enum InkMode
    {
        Pen,
        Highlighter,
        Eraser
    }
}
