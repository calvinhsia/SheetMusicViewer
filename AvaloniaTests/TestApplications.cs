using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using System;
using System.Threading.Tasks;

namespace AvaloniaTests;

public class TestApp : Application
{
    public static Func<Application, IClassicDesktopStyleApplicationLifetime, Task>? OnSetupWindow;
    public static Action<Application>? OnInitialize;
    
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        OnInitialize?.Invoke(this);
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

/// <summary>
/// Test application for headless testing scenarios
/// </summary>
public class TestHeadlessApp : Application
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

/// <summary>
/// Test application for PDF viewer testing scenarios
/// </summary>
public class PdfViewerApp : Application
{
    public static Action<Application, IClassicDesktopStyleApplicationLifetime>? OnSetupWindow;
    
    public override void Initialize()
    {
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

/// <summary>
/// Test application specifically for PDF viewer window integration tests
/// </summary>
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
                _ = OnSetupWindow.Invoke(this, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
