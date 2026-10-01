# ADR-0001 — RMS (Ryan Music Studio) architecture (release 1)

**Status:** Accepted  
**Date:** 2026-09-30  
**Decision owners:** Implementation for the local Windows 11 desktop app in this workspace.

## Context

Release 1 must let a novice singer pick a Windows microphone, drop in a backing track, record aligned vocal takes, assemble a comp, add built-in vocal effects, save a portable project, and export a stereo song. Recording and crash recovery are more important than a plugin marketplace. The core path must work offline with no account and no network.

## Decision

Ship a **native Windows desktop app** on **.NET 8 + WPF + NAudio (WASAPI)**.

| Layer | Project | Responsibility |
| --- | --- | --- |
| UI | `RyanMusicStudio.App` | Five places only: Home, Audio Setup, Arrange/Record, Mix, Export |
| Audio engine | `RyanMusicStudio.Engine` | Device I/O, mix callback, incremental record, import/export, waveforms |
| Project model | `RyanMusicStudio.Core` | Sample-frame timeline, tracks/takes/comps, edits, undo, DSP algorithms |
| Persistence | `RyanMusicStudio.Core` | Versioned manifest, atomic writes, autosave, recovery |
| Hardware spike | `RyanMusicStudio.Spike` | Console proof of mic + backing + monitor + aligned overdub |

This stack was chosen over Tauri/Rust and Electron because:

1. This machine already has the **.NET 8 SDK** and working WASAPI devices (Realtek, Sound Blaster Z, Arctis).
2. **NAudio WASAPI** is a proven Windows capture/render path. The record callback only copies into a pre-allocated ring buffer.
3. WPF can ship a **self-contained `RyanMusicStudio.exe`** without a browser record path.
4. Media Foundation (inbox on Windows 10/11) decodes/encodes MP3 without vendoring LAME or GPL into the binary.

Electron/Web Audio is rejected as the primary record path: latency and disk-finalize behavior are harder to guarantee.

## Windows drivers

| Path | Release 1 | Notes |
| --- | --- | --- |
| **WASAPI Shared** | Required default | Works with a standard Windows mic (Realtek, USB headsets, console mics). |
| **WASAPI Exclusive** | Supported option | Lower-latency path without a third-party driver SDK. |
| **ASIO** | Not shipped | Steinberg ASIO SDK terms are a distribution risk. Interfaces still work through their WASAPI endpoints. Low-latency path for R1 is **WASAPI Exclusive**. |

If an input or output device disappears, the engine **stops transport and finalizes the take**. It does not silently fail over to another device.

Latency compensation = reported WASAPI input latency + output latency + a user recording offset (sample frames). A loopback calibration procedure is documented in `docs/LOOPBACK-CALIBRATION.md`.

Bluetooth / high-latency outputs: warn when the endpoint name looks like Bluetooth/wireless or reported output latency exceeds 80 ms.

## Project folder format

A project is a **portable directory** (not a single opaque file):

```
My Song/
  project.json              # versioned manifest (formatVersion = 1)
  project.json.bak          # last successful replace
  autosave/
    project.json            # latest valid autosave
  media/
    originals/              # untouched imports (copy of user files)
    working/                # PCM WAV at project sample rate
    takes/                  # recorded takes (never rewritten by edits)
  cache/
    waveforms/              # disposable peak files
  recovery/                 # crash markers, in-progress take sidecars
```

Rules:

- Timeline positions are **int64 sample frames** at the project sample rate (44_100 or 48_000).
- Edits change clip/comp metadata only. Take WAV files are append-only then immutable.
- Deleting `cache/` must not break reopen or export.
- `project.json` is written to a temp file, flushed, then replaced with a `.bak` of the previous valid file.

## Real-time constraints

The WASAPI render/capture callbacks may not allocate, touch the filesystem, use the network, or wait on the UI thread.

| Work | Thread |
| --- | --- |
| Mix, meters, software monitor, metronome (playback only) | Audio callback, pre-allocated buffers |
| Incremental WAV write | Dedicated writer thread + ring buffer |
| Waveforms, import resample, autosave, export | Thread-pool / UI-idle |

On Record Stop: the writer flushes and patches the WAV header **before** the UI shows success.

## Codecs and licensing

| Component | License / terms | Use |
| --- | --- | --- |
| NAudio 2.2.1 | MIT | WASAPI, WAV, Media Foundation interop |
| .NET 8 / WPF | Microsoft terms | App runtime (self-contained publish) |
| Windows Media Foundation | OS component, not redistributed | MP3 decode + MP3 encode |
| Built-in DSP | Original, MIT with the app source | HPF, EQ, compressor, de-esser, reverb, delay, gate, limiter |
| Sample media | Original synthesized audio (public domain dedication in the sample folder) | Demo project |

No VST3 SDK, no GPL encoder statically linked, no telemetry, no accounts.

## Packaging

- **Portable package:** `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true` → `dist/RMS-portable/RMS.exe`
- **Optional install helper:** `scripts/Install-RMS.ps1` copies the portable build to `%LOCALAPPDATA%\RMS` and creates a Start Menu shortcut named RMS.
- Reproducible build: `scripts/build-windows.ps1`

## Extension points (not shipped as UI)

Interfaces reserved for later releases, with **no dead buttons** in R1:

- `IClip` / track `Role` enum includes future `Midi` without exposing a MIDI editor.
- `IAudioEffect` can later wrap a VST3 host; R1 only hosts built-in effects.
- Export renderer is offline and mix-identical, so a future mastering page can reuse it.
- Import copies bytes first so a later, rights-cleared external source can reuse the same media catalog. No third-party marketplace or brand content is included.

## Consequences

- Best-in-class plugin scanning and macOS are out of scope.
- Lowest latency on some interfaces will be better with a future, license-cleared ASIO option; R1 documents WASAPI Exclusive instead of pretending ASIO works.
- MP3 quality/availability depends on the Windows Media Foundation encoder present on the OS (standard on Windows 11).
