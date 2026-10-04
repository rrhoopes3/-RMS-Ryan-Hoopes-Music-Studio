# Beginner studio shortlist

The current audio engine already supports tracks, imported clips, takes, mixing, device selection, and WAV/MP3 export. This pass puts those capabilities in the main window and adds a live picture of a recording.

## Delivery order

1. **See and record audio.** Show input and output level, an obvious recording/count-in state, playback waveforms on track lanes, and a growing waveform in the armed lane while a take is written. The live display uses bounded peak data from the recording writer so it does not block capture.
2. **Build a song in layers.** Add a named recordable track, import WAV/MP3 onto its own lane, select and arm a lane, and record over the existing stack. Preserve previous takes. Add a named new-song flow with tempo, meter, and sample rate.
3. **Mix and export.** Expose per-track gain, pan, mute, solo, and the existing vocal presets. Export a whole mix or selected range to WAV/MP3 through the existing exporter.
4. **Finish an arrangement.** Expose timeline seeking, zoom, selection, split, move, delete, and undo/redo. The existing take lanes, loop, punch, and comp model remain available for the next round of controls.
5. **Integrate and verify.** Keep the pad composer usable, surface the new tools in one window, build and run the existing automated suite, and review recording safety, exported audio, and project persistence.

## Acceptance checks

- A new user can create a song, add a second vocal or instrument track, arm it, and record without erasing the first take.
- Imported audio appears on its own lane; original source files stay untouched.
- The selected/armed track is visible. Input level and a growing take waveform update during recording; saved clips show cached waveforms during playback.
- Track gain/pan/mute/solo and a vocal preset change playback and survive save/reopen.
- A user can export a named WAV or MP3 mix and edit a selected clip from the main window.
- Recording failure retains the existing recovery path and does not silently discard a take.

## Later work

MIDI instruments, plugin hosting, pitch correction, stem separation, cloud collaboration, detailed loudness mastering, and a full piano roll are outside this pass. They need separate design and testing after the basic record, layer, mix, and export journey works.
