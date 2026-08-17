# Third-Party Native Dependencies

This folder contains native binaries and soundfonts used by the optional
FluidSynth MIDI playback backend in `MxlMidiPlayer`.

## How it fits together

```
MxlMidiPlayer
  └─ FluidSynthMidiBackend (NFluidsynth NuGet — MIT)
	   └─ libfluidsynth-3.dll  ← this folder
			└─ SDL3.dll        ← this folder (audio I/O)
			└─ sndfile.dll     ← this folder (audio file support)
	   └─ *.sf2 soundfont      ← Soundfonts\ sub-folder
```

The default winmm backend requires **none** of these files.

---

## FluidSynth  (`FluidSynth\win-x64\`)

| File | Purpose |
|---|---|
| `libfluidsynth-3.dll` | Core synthesis engine |
| `SDL3.dll` | Audio output (DirectSound/WASAPI) |
| `sndfile.dll` | Audio file I/O (used by FluidSynth internals) |

**Version:** 2.5.6 (cpp11 self-contained build — no GLib required)  
**Source:** https://github.com/FluidSynth/fluidsynth/releases/tag/v2.5.6  
**License:** GNU Lesser General Public License v2.1 (LGPL-2.1)

### LGPL-2.1 compliance for distribution

Because FluidSynth is LGPL and is dynamically linked (DLL, not statically compiled
into your app), you **may** distribute it with a proprietary/commercial application
provided you:

1. Include the LGPL-2.1 license text (see `FluidSynth\LICENSE`).
2. Allow end-users to re-link against a modified version of FluidSynth
   (satisfied by shipping as separate DLLs rather than linking statically).
3. Display a credit such as:

   > "This application uses FluidSynth (https://www.fluidsynth.org/),
   >  licensed under the GNU Lesser General Public License v2.1."

**SDL3** is licensed under the zlib/libpng license (permissive, no special requirements).  
**sndfile** is licensed under LGPL-2.1 (same requirements as FluidSynth above).

Full license texts:
- FluidSynth / sndfile: https://www.gnu.org/licenses/lgpl-2.1.html
- SDL3: https://www.libsdl.org/license.php

---

## Soundfonts  (`Soundfonts\`)

Both soundfonts are auto-copied to `Soundfonts\` in the build output by MSBuild.
`MxlMidiPlayer.SoundfontPath` defaults to `YDP-GrandPiano.sf2`.

### YDP-GrandPiano  (`YDP-GrandPiano.sf2`, 113 MB) ← **default**

A high-quality, realistic acoustic grand piano soundfont.

**Author:** Freepats Project  
**License:** Creative Commons Attribution 3.0 Unported (CC BY 3.0)  
**Source:** https://freepats.zenvoid.org/Piano/acoustic-grand-piano.html

**Required attribution when distributing:**

> "YDP-GrandPiano soundfont by the Freepats Project,
>  licensed under CC BY 3.0 (https://creativecommons.org/licenses/by/3.0/)."

### VintageDreamsWaves v2  (`VintageDreamsWaves.sf2`, 0.3 MB)

A tiny General MIDI soundfont — useful as a fast-loading fallback or for CI.
Not a realistic piano; covers all 128 GM patches.

**Author:** Ian Wilson  
**License:** Creative Commons Attribution 4.0 International (CC BY 4.0)  
**Source:** https://github.com/FluidSynth/fluidsynth (bundled test asset)

**Required attribution when distributing:**

> "VintageDreamsWaves soundfont by Ian Wilson,
>  licensed under CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/)."

---

## Optional upgrade: GeneralUser GS (~30 MB, GM coverage + better piano)

If 113 MB is too large to ship, GeneralUser GS is a good middle ground.

1. Download from: http://www.schristiancollins.com/generaluser.php
2. Place the `.sf2` in this `Soundfonts\` folder and rebuild.
3. Change `SoundfontPath` in `MxlMidiPlayer` (or expose it in settings UI).

**License:** GeneralUser GS License v2.0 — free for any use including commercial.  
**Recommended attribution:** "GeneralUser GS soundfont by S. Christian Collins."

---

## Production distribution checklist

When shipping the FluidSynth backend in a production build:

- [ ] Copy `FluidSynth\win-x64\*.dll` to your app's output folder
	  (or add them to the installer).
- [ ] Include LGPL-2.1 license text for FluidSynth and sndfile.
- [ ] Include SDL3 license text (zlib).
- [ ] Place your chosen `.sf2` soundfont in a known location and expose
	  its path via a user setting (so users can substitute their own).
- [ ] Add attribution text to your About screen / Help file.

### MSBuild: auto-copy DLLs to output

Add this to `AvaloniaTests.csproj` (or your production `.csproj`):

```xml
<ItemGroup>
  <!-- Copy FluidSynth native DLLs to the output directory -->
  <Content Include="$(MSBuildThisFileDirectory)..\ThirdParty\FluidSynth\win-x64\*.dll">
	<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
	<Link>%(Filename)%(Extension)</Link>
  </Content>
</ItemGroup>
```
