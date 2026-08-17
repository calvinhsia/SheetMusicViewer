using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace AvaloniaTests.Tests;

/// <summary>
/// Assembly-level fixture that owns the single Avalonia UI thread for all headless tests.
/// Avalonia's SetupWithoutStarting() designates the calling thread as the UI thread;
/// only one thread can hold that role for the entire process lifetime.
/// All tests that need Avalonia UI thread access must use RunOnUIThread().
///
/// Headless Avalonia is initialised lazily on the first call to EnsureInitialized()
/// (or RunOnUIThread()), so that [TestCategory("Manual")] desktop tests can call
/// RunAvaloniaTest() without hitting "Setup was already called" from AppBuilder.
/// </summary>
[TestClass]
public static class AvaloniaUIThreadFixture
{
    private static Thread? _uiThread;
    private static BlockingCollection<(Action work, ManualResetEventSlim done, ExceptionHolder holder)>? _queue;
    private static readonly object _initLock = new();

    public static bool IsInitialized { get; private set; }
    public static bool InitializationFailed { get; private set; }
    public static string? InitializationError { get; private set; }

    // Only meaningful on Windows — Avalonia headless WriteableBitmap is Windows-only.
    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                                      && IsInitialized;

    internal sealed class ExceptionHolder
    {
        public System.Runtime.ExceptionServices.ExceptionDispatchInfo? Info;
    }


    [AssemblyCleanup]
    public static void AssemblyCleanup()
    {
        _queue?.CompleteAdding();
        _uiThread?.Join(TimeSpan.FromSeconds(5));
        _queue?.Dispose();
    }

    /// <summary>
    /// Initialises the headless Avalonia UI thread on first call. Subsequent calls
    /// are no-ops. Must be called before RunOnUIThread() or IsInitialized checks.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (IsInitialized || InitializationFailed) return;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        lock (_initLock)
        {
            if (IsInitialized || InitializationFailed) return;

            _queue = new BlockingCollection<(Action, ManualResetEventSlim, ExceptionHolder)>();

            _uiThread = new Thread(() =>
            {
                // This thread becomes the Avalonia UI thread for headless tests.
                try
                {
                    AppBuilder.Configure<HeadlessTestApp>()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                        .SetupWithoutStarting();
                    IsInitialized = true;
                }
                catch (Exception ex)
                {
                    // Includes "Setup was already called" when a desktop test ran first.
                    InitializationFailed = true;
                    InitializationError = ex.Message;
                }

                foreach (var (work, done, holder) in _queue!.GetConsumingEnumerable())
                {
                    try { work(); }
                    catch (Exception ex)
                    {
                        holder.Info = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                    }
                    finally { done.Set(); }
                }
            });

            _uiThread.IsBackground = true;
            _uiThread.Name = "AvaloniaUIThread";
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();

            // Wait up to 10 s for Avalonia to initialise.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!IsInitialized && !InitializationFailed && DateTime.UtcNow < deadline)
                Thread.Sleep(10);
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the Avalonia UI thread and blocks until complete.
    /// Any exception thrown inside the action is re-thrown on the calling thread.
    /// </summary>
    public static void RunOnUIThread(Action action)
    {
        EnsureInitialized();

        if (!IsInitialized)
            throw new InvalidOperationException("Avalonia UI thread is not initialized.");

        var done = new ManualResetEventSlim(false);
        var holder = new ExceptionHolder();
        _queue!.Add((action, done, holder));
        done.Wait();
        holder.Info?.Throw();
    }

    /// <summary>Minimal Avalonia application for headless tests.</summary>
    private sealed class HeadlessTestApp : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }
}
