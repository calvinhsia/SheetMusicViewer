using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using PDFtoImage;
using SheetMusicLib;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaTests;

/// <summary>
/// ChooseMusic-style window with book cover bitmaps from actual PDFs.
/// Reusable control that can be used in tests or as part of the application.
/// </summary>
public class ChooseMusicWindow : Window
{
    private TabControl _tabControl;
    private ListBox _lbBooks;
    private TextBlock _tbxTotals;
    private ComboBox _cboRootFolder;
    private TextBox _tbxFilter;
    
    private List<PdfMetaDataReadResult> _pdfMetadata;
    private string _rootFolder;

    private const int ThumbnailWidth = 150;
    private const int ThumbnailHeight = 225;

    /// <summary>
    /// If true, skip cloud-only files instead of triggering download for thumbnails.
    /// Set to false to allow on-demand downloading of cloud files.
    /// </summary>
    public bool SkipCloudOnlyFiles { get; set; } = true;

    public ChooseMusicWindow() : this(null, null)
    {
    }

    public ChooseMusicWindow(List<PdfMetaDataReadResult> pdfMetadata, string rootFolder)
    {
        _pdfMetadata = pdfMetadata;
        _rootFolder = rootFolder;
        
        Title = "Choose Music - Avalonia Test";
        Width = 1200;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        
        BuildUI();
        
        // Generate books after window is loaded
        this.Opened += async (s, e) =>
        {
            if (_pdfMetadata != null && _pdfMetadata.Count > 0)
            {
                await FillBooksTabWithRealDataAsync();
            }
            else
            {
                await FillBooksTabAsync();
            }
        };
    }

    private void BuildUI()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        
        // Tab control for Books, Favorites, Query, Playlists
        _tabControl = new TabControl();
        Grid.SetRow(_tabControl, 0);
        Grid.SetRowSpan(_tabControl, 2);
        
        // Books tab
        var booksTab = new TabItem { Header = "_Books" };
        var booksGrid = new Grid();
        booksGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        booksGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        
        // Filter bar
        var filterPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        filterPanel.Children.Add(new RadioButton { Content = "ByDate", GroupName = "Sort", IsChecked = true, Margin = new Thickness(20, 5, 0, 0) });
        filterPanel.Children.Add(new RadioButton { Content = "ByFolder", GroupName = "Sort", Margin = new Thickness(20, 5, 0, 0) });
        filterPanel.Children.Add(new RadioButton { Content = "ByNumPages", GroupName = "Sort", Margin = new Thickness(20, 5, 0, 0) });
        filterPanel.Children.Add(new Label { Content = "Filter", Margin = new Thickness(20, 0, 0, 0) });
        _tbxFilter = new TextBox { Width = 150, Margin = new Thickness(5, 0, 0, 0) };
        filterPanel.Children.Add(_tbxFilter);
        Grid.SetRow(filterPanel, 0);
        booksGrid.Children.Add(filterPanel);
        
        // Books list with wrap panel
        _lbBooks = new ListBox();
        
        // Create a WrapPanel as the items panel
        var wrapPanelFactory = new FuncTemplate<Panel?>(() => new WrapPanel
        {
            Orientation = Orientation.Horizontal
        });
        _lbBooks.ItemsPanel = wrapPanelFactory;
        
        var scrollViewer = new ScrollViewer 
        { 
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _lbBooks
        };
        
        Grid.SetRow(scrollViewer, 1);
        booksGrid.Children.Add(scrollViewer);
        
        booksTab.Content = booksGrid;
        _tabControl.Items.Add(booksTab);
        
        // Favorites tab (placeholder)
        var favTab = new TabItem { Header = "Fa_vorites", Content = new TextBlock { Text = "Favorites go here", Margin = new Thickness(20) } };
        _tabControl.Items.Add(favTab);
        
        // Query tab (placeholder)
        var queryTab = new TabItem { Header = "_Query", Content = new TextBlock { Text = "Query goes here", Margin = new Thickness(20) } };
        _tabControl.Items.Add(queryTab);
        
