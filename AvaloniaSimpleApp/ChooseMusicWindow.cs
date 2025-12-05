using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using SkiaSharp;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AvaloniaSimpleApp;

/// <summary>
/// ChooseMusic-style window with generated book cover bitmaps.
/// Reusable control that can be used in tests or as part of the application.
/// </summary>
public class ChooseMusicWindow : Window
{
    public ChooseMusicWindow()
    {
        Title = "Choose Music - Avalonia Test (Generated Bitmaps)";
        Width = 1400;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        
        BuildUI();
    }

    private void BuildUI()
    {
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        
        var wrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 220,
            ItemHeight = 320,
            Margin = new Thickness(10)
        };
        
        // Generate 50 colorful book covers
        for (int i = 0; i < 50; i++)
        {
            var bookPanel = CreateBookCover(i);
            wrapPanel.Children.Add(bookPanel);
        }
        
        scrollViewer.Content = wrapPanel;
        Content = scrollViewer;
    }

    private Panel CreateBookCover(int bookIndex)
    {
        var panel = new StackPanel
        {
            Width = 200,
            Height = 300,
            Margin = new Thickness(10)
        };
        
        // Generate a colorful bitmap for the book cover
        var bitmap = GenerateBookCoverBitmap(bookIndex);
        
        var image = new Image
        {
            Source = bitmap,
            Width = 200,
            Height = 260,
            Stretch = Stretch.UniformToFill
        };
        
        var titleText = new TextBlock
        {
            Text = $"Book {bookIndex + 1}",
            FontSize = 14,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(5)
        };
        
        panel.Children.Add(image);
        panel.Children.Add(titleText);
        
        return panel;
    }

    private Avalonia.Media.Imaging.Bitmap GenerateBookCoverBitmap(int index)
    {
        const int width = 200;
        const int height = 260;
        
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        
        // Create a gradient background based on index
        var hue = (index * 37) % 360; // Different hue for each book
        var color1 = SKColor.FromHsv(hue, 80, 90);
        var color2 = SKColor.FromHsv((hue + 60) % 360, 70, 70);
        
        var gradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            new[] { color1, color2 },
            SKShaderTileMode.Clamp);
        
        var paint = new SKPaint
        {
            Shader = gradient,
            IsAntialias = true
        };
        
        canvas.DrawRect(0, 0, width, height, paint);
        
        // Add decorative elements
        var random = new Random(index);
        
        // Add some circles
        for (int i = 0; i < 5; i++)
        {
            var circlePaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)random.Next(30, 80)),
                IsAntialias = true
            };
            
            canvas.DrawCircle(
                random.Next(width),
                random.Next(height),
                random.Next(20, 60),
                circlePaint);
        }
        
        // Add book title
        var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 24,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };
        
        canvas.DrawText($"Book {index + 1}", width / 2, height / 2, textPaint);
        
        // Convert to Avalonia Bitmap
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        
        return new Avalonia.Media.Imaging.Bitmap(stream);
    }
}

/// <summary>
/// Test application for ChooseMusic dialog.
/// Used in tests to host the ChooseMusicWindow.
/// </summary>
public class TestChooseMusicApp : Avalonia.Application
{
    public static Func<Avalonia.Application, IClassicDesktopStyleApplicationLifetime, Task>? OnSetupWindow;
    
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
    
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OnSetupWindow != null)
            {
                _ = OnSetupWindow.Invoke(this, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
