using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaTests;

public static class AvaloniaTestHelper
{
    public static async Task RunAvaloniaTest(
        Func<IClassicDesktopStyleApplicationLifetime, TaskCompletionSource<bool>, Task> setupWindow,
        Action<Application>? configureApp = null,
        int timeoutMs = 40000)
    {
        var testCompleted = new TaskCompletionSource<bool>();
        var uiThread = new Thread(() =>
        {
            try
            {
                TestApp.OnInitialize = configureApp;
                AppBuilder.Configure<TestApp>()
                    .UsePlatformDetect()
                    .WithInterFont()
                    .LogToTrace()
                    .StartWithClassicDesktopLifetime(Array.Empty<string>());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"UI thread error: {ex.Message}");
                testCompleted.TrySetException(ex);
            }
        })
        { 
            Name = "AvaloniaTestUIThread",
        };

        TestApp.OnSetupWindow = async (app, lifetime) =>
        {
            try
            {
                await setupWindow(lifetime, testCompleted);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error: {ex}");
                testCompleted.SetException(ex);
                lifetime.Shutdown();
            }
        };

        if (OperatingSystem.IsWindows())
        {
            uiThread.SetApartmentState(ApartmentState.STA);
        }
        uiThread.Start();

        var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(timeoutMs));
        if (completedTask != testCompleted.Task)
        {
            Trace.WriteLine($"Test timed out after {timeoutMs}ms — closing window automatically");
            // Signal completion so the test doesn't block forever, then shut down the UI.
            testCompleted.TrySetResult(false);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
                        lt.Shutdown();
                }
                catch { }
            });
        }

        await testCompleted.Task;
        uiThread.Join(5000);
    }

    public static EventHandler CreateWindowClosedHandler(
        TaskCompletionSource<bool> testCompleted,
        IClassicDesktopStyleApplicationLifetime lifetime,
        string? closedMessage = null)
    {
        return (s, e) =>
        {
            if (!string.IsNullOrEmpty(closedMessage))
            {
                Trace.WriteLine(closedMessage);
            }
            testCompleted.TrySetResult(true);
            lifetime.Shutdown();
        };
    }
}
