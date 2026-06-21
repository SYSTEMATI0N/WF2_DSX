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
});

Run("rejects invalid signature and truncated packets", () =>
{
    var packet = new byte[PinoMainDecoder.PacketSize];
    Assert(!PinoMainDecoder.TryDecode(packet, out _), "invalid signature accepted");
    Assert(!PinoMainDecoder.TryDecode(packet.AsSpan(0, 100), out _), "truncated packet accepted");
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
