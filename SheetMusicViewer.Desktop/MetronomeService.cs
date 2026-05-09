using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SheetMusicLib;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace SheetMusicViewer.Desktop;

/// <summary>
/// Arguments for a metronome beat event.
/// </summary>
public class BeatEventArgs : EventArgs
{
    /// <summary>0-based running beat count since Start().</summary>
    public long TotalBeats { get; init; }
    /// <summary>
    /// True when this beat is an accent beat.
    /// AccentEvery==0 → never accent; otherwise accent when TotalBeats % AccentEvery == 0.
    /// </summary>
    public bool IsAccent { get; init; }
}

/// <summary>
/// Cross-platform metronome engine.
/// Uses a drift-corrected System.Threading.Timer for accurate tempo and
/// platform-native audio (winmm on Windows, afplay on macOS, aplay on Linux).
/// </summary>
public sealed class MetronomeService : IDisposable
{
    // ── Configuration ──────────────────────────────────────────────────────
    private int _tempo = 120;           // beats per minute
    private int _accentEvery = 4;       // 0 = no accent; N = accent on every Nth beat
    private bool _muteAudio;
    private BeatSound _sound = BeatSound.Woodblock;

    // ── Runtime state ──────────────────────────────────────────────────────
    private System.Threading.Timer? _timer;
    private readonly Stopwatch _stopwatch = new();
    private long _beatIntervalMs;
    private long _startEpochMs;         // wallclock origin for drift correction
    private long _totalBeatsElapsed;    // beats since Start()

    // ── Audio ───────────────────────────────────────────────────────────────
    // Windows: WaveOutEvent via NAudio — routes to current default device including Bluetooth
    private WaveOutEvent?   _waveOut;
    private MixingSampleProvider? _mixer;
    private float[]?     _accentSamples;
    private float[]?     _normalSamples;
    private const int    WavSampleRate = 44100;

    // non-Windows: write WAV to temp files and shell out
    private string?      _accentWavPath;
    private string?      _normalWavPath;

    private bool         _audioReady;
    private static readonly object _audioLock = new();

    // ── Events ──────────────────────────────────────────────────────────────
    /// <summary>Raised on every beat (from a thread-pool thread).</summary>
    public event EventHandler<BeatEventArgs>? Beat;

    // ── Properties ──────────────────────────────────────────────────────────
    public bool IsRunning { get; private set; }

    public int Tempo
    {
        get => _tempo;
        set
        {
            _tempo = Math.Clamp(value, 20, 300);
            _beatIntervalMs = BpmToMs(_tempo);
            // Re-anchor the drift-correction epoch so the next beat fires at the
            // NEW interval from now, not from the original start time.
            // Without this, lowering BPM causes a long silence because the old
            // epoch math puts the next expected beat far in the future.
            if (IsRunning)
            {
                _startEpochMs      = Environment.TickCount64;
                _totalBeatsElapsed = 0;
                // Fire the next beat immediately at the new interval
                _timer?.Change((int)_beatIntervalMs, Timeout.Infinite);
            }
        }
    }

    /// <summary>
    /// Accent every N beats.  0 = no accent (all clicks are identical normal clicks).
    /// E.g. 4 = accent on beats 0, 4, 8, 12 …
    /// </summary>
    public int AccentEvery
    {
        get => _accentEvery;
        set => _accentEvery = Math.Max(0, value);
    }

    public bool MuteAudio
    {
        get => _muteAudio;
        set => _muteAudio = value;
    }

