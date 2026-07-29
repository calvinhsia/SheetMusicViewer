// MusicGenerator.cs
// Builds MxlScore objects from scratch, without any MusicXML file.
// Used by the Rhythms and Styles tutorial windows and by manual tests.

using System;
using System.Collections.Generic;
using System.Linq;

namespace SheetMusicViewer.Desktop;

// ─────────────────────────────────────────────────────────────────────────────
//  Pattern descriptors
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Category of a generated music pattern.</summary>
public enum MusicPatternCategory { Rhythm, Style }

/// <summary>Describes a single selectable pattern for the tutorial windows.</summary>
public sealed class MusicPatternInfo
{
    public string                Key      { get; init; } = string.Empty;
    public string                Display  { get; init; } = string.Empty;
    public string                Tooltip  { get; init; } = string.Empty;
    public MusicPatternCategory  Category { get; init; }
    public Func<MxlScore>        Build    { get; init; } = () => new MxlScore();
}

// ─────────────────────────────────────────────────────────────────────────────
//  MusicGenerator
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Static factory that builds <see cref="MxlScore"/> objects entirely in memory,
/// without any MusicXML file.  Use <see cref="RhythmPatterns"/> or
/// <see cref="StylePatterns"/> to enumerate the available patterns.
/// </summary>
public static class MusicGenerator
{
    // ── constants ────────────────────────────────────────────────────────────
    private const int Divisions = 4;
    private const int Eighth    = 2;
    private const int Quarter   = 4;
    private const int Half      = 8;
    private const int Sixteenth = 1;

    private const int MidiA0 = 21;
    private const int MidiC8 = 108;
    private const int MidiC4 = 60;

    // ── pattern catalogues ───────────────────────────────────────────────────

    /// <summary>Rhythm / theory patterns (single-pitch or interval exercises).</summary>
    public static readonly IReadOnlyList<MusicPatternInfo> RhythmPatterns = new[]
    {
        P("ChromaticFullRange",       "Chromatic Full Range",
          "Every semitone A0 → C8, then back down (176 eighth notes).",
          MusicPatternCategory.Rhythm, ChromaticFullRange),

        P("ChromaticWithOctave",      "Chromatic + Octave",
          "Chromatic sweep; every note doubled one octave above simultaneously.",
          MusicPatternCategory.Rhythm, ChromaticWithOctave),

        P("MajorScales",              "Major Scales (circle of 5ths)",
          "All 12 major scales ascending + descending through the circle of 5ths.",
          MusicPatternCategory.Rhythm, MajorScales),

        P("WholeToneScales",          "Whole-Tone Scales",
          "Both whole-tone hexatonic sets (C and C#), each up then down — Debussy.",
          MusicPatternCategory.Rhythm, WholeToneScales),

        P("MinorArpeggios",           "Minor Arpeggios",
          "Broken minor triads (root, ♭3, 5) across all 12 chromatic roots.",
          MusicPatternCategory.Rhythm, MinorArpeggios),

        P("AlbertiBass",              "Alberti Bass + Melody",
          "Classic Alberti-bass LH pattern with C-major scale melody in RH.",
          MusicPatternCategory.Rhythm, AlbertiBassAndMelody),

        P("Polyrhythm",               "Polyrhythm 3-against-4",
          "LH triplets (3 per bar) against RH quarter notes (4 per bar).",
          MusicPatternCategory.Rhythm, Polyrhythm),

        P("PolyrhythmTonicDominant",  "Polyrhythm Tonic/Dominant",
          "Single-pitch 3:4 polyrhythm — RH = C4 tonic, LH = G3 dominant, beat-1 accented.",
          MusicPatternCategory.Rhythm, PolyrhythmTonicDominant),

        P("BrownianWalk",             "Brownian Walk",
          "Pitch drifts ±1 semitone per eighth note (random walk, seed 42).",
          MusicPatternCategory.Rhythm, () => BrownianWalk()),
    };

