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
using NFluidsynth;
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
    // - Edit this relative path to point at the .mxl you want to inspect -
    private string AdhocMxlPath =>
        Path.Combine(GetSheetMusicFolder(), @"Pop\SangahNoonaSingles\Tico-Tico no Fubá - A Minor - MN0227296 - Tico-Tico no Fubá - A Minor - MN0227296.mxl");
    // "C:\Users\Calvi\OneDrive\SheetMusic\Pop\KristenMoscaSingles\Mary Poppins Rag - G Major - MN0189186 - Mary Poppins Rag - G Major - MN0189186.mxl"
    // -

    // -----------------------------------------------------------------------
    //  Ad-hoc entry point - edit AdhocMxlPath then run
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
    /// Part - Measure - Staff - Voice - Pitch - Octave - Duration - Dots - Accidental.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_NotesBrowser()
    {
        var score = ParseMxl(AdhocMxlPath);
        await ShowNotesBrowserWindowAsync(AdhocMxlPath, score);
    }

    /// <summary>
    /// Opens a BrowseControl grid showing each part/instrument with its statistics:
    /// Part ID - Instrument name - MIDI program - Measure count - Note count - Rest count.
    /// </summary>
    [TestMethod]
    public async Task VisualizeAdhocMxl_PartsBrowser()
    {
        var score = ParseMxl(AdhocMxlPath);
        await ShowPartsBrowserWindowAsync(AdhocMxlPath, score);
    }

    /// <summary>
    /// Opens a BrowseControl grid showing every measure across all parts:
    /// Part - Measure # - Time sig - Notes - Rests - Chords - Stave attributes.
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
    /// Hand range view: for each measure shows the pitch range (min - max MIDI note)
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
    /// color-coded by chromatic pitch class - a quick harmonic fingerprint.
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
        var pathToMxl = Path.Combine(GetSheetMusicFolder(), @"Pop\KristenMoscaSingles\Cantina Band - Cantina Band.mxl");

        var score = ParseMxl(pathToMxl);
        await ShowVerticalPianoRollWindowAsync(pathToMxl, score, startMeasure: 0, syncDiagnostics: true);
    }

    /// <summary>
    /// Same as <see cref="VisualizeAdhocMxl_VerticalPianoRoll"/> but starts playback at
    /// measure 80 so the tail of the score (the section that previously appeared to hang)
    /// can be verified quickly.  The window auto-closes when playback ends naturally.
    /// Note logging is on by default so every NoteOn appears in the output.
    /// </summary>
    [TestMethod]
    [Timeout(120_000)]  // 2 minutes - the tail section is ~45 s at default BPM
    public async Task VisualizeAdhocMxl_VerticalPianoRoll_From80()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("MIDI playback requires Windows (winmm.dll).");

        // Write all Trace output to a file so we can inspect it regardless of how the test is run.
        var logPath = Path.Combine(Path.GetTempPath(), "VtPianoRoll_From80_run.log");
        using var fileListener = new System.Diagnostics.TextWriterTraceListener(logPath) { TraceOutputOptions = System.Diagnostics.TraceOptions.DateTime };
        Trace.Listeners.Add(fileListener);
        Trace.AutoFlush = true;
        Trace.WriteLine($"=== VisualizeAdhocMxl_VerticalPianoRoll_From80 started {DateTime.Now:HH:mm:ss} log={logPath} ===");
        try
        {
            var score = ParseMxl(AdhocMxlPath);
            await ShowVerticalPianoRollWindowAsync(AdhocMxlPath, score,
                startMeasure: 80, autoCloseOnEnd: true, logNotesDefault: true);
        }
        finally
        {
            Trace.WriteLine($"=== test method returning {DateTime.Now:HH:mm:ss} ===");
            Trace.Listeners.Remove(fileListener);
        }
        LogMessage($"Full trace log: {logPath}");
    }

    // -----------------------------------------------------------------------
    //  Generated-music vertical piano roll
    //  No MXL file required — an MxlScore is built programmatically.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Demonstrates the vertical piano-roll visualizer without any MusicXML file.
    /// An <see cref="MusicGenerator"/> builds an <see cref="MxlScore"/> in memory
    /// for the pattern selected by the index in <c>patterns[]</c> below.
    ///
    /// Theory / exercise patterns:
    ///   ChromaticFullRange      — every semitone A0 → C8, then back down
    ///   ChromaticWithOctave     — same, but each note doubled one octave above simultaneously
    ///   MajorScales             — all 12 major scales ascending/descending (cycle of 5ths)
    ///   WholeToneScales         — both whole-tone sets, each up and down
    ///   MinorArpeggios          — broken minor triads (root, ♭3, 5) across all 12 roots
    ///   AlbertiBassAndMelody    — classic LH Alberti bass + RH C-major melody (grand staff)
    ///   Polyrhythm              — LH 3-against-RH-4 for 12 measures
    ///   PolyrhythmTonicDominant — RH=tonic C4 (4/bar) vs LH=dominant G3 (3/bar), accented
    ///   BrownianWalk            — pitch drifts ±1 semitone per eighth note (random walk)
    ///
    /// Style tutorial patterns (RH melody/chords + LH bass/rhythm, 16 measures each):
    ///   Pop     — I–V–vi–IV (C G Am F), block chords RH + walking bass LH, 100 BPM
    ///   Jazz    — ii–V–I (Dm7 G7 Cmaj7), shell voicings RH + walking bass LH, 120 BPM
    ///   RnB     — 16th-note groove C7–F7, pentatonic melody RH + syncopated bass LH, 92 BPM
    ///   Rock    — 12-bar blues in E, pentatonic lick RH + power chords LH, 130 BPM
    ///   HipHop  — boom-bap Am7/Dm7 chord stabs RH + kick/snare/hi-hat LH, 85 BPM
    ///   Latin   — 2-3 son clave montuno (C F G vamp), 110 BPM
    ///   Tango   — habanera bass LH + chromatic descending chord stabs RH in A minor, 110 BPM
    ///   Gospel  — I–vi–ii–V7 in F (Fmaj7 Dm7 Gm7 C7), block chords + stride bass, 88 BPM
    ///   Country — two-step in G (G C D G), pentatonic fiddle RH + boom-chick bass LH, 120 BPM
    ///   Ragtime — oom-pah in C (Joplin style), syncopated 16th melody + stride LH, 100 BPM
    ///
    /// Edit the index in <c>patterns[N]</c> to select a pattern and run.
    /// </summary>
    [TestMethod]
    public async Task VisualizeGenerated_VerticalPianoRoll()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("MIDI playback requires Windows (winmm.dll).");

        string[] patterns = {
            // ── theory / exercise ──────────────────────────────
            "ChromaticFullRange",
            "ChromaticWithOctave",
            "MajorScales",
            "WholeToneScales",
            "MinorArpeggios",
            "AlbertiBassAndMelody",
            "Polyrhythm",
            "PolyrhythmTonicDominant",
            "BrownianWalk",
            // ── style tutorials ────────────────────────────────
            "Pop",
            "Jazz",
            "RnB",
            "Rock",
            "HipHop",
            "Latin",
            "Tango",
            "BossaNova",
            "Gospel",
            "Country",
            "Ragtime",
        };
        var pattern = patterns[7];  // edit index to select pattern; 9 = Pop
        {
            var score = pattern switch
            {
                "ChromaticFullRange" => MusicGenerator.ChromaticFullRange(),
                "ChromaticWithOctave" => MusicGenerator.ChromaticWithOctave(),
                "MajorScales" => MusicGenerator.MajorScales(),
                "WholeToneScales" => MusicGenerator.WholeToneScales(),
                "MinorArpeggios" => MusicGenerator.MinorArpeggios(),
                "AlbertiBassAndMelody" => MusicGenerator.AlbertiBassAndMelody(),
                "Polyrhythm" => MusicGenerator.Polyrhythm(),
                "PolyrhythmTonicDominant" => MusicGenerator.PolyrhythmTonicDominant(),
                "BrownianWalk" => MusicGenerator.BrownianWalk(),
                "Pop" => MusicGenerator.StylePop(),
                "Jazz" => MusicGenerator.StyleJazz(),
                "RnB" => MusicGenerator.StyleRnB(),
                "Rock" => MusicGenerator.StyleRock(),
                "HipHop" => MusicGenerator.StyleHipHop(),
                "Latin" => MusicGenerator.StyleLatin(),
                "Tango" => MusicGenerator.StyleTango(),
                "BossaNova" => MusicGenerator.StyleBossaNova(),
                "Gospel" => MusicGenerator.StyleGospel(),
                "Country" => MusicGenerator.StyleCountry(),
                "Ragtime" => MusicGenerator.StyleRagtime(),
                _ => throw new ArgumentException($"Unknown pattern '{pattern}'.")
            };

            LogMessage($"Generated score: pattern={pattern}  parts={score.Parts.Count}  " +
                       $"measures={score.TotalMeasures}  notes={score.TotalNotes}");
            await ShowVerticalPianoRollWindowAsync($"[generated] {pattern}", score);
        }

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
    //  Window builders - return a Window without starting Avalonia themselves.
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
        sb.AppendLine("- Parts -");
        foreach (var p in score.Parts)
        {
            int staff1Notes = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff == 1));
            int staff2Notes = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff == 2));
            int otherNotes = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff > 2));
            sb.AppendLine($"  [{p.PartId}] idx={p.PartIndex} {p.InstrumentName,-28}  MIDI={p.MidiProgram,3}  " +
                          $"Measures={p.Measures.Count,4}  Notes={p.NoteCount,5}  Rests={p.RestCount,4}  " +
                          $"Staff1={staff1Notes}  Staff2={staff2Notes}  StaffOther={otherNotes}");
        }
        bool isMultiStaff = score.Parts.Any(p =>
            p.Measures.Any(m => m.Notes.Any(n => !n.IsRest && n.Staff > 1)));
        sb.AppendLine();
        sb.AppendLine($"Layout: {(isMultiStaff ? "Grand-staff (1 part, 2 staves - coloring by <staff> element)" : "Two-part (coloring by part index: part 0=green, part 1=blue)")}");
        var summaryText = sb.ToString();
        Trace.WriteLine(summaryText);

        return new Window
        {
            Title = $"Score Summary - {Path.GetFileName(mxlPath)}",
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
            Title = $"Parts / Instruments - {Path.GetFileName(mxlPath)}",
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
            Title = $"Measures - {Path.GetFileName(mxlPath)}  ({rows.Count} rows)",
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
            Title = $"Notes Browser - {Path.GetFileName(mxlPath)}  ({rows.Count:N0} notes)",
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
            Title = $"Raw MusicXML - {Path.GetFileName(mxlPath)}",
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

    // Individual Show*Async wrappers - each starts its own Avalonia session so
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
            Title = $"Piano Roll - {Path.GetFileName(mxlPath)}",
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
            Title = $"Rhythm Density - {Path.GetFileName(mxlPath)}",
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
            Title = $"Hand Range Analysis - {Path.GetFileName(mxlPath)}",
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
            Title = $"Harmony Timeline - {Path.GetFileName(mxlPath)}",
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
            ? "Grand-staff (coloring by <staff> element)"
            : "Two-part (coloring by part index: 0=green, 1=blue)";
        var diagSbP = new StringBuilder();
        diagSbP.AppendLine($"Playable piano roll: {score.TotalNotes} notes  Parts={score.Parts.Count}  BPM={score.DefaultBpm}  Layout={layoutP}");
        foreach (var p in score.Parts)
        {
            int s1 = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff == 1));
            int s2 = p.Measures.Sum(m => m.Notes.Count(n => !n.IsRest && n.Staff == 2));
            diagSbP.AppendLine($"  Part[{p.PartIndex}] {p.PartId,-6} {p.InstrumentName,-28}  Notes={p.NoteCount}  Staff1={s1}  Staff2={s2}");
        }
        LogMessage(diagSbP.ToString());

        var canvas = new PlayablePianoRollCanvas(score);
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
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
            Minimum = 40,
            Maximum = 300,
            Value = score.DefaultBpm,
            Width = 180,
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

        var playBtn = new Button { Content = "-  Play", Margin = new Thickness(4), Padding = new Thickness(8, 2) };
        var stopBtn = new Button { Content = "-  Stop", Margin = new Thickness(4), Padding = new Thickness(8, 2), IsEnabled = false };

        int totalMeasuresH = score.Parts.Max(p => p.Measures.Count);
        var measureSlider = new Slider
        {
            Minimum = 1,
            Maximum = Math.Max(1, totalMeasuresH),
            Value = 1,
            Width = 260,
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

        var fluidSynthChk = new CheckBox
        {
            Content = "FluidSynth",
            IsChecked = false,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            [ToolTip.TipProperty] = "FluidSynth (better sound) - may stall at high polyphony; use VintageDreamsWaves.sf2 for reliability. Unchecked = WinMM (default, reliable)"
        };

        var sfComboH = BuildSoundfontCombo(fluidSynthChk);

        void SetStopped()
        {
            // Dispose runs Stop() which waits for the playback task - do it off the UI thread.
            var playerToStop = player;
            player = null;
            canvas.CurrentGlobalDivisions = -1;
            statusBlock.Text = "Stopped";
            playBtn.IsEnabled = true;
            stopBtn.IsEnabled = false;
            if (playerToStop != null) Task.Run(() => playerToStop.Dispose());
        }

        // Wire once - updates player.LogNotes whenever the checkbox is toggled (including mid-playback)
        logNotesChk.IsCheckedChanged += (_, _) =>
        {
            if (player != null) player.LogNotes = logNotesChk.IsChecked == true;
        };

        playBtn.Click += (_, _) =>
        {
            var prev = player; player = null;
            if (prev != null) Task.Run(() => prev.Dispose());
            string sfPathH = sfComboH.SelectedItem is string sfN
                ? Path.Combine(AppContext.BaseDirectory, "Soundfonts", sfN)
                : Path.Combine(AppContext.BaseDirectory, "Soundfonts", "VintageDreamsWaves.sf2");
            player = new MxlMidiPlayer(score)
            {
                Bpm = bpmSlider.Value,
                StartMeasure = (int)measureSlider.Value,
                LogNotes = logNotesChk.IsChecked == true,
                Backend = fluidSynthChk.IsChecked == true ? MidiBackendKind.FluidSynth : MidiBackendKind.Winmm,
                SoundfontPath = sfPathH,
            };

            player.PositionChanged += (_, divs) => Dispatcher.UIThread.Post(() =>
            {
                canvas.CurrentGlobalDivisions = divs;
                int measureNo = score.Parts[0].Measures
                    .LastOrDefault(m => m.GlobalOnsetDivisions <= divs)?.Number ?? 1;
                sliderDrivingH = true;
                measureSlider.Value = measureNo;
                sliderDrivingH = false;
                statusBlock.Text = $"Playing -  measure {measureNo}  |  {bpmSlider.Value:F0} BPM";
            });

            player.PlaybackEnded += (_, _) => Dispatcher.UIThread.Post(SetStopped);

            playBtn.IsEnabled = false;
            stopBtn.IsEnabled = true;
            statusBlock.Text = "Playing -  ...";
            player.Start();
        };

        stopBtn.Click += (_, _) => SetStopped();

        var toolbar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin = new Thickness(4),
            Children = { playBtn, stopBtn, bpmSlider, bpmLabel, measureSlider, measureLabel, logNotesChk, fluidSynthChk, sfComboH, statusBlock }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(scrollViewer);

        var window = new Window
        {
            Title = $"Playable Piano Roll - {Path.GetFileName(mxlPath)}",
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

    /// <summary>
    /// Builds a ComboBox listing all .sf2 files in the output Soundfonts folder.
    /// Defaults to VintageDreamsWaves (fast voice allocation).
    /// Enabled/disabled in sync with the FluidSynth checkbox.
    /// </summary>
    private static ComboBox BuildSoundfontCombo(CheckBox fluidSynthChk)
    {
        string soundfontsDir = Path.Combine(AppContext.BaseDirectory, "Soundfonts");
        var sf2Files = Directory.Exists(soundfontsDir)
            ? Directory.GetFiles(soundfontsDir, "*.sf2").Select(Path.GetFileName).OfType<string>().OrderBy(x => x).ToList()
            : new List<string>();
        //string defaultSf = "VintageDreamsWaves.sf2";
        string defaultSf = "YDP-GrandPiano.sf2";
        if (!sf2Files.Contains(defaultSf) && sf2Files.Count > 0) defaultSf = sf2Files[0];
        var combo = new ComboBox
        {
            ItemsSource = sf2Files,
            SelectedItem = sf2Files.Contains(defaultSf) ? defaultSf : sf2Files.FirstOrDefault(),
            Width = 220,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            IsEnabled = fluidSynthChk.IsChecked == true,
            [ToolTip.TipProperty] = "Soundfont (.sf2) - VintageDreams is fast; YDP-GrandPiano sounds best but needs polyphony-32"
        };
        fluidSynthChk.IsCheckedChanged += (_, _) => combo.IsEnabled = fluidSynthChk.IsChecked == true;
        return combo;
    }

    private Window BuildVerticalPianoRollWindow(string mxlPath, MxlScore score,
        int startMeasure = 1, bool autoCloseOnEnd = false, bool logNotesDefault = false,
        bool syncDiagnostics = false)
    {
        // Delegate to the production factory in SheetMusicViewer.Desktop.
        // The factory builds the full window including toolbar, canvas, and autoplay wiring.
        LogMessage($"Vertical piano roll: {score.TotalNotes} notes  Parts={score.Parts.Count}  Title={score.Title}");
        return VerticalPianoRollWindowFactory.BuildWindow(mxlPath, score,
            startMeasure, autoCloseOnEnd, logNotesDefault, syncDiagnostics);
    }

    private async Task ShowVerticalPianoRollWindowAsync(string mxlPath, MxlScore score,
        int startMeasure = 1, bool autoCloseOnEnd = false, bool logNotesDefault = false,
        bool syncDiagnostics = false)
    {
        // Compute how long the tail of the score takes at default BPM=120 and add a 30s buffer.
        // This ensures RunAvaloniaTest's internal timeout never fires before playback ends.
        var seekMeasure = score.Parts.Count > 0
            ? score.Parts[0].Measures.FirstOrDefault(m => m.Number >= startMeasure)
            : null;
        double seekMs = seekMeasure?.GlobalOnsetMs ?? 0.0;
        var lastMeasure = score.Parts.Count > 0 ? score.Parts[0].Measures.LastOrDefault() : null;
        double totalMs = lastMeasure != null
            ? (lastMeasure.GlobalOnsetMs - seekMs) * 1.0 + 8_000   // +8 s for last measure's notes
            : 60_000;
        int timeoutMs = (int)Math.Clamp(totalMs + 30_000, 60_000, 600_000);  // min 60 s, max 10 min
        Trace.WriteLine($"{DateTime.Now:HH:mm:ss.fff} RunAvaloniaTest timeout={timeoutMs / 1000} s  " +
            $"(scoreMs={totalMs:F0}  startMeasure={startMeasure})");
        await AvaloniaTestHelper.RunAvaloniaTest(async (lifetime, testCompleted) =>
        {
            var window = BuildVerticalPianoRollWindow(mxlPath, score, startMeasure, autoCloseOnEnd, logNotesDefault, syncDiagnostics);
            lifetime.MainWindow = window;
            window.Closed += AvaloniaTestHelper.CreateWindowClosedHandler(testCompleted, lifetime, "Vertical piano roll closed.");
            window.Show();
            await Task.CompletedTask;
        }, timeoutMs: timeoutMs);
    }

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
/// <summary>
/// Renders a piano-roll grid: time (measures) on the X axis, MIDI pitch on Y.
/// Staff 1 (treble/right hand) = green; staff 2 (bass/left hand) = blue.
/// </summary>
internal class PianoRollCanvas : Control
{
    protected static readonly int MinMidi = 21;
    protected static readonly int MaxMidi = 108;
    protected static readonly int KeyH = 5;
    protected static readonly int MeasureW = 80;
    protected static readonly int YAxisW = 32;
    private static readonly int[] BlackKeys = { 1, 3, 6, 8, 10 };

    protected readonly MxlScore _score;
    protected readonly int _totalMeasures;
    protected readonly double _canvasW;
    protected readonly double _canvasH;

    public PianoRollCanvas(MxlScore score)
    {
        _score = score;
        _totalMeasures = score.Parts.Max(p => p.Measures.Count);
        _canvasW = YAxisW + _totalMeasures * MeasureW;
        _canvasH = (MaxMidi - MinMidi + 1) * KeyH;
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
            int divs = Math.Max(1, measure.Divisions);
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
        var bg = new SolidColorBrush(Color.FromRgb(28, 28, 28));
        var blackKeyB = new SolidColorBrush(Color.FromRgb(48, 48, 48));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(55, 55, 55)), 0.5);
        var octavePen = new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 90)), 0.8);
        var measurePen = new Pen(new SolidColorBrush(Color.FromRgb(65, 65, 65)), 0.5);
        var labelBrush = new SolidColorBrush(Color.FromRgb(140, 140, 140));
        var tf = new Typeface("Consolas");

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

        // Notes - split each pitch row top/bottom by staff so both are visible at the same pitch.
        // Staff 1 (right hand) - top half of the row (green)
        // Staff 2 (left hand)  - bottom half of the row (blue)
        var staff1Brush = new SolidColorBrush(Color.FromArgb(220, 64, 192, 87));   // green
        var staff2Brush = new SolidColorBrush(Color.FromArgb(220, 88, 130, 226));  // blue
        var otherBrush = new SolidColorBrush(Color.FromArgb(200, 200, 180, 80));  // amber

        int halfH = Math.Max(1, (KeyH - 2) / 2);

        foreach (var part in _score.Parts)
            foreach (var measure in part.Measures)
                foreach (var note in measure.Notes)
                {
                    if (note.IsRest || note.MidiPitch < MinMidi || note.MidiPitch > MaxMidi) continue;

                    int divs = Math.Max(1, measure.Divisions);
                    double xFrac = (double)note.OnsetDivisions / (divs * 4);
                    double wFrac = (double)note.Duration / (divs * 4);
                    double x = YAxisW + (measure.Number - 1) * MeasureW + xFrac * MeasureW;
                    double w = Math.Max(1.5, wFrac * MeasureW - 1);
                    double rowTop = _canvasH - (note.MidiPitch - MinMidi + 1) * KeyH + 1;

                    int visualStaff = _score.VisualStaff(part, note);
                    double ny, nh;
                    IBrush brush;
                    if (visualStaff == 1) { ny = rowTop; nh = halfH; brush = staff1Brush; }
                    else if (visualStaff == 2) { ny = rowTop + halfH; nh = halfH; brush = staff2Brush; }
                    else { ny = rowTop; nh = KeyH - 2; brush = otherBrush; }

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
    private const int Slots = 16;   // 16th-note positions per measure
    private const int BarW = 40;
    private const int LabelH = 24;
    private const int LegendH = 20;
    private readonly MxlScore _score;
    private readonly int[] _staff1 = new int[Slots];
    private readonly int[] _staff2 = new int[Slots];
    private readonly double _canvasW;
    private readonly double _canvasH = 420;

    public RhythmDensityCanvas(MxlScore score)
    {
        _score = score;
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
                    else _staff2[slot]++;
                }
            }
    }

    protected override Size MeasureOverride(Size _) => new(_canvasW, _canvasH);

    public override void Render(DrawingContext ctx)
    {
        var bg = new SolidColorBrush(Color.FromRgb(28, 28, 28));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(60, 60, 60)), 0.5);
        var axispen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1);
        var s1Brush = new SolidColorBrush(Color.FromArgb(220, 64, 192, 87));
        var s2Brush = new SolidColorBrush(Color.FromArgb(220, 88, 130, 226));
        var labelBr = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        var tf = new Typeface("Consolas");

        ctx.FillRectangle(bg, new Rect(0, 0, _canvasW, _canvasH));

        int maxCount = Math.Max(1, _staff1.Concat(_staff2).Max());
        double chartH = _canvasH - LabelH - LegendH - 10;
        double xOff = 30;

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
            string[] beatNames = { "1", "e", "+", "a", "2", "e", "+", "a", "3", "e", "+", "a", "4", "e", "+", "a" };
            var ft = new FormattedText(beatNames[i], CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, 10, i % 4 == 0 ? labelBr : new SolidColorBrush(Color.FromRgb(100, 100, 100)));
            ctx.DrawText(ft, new Point(x + BarW / 4.0, _canvasH - LabelH - LegendH + 4));
        }

        // Legend
        double ly = _canvasH - LegendH + 2;
        ctx.FillRectangle(s1Brush, new Rect(xOff, ly, 14, 10));
        ctx.DrawText(new FormattedText("Staff 1 (treble)", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf, 10, labelBr), new Point(xOff + 18, ly));
        ctx.FillRectangle(s2Brush, new Rect(xOff + 150, ly, 14, 10));
        ctx.DrawText(new FormattedText("Staff 2 (bass)", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf, 10, labelBr), new Point(xOff + 168, ly));
    }
}

