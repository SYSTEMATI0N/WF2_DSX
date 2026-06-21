using System.Buffers.Binary;

namespace Wf2Dsx.Core;

public static class PinoMainDecoder
{
    public const uint Signature = 1_869_769_584;
    public const int PacketSize = 1_218;

    private const int AssistsOffset = 350;
    private const int DrivelineOffset = 410;
    private const int EngineOffset = 434;
    private const int InputOffset = 490;
    private const int TiresOffset = 580;
    private const int TireSize = 72;
    private const int PlayerStatusOffset = 1_090;
    private const int DamageOffset = 329;

    public static bool TryDecode(ReadOnlySpan<byte> packet, out TelemetryFrame value)
    {
        value = null!;
        if (packet.Length < PacketSize || ReadUInt32(packet, 0) != Signature || packet[4] != 0)
        {
            return false;
        }

        var statusFlags = packet[5];
        var assistFlags = packet[AssistsOffset];
        var engineFlags = packet[EngineOffset];
        var playerFlags = BinaryPrimitives.ReadUInt16LittleEndian(packet[PlayerStatusOffset..]);
        var rawGear = packet[DrivelineOffset + 1];
        var waterKelvin = ReadFloat(packet, EngineOffset + 29);

        var slips = new float[4];
        for (var tire = 0; tire < slips.Length; tire++)
        {
            slips[tire] = ReadFloat(packet, TiresOffset + tire * TireSize + 8);
        }

        value = new TelemetryFrame(
            InRace: (statusFlags & (1 << 5)) != 0,
            PlayerDriving: (playerFlags & 0b1111) == 0b1111,
            EngineRunning: (engineFlags & 1) != 0,
            EngineMisfiring: (engineFlags & (1 << 2)) != 0,
            AbsActive: (assistFlags & 1) != 0,
            TcsActive: (assistFlags & (1 << 1)) != 0,
            Driveline: (DrivelineType)packet[DrivelineOffset],
            Gear: rawGear switch { 0 => -1, 1 => 0, _ => rawGear - 1 },
            EngineRpm: ReadInt32(packet, EngineOffset + 1),
            EngineRpmMax: ReadInt32(packet, EngineOffset + 5),
            EngineRpmRedline: ReadInt32(packet, EngineOffset + 9),
            Throttle: ReadFloat(packet, InputOffset),
            Brake: ReadFloat(packet, InputOffset + 4),
            Clutch: ReadFloat(packet, InputOffset + 8),
            WaterTemperatureCelsius: waterKelvin > 0 ? waterKelvin - 273.15f : 0,
            EngineDamage: ReadDamageState(packet, 0),
            GearboxDamage: ReadDamageState(packet, 1),
            TireSlipRatios: slips);
        return true;
    }

    private static byte ReadDamageState(ReadOnlySpan<byte> packet, int part)
    {
        var bit = part * 3;
        var packed = BinaryPrimitives.ReadUInt16LittleEndian(packet[(DamageOffset + bit / 8)..]);
        return (byte)((packed >> (bit % 8)) & 0b111);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);

    private static float ReadFloat(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int32BitsToSingle(ReadInt32(data, offset));
}