    /// <summary>Style / genre tutorial patterns.</summary>
    public static readonly IReadOnlyList<MusicPatternInfo> StylePatterns = new[]
    {
        P("Pop",        "Pop — I–V–vi–IV",
          "Block chords (C G Am F) in RH + walking bass in LH, 100 BPM.",
          MusicPatternCategory.Style, StylePop),

        P("Jazz",       "Jazz — ii–V–I",
          "Shell voicings (Dm7 G7 Cmaj7) in RH + walking quarter-note bass, 120 BPM.",
          MusicPatternCategory.Style, StyleJazz),

        P("RnB",        "R&B — 16th-note groove",
          "Syncopated pentatonic RH over C7–F7 bass ostinato, 92 BPM.",
          MusicPatternCategory.Style, StyleRnB),

        P("Rock",       "Rock — 12-bar Blues in E",
          "E minor pentatonic lick RH + power-chord LH, 130 BPM.",
          MusicPatternCategory.Style, StyleRock),

        P("HipHop",     "Hip-Hop — Boom-Bap",
          "Am7/Dm7 chord stabs on off-beats + kick/snare/hi-hat pattern, 85 BPM.",
          MusicPatternCategory.Style, StyleHipHop),

        P("Latin",      "Latin — 2-3 Son Clave",
          "Montuno riff (C F G vamp) RH + 2-3 son clave LH, 110 BPM.",
          MusicPatternCategory.Style, StyleLatin),

        P("Tango",      "Tango — Habanera",
          "Habanera bass LH + descending chromatic chord stabs RH in A minor, 110 BPM.",
          MusicPatternCategory.Style, StyleTango),

        P("BossaNova",  "Bossa Nova — Girl from Ipanema feel",
          "Bossa 2-bar clave LH + samba-inflected shell voicings RH in C, 130 BPM.",
          MusicPatternCategory.Style, StyleBossaNova),

        P("Gospel",     "Gospel — I–vi–ii–V7 in F",
          "Quarter-note block chords (Fmaj7 Dm7 Gm7 C7) RH + stride LH, 88 BPM.",
          MusicPatternCategory.Style, StyleGospel),

        P("Country",    "Country — Two-Step in G",
          "Pentatonic fiddle melody RH + boom-chick bass LH (G C D G), 120 BPM.",
          MusicPatternCategory.Style, StyleCountry),

        P("Ragtime",    "Ragtime — Joplin Oom-Pah",
          "Syncopated 16th-note melody RH + oom-pah LH in C, 100 BPM.",
          MusicPatternCategory.Style, StyleRagtime),
    };

    private static MusicPatternInfo P(string key, string display, string tooltip,
        MusicPatternCategory cat, Func<MxlScore> build) =>
        new() { Key = key, Display = display, Tooltip = tooltip, Category = cat, Build = build };

    // ── helpers ───────────────────────────────────────────────────────────────

    private static (MxlScore score, MxlPart part) MakeSinglePartScore(string title, double bpm)
    {
        var score = new MxlScore(title, bpm);
        var part  = new MxlPart { PartId = "P1", InstrumentName = "Piano", PartIndex = 0 };
        score.Parts.Add(part);
        return (score, part);
    }

    private static MxlNote NoteFromMidi(int midi, int duration, int onset,
        int staff = 1, int voice = 1, int velocity = 64)
    {
        int octave  = midi / 12 - 1;
        int pc      = midi % 12;
        string[] names = { "C","C","D","D","E","F","F","G","G","A","A","B" };
        string[] alts  = { "","#","","#","","","#","","#","","#","" };
        return new MxlNote
        {
            Pitch          = names[pc],      // step letter only; alteration is in PitchAlter
            Octave         = octave.ToString(),
            PitchAlter     = alts[pc] == "#" ? 1 : 0,
            Duration       = duration,
            IsRest         = false,
            IsChord        = false,
            OnsetDivisions = onset,
            Staff          = staff,
            Voice          = voice,
            Velocity       = velocity,
            NoteType       = duration switch { 2 => "eighth", 4 => "quarter", 8 => "half", 1 => "16th", _ => "quarter" },
        };
    }

    private static void AppendSequentialNotes(MxlPart part, IEnumerable<int> midiPitches,
        int noteDuration = Quarter, double bpm = 120.0,
        int beatsPerMeasure = 4, int beatType = 4)
    {
        var queue       = new Queue<int>(midiPitches);
        int measureNo   = part.Measures.Count + 1;
        int capacityDiv = beatsPerMeasure * Divisions;
        const double RefBpm = 120.0;
        double msPerDiv = 60_000.0 / (RefBpm * Divisions);
        double globalMs = part.Measures.Count == 0
            ? 0.0
            : part.Measures.Last().GlobalOnsetMs +
              part.Measures.Last().Notes
                  .Where(n => !n.IsChord).Select(n => n.OnsetDivisions + n.Duration)
                  .DefaultIfEmpty(0).Max() * msPerDiv;
        long globalDiv = part.Measures.Count == 0
            ? 0L
            : part.Measures.Last().GlobalOnsetDivisions +
              part.Measures.Last().Notes
                  .Where(n => !n.IsChord).Select(n => n.OnsetDivisions + n.Duration)
                  .DefaultIfEmpty(0).Max();

        while (queue.Count > 0)
        {
            var measure = new MxlMeasure
            {
                Number               = measureNo++,
                TimeSig              = $"{beatsPerMeasure}/{beatType}",
                KeySig               = "C major",
                Divisions            = Divisions,
                GlobalOnsetDivisions = globalDiv,
                GlobalOnsetMs        = globalMs,
                TimeSigBeats         = beatsPerMeasure,
                TimeSigBeatType      = beatType,
            };
            int cursor = 0;
            while (queue.Count > 0 && cursor + noteDuration <= capacityDiv)
            {
                measure.Notes.Add(NoteFromMidi(queue.Dequeue(), noteDuration, cursor));
                cursor += noteDuration;
            }
            int usedDiv = measure.Notes.Count > 0
                ? measure.Notes.Last().OnsetDivisions + measure.Notes.Last().Duration
                : capacityDiv;
            globalMs  += usedDiv * msPerDiv;
            globalDiv += usedDiv;
            part.Measures.Add(measure);
        }
    }

