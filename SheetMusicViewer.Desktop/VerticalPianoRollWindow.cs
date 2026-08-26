// VerticalPianoRollWindow.cs
// Vertical falling-notes piano roll with MIDI playback.
//
// Contains:
//   MxlScore / MxlPart / MxlMeasure / MxlNote   — lightweight MusicXML object model
//   IMidiBackend / WinmmMidiBackend / FluidSynthMidiBackend — MIDI output abstraction
//   MidiBackendKind                              — backend selector enum
//   MxlMidiPlayer                                — schedules MIDI events from an MxlScore
//   VerticalPianoRollCanvas                      — Avalonia Control (falling notes + keyboard)
//   VerticalPianoRollWindow                      — static factory that builds the full Window

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.VisualTree;

namespace SheetMusicViewer.Desktop;

// ─────────────────────────────────────────────────────────────────────────────
//  MusicXML object model
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Parsed representation of a MusicXML score (one .xml or .mxl entry).</summary>
public sealed class MxlScore
{
    public string Title      { get; private set; } = string.Empty;
    public string Composer   { get; private set; } = string.Empty;
    public double DefaultBpm { get; private set; } = 120.0;
    public List<MxlPart> Parts { get; } = new();

    /// <summary>Creates an empty score with the given title and BPM (for programmatic use).</summary>
    public MxlScore() { }
    public MxlScore(string title, double bpm = 120.0) { Title = title; DefaultBpm = bpm; }

    public int TotalMeasures => Parts.Sum(p => p.Measures.Count);
    public int TotalNotes    => Parts.Sum(p => p.NoteCount);
    public int TotalRests    => Parts.Sum(p => p.RestCount);

    private bool? _isMultiStaff;

    /// <summary>
    /// Resolves the visual staff (1 = right hand / green, 2 = left hand / blue) for a note.
    /// Grand-staff: use note.Staff directly. Two-part: use part.PartIndex + 1.
    /// </summary>
    public int VisualStaff(MxlPart part, MxlNote note)
    {
        _isMultiStaff ??= Parts.Any(p => p.Measures.Any(m => m.Notes.Any(n => !n.IsRest && n.Staff > 1)));
        return _isMultiStaff.Value ? note.Staff : (part.PartIndex + 1);
    }

