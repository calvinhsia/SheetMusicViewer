using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using System;
using System.Threading.Tasks;

namespace AvaloniaTests;

// Generic test application that can be configured for different scenarios
public class TestApp : Application
{
    public static Func<Application, IClassicDesktopStyleApplicationLifetime, Task>? OnSetupWindow;
    public static Action<Application>? OnInitialize;
    
    public override void Initialize()
    {
        // Default: Add Fluent theme
        Styles.Add(new FluentTheme());
        
        // Allow custom initialization (e.g., adding DataGrid styles)
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

// Helper class to configure common scenarios
public static class TestAppConfigurations
{
    /// <summary>
    /// Configure TestApp for DataGrid testing (adds DataGrid styles)
    /// </summary>
    public static void ConfigureForDataGrid()
    {
        TestApp.OnInitialize = app =>
        {
            var dataGridStyles = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Avalonia.Controls.DataGrid/"))
            {
                Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
            };
            app.Styles.Add(dataGridStyles);
        };
    }
    
    /// <summary>
    /// Reset TestApp to default configuration (Fluent theme only)
    /// </summary>
    public static void ConfigureDefault()
    {
        TestApp.OnInitialize = null;
    }
}
