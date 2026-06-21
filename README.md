# WF2_DSX

Windows bridge from Wreckfest 2 Pino telemetry to DSX adaptive trigger and LED commands.

## Ports

- Wreckfest 2 Pino input: UDP `5606`
- DSX output: `127.0.0.1:6969` — fixed to match the BeamNG mod

The two ports must differ because WF2_DSX receives Pino while DSX already owns UDP 6969.

## Run

1. Start DSX and connect the first DualSense controller.
2. Enable Wreckfest 2 Pino telemetry with destination `127.0.0.1:5606`.
3. Start `WF2_DSX.exe` before or after entering a race.

The bridge sends at most 20 updates per second, matching the BeamNG mod. If telemetry
stops for 500 ms it resets adaptive triggers and controller LEDs.

Optional diagnostic override: `WF2_DSX.exe --pino-port 5607`. The DSX port cannot be changed.
