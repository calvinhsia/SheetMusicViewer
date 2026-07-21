using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer.Desktop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static SheetMusicViewer.Desktop.BrowseControl;

namespace AvaloniaTests.Tests;

/// <summary>
/// Manual tests that take a .mxl file (compressed MusicXML produced by the Audiveris
/// export pipeline) and open various Avalonia windows to visualise its contents.
///
/// Nothing is written to disk; all output goes to Trace / the test-output window.
///
/// Run with:
///   dotnet test --filter "TestCategory=Manual&amp;ClassName=AvaloniaTests.Tests.MxlVisualizationManualTests"
///
/// Close each window to advance to the next test.
/// </summary>
[TestClass]
[TestCategory("Manual")]
[DoNotParallelize]
public class MxlVisualizationManualTests : TestBase
{
    // ── Edit this relative path to point at the .mxl you want to inspect ──
    private string AdhocMxlPath =>
        Path.Combine(GetSheetMusicFolder(), @"..\Temp\Tico-Tico no Fubá - A Minor - MN0227296 - Tico-Tico no Fubá - A Minor - MN0227296.mxl\Tico-Tico no Fubá - A Minor - MN0227296.xml");

    // ───────────────────────────────────────────────────────────────────────

    // -----------------------------------------------------------------------
    //  Ad-hoc entry point — edit AdhocMxlPath then run
    // -----------------------------------------------------------------------

    /// <summary>
    /// Ad-hoc test: edit <see cref="AdhocMxlPath"/> at the top of this class to point
    /// at any .mxl produced by the app, then run.  Shows all visualizations in
    /// sequence (close each window to advance to the next).
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_AllViews()
    {
        await RunAllVisualizationsAsync(AdhocMxlPath);
    }

    // -----------------------------------------------------------------------
    //  Individual named visualizations (each opens a single window)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Shows a high-level score summary: file info, parts, total measures and notes.
    /// Edit <c>mxlPath</c> to point at the file you want to inspect.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_ScoreSummary()
    {
        var score = ParseMxl(AdhocMxlPath);
        await ShowScoreSummaryWindowAsync(AdhocMxlPath, score);
    }

    /// <summary>
    /// Opens a searchable BrowseControl grid showing every note in the score:
    /// Part · Measure · Staff · Voice · Pitch · Octave · Duration · Dots · Accidental.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_NotesBrowser()
    {
        var score = ParseMxl(AdhocMxlPath);
        await ShowNotesBrowserWindowAsync(AdhocMxlPath, score);
    }

    /// <summary>
    /// Opens a BrowseControl grid showing each part/instrument with its statistics:
    /// Part ID · Instrument name · MIDI program · Measure count · Note count · Rest count.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_PartsBrowser()
    {
        var score = ParseMxl(AdhocMxlPath);
        await ShowPartsBrowserWindowAsync(AdhocMxlPath, score);
    }

    /// <summary>
    /// Opens a BrowseControl grid showing every measure across all parts:
    /// Part · Measure # · Time sig · Notes · Rests · Chords · Stave attributes.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_MeasureBrowser()
    {
        var score = ParseMxl(AdhocMxlPath);
        await ShowMeasureBrowserWindowAsync(AdhocMxlPath, score);
    }

    /// <summary>
    /// Shows the raw MusicXML text in a scrollable read-only text editor window.
    /// Useful for spotting Audiveris transcription issues.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_RawXml()
    {
        await ShowRawXmlWindowAsync(AdhocMxlPath);
    }

    /// <summary>
    /// Launches the .mxl in MuseScore Studio (same as the production "Open in MuseScore"
    /// button).  MuseScore must be installed for this to do anything.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_LaunchMuseScore()
    {
        var mxlPath = AdhocMxlPath;

        if (!File.Exists(mxlPath))
            Assert.Inconclusive($"MXL not found: {mxlPath}");

        var museScorePath = MuseScoreExportService.AutoDetectMuseScore();
        if (museScorePath is null)
            Assert.Inconclusive("MuseScore executable not found.");

        LogMessage($"Launching MuseScore: {museScorePath}");
        LogMessage($"File             : {mxlPath}");
        MuseScoreExportService.LaunchMuseScore(museScorePath, mxlPath);

        // Give MuseScore time to start before the test exits
        await Task.Delay(2000);
    }

    /// <summary>
    /// Piano-roll view: each note rendered as a horizontal bar on a pitch-vs-measure grid.
    /// Green = staff 1 (treble / right hand), blue = staff 2 (bass / left hand).
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_PianoRoll()
    {
        var score = ParseMxl(AdhocMxlPath);
        await ShowPianoRollWindowAsync(AdhocMxlPath, score);
    }

    /// <summary>
    /// Rhythm density view: bar chart of how many notes fall on each 16th-note grid
    /// position within a beat, aggregated across the whole piece.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_RhythmDensity()
    {
        var score = ParseMxl(AdhocMxlPath);
        await ShowRhythmDensityWindowAsync(AdhocMxlPath, score);
    }

    /// <summary>
    /// Hand range view: for each measure shows the pitch range (min → max MIDI note)
    /// used by each staff, so you can see how the hands move across the keyboard.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_HandRange()
    {
        var score = ParseMxl(AdhocMxlPath);
        await ShowHandRangeWindowAsync(AdhocMxlPath, score);
    }

    /// <summary>
    /// Harmony timeline view: pitch-class (root) of the lowest note on each beat,
    /// colour-coded by chromatic pitch class — a quick harmonic fingerprint.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_HarmonyTimeline()
    {
        var score = ParseMxl(AdhocMxlPath);
        await ShowHarmonyTimelineWindowAsync(AdhocMxlPath, score);
    }

