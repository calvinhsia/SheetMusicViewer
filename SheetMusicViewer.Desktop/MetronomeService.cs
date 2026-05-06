using NAudio.Wave;
using NAudio.Wave.SampleProviders;
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

    // ── Runtime state ──────────────────────────────────────────────────────
    private System.Threading.Timer? _timer;
    private readonly Stopwatch _stopwatch = new();
    private long _beatIntervalMs;
    private long _startEpochMs;         // wallclock origin for drift correction
    private long _totalBeatsElapsed;    // beats since Start()

    // ── Audio ───────────────────────────────────────────────────────────────
    // Windows: WASAPI via NAudio (in-memory PCM, no temp files)
    private WasapiOut?   _wasapiOut;
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

        // Play audio click
        if (!_muteAudio && _audioReady)
        {
            PlayClick(isAccent);
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
                File.WriteAllBytes(_accentWavPath, ClickToWav(GenerateClick(isAccent: true,  sampleRate: 44100)));
                File.WriteAllBytes(_normalWavPath, ClickToWav(GenerateClick(isAccent: false, sampleRate: 44100)));
                _audioReady = true;
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
        // Pre-render both clicks as float PCM
        _accentSamples = GenerateClick(isAccent: true,  sampleRate: WavSampleRate);
        _normalSamples = GenerateClick(isAccent: false, sampleRate: WavSampleRate);

        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(WavSampleRate, 1);
        _mixer = new MixingSampleProvider(waveFormat) { ReadFully = true };

        _wasapiOut = new WasapiOut(
            NAudio.CoreAudioApi.AudioClientShareMode.Shared,
            useEventSync: true,
            latency: 50);
        _wasapiOut.Init(_mixer);
        _wasapiOut.Play();
        _audioReady = true;
    }

    private void CleanupAudio()
    {
        try { _wasapiOut?.Stop(); _wasapiOut?.Dispose(); _wasapiOut = null; } catch { }
        try { if (_accentWavPath != null) File.Delete(_accentWavPath); } catch { }
        try { if (_normalWavPath != null) File.Delete(_normalWavPath); } catch { }
    }

    private void PlayClick(bool isAccent)
    {
        if (!_audioReady) return;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                PlayClickWasapi(isAccent);
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
            Trace.WriteLine($"MetronomeService: PlayClick error: {ex.Message}");
        }
    }

    private void PlayClickWasapi(bool isAccent)
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

    /// <summary>
    /// Synthesises a realistic metronome click:
    /// - Sharp transient via a short burst of noise with instant attack
    /// - Pitched "body" from two sine tones that decay exponentially
    /// - All enveloped with a fast exponential decay so there is no tail
    /// Accent click is higher-pitched and louder than the normal click.
    /// </summary>
    public static float[] GenerateClick(bool isAccent, int sampleRate)
    {
        // Tuning: accent is a high woodblock-ish click, normal is a softer lower tick
        double bodyFreq   = isAccent ? 1800.0 : 1100.0;  // main tone (Hz)
        double bodyFreq2  = isAccent ? 900.0  :  550.0;  // 2nd partial (Hz)
        float  bodyVol    = isAccent ? 0.55f  :   0.40f;
        float  noiseVol   = isAccent ? 0.45f  :   0.28f;
        double decayRate  = isAccent ? 55.0   :   45.0;  // higher = faster decay
        double noiseTau   = isAccent ? 180.0  :  140.0;  // noise decays faster still
        double durationMs = 80.0;                         // total click length (ms)

        int numSamples = (int)(sampleRate * durationMs / 1000.0);
        var samples    = new float[numSamples];
        var rng        = new Random(42);  // deterministic so tests are stable

        for (int i = 0; i < numSamples; i++)
        {
            double t = i / (double)sampleRate;

            // Exponential amplitude envelope (fast decay, instant attack)
            double env      = Math.Exp(-decayRate * t);
            double noiseEnv = Math.Exp(-noiseTau  * t);

            // Tonal body: two sine partials
            double tone = Math.Sin(2 * Math.PI * bodyFreq  * t)
                        + Math.Sin(2 * Math.PI * bodyFreq2 * t) * 0.4;

            // Percussive transient: band-limited noise burst
            double noise = (rng.NextDouble() * 2.0 - 1.0);

            samples[i] = (float)(tone * bodyVol * env + noise * noiseVol * noiseEnv);
        }

        // Soft-clip to avoid any inter-sample overs
        for (int i = 0; i < numSamples; i++)
        {
            float s = samples[i];
            samples[i] = s / (1.0f + Math.Abs(s)) * 1.8f;  // tanh approximation
        }

        return samples;
    }

    /// <summary>Wraps float[] PCM samples into a standard 16-bit mono WAV byte array.</summary>
    public static byte[] ClickToWav(float[] samples, int sampleRate = 44100)
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
