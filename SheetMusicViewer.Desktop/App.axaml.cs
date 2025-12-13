using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SheetMusicLib;

namespace SheetMusicViewer.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Initialize cross-platform settings
        AppSettings.Initialize("SheetMusicViewer");
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new PdfViewerWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
