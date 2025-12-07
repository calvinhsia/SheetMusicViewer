using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using System;
using System.Threading.Tasks;

namespace AvaloniaSimpleApp;

// Test app for headless testing
public class TestHeadlessApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}

// Custom App that shows PdfViewerWindow instead of MainWindow
public class PdfViewerApp : Application
{
    public static Action<PdfViewerApp, IClassicDesktopStyleApplicationLifetime> OnSetupWindow;

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
            OnSetupWindow?.Invoke(this, desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

// Test app for PDF viewer testing
public class TestPdfViewerApp : Application
{
    public static Func<Application, IClassicDesktopStyleApplicationLifetime, Task>? OnSetupWindow;
    
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
                // Fire and forget - the async setup will handle its own completion
                _ = OnSetupWindow.Invoke(this, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

// Test app for BrowseList dialog
public class TestBrowseListApp : Application
{
    public static Func<Application, IClassicDesktopStyleApplicationLifetime, Task>? OnSetupWindow;
    
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
