# Spec: WF2_DSX bridge

## Objective

Bridge Wreckfest 2 Pino UDP telemetry to the DSX UDP API for one DualSense controller.
The first release covers RPM lighting, gear LEDs, brake/ABS feedback, driven-wheel
slip feedback, engine temperature, running state, and drivetrain damage.

The bridge listens on Wreckfest 2's official default UDP port `23123` and sends
to the BeamNG mod's fixed DSX endpoint `127.0.0.1:6969`.

## Tech stack

- C# on .NET 8
- No runtime NuGet dependencies
- Official `TelemetryDataFormatPino.h` revision 5 as the packet contract

## Commands

- Build: `dotnet build src/Wf2Dsx/Wf2Dsx.csproj -c Release`
- Test: `dotnet run --project tests/Wf2Dsx.Tests/Wf2Dsx.Tests.csproj -c Release`
- Publish: `dotnet publish src/Wf2Dsx/Wf2Dsx.csproj -c Release -r win-x64 --self-contained true`

## Project structure

- `src/Wf2Dsx.Core` — packet decoding and DSX mapping
- `src/Wf2Dsx` — UDP host and DSX client
- `tests/Wf2Dsx.Tests` — dependency-free executable test suite
- `docs` — protocol notes and scope

## Code style

```csharp
if (!PinoMainDecoder.TryDecode(packet, out var telemetry))
{
    return;
}
```

Use immutable records for decoded state, explicit protocol constants, and no
unsafe code.

## Testing strategy

Synthetic 1218-byte Pino packets verify offsets, validation, unit conversion,
and DSX instruction mapping. A localhost UDP smoke test verifies serialization.

## Boundaries

- Always: validate signature, packet type, and minimum packet size.
- Ask first: controller support beyond index 0 or game-memory access.
- Never: inject into Wreckfest 2, patch game binaries, or add unrelated vehicle mechanics.

## Success criteria

- Decode a revision 5 Pino Main packet at 60 Hz without allocation-heavy marshalling.
- Emit valid DSX JSON to `127.0.0.1:6969`, fixed to controller index 0.
- Reset controller effects when telemetry stops or the player is not driving.
- Build and tests pass on the installed .NET SDK.