    /// <summary>
    /// Playable piano roll: shows the piano-roll grid and plays the score through the
    /// Windows MIDI synthesizer simultaneously, with a red cursor that tracks playback.
    /// Use the Play / Stop button and the BPM slider in the window.  Windows only.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_PlayablePianoRoll()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("MIDI playback requires Windows (winmm.dll).");
        var score = ParseMxl(AdhocMxlPath);
        await ShowPlayablePianoRollWindowAsync(AdhocMxlPath, score);
    }

    /// <summary>
    /// Vertical falling-notes piano roll: notes fall downward toward a piano keyboard
    /// drawn at the bottom of the window. Keys light up (green = right hand,
    /// blue = left hand) as each note is played.  Includes Play / Stop / BPM controls
    /// and uses the same Windows MIDI back-end as the playable piano roll.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_VerticalPianoRoll()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("MIDI playback requires Windows (winmm.dll).");
        var score = ParseMxl(AdhocMxlPath);
        await ShowVerticalPianoRollWindowAsync(AdhocMxlPath, score);
    }

    // -----------------------------------------------------------------------
    //  Run all visualizations in sequence
    //  A single Avalonia AppBuilder session is used; windows are chained so
    //  closing one opens the next (Avalonia only allows one Setup() per process).
    // -----------------------------------------------------------------------

    private async Task RunAllVisualizationsAsync(string mxlPath)
    {
        if (!File.Exists(mxlPath))
            Assert.Inconclusive($"MXL not found: {mxlPath}");

        LogMessage($"=== MXL Visualizer: {Path.GetFileName(mxlPath)} ===");
        var score = ParseMxl(mxlPath);
        LogMessage($"Parts: {score.Parts.Count}  Measures: {score.TotalMeasures}  Notes: {score.TotalNotes}  Rests: {score.TotalRests}");

        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            // All window factories share the single Avalonia lifetime.
            // Each factory is called only when the previous window has closed.
            var factories = new List<Func<Window>>
            {
                () => BuildScoreSummaryWindow(mxlPath, score),
                () => BuildPartsBrowserWindow(mxlPath, score),
                () => BuildMeasureBrowserWindow(mxlPath, score),
                () => BuildNotesBrowserWindow(mxlPath, score),
                () => BuildPianoRollWindow(mxlPath, score),
                () => BuildPlayablePianoRollWindow(mxlPath, score),
                () => BuildVerticalPianoRollWindow(mxlPath, score),
                () => BuildRhythmDensityWindow(mxlPath, score),
                () => BuildHandRangeWindow(mxlPath, score),
                () => BuildHarmonyTimelineWindow(mxlPath, score),
                () => BuildRawXmlWindow(mxlPath),
            };

            void ShowNext(int index)
            {
                if (index >= factories.Count)
                {
                    testCompleted.TrySetResult(true);
                    lifetime.Shutdown();
                    return;
                }
                var window = factories[index]();
                lifetime.MainWindow = window;
                window.Closed += (_, _) => ShowNext(index + 1);
                window.Show();
            }

            ShowNext(0);
            await Task.CompletedTask;
        });
    }

    // -----------------------------------------------------------------------
    //  Window builders — return a Window without starting Avalonia themselves.
    //  Individual Show*Async methods wrap each builder in its own RunAvaloniaTest
    //  (safe when running a single test at a time).
    // -----------------------------------------------------------------------

    private Window BuildScoreSummaryWindow(string mxlPath, MxlScore score)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"File  : {mxlPath}");
        sb.AppendLine($"Size  : {new FileInfo(mxlPath).Length:N0} bytes");
        sb.AppendLine();
        sb.AppendLine($"Title    : {score.Title}");
        sb.AppendLine($"Composer : {score.Composer}");
        sb.AppendLine();
        sb.AppendLine($"Parts          : {score.Parts.Count}");
        sb.AppendLine($"Total measures : {score.TotalMeasures}");
        sb.AppendLine($"Total notes    : {score.TotalNotes}");
        sb.AppendLine($"Total rests    : {score.TotalRests}");
        sb.AppendLine();
        sb.AppendLine("─── Parts ───────────────────────────────────────────");
        foreach (var p in score.Parts)
        {
            int staff1Notes = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff == 1));
            int staff2Notes = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff == 2));
            int otherNotes  = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff > 2));
            sb.AppendLine($"  [{p.PartId}] idx={p.PartIndex} {p.InstrumentName,-28}  MIDI={p.MidiProgram,3}  " +
                          $"Measures={p.Measures.Count,4}  Notes={p.NoteCount,5}  Rests={p.RestCount,4}  " +
                          $"Staff1={staff1Notes}  Staff2={staff2Notes}  StaffOther={otherNotes}");
        }
        bool isMultiStaff = score.Parts.Any(p =>
            p.Measures.Any(m => m.Notes.Any(n => !n.IsRest && n.Staff > 1)));
        sb.AppendLine();
        sb.AppendLine($"Layout: {(isMultiStaff ? "Grand-staff (1 part, 2 staves — colouring by <staff> element)" : "Two-part (colouring by part index: part 0=green, part 1=blue)")}");
        var summaryText = sb.ToString();
        Trace.WriteLine(summaryText);

        return new Window
        {
            Title = $"Score Summary — {Path.GetFileName(mxlPath)}",
            Width = 750,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBox
            {
                Text = summaryText,
                IsReadOnly = true,
                AcceptsReturn = true,
                FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                FontSize = 13,
                Background = Brushes.Black,
                Foreground = Brushes.LightGreen,
                BorderThickness = new Avalonia.Thickness(0),
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            }
        };
    }

    private Window BuildPartsBrowserWindow(string mxlPath, MxlScore score)
    {
        var rows = score.Parts.Select(p => new
        {
            PartId = p.PartId,
            Instrument = p.InstrumentName,
            MidiProgram = p.MidiProgram,
            Measures = p.Measures.Count,
            Notes = p.NoteCount,
            Rests = p.RestCount,
            AvgNotesPerMeasure = p.Measures.Count > 0
                ? Math.Round((double)p.NoteCount / p.Measures.Count, 1)
                : 0.0
        }).ToList();

        LogMessage($"Parts browser: {rows.Count} parts");

        return new Window
        {
            Title = $"Parts / Instruments — {Path.GetFileName(mxlPath)}",
            Width = 860,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new BrowseControl(rows, colWidths: new[] { 60, 240, 80, 80, 80, 80, 120 })
        };
    }

    private Window BuildMeasureBrowserWindow(string mxlPath, MxlScore score)
    {
        var rows = (from part in score.Parts
                    from measure in part.Measures
                    select new
                    {
                        Part = part.PartId,
                        Instrument = part.InstrumentName,
                        MeasureNo = measure.Number,
                        TimeSig = measure.TimeSig,
                        Notes = measure.NoteCount,
                        Rests = measure.RestCount,
                        Chords = measure.ChordCount,
                        KeySig = measure.KeySig
                    }).ToList();

        LogMessage($"Measure browser: {rows.Count} measures across {score.Parts.Count} parts");

        return new Window
        {
            Title = $"Measures — {Path.GetFileName(mxlPath)}  ({rows.Count} rows)",
            Width = 860,
            Height = 700,
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new BrowseControl(rows, colWidths: new[] { 60, 180, 80, 70, 60, 60, 70, 80 })
        };
    }

    private Window BuildNotesBrowserWindow(string mxlPath, MxlScore score)
    {
        var rows = (from part in score.Parts
                    from measure in part.Measures
                    from note in measure.Notes
                    select new
                    {
                        Part = part.PartId,
                        Instrument = part.InstrumentName,
                        MeasureNo = measure.Number,
                        Staff = note.Staff,
                        Voice = note.Voice,
                        Pitch = note.Pitch,
                        Octave = note.Octave,
                        Duration = note.Duration,
                        Type = note.NoteType,
                        Dots = note.Dots,
                        Accidental = note.Accidental,
                        IsRest = note.IsRest,
                        IsChord = note.IsChord
                    }).ToList();

        LogMessage($"Notes browser: {rows.Count} notes/rests across {score.Parts.Count} parts");

        return new Window
        {
            Title = $"Notes Browser — {Path.GetFileName(mxlPath)}  ({rows.Count:N0} notes)",
            Width = 1100,
            Height = 700,
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new BrowseControl(rows, colWidths: new[] { 60, 160, 80, 50, 50, 60, 60, 70, 70, 50, 80, 60, 60 })
        };
    }

    private Window BuildRawXmlWindow(string mxlPath)
    {
        var rawXml = ExtractXmlFromMxl(mxlPath);
        try
        {
            rawXml = XDocument.Parse(rawXml).ToString();
        }
        catch
        {
            // Leave as-is if parsing fails
        }

        LogMessage($"Raw XML length: {rawXml.Length:N0} chars");

        return new Window
        {
            Title = $"Raw MusicXML — {Path.GetFileName(mxlPath)}",
            Width = 1000,
            Height = 700,
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBox
            {
                Text = rawXml,
                IsReadOnly = true,
                AcceptsReturn = true,
                FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                FontSize = 12,
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            }
        };
    }

    // Individual Show*Async wrappers — each starts its own Avalonia session so
    // they work when run as a single isolated test (not in AllViews sequence).

    private async Task ShowScoreSummaryWindowAsync(string mxlPath, MxlScore score) =>
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildScoreSummaryWindow(mxlPath, score);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Score summary window closed.");
            window.Show();
            await Task.CompletedTask;
        });

    private async Task ShowPartsBrowserWindowAsync(string mxlPath, MxlScore score) =>
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildPartsBrowserWindow(mxlPath, score);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Parts browser window closed.");
            window.Show();
            await Task.CompletedTask;
        });

    private async Task ShowMeasureBrowserWindowAsync(string mxlPath, MxlScore score) =>
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildMeasureBrowserWindow(mxlPath, score);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Measure browser window closed.");
            window.Show();
            await Task.CompletedTask;
        });

    private async Task ShowNotesBrowserWindowAsync(string mxlPath, MxlScore score) =>
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildNotesBrowserWindow(mxlPath, score);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Notes browser window closed.");
            window.Show();
            await Task.CompletedTask;
        });

    private async Task ShowRawXmlWindowAsync(string mxlPath)
    {
        if (!File.Exists(mxlPath))
            Assert.Inconclusive($"MXL not found: {mxlPath}");

        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildRawXmlWindow(mxlPath);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Raw XML window closed.");
            window.Show();
            await Task.CompletedTask;
        });
    }

    private Window BuildPianoRollWindow(string mxlPath, MxlScore score)
    {
        LogMessage($"Piano roll: {score.TotalNotes} notes across {score.TotalMeasures} measures");
        return new Window
        {
            Title = $"Piano Roll — {Path.GetFileName(mxlPath)}",
            Width = 1400,
            Height = 620,
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new PianoRollCanvas(score)
            }
        };
    }

    private Window BuildRhythmDensityWindow(string mxlPath, MxlScore score)
    {
        LogMessage($"Rhythm density: {score.TotalNotes} notes");
        return new Window
        {
            Title = $"Rhythm Density — {Path.GetFileName(mxlPath)}",
            Width = 1000,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new RhythmDensityCanvas(score)
            }
        };
    }

    private Window BuildHandRangeWindow(string mxlPath, MxlScore score)
    {
        LogMessage($"Hand range: {score.TotalMeasures} measures");
        return new Window
        {
            Title = $"Hand Range Analysis — {Path.GetFileName(mxlPath)}",
            Width = 1400,
            Height = 500,
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new HandRangeCanvas(score)
            }
        };
    }

    private Window BuildHarmonyTimelineWindow(string mxlPath, MxlScore score)
    {
        LogMessage($"Harmony timeline: {score.TotalMeasures} measures");
        return new Window
        {
            Title = $"Harmony Timeline — {Path.GetFileName(mxlPath)}",
            Width = 1400,
            Height = 360,
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new HarmonyTimelineCanvas(score)
            }
        };
    }

    private async Task ShowPianoRollWindowAsync(string mxlPath, MxlScore score) =>
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildPianoRollWindow(mxlPath, score);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Piano roll closed.");
            window.Show();
            await Task.CompletedTask;
        });

    private Window BuildPlayablePianoRollWindow(string mxlPath, MxlScore score)
    {
        bool isMultiStaffP = score.Parts.Any(p =>
            p.Measures.Any(m => m.Notes.Any(n => !n.IsRest && n.Staff > 1)));
        string layoutP = isMultiStaffP
            ? "Grand-staff (colouring by <staff> element)"
            : "Two-part (colouring by part index: 0=green, 1=blue)";
        var diagSbP = new StringBuilder();
        diagSbP.AppendLine($"Playable piano roll: {score.TotalNotes} notes  Parts={score.Parts.Count}  BPM={score.DefaultBpm}  Layout={layoutP}");
        foreach (var p in score.Parts)
        {
            int s1 = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff == 1));
            int s2 = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff == 2));
            diagSbP.AppendLine($"  Part[{p.PartIndex}] {p.PartId,-6} {p.InstrumentName,-28}  Notes={p.NoteCount}  Staff1={s1}  Staff2={s2}");
        }
        LogMessage(diagSbP.ToString());

        var canvas       = new PlayablePianoRollCanvas(score);
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Content = canvas
        };

        // Auto-scroll so the playhead stays centred horizontally
        canvas.PlayheadXChanged += xPx => Dispatcher.UIThread.Post(() =>
        {
            double target = Math.Max(0, xPx - scrollViewer.Bounds.Width / 2.0);
            scrollViewer.Offset = new Vector(target, scrollViewer.Offset.Y);
        });

        var statusBlock = new TextBlock
        {
            Text = $"Stopped  |  BPM: {score.DefaultBpm:F0}  |  {score.Title}",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 12,
            Margin = new Thickness(8, 0)
        };

        var bpmSlider = new Slider
        {
            Minimum = 40, Maximum = 300,
            Value   = score.DefaultBpm,
            Width   = 180,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [ToolTip.TipProperty] = "Tempo (BPM)"
        };
        var bpmLabel = new TextBlock
        {
            Text = $"{score.DefaultBpm:F0} BPM",
            Width = 60,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 12
        };
        bpmSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                bpmLabel.Text = $"{bpmSlider.Value:F0} BPM";
        };

        var playBtn = new Button { Content = "▶  Play",  Margin = new Thickness(4), Padding = new Thickness(8, 2) };
        var stopBtn = new Button { Content = "■  Stop",  Margin = new Thickness(4), Padding = new Thickness(8, 2), IsEnabled = false };

        int totalMeasuresH = score.Parts.Max(p => p.Measures.Count);
        var measureSlider  = new Slider
        {
            Minimum = 1, Maximum = Math.Max(1, totalMeasuresH),
            Value   = 1,
            Width   = 260,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [ToolTip.TipProperty] = "Jump to measure (stop first)"
        };
        var measureLabel = new TextBlock
        {
            Text = $"M 1/{totalMeasuresH}",
            Width = 72,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 12
        };
        measureSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                measureLabel.Text = $"M {(int)measureSlider.Value}/{totalMeasuresH}";
        };

        MxlMidiPlayer? player = null;
        bool sliderDrivingH = false;

        var logNotesChk = new CheckBox
        {
            Content = "Log notes",
            IsChecked = false,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            [ToolTip.TipProperty] = "Write every NoteOn to Trace while playing (check before pressing Play, or mid-song)"
        };

        void SetStopped()
        {
            player?.Dispose();
            player = null;
            canvas.CurrentGlobalDivisions = -1;
            statusBlock.Text = "Stopped";
            playBtn.IsEnabled = true;
            stopBtn.IsEnabled = false;
        }

        playBtn.Click += (_, _) =>
        {
            player?.Dispose();
            player = new MxlMidiPlayer(score)
            {
                Bpm          = bpmSlider.Value,
                StartMeasure = (int)measureSlider.Value,
                LogNotes     = logNotesChk.IsChecked == true,
            };

            // Allow toggling mid-playback
            logNotesChk.IsCheckedChanged += (_, _) =>
            {
                if (player != null) player.LogNotes = logNotesChk.IsChecked == true;
            };

            player.PositionChanged += (_, divs) => Dispatcher.UIThread.Post(() =>
            {
                canvas.CurrentGlobalDivisions = divs;
                int measureNo = score.Parts[0].Measures
                    .LastOrDefault(m => m.GlobalOnsetDivisions <= divs)?.Number ?? 1;
                sliderDrivingH = true;
                measureSlider.Value = measureNo;
                sliderDrivingH = false;
                statusBlock.Text = $"Playing ▶  measure {measureNo}  |  {bpmSlider.Value:F0} BPM";
            });

            player.PlaybackEnded += (_, _) => Dispatcher.UIThread.Post(SetStopped);

            playBtn.IsEnabled = false;
            stopBtn.IsEnabled = true;
            statusBlock.Text  = "Playing ▶  ...";
            player.Start();
        };

        stopBtn.Click += (_, _) => SetStopped();

        var toolbar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin = new Thickness(4),
            Children = { playBtn, stopBtn, bpmSlider, bpmLabel, measureSlider, measureLabel, logNotesChk, statusBlock }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(scrollViewer);

        var window = new Window
        {
            Title = $"Playable Piano Roll — {Path.GetFileName(mxlPath)}",
            Width = 1400,
            Height = 620,
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = layout
        };
        window.Closed += (_, _) => SetStopped();
        window.Opened += (_, _) => playBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        return window;
    }

    private async Task ShowPlayablePianoRollWindowAsync(string mxlPath, MxlScore score) =>
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildPlayablePianoRollWindow(mxlPath, score);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Playable piano roll closed.");
            window.Show();
            await Task.CompletedTask;
        });

    private Window BuildVerticalPianoRollWindow(string mxlPath, MxlScore score)
    {
        bool isMultiStaff = score.Parts.Any(p =>
            p.Measures.Any(m => m.Notes.Any(n => !n.IsRest && n.Staff > 1)));
        string staffLayout = isMultiStaff
            ? "Grand-staff (colouring by <staff> element)"
            : "Two-part (colouring by part index: 0=green, 1=blue)";

        var diagSb = new StringBuilder();
        diagSb.AppendLine($"Vertical piano roll: {score.TotalNotes} notes  Parts={score.Parts.Count}  Layout={staffLayout}");
        foreach (var p in score.Parts)
        {
            int s1 = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff == 1));
            int s2 = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff == 2));
            int sX = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff > 2));
            int lowestS1  = p.Measures.SelectMany(m => m.Notes).Where(n => !n.IsRest && n.Staff == 1 && n.MidiPitch > 0).Select(n => n.MidiPitch).DefaultIfEmpty(0).Min();
            int highestS2 = p.Measures.SelectMany(m => m.Notes).Where(n => !n.IsRest && n.Staff == 2 && n.MidiPitch > 0).Select(n => n.MidiPitch).DefaultIfEmpty(0).Max();
            int overlapRange = Math.Max(0, highestS2 - lowestS1 + 1);
            diagSb.AppendLine($"  Part[{p.PartIndex}] {p.PartId,-6} {p.InstrumentName,-28}  Notes={p.NoteCount}  Staff1={s1} (lowest={lowestS1})  Staff2={s2} (highest={highestS2})  StaffOther={sX}");
            diagSb.AppendLine($"    Pitch overlap between staves: {overlapRange} semitones  " +
                              $"(Staff1 bottom={lowestS1}, Staff2 top={highestS2} — " +
                              $"{(overlapRange > 0 ? "hands DO share pitch range, both colours should appear" : "hands occupy disjoint ranges — colour split only visible where ranges cross")})");

            // Per-note dump (first 5 measures) — commented out; re-enable to debug missing notes.
            // To enable: change the measure range in the Where clause and uncomment below.
            // diagSb.AppendLine($"    --- Per-note dump: measures M..N ---");
            // int dumpFrom = 1, dumpTo = 5;   // ← edit range here
            // foreach (var m in p.Measures.Where(m => m.Number >= dumpFrom && m.Number <= dumpTo))
            // {
            //     diagSb.AppendLine($"    Measure {m.Number,2}  (divs={m.Divisions} globalOnset={m.GlobalOnsetDivisions})");
            //     foreach (var n in m.Notes)
            //     {
            //         int vs = score.VisualStaff(p, n);
            //         string colour = vs == 1 ? "GREEN" : vs == 2 ? "BLUE " : "OTHER";
            //         diagSb.AppendLine($"      onset={n.OnsetDivisions,5} dur={n.Duration,5}  staff={n.Staff} vs={vs} [{colour}]"
            //                         + $"  midi={n.MidiPitch,3}  {(n.IsRest ? "REST" : $"{n.Pitch}{n.Octave}")}  chord={n.IsChord}");
            //     }
            // }
        }
        LogMessage(diagSb.ToString());

        var canvas = new VerticalPianoRollCanvas(score);

        var statusBlock = new TextBlock
        {
            Text = $"Stopped  |  BPM: {score.DefaultBpm:F0}  |  {score.Title}",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 12,
            Margin = new Thickness(8, 0)
        };

        var bpmSlider = new Slider
        {
            Minimum = 40, Maximum = 300,
            Value   = score.DefaultBpm,
            Width   = 180,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [ToolTip.TipProperty] = "Tempo (BPM)"
        };
        var bpmLabel = new TextBlock
        {
            Text = $"{score.DefaultBpm:F0} BPM",
            Width = 60,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 12
        };
        bpmSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                bpmLabel.Text = $"{bpmSlider.Value:F0} BPM";
        };

        var playBtn = new Button { Content = "▶  Play",  Margin = new Thickness(4), Padding = new Thickness(8, 2) };
        var stopBtn = new Button { Content = "■  Stop",  Margin = new Thickness(4), Padding = new Thickness(8, 2), IsEnabled = false };

        int totalMeasuresV = score.Parts.Max(p => p.Measures.Count);
        var measureSlider  = new Slider
        {
            Minimum = 1, Maximum = Math.Max(1, totalMeasuresV),
            Value   = 1,
            Width   = 260,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [ToolTip.TipProperty] = "Jump to measure (stop first)"
        };
        var measureLabel = new TextBlock
        {
            Text = $"M 1/{totalMeasuresV}",
            Width = 72,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 12
        };
        measureSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                measureLabel.Text = $"M {(int)measureSlider.Value}/{totalMeasuresV}";
        };

        MxlMidiPlayer? player = null;
        bool sliderDrivingV = false;

        var logNotesChk = new CheckBox
        {
            Content = "Log notes",
            IsChecked = false,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            [ToolTip.TipProperty] = "Write every NoteOn to Trace while playing (check before pressing Play, or mid-song)"
        };

        void SetStopped()
        {
            player?.Dispose();
            player = null;
            canvas.CurrentGlobalDivisions = -1;
            statusBlock.Text = "Stopped";
            playBtn.IsEnabled = true;
            stopBtn.IsEnabled = false;
        }

        playBtn.Click += (_, _) =>
        {
            player?.Dispose();
            player = new MxlMidiPlayer(score)
            {
                Bpm          = bpmSlider.Value,
                StartMeasure = (int)measureSlider.Value,
                LogNotes     = logNotesChk.IsChecked == true,
            };

            // Allow toggling mid-playback
            logNotesChk.IsCheckedChanged += (_, _) =>
            {
                if (player != null) player.LogNotes = logNotesChk.IsChecked == true;
            };

            player.PositionChanged += (_, divs) => Dispatcher.UIThread.Post(() =>
            {
                canvas.CurrentGlobalDivisions = divs;
                int measureNo = score.Parts[0].Measures
                    .LastOrDefault(m => m.GlobalOnsetDivisions <= divs)?.Number ?? 1;
                sliderDrivingV = true;
                measureSlider.Value = measureNo;
                sliderDrivingV = false;
                statusBlock.Text = $"Playing ▶  measure {measureNo}  |  {bpmSlider.Value:F0} BPM";
            });

            player.PlaybackEnded += (_, _) => Dispatcher.UIThread.Post(SetStopped);

            playBtn.IsEnabled = false;
            stopBtn.IsEnabled = true;
            statusBlock.Text  = "Playing ▶  ...";
            player.Start();
        };

        stopBtn.Click += (_, _) => SetStopped();

        var toolbar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin = new Thickness(4),
            Children = { playBtn, stopBtn, bpmSlider, bpmLabel, measureSlider, measureLabel, logNotesChk, statusBlock }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(canvas);

        var window = new Window
        {
            Title = $"Vertical Piano Roll — {Path.GetFileName(mxlPath)}",
            Width = 1400,
            Height = 720,
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = layout
        };
        window.Closed += (_, _) => SetStopped();
        window.Opened += (_, _) => playBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        return window;
    }

    private async Task ShowVerticalPianoRollWindowAsync(string mxlPath, MxlScore score) =>
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildVerticalPianoRollWindow(mxlPath, score);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Vertical piano roll closed.");
            window.Show();
            await Task.CompletedTask;
        });

    private async Task ShowRhythmDensityWindowAsync(string mxlPath, MxlScore score) =>
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildRhythmDensityWindow(mxlPath, score);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Rhythm density closed.");
            window.Show();
            await Task.CompletedTask;
        });

    private async Task ShowHandRangeWindowAsync(string mxlPath, MxlScore score) =>
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildHandRangeWindow(mxlPath, score);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Hand range closed.");
            window.Show();
            await Task.CompletedTask;
        });

    private async Task ShowHarmonyTimelineWindowAsync(string mxlPath, MxlScore score) =>
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildHarmonyTimelineWindow(mxlPath, score);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Harmony timeline closed.");
            window.Show();
            await Task.CompletedTask;
        });

    // -----------------------------------------------------------------------
    //  MXL / MusicXML parsing
    // -----------------------------------------------------------------------

    /// <summary>Reads and parses a .mxl (or plain .musicxml/.xml) file into an <see cref="MxlScore"/>.</summary>
    private static MxlScore ParseMxl(string mxlPath)
    {
        if (!File.Exists(mxlPath))
            throw new FileNotFoundException($"MXL not found: {mxlPath}");

        var xml = ExtractXmlFromMxl(mxlPath);
        return MxlScore.Parse(xml);
    }

    /// <summary>
    /// Returns the raw MusicXML text from a .mxl ZIP (or a plain .xml/.musicxml file).
    /// </summary>
    private static string ExtractXmlFromMxl(string filePath)
    {
        if (filePath.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(filePath);
            var scoreEntry = zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                !e.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase));
            if (scoreEntry == null)
                throw new InvalidOperationException($"No score XML entry found inside {filePath}");
            using var reader = new StreamReader(scoreEntry.Open());
            return reader.ReadToEnd();
        }
        return File.ReadAllText(filePath);
    }
}

