# RMS — Ryan Music Studio product and build specification

**Status:** Implementation handoff, version 0.1  
**Platform:** Windows 11, 64-bit desktop  
**Primary user:** A singer with a microphone and headphones who wants to record over backing music and finish songs without learning a large DAW.

**Public brand:** **RMS** (expands to Ryan Music Studio). Not affiliated with any other DAW or music brand.  
**Source license:** MIT, as provided in `LICENSE`. This covers original project code and documentation, not third-party plugins, sample audio, models, brand names, or service content. Audit and document each bundled dependency and asset before distribution.

The later-roadmap table names possible integrations only. Those products and brand-amp suites are **not included** and must not ship without rights.

## 1. Goal and release boundary

The first release must make this journey reliable:

1. Select a microphone and headphone output.
2. Drag in an instrumental or backing track.
3. Record several vocal takes while hearing the track.
4. Combine the best parts, clean up timing, and add vocal effects.
5. Save the session and export a shareable stereo song.

The supplied advanced feature list is the long-term product target, not the acceptance bar for the first release. Prioritize dependable recording, predictable monitoring, and recoverable sessions. No account or network connection is required for the core journey.

### Assumptions to implement against

- One local user per installation; no collaboration or cloud sync in the first release.
- The mic may be USB or connected through an audio interface. The app must work with a standard Windows audio device and expose installed low-latency drivers where supported.
- Headphones are recommended for overdubbing. The setup screen should explain how to avoid the backing track bleeding into the mic.
- Projects use local storage and keep source audio intact.

## 2. First-release functional requirements

`MUST` items are required for release 1. `SHOULD` items can follow after the recording path works end to end.

### 2.1 Audio setup and monitoring

- **MUST:** Enumerate available input and output devices, show their channels, and let the user choose a mic input and playback output.
- **MUST:** Support WASAPI so a standard Windows mic works without an extra driver. Support installed ASIO drivers for interfaces if the selected implementation and distribution terms permit it; otherwise document the supported low-latency path before release.
- **MUST:** Show a live input meter, input gain/clipping indicator, output test, sample rate, and buffer setting where the driver exposes them.
- **MUST:** Offer software monitoring on/off and explain direct monitoring when the interface provides it, so the user can avoid hearing two copies of their voice.
- **MUST:** Record a short test take and play it back before entering a project.
- **MUST:** If an input or output device disconnects during recording, stop safely, keep audio already written, and show a clear recovery path. Do not silently change devices.
- **MUST:** Preserve correct recorded-take placement relative to the backing track using reported device latency and a user-adjustable recording offset. Include a loopback calibration procedure in testing.
- **SHOULD:** Warn when a selected output has latency unsuitable for live monitoring, such as typical Bluetooth headphone paths.

### 2.2 Projects and tracks

- **MUST:** New-project dialog with project name, folder, tempo, time signature, and 44.1 or 48 kHz sample rate. Default to a "Vocal over Beat" template with one backing track and one armed vocal track.
- **MUST:** Audio tracks have no artificial count limit; actual capacity depends on hardware. Support mono and stereo tracks, mute, solo, arm, gain, pan, and color/name.
- **MUST:** Drag and drop WAV and MP3 files onto an empty project or existing track. Preserve the source; copy imported media into the project for portability.
- **MUST:** Convert imported audio to the project's sample rate for playback and editing while preserving the untouched original file in the project.
- **MUST:** Timeline has a ruler, waveforms, zoom, snapping, loop region, playhead, markers, and a metronome that is excluded from export.
- **MUST:** Save, Save As, and reopen a portable project folder. A project must reopen with media, edits, levels, and effects in the same state.
- **SHOULD:** Add lyrics or notes per section, visible during recording.

### 2.3 Recording and editing

- **MUST:** Record a vocal while playing other tracks. Support count-in, pre-roll, loop recording, and punch in/out.
- **MUST:** Keep each recording pass as a take lane. Let the user audition takes and assemble one comp from selected regions.
- **MUST:** Non-destructive split, trim, move, slip, fade in/out, crossfade, delete, and undo/redo. Editing may not rewrite the original take files.
- **MUST:** Show recording status, elapsed time, armed input, and clipping clearly. On stop, commit the take to disk before indicating success.
- **MUST:** Keyboard controls for play/stop, record, save, undo/redo, zoom, and delete; provide visible shortcuts in menus or tooltips.
- **SHOULD:** Basic time stretch for small vocal timing fixes, with an audible quality check before release.

### 2.4 Vocal processing and mix

- **MUST:** Per-track effect chain with bypass and reorder. Include high-pass filter, parametric EQ, compressor, de-esser, reverb, delay, and noise gate or expander.
- **MUST:** Provide at least three understandable vocal starting presets (for example Clean, Warm, and Spacious) whose individual controls remain editable.
- **MUST:** Mixer with track faders, pan, meters, mute/solo, effect access, and a stereo master bus with a limiter.
- **MUST:** Effects and level changes remain editable after save/reopen; audio processing must not modify source recordings.
- **SHOULD:** A/B comparison against an imported reference track that is excluded from export.

### 2.5 Export and recovery

- **MUST:** Export the selected song range or whole project to stereo WAV (16- or 24-bit PCM) and MP3. Let the user choose destination and filename.
- **MUST:** Render the same track, effect, mute/solo, and master settings heard in playback, excluding click and reference tracks.
- **MUST:** Autosave project state at least every 30 seconds when changed and after recording stops. Use atomic project-state writes; never replace the last valid state with a partial file.
- **MUST:** On restart after a forced exit, offer recovery of the latest valid autosave and completed audio takes.
- **SHOULD:** Export a clean vocal stem and backing stem separately.

