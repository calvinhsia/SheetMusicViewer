using Avalonia.Threading;
using AvaloniaTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer.Desktop;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Manual tests for MIDI output device enumeration and playback.
///
/// Run with:
///   dotnet test --filter "TestCategory=Manual&amp;ClassName=MidiDeviceManualTests"
/// </summary>
[TestClass]
[TestCategory("Manual")]
public class MidiDeviceManualTests : TestBase
{
    /// <summary>
    /// Opens a window that:
    ///   • Lists all available MIDI output devices.
    ///   • Auto-refreshes the list every 2 seconds (so plug-in/unplug is detected).
    ///   • Lets you pick a device and play a repeating C-major scale via ▶ Play.
    ///
    /// Close the window to end the test.
    /// </summary>
    [TestMethod]
    public async Task ShowMidiDeviceTesterWindow()
    {
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = MidiDeviceTesterWindow.Create();
            lifetime.MainWindow = window;

            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(
                testCompleted,
                lifetime,
                "MidiDeviceTesterWindow closed by user.");

            window.Show();

            Trace.WriteLine("MIDI Device Tester window opened.  Close it to finish the test.");
            await Task.CompletedTask;

        }, timeoutMs: 300_000); // 5-minute timeout — leave plenty of time for manual testing
    }
}
