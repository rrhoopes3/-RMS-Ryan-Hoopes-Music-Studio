# RMS

**Ryan Music Studio** — a Windows app for a singer who wants to record vocals over a backing track and export a song, without learning a big studio program.

RMS runs on **Windows 11**, works **offline**, and needs **no account**. Your audio stays on this computer unless you export a file yourself.

## First session

1. Open **Audio Setup**. Pick your microphone and headphones. Wear headphones so the beat does not leak into the mic.
2. Play the test tone, then record a short test and listen back.
3. Start a **New Vocal Session**. Drag in a WAV or MP3 backing track.
4. Press **Record another take**. Sing. Do it again until you like a pass.
5. **Choose best parts** from take lanes, tidy fades, then open **Mix** for Clean / Warm / Spacious.
6. **Export** a stereo WAV or MP3.

## Download and run

After a release build (see below), the portable app is:

`dist/RMS-portable/RMS.exe`

Double-click `RMS.exe`. No installer is required. Optional helper:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Install-RMS.ps1
```

That copies the portable build to `%LOCALAPPDATA%\RMS` and adds a Start Menu shortcut named **RMS**.

## Build from source

Requirements: Windows 11 64-bit, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-windows.ps1
```

This restores packages, runs tests, writes the public-domain sample project, and publishes `RMS.exe` to `dist/RMS-portable\`.

Or step by step:

```powershell
dotnet test RyanMusicStudio.sln -c Release
dotnet publish src\RyanMusicStudio.App\RyanMusicStudio.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\RMS-portable
```

WASAPI hardware spike (lists devices; optional live mic test):

```powershell
dotnet run --project src\RyanMusicStudio.Spike\RyanMusicStudio.Spike.csproj -- --list-only
```

## Keyboard

| Key | Action |
| --- | --- |
| Space | Play / stop |
| R | Record another take |
| Ctrl+S | Save |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| S | Split at playhead |
| Delete | Delete selected clip |
| Home | Go to start |
| L | Loop region |
| Ctrl++ / Ctrl+- | Zoom |

Shortcuts also appear in the menus.

## What release 1 does

- WASAPI microphone and headphone setup, live meters, software monitoring, test take
- Vocal-over-beat projects, drag-in WAV/MP3, take lanes, comps, non-destructive edits
- Built-in vocal effects (HPF, EQ, compressor, de-esser, reverb, delay, gate) and a master limiter
- Save / autosave / crash recovery, export WAV 16/24 and MP3 (Windows Media Foundation)

Low-latency path: **WASAPI Exclusive**. ASIO is not shipped.

## What release 1 does not do

MIDI, VST3 plugins, automatic pitch correction, a mastering page, stem-separation AI, and any marketplace or brand-amp integrations are **not included**. Those names in the internal spec are a future wishlist only. RMS does not ship other companies' sounds, logos, or model names.

## License and credits

- RMS application source: [MIT](LICENSE) © 2026 Ryan Hoopes
- Third-party notices: [NOTICE](NOTICE) and [docs/THIRD_PARTY_LICENSES.md](docs/THIRD_PARTY_LICENSES.md)
- Sample beat: original synthesized audio, public-domain dedication (see `samples/vocal-over-beat/MEDIA-LICENSE.txt` after you build)

RMS is a personal studio tool. It is not affiliated with any other DAW or music brand.
