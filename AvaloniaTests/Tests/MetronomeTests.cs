using Microsoft.VisualStudio.TestTools.UnitTesting;
using SheetMusicLib;
using SheetMusicViewer.Desktop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AvaloniaTests.Tests;

/// <summary>
/// Unit tests for MetronomeService and MetronomeSettings.
/// </summary>
[TestClass]
public class MetronomeTests : TestBase
{
    private MetronomeService? _metronome;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _metronome = new MetronomeService();
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        _metronome?.Dispose();
        _metronome = null;
        base.TestCleanup();
    }

    // ── BpmToMs ────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void BpmToMs_120Bpm_Returns500()
    {
        Assert.AreEqual(500L, MetronomeService.BpmToMs(120));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BpmToMs_60Bpm_Returns1000()
    {
        Assert.AreEqual(1000L, MetronomeService.BpmToMs(60));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BpmToMs_ZeroBpm_DoesNotThrow()
    {
        // Should return the safe fallback value, not throw
        var ms = MetronomeService.BpmToMs(0);
        Assert.IsTrue(ms > 0, "Result should be positive");
    }

    // ── AccentEvery ────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void AccentEvery_Default_Is4()
    {
        Assert.AreEqual(4, _metronome!.AccentEvery);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AccentEvery_Set_StoresValue()
    {
        _metronome!.AccentEvery = 6;
        Assert.AreEqual(6, _metronome.AccentEvery);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AccentEvery_NegativeValue_ClampedToZero()
    {
        _metronome!.AccentEvery = -5;
        Assert.AreEqual(0, _metronome.AccentEvery);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AccentEvery_Zero_MeansNoAccent()
    {
        _metronome!.AccentEvery = 0;
        Assert.AreEqual(0, _metronome.AccentEvery);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AccentEvery_FrequentValues_AreAccepted()
    {
        // Free-form: 5/4, 7/8, 9/8, 11/8 etc. represented as just the beat count
        foreach (var n in new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 })
        {
            _metronome!.AccentEvery = n;
            Assert.AreEqual(n, _metronome.AccentEvery, $"AccentEvery={n} should be stored as-is");
        }
    }


    // ── Tempo clamping ─────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void Tempo_ClampedToMin_20()
    {
        _metronome!.Tempo = -100;
        Assert.AreEqual(20, _metronome.Tempo);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Tempo_ClampedToMax_300()
    {
        _metronome!.Tempo = 9999;
        Assert.AreEqual(300, _metronome.Tempo);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Tempo_ValidValue_Stored()
    {
        _metronome!.Tempo = 180;
        Assert.AreEqual(180, _metronome.Tempo);
    }

    // ── Start / Stop ───────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void IsRunning_AfterStart_IsTrue()
    {
        _metronome!.Start();
        Assert.IsTrue(_metronome.IsRunning);
        _metronome.Stop();
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IsRunning_AfterStop_IsFalse()
    {
        _metronome!.Start();
        _metronome.Stop();
        Assert.IsFalse(_metronome.IsRunning);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DoubleStart_DoesNotThrow()
    {
        _metronome!.Start();
        _metronome.Start(); // second Start should be a no-op
        Assert.IsTrue(_metronome.IsRunning);
        _metronome.Stop();
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DoubleStop_DoesNotThrow()
    {
        _metronome!.Start();
        _metronome.Stop();
        _metronome.Stop(); // second Stop should be a no-op
        Assert.IsFalse(_metronome.IsRunning);
    }

    // ── Beat events fire ────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BeatEvent_FiresAtLeastOnce_WithinTimeout()
    {
        _metronome!.MuteAudio = true;
        _metronome.Tempo = 300; // fast so test completes quickly

        var beatFired = new TaskCompletionSource<bool>();
        _metronome.Beat += (_, _) => beatFired.TrySetResult(true);

        _metronome.Start();
        var completed = await Task.WhenAny(beatFired.Task, Task.Delay(1000));
        _metronome.Stop();

        Assert.IsTrue(beatFired.Task.IsCompleted, "Beat event should fire within timeout");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BeatEvent_FirstBeatIsAccent_WhenAccentEvery4()
    {
        _metronome!.MuteAudio = true;
        _metronome.Tempo = 300;
        _metronome.AccentEvery = 4;

        var firstBeat = new TaskCompletionSource<BeatEventArgs>();
        _metronome.Beat += (_, e) => firstBeat.TrySetResult(e);

        _metronome.Start();
        await Task.WhenAny(firstBeat.Task, Task.Delay(1000));
        _metronome.Stop();

        Assert.IsTrue(firstBeat.Task.IsCompleted, "Beat event should fire");
        Assert.AreEqual(0L, firstBeat.Task.Result.TotalBeats, "First beat TotalBeats should be 0");
        Assert.IsTrue(firstBeat.Task.Result.IsAccent, "Beat 0 is accent when AccentEvery=4");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BeatEvent_FiresMultipleBeats_TotalBeatsIncrement()
    {
        _metronome!.MuteAudio = true;
        _metronome.Tempo = 300;
        _metronome.AccentEvery = 4;

        var beatNums = new List<long>();
        var tcs = new TaskCompletionSource<bool>();

        _metronome.Beat += (_, e) =>
        {
            beatNums.Add(e.TotalBeats);
            if (beatNums.Count >= 8)
                tcs.TrySetResult(true);
        };

        _metronome.Start();
        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        _metronome.Stop();

        Assert.IsTrue(beatNums.Count >= 4, $"Expected at least 4 beats, got {beatNums.Count}");
        Assert.AreEqual(0L, beatNums[0], "First TotalBeats should be 0");
        Assert.AreEqual(1L, beatNums[1], "Second TotalBeats should be 1");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BeatEvent_AccentsFireCorrectly_WhenAccentEvery3()
    {
        _metronome!.MuteAudio = true;
        _metronome.Tempo = 300;
        _metronome.AccentEvery = 3;

        var beats = new List<BeatEventArgs>();
        var tcs = new TaskCompletionSource<bool>();

        _metronome.Beat += (_, e) =>
        {
            beats.Add(e);
            if (beats.Count >= 9)
                tcs.TrySetResult(true);
        };

        _metronome.Start();
        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        _metronome.Stop();

        Assert.IsTrue(beats.Count >= 6, $"Expected >=6 beats, got {beats.Count}");
        // beats at TotalBeats 0, 3, 6 should be accents
        foreach (var b in beats)
        {
            bool expectedAccent = b.TotalBeats % 3 == 0;
            Assert.AreEqual(expectedAccent, b.IsAccent,
                $"TotalBeats={b.TotalBeats}: expected IsAccent={expectedAccent}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BeatEvent_AccentEvery0_NeverAccents()
    {
        _metronome!.MuteAudio = true;
        _metronome.Tempo = 300;
        _metronome.AccentEvery = 0;

        var beats = new List<BeatEventArgs>();
        var tcs = new TaskCompletionSource<bool>();

        _metronome.Beat += (_, e) =>
        {
            beats.Add(e);
            if (beats.Count >= 6)
                tcs.TrySetResult(true);
        };

        _metronome.Start();
        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        _metronome.Stop();

        Assert.IsTrue(beats.Count >= 4, $"Expected >=4 beats, got {beats.Count}");
        Assert.IsTrue(beats.TrueForAll(b => !b.IsAccent),
            "AccentEvery=0 means no beat is an accent");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BeatEvent_FreeFormAccentEvery7_AccentsOnMultiplesOf7()
    {
        _metronome!.MuteAudio = true;
        _metronome.Tempo = 300;
        _metronome.AccentEvery = 7;

        var beats = new List<BeatEventArgs>();
        var tcs = new TaskCompletionSource<bool>();

        _metronome.Beat += (_, e) =>
        {
            beats.Add(e);
            if (beats.Count >= 14)
                tcs.TrySetResult(true);
        };

        _metronome.Start();
        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        _metronome.Stop();

        Assert.IsTrue(beats.Count >= 7, $"Expected >=7 beats, got {beats.Count}");
        foreach (var b in beats)
        {
            bool expectedAccent = b.TotalBeats % 7 == 0;
            Assert.AreEqual(expectedAccent, b.IsAccent,
                $"TotalBeats={b.TotalBeats}: expected IsAccent={expectedAccent} for AccentEvery=7");
        }
    }

    // ── Click synthesis ────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void GenerateClick_ProducesNonEmptySamples()
    {
        var accent = MetronomeService.GenerateClick(isAccent: true,  sampleRate: 44100);
        var normal = MetronomeService.GenerateClick(isAccent: false, sampleRate: 44100);
        Assert.IsTrue(accent.Length > 0, "Accent click should have samples");
        Assert.IsTrue(normal.Length > 0, "Normal click should have samples");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GenerateClick_SamplesAreWithinRange()
    {
        var samples = MetronomeService.GenerateClick(isAccent: true, sampleRate: 44100);
        foreach (var s in samples)
            Assert.IsTrue(s >= -1.5f && s <= 1.5f, $"Sample {s} out of expected range");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GenerateClick_AccentAndNormalAreDifferent()
    {
        var accent = MetronomeService.GenerateClick(isAccent: true,  sampleRate: 44100);
        var normal = MetronomeService.GenerateClick(isAccent: false, sampleRate: 44100);
        // They should differ in at least some samples (different pitch/volume)
        bool differs = false;
        for (int i = 0; i < Math.Min(accent.Length, normal.Length); i++)
            if (Math.Abs(accent[i] - normal[i]) > 0.001f) { differs = true; break; }
        Assert.IsTrue(differs, "Accent and normal clicks should sound different");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ClickToWav_ProducesValidRiffHeader()
    {
        var samples = MetronomeService.GenerateClick(isAccent: true, sampleRate: 44100);
        var wav     = MetronomeService.ClickToWav(samples, sampleRate: 44100);
        Assert.IsTrue(wav.Length > 44, "WAV should have header + data");
        Assert.AreEqual((byte)'R', wav[0]);
        Assert.AreEqual((byte)'I', wav[1]);
        Assert.AreEqual((byte)'F', wav[2]);
        Assert.AreEqual((byte)'F', wav[3]);
        Assert.AreEqual((byte)'W', wav[8]);
        Assert.AreEqual((byte)'A', wav[9]);
        Assert.AreEqual((byte)'V', wav[10]);
        Assert.AreEqual((byte)'E', wav[11]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ClickToWav_EmptySamples_ProducesHeaderOnly()
    {
        var wav = MetronomeService.ClickToWav([], sampleRate: 44100);
        Assert.AreEqual(44, wav.Length, "Empty samples → 44-byte header only");
    }

    // ── MetronomeSettings ─────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void MetronomeSettings_DefaultValues_AreReasonable()
    {
        var s = new MetronomeSettings();
        Assert.AreEqual(120, s.Tempo);
        Assert.AreEqual(4, s.AccentEvery);
        Assert.IsFalse(s.MuteAudio);
        Assert.AreEqual(-1, s.WindowLeft);
        Assert.AreEqual(-1, s.WindowTop);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AppSettings_HasMetronomeSettings_Property()
    {
        var s = new AppSettings();
        Assert.IsNotNull(s.Metronome);
        Assert.AreEqual(120, s.Metronome.Tempo);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AppSettings_MetronomeSettings_PersistedViaLocalSettings()
    {
        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"MetronomeTest_{Guid.NewGuid():N}.json");

        try
        {
            AppSettings.ResetForTesting(tempPath);
            var settings = AppSettings.Instance;
            settings.Metronome.Tempo = 90;
            settings.Metronome.AccentEvery = 3;
            settings.Metronome.MuteAudio = true;
            settings.SaveLocal();

            // Reload
            AppSettings.ResetForTesting(tempPath);
            var loaded = AppSettings.Instance;
            Assert.AreEqual(90, loaded.Metronome.Tempo);
            Assert.AreEqual(3, loaded.Metronome.AccentEvery);
            Assert.IsTrue(loaded.Metronome.MuteAudio);
        }
        finally
        {
            AppSettings.ResetForTesting();
            try { System.IO.File.Delete(tempPath); } catch { }
        }
    }

    // ── MuteAudio prevents events but doesn't break ─────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MuteAudio_True_BeatEventStillFires()
    {
        // Audio is muted but beat events should still fire
        _metronome!.MuteAudio = true;
        _metronome.Tempo = 300;

        var beatFired = new TaskCompletionSource<bool>();
        _metronome.Beat += (_, _) => beatFired.TrySetResult(true);

        _metronome.Start();
        await Task.WhenAny(beatFired.Task, Task.Delay(1000));
        _metronome.Stop();

        Assert.IsTrue(beatFired.Task.IsCompleted, "Beat event fires even when audio is muted");
    }

    // ── Dispose safety ─────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void Dispose_WhileRunning_DoesNotThrow()
    {
        _metronome!.MuteAudio = true;
        _metronome.Start();
        _metronome.Dispose(); // Should not throw
        _metronome = null;    // Prevent double-dispose in cleanup
    }
}
