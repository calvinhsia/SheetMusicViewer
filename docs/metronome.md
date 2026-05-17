# Metronome

SheetMusicViewer includes a built-in, drift-corrected metronome that works on Windows, macOS, and Linux.

## Using the Metronome

1. Open a PDF in SheetMusicViewer
2. Click **Menu → Metronome** (or the metronome toolbar button)
3. The **Metronome Overlay** window appears — it floats above the score so you can read music while it ticks
4. Set tempo (BPM), beats per measure, and click sound
5. Press **Start / Stop**

The overlay window is always-on-top and can be repositioned freely.

## Controls

| Control | Description |
|---|---|
| BPM spinner | Tempo in beats per minute (20–300) |
| Accent Every | Accent on every N-th beat (`0` = no accent) |
| Sound | `Woodblock`, `Click`, or `Beep` |
| Mute | Suppress audio; visual flash only |
| Start / Stop | Toggle metronome |
| Tap Tempo | Tap repeatedly to set BPM from your natural feel |

Settings are persisted via `MetronomeSettings` and restored on next launch.

## Implementation — `MetronomeService`

`MetronomeService` is the cross-platform engine in `SheetMusicViewer.Desktop\MetronomeService.cs`.

### Drift Correction

A `System.Threading.Timer` fires each beat. To prevent accumulated drift (the timer fires slightly late on each beat, adding up over time), each callback computes the *exact* time the next beat should fire relative to a fixed start epoch:

```
nextDelayMs = startEpochMs + (nextBeatNumber × beatIntervalMs) − nowMs
```

This keeps the metronome locked to wall-clock time regardless of system load.

### Tempo Change Without Glitches

When the user changes BPM while the metronome is running, the epoch is re-anchored to `now` so the next beat fires at the new interval from the current moment — avoiding a silent gap or double-fire.

### Audio

| Platform | Engine |
|---|---|
| Windows | [NAudio](https://github.com/naudio/NAudio) `WaveOutEvent` → current default audio device (including Bluetooth) |
| macOS | Shell out to `afplay` with a temporary WAV file |
| Linux | Shell out to `aplay` with a temporary WAV file |

Beat sounds are pre-rendered as PCM samples at 44 100 Hz for low latency. Two sets of samples are maintained: accent and normal.

### `BeatSound` Enum

```csharp
public enum BeatSound { Woodblock, Click, Beep }
```

### `BeatEventArgs`

```csharp
public class BeatEventArgs : EventArgs
{
	public long TotalBeats { get; init; }  // 0-based running count
	public bool IsAccent   { get; init; }  // true on accent beats
}
```

### Lifecycle

```csharp
var metro = new MetronomeService();
metro.Tempo      = 120;
metro.AccentEvery = 4;
metro.Sound      = BeatSound.Woodblock;
metro.Beat      += (s, e) => Console.WriteLine(e.IsAccent ? "TICK" : "tick");

metro.Start();
// ... later ...
metro.Stop();
metro.Dispose();
```

`MetronomeService` implements `IDisposable`. Always dispose it when the owning window closes to release the audio device and background timer.

## `MetronomeSettings` (persistence)

`MetronomeSettings` in `SheetMusicLib` holds the persisted configuration:

| Property | Type | Default |
|---|---|---|
| `Tempo` | int | `120` |
| `AccentEvery` | int | `4` |
| `Sound` | `BeatSound` | `Woodblock` |
| `MuteAudio` | bool | `false` |

Settings are serialized as part of `AppSettings` (JSON) in the user's app-data folder.
