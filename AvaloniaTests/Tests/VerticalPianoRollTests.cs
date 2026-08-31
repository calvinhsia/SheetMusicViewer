using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicViewer.Desktop;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Unit tests for the vertical piano roll production code in SheetMusicViewer.Desktop:
///   MxlScore.Parse, MxlMidiPlayer, VerticalPianoRollWindowFactory.
///
/// These tests are safe to run in any environment — no Avalonia window is opened,
/// no audio device is required.
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class VerticalPianoRollTests
{
    // Minimal valid MusicXML 3.1 that contains one part, one measure, one note (middle C).
    private const string MinimalMxl = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE score-partwise PUBLIC ""-//Recordare//DTD MusicXML 3.1 Partwise//EN""
    ""http://www.musicxml.org/dtds/partwise.dtd"">
<score-partwise version=""3.1"">
  <movement-title>Test Score</movement-title>
  <identification>
    <creator type=""composer"">Test Composer</creator>
  </identification>
  <part-list>
    <score-part id=""P1"">
      <part-name>Piano</part-name>
      <score-instrument id=""P1-I1""><instrument-name>Piano</instrument-name></score-instrument>
      <midi-instrument id=""P1-I1"">
        <midi-channel>1</midi-channel>
        <midi-program>1</midi-program>
      </midi-instrument>
    </score-part>
  </part-list>
  <part id=""P1"">
    <measure number=""1"">
      <attributes>
        <divisions>1</divisions>
        <key><fifths>0</fifths><mode>major</mode></key>
        <time><beats>4</beats><beat-type>4</beat-type></time>
        <clef><sign>G</sign><line>2</line></clef>
      </attributes>
      <direction placement=""above"">
        <direction-type><metronome parentheses=""no"">
          <beat-unit>quarter</beat-unit><per-minute>120</per-minute>
        </metronome></direction-type>
        <sound tempo=""120""/>
      </direction>
      <note>
        <pitch><step>C</step><octave>4</octave></pitch>
        <duration>4</duration>
        <type>whole</type>
        <staff>1</staff><voice>1</voice>
      </note>
    </measure>
    <measure number=""2"">
      <note>
        <pitch><step>E</step><alter>0</alter><octave>4</octave></pitch>
        <duration>2</duration><type>half</type>
        <staff>1</staff><voice>1</voice>
      </note>
      <note>
        <pitch><step>G</step><octave>4</octave></pitch>
        <duration>2</duration><type>half</type>
        <staff>2</staff><voice>2</voice>
      </note>
    </measure>
  </part>
</score-partwise>";

    // ── MxlScore.Parse ────────────────────────────────────────────────────────

    [TestMethod]
    public void MxlScore_Parse_ReturnsTitle()
    {
        var score = MxlScore.Parse(MinimalMxl);
        Assert.AreEqual("Test Score", score.Title);
    }

    [TestMethod]
    public void MxlScore_Parse_ReturnsComposer()
    {
        var score = MxlScore.Parse(MinimalMxl);
        Assert.AreEqual("Test Composer", score.Composer);
    }

    [TestMethod]
    public void MxlScore_Parse_DefaultBpmFromSoundElement()
    {
        var score = MxlScore.Parse(MinimalMxl);
        Assert.AreEqual(120.0, score.DefaultBpm, delta: 0.001);
    }

    [TestMethod]
    public void MxlScore_Parse_OnePart()
    {
        var score = MxlScore.Parse(MinimalMxl);
        Assert.AreEqual(1, score.Parts.Count);
        Assert.AreEqual("Piano", score.Parts[0].InstrumentName);
    }

    [TestMethod]
    public void MxlScore_Parse_TwoMeasures()
    {
        var score = MxlScore.Parse(MinimalMxl);
        Assert.AreEqual(2, score.Parts[0].Measures.Count);
    }

    [TestMethod]
    public void MxlScore_Parse_NotesHaveMidiPitch()
    {
        var score = MxlScore.Parse(MinimalMxl);
        // Middle C (C4) = MIDI 60
        var firstNote = score.Parts[0].Measures[0].Notes.First(n => !n.IsRest);
        Assert.AreEqual(60, firstNote.MidiPitch, "C4 should be MIDI 60");
        // E4 = MIDI 64
        var e4 = score.Parts[0].Measures[1].Notes.First(n => n.Pitch == "E");
        Assert.AreEqual(64, e4.MidiPitch, "E4 should be MIDI 64");
    }

    [TestMethod]
    public void MxlScore_Parse_GlobalOnsetMsIsMonotonicallyNonDecreasing()
    {
        var score = MxlScore.Parse(MinimalMxl);
        var measures = score.Parts[0].Measures;
        for (int i = 1; i < measures.Count; i++)
            Assert.IsTrue(measures[i].GlobalOnsetMs >= measures[i - 1].GlobalOnsetMs,
                $"Measure {measures[i].Number} onset {measures[i].GlobalOnsetMs} < previous {measures[i - 1].GlobalOnsetMs}");
    }

    [TestMethod]
    public void MxlScore_Parse_GlobalOnsetDivisionsIsMonotonicallyNonDecreasing()
    {
        var score = MxlScore.Parse(MinimalMxl);
        var measures = score.Parts[0].Measures;
        for (int i = 1; i < measures.Count; i++)
            Assert.IsTrue(measures[i].GlobalOnsetDivisions >= measures[i - 1].GlobalOnsetDivisions,
                $"Measure {measures[i].Number} globalOnset {measures[i].GlobalOnsetDivisions} < previous {measures[i - 1].GlobalOnsetDivisions}");
    }

    /// <summary>
    /// Regression test: when a score changes &lt;divisions&gt; mid-piece (e.g. 4 → 12),
    /// all GlobalOnsetDivisions and note OnsetDivisions must still be expressed in the
    /// canonical (first-measure) unit.  Before the fix, notes in the high-divisions
    /// section appeared 3× ahead of the SyncAnchor predictor, causing visible jumps.
    /// </summary>
    [TestMethod]
    public void MxlScore_Parse_DivisionsChange_NormalisesToCanonicalUnit()
    {
        // Two measures: first uses divisions=4, second changes to divisions=12.
        // A quarter note at divisions=4 has duration=4; at divisions=12 has duration=12.
        // Both should land at the same canonical tick after normalisation.
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<score-partwise version=""3.1"">
  <movement-title>DivTest</movement-title>
  <part-list><score-part id=""P1""><part-name>Piano</part-name></score-part></part-list>
  <part id=""P1"">
    <measure number=""1"">
      <attributes>
        <divisions>4</divisions>
        <time><beats>4</beats><beat-type>4</beat-type></time>
      </attributes>
      <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
      <note><pitch><step>D</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
      <note><pitch><step>E</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
      <note><pitch><step>F</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
    </measure>
    <measure number=""2"">
      <attributes><divisions>12</divisions></attributes>
      <note><pitch><step>G</step><octave>4</octave></pitch><duration>12</duration><type>quarter</type></note>
      <note><pitch><step>A</step><octave>4</octave></pitch><duration>12</duration><type>quarter</type></note>
      <note><pitch><step>B</step><octave>4</octave></pitch><duration>12</duration><type>quarter</type></note>
      <note><pitch><step>C</step><octave>5</octave></pitch><duration>12</duration><type>quarter</type></note>
    </measure>
  </part>
</score-partwise>";

        var score   = MxlScore.Parse(xml);
        var part    = score.Parts[0];
        var m1      = part.Measures[0];
        var m2      = part.Measures[1];

        // Both measures store the canonical (divisions=4) unit.
        Assert.AreEqual(4, m1.Divisions, "Measure 1 should store canonical divisions=4");
        Assert.AreEqual(4, m2.Divisions, "Measure 2 should store canonical divisions=4 after normalisation");

        // Measure 2 starts exactly one 4/4 measure after measure 1 (4 beats × 4 divs = 16 ticks).
        Assert.AreEqual(0L,  m1.GlobalOnsetDivisions, "Measure 1 globalOnset should be 0");
        Assert.AreEqual(16L, m2.GlobalOnsetDivisions, "Measure 2 globalOnset should be 16 (one 4/4 measure)");

        // Notes in measure 2 must have OnsetDivisions and Duration scaled to canonical unit (÷3).
        var g4 = m2.Notes.First(n => n.Pitch == "G");
        Assert.AreEqual(0,  g4.OnsetDivisions, "G4 onset should be 0 within measure 2");
        Assert.AreEqual(4,  g4.Duration,        "G4 duration should be 4 (canonical quarter)");

        var a4 = m2.Notes.First(n => n.Pitch == "A");
        Assert.AreEqual(4, a4.OnsetDivisions, "A4 onset should be 4 within measure 2");
    }

    /// <summary>
    /// Regression test for downward divisions change (e.g. 24 → 2).
    /// Before the fix, divScale = 24/2 = 12 caused notes to be ÷12 instead of ×12,
    /// producing GlobalOnsetDivisions that advanced only 8 ticks per measure instead of 96,
    /// making those measures play ~12× too fast visually.
    /// </summary>
    [TestMethod]
    public void MxlScore_Parse_DivisionsDecrease_NormalisesToCanonicalUnit()
    {
        // Measure 1: divisions=24 (canonical). Measure 2: divisions=2 (smaller, e.g. after Audiveris simplification).
        // A quarter note at div=24 has duration=24; at div=2 has duration=2.
        // After normalisation both should be 24 (canonical quarter).
        const string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<score-partwise version=""3.1"">
  <movement-title>DivDecreaseTest</movement-title>
  <part-list><score-part id=""P1""><part-name>Piano</part-name></score-part></part-list>
  <part id=""P1"">
    <measure number=""1"">
      <attributes>
        <divisions>24</divisions>
        <time><beats>4</beats><beat-type>4</beat-type></time>
      </attributes>
      <note><pitch><step>C</step><octave>4</octave></pitch><duration>24</duration><type>quarter</type></note>
      <note><pitch><step>D</step><octave>4</octave></pitch><duration>24</duration><type>quarter</type></note>
      <note><pitch><step>E</step><octave>4</octave></pitch><duration>24</duration><type>quarter</type></note>
      <note><pitch><step>F</step><octave>4</octave></pitch><duration>24</duration><type>quarter</type></note>
    </measure>
    <measure number=""2"">
      <attributes><divisions>2</divisions></attributes>
      <note><pitch><step>G</step><octave>4</octave></pitch><duration>2</duration><type>quarter</type></note>
      <note><pitch><step>A</step><octave>4</octave></pitch><duration>2</duration><type>quarter</type></note>
      <note><pitch><step>B</step><octave>4</octave></pitch><duration>2</duration><type>quarter</type></note>
      <note><pitch><step>C</step><octave>5</octave></pitch><duration>2</duration><type>quarter</type></note>
    </measure>
  </part>
</score-partwise>";

        var score = MxlScore.Parse(xml);
        var part  = score.Parts[0];
        var m1    = part.Measures[0];
        var m2    = part.Measures[1];

        // Both measures store the canonical (divisions=24) unit.
        Assert.AreEqual(24, m1.Divisions, "Measure 1 should store canonical divisions=24");
        Assert.AreEqual(24, m2.Divisions, "Measure 2 should store canonical divisions=24 after normalisation");

        // Measure 2 starts exactly one 4/4 measure after measure 1 (4 beats × 24 divs = 96 ticks).
        Assert.AreEqual(0L,  m1.GlobalOnsetDivisions, "Measure 1 globalOnset should be 0");
        Assert.AreEqual(96L, m2.GlobalOnsetDivisions, "Measure 2 globalOnset should be 96 (one 4/4 measure at div=24)");

        // Notes in measure 2 must have OnsetDivisions and Duration scaled up to canonical unit (×12).
        var g4 = m2.Notes.First(n => n.Pitch == "G");
        Assert.AreEqual(0,  g4.OnsetDivisions, "G4 onset should be 0 within measure 2");
        Assert.AreEqual(24, g4.Duration,        "G4 duration should be 24 (canonical quarter at div=24)");

        var a4 = m2.Notes.First(n => n.Pitch == "A");
        Assert.AreEqual(24, a4.OnsetDivisions, "A4 onset should be 24 within measure 2");
    }

    // ── Tied-note merging ────────────────────────────────────────────────────

    private static string TieMxl(string measuresXml) => $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<score-partwise version=""3.1"">
  <movement-title>TieTest</movement-title>
  <part-list><score-part id=""P1""><part-name>Piano</part-name></score-part></part-list>
  <part id=""P1"">{measuresXml}</part>
</score-partwise>";

    [TestMethod]
    public void MxlScore_Parse_TiedNotes_WithinMeasure_MergedIntoOne()
    {
        // Two quarter C4s tied within the same measure → one half-note bar, second note absorbed.
        var xml = TieMxl(@"
<measure number=""1"">
  <attributes><divisions>4</divisions><time><beats>4</beats><beat-type>4</beat-type></time></attributes>
  <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type>
    <tie type=""start""/></note>
  <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type>
    <tie type=""stop""/></note>
  <note><pitch><step>E</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
  <note><pitch><step>G</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
</measure>");

        var score  = MxlScore.Parse(xml);
        var notes  = score.Parts[0].Measures[0].Notes;
        var c4open = notes.First(n => n.Pitch == "C" && !n.IsAbsorbed);
        var c4cont = notes.First(n => n.Pitch == "C" && n.IsAbsorbed);

        Assert.IsFalse(c4open.IsAbsorbed,  "Opener should not be absorbed");
        Assert.IsTrue (c4cont.IsAbsorbed,  "Continuation should be absorbed");
        Assert.AreEqual(8, c4open.Duration, "Opener duration should be extended to 8 (two quarters)");
        Assert.AreEqual(0, c4open.OnsetDivisions, "Opener onset unchanged");
    }

    [TestMethod]
    public void MxlScore_Parse_TiedNotes_CrossMeasure_MergedIntoOne()
    {
        // Tied C4 across a bar line: half note in m1 + quarter in m2 → one bar of duration 12.
        var xml = TieMxl(@"
<measure number=""1"">
  <attributes><divisions>4</divisions><time><beats>4</beats><beat-type>4</beat-type></time></attributes>
  <note><pitch><step>C</step><octave>4</octave></pitch><duration>8</duration><type>half</type>
    <tie type=""start""/></note>
  <note><pitch><step>D</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
  <note><pitch><step>E</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
</measure>
<measure number=""2"">
  <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type>
    <tie type=""stop""/></note>
  <note><pitch><step>F</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
  <note><pitch><step>G</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
  <note><pitch><step>A</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
</measure>");

        var score = MxlScore.Parse(xml);
        var m1    = score.Parts[0].Measures[0];
        var m2    = score.Parts[0].Measures[1];

        var opener = m1.Notes.First(n => n.Pitch == "C");
        var cont   = m2.Notes.First(n => n.Pitch == "C");

        Assert.IsFalse(opener.IsAbsorbed, "Opener in m1 must not be absorbed");
        Assert.IsTrue (cont.IsAbsorbed,   "Continuation in m2 must be absorbed");
        // Opener starts at onset 0 in m1 (globalOnset 0).
        // Continuation starts at onset 0 in m2 (globalOnset 16), duration 4.
        // Expected merged duration = (16 - 0 - 0 + 0 + 4) = 20.
        Assert.AreEqual(20, opener.Duration, "Merged duration should span m1 half + m2 quarter = 20 divs");
    }

    [TestMethod]
    public void MxlScore_Parse_TiedNotes_ChainedAcrossThreeMeasures()
    {
        // C4 tied across three measures: quarter + quarter + quarter → duration = 12.
        var xml = TieMxl(@"
<measure number=""1"">
  <attributes><divisions>4</divisions><time><beats>4</beats><beat-type>4</beat-type></time></attributes>
  <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type>
    <tie type=""start""/></note>
  <note><pitch><step>D</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
  <note><pitch><step>E</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
  <note><pitch><step>F</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
</measure>
<measure number=""2"">
  <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type>
    <tie type=""stop""/><tie type=""start""/></note>
  <note><pitch><step>G</step><octave>4</octave></pitch><duration>12</duration><type>dotted-half</type></note>
</measure>
<measure number=""3"">
  <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type>
    <tie type=""stop""/></note>
  <note><pitch><step>A</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
  <note><pitch><step>B</step><octave>4</octave></pitch><duration>4</duration><type>quarter</type></note>
  <note><pitch><step>D</step><octave>5</octave></pitch><duration>4</duration><type>quarter</type></note>
</measure>");

        var score = MxlScore.Parse(xml);
        var m1    = score.Parts[0].Measures[0];
        var m2    = score.Parts[0].Measures[1];
        var m3    = score.Parts[0].Measures[2];

        var opener = m1.Notes.First(n => n.Pitch == "C");
        var mid    = m2.Notes.First(n => n.Pitch == "C");
        var last   = m3.Notes.First(n => n.Pitch == "C");

        Assert.IsFalse(opener.IsAbsorbed, "Opener must not be absorbed");
        Assert.IsTrue (mid.IsAbsorbed,    "Middle link must be absorbed");
        Assert.IsTrue (last.IsAbsorbed,   "Final link must be absorbed");
        // globalOnset: m1=0, m2=16, m3=32.  Final C ends at 32+0+4=36.  Opener onset=0 in m1.
        // merged duration = 36 - 0 - 0 = 36.
        Assert.AreEqual(36, opener.Duration, "Chained tie: total duration = 3 quarters = 36 divs");
    }

    [TestMethod]
    public void MxlScore_VisualStaff_GrandStaffUsesStaffElement()
    {
        // Measure 2 has notes on staff 1 and staff 2 → isMultiStaff = true
        var score = MxlScore.Parse(MinimalMxl);
        var part  = score.Parts[0];
        var noteS1 = part.Measures[1].Notes.First(n => n.Staff == 1);
        var noteS2 = part.Measures[1].Notes.First(n => n.Staff == 2);

        Assert.AreEqual(1, score.VisualStaff(part, noteS1), "Staff 1 note should map to visual staff 1");
        Assert.AreEqual(2, score.VisualStaff(part, noteS2), "Staff 2 note should map to visual staff 2");
    }

    // ── MxlNote.MidiPitch ────────────────────────────────────────────────────

    [TestMethod]
    public void MxlNote_MidiPitch_Rest_ReturnsZero()
    {
        var rest = new MxlNote { IsRest = true };
        Assert.AreEqual(0, rest.MidiPitch);
    }

    [TestMethod]
    public void MxlNote_MidiPitch_WithAlter_AppliesSharp()
    {
        // F#4 = MIDI 66
        var note = new MxlNote { Pitch = "F", Octave = "4", PitchAlter = 1 };
        Assert.AreEqual(66, note.MidiPitch, "F#4 should be MIDI 66");
    }

    // ── VerticalPianoRollWindowFactory.FindSoundfont ────────────────────────

    [TestMethod]
    public void FindSoundfont_ReturnsGeneralUserGS_WhenBundled()
    {
        // The MSBuild ItemGroup copies GeneralUser-GS.sf2 to Soundfonts\ in the build output.
        string? sf = VerticalPianoRollWindowFactory.FindSoundfont();
        if (sf == null)
        {
            // ThirdParty folder was not present during this build — skip gracefully.
            Assert.Inconclusive("GeneralUser-GS.sf2 not found in build output (ThirdParty folder absent). " +
                "Add ThirdParty\\Soundfonts\\GeneralUser-GS.sf2 to enable FluidSynth playback.");
            return;
        }
        Assert.IsTrue(File.Exists(sf), $"FindSoundfont returned a path that does not exist: {sf}");
        StringAssert.EndsWith(sf.ToLowerInvariant(), ".sf2", "FindSoundfont should return a .sf2 file");
    }

    // ── OpenInVerticalPianoRoll command path ─────────────────────────────────

    [TestMethod]
    public void TocEntryViewModel_OpenInVerticalPianoRoll_DoesNotThrowForMissingMxl()
    {
        // When CachedMxlPath is null the command should be a no-op (not throw).
        var vm = new TocEntryViewModel { CachedMxlPath = null };
        // The RelayCommand delegates to the private OpenInVerticalPianoRoll method.
        // Executing with no cached path must complete silently.
        vm.OpenInVerticalPianoRollCommand.Execute(null);
    }

    [TestMethod]
    public void TocEntryViewModel_OpenInVerticalPianoRoll_DoesNotThrowForNonExistentPath()
    {
        var vm = new TocEntryViewModel { CachedMxlPath = @"C:\does\not\exist.mxl" };
        // The command catches the exception internally and writes to Debug — must not propagate.
        vm.OpenInVerticalPianoRollCommand.Execute(null);
    }

    // ── VerticalPianoRollCanvas — pure logic, no rendering ───────────────────

    [TestMethod]
    public void VerticalPianoRollCanvas_ConstructsWithoutException()
    {
        // VerticalPianoRollCanvas is an Avalonia control; construction must happen on the UI thread.
        // Headless Avalonia initialisation is Windows-only in this test suite.
        AvaloniaUIThreadFixture.EnsureInitialized();
        if (!AvaloniaUIThreadFixture.IsSupported)
            Assert.Inconclusive("Headless Avalonia UI thread is only supported on Windows.");

        var score = MxlScore.Parse(MinimalMxl);
        VerticalPianoRollCanvas? canvas = null;
        AvaloniaUIThreadFixture.RunOnUIThread(() =>
        {
            canvas = new VerticalPianoRollCanvas(score);
        });
        // DesiredSize is (0,0) until Measure is called; just confirm construction did not throw.
        Assert.IsNotNull(canvas);
    }

    // ── MxlMidiPlayer — no audio device needed ───────────────────────────────

    [TestMethod]
    public void MxlMidiPlayer_DefaultSoundfontPath_PointsToGeneralUserGS()
    {
        var score  = MxlScore.Parse(MinimalMxl);
        var player = new MxlMidiPlayer(score);
        StringAssert.Contains(player.SoundfontPath, "GeneralUser-GS.sf2",
            "Default soundfont should be GeneralUser-GS.sf2");
    }

    [TestMethod]
    public void MxlMidiPlayer_DefaultBackend_IsFluidSynth()
    {
        var score  = MxlScore.Parse(MinimalMxl);
        var player = new MxlMidiPlayer(score);
        Assert.AreEqual(MidiBackendKind.FluidSynth, player.Backend,
            "Default backend should be FluidSynth for best sound quality");
    }
}
