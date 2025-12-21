using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SheetMusicLib;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Options dialog for configuring application settings.
/// Displays only user-configurable options from AppSettings.UserOptions.
/// </summary>
public class OptionsWindow : Window
{
    private CheckBox _chkSkipCloudOnlyFiles = null!;
    private NumericUpDown _nudDoubleTapTime = null!;
    private NumericUpDown _nudDoubleTapDistance = null!;
    private NumericUpDown _nudPageCacheSize = null!;
    private NumericUpDown _nudThumbnailParallelism = null!;
    private NumericUpDown _nudRenderDpi = null!;
    private NumericUpDown _nudThumbnailWidth = null!;
    private NumericUpDown _nudThumbnailHeight = null!;
    
    public OptionsWindow()
    {
        Title = "Options";
        Width = 500;
        Height = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;
        MinWidth = 400;
        MinHeight = 500;
        
        BuildUI();
        
        KeyDown += OnKeyDown;
    }
    
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
    
    private void BuildUI()
    {
        var settings = AppSettings.Instance.UserOptions;
        
        var mainGrid = new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(1, GridUnitType.Star)),
                new RowDefinition(GridLength.Auto)
            }
        };
        
        // Title
        var titleBlock = new TextBlock
        {
            Text = "Application Options",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 15)
        };
        Grid.SetRow(titleBlock, 0);
        mainGrid.Children.Add(titleBlock);
        
        // Scrollable options panel
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Grid.SetRow(scrollViewer, 1);
        
        var optionsPanel = new StackPanel { Spacing = 20 };
        scrollViewer.Content = optionsPanel;
        mainGrid.Children.Add(scrollViewer);
        
        // === Cloud/Performance Section ===
        optionsPanel.Children.Add(CreateSectionHeader("Cloud Storage & Performance"));
        
        _chkSkipCloudOnlyFiles = new CheckBox
        {
            IsChecked = settings.SkipCloudOnlyFiles
        };
        var chkContent = new StackPanel { Spacing = 2 };
        chkContent.Children.Add(new TextBlock { Text = "Skip cloud-only files (OneDrive, etc.)" });
        chkContent.Children.Add(CreateHelpText("Improves performance by not triggering downloads for cloud-only PDFs."));
        _chkSkipCloudOnlyFiles.Content = chkContent;
        optionsPanel.Children.Add(_chkSkipCloudOnlyFiles);
        
        // === Double-Tap Detection Section ===
        optionsPanel.Children.Add(CreateSectionHeader("Double-Tap Detection"));
        
        optionsPanel.Children.Add(CreateNumericOption(
            "Time threshold (ms):",
            settings.DoubleTapTimeThresholdMs,
            100, 1000, 50,
            "Maximum time between taps to count as double-tap",
            out _nudDoubleTapTime));
        
        optionsPanel.Children.Add(CreateNumericOption(
            "Distance threshold (pixels):",
            settings.DoubleTapDistanceThreshold,
            10, 200, 10,
            "Maximum distance between taps to count as same location",
            out _nudDoubleTapDistance));
        
        // === Cache Settings Section ===
        optionsPanel.Children.Add(CreateSectionHeader("Cache Settings"));
        
        optionsPanel.Children.Add(CreateNumericOption(
            "Page cache size:",
            settings.PageCacheMaxSize,
            10, 200, 10,
            "Number of rendered pages to keep in memory",
            out _nudPageCacheSize));
        
        optionsPanel.Children.Add(CreateNumericOption(
            "Thumbnail loading threads:",
            settings.ThumbnailLoadingParallelism,
            1, 16, 1,
            "Number of parallel threads for loading thumbnails",
            out _nudThumbnailParallelism));
        
        // === Rendering Settings Section ===
        optionsPanel.Children.Add(CreateSectionHeader("Rendering"));
        
        optionsPanel.Children.Add(CreateNumericOption(
            "Render DPI:",
            settings.RenderDpi,
            72, 300, 25,
            "Higher values are sharper but slower (default: 150)",
            out _nudRenderDpi));
        
        optionsPanel.Children.Add(CreateNumericOption(
            "Thumbnail width (pixels):",
            settings.ThumbnailWidth,
            50, 300, 25,
            "Width of thumbnail images in chooser",
            out _nudThumbnailWidth));
        
        optionsPanel.Children.Add(CreateNumericOption(
            "Thumbnail height (pixels):",
            settings.ThumbnailHeight,
            75, 450, 25,
            "Height of thumbnail images in chooser",
            out _nudThumbnailHeight));
        
        // === Button Panel ===
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 15, 0, 0)
        };
        Grid.SetRow(buttonPanel, 2);
        
        var btnReset = new Button
        {
            Content = "Reset to Defaults",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        btnReset.Click += (s, e) => ResetToDefaults();
        buttonPanel.Children.Add(btnReset);
        
        // Spacer
        buttonPanel.Children.Add(new Border { Width = 50 });
        
        var btnCancel = new Button
        {
            Content = "Cancel",
            Width = 80
        };
        btnCancel.Click += (s, e) => Close();
        buttonPanel.Children.Add(btnCancel);
        
        var btnOk = new Button
        {
            Content = "OK",
            Width = 80
        };
        btnOk.Click += (s, e) =>
        {
            SaveSettings();
            Close();
        };
        buttonPanel.Children.Add(btnOk);
        
        mainGrid.Children.Add(buttonPanel);
        
        Content = mainGrid;
    }
    
    private static TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 10, 0, 5)
        };
    }
    
    private static TextBlock CreateHelpText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        };
    }
    
    private static Control CreateNumericOption(
        string label, 
        double value, 
        double min, 
        double max, 
        double increment,
        string helpText,
        out NumericUpDown numericUpDown)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
        
        var labelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        labelRow.Children.Add(new TextBlock 
        { 
            Text = label, 
            VerticalAlignment = VerticalAlignment.Center,
            Width = 180
        });
        
        numericUpDown = new NumericUpDown
        {
            Value = (decimal)value,
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            Increment = (decimal)increment,
            Width = 120,
            FormatString = "0"
        };
        labelRow.Children.Add(numericUpDown);
        
        panel.Children.Add(labelRow);
        panel.Children.Add(CreateHelpText(helpText));
        
        return panel;
    }
    
    private void ResetToDefaults()
    {
        var defaults = new AppSettings.UserOptionsSettings();
        
        _chkSkipCloudOnlyFiles.IsChecked = defaults.SkipCloudOnlyFiles;
        _nudDoubleTapTime.Value = defaults.DoubleTapTimeThresholdMs;
        _nudDoubleTapDistance.Value = (decimal)defaults.DoubleTapDistanceThreshold;
        _nudPageCacheSize.Value = defaults.PageCacheMaxSize;
        _nudThumbnailParallelism.Value = defaults.ThumbnailLoadingParallelism;
        _nudRenderDpi.Value = defaults.RenderDpi;
        _nudThumbnailWidth.Value = defaults.ThumbnailWidth;
        _nudThumbnailHeight.Value = defaults.ThumbnailHeight;
    }
    
    private void SaveSettings()
    {
        var settings = AppSettings.Instance.UserOptions;
        var defaults = new AppSettings.UserOptionsSettings();
        
        settings.SkipCloudOnlyFiles = _chkSkipCloudOnlyFiles.IsChecked == true;
        settings.DoubleTapTimeThresholdMs = (int)(_nudDoubleTapTime.Value ?? defaults.DoubleTapTimeThresholdMs);
        settings.DoubleTapDistanceThreshold = (double)(_nudDoubleTapDistance.Value ?? (decimal)defaults.DoubleTapDistanceThreshold);
        settings.PageCacheMaxSize = (int)(_nudPageCacheSize.Value ?? defaults.PageCacheMaxSize);
        settings.ThumbnailLoadingParallelism = (int)(_nudThumbnailParallelism.Value ?? defaults.ThumbnailLoadingParallelism);
        settings.RenderDpi = (int)(_nudRenderDpi.Value ?? defaults.RenderDpi);
        settings.ThumbnailWidth = (int)(_nudThumbnailWidth.Value ?? defaults.ThumbnailWidth);
        settings.ThumbnailHeight = (int)(_nudThumbnailHeight.Value ?? defaults.ThumbnailHeight);
        
        AppSettings.Instance.Save();
    }
}
