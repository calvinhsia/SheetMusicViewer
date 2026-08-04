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
                    divisions = newDivs;

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
                    Divisions            = divisions,
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
                        Duration       = dur,
                        OnsetDivisions = onset,
                        NoteType       = child.Element(ns + "type")?.Value ?? string.Empty,
                        Dots           = child.Elements(ns + "dot").Count(),
                        Staff          = int.TryParse(child.Element(ns + "staff")?.Value, out var st) ? st : 1,
                        Voice          = int.TryParse(child.Element(ns + "voice")?.Value, out var v)  ? v  : 1,
                        Velocity       = noteVelocity,
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
                globalOnset += rawMeasureDur;

                double msPerDiv     = 60_000.0 / (120.0 * divisions);
                double quarterNotes = currentTSBeats * (4.0 / currentTSBeatType);
                double expectedDivs = quarterNotes * divisions;
                double actualMs     = expectedDivs * msPerDiv;
                double noteBasedMs  = rawMeasureDur * msPerDiv;
                globalOnsetMs += Math.Min(actualMs, noteBasedMs > 0 ? noteBasedMs : actualMs);
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

    private IntPtr _h = IntPtr.Zero;

    public void Open()
    {
        // Retry with back-off: the OS can take a short time to release the device
        // after a previous Close(), especially when patterns switch rapidly.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (midiOutOpen(out _h, -1, IntPtr.Zero, IntPtr.Zero, 0) == 0) return;
            Thread.Sleep(30 * (attempt + 1));  // 30 ms, 60 ms, 90 ms …
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
            ? new WinmmMidiBackend()
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
        try { _playTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
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
                int    maxOnsetDivs   = measure.Notes
                    .Where(n => !n.IsChord)
                    .Select(n => n.OnsetDivisions + n.Duration)
                    .DefaultIfEmpty(divs * measure.TimeSigBeats)
                    .Max();

                foreach (var note in measure.Notes)
                {
                    if (note.IsRest || note.MidiPitch < 21 || note.MidiPitch > 108) continue;
                    int midi = note.MidiPitch;
                    int clampedOnset = Math.Min(note.OnsetDivisions, maxOnsetDivs - 1);
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
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
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
            if (note.IsRest || note.MidiPitch < MinMidi || note.MidiPitch > MaxMidi) continue;
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
            double bx = DivsToX(bd);
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
//  VerticalPianoRollWindow  — static factory
// ─────────────────────────────────────────────────────────────────────────────

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
        var canvas      = new VerticalPianoRollCanvas(score);
        var staffCanvas = new StaffNotationCanvas(score);
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

        var playStopBtn = new Button { Content = "▶  Play", Margin = new Thickness(4), Padding = new Thickness(8, 2) };
        // NOTE: live-playback BPM debounce and measure-seek handlers are wired below, after StartPlayer is defined.

        int totalMeasures = score.Parts.Count > 0 ? score.Parts.Max(p => p.Measures.Count) : 1;
        var measureSlider = new Slider
        {
            Minimum = 1, Maximum = Math.Max(1, totalMeasures),
            Value   = Math.Max(1, startMeasure),
            Width   = 260,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [ToolTip.TipProperty] = "Jump to measure (drag to seek)"
        };
        var measureLabel = new TextBlock
        {
            Text = $"M 1/{totalMeasures}",
            Width = 72,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 12
        };
        measureSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                measureLabel.Text = $"M {(int)measureSlider.Value}/{totalMeasures}";
        };
        // NOTE: live seek handler wired below, after StartPlayer is defined.

        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var fluidSynthChk = new CheckBox
        {
            Content = "FluidSynth",
            IsChecked = true,
            IsVisible = isWindows,   // Non-Windows: always FluidSynth, checkbox not needed
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            [ToolTip.TipProperty] = "FluidSynth (better sound). Unchecked = WinMM (Windows MIDI, no extra dependencies)."
        };

        var logNotesChk = new CheckBox
        {
            Content = "Log notes",
            IsChecked = logNotesDefault,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            [ToolTip.TipProperty] = "Write every NoteOn to Trace while playing"
        };

        // Build a sorted lookup: global-division onset -> measure number (for live slider sync)
        var measureDivMap = score.Parts.Count > 0
            ? score.Parts[0].Measures
                .Select(m => (Divs: m.GlobalOnsetDivisions, m.Number))
                .OrderBy(x => x.Divs)
                .ToList()
            : new List<(long Divs, int Number)>();

        int DivsToMeasure(long divs)
        {
            int lo = 0, hi = measureDivMap.Count - 1, result = 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (measureDivMap[mid].Divs <= divs) { result = measureDivMap[mid].Number; lo = mid + 1; }
                else hi = mid - 1;
            }
            return result;
        }

        MxlMidiPlayer? player = null;
        Window?[] windowHolder = [null];

        void SetStopped()
        {
            var playerToStop = player;
            player = null;
            canvas.CurrentGlobalDivisions      = -1;
            staffCanvas.CurrentGlobalDivisions = -1;
            measureSlider.Value = score.Parts.Count > 0 ? 1 : 0;
            statusBlock.Text = $"Stopped  |  {score.Title}";
            playStopBtn.Content = "▶  Play";
            if (playerToStop != null)
                Task.Run(() => { try { playerToStop.Stop(); playerToStop.Dispose(); } catch { } });
        }

        // True while PositionChanged is updating the slider to suppress the seek-on-change handler.
        bool suppressMeasureSliderSync = false;

        void StartPlayer(int measure, double bpm)
        {
            // Stop the currently running player asynchronously so the new one can start immediately.
            var playerToStop = player;
            player = null;
            if (playerToStop != null)
                Task.Run(() => { try { playerToStop.Stop(); playerToStop.Dispose(); } catch { } });

            bool useFluid = fluidSynthChk.IsChecked == true || !isWindows;
            string? sf = useFluid ? FindSoundfont() : null;
            player = new MxlMidiPlayer(score)
            {
                Bpm           = bpm,
                StartMeasure  = measure,
                Backend       = (useFluid && sf != null) ? MidiBackendKind.FluidSynth : MidiBackendKind.Winmm,
                SoundfontPath = sf ?? string.Empty,
                LogNotes      = logNotesChk.IsChecked == true,
            };
            if (useFluid && sf == null)
                Trace.WriteLine("VerticalPianoRoll: no soundfont found -- falling back to WinMM");

            canvas.PlayBpm      = bpm;
            staffCanvas.PlayBpm = bpm;

            long startDivisions = measure <= 1
                ? 0
                : (measureDivMap.FirstOrDefault(x => x.Number >= measure).Divs);
            canvas.StartSmoothPlay(startDivisions);
            staffCanvas.StartSmoothPlay(startDivisions);

            player.PositionChanged += (_, divs) =>
            {
                // High-priority post for the one-time clock sync; normal post for UI updates.
                Dispatcher.UIThread.Post(() =>
                {
                    canvas.SyncAnchor(divs);
                    staffCanvas.SyncAnchor(divs);
                }, DispatcherPriority.Render);
                Dispatcher.UIThread.Post(() =>
                {
                    int mno = DivsToMeasure(divs);
                    suppressMeasureSliderSync = true;
                    if ((int)measureSlider.Value != mno)
                        measureSlider.Value = mno;
                    suppressMeasureSliderSync = false;
                    statusBlock.Text = $"M {mno}/{totalMeasures}  |  BPM: {bpm:F0}  |  {score.Title}";
                }, DispatcherPriority.Normal);
            };

            player.PlaybackEnded += (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    SetStopped();
                    if (autoCloseOnEnd) windowHolder[0]?.Close();
                }, DispatcherPriority.Normal);

            statusBlock.Text = $"Playing  |  BPM: {bpm:F0}  |  {score.Title}";
            playStopBtn.Content = "■  Stop";
            player.Start();
            foreach (var s in canvas.MutedStaves) player.MutedStaves.Add(s);
        }

        playStopBtn.Click += (_, _) =>
        {
            // If playing -> stop
            if (player != null) { SetStopped(); return; }

            // Start playback
            StartPlayer((int)measureSlider.Value, bpmSlider.Value);
        };

        // ── Live-playback slider wiring ────────────────────────────────────────────────
        // Both handlers live here so StartPlayer, player, suppressMeasureSliderSync etc.
        // are all already declared above.
        CancellationTokenSource? bpmDebounceCts     = null;
        CancellationTokenSource? measureDebounceCts = null;

        bpmSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            bpmLabel.Text = $"{bpmSlider.Value:F0} BPM";
            if (player == null) return;
            // Debounce: restart audio only after the slider has been idle for 300 ms.
            bpmDebounceCts?.Cancel();
            bpmDebounceCts = new CancellationTokenSource();
            var ct = bpmDebounceCts.Token;
            Task.Delay(300, ct).ContinueWith(_ =>
            {
                if (!ct.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() => { if (player != null) StartPlayer((int)measureSlider.Value, bpmSlider.Value); });
            }, TaskScheduler.Default);
        };

        measureSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            measureLabel.Text = $"M {(int)measureSlider.Value}/{totalMeasures}";
            if (suppressMeasureSliderSync || player == null) return;
            // Debounce: seek only after the slider has been idle for 200 ms.
            measureDebounceCts?.Cancel();
            measureDebounceCts = new CancellationTokenSource();
            var ct = measureDebounceCts.Token;
            Task.Delay(200, ct).ContinueWith(_ =>
            {
                if (!ct.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() => { if (player != null) StartPlayer((int)measureSlider.Value, bpmSlider.Value); });
            }, TaskScheduler.Default);
        };

        // ── Part mute checkboxes
        // Staff 1 = RH (green), staff 2 = LH (blue).  Wired to both canvas and player.
        var rhChk = new CheckBox
        {
            Content           = "RH",
            IsChecked         = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin            = new Thickness(6, 0, 2, 0),
            Foreground        = new SolidColorBrush(Color.FromRgb(64, 200, 90)),
            [ToolTip.TipProperty] = "Enable right-hand / staff 1 (green)"
        };
        var lhChk = new CheckBox
        {
            Content           = "LH",
            IsChecked         = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin            = new Thickness(2, 0, 6, 0),
            Foreground        = new SolidColorBrush(Color.FromRgb(80, 130, 230)),
            [ToolTip.TipProperty] = "Enable left-hand / staff 2 (blue)"
        };

        void ApplyMutes()
        {
            bool rhOn = rhChk.IsChecked == true;
            bool lhOn = lhChk.IsChecked == true;
            if (!rhOn) canvas.MutedStaves.Add(1); else canvas.MutedStaves.Remove(1);
            if (!lhOn) canvas.MutedStaves.Add(2); else canvas.MutedStaves.Remove(2);
            canvas.RefreshBars();
            if (!rhOn) staffCanvas.MutedStaves.Add(1); else staffCanvas.MutedStaves.Remove(1);
            if (!lhOn) staffCanvas.MutedStaves.Add(2); else staffCanvas.MutedStaves.Remove(2);
            staffCanvas.RefreshNotes();
            if (player != null)
            {
                if (!rhOn) player.MutedStaves.Add(1); else player.MutedStaves.Remove(1);
                if (!lhOn) player.MutedStaves.Add(2); else player.MutedStaves.Remove(2);
            }
        }
        rhChk.IsCheckedChanged += (_, _) => ApplyMutes();
        lhChk.IsCheckedChanged += (_, _) => ApplyMutes();

        var toolbar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin      = new Thickness(4),
            Children    = { playStopBtn, bpmSlider, bpmLabel, measureSlider, measureLabel, lhChk, rhChk, fluidSynthChk, logNotesChk, statusBlock }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        Grid.SetColumn(canvas,      0);
        Grid.SetColumn(staffCanvas, 1);
        contentGrid.Children.Add(canvas);
        contentGrid.Children.Add(staffCanvas);
        layout.Children.Add(contentGrid);

        var window = new Window
        {
            Title  = $"Vertical Piano Roll — {Path.GetFileName(mxlPath)}",
            Width  = 1400,
            Height = 720,
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            Content = layout
        };
        windowHolder[0] = window;
        window.Closed += (_, _) => SetStopped();

        bool autoPlayFired = false;
        window.Opened += (_, _) =>
        {
            if (autoPlayFired) return;
            autoPlayFired = true;
            playStopBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
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
        // ── initial score (first entry) ───────────────────────────────────
        var firstPattern = patterns[0];
        var score        = firstPattern.Build();

        // Canvas and player state
        var canvasHolder = new Control?[1];
        var staffHolder  = new StaffNotationCanvas?[1];
        var playerHolder = new MxlMidiPlayer?[1];
        var windowHolder = new Window?[1];

        void StopPlayer()
        {
            playerHolder[0]?.Stop();
            playerHolder[0] = null;
        }

        // ── toolbar controls ──────────────────────────────────────────────
        var patternCombo = new ComboBox
        {
            Width                = 320,
            VerticalAlignment    = Avalonia.Layout.VerticalAlignment.Center,
            [ToolTip.TipProperty] = "Select a pattern",
        };
        patternCombo.ItemsSource    = patterns;
        patternCombo.SelectedIndex  = 0;
        patternCombo.ItemTemplate   = new Avalonia.Controls.Templates.FuncDataTemplate<MusicPatternInfo>(
            (info, _) => new TextBlock { Text = info.Display });

        var statusBlock = new TextBlock
        {
            Text              = $"Stopped  |  BPM: {score.DefaultBpm:F0}  |  {score.Title}",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize          = 12,
            Margin            = new Thickness(8, 0)
        };

        var bpmSlider = new Slider
        {
            Minimum = 40, Maximum = 300, Value = score.DefaultBpm,
            Width   = 180,
            VerticalAlignment    = Avalonia.Layout.VerticalAlignment.Center,
            [ToolTip.TipProperty] = "Tempo (BPM)"
        };
        var bpmLabel = new TextBlock
        {
            Text = $"{score.DefaultBpm:F0} BPM", Width = 60,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 12
        };
        bpmSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                bpmLabel.Text = $"{bpmSlider.Value:F0} BPM";
        };

        var playStopBtn = new Button
        {
            Content = "▶  Play",
            Margin  = new Thickness(4),
            Padding = new Thickness(8, 2)
        };

        bool isWindows    = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var fluidSynthChk = new CheckBox
        {
            Content           = "FluidSynth",
            IsChecked         = !isWindows,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin            = new Thickness(4, 0),
            [ToolTip.TipProperty] = "Use FluidSynth (cross-platform); uncheck for WinMM (Windows only)"
        };

        // ── content grid: piano roll left, staff notation right ─────────
        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        // ── Part mute checkboxes (declared here so LoadPattern can call ApplyMutesP) ──
        var rhChkP = new CheckBox
        {
            Content           = "RH",
            IsChecked         = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin            = new Thickness(6, 0, 2, 0),
            Foreground        = new SolidColorBrush(Color.FromRgb(64, 200, 90)),
            [ToolTip.TipProperty] = "Enable right-hand / staff 1 (green)"
        };
        var lhChkP = new CheckBox
        {
            Content           = "LH",
            IsChecked         = true,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin            = new Thickness(2, 0, 6, 0),
            Foreground        = new SolidColorBrush(Color.FromRgb(80, 130, 230)),
            [ToolTip.TipProperty] = "Enable left-hand / staff 2 (blue)"
        };

        void ApplyMutesP()
        {
            bool rhOn = rhChkP.IsChecked == true;
            bool lhOn = lhChkP.IsChecked == true;
            if (canvasHolder[0] is VerticalPianoRollCanvas cv)
            {
                if (!rhOn) cv.MutedStaves.Add(1); else cv.MutedStaves.Remove(1);
                if (!lhOn) cv.MutedStaves.Add(2); else cv.MutedStaves.Remove(2);
                cv.RefreshBars();
            }
            if (staffHolder[0] is StaffNotationCanvas sc)
            {
                if (!rhOn) sc.MutedStaves.Add(1); else sc.MutedStaves.Remove(1);
                if (!lhOn) sc.MutedStaves.Add(2); else sc.MutedStaves.Remove(2);
                sc.RefreshNotes();
            }
            if (playerHolder[0] is MxlMidiPlayer pr)
            {
                if (!rhOn) pr.MutedStaves.Add(1); else pr.MutedStaves.Remove(1);
                if (!lhOn) pr.MutedStaves.Add(2); else pr.MutedStaves.Remove(2);
            }
        }
        rhChkP.IsCheckedChanged += (_, _) => ApplyMutesP();
        lhChkP.IsCheckedChanged += (_, _) => ApplyMutesP();

        void LoadPattern(MusicPatternInfo info)
        {
            StopPlayer();
            var s = info.Build();

            // Rebuild both canvases with the new score
            contentGrid.Children.Clear();
            var newCanvas = new VerticalPianoRollCanvas(s);
            var newStaff  = new StaffNotationCanvas(s);
            Grid.SetColumn(newCanvas, 0);
            Grid.SetColumn(newStaff,  1);
            canvasHolder[0] = newCanvas;
            staffHolder[0]  = newStaff;
            contentGrid.Children.Add(newCanvas);
            contentGrid.Children.Add(newStaff);

            bpmSlider.Value = s.DefaultBpm;
            patternCombo[ToolTip.TipProperty] = info.Tooltip;
            statusBlock.Text     = $"Stopped  |  BPM: {s.DefaultBpm:F0}  |  {s.Title}";
            playStopBtn.Content  = "▶  Play";

            ApplyMutesP();

            // Auto-play
            Dispatcher.UIThread.Post(() =>
                playStopBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)),
                DispatcherPriority.Background);
        }

        patternCombo.SelectionChanged += (_, _) =>
        {
            if (patternCombo.SelectedItem is MusicPatternInfo info)
                LoadPattern(info);
        };

        // ── play/stop wiring ──────────────────────────────────────────────
        playStopBtn.Click += (_, _) =>
        {
            if (playerHolder[0] != null) { StopPlayer(); playStopBtn.Content = "▶  Play"; return; }

            if (patternCombo.SelectedItem is not MusicPatternInfo info) return;
            var s = info.Build();

            bool useFluid = fluidSynthChk.IsChecked == true || !isWindows;
            string? sf    = useFluid ? FindSoundfont() : null;
            var p = new MxlMidiPlayer(s)
            {
                Bpm           = bpmSlider.Value,
                StartMeasure  = 1,
                Backend       = (useFluid && sf != null) ? MidiBackendKind.FluidSynth : MidiBackendKind.Winmm,
                SoundfontPath = sf ?? string.Empty,
            };
            playerHolder[0] = p;
            // Apply current mute state before playback starts
            ApplyMutesP();

            p.PositionChanged += (_, divs) =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (canvasHolder[0] is VerticalPianoRollCanvas c)
                        c.CurrentGlobalDivisions = divs;
                    if (staffHolder[0] is StaffNotationCanvas sc)
                        sc.CurrentGlobalDivisions = divs;
                    statusBlock.Text = $"Playing  |  BPM: {bpmSlider.Value:F0}  |  {s.Title}";
                }, DispatcherPriority.Render);

            p.PlaybackEnded += (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    var old = playerHolder[0];
                    playerHolder[0] = null;
                    if (canvasHolder[0] is VerticalPianoRollCanvas cv)
                        cv.CurrentGlobalDivisions = -1;
                    if (staffHolder[0] is StaffNotationCanvas sc)
                        sc.CurrentGlobalDivisions = -1;
                    playStopBtn.Content = "▶  Play";
                    statusBlock.Text    = $"Stopped  |  BPM: {bpmSlider.Value:F0}  |  {s.Title}";
                    if (old != null) Task.Run(() => { try { old.Stop(); } catch { } });
                }, DispatcherPriority.Normal);

            playStopBtn.Content = "■  Stop";
            p.Start();
        };

        // ── initial canvas + staff ─────────────────────────────────────────
        var initialCanvas = new VerticalPianoRollCanvas(score);
        var initialStaff  = new StaffNotationCanvas(score);
        Grid.SetColumn(initialCanvas, 0);
        Grid.SetColumn(initialStaff,  1);
        canvasHolder[0] = initialCanvas;
        staffHolder[0]  = initialStaff;
        contentGrid.Children.Add(initialCanvas);
        contentGrid.Children.Add(initialStaff);

        var toolbar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin      = new Thickness(4),
            Children    = { patternCombo, playStopBtn, bpmSlider, bpmLabel, lhChkP, rhChkP, fluidSynthChk, statusBlock }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(contentGrid);

        var window = new Window
        {
            Title                 = windowTitle,
            Width                 = 1400,
            Height                = 720,
            WindowState           = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar         = false,
            Content               = layout,
        };
        windowHolder[0] = window;
        window.Closed += (_, _) => StopPlayer();

        // Auto-play first pattern on open
        bool autoPlayFired2 = false;
        window.Opened += (_, _) =>
        {
            if (autoPlayFired2) return;
            autoPlayFired2 = true;
            playStopBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        };

        return window;
    }
}