// ---------------------------------------------------------------------------
//  Lightweight MusicXML object model (used only by the tests above)
// ---------------------------------------------------------------------------

/// <summary>Top-level parsed score.</summary>
internal sealed class MxlScore
{
    public string Title      { get; private set; } = string.Empty;
    public string Composer   { get; private set; } = string.Empty;
    public double DefaultBpm { get; private set; } = 120.0;   // from <sound tempo=""> or 120
    public List<MxlPart> Parts { get; } = new();

    public int TotalMeasures => Parts.Sum(p => p.Measures.Count);
    public int TotalNotes    => Parts.Sum(p => p.NoteCount);
    public int TotalRests    => Parts.Sum(p => p.RestCount);

    /// <summary>
    /// Resolves the "visual staff" (1 = right hand / green, 2 = left hand / blue) for a note.
    /// Piano scores come in two common layouts:
    ///  - Grand-staff (1 part, 2 staves): use note.Staff directly (already 1 or 2).
    ///  - Two-part (2 parts, each staff=1): use part.PartIndex + 1.
    /// We detect which layout is in use by checking whether any note has Staff > 1.
    /// </summary>
    private bool? _isMultiStaff;
    public int VisualStaff(MxlPart part, MxlNote note)
    {
        if (_isMultiStaff == null)
        {
            _isMultiStaff = Parts.Any(p =>
                p.Measures.Any(m => m.Notes.Any(n => !n.IsRest && n.Staff > 1)));
        }
        return _isMultiStaff.Value ? note.Staff : (part.PartIndex + 1);
    }

