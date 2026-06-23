# WF2_DSX — DualSense for Wreckfest 2

Adaptive triggers and lightbar for Wreckfest 2 on a PS5 DualSense, via DSX.
Feel the brake firm up, the wheels lock and spin, the road surface buzz through the
throttle, and watch the lightbar track your RPM.

## Quick start (no Steam settings needed)

1. **Install DSX** and connect your DualSense (DSX is the app that drives the controller).
2. **Run `WF2_DSX.exe`** (double-click). A small status window opens — keep it open.
3. **Play Wreckfest 2** as usual.

That's it. WF2_DSX turns on the game's telemetry by itself, waits for the race, and the
effects kick in. Close the window when you're done.

Notes:
- **First time:** if Wreckfest 2 was already running, restart it once so it picks up
  telemetry. After that it's on for good.
- **Order doesn't matter:** start DSX, WF2_DSX and the game in any order. If DSX or the game
  isn't up yet, WF2_DSX just waits and connects automatically.
- **Keyboard players unaffected:** with no DualSense bound in DSX, nothing happens — you can
  leave WF2_DSX off entirely.
- A copy of the status text is saved to `wf2_dsx.log` next to the exe (useful for support).

## Advanced: launch automatically with the game (Steam)

Optional. If you'd rather not click the exe each time, let Steam start it for you.

Steam → right-click **Wreckfest 2** → **Properties** → **Launch Options**, paste:

```
"<full path>\WF2_DSX.exe" --play %command%
```

WF2_DSX then launches the game itself, runs hidden in the background, and closes when you
quit the game. Steam still tracks playtime, and the game's files are never touched (updates
and integrity checks are unaffected).

## Customising the effects (config.json)

On first run WF2_DSX writes a `config.json` next to the exe. Edit it, save, and restart
WF2_DSX — no rebuild needed. Delete the file to reset to defaults.

You can turn each effect on/off and adjust a few values:

| Setting | What it does | Default |
| --- | --- | --- |
| `brake` | firm brake-pedal resistance | `true` |
| `abs` | fast pulse while ABS works | `true` |
| `wheelLock` | pulse when the wheels lock under braking | `true` |
| `wheelspin` | pulse on wheelspin | `true` |
| `tractionControl` | pulse while TCS cuts power | `true` |
| `surfaceFeel` | buzz the throttle over bumps/gravel | `true` |
| `lightbar` | RPM colour + warnings on the light bar | `true` |
| `collisionFlash` | white flash on impacts (needs `lightbar`) | `true` |
| `gearLeds` | show the current gear on the player LEDs | `true` |
| `temperatureWarning` | mic LED warns on engine overheat | `true` |
| `brakeForceRunning` | brake stiffness, engine on (0–8) | `4` |
| `brakeForceEngineOff` | brake stiffness, engine dead (0–8) | `8` |
| `surfaceIntensity` | road-texture strength multiplier | `1.0` |
| `lightbarBrightness` | light bar brightness (0.0–1.0) | `1.0` |
| `pinoPort` | Wreckfest 2 telemetry port | `23123` |

## Ports

- Wreckfest 2 Pino input: UDP `23123` (official default)
- DSX output: `127.0.0.1:6969` — fixed to match the BeamNG mod

Optional Pino input override: `WF2_DSX.exe --pino-port 23124` (overrides `config.json`). The DSX
port cannot be changed. The bridge sends at most 20 updates per second; if telemetry stops for
500 ms it resets the triggers and LEDs.

### Flags

- `--play <game command>` — launch and supervise the game; everything after `--play` is the
  game command line (provided by Steam's `%command%`).
- `--watch-game [name]` — exit when an already-running game process closes (default
  `Wreckfest2`). Used by the fallback `wf2_dsx.cmd`.
- `--hidden` — start with the console hidden (implied by `--play`).
- `--diagnostic` — record full-rate telemetry to `diagnostics/` (keeps the console visible).

### Fallback: wf2_dsx.cmd

If launching the game from the exe ever misbehaves, use `wf2_dsx.cmd` instead with launch
options `"<full path>\wf2_dsx.cmd" %command%`. It starts the bridge hidden (`--watch-game
--hidden`) and runs the game; its own cmd window stays open while you play.

## Effects

- **Left trigger** — brake pedal resistance (firmer with the engine off), a fast pulse
  while ABS intervenes, and a 30 Hz pulse on a real wheel lock.
- **Right trigger** — a pulse on wheelspin / while TCS cuts power.
- **Lightbar** — RPM-based colour gradient, a red pulse on overheat/engine damage, and a
  brief white flash on collision impacts (`LastCollisionTime` increases per hit).
- **Player LEDs** — current gear. **Mic LED** — coolant-temperature warning.

### Slip gating

Pino reports `slip = -1` for every wheel (and `+1` for the rear-right) on a stationary
car, so the raw value reads as a permanent wheel lock / wheelspin. Gating is asymmetric:

- **Brake lock-up** is gated on real motion (above ~7 km/h, `MovingMetersPerSecond`) plus
  an applied brake — a parked car with the brake held is the main source of the artifact.
- **Wheelspin** is gated on applied throttle, **not** speed. Real launch wheelspin happens
  at 1–2 km/h (rear slip saturates to 1.0), so a speed gate would swallow it; the parked
  artifact, by contrast, almost always occurs with no throttle.

ABS and TCS are authoritative game flags and are trusted directly. Thresholds
(`LockupThreshold`, `WheelspinThreshold`) were tuned from recorded telemetry: real wheel
lock while braking sits around 0.10–0.26, wheelspin fans out to ~0.7.

## Diagnostic mode

Run `WF2_DSX.exe --diagnostic`. DSX effects remain active while full-rate Pino
samples are written to `diagnostics/wf2_telemetry_YYYYMMDD_HHMMSS.csv` beside
the executable. Stop with Ctrl+C after recording representative driving,
wheelspin, braking, ABS, collisions, jumps, and damaged-car behavior.

Wreckfest 2 creates `telemetry/config.json` inside the Steam-ID-specific
`Documents/My Games/Wreckfest 2/.../savegame` directory. WF2_DSX resolves
Documents with `Environment.SpecialFolder.MyDocuments`, so redirected and
OneDrive-backed Documents folders work without hard-coded paths.

## Credits & License

WF2_DSX is an independent, fan-made tool. It is not affiliated with Bugbear, THQ Nordic,
or Sony. "Wreckfest" and "DualSense" are trademarks of their respective owners. WF2_DSX
talks to [DSX](https://store.steampowered.com/app/1812620/DSX/) over its local UDP API.

Released under the [MIT License](LICENSE) © 2026 SYSTEMATI0N — free to use, modify, and
redistribute; just keep the copyright notice.
