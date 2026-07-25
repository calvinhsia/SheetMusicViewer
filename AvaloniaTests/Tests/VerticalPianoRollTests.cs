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
