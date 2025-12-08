using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Diagnostics;

namespace AvaloniaTests.Tests;

/// <summary>
/// Base application class for tests, headless mode
/// </summary>
public class TestHeadlessApp : Application
{
    public override void Initialize()
    {
        try
        {
            Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error initializing theme: {ex.Message}");
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // For headless tests, don't create a main window
        }
        
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// PdfViewer application for tests with window setup callback
/// </summary>
public class PdfViewerApp : Application
{
    public static Action<Application, IClassicDesktopStyleApplicationLifetime>? OnSetupWindow { get; set; }
    
    public override void Initialize()
    {
        try
        {
            Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error initializing theme: {ex.Message}");
        }
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
/// TestPdfViewerApp for integration tests with window setup callback
/// </summary>
public class TestPdfViewerApp : Application
{
    public static Action<Application, IClassicDesktopStyleApplicationLifetime>? OnSetupWindow { get; set; }
    
    public override void Initialize()
    {
        try
        {
            Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error initializing theme: {ex.Message}");
        }
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
