# IP audit — RMS (Ryan Music Studio)

Date: 2026-09-30. Scope: this workspace before the public GitHub push.

## Checked

- All `*.cs`, `*.xaml`, `*.md`, `*.txt` for other-product UI copy, skins, and brand lockups
- Bundled audio under `samples/`
- NuGet dependencies (NAudio 2.2.1 MIT; no LAME, no VST3 SDK, no Steinberg ASIO SDK used at runtime)
- Fonts: Palatino Linotype, Segoe UI, Consolas (Windows system fonts only)
- Icons/images: none bundled (no third-party DAW artwork)
- Secrets: no `.env`, API keys, cookies, or user recordings in source

## Removed or rewritten

- Spec/docs no longer use a different public working name
- Roadmap rows that named commercial catalogs, amp suites, or spatial-audio brands as if they were product features were rewritten as generic, rights-required future work
- Architecture notes no longer point at third-party importers as if they ship in release 1

## Bundled media

| File | Source | License |
| --- | --- | --- |
| `samples/vocal-over-beat/media/originals/practice-beat.wav` | RMS `BackingRenderer` (synthesized) | CC0-equivalent; see MEDIA-LICENSE.txt |
| `samples/vocal-over-beat/media/working/practice-beat.wav` | Copy of the same render at project rate | Same |

No copyrighted songs, commercial loops, or ripped media.

## Remaining risks

- NAudio.Asio is a **transitive NuGet** package. RMS does not call ASIO. Do not enable it without Steinberg distribution review.
- MP3 depends on the OS Media Foundation encoder. We do not redistribute that component.
- Users can import any WAV/MP3 they own. RMS copies into the project folder; that is the user's license problem, not bundled content.
- A trademark search for the letters "RMS" in other industries was not a legal clearance. The expansion "Ryan Music Studio" is the intended mark.

## Not in the repo

User takes, `%LOCALAPPDATA%\RMS` settings, `dist/` binaries, `bin/`, `obj/`.
