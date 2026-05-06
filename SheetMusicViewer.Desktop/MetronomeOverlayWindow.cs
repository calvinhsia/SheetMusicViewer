using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SheetMusicLib;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Transparent floating overlay window that shows metronome controls
/// and provides a visual beat indicator.  The window persists across page
/// turns (it is owned by PdfViewerWindow and stays open independently).
/// Closing the window also stops the metronome.
/// </summary>
public class MetronomeOverlayWindow : Window, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    // ── Dependencies ──────────────────────────────────────────────────────
    private readonly MetronomeService _metronome;

    // ── Beat flash ────────────────────────────────────────────────────────
    private readonly DispatcherTimer _fadeTimer;
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(255,  80,  80));
    private static readonly IBrush NormalBrush = new SolidColorBrush(Color.FromRgb( 80, 160, 255));
    private static readonly IBrush IdleBrush   = new SolidColorBrush(Color.FromArgb(180,  50,  50,  50));

    // ── Touch/pen/mouse drag tracking ────────────────────────────────────
    private bool    _isDragging;
    private Point   _dragStart;       // pointer position relative to window at drag start
    private IPointer? _dragPointer;   // captured pointer

    // ── Bindable backing fields ───────────────────────────────────────────
    private bool   _isRunning;
    private int    _tempo;
    private int    _accentEvery;
    private bool   _muteAudio;
    private string _startStopLabel = "▶ Start";
    private IBrush _beatBrush      = IdleBrush;
    private string _beatCountText  = "–";

    // ── Properties ────────────────────────────────────────────────────────

    public bool IsRunning
    {
        get => _isRunning;
        private set { _isRunning = value; OnPC(); StartStopLabel = value ? "■ Stop" : "▶ Start"; }
    }

    public int Tempo
    {
        get => _tempo;
        set
        {
            _tempo = Math.Clamp(value, 20, 300);
            OnPC();
            _metronome.Tempo = _tempo;
            SaveSettings();
        }
    }

    /// <summary>Accent every N beats.  0 = no accent; 4 = accent on beat 1, 5, 9 …</summary>
    public int AccentEvery
    {
        get => _accentEvery;
        set
        {
            _accentEvery = Math.Max(0, value);
            OnPC();
            _metronome.AccentEvery = _accentEvery;
            SaveSettings();
        }
    }

    public bool MuteAudio
    {
        get => _muteAudio;
        set
        {
            _muteAudio = value;
            OnPC();
            _metronome.MuteAudio = value;
            SaveSettings();
        }
    }

    public string StartStopLabel
    {
        get => _startStopLabel;
        private set { _startStopLabel = value; OnPC(); }
    }

    public IBrush BeatBrush
    {
        get => _beatBrush;
        private set { _beatBrush = value; OnPC(); }
    }

    /// <summary>Running beat count shown in the overlay (resets on Start).</summary>
    public string BeatCountText
    {
        get => _beatCountText;
        private set { _beatCountText = value; OnPC(); }
    }

    // ── Constructor ───────────────────────────────────────────────────────
    public MetronomeOverlayWindow(MetronomeService metronome)
    {
        _metronome = metronome;
        _metronome.Beat += OnBeat;

        // Load persisted settings
        var settings = AppSettings.Instance.Metronome;
        _tempo       = settings.Tempo;
        _accentEvery = settings.AccentEvery;
        _muteAudio   = settings.MuteAudio;

        // Push to engine
        _metronome.Tempo       = _tempo;
        _metronome.AccentEvery = _accentEvery;
        _metronome.MuteAudio   = _muteAudio;
        _isRunning             = metronome.IsRunning;

        // Fade timer restores idle colour after each beat flash
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _fadeTimer.Tick += (_, _) => { BeatBrush = IdleBrush; _fadeTimer.Stop(); };

        // ── Window chrome ──────────────────────────────────────────────────
        Title                 = "Metronome";
        Width                 = 280;
        Height                = 220;
        CanResize             = true;
        ShowInTaskbar         = false;
        Topmost               = true;
        SystemDecorations     = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background            = Brushes.Transparent;

        DataContext = this;
        BuildUI();
        RestorePosition(settings);

        PositionChanged += (_, _) => SaveSettings();

        // Closing the window stops the metronome
        Closing += (_, _) =>
        {
            if (_metronome.IsRunning)
            {
                _metronome.Stop();
                BeatBrush     = IdleBrush;
                BeatCountText = "–";
                _fadeTimer.Stop();
                IsRunning     = false;
            }
            _metronome.Beat -= OnBeat;
        };
    }

    // ── UI construction ────────────────────────────────────────────────────
    private void BuildUI()
    {
        // Semi-transparent dark pill that floats over the sheet music
        var panel = new Border
        {
            Background   = new SolidColorBrush(Color.FromArgb(110, 20, 20, 20)),
            CornerRadius = new CornerRadius(12),
            Padding      = new Thickness(14, 10),
            BoxShadow    = new BoxShadows(new BoxShadow
            {
                OffsetX = 0, OffsetY = 2, Blur = 12,
                Color   = Color.FromArgb(160, 0, 0, 0)
            })
        };

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 7 };

        // ── Title / drag handle / close ────────────────────────────────────
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text              = "🎵 Metronome",
            Foreground        = Brushes.White,
            FontWeight        = FontWeight.SemiBold,
            FontSize          = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor            = new Cursor(StandardCursorType.SizeAll)
        };
        Grid.SetColumn(titleBlock, 0);
        titleRow.Children.Add(titleBlock);

        var closeBtn = MakeIconButton("✕", "Close (also stops metronome)");
        closeBtn.Click += (_, _) => Close();
        Grid.SetColumn(closeBtn, 1);
        titleRow.Children.Add(closeBtn);
        root.Children.Add(titleRow);

        // ── Beat flash circle + running beat counter ───────────────────────
        var beatRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing             = 10,
            VerticalAlignment   = VerticalAlignment.Center
        };

        var beatLight = new Border
        {
            Width        = 32,
            Height       = 32,
            CornerRadius = new CornerRadius(16),
            [!Border.BackgroundProperty] = new Avalonia.Data.Binding(nameof(BeatBrush))
        };
        beatRow.Children.Add(beatLight);

        var beatCountLabel = new TextBlock
        {
            Foreground        = Brushes.White,
            FontSize          = 20,
            FontWeight        = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth          = 50,
            TextAlignment     = TextAlignment.Center,
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(BeatCountText))
        };
        beatRow.Children.Add(beatCountLabel);
        root.Children.Add(beatRow);

        // ── Tempo row ─────────────────────────────────────────────────────
        var tempoRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            Spacing             = 4,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        tempoRow.Children.Add(MakeLabel("BPM:"));
        tempoRow.Children.Add(MakeAdjustButton("−10", () => Tempo = Math.Max(20, Tempo - 10)));
        tempoRow.Children.Add(MakeAdjustButton("−",   () => Tempo = Math.Max(20, Tempo - 1)));

        var tempoBox = new NumericUpDown
        {
            Minimum           = 20,
            Maximum           = 300,
            Increment         = 1,
            Width             = 72,
            Foreground        = Brushes.White,
            Background        = new SolidColorBrush(Color.FromArgb(160, 60, 60, 60)),
            BorderBrush       = Brushes.Gray,
            ShowButtonSpinner = false,
            [!NumericUpDown.ValueProperty] = new Avalonia.Data.Binding(nameof(Tempo))
                { Mode = Avalonia.Data.BindingMode.TwoWay }
        };
        tempoRow.Children.Add(tempoBox);
        tempoRow.Children.Add(MakeAdjustButton("+",   () => Tempo = Math.Min(300, Tempo + 1)));
        tempoRow.Children.Add(MakeAdjustButton("+10", () => Tempo = Math.Min(300, Tempo + 10)));
        root.Children.Add(tempoRow);

        // ── Accent-every row ───────────────────────────────────────────────
        var accentRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            Spacing             = 4,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        accentRow.Children.Add(MakeLabel("Accent every:"));
        accentRow.Children.Add(MakeAdjustButton("−", () => AccentEvery = Math.Max(0, AccentEvery - 1)));

        var accentBox = new NumericUpDown
        {
            Minimum           = 0,
            Maximum           = 64,
            Increment         = 1,
            Width             = 60,
            Foreground        = Brushes.White,
            Background        = new SolidColorBrush(Color.FromArgb(160, 60, 60, 60)),
            BorderBrush       = Brushes.Gray,
            ShowButtonSpinner = false,
            [!NumericUpDown.ValueProperty] = new Avalonia.Data.Binding(nameof(AccentEvery))
                { Mode = Avalonia.Data.BindingMode.TwoWay }
        };
        accentRow.Children.Add(accentBox);
        accentRow.Children.Add(MakeAdjustButton("+", () => AccentEvery = Math.Min(64, AccentEvery + 1)));

        var accentHint = new TextBlock
        {
            Text              = "(0=none)",
            Foreground        = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            FontSize          = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        accentRow.Children.Add(accentHint);
        root.Children.Add(accentRow);

        // ── Controls row: Start/Stop + Mute ───────────────────────────────
        var ctrlRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            Spacing             = 10,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var startStopBtn = new Button
        {
            FontSize   = 14,
            Padding    = new Thickness(14, 5),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(220, 40, 110, 40)),
            [!Button.ContentProperty] = new Avalonia.Data.Binding(nameof(StartStopLabel))
        };
        startStopBtn.Click += ToggleStartStop;
        ctrlRow.Children.Add(startStopBtn);

        var muteBox = new CheckBox
        {
            Content           = "🔇 Mute",
            Foreground        = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            [!CheckBox.IsCheckedProperty] = new Avalonia.Data.Binding(nameof(MuteAudio))
                { Mode = Avalonia.Data.BindingMode.TwoWay }
        };
        ctrlRow.Children.Add(muteBox);
        root.Children.Add(ctrlRow);

        panel.Child = root;
        Content     = panel;

        // ── Drag: works for mouse, touch, and stylus ──────────────────────
        // Attach to the whole panel; non-interactive sources start a drag.
        panel.PointerPressed  += OnDragPressed;
        panel.PointerMoved    += OnDragMoved;
        panel.PointerReleased += OnDragReleased;
        panel.PointerCaptureLost += (_, _) => _isDragging = false;
    }

    // ── Drag handlers (mouse / touch / stylus) ────────────────────────────
    private bool IsDraggableSource(object? source) =>
        source is Border or StackPanel or TextBlock or Grid;

    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        bool isTouch = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
        if (!isTouch && !IsDraggableSource(e.Source)) return;

        _isDragging   = true;
        _dragPointer  = e.Pointer;
        _dragStart    = e.GetPosition(this);  // position relative to window
        e.Pointer.Capture(sender as IInputElement);
        e.Handled = true;
    }

    private void OnDragMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || e.Pointer != _dragPointer) return;

        var current = e.GetPosition(this);
        var delta   = current - _dragStart;
        Position    = new PixelPoint(
            Position.X + (int)delta.X,
            Position.Y + (int)delta.Y);
        e.Handled = true;
    }

    private void OnDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging || e.Pointer != _dragPointer) return;
        _isDragging  = false;
        _dragPointer = null;
        e.Pointer.Capture(null);
        SaveSettings();
        e.Handled = true;
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text              = text,
        Foreground        = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
        VerticalAlignment = VerticalAlignment.Center,
        FontSize          = 12
    };

    private static Button MakeAdjustButton(string label, Action onClick)
    {
        var btn = new Button
        {
            Content     = label,
            Padding     = new Thickness(5, 2),
            Foreground  = Brushes.White,
            Background  = new SolidColorBrush(Color.FromArgb(160, 70, 70, 70)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 150, 150, 150)),
            FontSize    = 12
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static Button MakeIconButton(string icon, string tooltip)
    {
        return new Button
        {
            Content         = icon,
            Padding         = new Thickness(5, 1),
            Foreground      = Brushes.LightGray,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize        = 13,
            [ToolTip.TipProperty] = tooltip
        };
    }

    // ── Beat handler ──────────────────────────────────────────────────────
    private void OnBeat(object? sender, BeatEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            BeatBrush     = e.IsAccent ? AccentBrush : NormalBrush;
            BeatCountText = this.AccentEvery == 0 ?  (e.TotalBeats + 1).ToString() : (e.TotalBeats % this.AccentEvery + 1).ToString();
            _fadeTimer.Stop();
            _fadeTimer.Start();
        });
    }

    // ── Toggle Start / Stop ───────────────────────────────────────────────
    private void ToggleStartStop(object? sender, RoutedEventArgs e)
    {
        if (_metronome.IsRunning)
        {
            _metronome.Stop();
            BeatBrush     = IdleBrush;
            BeatCountText = "–";
            _fadeTimer.Stop();
        }
        else
        {
            BeatCountText = "–";
            _metronome.Start();
        }
        IsRunning = _metronome.IsRunning;
    }

    // ── Persistence ────────────────────────────────────────────────────────
    private void SaveSettings()
    {
        var s         = AppSettings.Instance.Metronome;
        s.Tempo       = _tempo;
        s.AccentEvery = _accentEvery;
        s.MuteAudio   = _muteAudio;
        s.WindowLeft  = Position.X;
        s.WindowTop   = Position.Y;
        AppSettings.Instance.SaveLocal();
    }

    private void RestorePosition(MetronomeSettings s)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = s.WindowLeft >= 0 && s.WindowTop >= 0
            ? new PixelPoint((int)s.WindowLeft, (int)s.WindowTop)
            : new PixelPoint(100, 100);
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────
    protected void OnPC([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
