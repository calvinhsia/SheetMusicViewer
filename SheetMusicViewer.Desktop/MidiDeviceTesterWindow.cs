// MidiDeviceTesterWindow.cs
// Code-only Avalonia window for interactively testing MIDI output devices.
//
// Features:
//   • Enumerates all available MIDI output devices (plus the MIDI Mapper)
//   • Auto-refreshes the device list every 2 seconds (toggleable)
//   • Play / Stop a repeating C-major scale on the selected device
//
// Used from AvaloniaTests manual tests only — not referenced by production code.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SheetMusicViewer.Desktop;

public static class MidiDeviceTesterWindow
{
    // ─────────────────────────────────────────────────────────────────────────
    //  winmm P/Invoke
    // ─────────────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MIDIOUTCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint   vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public ushort wTechnology;
        public ushort wVoices;
        public ushort wNotes;
        public ushort wChannelMask;
        public uint   dwSupport;
    }

    [DllImport("winmm.dll")]
    private static extern int midiOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int midiOutGetDevCaps(uint uDeviceID, ref MIDIOUTCAPS lpMidiOutCaps, uint cbMidiOutCaps);

    [DllImport("winmm.dll")]
    private static extern int midiOutOpen(out IntPtr h, int dev, IntPtr cb, IntPtr inst, int flags);

    [DllImport("winmm.dll")]
    private static extern int midiOutShortMsg(IntPtr h, uint msg);

    [DllImport("winmm.dll")]
    private static extern int midiOutClose(IntPtr h);

    // ─────────────────────────────────────────────────────────────────────────
    //  Device enumeration
    // ─────────────────────────────────────────────────────────────────────────

    private record MidiDevice(int DeviceId, string Name)
    {
        public override string ToString() => DeviceId == -1 ? Name : $"[{DeviceId}]  {Name}";
    }

    private static List<MidiDevice> EnumerateDevices()
    {
        var list = new List<MidiDevice> { new(-1, "MIDI Mapper (system default)") };
        int count = midiOutGetNumDevs();
        for (int i = 0; i < count; i++)
        {
            var caps = new MIDIOUTCAPS();
            midiOutGetDevCaps((uint)i, ref caps, (uint)Marshal.SizeOf<MIDIOUTCAPS>());
            list.Add(new MidiDevice(i, caps.szPname ?? $"Device {i}"));
        }
        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  C-major scale (C4–C5 ascending, C5–C4 descending)
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly int[] ScaleNotes =
    [
        60, 62, 64, 65, 67, 69, 71, 72,   // C4 D4 E4 F4 G4 A4 B4 C5
        71, 69, 67, 65, 64, 62, 60        // B4 A4 G4 F4 E4 D4 C4
    ];

    private static uint NoteOn (int note, int velocity = 80) => (uint)(0x90 | (note << 8) | (velocity << 16));
    private static uint NoteOff(int note)                    => (uint)(0x80 | (note << 8));

    private static void AllNotesOff(IntPtr h)
    {
        for (int ch = 0; ch < 16; ch++)
            midiOutShortMsg(h, (uint)((0xB0 | ch) | (123 << 8)));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Window factory
    // ─────────────────────────────────────────────────────────────────────────

    public static Window Create()
    {
        // ── controls ─────────────────────────────────────────────────────────
        var deviceListBox = new ListBox
        {
            MinHeight        = 180,
            SelectionMode    = SelectionMode.Single,
            Margin           = new Thickness(0, 4, 0, 0),
        };

        var statusLabel   = new TextBlock
        {
            Text       = "Select a device and click ▶ Play.",
            Margin     = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var refreshBtn    = new Button { Content = "🔄 Refresh", Margin = new Thickness(0, 0, 4, 0) };
        var autoRefreshChk = new CheckBox { Content = "Auto-refresh (2 s)", IsChecked = true, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var playBtn       = new Button { Content = "▶  Play Scale", Margin = new Thickness(0, 0, 4, 0) };
        var stopBtn       = new Button { Content = "■  Stop",  Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };

        // ── device state ──────────────────────────────────────────────────────
        var currentDevices = new List<MidiDevice>();

        void RefreshDevices()
        {
            var devices   = EnumerateDevices();
            int deviceCount = devices.Count;

            // Try to keep the same device selected by name.
            string? prevName = deviceListBox.SelectedIndex >= 0 && deviceListBox.SelectedIndex < currentDevices.Count
                ? currentDevices[deviceListBox.SelectedIndex].Name
                : null;

            currentDevices.Clear();
            currentDevices.AddRange(devices);

            deviceListBox.ItemsSource = null;
            deviceListBox.ItemsSource = currentDevices;

            int newIdx = 0;
            if (prevName != null)
            {
                int found = currentDevices.FindIndex(d => d.Name == prevName);
                if (found >= 0) newIdx = found;
            }
            deviceListBox.SelectedIndex = newIdx;
            statusLabel.Text = $"{deviceCount} MIDI output device(s) found.  Select one and click ▶ Play.";
        }

        RefreshDevices();

        // ── auto-refresh timer ────────────────────────────────────────────────
        var refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        refreshTimer.Tick += (_, _) =>
        {
            if (autoRefreshChk.IsChecked == true)
                RefreshDevices();
        };
        refreshTimer.Start();

        refreshBtn.Click += (_, _) => RefreshDevices();

        // ── playback state ────────────────────────────────────────────────────
        CancellationTokenSource? playCts = null;

        playBtn.Click += (_, _) =>
        {
            int selIdx = deviceListBox.SelectedIndex;
            if (selIdx < 0 || selIdx >= currentDevices.Count)
            {
                statusLabel.Text = "⚠  Please select a MIDI output device first.";
                return;
            }

            var device = currentDevices[selIdx];

            if (midiOutOpen(out IntPtr handle, device.DeviceId, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            {
                statusLabel.Text = $"⚠  Could not open '{device.Name}'.";
                return;
            }

            playBtn.IsEnabled = false;
            stopBtn.IsEnabled = true;
            statusLabel.Text  = $"▶  Playing on '{device.Name}' …";

            var cts = new CancellationTokenSource();
            playCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        foreach (int note in ScaleNotes)
                        {
                            if (cts.Token.IsCancellationRequested) break;
                            midiOutShortMsg(handle, NoteOn(note));
                            await Task.Delay(220, cts.Token);
                            midiOutShortMsg(handle, NoteOff(note));
                            await Task.Delay(30, cts.Token);
                        }
                        // Brief pause between repetitions.
                        await Task.Delay(500, cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    AllNotesOff(handle);
                    midiOutClose(handle);

                    Dispatcher.UIThread.Post(() =>
                    {
                        // Only reset UI if this CTS is still the active one.
                        if (playCts == cts) playCts = null;
                        playBtn.IsEnabled = true;
                        stopBtn.IsEnabled = false;
                        statusLabel.Text  = "■  Stopped.";
                    });
                }
            }, CancellationToken.None);
        };

        stopBtn.Click += (_, _) => playCts?.Cancel();

        // ─── layout ───────────────────────────────────────────────────────────

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 6),
        };
        toolbar.Children.Add(refreshBtn);
        toolbar.Children.Add(autoRefreshChk);
        toolbar.Children.Add(playBtn);
        toolbar.Children.Add(stopBtn);
        toolbar.Children.Add(statusLabel);

        var listHeader = new TextBlock
        {
            Text       = "Available MIDI Output Devices:",
            FontWeight = FontWeight.Bold,
            Margin     = new Thickness(0, 0, 0, 2),
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(toolbar,    Dock.Top);
        DockPanel.SetDock(listHeader, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(listHeader);
        root.Children.Add(deviceListBox); // fills remaining space

        var window = new Window
        {
            Title                  = "MIDI Device Tester",
            Width                  = 520,
            Height                 = 360,
            WindowStartupLocation  = WindowStartupLocation.CenterScreen,
            Content                = root,
        };

        window.Closed += (_, _) =>
        {
            refreshTimer.Stop();
            playCts?.Cancel();
        };

        return window;
    }
}
