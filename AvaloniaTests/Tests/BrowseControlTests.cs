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
public class BrowseControlTests : TestBase
{
    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestAvaloniaChooseMusicDialog()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = new ChooseMusicWindow();
            lifetime.MainWindow = window;
            
            var timer = new System.Timers.Timer(30000);
            
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "ChooseMusicWindow closed");
            
            window.Closed += (s, e) => timer.Stop();
            
            timer.Elapsed += (s, e) =>
            {
                timer.Stop();
                Dispatcher.UIThread.Post(() =>
                {
                    Trace.WriteLine("Auto-closing ChooseMusicWindow after 30 seconds");
                    window?.Close();
                });
            };
            
            window.Show();
            
            Trace.WriteLine($"? ChooseMusicWindow created and shown");
            Trace.WriteLine($"? Generating bitmaps for books...");
            Trace.WriteLine($"? Window will auto-close after 30 seconds, or close manually");

            await Task.Delay(1000);
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
            
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "BrowseListWindow closed by user");
            
            window.Show();
            
            Trace.WriteLine($"? BrowseListWindow created and shown");
            Trace.WriteLine($"? Loading types from Avalonia assemblies...");

            await Task.Delay(100);
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
                Trace.WriteLine("=== BrowseControl with 10,000 Items ===");
                
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
                var browseControl = new BrowseControl(query, colWidths: new[] { 80, 150, 120, 80, 100, 100, 350 });
                sw.Stop();
                
                var window = new Window
                {
                    Title = $"BrowseControl - {itemCount:n0} Items (Virtualized)",
                    Width = 1200,
                    Height = 800,
                    Content = browseControl,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                
                window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                    testCompleted,
                    lifetime,
                    "BrowseControl window closed");
                
                window.Show();
                await Task.Delay(1000);
                
                var memoryMB = GC.GetTotalMemory(false) / 1024 / 1024;
                
                Trace.WriteLine($"? Creation time: {sw.ElapsedMilliseconds:n0} ms");
                Trace.WriteLine($"? Memory: ~{memoryMB:n0} MB");
                Trace.WriteLine("Close the window when finished testing.");
            });
        });
    }

    [TestMethod]
    [TestCategory("Manual")]
    public async Task TestBrowseControlWithSelectionHandler()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            await Task.Delay(1000);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                Trace.WriteLine("=== BrowseControl with Selection Handler ===");
                Trace.WriteLine("Try double-clicking or pressing Enter on a row");
                Trace.WriteLine("");
                
                var itemCount = 100;
                var query = Enumerable.Range(0, itemCount)
                    .Select(i => new
                    {
                        Id = i,
                        Name = $"Item {i:D5}",
                        Category = $"Category {i % 20}",
                        Type = $"Type {i % 5}",
                        Value = i * 2.5,
                        Status = i % 3 == 0 ? "Active" : i % 3 == 1 ? "Pending" : "Inactive",
                        Description = $"Description for test item number {i}"
                    });
                
                // Create BrowseControl with selection handler
                var browseControl = new BrowseControl(
                    query, 
                    colWidths: new[] { 80, 150, 120, 80, 100, 100, 350 },
                    selectionHandler: args =>
                    {
                        Trace.WriteLine($"");
                        Trace.WriteLine($"=== Selection Activated ===");
                        Trace.WriteLine($"Activation Type: {args.ActivationType}");
                        Trace.WriteLine($"Selected Index: {args.SelectedIndex}");
                        Trace.WriteLine($"Selected Item: {args.SelectedItem}");
                        Trace.WriteLine($"Total Selected Items: {args.SelectedItems.Count}");
                        
                        if (args.SelectedItems.Count > 1)
                        {
                            Trace.WriteLine($"Multiple items selected:");
                            foreach (var item in args.SelectedItems)
                            {
                                Trace.WriteLine($"  - {item}");
                            }
                        }
                        Trace.WriteLine($"======================");
                        Trace.WriteLine($"");
                    });
                
                var window = new Window
                {
                    Title = $"BrowseControl with Selection Handler - {itemCount:n0} Items",
                    Width = 1200,
                    Height = 800,
                    Content = browseControl,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                
                window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                    testCompleted,
                    lifetime,
                    "BrowseControl with selection handler window closed");
                
                window.Show();
                await Task.Delay(1000);
                
                Trace.WriteLine($"? Window created with selection handler");
                Trace.WriteLine($"? Double-click or press Enter on any row to trigger the handler");
                Trace.WriteLine($"? Select multiple rows (Ctrl+Click) and press Enter to see multiple selection");
                Trace.WriteLine("Close the window when finished testing.");
            });
        });
    }
}
