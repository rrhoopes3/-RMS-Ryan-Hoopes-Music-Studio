# Implementation plan — Ryan Music Studio release 1

## Stack

- **UI:** .NET 8 WPF (five places, novice copy, large transport)
- **Audio:** NAudio WASAPI Shared + Exclusive (`RyanMusicStudio.Engine`)
- **Model / DSP / save:** `RyanMusicStudio.Core` (sample frames, atomic JSON)
- **Package:** self-contained win-x64 single-file exe + install helper

## Milestone order

1. Architecture record (this folder) — done before engine/UI.
2. Device + recording spike (`RyanMusicStudio.Spike`) proving mic, backing playback, software monitor, and latency-offset overdub.
3. Vertical slices in one product:
   - Project create / import / portable folder
   - Record takes (count-in, pre-roll, loop, punch) + safe stop
   - Edit / take lanes / comp
   - Effects + mixer + master limiter
   - Export WAV/MP3 + autosave/recovery
4. Sample project, automated tests, Windows package, manual audio log.

## Key dependencies

| Package | Why | License |
| --- | --- | --- |
| NAudio | WASAPI devices, WAV I/O, Media Foundation MP3 | MIT |
| xunit + Microsoft.NET.Test.Sdk | Persistence, timeline, edit tests | Apache-2.0 / MIT |

No cloud SDKs. No MCP servers. No secrets in repo.

## Deferred if they threaten recording

- ASIO (license/distribution)
- Vocal tune, MIDI, VST3, stem ML, and any third-party marketplace or brand-amp integrations
- Time-stretch ships only as a conservative WSOLA for small ratios; if quality is poor it stays labeled experimental in the manual log
