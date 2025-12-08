using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

[TestClass]
[DoNotParallelize]
public class BrowseControlTests
{
    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestAvaloniaChooseMusicDialog()
    {
        if (Environment.GetEnvironmentVariable("CI") == "true" || 
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Assert.Inconclusive("Test skipped in headless CI environment - requires display");
            return;
        }

        var testCompleted = new TaskCompletionSource<bool>();
        var uiThread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<TestChooseMusicApp>()
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
        });

        TestChooseMusicApp.OnSetupWindow = async (app, lifetime) =>
        {
            try
            {
                var window = new ChooseMusicWindow();
                lifetime.MainWindow = window;
                window.Show();
                
                Trace.WriteLine($"? ChooseMusicWindow created and shown");
                Trace.WriteLine($"? Generating bitmaps for books...");

                await Task.Delay(1000);

                var timer = new System.Timers.Timer(30000);
                timer.Elapsed += (s, e) =>
                {
                    timer.Stop();
                    Dispatcher.UIThread.Post(() =>
                    {
                        Trace.WriteLine("Closing ChooseMusicWindow");
                        window?.Close();
                        testCompleted.SetResult(true);
                        lifetime.Shutdown();
                    });
                };
                timer.Start();
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

        var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(35000));
        if (completedTask != testCompleted.Task)
        {
            Trace.WriteLine("Test timed out - manually close the window to complete");
        }
        else
        {
            await testCompleted.Task;
        }

        uiThread.Join(2000);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestAvaloniaDataGridBrowseList()
    {
        if (Environment.GetEnvironmentVariable("CI") == "true" || 
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Assert.Inconclusive("Test skipped in headless CI environment - requires display");
            return;
        }

        var testCompleted = new TaskCompletionSource<bool>();
        var uiThread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<TestBrowseListApp>()
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
        });

        TestBrowseListApp.OnSetupWindow = async (app, lifetime) =>
        {
            try
            {
                var window = new BrowseListWindow();
                lifetime.MainWindow = window;
                
                window.Closed += (s, e) =>
                {
                    Trace.WriteLine("BrowseListWindow closed by user");
                    testCompleted.TrySetResult(true);
                    lifetime.Shutdown();
                };
                
                window.Show();
                
                Trace.WriteLine($"? BrowseListWindow created and shown");
                Trace.WriteLine($"? Loading types from Avalonia assemblies...");

                await Task.Delay(100);
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

        await testCompleted.Task;
        uiThread.Join(2000);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestBrowseControlComparison()
    {
        if (Environment.GetEnvironmentVariable("CI") == "true" || 
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Assert.Inconclusive("Test skipped in headless CI environment - requires display");
            return;
        }

        var testCompleted = new TaskCompletionSource<bool>();
        var uiThread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<TestHeadlessApp>()
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
        });

        if (OperatingSystem.IsWindows())
        {
            uiThread.SetApartmentState(ApartmentState.STA);
        }
        uiThread.Start();

        await Task.Delay(1000);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                Trace.WriteLine("=== BrowseControl Comparison Test ===");
                Trace.WriteLine("");
                
                var itemCount = 1000;
                var query = Enumerable.Range(0, itemCount)
                    .Select(i => new
                    {
                        Id = i,
                        Name = $"Item {i:D4}",
                        Category = $"Category {i % 10}",
                        Value = i * 1.5,
                        Description = $"This is test item number {i}"
                    });
                
                Trace.WriteLine($"Created query with {itemCount:n0} items");
                Trace.WriteLine("");
                
                Trace.WriteLine("TEST 1: BrowseControl (Manual Rendering)");
                Trace.WriteLine("------------------------------------------");
                
                var sw1 = Stopwatch.StartNew();
                var browseControl1 = new BrowseControl(query, colWidths: new[] { 100, 200, 150, 100, 300 });
                sw1.Stop();
                
                var window1 = new Window
                {
                    Title = "BrowseControl (Manual) - 1,000 items",
                    Width = 1000,
                    Height = 600,
                    Content = browseControl1
                };
                
                window1.Show();
                await Task.Delay(500);
                
                var memoryBefore1 = GC.GetTotalMemory(false);
                
                var panelField1 = browseControl1.ListView.GetType().GetField("_itemsPanel", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var panel1 = panelField1?.GetValue(browseControl1.ListView) as StackPanel;
                var panelChildCount1 = panel1?.Children.Count ?? 0;
                
                Trace.WriteLine($"Creation time: {sw1.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"ListView panel children: {panelChildCount1:n0}");
                Trace.WriteLine($"Memory footprint: ~{memoryBefore1 / 1024 / 1024:n0} MB");
                Trace.WriteLine("");
                
                Trace.WriteLine("TEST 2: ListBoxBrowseControl (Virtualized ListBox)");
                Trace.WriteLine("---------------------------------------------------");
                
                var sw2 = Stopwatch.StartNew();
                var browseControl2 = new ListBoxBrowseControl(query, colWidths: new[] { 100, 200, 150, 100, 300 });
                sw2.Stop();
                
                var window2 = new Window
                {
                    Title = "ListBoxBrowseControl (Virtualized) - 1,000 items",
                    Width = 1000,
                    Height = 600,
                    Content = browseControl2,
                    Position = new PixelPoint(window1.Position.X + 50, window1.Position.Y + 50)
                };
                
                window2.Show();
                await Task.Delay(500);
                
                var memoryBefore2 = GC.GetTotalMemory(false);
                
                var listBoxView = browseControl2.ListView;
                var listBoxField = listBoxView.GetType().GetField("_listBox", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var listBox = listBoxField?.GetValue(listBoxView) as ListBox;
                var presenter = listBox?.Presenter;
                var panel = presenter?.Panel;
                
                Trace.WriteLine($"Creation time: {sw2.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"ListBox panel type: {panel?.GetType().Name ?? "N/A"}");
                Trace.WriteLine($"ListBox panel children: {panel?.Children.Count.ToString("n0") ?? "N/A"}");
                Trace.WriteLine($"Memory footprint: ~{memoryBefore2 / 1024 / 1024:n0} MB");
                Trace.WriteLine("");
                
                Trace.WriteLine("=== COMPARISON ===");
                Trace.WriteLine($"Dataset: {itemCount:n0} items");
                Trace.WriteLine($"Manual: {itemCount:n0} rows, {sw1.ElapsedMilliseconds:n0} ms, ~{memoryBefore1 / 1024 / 1024:n0} MB");
                Trace.WriteLine($"ListBox: ~{panel?.Children.Count ?? 0} rows, {sw2.ElapsedMilliseconds:n0} ms, ~{memoryBefore2 / 1024 / 1024:n0} MB");
                
                if (panel?.Children.Count < itemCount * 0.1)
                {
                    Trace.WriteLine($"? VIRTUALIZATION WORKING! Only {panel.Children.Count:n0} / {itemCount:n0} items in visual tree");
                }
                
                Trace.WriteLine("");
                Trace.WriteLine("Windows will close in 30 seconds...");
                
                await Task.Delay(30000);
                
                window1.Close();
                window2.Close();
                testCompleted.SetResult(true);
                
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Test error: {ex}");
                testCompleted.SetException(ex);
                
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.Shutdown();
                }
            }
        });

        var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(40000));
        if (completedTask != testCompleted.Task)
        {
            Assert.Fail("Test timed out");
        }

        await testCompleted.Task;
        uiThread.Join(2000);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestListBoxBrowseControl()
    {
        if (Environment.GetEnvironmentVariable("CI") == "true" || 
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Assert.Inconclusive("Test skipped in headless CI environment - requires display");
            return;
        }

        var testCompleted = new TaskCompletionSource<bool>();
        var uiThread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<TestHeadlessApp>()
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
        });

        if (OperatingSystem.IsWindows())
        {
            uiThread.SetApartmentState(ApartmentState.STA);
        }
        uiThread.Start();

        await Task.Delay(1000);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                Trace.WriteLine("=== ListBoxBrowseControl with 10,000 Items ===");
                
                var itemCount = 10000;
                var query = Enumerable.Range(0, itemCount)
                    .Select(i => new
                    {
                        Id = i,
                        Name = $"Item {i:D5}",
                        Category = $"Category {i % 20}",
                        Type = $"Type {i % 5}",
                        Value = i * 2.5,
                        Status = i % 3 == 0 ? "Active" : i % 3 == 1 ? "Pending" : "Inactive",
                        Description = $"Description for test item number {i} with additional details"
                    });
                
                var sw = Stopwatch.StartNew();
                var browseControl = new ListBoxBrowseControl(query, colWidths: new[] { 80, 150, 120, 80, 100, 100, 350 });
                sw.Stop();
                
                var window = new Window
                {
                    Title = $"ListBoxBrowseControl - {itemCount:n0} Items (Virtualized)",
                    Width = 1200,
                    Height = 800,
                    Content = browseControl,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                
                window.Closed += (s, e) =>
                {
                    testCompleted.TrySetResult(true);
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                    {
                        lifetime.Shutdown();
                    }
                };
                
                window.Show();
                await Task.Delay(1000);
                
                var memoryMB = GC.GetTotalMemory(false) / 1024 / 1024;
                
                Trace.WriteLine($"? Creation time: {sw.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"? Memory: ~{memoryMB:n0} MB");
                Trace.WriteLine("Close the window when finished testing.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Test error: {ex}");
                testCompleted.SetException(ex);
                
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.Shutdown();
                }
            }
        });

        await testCompleted.Task;
        uiThread.Join(2000);
    }
}
