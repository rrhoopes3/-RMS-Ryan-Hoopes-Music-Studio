# Loopback calibration (recording offset)

RMS places each take using:

`take start = playhead when record actually began − (input buffer + output buffer + your offset)`

Buffers are the Audio Setup slider (default 20 ms each way). The offset slider is for leftover delay after that.

## Procedure

1. Use **wired headphones** and a Windows WASAPI mic (or an interface in WASAPI Exclusive).
2. Import or generate a clicky backing track (the Practice Beat sample works).
3. Route the RMS output back into the input (hardware loopback, or a cable from line-out to line-in). Turn **software monitoring off**.
4. Record one pass of the beat through the loopback.
5. Zoom the take against the backing clip. Measure the shift in samples (or milliseconds).
6. Enter the opposite value in **Recording offset (ms)** so the next take lands on the beat.
7. Record again. Alignment should be inside a few milliseconds at 48 kHz / 20 ms buffer. Write the measured leftover in the manual test log.

Do not assume one offset works for every Bluetooth headset or Exclusive/Shared change. Recalibrate if you change device, buffer, or share mode.

## Bluetooth

If Audio Setup warns that the output looks wireless or slower than ~80 ms, use wired headphones for overdubs. Bluetooth is fine for listening back, not for tight singing.
