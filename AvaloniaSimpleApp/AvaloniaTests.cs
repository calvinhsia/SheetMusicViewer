using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace AvaloniaSimpleApp;

[TestClass]
public class AvaloniaTests
{
    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestAvaloniaPdfStressTest()
    {
        await Task.Run(() =>
        {
            // Build and start the Avalonia application with stress test window
            var app = Program.BuildAvaloniaApp();
            app.StartWithClassicDesktopLifetime(new string[0]);
        });
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestAvaloniaPdfViewerUI()
    {
        await Task.Run(() =>
        {
            // Build and start the Avalonia application with PdfViewerWindow
            var app = BuildAvaloniaAppForPdfViewer();
            app.StartWithClassicDesktopLifetime(new string[0]);
        });
    }

    private static AppBuilder BuildAvaloniaAppForPdfViewer()
        => AppBuilder.Configure<PdfViewerApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

// Custom App that shows PdfViewerWindow instead of MainWindow
public class PdfViewerApp : Avalonia.Application
{
    public override void Initialize()
    {
        // Manually add FluentTheme instead of loading XAML
        // This avoids the "No precompiled XAML found" error
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new PdfViewerWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}