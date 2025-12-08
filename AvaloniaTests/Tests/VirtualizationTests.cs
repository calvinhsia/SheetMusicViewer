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
public class VirtualizationTests
{
    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestItemContainerGeneratorDirect()
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
                Trace.WriteLine("=== ItemContainerGenerator Diagnostic Test ===");
                Trace.WriteLine("");
                
                var items = new[] { "Item 1", "Item 2", "Item 3", "Item 4", "Item 5" };
                
                Trace.WriteLine("TEST 1: ItemsControl without ItemTemplate");
                Trace.WriteLine("------------------------------------------");
                var itemsControl = new ItemsControl
                {
                    Width = 400,
                    Height = 300,
                    ItemsSource = items
                };
                
                var window = new Window
                {
                    Title = "ItemContainerGenerator Test",
                    Width = 500,
                    Height = 400,
                    Content = itemsControl
                };
                window.Show();
                
                await Task.Delay(1000);
                
                var generator = itemsControl.ItemContainerGenerator;
                Trace.WriteLine($"? Generator exists: {generator != null}");
                Trace.WriteLine($"? Items count: {items.Length}");
                Trace.WriteLine("");
                
                Trace.WriteLine("Attempting to retrieve containers from generator:");
                for (int i = 0; i < items.Length; i++)
                {
                    var container = generator.ContainerFromIndex(i);
                    Trace.WriteLine($"  Index {i}: Container={container?.GetType().Name ?? "NULL"}");
                }
                Trace.WriteLine("");
                
                Trace.WriteLine("Checking visual tree:");
                var presenter = itemsControl.Presenter;
                Trace.WriteLine($"  Presenter exists: {presenter != null}");
                
                if (presenter != null)
                {
                    var panel = presenter.Panel;
                    Trace.WriteLine($"  Panel exists: {panel != null}");
                    Trace.WriteLine($"  Panel type: {panel?.GetType().Name ?? "NULL"}");
                    Trace.WriteLine($"  Panel children count: {panel?.Children.Count ?? 0}");
                    
                    if (panel != null && panel.Children.Count > 0)
                    {
                        Trace.WriteLine("  Panel children:");
                        foreach (var child in panel.Children)
                        {
                            Trace.WriteLine($"    - {child.GetType().Name}: {child}");
                        }
                    }
                }
                Trace.WriteLine("");
                
                Trace.WriteLine("TEST 2: ListBox with items");
                Trace.WriteLine("---------------------------");
                
                var listBox = new ListBox
                {
                    Width = 400,
                    Height = 300,
                    ItemsSource = items
                };
                
                window.Content = listBox;
                await Task.Delay(1000);
                
                var generator3 = listBox.ItemContainerGenerator;
                Trace.WriteLine($"? Generator exists: {generator3 != null}");
                
                Trace.WriteLine("Attempting to retrieve ListBoxItem containers:");
                for (int i = 0; i < items.Length; i++)
                {
                    var container = generator3.ContainerFromIndex(i);
                    Trace.WriteLine($"  Index {i}: Container={container?.GetType().Name ?? "NULL"}");
                    
                    if (container is ListBoxItem listBoxItem)
                    {
                        Trace.WriteLine($"    Content: {listBoxItem.Content}");
                    }
                }
                
                var presenter3 = listBox.Presenter;
                if (presenter3?.Panel != null)
                {
                    Trace.WriteLine($"  Panel children count: {presenter3.Panel.Children.Count}");
                    Trace.WriteLine("  Visual children:");
                    foreach (var child in presenter3.Panel.Children)
                    {
                        Trace.WriteLine($"    - {child.GetType().Name}");
                        if (child is ListBoxItem lbi)
                        {
                            Trace.WriteLine($"      Content: {lbi.Content}");
                        }
                    }
                }
                Trace.WriteLine("");
                
                Trace.WriteLine("=== SUMMARY ===");
                Trace.WriteLine($"ItemsControl: Panel has {itemsControl.Presenter?.Panel?.Children.Count ?? 0} children");
                Trace.WriteLine($"ListBox: Panel has {listBox.Presenter?.Panel?.Children.Count ?? 0} children");
                Trace.WriteLine("Window will close in 10 seconds...");
                
                await Task.Delay(10000);
                
                window.Close();
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

        var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(20000));
        if (completedTask != testCompleted.Task)
        {
            Assert.Fail("Test timed out");
        }

        await testCompleted.Task;
        uiThread.Join(2000);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestListBoxVirtualization()
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
                Trace.WriteLine("=== ListBox Virtualization Test ===");
                Trace.WriteLine("");
                
                var itemCount = 10000;
                var items = Enumerable.Range(0, itemCount)
                    .Select(i => new TestDataItem 
                    { 
                        Id = i, 
                        Name = $"Item {i}",
                        Description = $"This is test item number {i} with some description text",
                        Value = i * 1.5
                    })
                    .ToList();
                
                Trace.WriteLine($"Created {itemCount:n0} test items");
                Trace.WriteLine("");
                
                var window = new Window
                {
                    Title = "ListBox Virtualization Test",
                    Width = 800,
                    Height = 600
                };
                
                Trace.WriteLine("TEST 1: ListBox with default StackPanel");
                Trace.WriteLine("------------------------------------------");
                
                var listBox1 = new ListBox
                {
                    Width = 750,
                    Height = 500,
                    ItemsSource = items
                };
                
                window.Content = listBox1;
                window.Show();
                
                await Task.Delay(2000);
                
                var presenter1 = listBox1.Presenter;
                var panel1 = presenter1?.Panel;
                
                Trace.WriteLine($"ItemsSource count: {items.Count:n0}");
                Trace.WriteLine($"Panel type: {panel1?.GetType().Name ?? "NULL"}");
                Trace.WriteLine($"Panel.Children.Count: {panel1?.Children.Count ?? 0:n0}");
                Trace.WriteLine($"Memory: ~{GC.GetTotalMemory(false) / 1024 / 1024:n0} MB");
                
                if (panel1?.Children.Count == items.Count)
                {
                    Trace.WriteLine("?? NO VIRTUALIZATION - All items rendered!");
                }
                else
                {
                    Trace.WriteLine("? VIRTUALIZATION WORKING - Only visible items rendered!");
                }
                Trace.WriteLine("");
                
                Trace.WriteLine("=== SUMMARY ===");
                Trace.WriteLine($"Total items: {itemCount:n0}");
                Trace.WriteLine($"Visual tree items: {panel1?.Children.Count ?? 0:n0}");
                
                if (panel1?.Children.Count < itemCount * 0.1)
                {
                    Trace.WriteLine("? EXCELLENT: Strong virtualization detected!");
                }
                else if (panel1?.Children.Count < itemCount * 0.5)
                {
                    Trace.WriteLine("?? PARTIAL: Some virtualization working");
                }
                else
                {
                    Trace.WriteLine("? POOR: No meaningful virtualization");
                }
                
                Trace.WriteLine("");
                Trace.WriteLine("Window will close in 10 seconds...");
                
                await Task.Delay(10000);
                
                window.Close();
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

        var completedTask = await Task.WhenAny(testCompleted.Task, Task.Delay(25000));
        if (completedTask != testCompleted.Task)
        {
            Assert.Fail("Test timed out");
        }

        await testCompleted.Task;
        uiThread.Join(2000);
    }
}
