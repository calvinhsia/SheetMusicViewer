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
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
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
        }, timeoutMs: 35000);
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestAvaloniaDataGridBrowseList()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
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
        });
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestBrowseControlComparison()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            await Task.Delay(1000);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                Trace.WriteLine("=== ListBoxBrowseControl Performance Test ===");
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
                
                Trace.WriteLine("TEST: ListBoxBrowseControl (Virtualized ListBox)");
                Trace.WriteLine("--------------------------------------------------");
                
                var sw = Stopwatch.StartNew();
                var browseControl = new ListBoxBrowseControl(query, colWidths: new[] { 100, 200, 150, 100, 300 });
                sw.Stop();
                
                var window = new Window
                {
                    Title = "ListBoxBrowseControl (Virtualized) - 1,000 items",
                    Width = 1000,
                    Height = 600,
                    Content = browseControl
                };
                
                window.Show();
                await Task.Delay(500);
                
                var memoryBefore = GC.GetTotalMemory(false);
                
                var listBoxView = browseControl.ListView;
                var listBoxField = listBoxView.GetType().GetField("_listBox", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var listBox = listBoxField?.GetValue(listBoxView) as ListBox;
                var presenter = listBox?.Presenter;
                var panel = presenter?.Panel;
                
                Trace.WriteLine($"Creation time: {sw.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"ListBox panel type: {panel?.GetType().Name ?? "N/A"}");
                Trace.WriteLine($"ListBox panel children: {panel?.Children.Count.ToString("n0") ?? "N/A"}");
                Trace.WriteLine($"Memory footprint: ~{memoryBefore / 1024 / 1024:n0} MB");
                Trace.WriteLine("");
                
                Trace.WriteLine("=== RESULTS ===");
                Trace.WriteLine($"Dataset: {itemCount:n0} items");
                Trace.WriteLine($"Rendered: ~{panel?.Children.Count ?? 0} rows");
                Trace.WriteLine($"Time: {sw.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"Memory: ~{memoryBefore / 1024 / 1024:n0} MB");
                
                if (panel?.Children.Count < itemCount * 0.1)
                {
                    Trace.WriteLine($"? VIRTUALIZATION WORKING! Only {panel.Children.Count:n0} / {itemCount:n0} items in visual tree");
                }
                
                Trace.WriteLine("");
                Trace.WriteLine("Window will close in 30 seconds...");
                
                await Task.Delay(30000);
                
                window.Close();
                testCompleted.SetResult(true);
                lifetime.Shutdown();
            });
        });
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestListBoxBrowseControl()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            await Task.Delay(1000);

            await Dispatcher.UIThread.InvokeAsync(async () =>
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
            });
        });
    }
}