    public static MxlScore Parse(string xml)
    {
        var score = new MxlScore();
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;
        XNamespace ns = root.Name.Namespace;

        // Title / composer
        score.Title = root.Descendants(ns + "movement-title").FirstOrDefault()?.Value.Trim()
                   ?? root.Descendants(ns + "work-title").FirstOrDefault()?.Value.Trim()
                   ?? string.Empty;
        score.Composer = root.Descendants(ns + "creator")
                             .FirstOrDefault(e => string.Equals(
                                 e.Attribute("type")?.Value, "composer",
                                 StringComparison.OrdinalIgnoreCase))
                             ?.Value.Trim()
                         ?? string.Empty;

        // Tempo from first <sound tempo=""> element
        var soundEl = root.Descendants(ns + "sound")
                          .FirstOrDefault(e => e.Attribute("tempo") != null);
        if (soundEl != null && double.TryParse(soundEl.Attribute("tempo")?.Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedBpm)
            && parsedBpm > 0)
        {
            score.DefaultBpm = parsedBpm;
        }

        // Build part-name map from <part-list>
        var partNames = new Dictionary<string, (string Name, int Midi)>(StringComparer.OrdinalIgnoreCase);
        foreach (var sp in root.Descendants(ns + "score-part"))
        {
            var id   = sp.Attribute("id")?.Value ?? string.Empty;
            var name = sp.Element(ns + "part-name")?.Value.Trim() ?? id;
            var midi = int.TryParse(
                sp.Descendants(ns + "midi-program").FirstOrDefault()?.Value, out var m)
                ? m : 0;
            partNames[id] = (name, midi);
        }

        // Parse each <part>
        int partIndex = 0;
        foreach (var partEl in root.Elements(ns + "part"))
        {
            var partId = partEl.Attribute("id")?.Value ?? string.Empty;
            partNames.TryGetValue(partId, out var nameInfo);

            var part = new MxlPart
            {
                PartId         = partId,
                InstrumentName = nameInfo.Name,
                MidiProgram    = nameInfo.Midi,
                PartIndex      = partIndex++,
            };

            string currentTimeSig = string.Empty;
            string currentKeySig  = string.Empty;
            int    divisions      = 1;    // ticks per quarter note (from <divisions>)
            long   globalOnset    = 0;   // running total across measures

            foreach (var measureEl in partEl.Elements(ns + "measure"))
            {
                var measureNo = int.TryParse(measureEl.Attribute("number")?.Value, out var mn) ? mn : 0;

                // Divisions update (inside <attributes>)
                var divsEl = measureEl.Descendants(ns + "divisions").FirstOrDefault();
                if (divsEl != null && int.TryParse(divsEl.Value, out var newDivs) && newDivs > 0)
                    divisions = newDivs;

                // Time signature change in this measure?
                var timeEl = measureEl.Descendants(ns + "time").FirstOrDefault();
                if (timeEl != null)
                {
                    var beats    = timeEl.Element(ns + "beats")?.Value ?? "?";
                    var beatType = timeEl.Element(ns + "beat-type")?.Value ?? "?";
                    currentTimeSig = $"{beats}/{beatType}";
                }

                // Key signature change?
                var keyEl = measureEl.Descendants(ns + "key").FirstOrDefault();
                if (keyEl != null)
                {
                    var fifths = int.TryParse(keyEl.Element(ns + "fifths")?.Value, out var f) ? f : 0;
                    var mode   = keyEl.Element(ns + "mode")?.Value ?? "major";
                    currentKeySig = $"{KeyName(fifths, mode)}";
                }

                var measure = new MxlMeasure
                {
                    Number               = measureNo,
                    TimeSig              = currentTimeSig,
                    KeySig               = currentKeySig,
                    Divisions            = divisions,
                    GlobalOnsetDivisions = globalOnset,
                };

                int cursor     = 0;  // running onset within this measure (advances with notes, resets on <backup>)
                int lastCursor = 0;  // onset of the most recent non-chord note

                // Iterate ALL child elements so we honour <backup> and <forward> time-cursor moves.
                // MusicXML uses <backup> to rewind the cursor when switching staves/voices within a
                // measure — without it every staff-2 note appears *after* all staff-1 notes instead
                // of alongside them.
                foreach (var child in measureEl.Elements())
                {
                    var localName = child.Name.LocalName;

                    if (localName == "backup")
                    {
                        var backDur = int.TryParse(child.Element(ns + "duration")?.Value, out var bd) ? bd : 0;
                        cursor = Math.Max(0, cursor - backDur);
                        lastCursor = cursor;
                        continue;
                    }

                    if (localName == "forward")
                    {
                        var fwdDur = int.TryParse(child.Element(ns + "duration")?.Value, out var fd) ? fd : 0;
                        cursor += fwdDur;
                        lastCursor = cursor;
                        continue;
                    }

                    if (localName != "note") continue;

                    var noteEl  = child;
                    var isRest  = noteEl.Element(ns + "rest")  != null;
                    var isChord = noteEl.Element(ns + "chord") != null;
                    var dur     = int.TryParse(noteEl.Element(ns + "duration")?.Value, out var d) ? d : 0;

                    // Chord notes share the onset of the preceding non-chord note
                    int onset = isChord ? lastCursor : cursor;

                    string pitch = string.Empty, octave = string.Empty, accidental = string.Empty;
                    int pitchAlter = 0;
                    if (!isRest)
                    {
                        var pitchEl = noteEl.Element(ns + "pitch");
                        pitch      = pitchEl?.Element(ns + "step")?.Value ?? string.Empty;
                        octave     = pitchEl?.Element(ns + "octave")?.Value ?? string.Empty;
                        accidental = noteEl.Element(ns + "accidental")?.Value ?? string.Empty;
                        // <alter> is the authoritative chromatic offset (includes key-sig sharps/flats).
                        // Parse as double first (MusicXML allows 0.5 for quarter-tones) then round.
                        if (double.TryParse(pitchEl?.Element(ns + "alter")?.Value,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var alterD))
                            pitchAlter = (int)Math.Round(alterD);
                    }

                    var note = new MxlNote
                    {
                        IsRest        = isRest,
                        IsChord       = isChord,
                        Pitch         = pitch,
                        Octave        = octave,
                        Accidental    = accidental,
                        PitchAlter    = pitchAlter,
                        Duration      = dur,
                        OnsetDivisions= onset,
                        NoteType      = noteEl.Element(ns + "type")?.Value ?? string.Empty,
                        Dots          = noteEl.Elements(ns + "dot").Count(),
                        Staff         = int.TryParse(noteEl.Element(ns + "staff")?.Value, out var st) ? st : 1,
                        Voice         = int.TryParse(noteEl.Element(ns + "voice")?.Value, out var v) ? v : 1,
                    };

                    measure.Notes.Add(note);
                    if (isChord)
                    {
                        measure.ChordCount++;
                    }
                    else
                    {
                        lastCursor = cursor;
                        cursor    += dur;
                    }
                }

                // Advance the global onset by the true measure duration.
                // After <backup> elements the cursor may be behind the furthest point
                // reached, so track the high-water mark separately.
                int measureDuration = measure.Notes
                    .Where(n => !n.IsChord)
                    .Select(n => n.OnsetDivisions + n.Duration)
                    .DefaultIfEmpty(0)
                    .Max();
                globalOnset += measureDuration;
                part.Measures.Add(measure);
            }

            score.Parts.Add(part);
        }

        return score;
    }

    private static readonly string[] _sharpKeys = { "C", "G", "D", "A", "E", "B", "F#", "C#" };
    private static readonly string[] _flatKeys  = { "C", "F", "Bb", "Eb", "Ab", "Db", "Gb", "Cb" };