    public static MxlScore Parse(string xml)
    {
        var score = new MxlScore();
        var doc   = XDocument.Parse(xml);
        var root  = doc.Root!;
        XNamespace ns = root.Name.Namespace;

        score.Title = root.Descendants(ns + "movement-title").FirstOrDefault()?.Value.Trim()
                   ?? root.Descendants(ns + "work-title").FirstOrDefault()?.Value.Trim()
                   ?? string.Empty;
        score.Composer = root.Descendants(ns + "creator")
                             .FirstOrDefault(e => string.Equals(
                                 e.Attribute("type")?.Value, "composer",
                                 StringComparison.OrdinalIgnoreCase))
                             ?.Value.Trim()
                         ?? string.Empty;

        var soundEl = root.Descendants(ns + "sound")
                          .FirstOrDefault(e => e.Attribute("tempo") != null);
        if (soundEl != null && double.TryParse(soundEl.Attribute("tempo")?.Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedBpm)
            && parsedBpm > 0)
            score.DefaultBpm = parsedBpm;

        var partNames = new Dictionary<string, (string Name, int Midi)>(StringComparer.OrdinalIgnoreCase);
        foreach (var sp in root.Descendants(ns + "score-part"))
        {
            var id   = sp.Attribute("id")?.Value ?? string.Empty;
            var name = sp.Element(ns + "part-name")?.Value.Trim() ?? id;
            var midi = int.TryParse(
                sp.Descendants(ns + "midi-program").FirstOrDefault()?.Value, out var m) ? m : 0;
            partNames[id] = (name, midi);
        }

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

            string currentTimeSig     = string.Empty;
            string currentKeySig      = string.Empty;
            int    divisions          = 1;
            int    canonicalDivs      = 0;   // set from first <divisions> seen; all globalOnset values use this unit
            long   globalOnset        = 0;
            double globalOnsetMs      = 0.0;
            int    currentTSBeats     = 4;
            int    currentTSBeatType  = 4;
            int    currentVelocity    = 64;   // MIDI velocity tracking; updated by <sound dynamics>

            foreach (var measureEl in partEl.Elements(ns + "measure"))
            {
                var measureNo = int.TryParse(measureEl.Attribute("number")?.Value, out var mn) ? mn : 0;

                var divsEl = measureEl.Descendants(ns + "divisions").FirstOrDefault();
                if (divsEl != null && int.TryParse(divsEl.Value, out var newDivs) && newDivs > 0)
                {
                    divisions = newDivs;
                    if (canonicalDivs == 0) canonicalDivs = newDivs;   // lock canonical unit to first value
                }
                // Normalise all tick values to the canonical unit (first <divisions> seen).
                // Use rational multiply-then-divide so both increases (4→12) and
                // decreases (24→2) are handled correctly without integer truncation to zero.
                int scaleMul = canonicalDivs > 0 ? canonicalDivs : 1;
                int scaleDiv = divisions > 0      ? divisions      : 1;

                var timeEl = measureEl.Descendants(ns + "time").FirstOrDefault();
                if (timeEl != null)
                {
                    var beats    = timeEl.Element(ns + "beats")?.Value ?? "?";
                    var beatType = timeEl.Element(ns + "beat-type")?.Value ?? "?";
                    currentTimeSig = $"{beats}/{beatType}";
                    if (int.TryParse(beats,    out var tsb)  && tsb  > 0) currentTSBeats    = tsb;
                    if (int.TryParse(beatType, out var tsbt) && tsbt > 0) currentTSBeatType = tsbt;
                }

                var keyEl = measureEl.Descendants(ns + "key").FirstOrDefault();
                if (keyEl != null)
                {
                    var fifths = int.TryParse(keyEl.Element(ns + "fifths")?.Value, out var f) ? f : 0;
                    var mode   = keyEl.Element(ns + "mode")?.Value ?? "major";
                    currentKeySig = KeyName(fifths, mode);
                }

                var measure = new MxlMeasure
                {
                    Number               = measureNo,
                    TimeSig              = currentTimeSig,
                    KeySig               = currentKeySig,
                    Divisions            = canonicalDivs > 0 ? canonicalDivs : divisions,
                    GlobalOnsetDivisions = globalOnset,
                    GlobalOnsetMs        = globalOnsetMs,
                    TimeSigBeats         = currentTSBeats,
                    TimeSigBeatType      = currentTSBeatType,
                };

                // Update running velocity from any <direction><sound dynamics="N"/> in this measure.
                // MusicXML dynamics range 0–160; clamp to MIDI 0–127.
                foreach (var dirEl in measureEl.Elements(ns + "direction"))
                {
                    var dirSoundEl = dirEl.Element(ns + "sound");
                    if (dirSoundEl != null &&
                        double.TryParse(dirSoundEl.Attribute("dynamics")?.Value,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var dynVal))
                        currentVelocity = Math.Clamp((int)Math.Round(dynVal * 127.0 / 160.0), 1, 127);
                }

                int cursor     = 0;
                int lastCursor = 0;

                foreach (var child in measureEl.Elements())
                {
                    var localName = child.Name.LocalName;
                    if (localName == "backup")
                    {
                        var bd = int.TryParse(child.Element(ns + "duration")?.Value, out var b) ? b : 0;
                        cursor = Math.Max(0, cursor - bd);
                        lastCursor = cursor;
                        continue;
                    }
                    if (localName == "forward")
                    {
                        var fd = int.TryParse(child.Element(ns + "duration")?.Value, out var fw) ? fw : 0;
                        cursor += fd;
                        lastCursor = cursor;
                        continue;
                    }
                    if (localName != "note") continue;

                    var isRest  = child.Element(ns + "rest")  != null;
                    var isChord = child.Element(ns + "chord") != null;
                    var dur     = int.TryParse(child.Element(ns + "duration")?.Value, out var d) ? d : 0;
                    int onset   = isChord ? lastCursor : cursor;

                    string pitch = string.Empty, octave = string.Empty, accidental = string.Empty;
                    int pitchAlter = 0;
                    if (!isRest)
                    {
                        var pe = child.Element(ns + "pitch");
                        pitch      = pe?.Element(ns + "step")?.Value   ?? string.Empty;
                        octave     = pe?.Element(ns + "octave")?.Value ?? string.Empty;
                        accidental = child.Element(ns + "accidental")?.Value ?? string.Empty;
                        if (double.TryParse(pe?.Element(ns + "alter")?.Value,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var alterD))
                            pitchAlter = (int)Math.Round(alterD);
                    }

                    // Per-note dynamics override: <notations><dynamics> symbolic markings.
                    int noteVelocity = currentVelocity;
                    var notationsEl = child.Element(ns + "notations");
                    if (notationsEl != null)
                    {
                        var dynEl = notationsEl.Element(ns + "dynamics");
                        if (dynEl != null)
                        {
                            // <dynamics> may contain a numeric value or a symbolic child element.
                            if (double.TryParse(dynEl.Value,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out var perNoteDyn))
                                noteVelocity = Math.Clamp((int)Math.Round(perNoteDyn * 127.0 / 160.0), 1, 127);
                            else
                                noteVelocity = dynEl.Elements().FirstOrDefault()?.Name.LocalName switch
                                {
                                    "ppp" => 16, "pp" => 33, "p" => 49, "mp" => 64,
                                    "mf"  => 80, "f"  => 96, "ff" => 112, "fff" => 127,
                                    _ => currentVelocity
                                };
                        }
                    }

                    var note = new MxlNote
                    {
                        IsRest         = isRest,
                        IsChord        = isChord,
                        Pitch          = pitch,
                        Octave         = octave,
                        Accidental     = accidental,
                        PitchAlter     = pitchAlter,
                        Duration       = (int)((long)dur   * scaleMul / scaleDiv),
                        OnsetDivisions = (int)((long)onset * scaleMul / scaleDiv),
                        NoteType       = child.Element(ns + "type")?.Value ?? string.Empty,
                        Dots           = child.Elements(ns + "dot").Count(),
                        Staff          = int.TryParse(child.Element(ns + "staff")?.Value, out var st) ? st : 1,
                        Voice          = int.TryParse(child.Element(ns + "voice")?.Value, out var v)  ? v  : 1,
                        Velocity       = noteVelocity,
                        TieStart       = child.Elements(ns + "tie").Any(t => t.Attribute("type")?.Value == "start"),
                        TieStop        = child.Elements(ns + "tie").Any(t => t.Attribute("type")?.Value == "stop"),
                    };
                    measure.Notes.Add(note);

                    if (!isChord)
                    {
                        lastCursor = cursor;
                        cursor += dur;
                    }
                }

                int rawMeasureDur = measure.Notes
                    .Where(n => !n.IsChord)
                    .Select(n => n.OnsetDivisions + n.Duration)
                    .DefaultIfEmpty(0)
                    .Max();

                int canonDivisions = canonicalDivs > 0 ? canonicalDivs : divisions;
                double msPerDiv     = 60_000.0 / (120.0 * canonDivisions);
                double quarterNotes = currentTSBeats * (4.0 / currentTSBeatType);
                double expectedDivs = quarterNotes * canonDivisions;

                if (MxlMidiPlayer.TimingDiagnostics)
                {
                    string tag = rawMeasureDur == (int)expectedDivs ? string.Empty
                        : rawMeasureDur > (int)expectedDivs ? "  [OVERCOUNT]"
                        : rawMeasureDur == 0                ? "  [EMPTY]"
                        :                                     "  [SHORT]";
                    Trace.WriteLine(
                        $"PARSE m={measureNo,4}  ts={currentTimeSig,-5}  divs={canonDivisions,4}" +
                        $"  globalOnset={globalOnset,8}  raw={rawMeasureDur,6}  expected={(int)expectedDivs,6}{tag}");
                }

                // Cap advancement at expectedDivs (same logic applied to globalOnsetMs).
                // Overcounting happens when multi-voice backup/forward pushes the cursor beyond
                // one measure; without capping every subsequent GlobalOnsetDivisions is wrong.
                int advanceDivs = rawMeasureDur > 0
                    ? Math.Min(rawMeasureDur, (int)expectedDivs)
                    : (int)expectedDivs;
                globalOnset += advanceDivs;

                double actualMs     = expectedDivs * msPerDiv;
                double noteBasedMs  = rawMeasureDur * msPerDiv;
                globalOnsetMs += Math.Min(actualMs, noteBasedMs > 0 ? noteBasedMs : actualMs);
                part.Measures.Add(measure);
            }

            // ── Tie-merge pass ────────────────────────────────────────────────────────
            // Walk every note in chronological order.  When a TieStop note is found,
            // locate the most recent unabsorbed TieStart note with the same MIDI pitch
            // and extend its Duration to cover both notes.  The TieStop note is then
            // marked IsAbsorbed so BuildBars and PlaySync skip it.
            //
            // Key: (midiPitch, voice) — voice disambiguates same-pitch ties on different
            // voices (e.g. left-hand C held while right hand plays the same C).
            var openTies = new Dictionary<(int midi, int voice), MxlNote>();
            foreach (var measure in part.Measures)
            {
                foreach (var note in measure.Notes)
                {
                    if (note.IsRest) continue;
                    int midi = note.MidiPitch;
                    if (midi == 0) continue;
                    var key = (midi, note.Voice);

                    if (note.TieStop && openTies.TryGetValue(key, out var opener))
                    {
                        // Extend opener's duration by this note's duration.
                        // The opener lives in a (possibly earlier) measure, so we need the
                        // extra divisions relative to its own measure start.
                        long openerGlobal = opener.IsAbsorbed ? 0   // defensive; should not occur
                            : part.Measures.First(m => m.Notes.Contains(opener)).GlobalOnsetDivisions;
                        long thisGlobal   = measure.GlobalOnsetDivisions;
                        // Total span = distance from opener onset to this note's end.
                        int newDuration = (int)(thisGlobal - openerGlobal
                            - opener.OnsetDivisions + note.OnsetDivisions + note.Duration);
                        opener.Duration = newDuration;
                        note.IsAbsorbed = true;

                        // If this tie-stop also starts a new tie, keep the opener in the map.
                        if (!note.TieStart)
                            openTies.Remove(key);
                        // else: opener stays so the next TieStop extends it further.
                    }

                    // Register this note as the opener for a tie chain.
                    if (note.TieStart && !note.IsAbsorbed)
                        openTies[key] = note;
                }
            }
            // ─────────────────────────────────────────────────────────────────────────

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

public sealed class MxlPart
{
    public string PartId         { get; set; } = string.Empty;
    public string InstrumentName { get; set; } = string.Empty;
    public int    MidiProgram    { get; set; }
    public int    PartIndex      { get; set; }
    public List<MxlMeasure> Measures { get; } = new();
    public int NoteCount => Measures.Sum(m => m.NoteCount);
    public int RestCount => Measures.Sum(m => m.RestCount);
}

public sealed class MxlMeasure
{
    public int    Number               { get; set; }
    public string TimeSig              { get; set; } = string.Empty;
    public string KeySig               { get; set; } = string.Empty;
    public int    Divisions            { get; set; } = 1;
    public long   GlobalOnsetDivisions { get; set; }
    public double GlobalOnsetMs        { get; set; }
    public int    TimeSigBeats         { get; set; } = 4;
    public int    TimeSigBeatType      { get; set; } = 4;
    public List<MxlNote> Notes { get; } = new();
    public int ChordCount  { get; set; }
    public int NoteCount => Notes.Count(n => !n.IsRest);
    public int RestCount => Notes.Count(n =>  n.IsRest);
}

public sealed class MxlNote
{
    public bool   IsRest          { get; set; }
    public bool   IsChord         { get; set; }
    public string Pitch           { get; set; } = string.Empty;
    public string Octave          { get; set; } = string.Empty;
    public string Accidental      { get; set; } = string.Empty;
    public int    PitchAlter      { get; set; }
    public int    Duration        { get; set; }
    public int    OnsetDivisions  { get; set; }
    public string NoteType        { get; set; } = string.Empty;
    public int    Dots            { get; set; }
    public int    Staff           { get; set; }
    public int    Voice           { get; set; }
    /// <summary>MIDI velocity 0–127 (parsed from MusicXML dynamics; default 64 = mf).</summary>
    public int    Velocity        { get; set; } = 64;
    /// <summary>True when this note begins a tie (<tie type="start"/>).</summary>
    public bool   TieStart        { get; set; }
    /// <summary>True when this note continues a tie (<tie type="stop"/>).</summary>
    public bool   TieStop         { get; set; }
    /// <summary>
    /// Set during the post-parse tie-merge pass when this note's duration has been
    /// folded into its predecessor.  Absorbed notes are skipped by BuildBars and PlaySync.
    /// </summary>
    public bool   IsAbsorbed      { get; set; }

    /// <summary>MIDI pitch (21=A0 … 108=C8). Returns 0 for rests.</summary>
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
            return int.TryParse(Octave, out var oct) ? 12 * (oct + 1) + step + PitchAlter : 0;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  MIDI backend abstraction
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Which MIDI output backend to use for playback.</summary>
public enum MidiBackendKind { Winmm, FluidSynth }

/// <summary>Thin abstraction over a MIDI output device.</summary>
public interface IMidiBackend : IDisposable
{
    void Open();
    void Send(uint message);
    void Close();
}

// ── Windows winmm backend ────────────────────────────────────────────────────
internal sealed class WinmmMidiBackend : IMidiBackend
{
    [DllImport("winmm.dll")] static extern int midiOutOpen(out IntPtr h, int dev, IntPtr cb, IntPtr inst, int flags);
    [DllImport("winmm.dll")] static extern int midiOutShortMsg(IntPtr h, uint msg);
    [DllImport("winmm.dll")] static extern int midiOutClose(IntPtr h);
    [DllImport("winmm.dll")] static extern int midiOutGetNumDevs();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MIDIOUTCAPS
    {
        public ushort wMid, wPid;
        public uint   vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public ushort wTechnology, wVoices, wNotes, wChannelMask;
        public uint   dwSupport;
    }
    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    static extern int midiOutGetDevCaps(uint uDeviceID, ref MIDIOUTCAPS caps, uint cb);

    /// <summary>WinMM device index to open. -1 = MIDI Mapper (system default).</summary>
    public int DeviceId { get; set; } = -1;

    /// <summary>Returns all available WinMM MIDI output devices. Id == -1 is the MIDI Mapper.</summary>
    public static IReadOnlyList<(int Id, string Name)> EnumerateDevices()
    {
        var list = new List<(int, string)> { (-1, "MIDI Mapper (system default)") };
        int n = midiOutGetNumDevs();
        for (int i = 0; i < n; i++)
        {
            var caps = new MIDIOUTCAPS();
            midiOutGetDevCaps((uint)i, ref caps, (uint)Marshal.SizeOf<MIDIOUTCAPS>());
            list.Add((i, caps.szPname ?? $"Device {i}"));
        }
        return list;
    }

    private IntPtr _h = IntPtr.Zero;

    public void Open()
    {
        // Small initial yield: midiOutClose() is asynchronous inside the driver;
        // giving the OS a moment before the first Open() attempt avoids a spurious
        // failure when a new player is started immediately after the previous one closed.
        Thread.Sleep(20);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (midiOutOpen(out _h, DeviceId, IntPtr.Zero, IntPtr.Zero, 0) == 0) return;
            Thread.Sleep(50 * (attempt + 1));  // 50 ms, 100 ms, 150 ms … up to 400 ms
        }
        throw new InvalidOperationException("Could not open MIDI output device (winmm).");
    }

    public void Send(uint message) => midiOutShortMsg(_h, message);

    public void Close()
    {
        if (_h == IntPtr.Zero) return;
        for (int ch = 0; ch < 16; ch++) midiOutShortMsg(_h, (uint)((0xB0 | ch) | (123 << 8)));
        midiOutClose(_h);
        _h = IntPtr.Zero;
    }

    public void Dispose() => Close();
}

// ── FluidSynth backend ───────────────────────────────────────────────────────
/// <summary>
/// FluidSynth backend via NFluidSynth.
/// Requires libfluidsynth-3.dll (in app folder) and a .sf2 soundfont.
///
/// Bundled soundfont (copied to Soundfonts\ by MSBuild):
///   GeneralUser-GS.sf2  — ~30 MB GM coverage (GeneralUser GS License v2.0 — free for any use)
///   Attribution: "GeneralUser GS soundfont by S. Christian Collins."
///   Source: http://www.schristiancollins.com/generaluser.php
/// </summary>
internal sealed class FluidSynthMidiBackend : IMidiBackend
{
    private readonly string _soundfontPath;
    private NFluidsynth.Settings?    _settings;
    private NFluidsynth.Synth?       _synth;
    private NFluidsynth.AudioDriver? _driver;

    private System.Threading.Channels.Channel<uint>? _msgChannel;
    private CancellationTokenSource _consumerCts = new();
    private Task _consumerTask = Task.CompletedTask;
    internal int _channelBacklog = 0;
    internal bool LogNotes { get; set; }

    private int _consumerDispatchCount;
    private volatile string? _consumerStuckIn;
    private System.Threading.Timer? _consumerHeartbeatTimer;

    private readonly HashSet<int>[] _activeNotes =
        Enumerable.Range(0, 16).Select(_ => new HashSet<int>()).ToArray();

    public FluidSynthMidiBackend(string soundfontPath) => _soundfontPath = soundfontPath;

    public void Open()
    {
        _settings = new NFluidsynth.Settings();
        _settings[NFluidsynth.ConfigurationKeys.AudioDriver].StringValue = "wasapi";
        _settings[NFluidsynth.ConfigurationKeys.AudioPeriodSize].IntValue = 256;
        _settings[NFluidsynth.ConfigurationKeys.AudioPeriods].IntValue    = 4;
        _settings[NFluidsynth.ConfigurationKeys.SynthThreadSafeApi].IntValue = 0;
        _settings[NFluidsynth.ConfigurationKeys.SynthPolyphony].IntValue = 64;
        _settings[NFluidsynth.ConfigurationKeys.SynthReverbActive].IntValue  = 0;
        _settings[NFluidsynth.ConfigurationKeys.SynthChorusActive].IntValue  = 0;
        _synth = new NFluidsynth.Synth(_settings);
        _synth.LoadSoundFont(_soundfontPath, resetPresets: true);
        _driver = new NFluidsynth.AudioDriver(_settings, _synth);

        _consumerCts = new CancellationTokenSource();
        _msgChannel  = System.Threading.Channels.Channel.CreateUnbounded<uint>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
        var ch  = _msgChannel;
        var syn = _synth;
        var cct = _consumerCts.Token;

        _consumerDispatchCount = 0;
        _consumerHeartbeatTimer = new System.Threading.Timer(_ =>
        {
            int cnt = System.Threading.Volatile.Read(ref _consumerDispatchCount);
            int bl  = System.Threading.Volatile.Read(ref _channelBacklog);
            Trace.WriteLine($"{Ts()} CONSUMER-HB  dispatched={cnt}  backlog={bl}  stuck={_consumerStuckIn ?? "no"}");
        }, null, 2000, 2000);

        _consumerTask = Task.Factory.StartNew(async () =>
        {
            var reader = ch.Reader;
            try
            {
                while (await reader.WaitToReadAsync(cct).ConfigureAwait(false))
                {
                    while (reader.TryRead(out uint msg))
                    {
                        if (LogNotes)
                        {
                            uint mt = msg & 0xF0;
                            int  mn = (int)((msg >> 8) & 0xFF);
                            int  vel = (int)((msg >> 16) & 0xFF);
                            int  bl = System.Threading.Volatile.Read(ref _channelBacklog);
                            if (mt == 0x90 && vel > 0)
                                Trace.WriteLine($"{Ts()} PRE-DISPATCH NoteOn  midi={mn,3}  vel={vel,3}  backlog={bl}");
                            else if (mt == 0x80 || (mt == 0x90 && vel == 0))
                                Trace.WriteLine($"{Ts()} PRE-DISPATCH NoteOff midi={mn,3}            backlog={bl}");
                        }
                        long t0 = Stopwatch.GetTimestamp();
                        DispatchDirect(syn, msg);
                        long ms = (Stopwatch.GetTimestamp() - t0) * 1000 / Stopwatch.Frequency;
                        int backlog = Interlocked.Decrement(ref _channelBacklog);
                        Interlocked.Increment(ref _consumerDispatchCount);
                        if (ms > 20)
                            Trace.WriteLine($"{Ts()} DISPATCH SLOW {ms,5} ms  backlog={backlog}  msg=0x{msg:X8}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Trace.WriteLine($"{Ts()} CONSUMER EXCEPTION {ex.GetType().Name}: {ex.Message}"); }
            finally
            {
                _consumerHeartbeatTimer?.Dispose();
                _consumerHeartbeatTimer = null;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    private void DispatchDirect(NFluidsynth.Synth syn, uint message)
    {
        int status  = (int)(message & 0xFF);
        int type    = status & 0xF0;
        int channel = status & 0x0F;
        int data1   = (int)((message >>  8) & 0xFF);
        int data2   = (int)((message >> 16) & 0xFF);
        switch (type)
        {
            case 0x90:
                if (data2 > 0)
                {
                    if (_activeNotes[channel].Contains(data1))
                    {
                        _consumerStuckIn = $"NoteOff(retrig ch={channel} m={data1})";
                        syn.NoteOff(channel, data1);
                        _consumerStuckIn = null;
                    }
                    _consumerStuckIn = $"NoteOn(ch={channel} m={data1} v={data2})";
                    syn.NoteOn(channel, data1, data2);
                    _consumerStuckIn = null;
                    _activeNotes[channel].Add(data1);
                }
                else
                {
                    if (_activeNotes[channel].Remove(data1))
                    {
                        _consumerStuckIn = $"NoteOff(vel0 ch={channel} m={data1})";
                        syn.NoteOff(channel, data1);
                        _consumerStuckIn = null;
                    }
                }
                break;
            case 0x80:
                if (_activeNotes[channel].Remove(data1))
                {
                    _consumerStuckIn = $"NoteOff(ch={channel} m={data1})";
                    syn.NoteOff(channel, data1);
                    _consumerStuckIn = null;
                }
                break;
            case 0xC0:
                _consumerStuckIn = $"ProgramChange(ch={channel} pgm={data1})";
                syn.ProgramChange(channel, data1);
                _consumerStuckIn = null;
                _activeNotes[channel].Clear();
                break;
        }
        _consumerStuckIn = null;
    }

    public void Send(uint message)
    {
        if (_msgChannel?.Writer.TryWrite(message) == true)
            Interlocked.Increment(ref _channelBacklog);
    }

    private static string Ts() => DateTime.Now.ToString("HH:mm:ss.fff");

    public void Close()
    {
        Trace.WriteLine($"{Ts()} CLOSE begin");
        if (_msgChannel is not null && _synth is not null)
        {
            for (int ch = 0; ch < 16; ch++)
                _msgChannel.Writer.TryWrite((uint)(0xB0 | ch) | (123u << 8));
            _msgChannel.Writer.Complete();
            _consumerCts.Cancel();
            _consumerTask.Wait(TimeSpan.FromSeconds(2));
            Trace.WriteLine($"{Ts()} CLOSE AllNotesOff + consumer done");
        }
        var drv = _driver;   _driver   = null;
        var syn = _synth;    _synth    = null;
        var set = _settings; _settings = null;
        _msgChannel = null;
        Task.Run(() =>
        {
            try { drv?.Dispose(); syn?.Dispose(); set?.Dispose(); }
            catch (Exception ex) { Trace.WriteLine($"{Ts()} CLOSE dispose ex: {ex.Message}"); }
        });
        Trace.WriteLine($"{Ts()} CLOSE done");
    }

    public void Dispose() => Close();
}

// ─────────────────────────────────────────────────────────────────────────────
//  MxlMidiPlayer
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Plays an <see cref="MxlScore"/> through a pluggable MIDI backend.
/// Default backend is <see cref="MidiBackendKind.FluidSynth"/> using the bundled
/// GeneralUser GS soundfont for best out-of-box sound quality.
/// </summary>
public sealed class MxlMidiPlayer : IDisposable
{
    /// <summary>Backend to use. Change before calling <see cref="Start"/>.</summary>
    public MidiBackendKind Backend { get; set; } = MidiBackendKind.FluidSynth;

    /// <summary>
    /// Path to a .sf2 soundfont — only used when <see cref="Backend"/> is
    /// <see cref="MidiBackendKind.FluidSynth"/>.
    ///
    /// Bundled soundfont (auto-copied to Soundfonts\ by MSBuild):
    ///   GeneralUser-GS.sf2  — ~30 MB General MIDI coverage
    ///   License: GeneralUser GS License v2.0 (free for any use, including commercial)
    ///   Attribution: "GeneralUser GS soundfont by S. Christian Collins."
    ///   Source: http://www.schristiancollins.com/generaluser.php
    /// </summary>
    public string SoundfontPath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "Soundfonts", "GeneralUser-GS.sf2");

    private static uint NoteOn (int ch, int n, int v) => (uint)((0x90 | ch) | (n << 8) | (v << 16));
    private static uint NoteOff(int ch, int n)        => (uint)((0x80 | ch) | (n << 8));
    private static uint ProgChg(int ch, int p)        => (uint)((0xC0 | ch) | (p << 8));

    private readonly MxlScore _score;
    private IMidiBackend? _backend;
    private CancellationTokenSource? _cts;
    private Task _playTask = Task.CompletedTask;
    private readonly System.Threading.ManualResetEventSlim _waitEvent = new(false);

    public double Bpm         { get; set; } = 120.0;
    public int    StartMeasure { get; set; } = 1;
    public bool   LogNotes    { get; set; } = false;
    /// <summary>WinMM output device index (-1 = MIDI Mapper). Ignored for FluidSynth.</summary>
    public int    WinmmDeviceId { get; set; } = -1;
    /// <summary>
    /// When true, emits Trace lines for:
    /// - per-measure GlobalOnsetDivisions / rawMeasureDur vs expectedDivs (parse-time)
    /// - non-monotonic GlobalDivisions jumps in the sorted NoteOn event list
    /// </summary>
    public static bool TimingDiagnostics { get; set; } = false;
    /// <summary>
    /// Staff numbers (1 = RH/green, 2 = LH/blue) whose NoteOn messages are suppressed.
    /// Checked dynamically at dispatch time — toggle at any point during playback.
    /// Mirrors <see cref="VerticalPianoRollCanvas.MutedStaves"/> so both canvas and audio
    /// are controlled by the same set.
    /// </summary>
    public HashSet<int> MutedStaves { get; } = new();

    /// <summary>Fired on the playback thread with the current global-divisions offset.</summary>
    public event EventHandler<long>? PositionChanged;
    /// <summary>Fired on the playback thread when playback finishes naturally.</summary>
    public event EventHandler? PlaybackEnded;

    public MxlMidiPlayer(MxlScore score) { _score = score; }

    public void Start()
    {
        Stop();
        _backend = (Backend == MidiBackendKind.Winmm && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ? new WinmmMidiBackend { DeviceId = WinmmDeviceId }
            : (IMidiBackend)new FluidSynthMidiBackend(SoundfontPath);
        _backend.Open();
        _cts = new CancellationTokenSource();
        if (_backend is FluidSynthMidiBackend fsb) fsb.LogNotes = LogNotes;
        var cts = _cts;
        _playTask = Task.Factory.StartNew(
            () => PlaySync(cts.Token),
            cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public void Stop()
    {
        Trace.WriteLine($"{DateTime.Now:HH:mm:ss.fff} PLAYER STOP called");
        _cts?.Cancel();
        try { _playTask.Wait(TimeSpan.FromSeconds(5)); } catch { }
        // PlaySync may have already closed _backend on natural end; Close() is
        // idempotent (guarded by _h == IntPtr.Zero) so calling it again is safe.
        _backend?.Close();
        _backend = null;
        _cts = null;
    }

    private record struct MidiEvent(long TimeMs, uint Message, long GlobalDivisions,
        int MeasureNo = 0, string NoteName = "", int MidiNote = 0, int Staff = 0, int Voice = 0,
        int PartIndex = -1);

    private void PlaySync(CancellationToken ct)
    {
        var events = new List<MidiEvent>();
        int ChannelFor(int pi) => pi >= 9 ? pi + 1 : pi;

        for (int pi = 0; pi < _score.Parts.Count && pi < 15; pi++)
        {
            int prog = Math.Clamp(_score.Parts[pi].MidiProgram - 1, 0, 127);
            events.Add(new MidiEvent(0, ProgChg(ChannelFor(pi), prog), 0));
        }

        double bpmScale = 120.0 / Bpm;
        for (int pi = 0; pi < _score.Parts.Count && pi < 15; pi++)
        {
            int ch = ChannelFor(pi);
            foreach (var measure in _score.Parts[pi].Measures)
            {
                int    divs           = Math.Max(1, measure.Divisions);
                double msPerDiv       = 60_000.0 / (Bpm * divs);
                double measureStartMs = measure.GlobalOnsetMs * bpmScale;
                // Cap note onsets at the measure's expected duration (timesig).
                // Without this cap, multi-voice <backup> can push OnsetDivisions beyond the
                // measure boundary, producing a globalDivs value that is one or more measures
                // ahead of what the SyncAnchor predictor expects, causing visible jumps.
                int    measureDurCap  = (int)(divs * measure.TimeSigBeats * (4.0 / Math.Max(1, measure.TimeSigBeatType)));
                int    maxOnsetDivs   = measure.Notes
                    .Where(n => !n.IsChord)
                    .Select(n => n.OnsetDivisions + n.Duration)
                    .DefaultIfEmpty(measureDurCap)
                    .Max();

                if (TimingDiagnostics && pi == 0)
                {
                    string tag = maxOnsetDivs > measureDurCap ? "  [OVERCOUNT]"
                               : maxOnsetDivs == 0            ? "  [EMPTY]"
                               :                                string.Empty;
                    if (tag.Length > 0)
                        Trace.WriteLine(
                            $"MEASURE m={measure.Number,4}  ts={measure.TimeSig,-5}  divs={divs,4}" +
                            $"  globalOnset={measure.GlobalOnsetDivisions,8}  raw={maxOnsetDivs,6}  cap={measureDurCap,6}{tag}");
                }

                foreach (var note in measure.Notes)
                {
                    if (note.IsRest || note.IsAbsorbed || note.MidiPitch < 21 || note.MidiPitch > 108) continue;
                    int midi = note.MidiPitch;
                    int clampedOnset = Math.Min(note.OnsetDivisions, Math.Min(maxOnsetDivs, measureDurCap) - 1);
                    long globalDivs  = measure.GlobalOnsetDivisions + clampedOnset;
                    long onsetMs     = (long)(measureStartMs + clampedOnset * msPerDiv);
                    long offMs       = onsetMs + Math.Max(30, Math.Min(4_000, (long)(note.Duration * msPerDiv) - 15));
                    // NoteOn carries Staff so the dispatch loop can check MutedStaves dynamically.
                    events.Add(new MidiEvent(onsetMs, NoteOn(ch, midi, note.Velocity), globalDivs,
                        MeasureNo: measure.Number, Staff: note.Staff, Voice: note.Voice, PartIndex: pi));
                    events.Add(new MidiEvent(offMs, NoteOff(ch, midi), -1));  // -1: NoteOff never fires PositionChanged
                }
            }
        }

        events.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        events = events
            .GroupBy(e => (e.TimeMs, e.Message))
            .Select(g => g.First())
            .OrderBy(e => e.TimeMs)
            .ToList();

        if (TimingDiagnostics)
        {
            // Dump every measure's globalOnset so we can spot gaps.
            if (_score.Parts.Count > 0)
            {
                long prevOnset = -1; long prevActualAdvance = 0;
                foreach (var m in _score.Parts[0].Measures)
                {
                    int d = Math.Max(1, m.Divisions);
                    int cap = (int)(d * m.TimeSigBeats * (4.0 / Math.Max(1, m.TimeSigBeatType)));
                    // Compute the actual advance used for this measure (same logic as parser).
                    int raw = m.Notes.Where(n => !n.IsChord)
                        .Select(n => n.OnsetDivisions + n.Duration).DefaultIfEmpty(0).Max();
                    int actualAdvance = raw > 0 ? Math.Min(raw, cap) : cap;
                    long gap = prevOnset < 0 ? 0 : m.GlobalOnsetDivisions - (prevOnset + prevActualAdvance);
                    // Only flag positive gaps (missing beats) — negative gaps are pickup bars, which are expected.
                    string gapTag = gap > 0 ? $"  [GAP=+{gap}]" : string.Empty;
                    Trace.WriteLine(
                        $"MMAP m={m.Number,4}  globalOnset={m.GlobalOnsetDivisions,8}  cap={cap,6}" +
                        $"  timeSig={m.TimeSig,-5}{gapTag}");
                    prevOnset = m.GlobalOnsetDivisions;
                    prevActualAdvance = actualAdvance;
                }
            }

            // Detect any forward jump (gap) or backward jump in the sorted NoteOn event list.
            long prevDivs = long.MinValue; long prevMs = 0;
            foreach (var ev in events)
            {
                if (ev.GlobalDivisions <= 0) continue;  // NoteOff (-1) or ProgChg (0)
                if (ev.GlobalDivisions < prevDivs)
                    Trace.WriteLine(
                        $"NON-MONOTONIC GlobalDivisions: m={ev.MeasureNo}  prev={prevDivs}  cur={ev.GlobalDivisions}" +
                        $"  timeMs={ev.TimeMs}  drop={(ev.GlobalDivisions - prevDivs)}");
                prevDivs = ev.GlobalDivisions;
                prevMs = ev.TimeMs;
            }
        }

        long seekDivs = 0;
        long seekMs   = 0;
        if (StartMeasure > 1 && _score.Parts.Count > 0)
        {
            var sm = _score.Parts[0].Measures.FirstOrDefault(m => m.Number >= StartMeasure);
            if (sm != null)
            {
                seekDivs = sm.GlobalOnsetDivisions;
                seekMs   = (long)(sm.GlobalOnsetMs * bpmScale);
            }
        }
        var playEvents = events
            .Where(e => e.TimeMs >= seekMs)
            .Select(e => e with { TimeMs = e.TimeMs - seekMs })
            .ToList();

        var start = DateTimeOffset.UtcNow;
        PositionChanged?.Invoke(this, seekDivs);
        Trace.WriteLine($"{DateTime.Now:HH:mm:ss.fff} PLAYBACK START  events={playEvents.Count}  backend={_backend?.GetType().Name}");

        try
        {
            foreach (var ev in playEvents)
            {
                if (ct.IsCancellationRequested) return;
                long elapsed = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                int  wait    = (int)(ev.TimeMs - elapsed);
                if (wait > 1)
                    _waitEvent.Wait(wait, ct);
                if (ct.IsCancellationRequested) return;

                // Dynamic mute: skip NoteOn for muted staves; always send NoteOff to release stuck notes.
                {
                    int evType = (int)(ev.Message & 0xF0);
                    int evVel  = (int)((ev.Message >> 16) & 0xFF);
                    bool isNoteOn = evType == 0x90 && evVel > 0;
                    if (isNoteOn && ev.Staff > 0 && MutedStaves.Contains(ev.Staff))
                        goto advancePosition;
                }
                _backend!.Send(ev.Message);
                advancePosition:
                if (LogNotes)
                {
                    int evType = (int)(ev.Message & 0xF0);
                    int evMidi = (int)((ev.Message >>  8) & 0xFF);
                    int evVel  = (int)((ev.Message >> 16) & 0xFF);
                    bool isOn  = evType == 0x90 && evVel > 0;
                    bool isOff = evType == 0x80 || (evType == 0x90 && evVel == 0);
                    if (isOn || isOff)
                    {
                        long actualMs = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
                        long drift    = actualMs - ev.TimeMs;
                        Trace.WriteLine(
                            $"{DateTime.Now:HH:mm:ss.fff} SCHED {(isOn ? "NoteOn " : "NoteOff")} " +
                            $"midi={evMidi,3}  sched={ev.TimeMs,6}ms  " +
                            $"actual={actualMs,6}ms  drift={(drift >= 0 ? "+" : "")}{drift}ms" +
                            (ev.MeasureNo > 0 ? $"  m={ev.MeasureNo} s={ev.Staff}" : ""));
                    }
                }
                if (ev.GlobalDivisions != -1)
                    PositionChanged?.Invoke(this, ev.GlobalDivisions);
            }
        }
        finally
        {
            Trace.WriteLine($"{DateTime.Now:HH:mm:ss.fff} PLAYBACK END  cancelled={ct.IsCancellationRequested}");
            if (!ct.IsCancellationRequested)
            {
                // Close the device NOW, while we are still on the play thread.
                // This releases the WinMM handle before the next song tries to open it.
                _backend?.Close();
                _backend = null;
                // Fire PlaybackEnded via Task.Run so this play task exits first.
                // If we invoked inline here, any handler that calls Stop()->Wait()
                // would deadlock waiting for this very task to finish.
                var ev = PlaybackEnded;
                if (ev != null)
                    Task.Run(() => ev.Invoke(this, EventArgs.Empty));
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _waitEvent.Dispose();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  VerticalPianoRollCanvas
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Avalonia Control that renders a vertical falling-notes piano roll:
/// notes fall down toward a piano keyboard at the bottom; keys light up
/// green (right hand) or blue (left hand) as each note is played.
/// Set <see cref="CurrentGlobalDivisions"/> from the MIDI player to animate.
/// </summary>
public sealed class VerticalPianoRollCanvas : Control
{
    private static readonly int MinMidi = 21;
    private static readonly int MaxMidi = 108;
    private static readonly int KeyCount = MaxMidi - MinMidi + 1;

    private static readonly HashSet<int> BlackPitchClass = new() { 1, 3, 6, 8, 10 };

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

    private const double KeyboardH    = 120;
    private const double WhiteKeyW    = 14;
    private const double BlackKeyW    = 8;
    private const double BlackKeyH    = 70;
    private const double LookaheadSec = 4.0;
    private const double LatencyMs    = 60.0;   // audio-buffer latency compensation (ms)

    private readonly int    _totalWhiteKeys;
    private readonly double _canvasW;

    private sealed record NoteBar(double X, double W, long GlobalOnset, long GlobalOff,
                                  int MidiPitch, int Staff, int Velocity);
    private readonly List<NoteBar> _bars = new();

    private long   _currentGlobalDivisions = -1;
    private long   _anchorDivisions;
    private long   _anchorTimestamp;            // Stopwatch.GetTimestamp() at last anchor
    private double _playBpm = 120;
    private readonly DispatcherTimer _animTimer;
    private readonly MxlScore _score;
    private readonly int _divsPerQuarter;
    private int    _syncCallCount;

    /// <summary>
    /// Set to true to emit Trace lines for every SyncAnchor call.
    /// Format:  SyncAnchor  #{n}  audio={divs}  predicted={p:F0}  err={errMs:+0.0;-0.0} ms  [{action}]
    /// </summary>
    public static bool SyncDiagnostics { get; set; } = false;

    /// <summary>BPM used for between-event position interpolation. Set this before calling Play.</summary>
    public double PlayBpm
    {
        get => _playBpm;
        set => _playBpm = Math.Max(1, value);
    }

    public long CurrentGlobalDivisions
    {
        get => _currentGlobalDivisions;
        set
        {
            // Only the stop path uses this setter during playback.
            _currentGlobalDivisions = value;
            if (value < 0)
            {
                _anchorTimestamp = 0;
                _animTimer.Stop();
            }
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Starts the 60-fps interpolation timer anchored to <paramref name="startDivisions"/>.
    /// Call once when playback begins; do NOT call again per MIDI event.
    /// </summary>
    public void StartSmoothPlay(long startDivisions)
    {
        _anchorDivisions        = startDivisions;
        _anchorTimestamp        = 0;              // clock frozen until SyncAnchor confirms audio started
        _currentGlobalDivisions = startDivisions;
        if (!_animTimer.IsEnabled) _animTimer.Start();
    }

    /// <summary>
    /// Stops the animation timer and freezes the visual at the current position.
    /// Call on pause; <see cref="CurrentGlobalDivisions"/> is preserved so resume
    /// restarts from the right spot.
    /// </summary>
    public void FreezeAtCurrentPosition()
    {
        _anchorTimestamp = 0;
        _animTimer.Stop();
        InvalidateVisual();
    }

    /// <summary>
    /// Called on every NoteOn PositionChanged event to re-lock the visual clock
    /// to the actual audio position.  NoteOff events no longer fire PositionChanged
    /// so divs is always monotonically increasing; a direct re-anchor on each call
    /// eliminates long-term drift without any visible jump.
    /// </summary>
    public void SyncAnchor(long divs)
    {
        long now = Stopwatch.GetTimestamp();
        int  call = ++_syncCallCount;
        if (SyncDiagnostics)
        {
            double bpm        = _playBpm > 0 ? _playBpm : 120.0;
            double divsPerSec = bpm / 60.0 * _divsPerQuarter;
            double predicted  = _anchorTimestamp == 0 ? divs
                : _anchorDivisions + (now - _anchorTimestamp) * divsPerSec / Stopwatch.Frequency;
            double errMs      = (divs - predicted) / divsPerSec * 1000.0;
            string tag        = _anchorTimestamp == 0 ? "INIT" : $"err={errMs:+0.0;-0.0} ms";
            Trace.WriteLine($"SyncAnchor #{call,4}  audio={divs,8}  predicted={predicted,8:F0}  [{tag}]");
        }
        _anchorDivisions = divs;
        _anchorTimestamp = now;
    }
    /// Staff 1 = right hand (green), staff 2 = left hand (blue).
    /// Changing this property automatically triggers a redraw.
    /// </summary>
    public HashSet<int> MutedStaves { get; } = new();

    public VerticalPianoRollCanvas(MxlScore score)
    {
        _score = score;
        int w = 0;
        for (int m = MinMidi; m <= MaxMidi; m++)
            if (!BlackPitchClass.Contains(m % 12)) w++;
        _totalWhiteKeys = w;
        _canvasW = _totalWhiteKeys * WhiteKeyW;

        _divsPerQuarter = score.Parts.Count > 0 && score.Parts[0].Measures.Count > 0
            ? Math.Max(1, score.Parts[0].Measures[0].Divisions) : 480;

        BuildBars();

        _animTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)   // ~60 fps
        };
        _animTimer.Tick += (_, _) =>
        {
            if (_anchorTimestamp == 0) return;
            double divsPerSec = _playBpm / 60.0 * _divsPerQuarter;
            long   elapsed    = Stopwatch.GetTimestamp() - _anchorTimestamp;
            _currentGlobalDivisions = _anchorDivisions + (long)(elapsed * divsPerSec / Stopwatch.Frequency);
            InvalidateVisual();
        };
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new Size(_canvasW, double.IsInfinity(availableSize.Height) ? 720 : availableSize.Height);

    private double MidiToX(int midi)
    {
        if (midi < MinMidi || midi > MaxMidi) return -1;
        var (wi, isBlack) = KeyLayout[midi - MinMidi];
        return isBlack ? wi * WhiteKeyW : wi * WhiteKeyW + WhiteKeyW / 2.0;
    }

    private double MidiToWidth(int midi) => WhiteKeyW - 1;

    private void BuildBars()
    {
        foreach (var part in _score.Parts)
        foreach (var measure in part.Measures)
        foreach (var note in measure.Notes)
        {
            if (note.IsRest || note.IsAbsorbed || note.MidiPitch < MinMidi || note.MidiPitch > MaxMidi) continue;
            long onset      = measure.GlobalOnsetDivisions + note.OnsetDivisions;
            long off        = onset + Math.Max(1, note.Duration);
            double x        = MidiToX(note.MidiPitch);
            double bw       = MidiToWidth(note.MidiPitch);
            int visualStaff = _score.VisualStaff(part, note);
            _bars.Add(new NoteBar(x, bw, onset, off, note.MidiPitch, visualStaff, note.Velocity));
        }
    }

    /// <summary>Rebuilds the bar list (call after MutedStaves changes to show/hide parts).</summary>
    public void RefreshBars()
    {
        _bars.Clear();
        BuildBars();
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double totalH  = Bounds.Height;
        double scrollH = Math.Max(50, totalH - KeyboardH);
        double bpm     = _playBpm > 0 ? _playBpm : (_score.DefaultBpm > 0 ? _score.DefaultBpm : 120);
        // Compute position with full double precision directly from the wall clock so
        // rendering is smooth regardless of when the timer fires.  Add LatencyMs to
        // compensate for audio-buffer delay so notes hit the cursor line on time.
        double latencyDiv  = bpm / 60.0 * _divsPerQuarter * (LatencyMs / 1000.0);
        double displayDivs = (_anchorTimestamp != 0
            ? _anchorDivisions + (Stopwatch.GetTimestamp() - _anchorTimestamp)
                                 * (bpm / 60.0 * _divsPerQuarter) / Stopwatch.Frequency
            : (_currentGlobalDivisions >= 0 ? (double)_currentGlobalDivisions : 0.0))
            + latencyDiv;

        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(20, 20, 20)),
            new Rect(0, 0, _canvasW, scrollH));

        var lanePen    = new Pen(new SolidColorBrush(Color.FromArgb(30, 200, 200, 200)), 0.5);
        var cPen       = new Pen(new SolidColorBrush(Color.FromArgb(60, 200, 200, 200)), 0.8);
        var labelBrush = new SolidColorBrush(Color.FromArgb(120, 200, 200, 200));
        var tf         = new Typeface("Consolas");

        for (int m = MinMidi; m <= MaxMidi; m++)
        {
            if (BlackPitchClass.Contains(m % 12)) continue;
            double x = KeyLayout[m - MinMidi].whiteIndex * WhiteKeyW + WhiteKeyW / 2.0;
            var pen = (m % 12 == 0) ? cPen : lanePen;
            ctx.DrawLine(pen, new Point(x, 0), new Point(x, scrollH));
            if (m % 12 == 0)
            {
                var ft = new FormattedText($"C{m / 12 - 1}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, tf, 7, labelBrush);
                ctx.DrawText(ft, new Point(x + 2, 2));
            }
        }

        // Base RGB values for each staff; alpha is scaled by velocity below.
        const byte S1R = 64,  S1G = 200, S1B = 90;   // green  – right hand
        const byte S2R = 80,  S2G = 130, S2B = 230;  // blue   – left hand
        const byte OtR = 200, OtG = 180, OtB = 80;   // yellow – other

        double divsPerSec    = bpm / 60.0 * _divsPerQuarter;
        double lookaheadDivs = divsPerSec * LookaheadSec;

        foreach (var bar in _bars)
        {
            if (MutedStaves.Contains(bar.Staff)) continue;
            double yBottom = scrollH - ((bar.GlobalOnset - displayDivs) / lookaheadDivs) * scrollH;
            double yTop    = scrollH - ((bar.GlobalOff   - displayDivs) / lookaheadDivs) * scrollH;
            if (yBottom < 0 || yTop > scrollH) continue;
            double clippedTop = Math.Max(0, yTop);
            double clippedH   = Math.Min(yBottom, scrollH) - clippedTop;
            if (clippedH <= 0) continue;

            // Alpha: map velocity 1–127 → 80–240 so even quiet notes are always visible.
            byte alpha = (byte)(80 + (int)Math.Round((bar.Velocity - 1) * (240 - 80) / 126.0));

            double fullX = bar.X - bar.W / 2.0;
            double halfW = bar.W / 2.0;
            double bx, bw;
            ISolidColorBrush brush;
            if (bar.Staff == 1)      { bx = fullX;         bw = halfW; brush = new SolidColorBrush(Color.FromArgb(alpha, S1R, S1G, S1B)); }
            else if (bar.Staff == 2) { bx = fullX + halfW; bw = halfW; brush = new SolidColorBrush(Color.FromArgb(alpha, S2R, S2G, S2B)); }
            else                     { bx = fullX;         bw = bar.W; brush = new SolidColorBrush(Color.FromArgb(alpha, OtR, OtG, OtB)); }

            ctx.FillRectangle(brush, new Rect(bx, clippedTop, bw, clippedH), (float)Math.Min(3, bw / 2));
            double edgeY = Math.Min(yBottom, scrollH - 1);
            if (edgeY - clippedTop > 2)
                ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                    new Rect(bx, edgeY - 2, bw, 2));
        }

        var activeKeys = new Dictionary<int, int>();
        if (_currentGlobalDivisions >= 0)
        {
            foreach (var bar in _bars)
            {
                if (MutedStaves.Contains(bar.Staff)) continue;
                if (displayDivs >= bar.GlobalOnset && displayDivs < bar.GlobalOff)
                {
                    int bit = bar.Staff == 1 ? 1 : bar.Staff == 2 ? 2 : 4;
                    activeKeys[bar.MidiPitch] = activeKeys.GetValueOrDefault(bar.MidiPitch) | bit;
                }
            }
        }

        double kbY            = scrollH;
        var whiteKeyBrush     = new SolidColorBrush(Color.FromRgb(240, 240, 240));
        var blackKeyBrush     = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        var whiteKeyPen       = new Pen(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 0.5);
        var activeS1          = new SolidColorBrush(Color.FromArgb(230, 64, 200, 90));
        var activeS2          = new SolidColorBrush(Color.FromArgb(230, 80, 130, 230));

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

        for (int m = MinMidi; m <= MaxMidi; m++)
        {
            var (wi, isBlack) = KeyLayout[m - MinMidi];
            if (!isBlack) continue;
            double kx   = wi * WhiteKeyW - BlackKeyW / 2.0;
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

// ─────────────────────────────────────────────────────────────────────────────
//  StaffNotationCanvas  — scrolling treble + bass staff view
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scrolling traditional staff notation (treble + bass clef) that stays in sync
/// with <see cref="VerticalPianoRollCanvas"/> via <see cref="CurrentGlobalDivisions"/>.
/// Notes scroll from right to left; a vertical "now" cursor is drawn at 25 % from the left.
/// </summary>
public sealed class StaffNotationCanvas : Control
{
    // ── constants ────────────────────────────────────────────────────────────
    private const double LineSpacing  = 11.0;   // pixels between staff lines
    private const double NoteRadiusX  = 5.5;    // oval x-radius
    private const double NoteRadiusY  = 4.0;    // oval y-radius
    private const double StemLength   = 32.0;
    private const double LookaheadSec = 4.0;
        private const double CursorFrac   = 0.25;   // "now" line position (0=left 1=right)
        private const double LatencyMs    = 60.0;   // audio-buffer latency compensation (ms)
    private const double ClefAreaW    = 48.0;   // left margin reserved for clef glyph

    // Diatonic step within octave (C=0 D=1 E=2 F=3 G=4 A=5 B=6)
    private static readonly int[] MidiPcToDiatonic = { 0,-1, 1,-1, 2, 3,-1, 4,-1, 5,-1, 6 };
    private static readonly bool[] MidiPcIsSharp   = { false,true,false,true,false,false,true,false,true,false,true,false };

    // Written step letter → diatonic position within octave (C=0 … B=6)
    private static int StepToDiatonicInOctave(string step) => step switch
    {
        "C" => 0, "D" => 1, "E" => 2, "F" => 3,
        "G" => 4, "A" => 5, "B" => 6, _ => 0
    };

    // MIDI → diatonic pitch row (C0 = 0, D0 = 1, … C1 = 7, …)
    private static int MidiToDiatonicRow(int midi)
    {
        int octave = midi / 12 - 1;
        int pc     = midi % 12;
        int dia    = MidiPcToDiatonic[pc];
        if (dia < 0) dia = MidiPcToDiatonic[pc - 1] + 1;
        return octave * 7 + dia;
    }

    // Middle-line notes: Treble = B4 (MIDI 71), Bass = D3 (MIDI 50)
    private static readonly int TrebleMiddleRow = MidiToDiatonicRow(71);
    private static readonly int BassMiddleRow   = MidiToDiatonicRow(50);

    // ── state ────────────────────────────────────────────────────────────────
    private readonly MxlScore _score;
    private readonly int      _divsPerQuarter;

    private sealed record StaffNote(
        long   GlobalOnset, long GlobalOff,
        int    DiatonicRow, string Accidental,
        int    Staff,       int  Velocity,
        int    MidiPitch,   long MeasureOnsetDivisions,
        string NoteType);

    private sealed record StaffRest(
        long GlobalOnset, int Staff, string NoteType);

    private readonly List<StaffNote>              _notes    = new();
    private readonly List<StaffRest>              _rests    = new();
    private readonly List<(long Divs, int Number)> _barlines = new();
    public HashSet<int> MutedStaves { get; } = new();

    private long _currentGlobalDivisions = -1;
    private long   _anchorDivisions;
    private long   _anchorTimestamp;
    private double _playBpm = 120;
    private readonly DispatcherTimer _animTimer;

    /// <summary>BPM used for between-event position interpolation. Set this before calling Play.</summary>
    public double PlayBpm
    {
        get => _playBpm;
        set => _playBpm = Math.Max(1, value);
    }

    public long CurrentGlobalDivisions
    {
        get => _currentGlobalDivisions;
        set
        {
            _currentGlobalDivisions = value;
            if (value < 0)
            {
                _anchorTimestamp = 0;
                _animTimer.Stop();
            }
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Starts the 60-fps interpolation timer anchored to <paramref name="startDivisions"/>.
    /// Call once when playback begins; do NOT call again per MIDI event.
    /// </summary>
    public void StartSmoothPlay(long startDivisions)
    {
        _anchorDivisions        = startDivisions;
        _anchorTimestamp        = 0;              // clock frozen until SyncAnchor confirms audio started
        _currentGlobalDivisions = startDivisions;
        if (!_animTimer.IsEnabled) _animTimer.Start();
    }

    /// <summary>
    /// Stops the animation timer and freezes the visual at the current position.
    /// </summary>
    public void FreezeAtCurrentPosition()
    {
        _anchorTimestamp = 0;
        _animTimer.Stop();
        InvalidateVisual();
    }

    /// <summary>
    /// <summary>
    /// Called on every NoteOn PositionChanged event to re-lock the visual clock.
    /// Direct re-anchor; see VerticalPianoRollCanvas.SyncAnchor for the rationale.
    /// </summary>
    public void SyncAnchor(long divs)
    {
        _anchorDivisions = divs;
        _anchorTimestamp = Stopwatch.GetTimestamp();
    }

    public StaffNotationCanvas(MxlScore score)
    {
        _score = score;
        _divsPerQuarter = score.Parts.Count > 0 && score.Parts[0].Measures.Count > 0
            ? Math.Max(1, score.Parts[0].Measures[0].Divisions) : 480;

        foreach (var part in score.Parts)
        foreach (var measure in part.Measures)
        {
            foreach (var note in measure.Notes)
            {
                long onset = measure.GlobalOnsetDivisions + note.OnsetDivisions;
                int  staff = score.VisualStaff(part, note);

                if (note.IsRest)
                {
                    _rests.Add(new StaffRest(onset, staff, note.NoteType));
                    continue;
                }
                if (note.MidiPitch < 21 || note.MidiPitch > 108) continue;

                // Use the WRITTEN pitch step+octave so that e.g. D# lands on D's line
                // (not E's), and Eb lands on E's line (not D's).
                int octave = int.TryParse(note.Octave, out var o) ? o : 4;
                int diaRow = octave * 7 + StepToDiatonicInOctave(note.Pitch);
                long off   = onset + Math.Max(1, note.Duration);
                _notes.Add(new StaffNote(onset, off,
                    diaRow, note.Accidental,
                    staff, note.Velocity, note.MidiPitch,
                    measure.GlobalOnsetDivisions, note.NoteType));
            }
            if (!_barlines.Any(b => b.Divs == measure.GlobalOnsetDivisions))
                _barlines.Add((measure.GlobalOnsetDivisions, measure.Number));
        }
        _notes.Sort((a, b) => a.GlobalOnset.CompareTo(b.GlobalOnset));
        _barlines.Sort((a, b) => a.Divs.CompareTo(b.Divs));

        _animTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)   // ~60 fps
        };
        _animTimer.Tick += (_, _) =>
        {
            if (_anchorTimestamp == 0) return;
            double divsPerSec = _playBpm / 60.0 * _divsPerQuarter;
            long   elapsed    = Stopwatch.GetTimestamp() - _anchorTimestamp;
            _currentGlobalDivisions = _anchorDivisions + (long)(elapsed * divsPerSec / Stopwatch.Frequency);
            InvalidateVisual();
        };
    }

    /// <summary>Large title painted at the top of the staff canvas (e.g. current song in playlist mode).</summary>
    public string HeaderLine1 { get; set; } = string.Empty;
    /// <summary>Smaller subtitle painted below <see cref="HeaderLine1"/> (e.g. "Next: …").</summary>
    public string HeaderLine2 { get; set; } = string.Empty;

    public void RefreshNotes() => InvalidateVisual();

    protected override Size MeasureOverride(Size available) =>
        new Size(double.IsInfinity(available.Width)  ? 600 : available.Width,
                 double.IsInfinity(available.Height) ? 720 : available.Height);

    public override void Render(DrawingContext ctx)
    {
        double W = Bounds.Width;
        double H = Bounds.Height;
        if (W < 20 || H < 20) return;

        double bpm          = _playBpm > 0 ? _playBpm : (_score.DefaultBpm > 0 ? _score.DefaultBpm : 120);
        double divsPerSec   = bpm / 60.0 * _divsPerQuarter;
        double lookaheadDiv = divsPerSec * LookaheadSec;
        // Compute with full double precision from the wall clock so rendering is
        // smooth regardless of when the timer fires.  Add LatencyMs to compensate
        // for audio-buffer delay so notes hit the cursor line on time.
        double latencyDiv   = bpm / 60.0 * _divsPerQuarter * (LatencyMs / 1000.0);
        double displayDivs  = (_anchorTimestamp != 0
            ? _anchorDivisions + (Stopwatch.GetTimestamp() - _anchorTimestamp)
                                 * (bpm / 60.0 * _divsPerQuarter) / Stopwatch.Frequency
            : (_currentGlobalDivisions >= 0 ? (double)_currentGlobalDivisions : 0.0))
            + latencyDiv;
        double nowX         = W * CursorFrac;

        // Global-divisions → pixel X (notes to the right of nowX scroll leftward)
        double DivsToX(double d) => nowX + (d - displayDivs) / lookaheadDiv * (W * (1 - CursorFrac));

        // ── background ────────────────────────────────────────────────────
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(250, 248, 240)), new Rect(0, 0, W, H));

        // ── header overlay (playlist / pattern title) ─────────────────────
        double headerY = 6.0;
        if (!string.IsNullOrEmpty(HeaderLine1))
        {
            var ft1 = new FormattedText(HeaderLine1, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial", FontStyle.Normal, FontWeight.SemiBold), 16,
                new SolidColorBrush(Color.FromRgb(40, 40, 120)));
            ctx.DrawText(ft1, new Point(ClefAreaW + 8, headerY));
            headerY += ft1.Height + 2;
        }
        if (!string.IsNullOrEmpty(HeaderLine2))
        {
            var ft2 = new FormattedText(HeaderLine2, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial", FontStyle.Normal, FontWeight.Normal), 12,
                new SolidColorBrush(Color.FromRgb(100, 100, 180)));
            ctx.DrawText(ft2, new Point(ClefAreaW + 8, headerY));
        }

        // ── staff layout ──────────────────────────────────────────────────
        // Each staff section occupies half the height.
        // The five-line staff is centered vertically in its section.
        double staffSpan  = 4 * LineSpacing;
        double staffGap   = LineSpacing * 2;   // gap between the two staves

        double trebleTopY = H / 2.0 - staffSpan - staffGap;
        double bassTopY   = H / 2.0 + staffGap;
        double trebleMidY = trebleTopY + 2 * LineSpacing;  // middle (3rd) line Y
        double bassMidY   = bassTopY   + 2 * LineSpacing;

        // diatonic row → canvas Y
        double RowToY(int dRow, bool isTreble)
        {
            int    refRow = isTreble ? TrebleMiddleRow : BassMiddleRow;
            double midY   = isTreble ? trebleMidY : bassMidY;
            return midY - (dRow - refRow) * (LineSpacing / 2.0);
        }

        // Staff top/bottom row numbers (each staff line is 2 diatonic steps apart)
        int TrebleTopRow = TrebleMiddleRow + 4;  // line 0 (top)
        int TrebleBotRow = TrebleMiddleRow - 4;  // line 4 (bottom)
        int BassTopRow   = BassMiddleRow   + 4;
        int BassBotRow   = BassMiddleRow   - 4;

        var inkBrush  = new SolidColorBrush(Color.FromRgb(20, 20, 20));
        var inkPen    = new Pen(inkBrush, 1.2);
        var staffPen  = new Pen(new SolidColorBrush(Color.FromRgb(60, 60, 60)), 0.9);
        var barPen    = new Pen(new SolidColorBrush(Color.FromRgb(60, 60, 60)), 1.4);
        var cursorPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 220, 50, 50)), 1.8);
        var tf        = new Typeface("Arial");

        // ── draw five staff lines ──────────────────────────────────────────
        void DrawStaffLines(double topY)
        {
            for (int i = 0; i < 5; i++)
                ctx.DrawLine(staffPen, new Point(0, topY + i * LineSpacing),
                                       new Point(W, topY + i * LineSpacing));
        }
        DrawStaffLines(trebleTopY);
        DrawStaffLines(bassTopY);



        // ── clef symbols ──────────────────────────────────────────────────────
        // On Windows "Segoe UI Symbol" carries U+1D11E (G clef) and U+1D122 (F clef)
        // from the SMuFL supplementary block.  On macOS/Linux that font is absent so
        // we fall back to primitive geometry which still looks reasonable.
        double g4Y  = RowToY(MidiToDiatonicRow(67), true);   // G4 line
        double f3Y  = RowToY(MidiToDiatonicRow(53), false);  // F3 line
        bool useGlyphs = OperatingSystem.IsWindows();
        if (useGlyphs)
        {
            // U+1D11E / U+1D122 are supplementary-plane chars (surrogate pairs in C#).
            var clefTf = new Typeface(new FontFamily("Segoe UI Symbol,Arial Unicode MS"),
                                      FontStyle.Normal, FontWeight.Regular);
            // Treble
            {
                string gClef = "\uD834\uDD1E";   // U+1D11E MUSICAL SYMBOL G CLEF
                var ft = new FormattedText(gClef, CultureInfo.InvariantCulture,
                             FlowDirection.LeftToRight, clefTf, 4 * LineSpacing * 1.6, inkBrush);
                ctx.DrawText(ft, new Point(2, g4Y - ft.Height * 0.72));
            }
            // Bass
            {
                string fClef = "\uD834\uDD22";   // U+1D122 MUSICAL SYMBOL F CLEF
                var ft = new FormattedText(fClef, CultureInfo.InvariantCulture,
                             FlowDirection.LeftToRight, clefTf, 4 * LineSpacing * 0.9, inkBrush);
                ctx.DrawText(ft, new Point(2, f3Y - ft.Height * 0.38));
            }
        }
        else
        {
            // Primitive fallback (macOS / Linux): bold text labels "G" / "F" + dot pair.
            var clefTf  = new Typeface("sans-serif", FontStyle.Italic, FontWeight.Bold);
            var cp      = new Pen(inkBrush, 1.5);
            // Treble: italic bold "G" centred on g4Y
            {
                var ft = new FormattedText("G", CultureInfo.InvariantCulture,
                             FlowDirection.LeftToRight, clefTf, 3 * LineSpacing, inkBrush);
                ctx.DrawText(ft, new Point(2, g4Y - ft.Height * 0.55));
                // small descending stem below
                ctx.DrawLine(cp, new Point(ft.Width / 2 + 2, g4Y + LineSpacing * 0.5),
                                 new Point(ft.Width / 2 + 2, g4Y + LineSpacing * 1.5));
            }
            // Bass: italic bold "F" + two dots
            {
                var ft = new FormattedText("F", CultureInfo.InvariantCulture,
                             FlowDirection.LeftToRight, clefTf, 2.5 * LineSpacing, inkBrush);
                ctx.DrawText(ft, new Point(2, f3Y - ft.Height * 0.45));
                double dotX = ft.Width + 6;
                ctx.DrawEllipse(inkBrush, null, new Point(dotX, f3Y - LineSpacing * 0.55), 2.0, 2.0);
                ctx.DrawEllipse(inkBrush, null, new Point(dotX, f3Y + LineSpacing * 0.25), 2.0, 2.0);
            }
        }

        // ── barlines ──────────────────────────────────────────────────────
        foreach (var (bd, bn) in _barlines)
        {
            double bx = DivsToX(bd) - 18;   // shift left so barline doesn't overlap note heads
            if (bx < ClefAreaW - 2 || bx > W) continue;
            // span each staff fully (top line to bottom line)
            ctx.DrawLine(barPen, new Point(bx, trebleTopY), new Point(bx, trebleTopY + 4 * LineSpacing));
            ctx.DrawLine(barPen, new Point(bx, bassTopY),   new Point(bx, bassTopY   + 4 * LineSpacing));
            // measure number above treble staff
            if (bn >= 1)
            {
                var ft = new FormattedText($"{bn}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, tf, 11,
                    new SolidColorBrush(Color.FromRgb(80, 80, 80)));
                ctx.DrawText(ft, new Point(bx + 2, trebleTopY - LineSpacing - 2));
            }
        }

        // ── "now" cursor ──────────────────────────────────────────────────
        ctx.DrawLine(cursorPen, new Point(nowX, 0), new Point(nowX, H));

        // ── notes ─────────────────────────────────────────────────────────
        foreach (var n in _notes)
        {
            if (MutedStaves.Contains(n.Staff)) continue;
            double x = DivsToX(n.GlobalOnset);
            if (x < -NoteRadiusX * 4 || x > W + NoteRadiusX * 4) continue;

            bool isTreble = n.Staff == 1;
            double y      = RowToY(n.DiatonicRow, isTreble);
            int topRow    = isTreble ? TrebleTopRow : BassTopRow;
            int botRow    = isTreble ? TrebleBotRow : BassBotRow;
            int refRow    = isTreble ? TrebleMiddleRow : BassMiddleRow;
            int rowDelta  = n.DiatonicRow - refRow;

            // Filled vs open head: whole/half = open; quarter/eighth/16th = filled
            bool isOpen = n.NoteType is "whole" or "half";
            var noteBrush = isOpen ? (IBrush)new SolidColorBrush(Colors.Transparent) : inkBrush;
            var noteHeadPen = new Pen(inkBrush, 1.3);

            ctx.DrawEllipse(noteBrush, noteHeadPen, new Point(x, y), NoteRadiusX, NoteRadiusY);

            // Stem for non-whole notes
            if (n.NoteType != "whole")
            {
                bool stemUp = rowDelta <= 0;
                double stemX  = stemUp ? x + NoteRadiusX - 0.5 : x - NoteRadiusX + 0.5;
                double stemY0 = stemUp ? y - NoteRadiusY : y + NoteRadiusY;
                double stemY1 = stemUp ? stemY0 - StemLength : stemY0 + StemLength;
                ctx.DrawLine(inkPen, new Point(stemX, stemY0), new Point(stemX, stemY1));

                // Flag for eighth notes (single flag)
                if (n.NoteType is "eighth")
                {
                    double fx = stemX;
                    double fy = stemY1;
                    double flagDir = stemUp ? 1 : -1;
                    ctx.DrawLine(new Pen(inkBrush, 1.5),
                        new Point(fx, fy),
                        new Point(fx + 8,  fy + flagDir * 8));
                    ctx.DrawLine(new Pen(inkBrush, 1.5),
                        new Point(fx + 8,  fy + flagDir * 8),
                        new Point(fx + 2,  fy + flagDir * 14));
                }
                // Two flags for 16th notes
                else if (n.NoteType is "16th")
                {
                    double fx = stemX;
                    double fy = stemY1;
                    double flagDir = stemUp ? 1 : -1;
                    for (int fi = 0; fi < 2; fi++)
                    {
                        double fyo = fy + flagDir * fi * 6;
                        ctx.DrawLine(new Pen(inkBrush, 1.5),
                            new Point(fx, fyo),
                            new Point(fx + 8, fyo + flagDir * 8));
                        ctx.DrawLine(new Pen(inkBrush, 1.5),
                            new Point(fx + 8, fyo + flagDir * 8),
                            new Point(fx + 2, fyo + flagDir * 14));
                    }
                }
            }

            // ── ledger lines ──────────────────────────────────────────────
            // Draw a ledger line for every even diatonic row outside the staff
            for (int lr = topRow + 2; lr <= n.DiatonicRow; lr += 2)
            {
                double ly = RowToY(lr, isTreble);
                ctx.DrawLine(staffPen,
                    new Point(x - NoteRadiusX * 2, ly),
                    new Point(x + NoteRadiusX * 2, ly));
            }
            for (int lr = botRow - 2; lr >= n.DiatonicRow; lr -= 2)
            {
                double ly = RowToY(lr, isTreble);
                ctx.DrawLine(staffPen,
                    new Point(x - NoteRadiusX * 2, ly),
                    new Point(x + NoteRadiusX * 2, ly));
            }

            // -- accidental --
            if (!string.IsNullOrEmpty(n.Accidental))
            {
                string sym = n.Accidental switch
                {
                    "sharp" or "natural-sharp" or "sharp-up"  => "♯",
                    "flat"  or "natural-flat"  or "flat-down" => "♭",
                    "natural"                                  => "♮",
                    _ => string.Empty
                };
                if (sym.Length > 0)
                {
                    var accTf = new Typeface("Arial", FontStyle.Normal, FontWeight.Bold);
                    var ft = new FormattedText(sym, CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, accTf, 20, inkBrush);
                    // Place the glyph immediately left of the notehead, vertically centred on y.
                    // ft.Height is the full bounding-box height; musical symbols have their
                    // visual centre at ~55 % from the top, so offset by 0.55 * ft.Height.
                    double ax = x - NoteRadiusX - ft.Width - 1;
                    double ay = y - ft.Height * 0.55;
                    ctx.DrawText(ft, new Point(ax, ay));
                }
            }
        }

        // -- rests --
        foreach (var r in _rests)
        {
            if (MutedStaves.Contains(r.Staff)) continue;
            double rx = DivsToX(r.GlobalOnset);
            if (rx < -20 || rx > W + 20) continue;

            bool   isTreble = r.Staff == 1;
            double rTopY    = isTreble ? trebleTopY : bassTopY;
            double rMidY    = isTreble ? trebleMidY : bassMidY;

            switch (r.NoteType)
            {
                case "whole":
                    ctx.FillRectangle(inkBrush, new Rect(rx - 5, rTopY + LineSpacing - 3, 10, 4));
                    break;
                case "half":
                    ctx.DrawRectangle(null, inkPen, new Rect(rx - 5, rMidY - 4, 10, 4));
                    break;
                case "quarter":
                {
                    var rp = new Pen(inkBrush, 1.5);
                    ctx.DrawLine(rp, new Point(rx + 2, rMidY - 7), new Point(rx + 6, rMidY - 2));
                    ctx.DrawLine(rp, new Point(rx + 6, rMidY - 2), new Point(rx - 2, rMidY + 3));
                    ctx.DrawLine(rp, new Point(rx - 2, rMidY + 3), new Point(rx + 3, rMidY + 9));
                    ctx.DrawEllipse(inkBrush, null, new Point(rx + 1, rMidY + 11), 2.5, 2.5);
                    break;
                }
                default:
                {
                    var rp = new Pen(inkBrush, 1.5);
                    ctx.DrawLine(rp, new Point(rx + 3, rMidY - 6), new Point(rx - 2, rMidY + 4));
                    ctx.DrawEllipse(inkBrush, null, new Point(rx + 3, rMidY - 6), 2.5, 2.5);
                    break;
                }
            }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  PianoRollPlayerControl
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Configuration options for <see cref="PianoRollPlayerControl"/>.
/// All properties have sensible defaults; only set what you need to change.
/// </summary>
public sealed class PianoRollOptions
{
    /// <summary>Initial measure to start playback from (1-based). Default 1.</summary>
    public int    StartMeasure      { get; init; } = 1;
    /// <summary>Auto-close the host window when playback ends naturally.</summary>
    public bool   AutoCloseOnEnd    { get; init; } = false;
    /// <summary>Show the measure-seek slider (appropriate for single-song mode).</summary>
    public bool   ShowMeasureSlider { get; init; } = true;
    /// <summary>Show the Log Notes checkbox.</summary>
    public bool   ShowLogNotes      { get; init; } = false;
    /// <summary>Show a ⏭ Skip button after the play/pause button.</summary>
    public bool   ShowSkipButton    { get; init; } = false;
    /// <summary>Optional control placed at the left of the toolbar (e.g. pattern ComboBox).</summary>
    public Control? LeadingControl  { get; init; } = null;
    /// <summary>Initial FluidSynth checkbox state. Null = read from AppSettings.</summary>
    public bool?  LogNotesDefault   { get; init; } = false;
}

/// <summary>
/// A self-contained Avalonia <see cref="UserControl"/> that embeds a
/// <see cref="VerticalPianoRollCanvas"/>, a <see cref="StaffNotationCanvas"/>,
/// and a full playback toolbar (BPM, measure, LH/RH mutes, FluidSynth/WinMM picker).
/// <para>
/// Call <see cref="LoadScore"/> to load (and optionally auto-play) a score.
/// The host window only needs to call <see cref="StopAll"/> when it closes.
/// </para>
/// </summary>
public sealed class PianoRollPlayerControl : UserControl
{
    // ── public surface ────────────────────────────────────────────────────────
    /// <summary>Raised on the UI thread when playback ends naturally (not when stopped/paused).</summary>
    public event EventHandler? PlaybackEnded;
    /// <summary>Raised on the UI thread when the current measure number changes during playback.</summary>
    public event EventHandler<int>? MeasureChanged;

    /// <summary>The staff canvas — caller may set HeaderLine1/HeaderLine2 for overlay text.</summary>
    public StaffNotationCanvas StaffCanvas { get; }

    // ── private state ─────────────────────────────────────────────────────────
    private readonly PianoRollOptions    _opts;
    private readonly bool                _isWindows;
    private readonly IReadOnlyList<(int Id, string Name)> _winmmDevices;

    // toolbar refs needed outside ctor
    private readonly Button   _playPauseBtn;
    private readonly Slider   _bpmSlider;
    private readonly TextBlock _bpmLabel;
    private readonly Slider?  _measureSlider;
    private readonly TextBlock? _measureLabel;
    private readonly TextBlock  _statusBlock;
    private readonly CheckBox   _fluidChk;
    private readonly CheckBox   _midiDeviceLabel_asChk; // kept as Control in toolbar
    private readonly ComboBox   _midiDeviceCombo;
    private readonly CheckBox   _rhChk;
    private readonly CheckBox   _lhChk;
    private readonly CheckBox?  _logNotesChk;
    private readonly Button?    _skipBtn;
    private readonly Grid       _contentGrid;

    // playback state
    private VerticalPianoRollCanvas? _canvas;
    private MxlScore?                _currentScore;
    private MxlMidiPlayer?           _player;
    private readonly SemaphoreSlim   _midiLock = new(1, 1);  // serialises all stop/open ops
    private int                      _startGen;              // incremented each StartPlayer call
    private bool                     _suppressMeasureSync;
    private List<(long Divs, int Number)> _measureDivMap = new();
    private int                      _totalMeasures = 1;
    private Window?                  _hostWindow;

    // sleep prevention
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);
    private const uint ES_CONTINUOUS       = 0x80000000u;
    private const uint ES_SYSTEM_REQUIRED  = 0x00000001u;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002u;

    private static void PreventSleep()  => SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
    private static void AllowSleep()    => SetThreadExecutionState(ES_CONTINUOUS);

    // debounce tokens
    private CancellationTokenSource? _bpmDebCts;
    private CancellationTokenSource? _measureDebCts;

    public PianoRollPlayerControl(PianoRollOptions? options = null)
    {
        _opts      = options ?? new PianoRollOptions();
        _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        _winmmDevices = _isWindows ? WinmmMidiBackend.EnumerateDevices()
                                   : Array.Empty<(int, string)>();

        // ── content grid (piano roll left | staff right) ──────────────────
        _contentGrid = new Grid();
        _contentGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _contentGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        StaffCanvas = new StaffNotationCanvas(new MxlScore());   // placeholder; replaced by LoadScore

        // ── toolbar controls ──────────────────────────────────────────────
        _playPauseBtn = new Button
        {
            Content = "▶  Play",
            Margin  = new Thickness(4),
            Padding = new Thickness(8, 2)
        };
        _playPauseBtn.Click += OnPlayPauseClick;

        _skipBtn = _opts.ShowSkipButton ? new Button
        {
            Content = "⏭  Skip",
            Margin  = new Thickness(4),
            Padding = new Thickness(8, 2),
            [ToolTip.TipProperty] = "Skip to next song"
        } : null;

        _bpmSlider = new Slider
        {
            Minimum = 40, Maximum = 300, Value = 120,
            Width   = 180,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [ToolTip.TipProperty] = "Tempo (BPM)"
        };
        _bpmLabel = new TextBlock
        {
            Text  = "120 BPM", Width = 60,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 12
        };
        _bpmSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            _bpmLabel.Text = $"{_bpmSlider.Value:F0} BPM";
            if (_player == null) return;
            _bpmDebCts?.Cancel();
            _bpmDebCts = new CancellationTokenSource();
            var ct = _bpmDebCts.Token;
            Task.Delay(300, ct).ContinueWith(_ =>
            {
                if (!ct.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() => { if (_player != null) StartPlayer((int)(_measureSlider?.Value ?? 1), _bpmSlider.Value); });
            }, TaskScheduler.Default);
        };

        if (_opts.ShowMeasureSlider)
        {
            _measureSlider = new Slider
            {
                Minimum = 1, Maximum = 1, Value = _opts.StartMeasure,
                Width   = 260,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                [ToolTip.TipProperty] = "Jump to measure (drag to seek)"
            };
            _measureLabel = new TextBlock
            {
                Text = "M 1/1", Width = 72,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 12
            };
            _measureSlider.PropertyChanged += OnMeasureSliderChanged;
        }

        _rhChk = new CheckBox
        {
            Content = "RH", IsChecked = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 2, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(64, 200, 90)),
            [ToolTip.TipProperty] = "Enable right-hand / staff 1 (green)"
        };
        _lhChk = new CheckBox
        {
            Content = "LH", IsChecked = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(80, 130, 230)),
            [ToolTip.TipProperty] = "Enable left-hand / staff 2 (blue)"
        };
        _rhChk.IsCheckedChanged += (_, _) => ApplyMutes();
        _lhChk.IsCheckedChanged += (_, _) => ApplyMutes();

        _fluidChk = new CheckBox
        {
            Content   = "FluidSynth",
            IsChecked = SheetMusicLib.AppSettings.Instance.PianoRollUseFluidSynth ?? true,
            IsVisible = _isWindows,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            [ToolTip.TipProperty] = "FluidSynth (better sound). Unchecked = WinMM."
        };
        _fluidChk.IsCheckedChanged += (_, _) =>
        {
            UpdateDevicePickerVisibility();
            SheetMusicLib.AppSettings.Instance.PianoRollUseFluidSynth = _fluidChk.IsChecked == true;
            SheetMusicLib.AppSettings.Instance.SaveLocal();
        };

        // WinMM device picker (label re-purposed as TextBlock via a plain TextBlock to save casts)
        var midiLbl = new TextBlock
        {
            Text = "MIDI out:",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 2, 0),
            IsVisible = false
        };
        _midiDeviceLabel_asChk = new CheckBox { IsVisible = false }; // unused; kept for field compat
        _midiDeviceCombo = new ComboBox
        {
            MinWidth = 180,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            IsVisible = false,
            [ToolTip.TipProperty] = "WinMM MIDI output device"
        };
        _midiDeviceCombo.ItemsSource = _winmmDevices.Select(d => d.Name).ToList();
        if (_isWindows && _winmmDevices.Count > 0)
        {
            string saved = SheetMusicLib.AppSettings.Instance.PianoRollWinmmDeviceName;
            int restoreIdx = string.IsNullOrEmpty(saved)
                ? 0
                : Math.Max(0, _winmmDevices.ToList().FindIndex(d => d.Name == saved));
            _midiDeviceCombo.SelectedIndex = restoreIdx;
        }
        _midiDeviceCombo.SelectionChanged += (_, _) =>
        {
            int idx = _midiDeviceCombo.SelectedIndex;
            string name = (idx >= 0 && idx < _winmmDevices.Count) ? _winmmDevices[idx].Name : string.Empty;
            SheetMusicLib.AppSettings.Instance.PianoRollWinmmDeviceName = name;
            SheetMusicLib.AppSettings.Instance.SaveLocal();
        };
        UpdateDevicePickerVisibility();

        _logNotesChk = _opts.ShowLogNotes ? new CheckBox
        {
            Content = "Log notes",
            IsChecked = _opts.LogNotesDefault == true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            [ToolTip.TipProperty] = "Write every NoteOn to Trace while playing"
        } : null;

        _statusBlock = new TextBlock
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 12, Margin = new Thickness(8, 0)
        };

        // ── assemble toolbar ──────────────────────────────────────────────
        var toolbar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin      = new Thickness(4)
        };
        if (_opts.LeadingControl != null) toolbar.Children.Add(_opts.LeadingControl);
        toolbar.Children.Add(_playPauseBtn);
        if (_skipBtn != null)    toolbar.Children.Add(_skipBtn);
        toolbar.Children.Add(_bpmSlider);
        toolbar.Children.Add(_bpmLabel);
        if (_measureSlider != null) { toolbar.Children.Add(_measureSlider); toolbar.Children.Add(_measureLabel!); }
        toolbar.Children.Add(_lhChk);
        toolbar.Children.Add(_rhChk);
        toolbar.Children.Add(_fluidChk);
        toolbar.Children.Add(midiLbl);
        toolbar.Children.Add(_midiDeviceCombo);
        if (_logNotesChk != null) toolbar.Children.Add(_logNotesChk);
        toolbar.Children.Add(_statusBlock);

        // ── layout ────────────────────────────────────────────────────────
        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(_contentGrid);
        Content = layout;

        // Wire up host-window lookup when we are attached
        AttachedToVisualTree += (_, _) =>
            _hostWindow = this.FindAncestorOfType<Window>();
    }

    // ── public methods ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads and optionally auto-plays a score.  Safe to call multiple times
    /// (stops the current player first).
    /// </summary>
    public void LoadScore(MxlScore score, int startMeasure = 1, bool autoPlay = true)
    {
        StopAll();
        _currentScore = score;

        // Rebuild canvases
        var canvas      = new VerticalPianoRollCanvas(score);
        var staffCanvas = StaffCanvas;    // reuse the same StaffNotationCanvas instance's type but need a fresh one
        // Replace the StaffNotationCanvas content by building a new one and swapping
        // (the property is readonly so we swap via the content grid)
        var newStaff = new StaffNotationCanvas(score)
        {
            HeaderLine1 = StaffCanvas.HeaderLine1,
            HeaderLine2 = StaffCanvas.HeaderLine2
        };
        // Update the public property via reflection trick — actually just expose a method
        ReplaceCanvases(canvas, newStaff);

        _canvas = canvas;

        // Rebuild measure map
        _totalMeasures = score.Parts.Count > 0 ? score.Parts.Max(p => p.Measures.Count) : 1;
        _measureDivMap = score.Parts.Count > 0
            ? score.Parts[0].Measures
                .Select(m => (Divs: m.GlobalOnsetDivisions, m.Number))
                .OrderBy(x => x.Divs).ToList()
            : new List<(long, int)>();

        if (_measureSlider != null)
        {
            _suppressMeasureSync = true;
            _measureSlider.Maximum = Math.Max(1, _totalMeasures);
            _measureSlider.Value   = Math.Max(1, Math.Min(startMeasure, _totalMeasures));
            _suppressMeasureSync = false;
            _measureLabel!.Text  = $"M {(int)_measureSlider.Value}/{_totalMeasures}";
        }

        _bpmSlider.Value = score.DefaultBpm;
        _statusBlock.Text = $"Stopped  |  {score.Title}";
        _playPauseBtn.Content = "▶  Play";

        if (autoPlay)
            Dispatcher.UIThread.Post(() =>
                _playPauseBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)),
                DispatcherPriority.Background);
    }

    /// <summary>Sets the now/next text drawn inside the staff canvas header area.</summary>
    public void SetHeaderText(string line1, string line2 = "")
    {
        // Find the current StaffNotationCanvas in the grid
        var sc = _contentGrid.Children.OfType<StaffNotationCanvas>().FirstOrDefault();
        if (sc == null) return;
        sc.HeaderLine1 = line1;
        sc.HeaderLine2 = line2;
        sc.RefreshNotes();
    }

    /// <summary>Registers a callback for the ⏭ Skip button. Only relevant when ShowSkipButton=true.</summary>
    public void SetSkipAction(Action onSkip)
    {
        if (_skipBtn != null)
            _skipBtn.Click += (_, _) => onSkip();
    }

    /// <summary>Stops playback and disposes the current player. Safe to call multiple times.</summary>
    public void StopAll()
    {
        var p = _player;
        _player = null;
        if (_canvas != null) { _canvas.CurrentGlobalDivisions = -1; }
        var sc = _contentGrid.Children.OfType<StaffNotationCanvas>().FirstOrDefault();
        if (sc != null) sc.CurrentGlobalDivisions = -1;
        if (_measureSlider != null) _measureSlider.Value = 1;
        _playPauseBtn.Content = "▶  Play";
        if (_isWindows) AllowSleep();
        // Stop on a background thread, holding _midiLock so a concurrent StartPlayer
        // cannot call midiOutOpen until this midiOutClose is fully complete.
        Interlocked.Increment(ref _startGen);  // invalidate any pending StartPlayer Task
        if (p != null)
            Task.Run(async () =>
            {
                await _midiLock.WaitAsync().ConfigureAwait(false);
                try   { p.Stop(); p.Dispose(); } catch { }
                finally { _midiLock.Release(); }
            });
    }

    // ── private helpers ───────────────────────────────────────────────────────

    private void ReplaceCanvases(VerticalPianoRollCanvas canvas, StaffNotationCanvas staff)
    {
        _contentGrid.Children.Clear();
        Grid.SetColumn(canvas, 0);
        Grid.SetColumn(staff,  1);
        _contentGrid.Children.Add(canvas);
        _contentGrid.Children.Add(staff);
        // Transfer mute state
        ApplyMutesToCanvas(canvas);
        ApplyMutesToStaff(staff);
    }

    private void ApplyMutes()
    {
        if (_canvas is { } cv) ApplyMutesToCanvas(cv);
        var sc = _contentGrid.Children.OfType<StaffNotationCanvas>().FirstOrDefault();
        if (sc != null) ApplyMutesToStaff(sc);
        if (_player is { } pr)
        {
            if (_rhChk.IsChecked != true) pr.MutedStaves.Add(1); else pr.MutedStaves.Remove(1);
            if (_lhChk.IsChecked != true) pr.MutedStaves.Add(2); else pr.MutedStaves.Remove(2);
        }
    }

    private void ApplyMutesToCanvas(VerticalPianoRollCanvas cv)
    {
        if (_rhChk.IsChecked != true) cv.MutedStaves.Add(1); else cv.MutedStaves.Remove(1);
        if (_lhChk.IsChecked != true) cv.MutedStaves.Add(2); else cv.MutedStaves.Remove(2);
        cv.RefreshBars();
    }

    private void ApplyMutesToStaff(StaffNotationCanvas sc)
    {
        if (_rhChk.IsChecked != true) sc.MutedStaves.Add(1); else sc.MutedStaves.Remove(1);
        if (_lhChk.IsChecked != true) sc.MutedStaves.Add(2); else sc.MutedStaves.Remove(2);
        sc.RefreshNotes();
    }

    private void UpdateDevicePickerVisibility()
    {
        bool show = _isWindows && _fluidChk.IsChecked != true;
        _midiDeviceCombo.IsVisible = show;
    }

    private int SelectedWinmmDeviceId()
    {
        int idx = _midiDeviceCombo.SelectedIndex;
        return (idx >= 0 && idx < _winmmDevices.Count) ? _winmmDevices[idx].Id : -1;
    }

    private int DivsToMeasure(long divs)
    {
        int lo = 0, hi = _measureDivMap.Count - 1, result = 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_measureDivMap[mid].Divs <= divs) { result = _measureDivMap[mid].Number; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result;
    }

    private void SetPaused()
    {
        var p = _player;
        _player = null;
        _canvas?.FreezeAtCurrentPosition();
        var sc = _contentGrid.Children.OfType<StaffNotationCanvas>().FirstOrDefault();
        sc?.FreezeAtCurrentPosition();
        int mno = (int)(_measureSlider?.Value ?? 1);
        _statusBlock.Text = $"Paused  |  M {mno}/{_totalMeasures}  |  BPM: {_bpmSlider.Value:F0}";
        _playPauseBtn.Content = "▶  Play";
        if (_isWindows) AllowSleep();
        if (p != null)
            Task.Run(async () =>
            {
                await _midiLock.WaitAsync().ConfigureAwait(false);
                try   { p.Stop(); p.Dispose(); } catch { }
                finally { _midiLock.Release(); }
            });
    }

    private void StartPlayer(int measure, double bpm)
    {
        if (_currentScore == null || _canvas == null) return;
        var score = _currentScore;
        var canvas = _canvas;
        var sc = _contentGrid.Children.OfType<StaffNotationCanvas>().FirstOrDefault();

        // Capture and clear the old player immediately so PositionChanged/PlaybackEnded
        // callbacks from it are ignored while we are switching.
        var old = _player;
        _player = null;

        bool useFluid  = _fluidChk.IsChecked == true || !_isWindows;
        string? sf     = useFluid ? VerticalPianoRollWindowFactory.FindSoundfont() : null;

        var player = new MxlMidiPlayer(score)
        {
            Bpm           = bpm,
            StartMeasure  = measure,
            Backend       = (useFluid && sf != null) ? MidiBackendKind.FluidSynth : MidiBackendKind.Winmm,
            SoundfontPath = sf ?? string.Empty,
            LogNotes      = _logNotesChk?.IsChecked == true,
            WinmmDeviceId = SelectedWinmmDeviceId(),
        };
        _player = player;
        ApplyMutes();
        if (_isWindows) PreventSleep();

        long startDivs = measure <= 1
            ? 0
            : (_measureDivMap.FirstOrDefault(x => x.Number >= measure).Divs);
        canvas.PlayBpm = bpm;
        canvas.StartSmoothPlay(startDivs);
        if (sc != null) { sc.PlayBpm = bpm; sc.StartSmoothPlay(startDivs); }

        player.PositionChanged += (_, divs) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                canvas.SyncAnchor(divs);
                sc?.SyncAnchor(divs);
            }, DispatcherPriority.Render);
            Dispatcher.UIThread.Post(() =>
            {
                int mno = DivsToMeasure(divs);
                if (_measureSlider != null)
                {
                    _suppressMeasureSync = true;
                    if ((int)_measureSlider.Value != mno) _measureSlider.Value = mno;
                    _suppressMeasureSync = false;
                }
                _statusBlock.Text = $"M {mno}/{_totalMeasures}  |  BPM: {bpm:F0}  |  {score.Title}";
                MeasureChanged?.Invoke(this, mno);
            }, DispatcherPriority.Normal);
        };

        player.PlaybackEnded += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                _player = null;
                canvas.CurrentGlobalDivisions = -1;
                if (sc != null) sc.CurrentGlobalDivisions = -1;
                if (_measureSlider != null) _measureSlider.Value = 1;
                _statusBlock.Text = $"Stopped  |  {score.Title}";
                _playPauseBtn.Content = "▶  Play";
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
                if (_opts.AutoCloseOnEnd) _hostWindow?.Close();
            }, DispatcherPriority.Normal);

        _statusBlock.Text = $"Playing  |  BPM: {bpm:F0}  |  {score.Title}";
        _playPauseBtn.Content = "⏸  Pause";

        // Use a semaphore so that every stop+open sequence is fully serialised.
        // Multiple rapid calls (debounce, playlist advance, pause+play) each
        // increment the generation; any Task that is no longer the latest simply
        // disposes the player it built and exits without calling midiOutOpen.
        var myGen = Interlocked.Increment(ref _startGen);
        Task.Run(async () =>
        {
            await _midiLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Stop old device — blocks until midiOutClose completes.
                if (old != null) { try { old.Stop(); old.Dispose(); } catch { } }

                // If a newer StartPlayer was called while we were waiting or stopping,
                // discard this player — it will be started by the newer Task.
                if (myGen != _startGen)
                {
                    try { player.Dispose(); } catch { }
                    return;
                }

                // Open + start new player.
                try
                {
                    player.Start();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"PianoRollPlayerControl.StartPlayer: {ex}");
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ReferenceEquals(_player, player)) _player = null;
                        _playPauseBtn.Content = "▶  Play";
                        _statusBlock.Text = $"MIDI error: {ex.Message}";
                    }, DispatcherPriority.Normal);
                }
            }
            finally
            {
                _midiLock.Release();
            }
        });
    }

    private void OnPlayPauseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_player != null) { SetPaused(); return; }
        StartPlayer((int)(_measureSlider?.Value ?? 1), _bpmSlider.Value);
    }

    private void OnMeasureSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Slider.ValueProperty || _measureSlider == null) return;
        int mno = (int)_measureSlider.Value;
        if (_measureLabel != null) _measureLabel.Text = $"M {mno}/{_totalMeasures}";

        if (_player == null && _canvas != null)
        {
            long previewDivs = mno <= 1 ? 0 : (_measureDivMap.FirstOrDefault(x => x.Number >= mno).Divs);
            _canvas.CurrentGlobalDivisions = previewDivs;
            var sc = _contentGrid.Children.OfType<StaffNotationCanvas>().FirstOrDefault();
            if (sc != null) sc.CurrentGlobalDivisions = previewDivs;
        }

        if (_suppressMeasureSync || _player == null) return;
        _measureDebCts?.Cancel();
        _measureDebCts = new CancellationTokenSource();
        var ct = _measureDebCts.Token;
        Task.Delay(200, ct).ContinueWith(_ =>
        {
            if (!ct.IsCancellationRequested)
                Dispatcher.UIThread.Post(() => { if (_player != null) StartPlayer((int)_measureSlider.Value, _bpmSlider.Value); });
        }, TaskScheduler.Default);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  VerticalPianoRollWindowFactory  — static factory