    public BeatSound Sound
    {
        get => _sound;
        set
        {
            _sound = value;
            RenderSamples();   // pre-render the new beat sound immediately
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────
    public MetronomeService()
    {
        _beatIntervalMs = BpmToMs(_tempo);
        InitAudio();
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _totalBeatsElapsed = 0;
        _stopwatch.Restart();
        _startEpochMs = Environment.TickCount64;
        _timer = new System.Threading.Timer(OnTimerTick, null, 0, Timeout.Infinite);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
        _stopwatch.Stop();
    }

    public void Dispose()
    {
        Stop();
        CleanupAudio();
    }

    // ── Timer callback ────────────────────────────────────────────────────────
    private void OnTimerTick(object? state)
    {
        if (!IsRunning) return;

        // Determine accent
        var isAccent = _accentEvery > 0 && (_totalBeatsElapsed % _accentEvery) == 0;

        // Fire the beat event
        try
        {
            Beat?.Invoke(this, new BeatEventArgs { TotalBeats = _totalBeatsElapsed, IsAccent = isAccent });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"MetronomeService: Beat handler error: {ex.Message}");
        }

        // Play audio beat
        if (!_muteAudio && _audioReady)
        {
            PlayBeat(isAccent);
        }

        _totalBeatsElapsed++;

        // Drift-corrected next delay: keep running if still active
        if (IsRunning)
        {
            ScheduleNextBeat();
        }
    }

    private void ScheduleNextBeat()
    {
        // Expected time of the next beat relative to the start epoch
        var expectedMs = _startEpochMs + (_totalBeatsElapsed * _beatIntervalMs);
        var nowMs = Environment.TickCount64;
        var delayMs = Math.Max(0, expectedMs - nowMs);
        _timer?.Change((int)delayMs, Timeout.Infinite);
    }

    // ── Audio helpers ─────────────────────────────────────────────────────────
    private void InitAudio()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                InitWasapi();
            }
            else
            {
                _accentWavPath = Path.Combine(Path.GetTempPath(), "smc_metronome_accent.wav");
                _normalWavPath = Path.Combine(Path.GetTempPath(), "smc_metronome_normal.wav");
                _audioReady = true;
                RenderSamples();
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"MetronomeService: Audio init failed: {ex.Message}");
            _audioReady = false;
        }
    }

    private void InitWasapi()
    {
        // Use WaveOutEvent (WinMM) — automatically routes to the current default
        // playback device, including Bluetooth speakers, without WASAPI format negotiation.
        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(WavSampleRate, 1);
        _mixer = new MixingSampleProvider(waveFormat) { ReadFully = true };

        _waveOut = new WaveOutEvent { DesiredLatency = 100 };
        _waveOut.Init(_mixer);
        _waveOut.Play();
        _audioReady = true;

        RenderSamples();
    }

    /// <summary>Re-renders _accentSamples/_normalSamples for the current Sound.</summary>
    private void RenderSamples()
    {
        _accentSamples = GenerateBeat(isAccent: true,  sound: _sound, sampleRate: WavSampleRate);
        _normalSamples = GenerateBeat(isAccent: false, sound: _sound, sampleRate: WavSampleRate);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _audioReady)
        {
            // Rewrite temp WAV files for mac/linux
            if (_accentWavPath != null) File.WriteAllBytes(_accentWavPath, BeatToWav(_accentSamples, 44100));
            if (_normalWavPath != null) File.WriteAllBytes(_normalWavPath, BeatToWav(_normalSamples, 44100));
        }
    }

    private void CleanupAudio()
    {
        try { _waveOut?.Stop(); _waveOut?.Dispose(); _waveOut = null; } catch { }
        try { if (_accentWavPath != null) File.Delete(_accentWavPath); } catch { }
        try { if (_normalWavPath != null) File.Delete(_normalWavPath); } catch { }
    }

    private void PlayBeat(bool isAccent)
    {
        if (!_audioReady) return;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                PlayBeatWasapi(isAccent);
            }
            else
            {
                var path = isAccent ? _accentWavPath : _normalWavPath;
                if (path == null) return;
                lock (_audioLock)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        Process.Start(new ProcessStartInfo("afplay", $"\"{path}\"")
                        {
                            UseShellExecute = false, CreateNoWindow = true,
                            RedirectStandardOutput = true, RedirectStandardError = true
                        });
                    }
                    else
                    {
                        if (!TryStartProcess("aplay", $"-q \"{path}\""))
                            TryStartProcess("paplay", $"\"{path}\"");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"MetronomeService: PlayBeat error: {ex.Message}");
        }
    }

    private void PlayBeatWasapi(bool isAccent)
    {
        var samples = isAccent ? _accentSamples : _normalSamples;
        if (samples == null || _mixer == null) return;
        // Wrap the float array as an ISampleProvider and add to the mixer
        var provider = new RawSourceWaveStream(
            new MemoryStream(FloatsToBytes(samples)), 
            WaveFormat.CreateIeeeFloatWaveFormat(WavSampleRate, 1))
            .ToSampleProvider();
        _mixer.AddMixerInput(provider);
    }

    private static byte[] FloatsToBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    // ── Beat synthesis ────────────────────────────────────────────────────────

    /// <summary>
    /// Dispatches to the correct synthesizer for the chosen <see cref="BeatSound"/>.
    /// All synthesizers return float[] PCM at <paramref name="sampleRate"/>.
    /// </summary>
    public static float[] GenerateBeat(bool isAccent, BeatSound sound, int sampleRate)
    {
        return sound switch
        {
            BeatSound.Sine      => SynthSine     (isAccent, sampleRate),
            BeatSound.Rimshot   => SynthRimshot  (isAccent, sampleRate),
            BeatSound.Hihat     => SynthHihat    (isAccent, sampleRate),
            BeatSound.Beep      => SynthBeep     (isAccent, sampleRate),
            _                    => SynthWoodblock(isAccent, sampleRate),  // Woodblock (default)
        };
    }

    // ── back-compat overload: delegates to Woodblock ──
    public static float[] GenerateBeat(bool isAccent, int sampleRate) =>
        GenerateBeat(isAccent, BeatSound.Woodblock, sampleRate);

    /// <summary>Woodblock: two-partial tone + noise burst, exponential decay.</summary>
    private static float[] SynthWoodblock(bool isAccent, int sampleRate)
    {
        double bodyFreq  = isAccent ? 1800.0 : 1100.0;
        double bodyFreq2 = isAccent ?  900.0 :  550.0;
        float  bodyVol   = isAccent ?  0.55f :   0.40f;
        float  noiseVol  = isAccent ?  0.45f :   0.28f;
        double decayRate = isAccent ?   55.0 :    45.0;
        double noiseTau  = isAccent ?  180.0 :   140.0;
        int    n         = (int)(sampleRate * 0.080);
        var    buf       = new float[n];
        var    rng       = new Random(42);
        for (int i = 0; i < n; i++)
        {
            double t    = i / (double)sampleRate;
            double env  = Math.Exp(-decayRate * t);
            double nEnv = Math.Exp(-noiseTau  * t);
            double tone = Math.Sin(2 * Math.PI * bodyFreq  * t)
                        + Math.Sin(2 * Math.PI * bodyFreq2 * t) * 0.4;
            buf[i] = (float)(tone * bodyVol * env + (rng.NextDouble() * 2 - 1) * noiseVol * nEnv);
        }
        return SoftClip(buf);
    }

    /// <summary>Sine: smooth pure tone with linear fade-out.</summary>
    private static float[] SynthSine(bool isAccent, int sampleRate)
    {
        double freq  = isAccent ? 880.0 : 660.0;
        float  vol   = isAccent ? 0.9f  : 0.6f;
        int    n     = (int)(sampleRate * 0.10);
        var    buf   = new float[n];
        int    fade  = (int)(n * 0.70);
        for (int i = 0; i < n; i++)
        {
            double t    = i / (double)sampleRate;
            double env  = i < fade ? 1.0 : 1.0 - (double)(i - fade) / (n - fade);
            buf[i] = (float)(Math.Sin(2 * Math.PI * freq * t) * vol * env);
        }
        return buf;
    }

    /// <summary>Rimshot: very short, high-pitched noise + sine transient.</summary>
    private static float[] SynthRimshot(bool isAccent, int sampleRate)
    {
        double freq  = isAccent ? 2500.0 : 1800.0;
        float  vol   = isAccent ?  0.85f :  0.65f;
        double decay = isAccent ?  200.0 :  160.0;
        int    n     = (int)(sampleRate * 0.045);
        var    buf   = new float[n];
        var    rng   = new Random(7);
        for (int i = 0; i < n; i++)
        {
            double t   = i / (double)sampleRate;
            double env = Math.Exp(-decay * t);
            double sig = Math.Sin(2 * Math.PI * freq * t) * 0.6
                       + (rng.NextDouble() * 2 - 1) * 0.4;
            buf[i] = (float)(sig * vol * env);
        }
        return SoftClip(buf);
    }

    /// <summary>Hihat: filtered white noise, metallic/crisp.</summary>
    private static float[] SynthHihat(bool isAccent, int sampleRate)
    {
        float  vol   = isAccent ? 0.80f :  0.55f;
        double decay = isAccent ?  90.0 :   70.0;
        int    n     = (int)(sampleRate * 0.060);
        var    buf   = new float[n];
        var    rng   = new Random(13);
        // Simple two-pole high-pass to give metallic colour
        double hp = 0, prev = 0;
        for (int i = 0; i < n; i++)
        {
            double t    = i / (double)sampleRate;
            double env  = Math.Exp(-decay * t);
            double raw  = rng.NextDouble() * 2 - 1;
            // one-pole HP: y[n] = 0.9*(y[n-1] + x[n] - x[n-1])
            hp   = 0.92 * (hp + raw - prev);
            prev = raw;
            buf[i] = (float)(hp * vol * env);
        }
        return SoftClip(buf);
    }

    /// <summary>Beep: short square-wave blip.</summary>
    private static float[] SynthBeep(bool isAccent, int sampleRate)
    {
        double freq  = isAccent ? 1046.5 : 784.0;   // C6 / G5
        float  vol   = isAccent ?  0.70f :  0.50f;
        int    n     = (int)(sampleRate * 0.055);
        var    buf   = new float[n];
        int    fade  = (int)(n * 0.80);
        for (int i = 0; i < n; i++)
        {
            double t   = i / (double)sampleRate;
            double env = i < fade ? 1.0 : 1.0 - (double)(i - fade) / (n - fade);
            // Square wave: sign of sine
            double sq  = Math.Sin(2 * Math.PI * freq * t) >= 0 ? 1.0 : -1.0;
            buf[i] = (float)(sq * vol * env);
        }
        return buf;
    }

    private static float[] SoftClip(float[] buf)
    {
        for (int i = 0; i < buf.Length; i++)
        {
            float s = buf[i];
            buf[i] = s / (1.0f + Math.Abs(s)) * 1.8f;
        }
        return buf;
    }

    /// <summary>Wraps float[] PCM samples into a standard 16-bit mono WAV byte array.</summary>
    public static byte[] BeatToWav(float[] samples, int sampleRate = 44100)
    {
        const int bitsPerSample = 16;
        const int channels      = 1;
        int       dataSize      = samples.Length * channels * (bitsPerSample / 8);

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        bw.Write(new[] { (byte)'R',(byte)'I',(byte)'F',(byte)'F' });
        bw.Write(36 + dataSize);
        bw.Write(new[] { (byte)'W',(byte)'A',(byte)'V',(byte)'E' });
        bw.Write(new[] { (byte)'f',(byte)'m',(byte)'t',(byte)' ' });
        bw.Write(16);                                              // chunk size
        bw.Write((short)1);                                        // PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bitsPerSample / 8);       // byte rate
        bw.Write((short)(channels * bitsPerSample / 8));           // block align
        bw.Write((short)bitsPerSample);
        bw.Write(new[] { (byte)'d',(byte)'a',(byte)'t',(byte)'a' });
        bw.Write(dataSize);

        foreach (var s in samples)
            bw.Write((short)Math.Clamp((int)(s * short.MaxValue), short.MinValue, short.MaxValue));

        return ms.ToArray();
    }

    private static bool TryStartProcess(string program, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(program, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            return true;
        }
        catch { return false; }
    }

    // ── Static helpers ────────────────────────────────────────────────────────
    public static long BpmToMs(int bpm) => bpm > 0 ? 60_000L / bpm : 500L;
}
