# RMS manual audio test log

Fill this in on the machine that actually recorded. Do not mark a feature complete from UI presence alone.

## Test PC (this workspace, 2026-09-30)

| Item | Value |
| --- | --- |
| OS | Windows 11 64-bit (10.0.26200) |
| App | RMS 1.0 (`RMS.exe` self-contained win-x64) |
| Audio endpoints seen | WASAPI spike listed SteelSeries Sonar mic, Sound Blaster Z What-U-Hear / Digital-In, Arctis Nova Pro Wireless mic, W1 mic; outputs include Sound Blaster Z speakers, Arctis wireless (warned), Sonar virtual, NVIDIA HDMI |
| Driver path under test | **WASAPI Shared** (required). WASAPI Exclusive available in Audio Setup. |
| ASIO | Not shipped. Not tested. |
| Intended sample rate | 48 kHz |
| Buffer | 20 ms (setup default) |
| Headphone path | Prefer a **wired** Realtek or Sound Blaster output. Arctis wireless should show the high-latency warning. |

## Automated results

Run `dotnet test RyanMusicStudio.sln -c Release`. Persistence, timeline, edit, and incremental-WAV tests do not need a mic.

## Hardware checks

| Check | Result | Notes |
| --- | --- | --- |
| Enumerate mic + output, show channels | Pass (spike --list-only) | 5 inputs / 10 outputs; Arctis headphones flagged wireless |
| Live input meter + clip | Pending | Needs a real mic |
| Output test tone | Pending | |
| Software monitor on/off | Pending | Explain double-voice if interface direct-monitors |
| Test take commit-then-play | Pending | Success only after WAV header finalize |
| Device unplug during record | Pending | Must stop, keep take, no silent failover |
| Overdub alignment after calibration | Pending | See LOOPBACK-CALIBRATION.md; record measured offset here: _____ ms |
| 10-minute overdub / 8 tracks / no dropouts | Not run in this agent session | Needs a long interactive pass |
| Three loop takes → one comp | Pending | |
| Save / reopen / force-close recovery | Pending interactive; autosave unit-tested | |
| Export WAV/MP3 excludes click + reference | Pending interactive; renderer uses same mix graph | |
| Clean Windows account install | Pending | Portable `RMS.exe` is the artifact |

## Known limits

- No ASIO. Interfaces use their WASAPI endpoints. Exclusive mode is the low-latency path.
- MP3 encode depends on Windows Media Foundation. If it fails, use WAV.
- Time stretch is a small-ratio WSOLA / playback ratio. Check by ear before keeping it.
- This agent session could not complete a full sung overdub on hardware. Treat audio rows above as **not validated** until a human fills them in.
