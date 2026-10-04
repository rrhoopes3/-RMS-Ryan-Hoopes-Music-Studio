# RMS

**Ryan Music Studio** is a Windows app for someone who wants to make a song without learning a studio program. One window. Press Play. Tap the drum boxes. Tap a piano key, then a melody box. Turn the volume up or down. Save. The beat and the melody are real audio.

RMS runs on **Windows 11**, works **offline**, and needs **no account**. The song stays in your Music folder unless you copy it yourself.

## Open it

If you already have **RMS.exe**, double-click it. No installer.

The first time, RMS makes a song called **My Song** in your Music folder, under `Music\RMS\My Song`. Press **Save** any time. **Open** brings a saved song back.

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

## What the window does

- **Play** and **Stop**
- **Tempo**
- Where you are in the song (step and time)
- **Drums**: kick, snare, and hat. Tap a box to turn that hit on. You hear it.
- **Melody**: eight piano keys. Tap a key, then tap a box in the row under it.
- **Volume** changes how loud the song is
- **Save** and **Open**
- **Record my voice** if a microphone is plugged in. That uses the same song file. It is not a separate screen.

If the song has no drums and no melody, RMS says so. If a song folder cannot be opened, or speakers are missing, the message says what to do next.

## Keyboard

Space plays or stops. Ctrl+S saves.

## What this version does not do

MIDI, plugins, pitch correction, a mixer full of knobs, and a separate export page are not in this window. Older recording tools are still in the project for the voice button. They are not separate screens.

## License and credits

- RMS application source: [MIT](LICENSE) © 2026 Ryan Hoopes
- Third-party notices: [NOTICE](NOTICE) and [docs/THIRD_PARTY_LICENSES.md](docs/THIRD_PARTY_LICENSES.md)

RMS is a personal studio tool. It is not affiliated with any other music program.
