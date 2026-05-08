namespace SheetMusicLib;

/// <summary>The synthesized beat sound used by the metronome.</summary>
public enum BeatSound
{
    Woodblock = 0,  // percussive noise + two sine partials (default)
    Sine      = 1,  // pure sine tone with fade-out
    Rimshot   = 2,  // very short high snappy burst
    Hihat     = 3,  // filtered white noise, metallic
    Beep      = 4,  // square-wave blip
}

/// <summary>
/// Persistent metronome settings stored in local (machine-specific) settings.
/// </summary>
public class MetronomeSettings
{
    /// <summary>Beats per minute (20-300).</summary>
    public int Tempo { get; set; } = 120;

    /// <summary>
    /// Accent every N beats.  0 means no accent (all clicks identical).
    /// 4 means accent on beat 1, 5, 9 ... (every 4th beat).
    /// </summary>
    public int AccentEvery { get; set; } = 4;

    /// <summary>Whether audio beat is muted.</summary>
    public bool MuteAudio { get; set; } = false;

    /// <summary>The synthesized beat sound to use.</summary>
    public BeatSound Sound { get; set; } = BeatSound.Woodblock;

    /// <summary>Overlay window left position (-1 = use default).</summary>
    public double WindowLeft { get; set; } = -1;

    /// <summary>Overlay window top position (-1 = use default).</summary>
    public double WindowTop { get; set; } = -1;
}
