# RMS on macOS and Linux

The macOS/Linux app shares the Windows project's model, effects, project folder format, autosave, take recovery, mixer, and export renderer. Avalonia draws the desktop window; PortAudio handles microphone and speaker streams. The Windows WPF/WASAPI app remains available.

## Build and run

Install the .NET 8 and .NET 10 SDKs. Install `ffmpeg` if you want to import WAV/MP3 or export MP3. On macOS, `brew install ffmpeg`; on Ubuntu, install the `ffmpeg` package. RMS itself uses no network connection.

```bash
dotnet test tests/RyanMusicStudio.Tests/RyanMusicStudio.Tests.csproj -f net8.0 -c Release
scripts/build-desktop.sh
```

On an Apple Silicon Mac, open `dist/RMS-osx-arm64.app`. The ZIP next to it preserves the app bundle for sharing. On an Intel Mac, the script makes `RMS-osx-x64.app`. The local build is ad hoc signed so macOS can identify it for microphone permission; a public download needs a Developer ID signature and notarization before distribution.

On Linux, unpack `dist/RMS-linux-x64.tar.gz` or `dist/RMS-linux-arm64.tar.gz` and run the `RMS` binary in the extracted folder. PortAudio needs the machine's audio system and Avalonia needs a graphical desktop. The package includes .NET and the native audio/UI libraries.

The bundled Linux PortAudio library also links to ALSA and JACK. On Ubuntu 24.04, install these runtime dependencies with `sudo apt-get install libasound2t64 libjack-jackd2-0` (no JACK server setup is needed). CI installs these before running the recording-recovery tests.

To cross-publish another architecture, set `RID`, for example `RID=linux-arm64 scripts/build-desktop.sh`. A cross-published package still needs to be run and audio-tested on its target OS.

## First song

1. In Audio Setup, choose microphone and headphones. Test the tone and microphone meter. A wired output gives the best recording alignment.
2. On Home, name the song, set its tempo, and choose the parent folder. RMS creates a new project folder without overwriting another song.
3. On Record, import a WAV or MP3 backing track, arm a vocal or guitar track, and record takes. Finish a take with the same Record button or Stop.
4. Select a take to audition it. Enter a start and end time, then choose the marked part. Additional choices replace overlapping parts while keeping the rest of the comp.
5. On Mix, set gain, pan, mute/solo, or a vocal preset. On Export, choose WAV or MP3 and the whole mix, marked range, or stems.

Transport buttons stay available on every page. Save As copies the song and its audio into an empty folder. Export shows progress and can be cancelled; existing exports are replaced only after successful completion. An export uses the mix as it was when you started it, so later edits do not change that file. On macOS, Command+S saves and Command+Shift+Z redoes an edit.

The portable window uses start/end fields for range and comp selection instead of the Windows timeline's drag controls. The core comp and export behavior is shared. PortAudio does not expose RMS's Windows-only WASAPI Exclusive switch. Recording offset and buffer settings remain in the project/settings; loop and punch controls are available on Record.

## Platform notes

- macOS asks for microphone access when Audio Setup opens the input. A denied permission leaves playback and editing available; grant access in macOS privacy settings to record.
- Linux device names and latency depend on ALSA/PulseAudio/PipeWire configuration. Choose wired headphones and use the test take to check timing.
- FFmpeg runs as a local process only during file conversion; live recording and playback use PortAudio. It is not bundled because distributions use different FFmpeg builds and licenses.
- Existing RMS project folders can be moved between operating systems. Keep each folder's `project.json` and `media/` directory together.
- The test suite validates portable model, recording recovery, and DSP behavior. A macOS device/WAV/import/export round trip was run during development. Linux packages are cross-built and need a Linux hardware check before a public release.