    private static void AppendSequentialChordPairs(MxlPart part,
        IEnumerable<(int root, int upper)> pairs,
        int noteDuration = Quarter, int beatsPerMeasure = 4, int beatType = 4)
    {
        var queue       = new Queue<(int, int)>(pairs);
        int measureNo   = part.Measures.Count + 1;
        int capacityDiv = beatsPerMeasure * Divisions;
        double msPerDiv = 60_000.0 / (120.0 * Divisions);
        double globalMs = 0.0;
        long   globalDiv = 0L;

        while (queue.Count > 0)
        {
            var measure = new MxlMeasure
            {
                Number               = measureNo++,
                TimeSig              = $"{beatsPerMeasure}/{beatType}",
                KeySig               = "C major",
                Divisions            = Divisions,
                GlobalOnsetDivisions = globalDiv,
                GlobalOnsetMs        = globalMs,
                TimeSigBeats         = beatsPerMeasure,
                TimeSigBeatType      = beatType,
            };
            int cursor = 0;
            while (queue.Count > 0 && cursor + noteDuration <= capacityDiv)
            {
                var (root, upper) = queue.Dequeue();
                measure.Notes.Add(NoteFromMidi(root,  noteDuration, cursor, staff: 1, voice: 1));
                var octaveNote = NoteFromMidi(upper, noteDuration, cursor, staff: 1, voice: 1);
                octaveNote.IsChord = true;
                measure.Notes.Add(octaveNote);
                cursor += noteDuration;
            }
            int usedDiv = measure.Notes.Where(n => !n.IsChord)
                .Select(n => n.OnsetDivisions + n.Duration).DefaultIfEmpty(capacityDiv).Max();
            globalMs  += usedDiv * msPerDiv;
            globalDiv += usedDiv;
            part.Measures.Add(measure);
        }
    }

    // ── rhythm / theory patterns ──────────────────────────────────────────────

    public static MxlScore ChromaticFullRange()
    {
        var (score, part) = MakeSinglePartScore("Chromatic Full Range", bpm: 160.0);
        var ascending  = Enumerable.Range(MidiA0, MidiC8 - MidiA0 + 1);
        var descending = Enumerable.Range(MidiA0, MidiC8 - MidiA0 + 1).Reverse();
        AppendSequentialNotes(part, ascending.Concat(descending), noteDuration: Eighth, bpm: 160.0);
        return score;
    }

    public static MxlScore ChromaticWithOctave()
    {
        var (score, part) = MakeSinglePartScore("Chromatic + Octave Above", bpm: 160.0);
        const int rootMax = MidiC8 - 12;
        var ascending  = Enumerable.Range(MidiA0, rootMax - MidiA0 + 1);
        var descending = Enumerable.Range(MidiA0, rootMax - MidiA0 + 1).Reverse();
        AppendSequentialChordPairs(part,
            ascending.Concat(descending).Select(n => (n, n + 12)),
            noteDuration: Eighth);
        return score;
    }

    public static MxlScore MajorScales()
    {
        var (score, part) = MakeSinglePartScore("All Major Scales (circle of 5ths)", bpm: 140.0);
        int[] roots = { 60, 67, 62, 69, 64, 71, 66, 61, 68, 63, 70, 65 };
        int[] majorIntervals = { 0, 2, 4, 5, 7, 9, 11, 12 };
        var pitches = new List<int>();
        foreach (int root in roots)
        {
            var asc  = majorIntervals.Select(i => root + i).ToList();
            var desc = majorIntervals.Reverse().Select(i => root + i).ToList();
            pitches.AddRange(asc);
            pitches.AddRange(desc.Skip(1));
        }
        AppendSequentialNotes(part, pitches, noteDuration: Eighth, bpm: 140.0);
        return score;
    }

    public static MxlScore WholeToneScales()
    {
        var (score, part) = MakeSinglePartScore("Whole-Tone Scales", bpm: 130.0);
        int[] set1Roots = { 60, 62, 64, 66, 68, 70 };
        int[] set2Roots = { 61, 63, 65, 67, 69, 71 };
        var pitches = new List<int>();
        foreach (int[] set in new[] { set1Roots, set2Roots })
        {
            var asc  = set.Concat(set.Select(p => p + 12)).ToList();
            var desc = asc.AsEnumerable().Reverse().ToList();
            pitches.AddRange(asc);
            pitches.AddRange(desc.Skip(1));
        }
        AppendSequentialNotes(part, pitches, noteDuration: Eighth, bpm: 130.0);
        return score;
    }

    public static MxlScore MinorArpeggios()
    {
        var (score, part) = MakeSinglePartScore("Minor Arpeggios (all 12 roots)", bpm: 120.0);
        int[] minorTriad = { 0, 3, 7 };
        var pitches = new List<int>();
        for (int octave = 0; octave < 2; octave++)
        {
            int baseOct = 48 + octave * 12;
            for (int root = 0; root < 12; root++)
            {
                var asc  = minorTriad.Select(i => baseOct + root + i).ToList();
                var desc = asc.AsEnumerable().Reverse().ToList();
                pitches.AddRange(asc);
                pitches.AddRange(desc.Skip(1));
            }
        }
        AppendSequentialNotes(part, pitches, noteDuration: Eighth, bpm: 120.0);
        return score;
    }

