using System.Buffers.Binary;
using Wf2Dsx.Core;

var failures = new List<string>();

Run("decodes Pino Main engine, input, gear and tire fields", () =>
{
    var packet = new byte[PinoMainDecoder.PacketSize];
    WriteUInt32(packet, 0, PinoMainDecoder.Signature);
    packet[4] = 0; // Main packet
    packet[5] = 1 << 5; // GAME_STATUS_IN_RACE
    WriteInt32(packet, 435, 6123);
    WriteInt32(packet, 439, 7500);
    WriteInt32(packet, 443, 6900);
    packet[411] = 4; // 3rd gear: 0=R, 1=N, 2=1st
    WriteFloat(packet, 490, 0.75f);
    WriteFloat(packet, 494, 0.4f);
    WriteFloat(packet, 413, 25f);
    WriteFloat(packet, 502, 0.2f);
    WriteFloat(packet, 506, -0.3f);
    WriteFloat(packet, 568, 4f);
    WriteFloat(packet, 572, -2f);
    WriteFloat(packet, 576, 9f);
    WriteFloat(packet, 592, 0.12f);
    WriteFloat(packet, 620, -1.5f);
    packet[636] = 4; // gravel
    WriteFloat(packet, 1093, -0.65f);
    packet[21] = 77;
    WriteInt32(packet, 305, 1234);
    packet[350] = 1; // ABS active
    packet[434] = 1; // engine running
    WriteFloat(packet, 463, 388.15f); // 115 C water
    WriteFloat(packet, 588, 0.35f); // FL slip ratio
    WriteFloat(packet, 660, -0.22f); // FR slip ratio
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(1090), 0b1111);

    Assert(PinoMainDecoder.TryDecode(packet, out var value), "packet rejected");
    Assert(value.EngineRpm == 6123, "rpm");
    Assert(value.EngineRpmMax == 7500, "rpm max");
    Assert(value.EngineRpmRedline == 6900, "redline");
    Assert(value.Gear == 3, "gear conversion");
    AssertNear(value.Throttle, 0.75f, "throttle");
    AssertNear(value.Brake, 0.4f, "brake");
    Assert(value.AbsActive, "ABS flag");
    Assert(value.EngineRunning, "engine flag");
    AssertNear(value.WaterTemperatureCelsius, 115f, "water temp");
    AssertNear(value.TireSlipRatios[0], 0.35f, "FL slip");
    AssertNear(value.TireSlipRatios[1], -0.22f, "FR slip");
    Assert(value.PlayerDriving, "player status");
    AssertNear(value.SpeedMetersPerSecond, 25f, "speed");
    AssertNear(value.Handbrake, 0.2f, "handbrake");
    AssertNear(value.Steering, -0.3f, "steering");
    AssertNear(value.AccelerationLocal![2], 9f, "longitudinal acceleration");
    AssertNear(value.TireSlipAngles![0], 0.12f, "FL slip angle");
    AssertNear(value.SuspensionVelocities![0], -1.5f, "FL suspension velocity");
    Assert(value.SurfaceTypes![0] == 4, "FL surface");
    AssertNear(value.FfbForce, -0.65f, "FFB force");
    Assert(value.Health == 77, "health");
    Assert(value.LastCollisionTime == 1234, "collision time");
});

Run("rejects invalid signature and truncated packets", () =>
{
    var packet = new byte[PinoMainDecoder.PacketSize];
    Assert(!PinoMainDecoder.TryDecode(packet, out _), "invalid signature accepted");
    Assert(!PinoMainDecoder.TryDecode(packet.AsSpan(0, 100), out _), "truncated packet accepted");
});

Run("maps driving telemetry to DSX instructions on controller zero", () =>
{
    var telemetry = new TelemetryFrame(
        InRace: true, PlayerDriving: true, EngineRunning: true, EngineMisfiring: false,
        AbsActive: true, TcsActive: false, Driveline: DrivelineType.RearWheelDrive,
        Gear: 3, EngineRpm: 6000, EngineRpmMax: 7500, EngineRpmRedline: 6900,
        Throttle: 0.8f, Brake: 0.6f, Clutch: 0, WaterTemperatureCelsius: 110,
        EngineDamage: 0, GearboxDamage: 0,
        TireSlipRatios: [0.02f, 0.01f, 0.45f, 0.4f]);

    var packet = DsxMapper.Map(telemetry, elapsedMilliseconds: 1000);
    var json = DsxJson.Serialize(packet);

    Assert(packet.Instructions.Count == 5, "instruction count");
    Assert(json.Contains("\"instructions\""), "JSON packet property");
    Assert(json.Contains("\"type\":2"), "RGB instruction");
    Assert(json.Contains("[0,1,17,0,4,10]"), "ABS trigger instruction");
    Assert(json.Contains("[0,true,true,true,false,false]"), "third gear LEDs");
    Assert(json.Contains("\"type\":5,\"parameters\":[0,1]"), "temperature pulse");
});

