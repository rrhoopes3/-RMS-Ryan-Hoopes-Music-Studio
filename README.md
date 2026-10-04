# RMS

**Ryan Music Studio** is a Windows app for making a song in one window. Build a beat, import audio, record voice or instruments on separate tracks, mix them, and export a WAV or MP3.

RMS runs on **Windows 11**, works **offline**, and needs **no account**. The song stays in your Music folder unless you copy it yourself.

## Open it

If you already have **RMS.exe**, double-click it. No installer.

The first time, RMS makes a song called **My Song** in your Music folder, under `Music\RMS\My Song`. Use **New song** to choose a name, folder, tempo, meter, and sample rate. **Save** keeps the current song; **Save as** makes a copy in an empty folder. **Open** brings a saved song back.

## Build on Windows

You need Windows 11 64-bit and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-windows.ps1
```

That publishes a folder you can hand to someone else. Double-click:

`dist\RMS-portable\RMS.exe`

Optional shortcut after that build:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Install-RMS.ps1
```

That copies the app to `%LOCALAPPDATA%\RMS` and adds a Start Menu shortcut named **RMS**.

Or, without the script:

```powershell
dotnet test RyanMusicStudio.sln -c Release
dotnet publish src\RyanMusicStudio.App\RyanMusicStudio.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist\RMS-portable
```

## Make a song

1. Choose your microphone and speakers. The input meter moves while recording, and the output meter moves during playback; red means the signal is too hot.
2. Expand **Drums & melody** to tap a kick, snare, hat, or piano step. These make the built-in beat. The 16 boxes fill one bar in the chosen meter.
3. Add a **Voice**, **Guitar**, or **Other** track, or import a WAV/MP3. Imported files get their own lane and are copied into the song folder without changing the originals. You can also drop audio files onto the window.
4. Click a lane, then **Arm selected** to choose where the next recording goes. Press **Record** and **Stop**. Earlier takes remain in their own lanes; the waveform grows as the new take is saved. The built-in beat repeats under longer recordings while the timeline advances.
5. Use the mixer beside the timeline to set track gain and pan, mute or solo tracks, and choose a vocal preset. Click the ruler to seek; drag on it to mark a range. You can move an imported clip, split or delete a selection, undo/redo, and zoom. **Choose takes** lets you drag across recorded passes to keep the best parts.
6. Choose a WAV or MP3 format and **Export audio**. Export the full mix, a marked range, or a vocal/backing stem. The click track and reference tracks stay out of the file.

If a song cannot open or an audio device disappears, RMS shows a recovery message. Finished takes remain in the song folder if saving is interrupted.

## Keyboard

Space plays or stops, **R** starts or stops recording, **Ctrl+S** saves, **Ctrl+Z** undoes, and **Ctrl+Y** redoes. In the timeline, the wheel scrolls tracks, Shift+wheel scrolls time, and Ctrl+wheel zooms.

## Current limits

The built-in beat remains a 16-step pattern with eight melody keys. MIDI instruments, plugins, pitch correction, detailed mastering, and cloud collaboration are later work. The [studio shortlist plan](docs/STUDIO-SHORTLIST-PLAN.md) records this pass and its acceptance checks.

## License and credits

- RMS application source: [MIT](LICENSE) © 2026 Ryan Hoopes
- Third-party notices: [NOTICE](NOTICE) and [docs/THIRD_PARTY_LICENSES.md](docs/THIRD_PARTY_LICENSES.md)

RMS is a personal studio tool. It is not affiliated with any other music program.