    public static MxlScore AlbertiBassAndMelody()
    {
        const int bpm        = 120;
        const int measures   = 8;
        const double msPerDiv = 60_000.0 / (bpm * Divisions);

        var score = new MxlScore("Alberti Bass + Melody", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Piano LH", PartIndex = 1 };

        int[] melodyMidi = { 60, 62, 64, 65, 67, 69, 71, 72, 71, 69, 67, 65, 64, 62, 60, 60 };
        for (int m = 0; m < measures; m++)
        {
            var measure = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = m * (long)(4 * Divisions),
                GlobalOnsetMs        = m * 4 * Divisions * msPerDiv,
                TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int n = 0; n < 4; n++)
                measure.Notes.Add(NoteFromMidi(melodyMidi[(m * 4 + n) % melodyMidi.Length], Quarter, n * Quarter, staff: 1, voice: 1));
            rh.Measures.Add(measure);
        }
        score.Parts.Add(rh);

        int[] alberti = { 48, 55, 52, 55 };
        for (int m = 0; m < measures; m++)
        {
            var measure = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = m * (long)(4 * Divisions),
                GlobalOnsetMs        = m * 4 * Divisions * msPerDiv,
                TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int n = 0; n < 4; n++)
                measure.Notes.Add(NoteFromMidi(alberti[n], Quarter, n * Quarter, staff: 2, voice: 2));
            lh.Measures.Add(measure);
        }
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore Polyrhythm()
    {
        const int bpm           = 100;
        const int divs          = 12;
        const int measures      = 12;
        const double msPerDivRef = 60_000.0 / (120.0 * divs);

        var score = new MxlScore("Polyrhythm 3-against-4", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Piano LH", PartIndex = 1 };

        int[] rhPitches = { 64, 65, 67, 69, 71, 72 };
        int[] lhPitches = { 52, 53, 55, 57, 59, 60 };

        for (int m = 0; m < measures; m++)
        {
            long   globalDiv = m * 4L * divs;
            double globalMs  = globalDiv * msPerDivRef;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = divs,
                GlobalOnsetDivisions = globalDiv, GlobalOnsetMs = globalMs,
                TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int n = 0; n < 4; n++)
                rhM.Notes.Add(NoteFromMidi(rhPitches[(m * 4 + n) % rhPitches.Length], divs / 4, n * divs, staff: 1, voice: 1));
            rh.Measures.Add(rhM);

            int tripletDur = divs * 4 / 3;
            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = divs,
                GlobalOnsetDivisions = globalDiv, GlobalOnsetMs = globalMs,
                TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int n = 0; n < 3; n++)
                lhM.Notes.Add(NoteFromMidi(lhPitches[(m * 3 + n) % lhPitches.Length], tripletDur / 4, n * tripletDur, staff: 2, voice: 2));
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore PolyrhythmTonicDominant()
    {
        const int bpm           = 100;
        const int divs          = 12;
        const int measures      = 12;
        const double msPerDivRef = 60_000.0 / (120.0 * divs);
        const int tonicMidi    = 60;  // C4
        const int dominantMidi = 55;  // G3
        const int accentVel    = 110;
        const int softVel      = 55;
        int tripletDur = divs * 4 / 3;              // = 16 divisions per triplet slot
        int rhNoteDur  = divs / 4;                   // staccato: 1/4 of a quarter note
        int lhNoteDur  = Math.Max(1, tripletDur / 4); // staccato: 1/4 of a triplet slot

        var score = new MxlScore("Polyrhythm Tonic/Dominant (C4 vs G3)", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Piano LH", PartIndex = 1 };

        for (int m = 0; m < measures; m++)
        {
            long   globalDiv = m * 4L * divs;
            double globalMs  = globalDiv * msPerDivRef;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = divs,
                GlobalOnsetDivisions = globalDiv, GlobalOnsetMs = globalMs,
                TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int n = 0; n < 4; n++)
                rhM.Notes.Add(NoteFromMidi(tonicMidi, rhNoteDur, n * divs, staff: 1, voice: 1, velocity: n == 0 ? accentVel : softVel));
            rh.Measures.Add(rhM);

            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = divs,
                GlobalOnsetDivisions = globalDiv, GlobalOnsetMs = globalMs,
                TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int n = 0; n < 3; n++)
            {
                int totalLhNotes = m * 3 + n;
                lhM.Notes.Add(NoteFromMidi(dominantMidi, lhNoteDur, n * tripletDur, staff: 2, voice: 2,
                    velocity: totalLhNotes % 3 == 0 ? accentVel : softVel));
            }
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore BrownianWalk(int seed = 42, int measures = 64)
    {
        var (score, part) = MakeSinglePartScore("Brownian Walk", bpm: 120.0);
        var rng   = new Random(seed);
        int pitch = MidiC4;
        const int lo = 48, hi = 84;
        int totalNotes = measures * 8;
        var pitches = new List<int>(totalNotes);
        for (int i = 0; i < totalNotes; i++)
        {
            pitches.Add(pitch);
            pitch = Math.Clamp(pitch + (rng.Next(2) == 0 ? -1 : 1), lo, hi);
        }
        AppendSequentialNotes(part, pitches, noteDuration: Eighth, bpm: 120.0);
        return score;
    }

    // ── style generators ──────────────────────────────────────────────────────

    public static MxlScore StylePop()
    {
        const int bpm        = 100;
        const int measures   = 16;
        const double msPerDiv = 60_000.0 / (120.0 * Divisions);

        var score = new MxlScore("Pop: I–V–vi–IV (C G Am F)", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Piano LH", PartIndex = 1 };

        int[][] chords   = { new[]{60,64,67}, new[]{67,71,74}, new[]{69,72,76}, new[]{65,69,72} };
        int[]   lhRoots  = { 48, 55, 45, 53 };
        int[]   lhFifths = { 55, 62, 52, 60 };

        for (int m = 0; m < measures; m++)
        {
            int ci  = m % 4;
            long   gd  = m * (long)(4 * Divisions);
            double gms = gd * msPerDiv;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            foreach (int onset in new[] { 0, Half })
            {
                bool isFirst = true;
                foreach (int p in chords[ci])
                {
                    var n = NoteFromMidi(p, Half, onset, staff: 1, voice: 1, velocity: 80);
                    if (!isFirst) n.IsChord = true;
                    rhM.Notes.Add(n);
                    isFirst = false;
                }
            }
            rh.Measures.Add(rhM);

            int nextRoot = lhRoots[(ci + 1) % 4];
            int walk     = (lhRoots[ci] + nextRoot) / 2;
            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            lhM.Notes.Add(NoteFromMidi(lhRoots[ci],  Quarter, 0 * Quarter, staff: 2, voice: 2, velocity: 90));
            lhM.Notes.Add(NoteFromMidi(lhFifths[ci], Quarter, 1 * Quarter, staff: 2, voice: 2, velocity: 70));
            lhM.Notes.Add(NoteFromMidi(lhRoots[ci],  Quarter, 2 * Quarter, staff: 2, voice: 2, velocity: 80));
            lhM.Notes.Add(NoteFromMidi(walk,           Quarter, 3 * Quarter, staff: 2, voice: 2, velocity: 65));
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore StyleJazz()
    {
        const int bpm        = 120;
        const int measures   = 12;
        const double msPerDiv = 60_000.0 / (120.0 * Divisions);

        var score = new MxlScore("Jazz: ii–V–I (Dm7 G7 Cmaj7)", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Piano LH", PartIndex = 1 };

        int[][] voicings = {
            new[]{62,65,69,72}, new[]{67,71,74,77}, new[]{60,64,67,71}, new[]{60,64,67,71},
        };
        int[][] walkers = {
            new[]{38,41,45,43}, new[]{43,47,50,48}, new[]{36,40,43,47}, new[]{36,40,43,37},
        };

        for (int m = 0; m < measures; m++)
        {
            int ci  = m % 4;
            long   gd  = m * (long)(4 * Divisions);
            double gms = gd * msPerDiv;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            foreach (int onset in new[] { 0, Half })
            {
                bool first = true;
                foreach (int p in voicings[ci])
                {
                    var n = NoteFromMidi(p, Half, onset, staff: 1, voice: 1, velocity: 75);
                    if (!first) n.IsChord = true;
                    rhM.Notes.Add(n);
                    first = false;
                }
            }
            rh.Measures.Add(rhM);

            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int b = 0; b < 4; b++)
                lhM.Notes.Add(NoteFromMidi(walkers[ci][b], Quarter, b * Quarter, staff: 2, voice: 2, velocity: 80));
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore StyleRnB()
    {
        const int bpm        = 92;
        const int measures   = 16;
        const double msPerDiv = 60_000.0 / (120.0 * Divisions);

        var score = new MxlScore("R&B: 16th-note groove C7–F7", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Piano LH", PartIndex = 1 };

        int[] penta    = { 60, 63, 65, 67, 70, 72, 75, 77 };
        int[] rhSlots  = { 0, -1, 1, -1, 2, -1, 3, 3, -1, 4, -1, 5, -1, 3, 1, -1 };
        (int slot, int midi, int vel)[] lhPattern = {
            (0, 36, 95), (2, 36, 55), (4, 36, 80), (6, 41, 60),
            (8, 41, 95), (10, 41, 55), (12, 36, 80), (14, 36, 65),
        };

        for (int m = 0; m < measures; m++)
        {
            long   gd  = m * (long)(4 * Divisions);
            double gms = gd * msPerDiv;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int s = 0; s < 16; s++)
            {
                int pi = rhSlots[s % rhSlots.Length];
                if (pi < 0) continue;
                int pitch = penta[(pi + m / 4) % penta.Length];
                rhM.Notes.Add(NoteFromMidi(pitch, Sixteenth, s * Sixteenth, staff: 1, voice: 1, velocity: 85));
            }
            rh.Measures.Add(rhM);

            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            foreach (var (slot, midi, vel) in lhPattern)
                lhM.Notes.Add(NoteFromMidi(midi, Sixteenth, slot * Sixteenth, staff: 2, voice: 2, velocity: vel));
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore StyleRock()
    {
        const int bpm        = 130;
        const double msPerDiv = 60_000.0 / (120.0 * Divisions);

        var score = new MxlScore("Rock: 12-bar Blues in E", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Piano LH", PartIndex = 1 };

        int[] roots = { 52,52,52,52, 57,57,52,52, 59,57,52,59 };
        int[] lick  = { 64,67,64,62, 64,67,69,67, 64,62,64,62, 60,62,64,64 };

        for (int m = 0; m < 12; m++)
        {
            long   gd  = m * (long)(4 * Divisions);
            double gms = gd * msPerDiv;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "E major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int b = 0; b < 4; b++)
                rhM.Notes.Add(NoteFromMidi(lick[(m * 4 + b) % lick.Length], Quarter, b * Quarter, staff: 1, voice: 1, velocity: 88));
            rh.Measures.Add(rhM);

            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "E major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            int r = roots[m];
            foreach (int onset in new[] { 0, Half })
            {
                lhM.Notes.Add(NoteFromMidi(r,     Half, onset, staff: 2, voice: 2, velocity: 95));
                var fifth = NoteFromMidi(r + 7, Half, onset, staff: 2, voice: 2, velocity: 90);
                fifth.IsChord = true;
                lhM.Notes.Add(fifth);
            }
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore StyleHipHop()
    {
        const int bpm        = 85;
        const double msPerDiv = 60_000.0 / (120.0 * Divisions);

        var score = new MxlScore("Hip-Hop: Boom-Bap (Am7 stabs)", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH (stabs)", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Drums LH",         PartIndex = 1 };

        int[] stab  = { 69, 72, 76, 79 };
        int[] stab2 = { 62, 65, 69, 72 };
        (int slot, int midi, int vel)[] drumPat = {
            (0,24,100),(2,30,55),(4,26,95),(6,30,55),(6,24,80),
            (8,24,100),(10,30,55),(12,26,95),(14,30,55),
        };
        int[] stabSlots = { 6, 14 };

        for (int m = 0; m < 16; m++)
        {
            long   gd  = m * (long)(4 * Divisions);
            double gms = gd * msPerDiv;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "A minor", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            int[] chord = (m % 2 == 0) ? stab : stab2;
            foreach (int s in stabSlots)
            {
                bool first = true;
                foreach (int p in chord)
                {
                    var n = NoteFromMidi(p, Sixteenth * 2, s * Sixteenth, staff: 1, voice: 1, velocity: 90);
                    if (!first) n.IsChord = true;
                    rhM.Notes.Add(n);
                    first = false;
                }
            }
            rh.Measures.Add(rhM);

            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "A minor", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            foreach (var (slot, midi, vel) in drumPat)
                lhM.Notes.Add(NoteFromMidi(midi, Sixteenth, slot * Sixteenth, staff: 2, voice: 2, velocity: vel));
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore StyleLatin()
    {
        const int bpm        = 110;
        const double msPerDiv = 60_000.0 / (120.0 * Divisions);

        var score = new MxlScore("Latin: 2-3 Son Clave Montuno (C F G)", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH (montuno)", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Clave/Bass LH",      PartIndex = 1 };

        (int slot, int midi)[] montunoA = { (0,67),(2,64),(3,67),(5,72),(8,64),(10,67),(12,72),(14,76) };
        (int slot, int midi)[] montunoB = { (0,65),(2,69),(3,65),(5,72),(8,69),(10,65),(12,72),(14,69) };
        int[] claveA   = { 0, 3, 6 };
        int[] claveB   = { 2, 4, 8, 12 };
        int clavePitch = 56;

        for (int m = 0; m < 16; m++)
        {
            long   gd  = m * (long)(4 * Divisions);
            double gms = gd * msPerDiv;
            bool isBarA  = m % 2 == 0;
            var montuno  = isBarA ? montunoA : montunoB;
            int[] clave  = isBarA ? claveA   : claveB;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            foreach (var (s, p) in montuno)
                rhM.Notes.Add(NoteFromMidi(p, Sixteenth * 2, s * Sixteenth, staff: 1, voice: 1, velocity: 80));
            rh.Measures.Add(rhM);

            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            foreach (int s in clave)
                lhM.Notes.Add(NoteFromMidi(clavePitch, Sixteenth * 2, s * Sixteenth, staff: 2, voice: 2, velocity: 95));
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore StyleTango()
    {
        const int bpm        = 110;
        const double msPerDiv = 60_000.0 / (120.0 * Divisions);

        var score = new MxlScore("Tango: Habanera in A minor", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Piano LH", PartIndex = 1 };

        int[][] chords = { new[]{57,60,64}, new[]{52,56,59} };
        int[] motif    = { 69,68,67,66,65,64,63,62 };
        (int slot, int dur, int midi, int vel)[] lhPat = {
            (0,3,45,95),(3,1,45,70),(4,2,45,85),(6,2,40,75),
            (8,3,45,95),(11,1,45,70),(12,2,45,85),(14,2,43,75),
        };

        for (int m = 0; m < 16; m++)
        {
            long   gd  = m * (long)(4 * Divisions);
            double gms = gd * msPerDiv;
            int ci = m % 2;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "A minor", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            bool first = true;
            foreach (int p in chords[ci])
            {
                var n = NoteFromMidi(p, Sixteenth * 2, 0, staff: 1, voice: 1, velocity: 92);
                if (!first) n.IsChord = true;
                rhM.Notes.Add(n);
                first = false;
            }
            rhM.Notes.Add(NoteFromMidi(motif[(m * 2) % motif.Length], Sixteenth * 2, 4 * Sixteenth, staff: 1, voice: 1, velocity: 75));
            first = true;
            foreach (int p in chords[ci])
            {
                var n = NoteFromMidi(p, Sixteenth * 2, 8 * Sixteenth, staff: 1, voice: 1, velocity: 88);
                if (!first) n.IsChord = true;
                rhM.Notes.Add(n);
                first = false;
            }
            rhM.Notes.Add(NoteFromMidi(motif[(m * 2 + 1) % motif.Length], Sixteenth * 2, 12 * Sixteenth, staff: 1, voice: 1, velocity: 72));
            rh.Measures.Add(rhM);

            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "A minor", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            foreach (var (slot, dur, midi, vel) in lhPat)
                lhM.Notes.Add(NoteFromMidi(midi, dur * Sixteenth, slot * Sixteenth, staff: 2, voice: 2, velocity: vel));
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    /// <summary>
    /// Bossa Nova — "Girl from Ipanema" feel in C major.
    /// LH plays the 2-bar bossa clave (3+3+2 rhythm); RH plays shell voicings
    /// (Cmaj7 → Am7 → Dm7 → G7) with characteristic samba-inflected anticipations.
    /// 16 measures, 130 BPM.
    /// </summary>
    public static MxlScore StyleBossaNova()
    {
        const int bpm        = 130;
        const double msPerDiv = 60_000.0 / (120.0 * Divisions);

        var score = new MxlScore("Bossa Nova: Girl from Ipanema feel (C)", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Guitar/Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Bass/Rhythm LH",  PartIndex = 1 };

        // Four-chord vamp (2 bars each): Cmaj7 Am7 Dm7 G7
        int[][] voicings = {
            new[]{60,64,67,71},  // Cmaj7: C4 E4 G4 B4
            new[]{57,60,64,67},  // Am7:   A3 C4 E4 G4
            new[]{62,65,69,72},  // Dm7:   D4 F4 A4 C5
            new[]{67,71,74,77},  // G7:    G4 B4 D5 F5
        };
        int[] lhRoots = { 36, 33, 38, 43 }; // C2 A1 D2 G2

        // Bossa clave: 3+3+2 over 16 sixteenth-note slots per bar
        // Feels like: slot 0, 3, 6, 10, 12  (bar A)
        // Bar B shifts: slot 0, 2, 6, 8, 12
        int[] claveA = { 0, 3, 6, 10, 12 };
        int[] claveB = { 0, 2, 6,  8, 12 };

        // RH anticipation offsets (16th-note slots within each half-bar)
        // Chord on downbeat (slot 0) + anticipation on slot 3 of each half-bar
        int[] rhSlots = { 0, 3 };

        for (int m = 0; m < 16; m++)
        {
            int ci  = (m / 2) % 4;          // chord changes every 2 bars
            long   gd  = m * (long)(4 * Divisions);
            double gms = gd * msPerDiv;
            int[] clave = m % 2 == 0 ? claveA : claveB;

            // RH: two chord stabs per bar at characteristic bossa positions
            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            foreach (int slot in rhSlots)
            {
                bool first = true;
                foreach (int p in voicings[ci])
                {
                    var n = NoteFromMidi(p, Sixteenth * 3, slot * Sixteenth, staff: 1, voice: 1, velocity: 78);
                    if (!first) n.IsChord = true;
                    rhM.Notes.Add(n);
                    first = false;
                }
            }
            rh.Measures.Add(rhM);

            // LH: bass note on beat 1 + bossa clave pattern
            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            // Root on beat 1
            lhM.Notes.Add(NoteFromMidi(lhRoots[ci], Quarter, 0, staff: 2, voice: 2, velocity: 90));
            // Clave taps (muted/ghost – softer) on remaining slots
            foreach (int s in clave.Skip(1))
                lhM.Notes.Add(NoteFromMidi(lhRoots[ci] + 12, Sixteenth, s * Sixteenth, staff: 2, voice: 2, velocity: 60));
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore StyleGospel()
    {
        const int bpm        = 88;
        const double msPerDiv = 60_000.0 / (120.0 * Divisions);

        var score = new MxlScore("Gospel: I–vi–ii–V7 in F (Fmaj7 Dm7 Gm7 C7)", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Piano LH", PartIndex = 1 };

        int[][] voicings = {
            new[]{65,69,72,76}, new[]{62,65,69,72}, new[]{67,70,74,77}, new[]{60,64,67,70},
        };
        int[] lhRoots = { 41, 38, 43, 36 };

        for (int m = 0; m < 16; m++)
        {
            int ci  = m % 4;
            long   gd  = m * (long)(4 * Divisions);
            double gms = gd * msPerDiv;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "F major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int b = 0; b < 4; b++)
            {
                bool first = true;
                foreach (int p in voicings[ci])
                {
                    var n = NoteFromMidi(p, Quarter, b * Quarter, staff: 1, voice: 1, velocity: 82);
                    if (!first) n.IsChord = true;
                    rhM.Notes.Add(n);
                    first = false;
                }
            }
            rh.Measures.Add(rhM);

            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "F major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            int lr = lhRoots[ci];
            lhM.Notes.Add(NoteFromMidi(lr,      Half, 0,    staff: 2, voice: 2, velocity: 90));
            lhM.Notes.Add(NoteFromMidi(lr + 16, Half, Half, staff: 2, voice: 2, velocity: 80));
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore StyleCountry()
    {
        const int bpm        = 120;
        const double msPerDiv = 60_000.0 / (120.0 * Divisions);

        var score = new MxlScore("Country: Two-Step in G (G C D G)", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Fiddle/Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Bass/Piano LH",   PartIndex = 1 };

        int[] penta     = { 67,69,71,74,76,79,81 };
        int[] phrase    = { 0,1,2,3,2,1,0,2, 3,4,3,2,1,2,3,4, 4,5,4,3,2,3,4,5, 5,6,5,4,3,4,5,4 };
        int[] lhRoots   = { 43, 41, 45, 43 };
        int[] lhFifths  = { 50, 48, 52, 50 };

        for (int m = 0; m < 16; m++)
        {
            int ci  = m % 4;
            long   gd  = m * (long)(4 * Divisions);
            double gms = gd * msPerDiv;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "G major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int b = 0; b < 4; b++)
            {
                int pi = phrase[(m * 4 + b) % phrase.Length];
                rhM.Notes.Add(NoteFromMidi(penta[pi % penta.Length], Quarter, b * Quarter, staff: 1, voice: 1, velocity: 78));
            }
            rh.Measures.Add(rhM);

            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "G major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            lhM.Notes.Add(NoteFromMidi(lhRoots[ci],  Half, 0,    staff: 2, voice: 2, velocity: 90));
            lhM.Notes.Add(NoteFromMidi(lhFifths[ci], Half, Half, staff: 2, voice: 2, velocity: 72));
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }

    public static MxlScore StyleRagtime()
    {
        const int bpm        = 100;
        const double msPerDiv = 60_000.0 / (120.0 * Divisions);

        var score = new MxlScore("Ragtime: Oom-Pah in C (Joplin style)", bpm);
        var rh    = new MxlPart { PartId = "P1", InstrumentName = "Piano RH", PartIndex = 0 };
        var lh    = new MxlPart { PartId = "P2", InstrumentName = "Piano LH", PartIndex = 1 };

        int[] melodySlots = { 60,62,-1,64, 65,64,-1,62, 60,-1,64,65, 67,-1,65,64 };
        int[][] lhChords  = {
            new[]{64,67}, new[]{62,65}, new[]{64,67}, new[]{62,65},
            new[]{65,69}, new[]{64,67}, new[]{62,65}, new[]{64,67},
        };
        int[] lhBass = { 48,43,48,43,41,48,43,48 };

        for (int m = 0; m < 16; m++)
        {
            long   gd  = m * (long)(4 * Divisions);
            double gms = gd * msPerDiv;
            int ci = m % lhChords.Length;

            var rhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int s = 0; s < 16; s++)
            {
                int p = melodySlots[s];
                if (p < 0) continue;
                if (m >= 8) p += 4;
                rhM.Notes.Add(NoteFromMidi(p, Sixteenth * 2, s * Sixteenth, staff: 1, voice: 1, velocity: 82));
            }
            rh.Measures.Add(rhM);

            var lhM = new MxlMeasure
            {
                Number = m + 1, TimeSig = "4/4", KeySig = "C major", Divisions = Divisions,
                GlobalOnsetDivisions = gd, GlobalOnsetMs = gms, TimeSigBeats = 4, TimeSigBeatType = 4,
            };
            for (int b = 0; b < 4; b++)
            {
                if (b % 2 == 0)
                {
                    lhM.Notes.Add(NoteFromMidi(lhBass[ci], Quarter, b * Quarter, staff: 2, voice: 2, velocity: 88));
                }
                else
                {
                    bool first = true;
                    foreach (int p in lhChords[ci])
                    {
                        var n = NoteFromMidi(p, Quarter, b * Quarter, staff: 2, voice: 2, velocity: 72);
                        if (!first) n.IsChord = true;
                        lhM.Notes.Add(n);
                        first = false;
                    }
                }
            }
            lh.Measures.Add(lhM);
        }
        score.Parts.Add(rh);
        score.Parts.Add(lh);
        return score;
    }
}