/// <summary>
/// Static factory for building the vertical piano roll <see cref="Window"/>.
/// Call <see cref="BuildWindow"/> to create a fully wired-up window that auto-starts
/// playback when opened.
/// </summary>
public static class VerticalPianoRollWindowFactory
{
    /// <summary>
    /// Finds the first .sf2 soundfont in the app's Soundfonts\ directory,
    /// preferring <c>GeneralUser-GS.sf2</c>.
    /// Returns null if no soundfont is found.
    /// </summary>
    public static string? FindSoundfont()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Soundfonts");
        if (!Directory.Exists(dir)) return null;
        const string preferred = "GeneralUser-GS.sf2";
        string prefer = Path.Combine(dir, preferred);
        if (File.Exists(prefer)) return prefer;
        return Directory.GetFiles(dir, "*.sf2").FirstOrDefault();
    }

    /// <summary>
    /// Parses the MusicXML at <paramref name="mxlPath"/> and builds the vertical piano roll window.
    /// Autoplay starts when the window is opened.
    /// </summary>
    /// <param name="mxlPath">Path to an uncompressed .xml (MusicXML) file.</param>
    /// <param name="startMeasure">Measure to seek to before starting playback (1-based).</param>
    /// <param name="autoCloseOnEnd">If true, closes the window when playback finishes naturally.</param>
    /// <param name="logNotesDefault">Initial value for the Log Notes checkbox.</param>
    public static Window BuildWindow(string mxlPath,
        int startMeasure = 1, bool autoCloseOnEnd = false, bool logNotesDefault = false,
        bool syncDiagnostics = false)
    {
        var xml   = File.ReadAllText(mxlPath);
        var score = MxlScore.Parse(xml);
        return BuildWindow(mxlPath, score, startMeasure, autoCloseOnEnd, logNotesDefault, syncDiagnostics);
    }

    /// <summary>
    /// Builds the vertical piano roll window from a pre-parsed <see cref="MxlScore"/>.
    /// Autoplay starts when the window is opened.
    /// </summary>
    /// <param name="mxlPath">Path (used only for the window title).</param>
    /// <param name="score">Pre-parsed score.</param>
    /// <param name="startMeasure">Measure to seek to before starting playback (1-based).</param>
    /// <param name="autoCloseOnEnd">If true, closes the window when playback finishes naturally.</param>
    /// <param name="logNotesDefault">Initial value for the Log Notes checkbox.</param>
    public static Window BuildWindow(string mxlPath, MxlScore score,
        int startMeasure = 1, bool autoCloseOnEnd = false, bool logNotesDefault = false,
        bool syncDiagnostics = false)
    {
        VerticalPianoRollCanvas.SyncDiagnostics = syncDiagnostics;
        MxlMidiPlayer.TimingDiagnostics         = syncDiagnostics;

        var player = new PianoRollPlayerControl(new PianoRollOptions
        {
            StartMeasure      = startMeasure,
            AutoCloseOnEnd    = autoCloseOnEnd,
            ShowMeasureSlider = true,
            ShowLogNotes      = true,
            ShowSkipButton    = false,
            LogNotesDefault   = logNotesDefault,
        });

        var window = new Window
        {
            Title                 = $"Vertical Piano Roll — {Path.GetFileName(mxlPath)}",
            Width                 = 1400,
            Height                = 720,
            WindowState           = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar         = false,
            Content               = player,
        };
        window.Closed += (_, _) => player.StopAll();

        bool autoPlayFired = false;
        window.Opened += (_, _) =>
        {
            if (autoPlayFired) return;
            autoPlayFired = true;
            player.LoadScore(score, startMeasure, autoPlay: true);
        };
        return window;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Rhythms tutorial window
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a vertical piano-roll window with a dropdown for selecting any rhythm
    /// or theory pattern from <see cref="MusicGenerator.RhythmPatterns"/>.
    /// </summary>
    public static Window BuildRhythmsWindow()
        => BuildPatternSelectorWindow("🥁 Rhythm Patterns", MusicGenerator.RhythmPatterns);

    // ─────────────────────────────────────────────────────────────────────────
    //  Styles tutorial window
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a vertical piano-roll window with a dropdown for selecting any style
    /// / genre pattern from <see cref="MusicGenerator.StylePatterns"/>.
    /// </summary>
    public static Window BuildStylesWindow()
        => BuildPatternSelectorWindow("🎹 Style Patterns", MusicGenerator.StylePatterns);

    // ─────────────────────────────────────────────────────────────────────────
    //  Shared pattern-selector scaffold
    // ─────────────────────────────────────────────────────────────────────────

    private static Window BuildPatternSelectorWindow(
        string windowTitle,
        IReadOnlyList<MusicPatternInfo> patterns)
    {
        var patternCombo = new ComboBox
        {
            Width                 = 320,
            VerticalAlignment     = Avalonia.Layout.VerticalAlignment.Center,
            [ToolTip.TipProperty] = "Select a pattern",
        };
        patternCombo.ItemsSource   = patterns;
        patternCombo.SelectedIndex = 0;
        patternCombo.ItemTemplate  = new Avalonia.Controls.Templates.FuncDataTemplate<MusicPatternInfo>(
            (info, _) => new TextBlock { Text = info.Display });

        var player = new PianoRollPlayerControl(new PianoRollOptions
        {
            ShowMeasureSlider = false,
            ShowLogNotes      = false,
            ShowSkipButton    = false,
            LeadingControl    = patternCombo,
        });

        // Load new pattern whenever the combo selection changes.
        patternCombo.SelectionChanged += (_, _) =>
        {
            if (patternCombo.SelectedItem is MusicPatternInfo info)
                player.LoadScore(info.Build(), autoPlay: true);
        };

        var window = new Window
        {
            Title                 = windowTitle,
            Width                 = 1400,
            Height                = 720,
            WindowState           = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar         = false,
            Content               = player,
        };
        window.Closed += (_, _) => player.StopAll();

        bool autoPlayFired = false;
        window.Opened += (_, _) =>
        {
            if (autoPlayFired) return;
            autoPlayFired = true;
            player.LoadScore(patterns[0].Build(), autoPlay: true);
        };

        return window;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Player-piano playlist window
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the score XML from a .mxl ZIP to a temp file.
    /// Returns the path unchanged if the file is already a plain .xml / .musicxml.
    /// </summary>
    public static string ResolveMxlToXml(string mxlPath)
    {
        if (!mxlPath.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase))
            return mxlPath;

        using var zip = System.IO.Compression.ZipFile.OpenRead(mxlPath);
        var scoreEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase));
        if (scoreEntry == null)
            throw new InvalidOperationException($"No score XML entry found inside {mxlPath}");

        string tmpPath = Path.Combine(
            Path.GetTempPath(),
            Path.GetFileNameWithoutExtension(mxlPath) + "_score.xml");
        using var stream = scoreEntry.Open();
        using var fs     = File.Create(tmpPath);
        stream.CopyTo(fs);
        return tmpPath;
    }

    /// <summary>
    /// Builds a "player piano" window that plays each song in <paramref name="songs"/>
    /// sequentially.  When a song finishes, the next one loads and starts automatically.
    /// A header bar shows the current song title and the next song up.
    /// </summary>
    /// <param name="songs">
    /// Ordered list of <c>(mxlPath, title)</c> pairs.
    /// <c>mxlPath</c> may be a .mxl ZIP or a plain .xml / .musicxml file.
    /// </param>
    public static Window BuildPlaylistWindow(IReadOnlyList<(string mxlPath, string title)> songs)
    {
        if (songs.Count == 0)
            throw new ArgumentException("Song list must not be empty.", nameof(songs));

        int currentIndex = 0;

        var player = new PianoRollPlayerControl(new PianoRollOptions
        {
            ShowMeasureSlider = true,
            ShowLogNotes      = false,
            ShowSkipButton    = true,
            AutoCloseOnEnd    = false,
        });

        void UpdateHeader()
        {
            string now  = songs[currentIndex].title;
            string next = currentIndex + 1 < songs.Count
                ? $"Next: {songs[currentIndex + 1].title}"
                : "Last song";
            player.SetHeaderText(now, next);
        }

        void LoadSong(int index)
        {
            if (index < 0 || index >= songs.Count) return;
            currentIndex = index;

            try
            {
                string xmlPath = ResolveMxlToXml(songs[index].mxlPath);
                var score      = MxlScore.Parse(File.ReadAllText(xmlPath));
                player.LoadScore(score, autoPlay: true);
                UpdateHeader();   // must be after LoadScore — it creates a fresh StaffNotationCanvas
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"BuildPlaylistWindow: failed to load song {index}: {ex.Message}");
                LoadSong(index + 1);
            }
        }

        // Skip button advances to the next song (or closes when exhausted).
        Window?[] windowRef = [null];
        player.SetSkipAction(() =>
        {
            int next = currentIndex + 1;
            if (next < songs.Count)
                LoadSong(next);
            else
                windowRef[0]?.Close();
        });

        // Auto-advance when a song ends (2-second pause between songs).
        player.PlaybackEnded += (_, _) =>
        {
            int next = currentIndex + 1;
            if (next < songs.Count)
                Task.Run(async () =>
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    Dispatcher.UIThread.Post(() => LoadSong(next), DispatcherPriority.Normal);
                });
            else
                Dispatcher.UIThread.Post(() => windowRef[0]?.Close(), DispatcherPriority.Normal);
        };

        var window = new Window
        {
            Title                 = $"🎹 PianoRoll Playlist — {songs.Count} songs",
            Width                 = 1400,
            Height                = 720,
            WindowState           = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar         = false,
            Content               = player,
        };
        windowRef[0] = window;
        window.Closed += (_, _) => player.StopAll();

        bool autoPlayFired = false;
        window.Opened += (_, _) =>
        {
            if (autoPlayFired) return;
            autoPlayFired = true;
            LoadSong(0);
        };

        return window;
    }
}