        // Playlists tab (placeholder)
        var playlistsTab = new TabItem { Header = "_Playlists", Content = new TextBlock { Text = "Playlists go here", Margin = new Thickness(20) } };
        _tabControl.Items.Add(playlistsTab);
        
        grid.Children.Add(_tabControl);
        
        // Top bar with totals and controls
        var topBar = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 5, 10, 5)
        };
        Grid.SetRow(topBar, 0);
        
        _tbxTotals = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        topBar.Children.Add(_tbxTotals);
        
        topBar.Children.Add(new Label { Content = "Music Folder Path:", Margin = new Thickness(20, 0, 0, 0) });
        _cboRootFolder = new ComboBox { Width = 300, Margin = new Thickness(10, 0, 10, 0) };
        _cboRootFolder.Items.Add("C:\\Users\\Music\\SheetMusic");
        _cboRootFolder.Items.Add("D:\\Music\\PDFs");
        _cboRootFolder.SelectedIndex = 0;
        topBar.Children.Add(_cboRootFolder);
        
        var btnCancel = new Button { Content = "Cancel", Margin = new Thickness(10, 0, 0, 0) };
        btnCancel.Click += (s, e) => Close();
        topBar.Children.Add(btnCancel);
        
        var btnOk = new Button { Content = "_OK", Width = 50, Margin = new Thickness(10, 0, 10, 0) };
        btnOk.Click += (s, e) => Close();
        topBar.Children.Add(btnOk);
        
        grid.Children.Add(topBar);
        
        Content = grid;
    }

    private async Task FillBooksTabWithRealDataAsync()
    {
        var random = new Random(42); // Fixed seed for consistent fallback colors
        var items = new List<Control>();
        
        int index = 0;
        int totalSongs = 0;
        int totalPages = 0;
        int totalFavs = 0;
        
        foreach (var pdfMetaData in _pdfMetadata)
        {
            var bookName = pdfMetaData.GetBookName(_rootFolder);
            var numSongs = pdfMetaData.TocEntries.Count;
            var numPages = pdfMetaData.VolumeInfoList.Sum(v => v.NPagesInThisVolume);
            var numFavs = pdfMetaData.Favorites.Count;
            
            totalSongs += numSongs;
            totalPages += numPages;
            totalFavs += numFavs;
            
            // Try to get actual PDF thumbnail, fall back to generated bitmap
            Bitmap bitmap;
            try
            {
                bitmap = await GetPdfThumbnailAsync(pdfMetaData);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to get PDF thumbnail for {bookName}: {ex.Message}");
                bitmap = GenerateBookCoverBitmap(200, 240, random, bookName, index);
            }
            
            // Create the book item UI
            var sp = new StackPanel { Orientation = Orientation.Vertical, Width = 150, Margin = new Thickness(5) };
            
            var img = new Image
            {
                Source = bitmap,
                Width = 140,
                Height = 200,
                Stretch = Stretch.UniformToFill
            };
            sp.Children.Add(img);
            
            sp.Children.Add(new TextBlock
            {
                Text = bookName,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 140,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 11
            });
            
            sp.Children.Add(new TextBlock
            {
                Text = $"#Sngs={numSongs} Pg={numPages} Fav={numFavs}",
                FontSize = 10,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            });
            
            items.Add(sp);
            
            // Add incrementally to show loading progress
            if (index % 10 == 9)
            {
                _lbBooks.ItemsSource = null;
                _lbBooks.ItemsSource = new List<Control>(items);
                _tbxTotals.Text = $"Total #Books = {items.Count} # Songs = {totalSongs:n0} # Pages = {totalPages:n0} #Fav={totalFavs:n0}";
                await Task.Delay(10); // Small delay to show progressive loading
            }
            
            index++;
        }
        
        // Final update
        _lbBooks.ItemsSource = items;
        _tbxTotals.Text = $"Total #Books = {items.Count} # Songs = {totalSongs:n0} # Pages = {totalPages:n0} #Fav={totalFavs:n0}";
    }

    /// <summary>
    /// Get a thumbnail bitmap from the first page of the PDF, similar to WPF's GetBitmapImageThumbnailAsync
    /// </summary>
    private async Task<Bitmap> GetPdfThumbnailAsync(PdfMetaDataReadResult pdfMetaData)
    {
        return await Task.Run(() =>
        {
            // Check if we have any volumes
            if (pdfMetaData.VolumeInfoList == null || pdfMetaData.VolumeInfoList.Count == 0)
            {
                throw new InvalidOperationException($"No volumes in metadata for: {pdfMetaData.FullPathFile}");
            }
            
            var firstVolume = pdfMetaData.VolumeInfoList[0];
            if (string.IsNullOrEmpty(firstVolume.FileNameVolume))
            {
                throw new InvalidOperationException($"Empty FileNameVolume for: {pdfMetaData.FullPathFile}");
            }
            
            // Get the path to the first volume PDF
            var pdfPath = pdfMetaData.GetFullPathFileFromVolno(0);
            
            if (string.IsNullOrEmpty(pdfPath))
            {
                throw new InvalidOperationException($"GetFullPathFileFromVolno returned empty for: {pdfMetaData.FullPathFile}");
            }
            
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException($"PDF file not found: {pdfPath}");
            }
            
            // Check for cloud-only files
            if (SkipCloudOnlyFiles)
            {
                var fileInfo = new FileInfo(pdfPath);
                const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
                const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
                
                var attrs = fileInfo.Attributes;
                bool isCloudOnly = (attrs & RecallOnDataAccess) == RecallOnDataAccess ||
                                   (attrs & RecallOnOpen) == RecallOnOpen ||
                                   (attrs & FileAttributes.Offline) == FileAttributes.Offline;
                
                // Small reparse point file is likely a cloud placeholder
                if (!isCloudOnly && (attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint && fileInfo.Length < 1024)
                {
                    isCloudOnly = true;
                }
                
                if (isCloudOnly)
                {
                    throw new IOException($"Cloud-only file, skipping to avoid download: {pdfPath}");
                }
            }
            
            // Use stream like MainWindow does - this avoids PDFtoImage string parsing issues
            using var pdfStream = File.OpenRead(pdfPath);
            using var skBitmap = Conversion.ToImage(pdfStream, page: 0, options: new PDFtoImage.RenderOptions(Width: ThumbnailWidth, Height: ThumbnailHeight));
            
            // Convert SKBitmap to Avalonia Bitmap
            using var data = skBitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Seek(0, SeekOrigin.Begin);
            
            return new Bitmap(stream);
        });
    }

    private async Task FillBooksTabAsync()
    {
        var random = new Random(42); // Fixed seed for consistent colors
        var items = new List<Control>();
        
        // Generate 50 book items with colorful bitmaps
        var bookNames = new[]
        {
            "Classical Piano Vol 1", "Jazz Standards", "Pop Hits 2020", "Rock Classics",
            "Broadway Favorites", "Country Gold", "Blues Collection", "Folk Songs",
            "Movie Themes", "Video Game Music", "Christmas Carols", "Gospel Hymns",
            "Opera Arias", "Chamber Music", "Symphonies", "Concertos",
            "Sonatas", "Etudes", "Preludes", "Fugues",
            "Nocturnes", "Waltzes", "Mazurkas", "Ballades",
            "Impromptus", "Scherzos", "Polonaises", "Rhapsodies",
            "Variations", "Suites", "Partitas", "Inventions",
            "Toccatas", "Fantasias", "Rondos", "Minuets",
            "Gavottes", "Bourrees", "Sarabandes", "Gigues",
            "Courantes", "Allemandes", "Passacaglias", "Chaconnes",
            "Marches", "Serenades", "Divertimentos", "Overtures",
            "Interludes", "Bagatelles"
        };
        
        for (int i = 0; i < 50; i++)
        {
            var bookName = bookNames[i % bookNames.Length];
            if (i >= bookNames.Length)
            {
                bookName += $" Vol {i / bookNames.Length + 1}";
            }
            
            // Create a colorful bitmap for this book
            var bitmap = GenerateBookCoverBitmap(200, 240, random, bookName, i);
            
            // Create the book item UI
            var sp = new StackPanel { Orientation = Orientation.Vertical, Width = 150, Margin = new Thickness(5) };
            
            var img = new Image
            {
                Source = bitmap,
                Width = 140,
                Height = 200,
                Stretch = Stretch.UniformToFill
            };
            sp.Children.Add(img);
            
            sp.Children.Add(new TextBlock
            {
                Text = bookName,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 140,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 11
            });
            
            var numSongs = random.Next(10, 100);
            var numPages = random.Next(20, 500);
            var numFavs = random.Next(0, 20);
            
            sp.Children.Add(new TextBlock
            {
                Text = $"#Sngs={numSongs} Pg={numPages} Fav={numFavs}",
                FontSize = 10,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            });
            
            items.Add(sp);
            
            // Add incrementally to show loading progress
            if (i % 5 == 4)
            {
                _lbBooks.ItemsSource = null;
                _lbBooks.ItemsSource = new List<Control>(items);
                _tbxTotals.Text = $"Total #Books = {items.Count} # Songs = {items.Count * 50} # Pages = {items.Count * 150} #Fav={items.Count * 5}";
                await Task.Delay(10); // Small delay to show progressive loading
            }
        }
        
        // Final update
        _lbBooks.ItemsSource = items;
        _tbxTotals.Text = $"Total #Books = {items.Count} # Songs = {items.Count * 50:n0} # Pages = {items.Count * 150:n0} #Fav={items.Count * 5:n0}";
    }

    private Bitmap GenerateBookCoverBitmap(int width, int height, Random random, string title, int index)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        
        // Generate a nice gradient background
        var color1 = SKColor.FromHsv(random.Next(360), random.Next(60, 100), random.Next(70, 100));
        var color2 = SKColor.FromHsv((random.Next(360) + 180) % 360, random.Next(60, 100), random.Next(40, 70));
        
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            new[] { color1, color2 },
            SKShaderTileMode.Clamp);
        
        using var paint = new SKPaint
        {
            Shader = shader,
            IsAntialias = true
        };
        canvas.DrawRect(0, 0, width, height, paint);
        
        // Add some decorative elements
        using var accentPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(100),
            IsAntialias = true,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke
        };
        
        // Draw some circles or rectangles as decoration
        var shapeType = index % 4;
        switch (shapeType)
        {
            case 0:
                canvas.DrawCircle(width / 2, height / 3, 30, accentPaint);
                break;
            case 1:
                canvas.DrawRect(width / 4, height / 4, width / 2, height / 2, accentPaint);
                break;
            case 2:
                for (int i = 0; i < 3; i++)
                {
                    canvas.DrawLine(10, height / 4 + i * 20, width - 10, height / 4 + i * 20, accentPaint);
                }
                break;
            case 3:
                canvas.DrawOval(new SKRect(width / 4, height / 3, width * 3 / 4, height * 2 / 3), accentPaint);
                break;
        }
        
        // Add title text at bottom with word wrapping
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 14,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            TextAlign = SKTextAlign.Center
        };
        
        // Add shadow for text
        using var shadowPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(150),
            IsAntialias = true,
            TextSize = 14,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            TextAlign = SKTextAlign.Center
        };
        
        var yPos = height - 30;
        
        // Word wrap the title
        var words = title.Split(' ');
        var currentLine = "";
        foreach (var word in words)
        {
            var testLine = currentLine.Length > 0 ? currentLine + " " + word : word;
            var lineWidth = textPaint.MeasureText(testLine);
            
            if (lineWidth > width - 20 && currentLine.Length > 0)
            {
                canvas.DrawText(currentLine, width / 2 + 1, yPos + 1, shadowPaint);
                canvas.DrawText(currentLine, width / 2, yPos, textPaint);
                currentLine = word;
                yPos += 18;
            }
            else
            {
                currentLine = testLine;
            }
        }
        
        if (currentLine.Length > 0)
        {
            canvas.DrawText(currentLine, width / 2 + 1, yPos + 1, shadowPaint);
            canvas.DrawText(currentLine, width / 2, yPos, textPaint);
        }
        
        // Convert to Avalonia bitmap
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        
        return new Bitmap(stream);
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