/// <summary>
/// For each measure draws a vertical line showing the pitch range (min-max MIDI note)
/// used by each staff - a quick view of how each hand moves across the keyboard.
/// </summary>
internal sealed class HandRangeCanvas : Control
{
    private static readonly int MinMidi = 21;
    private static readonly int MaxMidi = 108;
    private static readonly int MeasureW = 10;   // px per measure
    private static readonly int YAxisW = 36;
    private readonly MxlScore _score;
    private readonly int _totalMeasures;
    private readonly double _canvasW;
    private readonly double _canvasH;

    public HandRangeCanvas(MxlScore score)
    {
        _score = score;
        _totalMeasures = score.Parts.Max(p => p.Measures.Count);
        _canvasW = YAxisW + _totalMeasures * MeasureW;
        _canvasH = (MaxMidi - MinMidi + 1) * 4.0 + 20;
    }

    private double PitchY(int midi) => _canvasH - 20 - (midi - MinMidi) * 4.0;

    protected override Size MeasureOverride(Size _) => new(_canvasW, _canvasH);

    public override void Render(DrawingContext ctx)
    {
        var bg = new SolidColorBrush(Color.FromRgb(28, 28, 28));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(50, 50, 50)), 0.3);
        var octavePen = new Pen(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 0.5);
        var s1Pen = new Pen(new SolidColorBrush(Color.FromArgb(210, 64, 192, 87)), 2.5);
        var s2Pen = new Pen(new SolidColorBrush(Color.FromArgb(210, 88, 130, 226)), 2.5);
        var labelBr = new SolidColorBrush(Color.FromRgb(130, 130, 130));
        var tf = new Typeface("Consolas");

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
                    var pen = staffGroup.Key == 1 ? s1Pen : s2Pen;
                    double xOffset = staffGroup.Key == 1 ? -1.5 : 1.5;
                    ctx.DrawLine(pen,
                        new Point(x + xOffset, PitchY(minP)),
                        new Point(x + xOffset, PitchY(maxP)));
                }
            }

        // Legend
        var s1Br = new SolidColorBrush(Color.FromArgb(210, 64, 192, 87));
        var s2Br = new SolidColorBrush(Color.FromArgb(210, 88, 130, 226));
        var wBr = new SolidColorBrush(Color.FromRgb(180, 180, 180));
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
/// non-rest note and colors a cell by its pitch class (chromatic root),
/// giving a compact view of harmonic motion.
/// </summary>
internal sealed class HarmonyTimelineCanvas : Control
{
    private const int CellW = 12;   // px per beat
    private const int CellH = 18;   // px per pitch class row
    private const int YAxisW = 26;
    private const int XAxisH = 16;
    private static readonly string[] PitchNames = { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };
    // color wheel: each pitch class gets a distinct hue
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
    // beat - (pitch class, count) - lowest note determines pc
    private readonly List<(int Beat, int Pc)> _beatPcs = new();
    private readonly int _totalBeats;
    private readonly double _canvasW;
    private readonly double _canvasH = 12 * CellH + XAxisH;

    public HarmonyTimelineCanvas(MxlScore score)
    {
        _score = score;
        BuildBeatMap();
        _totalBeats = _beatPcs.Count > 0 ? _beatPcs.Max(b => b.Beat) + 1 : 1;
        _canvasW = YAxisW + _totalBeats * CellW;
    }

    private void BuildBeatMap()
    {
        // For each part, each measure: group notes by beat (onset / divisions) - lowest midi
        var beatLowest = new Dictionary<int, int>(); // global beat - lowest midi

        foreach (var part in _score.Parts)
        {
            int beatsAccum = 0;
            foreach (var measure in part.Measures)
            {
                int divs = Math.Max(1, measure.Divisions);
                // Parse beats from TimeSig (e.g. "4/4" - 4)
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
        var bg = new SolidColorBrush(Color.FromRgb(28, 28, 28));
        var labelBr = new SolidColorBrush(Color.FromRgb(160, 160, 160));
        var emptyBr = new SolidColorBrush(Color.FromRgb(45, 45, 45));
        var tf = new Typeface("Consolas");

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