    private static string KeyName(int fifths, string mode)
    {
        var tonic = fifths >= 0
            ? _sharpKeys[Math.Min(fifths, 7)]
            : _flatKeys[Math.Min(-fifths, 7)];
        return $"{tonic} {mode}";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Custom Avalonia canvas controls
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Renders a piano-roll grid: time (measures) on the X axis, MIDI pitch on Y.
/// Staff 1 (treble/right hand) = green; staff 2 (bass/left hand) = blue.
/// </summary>
internal class PianoRollCanvas : Control
{
    protected static readonly int MinMidi      = 21;
    protected static readonly int MaxMidi      = 108;
    protected static readonly int KeyH         = 5;
    protected static readonly int MeasureW     = 80;
    protected static readonly int YAxisW       = 32;
    private   static readonly int[] BlackKeys  = { 1, 3, 6, 8, 10 };

    protected readonly MxlScore _score;
    protected readonly int    _totalMeasures;
    protected readonly double _canvasW;
    protected readonly double _canvasH;

    public PianoRollCanvas(MxlScore score)
    {
        _score         = score;
        _totalMeasures = score.Parts.Max(p => p.Measures.Count);
        _canvasW       = YAxisW + _totalMeasures * MeasureW;
        _canvasH       = (MaxMidi - MinMidi + 1) * KeyH;
    }

    protected override Size MeasureOverride(Size _) => new(_canvasW, _canvasH);

    /// <summary>Maps a global-divisions offset to the canvas X coordinate.</summary>
    protected double DivisionsToX(long globalDivs)
    {
        // Walk all measures of the first part to find which measure contains globalDivs
        var parts = _score.Parts;
        if (parts.Count == 0) return YAxisW;
        foreach (var measure in parts[0].Measures)
        {
            int divs  = Math.Max(1, measure.Divisions);
            long mEnd = measure.GlobalOnsetDivisions + divs * 4;  // approx measure end
            if (globalDivs >= measure.GlobalOnsetDivisions && globalDivs < mEnd)
            {
                double frac = (double)(globalDivs - measure.GlobalOnsetDivisions) / (divs * 4);
                return YAxisW + (measure.Number - 1) * MeasureW + frac * MeasureW;
            }
        }
        return YAxisW + _totalMeasures * MeasureW;
    }

    /// <summary>Called at the end of Render for subclasses to draw overlays.</summary>
    protected virtual void RenderOverlay(DrawingContext ctx) { }

    public override void Render(DrawingContext ctx)
    {
        var bg        = new SolidColorBrush(Color.FromRgb(28, 28, 28));
        var blackKeyB = new SolidColorBrush(Color.FromRgb(48, 48, 48));
        var gridPen   = new Pen(new SolidColorBrush(Color.FromRgb(55, 55, 55)), 0.5);
        var octavePen = new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 90)), 0.8);
        var measurePen= new Pen(new SolidColorBrush(Color.FromRgb(65, 65, 65)), 0.5);
        var labelBrush= new SolidColorBrush(Color.FromRgb(140, 140, 140));
        var tf        = new Typeface("Consolas");

        ctx.FillRectangle(bg, new Rect(0, 0, _canvasW, _canvasH));

        // Pitch rows
        for (int midi = MinMidi; midi <= MaxMidi; midi++)
        {
            double y = _canvasH - (midi - MinMidi + 1) * KeyH;
            if (BlackKeys.Contains(midi % 12))
                ctx.FillRectangle(blackKeyB, new Rect(0, y, _canvasW, KeyH));

            if (midi % 12 == 0)  // every C
            {
                ctx.DrawLine(octavePen, new Point(YAxisW, y), new Point(_canvasW, y));
                var ft = new FormattedText($"C{midi / 12 - 1}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, tf, 8, labelBrush);
                ctx.DrawText(ft, new Point(1, y - 8));
            }
            else
            {
                ctx.DrawLine(gridPen, new Point(YAxisW, y + KeyH - 1), new Point(_canvasW, y + KeyH - 1));
            }
        }

        // Measure grid lines
        for (int m = 0; m <= _totalMeasures; m++)
        {
            double x = YAxisW + m * MeasureW;
            ctx.DrawLine(measurePen, new Point(x, 0), new Point(x, _canvasH));
            if (m > 0 && m % 4 == 0)
            {
                var ft = new FormattedText($"{m + 1}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, tf, 8, labelBrush);
                ctx.DrawText(ft, new Point(x + 1, 1));
            }
        }

        // Notes — split each pitch row top/bottom by staff so both are visible at the same pitch.
        // Staff 1 (right hand) → top half of the row (green)
        // Staff 2 (left hand)  → bottom half of the row (blue)
        var staff1Brush = new SolidColorBrush(Color.FromArgb(220, 64, 192, 87));   // green
        var staff2Brush = new SolidColorBrush(Color.FromArgb(220, 88, 130, 226));  // blue
        var otherBrush  = new SolidColorBrush(Color.FromArgb(200, 200, 180, 80));  // amber

        int halfH = Math.Max(1, (KeyH - 2) / 2);

        foreach (var part in _score.Parts)
        foreach (var measure in part.Measures)
        foreach (var note in measure.Notes)
        {
            if (note.IsRest || note.MidiPitch < MinMidi || note.MidiPitch > MaxMidi) continue;

            int divs = Math.Max(1, measure.Divisions);
            double xFrac   = (double)note.OnsetDivisions / (divs * 4);
            double wFrac   = (double)note.Duration        / (divs * 4);
            double x       = YAxisW + (measure.Number - 1) * MeasureW + xFrac * MeasureW;
            double w       = Math.Max(1.5, wFrac * MeasureW - 1);
            double rowTop  = _canvasH - (note.MidiPitch - MinMidi + 1) * KeyH + 1;

            int visualStaff = _score.VisualStaff(part, note);
            double ny, nh;
            IBrush brush;
            if (visualStaff == 1)      { ny = rowTop;         nh = halfH;     brush = staff1Brush; }
            else if (visualStaff == 2) { ny = rowTop + halfH; nh = halfH;     brush = staff2Brush; }
            else                       { ny = rowTop;         nh = KeyH - 2;  brush = otherBrush; }

            ctx.FillRectangle(brush, new Rect(x, ny, w, nh));
        }

        RenderOverlay(ctx);
    }
}

/// <summary>
/// Bar chart showing note density across 16 sub-beat positions (16th-note grid)
/// within a measure, aggregated over the whole piece, per staff.
/// </summary>
internal sealed class RhythmDensityCanvas : Control
{
    private const int Slots       = 16;   // 16th-note positions per measure
    private const int BarW        = 40;
    private const int LabelH      = 24;
    private const int LegendH     = 20;
    private readonly MxlScore _score;
    private readonly int[]   _staff1 = new int[Slots];
    private readonly int[]   _staff2 = new int[Slots];
    private readonly double  _canvasW;
    private readonly double  _canvasH = 420;

    public RhythmDensityCanvas(MxlScore score)
    {
        _score  = score;
        _canvasW = Slots * BarW + 60;
        BuildHistogram();
    }

    private void BuildHistogram()
    {
        foreach (var part in _score.Parts)
        foreach (var measure in part.Measures)
        {
            int divs = Math.Max(1, measure.Divisions);
            int divsPer16th = divs / 4;  // divisions per 16th note (quarter = divs)
            if (divsPer16th < 1) divsPer16th = 1;

            foreach (var note in measure.Notes)
            {
                if (note.IsRest || note.IsChord) continue;
                int slot = (int)Math.Round((double)note.OnsetDivisions / divsPer16th) % Slots;
                if (slot < 0) slot = 0;
                if (note.Staff == 1) _staff1[slot]++;
                else                  _staff2[slot]++;
            }
        }
    }

    protected override Size MeasureOverride(Size _) => new(_canvasW, _canvasH);

    public override void Render(DrawingContext ctx)
    {
        var bg       = new SolidColorBrush(Color.FromRgb(28, 28, 28));
        var gridPen  = new Pen(new SolidColorBrush(Color.FromRgb(60, 60, 60)), 0.5);
        var axispen  = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1);
        var s1Brush  = new SolidColorBrush(Color.FromArgb(220, 64, 192, 87));
        var s2Brush  = new SolidColorBrush(Color.FromArgb(220, 88, 130, 226));
        var labelBr  = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        var tf       = new Typeface("Consolas");

        ctx.FillRectangle(bg, new Rect(0, 0, _canvasW, _canvasH));

        int maxCount = Math.Max(1, _staff1.Concat(_staff2).Max());
        double chartH = _canvasH - LabelH - LegendH - 10;
        double xOff   = 30;

        // Axis
        ctx.DrawLine(axispen, new Point(xOff, 0), new Point(xOff, _canvasH - LabelH - LegendH));
        ctx.DrawLine(axispen, new Point(xOff, _canvasH - LabelH - LegendH),
            new Point(_canvasW - 4, _canvasH - LabelH - LegendH));

        for (int i = 0; i < Slots; i++)
        {
            double x = xOff + i * BarW;

            // Grid line
            ctx.DrawLine(gridPen, new Point(x, 0), new Point(x, _canvasH - LabelH - LegendH));

            // Staff 2 bar (behind)
            double h2 = chartH * _staff2[i] / maxCount;
            ctx.FillRectangle(s2Brush, new Rect(x + 2, _canvasH - LabelH - LegendH - h2, BarW / 2.0 - 2, h2));

            // Staff 1 bar (front)
            double h1 = chartH * _staff1[i] / maxCount;
            ctx.FillRectangle(s1Brush, new Rect(x + BarW / 2.0, _canvasH - LabelH - LegendH - h1, BarW / 2.0 - 2, h1));

            // Beat label (1, 1e, 1+, 1a, 2, ...)
            string[] beatNames = { "1","e","+","a","2","e","+","a","3","e","+","a","4","e","+","a" };
            var ft = new FormattedText(beatNames[i], CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, 10, i % 4 == 0 ? labelBr : new SolidColorBrush(Color.FromRgb(100,100,100)));
            ctx.DrawText(ft, new Point(x + BarW / 4.0, _canvasH - LabelH - LegendH + 4));
        }

        // Legend
        double ly = _canvasH - LegendH + 2;
        ctx.FillRectangle(s1Brush, new Rect(xOff,        ly, 14, 10));
        ctx.DrawText(new FormattedText("Staff 1 (treble)", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf, 10, labelBr), new Point(xOff + 18, ly));
        ctx.FillRectangle(s2Brush, new Rect(xOff + 150,  ly, 14, 10));
        ctx.DrawText(new FormattedText("Staff 2 (bass)", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf, 10, labelBr), new Point(xOff + 168, ly));
    }
}

/// <summary>
/// For each measure draws a vertical line showing the pitch range (min→max MIDI note)
/// used by each staff — a quick view of how each hand moves across the keyboard.
/// </summary>
internal sealed class HandRangeCanvas : Control
{
    private static readonly int MinMidi  = 21;
    private static readonly int MaxMidi  = 108;
    private static readonly int MeasureW = 10;   // px per measure
    private static readonly int YAxisW   = 36;
    private readonly MxlScore _score;
    private readonly int _totalMeasures;
    private readonly double _canvasW;
    private readonly double _canvasH;

    public HandRangeCanvas(MxlScore score)
    {
        _score         = score;
        _totalMeasures = score.Parts.Max(p => p.Measures.Count);
        _canvasW       = YAxisW + _totalMeasures * MeasureW;
        _canvasH       = (MaxMidi - MinMidi + 1) * 4.0 + 20;
    }

    private double PitchY(int midi) => _canvasH - 20 - (midi - MinMidi) * 4.0;

    protected override Size MeasureOverride(Size _) => new(_canvasW, _canvasH);

