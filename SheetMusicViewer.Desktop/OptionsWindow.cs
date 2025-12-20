using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SheetMusicLib;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Options dialog for configuring application settings.
/// </summary>
public class OptionsWindow : Window
{
    private CheckBox _chkSkipCloudOnlyFiles = null!;
    
    public OptionsWindow()
    {
        Title = "Options";
        Width = 450;
        Height = 250;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        
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
        
        // Options panel
        var optionsPanel = new StackPanel
        {
            Spacing = 15
        };
        Grid.SetRow(optionsPanel, 1);
        mainGrid.Children.Add(optionsPanel);
        
        // Cloud/Performance section
        var cloudSection = new StackPanel { Spacing = 8 };
        
        var cloudHeader = new TextBlock
        {
            Text = "Cloud Storage & Performance",
            FontWeight = FontWeight.SemiBold,
            FontSize = 14
        };
        cloudSection.Children.Add(cloudHeader);
        
        var settings = AppSettings.Instance;
        
        _chkSkipCloudOnlyFiles = new CheckBox
        {
            IsChecked = settings.SkipCloudOnlyFiles
        };
        
        var chkContent = new StackPanel { Spacing = 2 };
        chkContent.Children.Add(new TextBlock
        {
            Text = "Skip cloud-only files (OneDrive, etc.)",
            FontWeight = FontWeight.Normal
        });
        chkContent.Children.Add(new TextBlock
        {
            Text = "Improves performance by not triggering downloads for cloud-only PDFs.\nPlaceholder thumbnails will be shown instead.",
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 350
        });
        _chkSkipCloudOnlyFiles.Content = chkContent;
        
        cloudSection.Children.Add(_chkSkipCloudOnlyFiles);
        optionsPanel.Children.Add(cloudSection);
        
        // Button panel
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 15, 0, 0)
        };
        Grid.SetRow(buttonPanel, 2);
        
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
    
    private void SaveSettings()
    {
        var settings = AppSettings.Instance;
        settings.SkipCloudOnlyFiles = _chkSkipCloudOnlyFiles.IsChecked == true;
        settings.Save();
    }
}