## 3. UX layout

Keep the first release to five clear places:

1. **Home:** New Vocal Session, Open Project, Recent Projects, Audio Setup.
2. **Audio Setup:** Device selection, mic meter, monitoring choice, test recording/playback.
3. **Arrange/Record:** Tracks, take lanes, waveforms, markers, transport, loop/punch controls, optional lyrics panel.
4. **Mix:** Compact mixer and effect chains, with a direct path back to recording.
5. **Export:** Range, format, destination, and a final playback check.

Use plain labels such as "Record another take" and "Choose best parts" in the starter workflow. A beginner should be able to complete the main journey without opening preferences or reading a manual. Provide a scalable UI and full keyboard access to the core transport and editing actions.

## 4. Project data and audio-engine constraints

- Store each project as a portable folder containing a versioned project manifest, copied source media, recorded takes, and disposable waveform/cache data. Cache deletion must not damage a project.
- Store timeline positions in integer audio sample frames, not rounded display seconds. Preserve sample rate and original file metadata.
- Do not allocate memory, access the filesystem, call network services, or block on the UI from the real-time audio callback. Move analysis, waveform generation, autosave, export, and future ML work off that callback.
- The recording pipeline must write audio incrementally and finalize it safely when recording stops. A crash should never corrupt earlier takes or the last valid project save.
- Separate the audio engine, project model, persistence, and UI so later MIDI, plugin hosting, stem separation, and live playback can be added without rewriting the recording path.
- Choose the actual desktop/audio framework during an initial technical spike. Record the choice, supported Windows drivers, codec and plugin licensing, and packaging method in an architecture decision document before substantial implementation.
- Keep the core project usable offline. Future connected services must be opt-in, and audio must never be uploaded without an explicit user action.

## 5. Validation and definition of done for release 1

The coding agent must document the exact test PC, mic/interface, driver, sample rate, buffer size, and headphone path used for audio validation. Test both a standard Windows mic path and an audio-interface path where available.

Release 1 is done only when all of these pass:

1. A new user can select a mic, import a backing track, record a vocal, hear playback, and export a song using the visible UI.
2. On the documented reference interface at 48 kHz and a documented practical buffer size, a 10-minute vocal overdub over at least eight playing audio tracks has no missing samples or audible dropouts attributable to the app.
3. A three-pass loop recording creates three take lanes; selected regions from those lanes play as one comp without gaps or clicks at edit boundaries.
4. A loopback test demonstrates that overdubs align with the playback timeline after latency calibration; report the measured offset and tolerance rather than assuming a universal latency number.
5. Save/reopen preserves media links, sample-accurate region positions, effect settings, routing, levels, markers, and the selected comp.
6. Force-close during editing, relaunch, and recover the last autosave and all completed takes. Earlier project saves remain readable.
7. Exported WAV and MP3 contain the audible mix and exclude the metronome and any reference track. Reimport the WAV and compare it with the rendered mix.
8. The app installs, launches, records, saves, reopens, and exports on a clean Windows 11 account without development tools installed.

Deliver source code, a reproducible build, a Windows installer or portable package, a small sample project using distributable media, automated tests for project persistence/timeline/edit operations, and a manual audio test log. Do not mark a feature complete based only on UI presence.

## 6. Roadmap after release 1

These are separately planned releases. Design extension points for them now, but do not create nonfunctional buttons or claim they work in release 1.

| Priority | Capability | Release condition |
| --- | --- | --- |
| Next | MIDI tracks, piano roll, instruments, audio/MIDI drag and drop | MIDI timing, note editing, save/reopen, and export validated. No artificial track cap. |
| Next | VST3 effect/instrument hosting | Scan, load, save state, bypass, and recover from a faulty plugin without losing the project. |
| Next | Native Vocal Tune: pitch correction and formant control | Real-time/offline quality and latency tested on vocals; edits remain reversible. |
| Next | Dedicated **Project** mastering page | Load a mix, compare references, set loudness/true peak targets, and export a master. |
| Advanced | Nine virtual instruments and 45+ native effects | Counts refer to distinct usable processors, each with presets and save/reopen tests. |
| Advanced | Stem separation and audio-to-MIDI | Run as cancellable background jobs; keep originals, expose confidence/quality limits, and require user confirmation before replacing content. |
| Advanced | Chord Assistant | Detect or suggest chords, allow correction, and generate a usable MIDI/chord track. |
| Advanced | **Show** live-set page | Prepare songs, reorder sets, preload media, and recover predictably after audio-device loss. |
| Dependent | Optional sample-library or stem-service connectors | Only with supported API terms; keep a manual WAV/MP3 import path. No third-party catalogs are bundled. |
| Dependent | Session import from other apps | Only from a documented/authorized format; standard WAV stem import is the fallback. |
| Dependent | Third-party amp/cab/pedal content | Named commercial models require rights and an asset plan. Generic original effects can ship separately. |
| Dependent | Immersive / object-based export | Define a supported workflow and validate a licensed renderer before promising a delivery format. |

## 7. First tasks for the coding agent

1. Inspect this specification and write a short implementation plan with the chosen framework, audio driver strategy, project format, key dependencies/licenses, and milestone order.
2. Build an audio-device and recording spike that proves mic input, backing-track playback, monitoring, and correctly aligned overdubbing on Windows 11.
3. Implement the main journey as vertical slices: project/import → record/takes → edit/comp → effects/mix → export/recovery.
4. Run the release checks above on real hardware and record results and known limits.

**Scope rule:** If a choice threatens reliable recording or recovery, defer the advanced feature. The first usable build should finish a song, not merely display DAW controls.