    public override void Render(DrawingContext ctx)
    {
        var bg       = new SolidColorBrush(Color.FromRgb(28, 28, 28));
        var gridPen  = new Pen(new SolidColorBrush(Color.FromRgb(50, 50, 50)), 0.3);
        var octavePen= new Pen(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 0.5);
        var s1Pen    = new Pen(new SolidColorBrush(Color.FromArgb(210, 64, 192, 87)),  2.5);
        var s2Pen    = new Pen(new SolidColorBrush(Color.FromArgb(210, 88, 130, 226)), 2.5);
        var labelBr  = new SolidColorBrush(Color.FromRgb(130, 130, 130));
        var tf       = new Typeface("Consolas");

        ctx.FillRectangle(bg, new Rect(0, 0, _canvasW, _canvasH));

        // Octave grid lines
        for (int midi = MinMidi; midi <= MaxMidi; midi++)
        {
            if (midi % 12 != 0) continue;
            double y = PitchY(midi);
            ctx.DrawLine(octavePen, new Point(YAxisW, y), new Point(_canvasW, y));
            var ft = new FormattedText($"C{midi / 12 - 1}", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, 8, labelBr);
            ctx.DrawText(ft, new Point(1, y - 8));
        }

        // Measure bars every 4
        for (int m = 0; m < _totalMeasures; m += 4)
        {
            double x = YAxisW + m * MeasureW;
            ctx.DrawLine(gridPen, new Point(x, 0), new Point(x, _canvasH));
        }

        // Per-measure hand range lines
        foreach (var part in _score.Parts)
        foreach (var measure in part.Measures)
        {
            double x = YAxisW + (measure.Number - 1) * MeasureW + MeasureW / 2.0;

            foreach (var staffGroup in measure.Notes.Where(n => !n.IsRest && n.MidiPitch >= MinMidi && n.MidiPitch <= MaxMidi)
                                                    .GroupBy(n => n.Staff))
            {
                int minP = staffGroup.Min(n => n.MidiPitch);
                int maxP = staffGroup.Max(n => n.MidiPitch);
                var pen  = staffGroup.Key == 1 ? s1Pen : s2Pen;
                double xOffset = staffGroup.Key == 1 ? -1.5 : 1.5;
                ctx.DrawLine(pen,
                    new Point(x + xOffset, PitchY(minP)),
                    new Point(x + xOffset, PitchY(maxP)));
            }
        }

        // Legend
        var s1Br = new SolidColorBrush(Color.FromArgb(210, 64, 192, 87));
        var s2Br = new SolidColorBrush(Color.FromArgb(210, 88, 130, 226));
        var wBr  = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        ctx.FillRectangle(s1Br, new Rect(YAxisW, _canvasH - 16, 12, 8));
        ctx.DrawText(new FormattedText("Staff 1 (treble)", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf, 9, wBr), new Point(YAxisW + 16, _canvasH - 16));
        ctx.FillRectangle(s2Br, new Rect(YAxisW + 140, _canvasH - 16, 12, 8));
        ctx.DrawText(new FormattedText("Staff 2 (bass)", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf, 9, wBr), new Point(YAxisW + 156, _canvasH - 16));
    }
}

/// <summary>
/// Harmony timeline: for every beat across the piece, finds the lowest
/// non-rest note and colours a cell by its pitch class (chromatic root),
/// giving a compact view of harmonic motion.
/// </summary>
internal sealed class HarmonyTimelineCanvas : Control
{
    private const int CellW   = 12;   // px per beat
    private const int CellH   = 18;   // px per pitch class row
    private const int YAxisW  = 26;
    private const int XAxisH  = 16;
    private static readonly string[] PitchNames = { "C","C#","D","Eb","E","F","F#","G","Ab","A","Bb","B" };
    // Colour wheel: each pitch class gets a distinct hue
    private static readonly Color[] PcColors =
    {
        Color.FromRgb(220, 60,  60),   // C  red
        Color.FromRgb(220, 120, 60),   // C#
        Color.FromRgb(220, 185, 60),   // D  yellow-orange
        Color.FromRgb(160, 210, 60),   // Eb
        Color.FromRgb(80,  200, 80),   // E  green
        Color.FromRgb(60,  200, 160),  // F
        Color.FromRgb(60,  180, 220),  // F# cyan
        Color.FromRgb(60,  100, 220),  // G  blue
        Color.FromRgb(100, 60,  220),  // Ab
        Color.FromRgb(160, 60,  220),  // A  purple
        Color.FromRgb(210, 60,  180),  // Bb
        Color.FromRgb(220, 60,  110),  // B
    };

    private readonly MxlScore _score;
    // beat → (pitch class, count) – lowest note determines pc
    private readonly List<(int Beat, int Pc)> _beatPcs = new();
    private readonly int _totalBeats;
    private readonly double _canvasW;
    private readonly double _canvasH = 12 * CellH + XAxisH;

    public HarmonyTimelineCanvas(MxlScore score)
    {
        _score = score;
        BuildBeatMap();
        _totalBeats = _beatPcs.Count > 0 ? _beatPcs.Max(b => b.Beat) + 1 : 1;
        _canvasW    = YAxisW + _totalBeats * CellW;
    }

    private void BuildBeatMap()
    {
        // For each part, each measure: group notes by beat (onset / divisions) → lowest midi
        var beatLowest = new Dictionary<int, int>(); // global beat → lowest midi

        foreach (var part in _score.Parts)
        {
            int beatsAccum = 0;
            foreach (var measure in part.Measures)
            {
                int divs = Math.Max(1, measure.Divisions);
                // Parse beats from TimeSig (e.g. "4/4" → 4)
                int beatsPerMeasure = 4;
                if (measure.TimeSig.Contains('/'))
                    int.TryParse(measure.TimeSig.Split('/')[0], out beatsPerMeasure);

                foreach (var note in measure.Notes)
                {
                    if (note.IsRest || note.MidiPitch == 0) continue;
                    int beat = beatsAccum + note.OnsetDivisions / divs;
                    if (!beatLowest.TryGetValue(beat, out int cur) || note.MidiPitch < cur)
                        beatLowest[beat] = note.MidiPitch;
                }
                beatsAccum += beatsPerMeasure;
            }
        }

        foreach (var (beat, midi) in beatLowest.OrderBy(kv => kv.Key))
            _beatPcs.Add((beat, midi % 12));
    }

    protected override Size MeasureOverride(Size _) => new(_canvasW, _canvasH);

    public override void Render(DrawingContext ctx)
    {
        var bg      = new SolidColorBrush(Color.FromRgb(28, 28, 28));
        var labelBr = new SolidColorBrush(Color.FromRgb(160, 160, 160));
        var emptyBr = new SolidColorBrush(Color.FromRgb(45,  45,  45));
        var tf      = new Typeface("Consolas");

        ctx.FillRectangle(bg, new Rect(0, 0, _canvasW, _canvasH));

        // Row labels (pitch classes)
        for (int pc = 0; pc < 12; pc++)
        {
            double y = pc * CellH;
            var ft = new FormattedText(PitchNames[11 - pc], CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, 9, new SolidColorBrush(PcColors[11 - pc]));
            ctx.DrawText(ft, new Point(1, y + 2));
        }

        // Empty grid
        for (int beat = 0; beat < _totalBeats; beat++)
        for (int pc = 0; pc < 12; pc++)
        {
            double x = YAxisW + beat * CellW;
            double y = pc * CellH;
            ctx.FillRectangle(emptyBr, new Rect(x + 0.5, y + 0.5, CellW - 1, CellH - 1));
        }

        // Filled cells
        foreach (var (beat, pc) in _beatPcs)
        {
            double x = YAxisW + beat * CellW;
            double y = (11 - pc) * CellH;
            ctx.FillRectangle(new SolidColorBrush(PcColors[pc]),
                new Rect(x + 0.5, y + 0.5, CellW - 1, CellH - 1));
        }

        // Beat axis labels (every 4 beats)
        for (int beat = 0; beat < _totalBeats; beat += 4)
        {
            double x = YAxisW + beat * CellW;
            var ft = new FormattedText($"{beat + 1}", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, 8, labelBr);
            ctx.DrawText(ft, new Point(x + 1, 12 * CellH + 2));
        }
    }
}

internal sealed class MxlPart
{
    public string PartId         { get; set; } = string.Empty;
    public string InstrumentName { get; set; } = string.Empty;
    public int    MidiProgram    { get; set; }
    public int    PartIndex      { get; set; }   // 0-based index in the score's part list
    public List<MxlMeasure> Measures { get; } = new();
    public int NoteCount => Measures.Sum(m => m.NoteCount);
    public int RestCount => Measures.Sum(m => m.RestCount);
}

internal sealed class MxlMeasure
{
    public int    Number                { get; set; }
    public string TimeSig               { get; set; } = string.Empty;
    public string KeySig                { get; set; } = string.Empty;
    public int    Divisions             { get; set; } = 1;  // ticks per quarter note
    public long   GlobalOnsetDivisions  { get; set; }       // absolute onset from start of score
    public List<MxlNote> Notes { get; } = new();
    public int ChordCount   { get; set; }
    public int NoteCount    => Notes.Count(n => !n.IsRest);
    public int RestCount    => Notes.Count(n => n.IsRest);
}

internal sealed class MxlNote
{
    public bool   IsRest          { get; set; }
    public bool   IsChord         { get; set; }
    public string Pitch           { get; set; } = string.Empty;
    public string Octave          { get; set; } = string.Empty;
    public string Accidental      { get; set; } = string.Empty;  // display accidental (for labels only)
    public int    PitchAlter      { get; set; }                  // chromatic offset from <pitch><alter> — correct for key sig
    public int    Duration        { get; set; }  // in divisions
    public int    OnsetDivisions  { get; set; }  // offset from start of measure, in divisions
    public string NoteType        { get; set; } = string.Empty;
    public int    Dots            { get; set; }
    public int    Staff           { get; set; }
    public int    Voice           { get; set; }

