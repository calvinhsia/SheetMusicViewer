using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer.Desktop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
            sb.AppendLine($"  [{p.PartId}] {p.InstrumentName,-30}  MIDI={p.MidiProgram,3}  " +
                          $"Measures={p.Measures.Count,4}  Notes={p.NoteCount,5}  Rests={p.RestCount,4}");
        }
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
    public string Title { get; private set; } = string.Empty;
    public string Composer { get; private set; } = string.Empty;
    public List<MxlPart> Parts { get; } = new();

    public int TotalMeasures => Parts.Sum(p => p.Measures.Count);
    public int TotalNotes    => Parts.Sum(p => p.NoteCount);
    public int TotalRests    => Parts.Sum(p => p.RestCount);

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
        foreach (var partEl in root.Elements(ns + "part"))
        {
            var partId = partEl.Attribute("id")?.Value ?? string.Empty;
            partNames.TryGetValue(partId, out var nameInfo);

            var part = new MxlPart
            {
                PartId         = partId,
                InstrumentName = nameInfo.Name,
                MidiProgram    = nameInfo.Midi
            };

            string currentTimeSig = string.Empty;
            string currentKeySig  = string.Empty;

            foreach (var measureEl in partEl.Elements(ns + "measure"))
            {
                var measureNo = int.TryParse(measureEl.Attribute("number")?.Value, out var mn) ? mn : 0;

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
                    Number  = measureNo,
                    TimeSig = currentTimeSig,
                    KeySig  = currentKeySig
                };

                foreach (var noteEl in measureEl.Elements(ns + "note"))
                {
                    var isRest  = noteEl.Element(ns + "rest") != null;
                    var isChord = noteEl.Element(ns + "chord") != null;

                    string pitch = string.Empty, octave = string.Empty, accidental = string.Empty;
                    if (!isRest)
                    {
                        var pitchEl = noteEl.Element(ns + "pitch");
                        pitch      = pitchEl?.Element(ns + "step")?.Value ?? string.Empty;
                        octave     = pitchEl?.Element(ns + "octave")?.Value ?? string.Empty;
                        accidental = noteEl.Element(ns + "accidental")?.Value ?? string.Empty;
                    }

                    var note = new MxlNote
                    {
                        IsRest     = isRest,
                        IsChord    = isChord,
                        Pitch      = pitch,
                        Octave     = octave,
                        Accidental = accidental,
                        Duration   = int.TryParse(noteEl.Element(ns + "duration")?.Value, out var d) ? d : 0,
                        NoteType   = noteEl.Element(ns + "type")?.Value ?? string.Empty,
                        Dots       = noteEl.Elements(ns + "dot").Count(),
                        Staff      = int.TryParse(noteEl.Element(ns + "staff")?.Value, out var st) ? st : 1,
                        Voice      = int.TryParse(noteEl.Element(ns + "voice")?.Value, out var v) ? v : 1,
                    };

                    measure.Notes.Add(note);
                    if (isChord) measure.ChordCount++;
                }

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

internal sealed class MxlPart
{
    public string PartId         { get; set; } = string.Empty;
    public string InstrumentName { get; set; } = string.Empty;
    public int    MidiProgram    { get; set; }
    public List<MxlMeasure> Measures { get; } = new();
    public int NoteCount => Measures.Sum(m => m.NoteCount);
    public int RestCount => Measures.Sum(m => m.RestCount);
}

internal sealed class MxlMeasure
{
    public int    Number     { get; set; }
    public string TimeSig    { get; set; } = string.Empty;
    public string KeySig     { get; set; } = string.Empty;
    public List<MxlNote> Notes { get; } = new();
    public int ChordCount   { get; set; }
    public int NoteCount    => Notes.Count(n => !n.IsRest);
    public int RestCount    => Notes.Count(n => n.IsRest);
}

internal sealed class MxlNote
{
    public bool   IsRest     { get; set; }
    public bool   IsChord    { get; set; }
    public string Pitch      { get; set; } = string.Empty;
    public string Octave     { get; set; } = string.Empty;
    public string Accidental { get; set; } = string.Empty;
    public int    Duration   { get; set; }
    public string NoteType   { get; set; } = string.Empty;
    public int    Dots       { get; set; }
    public int    Staff      { get; set; }
    public int    Voice      { get; set; }
}