Run("maps inactive player to a complete controller reset", () =>
{
    var telemetry = new TelemetryFrame(
        InRace: false, PlayerDriving: false, EngineRunning: false, EngineMisfiring: false,
        AbsActive: false, TcsActive: false, Driveline: DrivelineType.FrontWheelDrive,
        Gear: 0, EngineRpm: 0, EngineRpmMax: 0, EngineRpmRedline: 0,
        Throttle: 0, Brake: 0, Clutch: 0, WaterTemperatureCelsius: 0,
        EngineDamage: 0, GearboxDamage: 0, TireSlipRatios: [0, 0, 0, 0]);

    var json = DsxJson.Serialize(DsxMapper.Map(telemetry, 0));
    Assert(json.Contains("[0,1,0,0,0,0]"), "left trigger reset");
    Assert(json.Contains("[0,2,0,0,0,0]"), "right trigger reset");
});

Run("enables telemetry below the supplied Windows Documents known folder", () =>
{
    var documents = Path.Combine(Path.GetTempPath(), $"WF2_DSX_{Guid.NewGuid():N}", "OneDrive Documents");
    var configPath = Path.Combine(documents, "My Games", "Wreckfest 2", "123456789", "savegame", "telemetry", "config.json");
    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
    File.WriteAllText(configPath, """
        {
          "udp": [{ "enabled": 0, "ip": "127.0.0.1", "port": "23123" }],
          "logging": [{ "format": "none" }],
          "server": [{ "enabled": 0, "automaticMainCar": 0 }]
        }
        """);

    try
    {
        var result = TelemetryConfigurator.EnsureEnabled(documents, 23123);
        var saved = File.ReadAllText(configPath);

        Assert(result.Found, "config not found below supplied Documents folder");
        Assert(result.Changed, "disabled config not changed");
        Assert(result.ConfigPaths.Single() == configPath, "wrong config path");
        Assert(saved.Contains("\"enabled\": 1"), "UDP not enabled");
        Assert(saved.Contains("\"port\": \"23123\""), "port must remain a string");
        Assert(saved.Contains("\"logging\""), "unrelated config section lost");
    }
    finally
    {
        Directory.Delete(Path.GetDirectoryName(documents)!, recursive: true);
    }
});

Run("formats extended telemetry as invariant diagnostic CSV", () =>
{
    var telemetry = new TelemetryFrame(
        InRace: true, PlayerDriving: true, EngineRunning: true, EngineMisfiring: false,
        AbsActive: true, TcsActive: false, Driveline: DrivelineType.RearWheelDrive,
        Gear: 2, EngineRpm: 5432, EngineRpmMax: 7000, EngineRpmRedline: 6500,
        Throttle: 0.75f, Brake: 0.25f, Clutch: 0, WaterTemperatureCelsius: 101.5f,
        EngineDamage: 1, GearboxDamage: 2, TireSlipRatios: [0.1f, -0.2f, 0.3f, 0.4f],
        SessionTime: 5000, RaceTime: 4000, SpeedMetersPerSecond: 20,
        EngineTorque: 350, EnginePower: 120000, Handbrake: 0.1f, Steering: -0.5f,
        FfbForce: 0.6f, Health: 83, LastCollisionTime: 3900,
        AccelerationLocal: [1, 2, 3], TireSlipAngles: [0.01f, 0.02f, 0.03f, 0.04f],
        TireLoadsVertical: [1000, 1100, 1200, 1300],
        TireForcesLateral: [10, 20, 30, 40], TireForcesLongitudinal: [50, 60, 70, 80],
        SuspensionVelocities: [0.1f, 0.2f, 0.3f, 0.4f],
        SuspensionDisplacementsNormalized: [0.5f, 0.6f, 0.7f, 0.8f],
        SurfaceTypes: [2, 2, 4, 4]);

    var header = DiagnosticCsv.Header;
    var row = DiagnosticCsv.FormatRow(telemetry, 12345);

    Assert(header.Contains("fl_slip_ratio,fl_slip_angle"), "wheel columns missing");
    Assert(header.Contains("accel_x,accel_y,accel_z"), "acceleration columns missing");
    Assert(row.StartsWith("12345,5000,4000,72"), "time or speed conversion");
    Assert(row.Contains(",0.75,0.25,"), "invariant pedal values");
    Assert(row.Split(',').Length == header.Split(',').Length, "CSV column count mismatch");
});

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAILED: {failures.Count}");
    failures.ForEach(Console.Error.WriteLine);
    return 1;
}

Console.WriteLine("PASS: all tests");
return 0;

void Run(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS: {name}"); }
    catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertNear(float actual, float expected, string message)
{
    if (MathF.Abs(actual - expected) > 0.001f)
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
}

static void WriteUInt32(byte[] data, int offset, uint value) =>
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);

static void WriteInt32(byte[] data, int offset, int value) =>
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);

static void WriteFloat(byte[] data, int offset, float value) =>
    WriteInt32(data, offset, BitConverter.SingleToInt32Bits(value));