    /// <summary>MIDI pitch number (21=A0 … 108=C8). Returns 0 for rests.</summary>
    public int MidiPitch
    {
        get
        {
            if (IsRest || string.IsNullOrEmpty(Pitch)) return 0;
            int step = Pitch switch
            {
                "C" => 0, "D" => 2, "E" => 4, "F" => 5,
                "G" => 7, "A" => 9, "B" => 11, _ => 0
            };
            // PitchAlter comes from <pitch><alter> which includes key-signature sharps/flats.
            // Accidental is only the *displayed* symbol and is absent for key-sig-implied alterations.
            return int.TryParse(Octave, out var oct) ? 12 * (oct + 1) + step + PitchAlter : 0;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Extends <see cref="PianoRollCanvas"/> with a red playhead cursor.
/// Set <see cref="CurrentGlobalDivisions"/> from the MIDI player to animate it.
/// </summary>
internal sealed class PlayablePianoRollCanvas : PianoRollCanvas
{
    private long _currentGlobalDivisions = -1;

    /// <summary>Fired (from any thread) when the playhead X pixel changes.</summary>
    public event Action<double>? PlayheadXChanged;

    public long CurrentGlobalDivisions
    {
        get => _currentGlobalDivisions;
        set
        {
            _currentGlobalDivisions = value;
            if (value >= 0)
                PlayheadXChanged?.Invoke(DivisionsToX(value));
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    public PlayablePianoRollCanvas(MxlScore score) : base(score) { }

    protected override void RenderOverlay(DrawingContext ctx)
    {
        if (_currentGlobalDivisions < 0) return;
        double x = DivisionsToX(_currentGlobalDivisions);
        ctx.DrawLine(new Pen(Brushes.Red, 2), new Point(x, 0), new Point(x, _canvasH));
        // Semi-transparent "now" band
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 80, 80)),
            new Rect(x - 1, 0, MeasureW / 2.0, _canvasH));
    }
}

/// <summary>
/// Plays an <see cref="MxlScore"/> through the Windows MIDI synthesizer (winmm.dll).
/// Fires <see cref="PositionChanged"/> with the current global-divisions offset so a
/// <see cref="PlayablePianoRollCanvas"/> can track playback in real time.
/// </summary>
internal sealed class MxlMidiPlayer : IDisposable
{
    [DllImport("winmm.dll")] static extern int midiOutOpen(out IntPtr hmo, int uDeviceID, IntPtr dwCallback, IntPtr dwInstance, int fdwOpen);
    [DllImport("winmm.dll")] static extern int midiOutShortMsg(IntPtr hmo, uint dwMsg);
    [DllImport("winmm.dll")] static extern int midiOutClose(IntPtr hmo);

    private static uint NoteOn (int ch, int n, int v) => (uint)((0x90 | ch) | (n << 8) | (v << 16));
    private static uint NoteOff(int ch, int n)        => (uint)((0x80 | ch) | (n << 8));
    private static uint ProgChg(int ch, int p)        => (uint)((0xC0 | ch) | (p << 8));
    private static uint AllOff (int ch)               => (uint)((0xB0 | ch) | (123 << 8));

    private readonly MxlScore _score;
    private IntPtr _handle = IntPtr.Zero;
    private CancellationTokenSource? _cts;

    public double Bpm { get; set; } = 120.0;
    /// <summary>Start playback from this measure number (1-based). Events before it are skipped.</summary>
    public int StartMeasure { get; set; } = 1;
    /// <summary>When true, every NoteOn is written to Trace with measure/pitch/staff detail.</summary>
    public bool LogNotes { get; set; } = false;

    /// <summary>Fired on the playback thread with the current global-divisions offset.</summary>
    public event EventHandler<long>? PositionChanged;
    /// <summary>Fired on the playback thread when playback finishes naturally.</summary>
    public event EventHandler? PlaybackEnded;

    public MxlMidiPlayer(MxlScore score) { _score = score; }

    public void Start()
    {
        Stop();
        if (midiOutOpen(out _handle, -1, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            throw new InvalidOperationException("Could not open MIDI output device.");
        _cts = new CancellationTokenSource();
        _ = PlayAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        if (_handle != IntPtr.Zero)
        {
            for (int ch = 0; ch < 16; ch++) midiOutShortMsg(_handle, AllOff(ch));
            midiOutClose(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private record struct MidiEvent(long TimeMs, uint Message, long GlobalDivisions,
        int MeasureNo = 0, string NoteName = "", int MidiNote = 0, int Staff = 0, int Voice = 0);

    private async Task PlayAsync(CancellationToken ct)
    {
        var events = new List<MidiEvent>();

        // Program-change events: one channel per part (channels 0-14; skip 9=drums)
        int ChannelFor(int partIndex) => partIndex >= 9 ? partIndex + 1 : partIndex;

        for (int pi = 0; pi < _score.Parts.Count && pi < 15; pi++)
        {
            int prog = Math.Clamp(_score.Parts[pi].MidiProgram - 1, 0, 127);
            events.Add(new MidiEvent(0, ProgChg(ChannelFor(pi), prog), 0));
        }

        // Note events
        for (int pi = 0; pi < _score.Parts.Count && pi < 15; pi++)
        {
            int ch = ChannelFor(pi);
            double measureStartMs = 0;  // accumulated wall-clock start of each measure

            foreach (var measure in _score.Parts[pi].Measures)
            {
                int    divs     = Math.Max(1, measure.Divisions);
                double msPerDiv = 60_000.0 / (Bpm * divs);
                int    cursorAdvance = 0;  // how far the non-chord cursor moved this measure

                foreach (var note in measure.Notes)
                {
                    if (note.IsRest || note.MidiPitch == 0) continue;

                    int  midi       = Math.Clamp(note.MidiPitch, 0, 127);
                    long globalDivs = measure.GlobalOnsetDivisions + note.OnsetDivisions;
                    string noteName = $"{note.Pitch}{note.Octave}{(string.IsNullOrEmpty(note.Accidental) ? "" : $"({note.Accidental})")}";

                    // Use measureStartMs + local onset so GlobalOnsetDivisions mixing is avoided
                    long onsetMs = (long)(measureStartMs + note.OnsetDivisions * msPerDiv);
                    long offMs   = onsetMs + Math.Max(30, (long)(note.Duration * msPerDiv) - 15);

                    events.Add(new MidiEvent(onsetMs, NoteOn(ch, midi, 72), globalDivs,
                        MeasureNo: measure.Number, NoteName: noteName, MidiNote: midi,
                        Staff: note.Staff, Voice: note.Voice));
                    events.Add(new MidiEvent(offMs, NoteOff(ch, midi), globalDivs));

                    // Track the furthest non-chord note for measure duration
                    if (!note.IsChord)
                        cursorAdvance = Math.Max(cursorAdvance, note.OnsetDivisions + note.Duration);
                }

                measureStartMs += cursorAdvance * msPerDiv;
            }
        }

        events.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));

        // Seek: drop events before the requested start measure and shift timestamps
        long seekDivs = 0;
        long seekMs   = 0;
        if (StartMeasure > 1 && _score.Parts.Count > 0)
        {
            var startMeasure = _score.Parts[0].Measures
                .FirstOrDefault(m => m.Number >= StartMeasure);
            if (startMeasure != null)
            {
                seekDivs = startMeasure.GlobalOnsetDivisions;
                // Find the earliest NoteOn at/after that measure to determine the ms offset
                var firstAfter = events.FirstOrDefault(e =>
                    (e.Message & 0xF0) == 0x90 && e.GlobalDivisions >= seekDivs);
                seekMs = firstAfter.TimeMs;
            }
        }
        var playEvents = events
            .Where(e => e.TimeMs >= seekMs)
            .Select(e => e with { TimeMs = e.TimeMs - seekMs })
            .ToList();

        // -- MIDI event dump (first 3 s) commented out — re-enable when debugging timing --
        // var dumpSb = new System.Text.StringBuilder();
        // dumpSb.AppendLine("MxlMidiPlayer — first 3000 ms:");
        // foreach (var ev in playEvents.Where(e => e.TimeMs <= 3000)) { ... }
        // System.Diagnostics.Trace.WriteLine(dumpSb.ToString());

        var start     = DateTimeOffset.UtcNow;
        long lastDivs = -1;

        // Fire an immediate position update so the canvas/slider shows where we started
        PositionChanged?.Invoke(this, seekDivs);

        try
        {
            foreach (var ev in playEvents)
            {
                if (ct.IsCancellationRequested) return;
                long elapsed = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                int  wait    = (int)(ev.TimeMs - elapsed);
                if (wait > 1) await Task.Delay(wait, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;

                midiOutShortMsg(_handle, ev.Message);

                // Per-note logging (toggled via checkbox in the UI)
                if (LogNotes && (ev.Message & 0xF0) == 0x90 && ev.MeasureNo > 0)
                    System.Diagnostics.Trace.WriteLine(
                        $"NOTE  m={ev.MeasureNo,4}  midi={ev.MidiNote,3}  {ev.NoteName,-8}  staff={ev.Staff}  voice={ev.Voice}  t={ev.TimeMs,7} ms");

                if (ev.GlobalDivisions != lastDivs)
                {
                    lastDivs = ev.GlobalDivisions;
                    PositionChanged?.Invoke(this, lastDivs);
                }
            }
        }
        catch (OperationCanceledException) { return; }

        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stop();
}

// ─────────────────────────────────────────────────────────────────────────────
/// <summary>
/// Vertical "falling notes" piano roll with a keyboard at the bottom.
/// <para>
/// The piano keyboard spans the full width; white keys are wider than black keys.
/// Note bars drop toward their key from above.  As the playhead reaches a note,
/// the corresponding piano key is highlighted (green = staff 1, blue = staff 2).
/// Set <see cref="CurrentGlobalDivisions"/> from <see cref="MxlMidiPlayer"/> to
/// animate playback; when negative the view shows a static preview of the first
/// few seconds of the score.
/// </para>
/// </summary>
internal sealed class VerticalPianoRollCanvas : Control
{
    // ── MIDI range to display ──────────────────────────────────────────────
    private static readonly int MinMidi = 21;   // A0
    private static readonly int MaxMidi = 108;  // C8
    private static readonly int KeyCount = MaxMidi - MinMidi + 1;

    // ── White-key layout helpers ───────────────────────────────────────────
    // Which pitch-classes within an octave are black keys
    private static readonly HashSet<int> BlackPitchClass = new() { 1, 3, 6, 8, 10 };

    // For every MIDI note in [MinMidi..MaxMidi], compute the white-key index
    // (i.e. how many white keys precede it) and whether it is a black key.
    private static readonly (int whiteIndex, bool isBlack)[] KeyLayout;

    static VerticalPianoRollCanvas()
    {
        KeyLayout = new (int, bool)[KeyCount];
        int whites = 0;
        for (int m = MinMidi; m <= MaxMidi; m++)
        {
            bool black = BlackPitchClass.Contains(m % 12);
            KeyLayout[m - MinMidi] = (whites, black);
            if (!black) whites++;
        }
    }

    // ── Sizing ────────────────────────────────────────────────────────────
    private const double KeyboardH    = 120;  // pixel height of the keyboard section
    private const double WhiteKeyW    = 14;
    private const double BlackKeyW    = 8;
    private const double BlackKeyH    = 70;   // fraction of keyboard height for black keys
    private const double LookaheadSec = 4.0;  // seconds of notes visible above the keyboard

    // Total white-key count across the display range
    private readonly int _totalWhiteKeys;
    private readonly double _canvasW;

    // Pre-built note render list: pitch → x-centre on keyboard
    private sealed record NoteBar(double X, double W, long GlobalOnset, long GlobalOff,
                                  int MidiPitch, int Staff, bool IsBlack);
    private readonly List<NoteBar> _bars = new();

    // Playback state (set from outside on UI thread)
    private long _currentGlobalDivisions = -1;
    private double _divisionsPerPixelTime; // globalDivs per pixel-height in the scroll zone
    private readonly MxlScore _score;

    // Precomputed: globalDivisions → seconds mapping (linear: bpm-independent at parse time)
    // We store globalDivisions per quarter note for the whole score (first part, first measure)
    private readonly int _divsPerQuarter;

    public long CurrentGlobalDivisions
    {
        get => _currentGlobalDivisions;
        set { _currentGlobalDivisions = value; InvalidateVisual(); }
    }

    public VerticalPianoRollCanvas(MxlScore score)
    {
        _score = score;

        // Count white keys
        int w = 0;
        for (int m = MinMidi; m <= MaxMidi; m++)
            if (!BlackPitchClass.Contains(m % 12)) w++;
        _totalWhiteKeys = w;
        _canvasW = _totalWhiteKeys * WhiteKeyW;

        _divsPerQuarter = score.Parts.Count > 0 && score.Parts[0].Measures.Count > 0
            ? Math.Max(1, score.Parts[0].Measures[0].Divisions)
            : 480;

        BuildBars();
    }

    protected override Size MeasureOverride(Size _) =>
        new Size(_canvasW, double.IsInfinity(_.Height) ? 720 : _.Height);

    // Convert white-key pixel X for a given MIDI pitch
    private double MidiToX(int midi)
    {
        if (midi < MinMidi || midi > MaxMidi) return -1;
        var (wi, isBlack) = KeyLayout[midi - MinMidi];
        if (!isBlack)
            return wi * WhiteKeyW + WhiteKeyW / 2.0;   // centre of white key
        // Black key sits between two white keys
        return wi * WhiteKeyW + WhiteKeyW - BlackKeyW / 2.0;
    }

    private double MidiToWidth(int midi) =>
        BlackPitchClass.Contains(midi % 12) ? BlackKeyW - 1 : WhiteKeyW - 1;

    private void BuildBars()
    {
        foreach (var part in _score.Parts)
        foreach (var measure in part.Measures)
        {
            int divs = Math.Max(1, measure.Divisions);
            foreach (var note in measure.Notes)
            {
                if (note.IsRest || note.MidiPitch < MinMidi || note.MidiPitch > MaxMidi) continue;
                long onset       = measure.GlobalOnsetDivisions + note.OnsetDivisions;
                long off         = onset + Math.Max(1, note.Duration);
                double x         = MidiToX(note.MidiPitch);
                double bw        = MidiToWidth(note.MidiPitch);
                bool black       = BlackPitchClass.Contains(note.MidiPitch % 12);
                int visualStaff  = _score.VisualStaff(part, note);
                _bars.Add(new NoteBar(x, bw, onset, off, note.MidiPitch, visualStaff, black));
            }
        }
    }

    /// <summary>
    /// Given a global-divisions offset and a BPM, returns the pixel Y position
    /// relative to the top of the scroll zone where that division falls.
    /// </summary>
    private double DivisionsToY(long globalDivisions, long currentDivs, double scrollH, double bpm)
    {
        // How many global divisions fit in LookaheadSec of music?
        double divsPerSec    = bpm / 60.0 * _divsPerQuarter;
        double lookaheadDivs = divsPerSec * LookaheadSec;
        // Map division offset (relative to now) → pixel y from top of scroll zone
        // "now" (currentDivs) maps to y = scrollH (keyboard top)
        // "now - lookaheadDivs" maps to y = 0 (top of scroll zone)
        double divsFromNow = globalDivisions - currentDivs;
        // divsFromNow = 0  → y = scrollH   (at keyboard)
        // divsFromNow = +lookaheadDivs → y = 0  (top of window)
        return scrollH - (divsFromNow / lookaheadDivs) * scrollH;
    }

    public override void Render(DrawingContext ctx)
    {
        double totalH  = Bounds.Height;
        double scrollH = Math.Max(50, totalH - KeyboardH);

        // Default BPM for static preview
        double bpm = _score.DefaultBpm > 0 ? _score.DefaultBpm : 120;

        // When stopped, show notes that start within LookaheadSec from time=0
        long displayDivs = _currentGlobalDivisions >= 0 ? _currentGlobalDivisions : 0;

        // ── Background ────────────────────────────────────────────────────
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(20, 20, 20)),
            new Rect(0, 0, _canvasW, scrollH));

        // ── Faint pitch lanes (every white key, every C label) ─────────────
        var lanePen  = new Pen(new SolidColorBrush(Color.FromArgb(30, 200, 200, 200)), 0.5);
        var cPen     = new Pen(new SolidColorBrush(Color.FromArgb(60, 200, 200, 200)), 0.8);
        var labelBrush = new SolidColorBrush(Color.FromArgb(120, 200, 200, 200));
        var tf         = new Typeface("Consolas");

        for (int m = MinMidi; m <= MaxMidi; m++)
        {
            bool isBlack = BlackPitchClass.Contains(m % 12);
            if (isBlack) continue; // only draw lanes for white keys
            double x = KeyLayout[m - MinMidi].whiteIndex * WhiteKeyW;
            ctx.DrawLine(m % 12 == 0 ? cPen : lanePen,
                new Point(x, 0), new Point(x, scrollH));
            if (m % 12 == 0)
            {
                var ft = new FormattedText($"C{m / 12 - 1}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, tf, 8, labelBrush);
                ctx.DrawText(ft, new Point(x + 1, 2));
            }
        }

        // ── "Now" glow line just above the keyboard ───────────────────────
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(60, 255, 255, 200)),
            new Rect(0, scrollH - 2, _canvasW, 4));

        // ── Note bars — left half = staff 1 (green), right half = staff 2 (blue) ──
        // Splitting each key column ensures both hands are visible at shared pitches.
        var staff1Brush = new SolidColorBrush(Color.FromArgb(210, 64, 200, 90));   // green
        var staff2Brush = new SolidColorBrush(Color.FromArgb(210, 80, 130, 230));  // blue
        var otherBrush  = new SolidColorBrush(Color.FromArgb(200, 200, 180, 80));  // amber

        double divsPerSec    = bpm / 60.0 * _divsPerQuarter;
        double lookaheadDivs = divsPerSec * LookaheadSec;

        foreach (var bar in _bars)
        {
            double yBottom = scrollH - ((bar.GlobalOnset - displayDivs) / lookaheadDivs) * scrollH;
            double yTop    = scrollH - ((bar.GlobalOff   - displayDivs) / lookaheadDivs) * scrollH;

            if (yBottom < 0 || yTop > scrollH) continue;

            double clippedTop = Math.Max(0, yTop);
            double clippedH   = Math.Min(yBottom, scrollH) - clippedTop;
            if (clippedH <= 0) continue;

            double fullX = bar.X - bar.W / 2.0;
            double halfW = bar.W / 2.0;
            double bx, bw;
            ISolidColorBrush brush;
            if (bar.Staff == 1)      { bx = fullX;         bw = halfW; brush = staff1Brush; }
            else if (bar.Staff == 2) { bx = fullX + halfW; bw = halfW; brush = staff2Brush; }
            else                     { bx = fullX;         bw = bar.W; brush = otherBrush; }

            ctx.FillRectangle(brush, new Rect(bx, clippedTop, bw, clippedH),
                (float)Math.Min(3, bw / 2));

            // Bright leading edge at onset
            double edgeY = Math.Min(yBottom, scrollH - 1);
            if (edgeY - clippedTop > 2)
                ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                    new Rect(bx, edgeY - 2, bw, 2));
        }

        // ── Which keys are sounding — bitfield per pitch ──────────────────
        // bit 0 = staff 1 active, bit 1 = staff 2 active, bit 2 = other active
        var activeKeys = new Dictionary<int, int>();
        if (_currentGlobalDivisions >= 0)
        {
            foreach (var bar in _bars)
            {
                if (displayDivs >= bar.GlobalOnset && displayDivs < bar.GlobalOff)
                {
                    int bit = bar.Staff == 1 ? 1 : bar.Staff == 2 ? 2 : 4;
                    activeKeys[bar.MidiPitch] = activeKeys.GetValueOrDefault(bar.MidiPitch) | bit;
                }
            }
        }

        // ── Piano keyboard ────────────────────────────────────────────────
        double kbY = scrollH;

        var whiteKeyBrush = new SolidColorBrush(Color.FromRgb(240, 240, 240));
        var blackKeyBrush = new SolidColorBrush(Color.FromRgb(30,  30,  30));
        var whiteKeyPen   = new Pen(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 0.5);
        var activeS1      = new SolidColorBrush(Color.FromArgb(230, 64, 200, 90));   // green
        var activeS2      = new SolidColorBrush(Color.FromArgb(230, 80, 130, 230));  // blue

        // Draw white keys first
        for (int m = MinMidi; m <= MaxMidi; m++)
        {
            var (wi, isBlack) = KeyLayout[m - MinMidi];
            if (isBlack) continue;
            double kx   = wi * WhiteKeyW;
            double kw   = WhiteKeyW - 0.5;
            int    bits = activeKeys.GetValueOrDefault(m);

            ctx.FillRectangle(whiteKeyBrush, new Rect(kx, kbY, kw, KeyboardH));
            if (bits != 0)
            {
                // Overlay each active-staff half
                double hw = kw / 2.0;
                if ((bits & 1) != 0) ctx.FillRectangle(activeS1, new Rect(kx,      kbY, hw, KeyboardH));
                if ((bits & 2) != 0) ctx.FillRectangle(activeS2, new Rect(kx + hw, kbY, hw, KeyboardH));
            }
            ctx.DrawRectangle(null, whiteKeyPen, new Rect(kx, kbY, kw, KeyboardH));

            if (m % 12 == 0 && bits == 0)
            {
                var ft = new FormattedText($"C{m / 12 - 1}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, tf, 7,
                    new SolidColorBrush(Color.FromRgb(100, 100, 100)));
                ctx.DrawText(ft, new Point(kx + 1, kbY + KeyboardH - 14));
            }
        }

        // Draw black keys on top
        for (int m = MinMidi; m <= MaxMidi; m++)
        {
            var (wi, isBlack) = KeyLayout[m - MinMidi];
            if (!isBlack) continue;
            double kx   = wi * WhiteKeyW + WhiteKeyW - BlackKeyW;
            int    bits = activeKeys.GetValueOrDefault(m);

            ctx.FillRectangle(blackKeyBrush, new Rect(kx, kbY, BlackKeyW, BlackKeyH));
            if (bits != 0)
            {
                double hw = BlackKeyW / 2.0;
                if ((bits & 1) != 0) ctx.FillRectangle(activeS1, new Rect(kx,      kbY, hw, BlackKeyH));
                if ((bits & 2) != 0) ctx.FillRectangle(activeS2, new Rect(kx + hw, kbY, hw, BlackKeyH));
            }
        }
    }
}

