# WF2_DSX

Windows bridge from Wreckfest 2 Pino telemetry to DSX adaptive trigger and LED commands.

## Ports

- Wreckfest 2 Pino input: UDP `23123` (official default)
- DSX output: `127.0.0.1:6969` — fixed to match the BeamNG mod

The two ports must differ because WF2_DSX receives Pino while DSX already owns UDP 6969.

## Run

1. Start DSX and connect the first DualSense controller.
2. Start WF2_DSX. It locates Windows' real Documents known folder and enables
   Wreckfest 2 Pino telemetry at `127.0.0.1:23123` automatically.
3. Start `WF2_DSX.exe` before or after entering a race.

The bridge sends at most 20 updates per second, matching the BeamNG mod. If telemetry
stops for 500 ms it resets adaptive triggers and controller LEDs.

Optional Pino input override: `WF2_DSX.exe --pino-port 23124`. The DSX port cannot be changed.

## Diagnostic mode

Run `WF2_DSX.exe --diagnostic`. DSX effects remain active while full-rate Pino
samples are written to `diagnostics/wf2_telemetry_YYYYMMDD_HHMMSS.csv` beside
the executable. Stop with Ctrl+C after recording representative driving,
wheelspin, braking, ABS, collisions, jumps, and damaged-car behavior.

Wreckfest 2 creates `telemetry/config.json` inside the Steam-ID-specific
`Documents/My Games/Wreckfest 2/.../savegame` directory. WF2_DSX resolves
Documents with `Environment.SpecialFolder.MyDocuments`, so redirected and
OneDrive-backed Documents folders work without hard-coded paths.
