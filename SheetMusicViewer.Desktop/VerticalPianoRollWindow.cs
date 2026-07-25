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
        if (midiOutOpen(out _h, -1, IntPtr.Zero, IntPtr.Zero, 0) != 0)
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

        _consumerTask = Task.Factory.StartNew(() =>
        {
            var reader = ch.Reader;
            try
            {
                while (reader.WaitToReadAsync(cct).AsTask().GetAwaiter().GetResult())
                {
                    while (reader.TryRead(out uint msg))
                    {
                        if (LogNotes)
                        {
                            uint mt = msg & 0xF0;
                            if (mt == 0x90 && ((msg >> 16) & 0xFF) > 0)
                                Trace.WriteLine($"{Ts()} PRE-DISPATCH NoteOn  midi={(msg >> 8) & 0xFF,3}  backlog={System.Threading.Volatile.Read(ref _channelBacklog)}");
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
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
        int MeasureNo = 0, string NoteName = "", int MidiNote = 0, int Staff = 0, int Voice = 0);

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
                    events.Add(new MidiEvent(onsetMs, NoteOn(ch, midi, 72), globalDivs,
                        MeasureNo: measure.Number, Staff: note.Staff, Voice: note.Voice));
                    events.Add(new MidiEvent(offMs, NoteOff(ch, midi), globalDivs));
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

                _backend!.Send(ev.Message);
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

    private readonly int    _totalWhiteKeys;
    private readonly double _canvasW;

    private sealed record NoteBar(double X, double W, long GlobalOnset, long GlobalOff,
                                  int MidiPitch, int Staff, bool IsBlack);
    private readonly List<NoteBar> _bars = new();

    private long   _currentGlobalDivisions = -1;
    private readonly MxlScore _score;
    private readonly int _divsPerQuarter;

    public long CurrentGlobalDivisions
    {
        get => _currentGlobalDivisions;
        set { _currentGlobalDivisions = value; InvalidateVisual(); }
    }

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
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new Size(_canvasW, double.IsInfinity(availableSize.Height) ? 720 : availableSize.Height);

    private double MidiToX(int midi)
    {
        if (midi < MinMidi || midi > MaxMidi) return -1;
        var (wi, isBlack) = KeyLayout[midi - MinMidi];
        return isBlack ? wi * WhiteKeyW : wi * WhiteKeyW + WhiteKeyW / 2.0;
    }

    private double MidiToWidth(int midi) =>
        BlackPitchClass.Contains(midi % 12) ? BlackKeyW - 1 : WhiteKeyW - 1;

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
            bool black      = BlackPitchClass.Contains(note.MidiPitch % 12);
            int visualStaff = _score.VisualStaff(part, note);
            _bars.Add(new NoteBar(x, bw, onset, off, note.MidiPitch, visualStaff, black));
        }
    }

    public override void Render(DrawingContext ctx)
    {
        double totalH  = Bounds.Height;
        double scrollH = Math.Max(50, totalH - KeyboardH);
        double bpm     = _score.DefaultBpm > 0 ? _score.DefaultBpm : 120;
        long   displayDivs = _currentGlobalDivisions >= 0 ? _currentGlobalDivisions : 0;

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

        var staff1Brush = new SolidColorBrush(Color.FromArgb(220, 64, 200, 90));
        var staff2Brush = new SolidColorBrush(Color.FromArgb(220, 80, 130, 230));
        var otherBrush  = new SolidColorBrush(Color.FromArgb(200, 200, 180, 80));

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
        int startMeasure = 1, bool autoCloseOnEnd = false, bool logNotesDefault = false)
    {
        var xml   = File.ReadAllText(mxlPath);
        var score = MxlScore.Parse(xml);
        return BuildWindow(mxlPath, score, startMeasure, autoCloseOnEnd, logNotesDefault);
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
        int startMeasure = 1, bool autoCloseOnEnd = false, bool logNotesDefault = false)
    {
        var canvas      = new VerticalPianoRollCanvas(score);
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

        int totalMeasures = score.Parts.Count > 0 ? score.Parts.Max(p => p.Measures.Count) : 1;
        var measureSlider = new Slider
        {
            Minimum = 1, Maximum = Math.Max(1, totalMeasures),
            Value   = Math.Max(1, startMeasure),
            Width   = 260,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            [ToolTip.TipProperty] = "Jump to measure (stop first)"
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
            canvas.CurrentGlobalDivisions = -1;
            statusBlock.Text = "Stopped";
            playStopBtn.Content = "▶  Play";
            if (playerToStop != null)
                Task.Run(() => { try { playerToStop.Dispose(); } catch { } });
        }

        playStopBtn.Click += (_, _) =>
        {
            // If playing -> stop
            if (player != null) { SetStopped(); return; }

            // Start playback
            bool useFluid = fluidSynthChk.IsChecked == true
                || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            string? sf = FindSoundfont();
            player = new MxlMidiPlayer(score)
            {
                Bpm           = bpmSlider.Value,
                StartMeasure  = (int)measureSlider.Value,
                Backend       = (useFluid && sf != null) ? MidiBackendKind.FluidSynth : MidiBackendKind.Winmm,
                SoundfontPath = sf ?? string.Empty,
                LogNotes      = logNotesChk.IsChecked == true,
            };
            if (useFluid && sf == null)
                Trace.WriteLine("VerticalPianoRoll: no soundfont found -- falling back to WinMM");

            player.PositionChanged += (_, divs) =>
                Dispatcher.UIThread.Post(() =>
                {
                    canvas.CurrentGlobalDivisions = divs;
                    int mno = DivsToMeasure(divs);
                    if ((int)measureSlider.Value != mno)
                        measureSlider.Value = mno;
                    statusBlock.Text = $"M {mno}/{totalMeasures}  |  BPM: {bpmSlider.Value:F0}  |  {score.Title}";
                }, DispatcherPriority.Render);

            player.PlaybackEnded += (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    SetStopped();
                    if (autoCloseOnEnd) windowHolder[0]?.Close();
                }, DispatcherPriority.Normal);

            statusBlock.Text = $"Playing  |  BPM: {bpmSlider.Value:F0}  |  {score.Title}";
            playStopBtn.Content = "■  Stop";
            player.Start();
        };

        var toolbar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Margin      = new Thickness(4),
            Children    = { playStopBtn, bpmSlider, bpmLabel, measureSlider, measureLabel, fluidSynthChk, logNotesChk, statusBlock }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(canvas);

        var window = new Window
        {
            Title  = $"Vertical Piano Roll — {Path.GetFileName(mxlPath)}",
            Width  = 1400,
            Height = 720,
            WindowState = WindowState.Maximized,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
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
}
