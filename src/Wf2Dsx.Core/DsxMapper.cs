namespace Wf2Dsx.Core;

public static class DsxMapper
{
    private const int TriggerUpdate = 1;
    private const int RgbUpdate = 2;
    private const int PlayerLed = 3;
    private const int MicLed = 5;
    private const int LeftTrigger = 1;
    private const int RightTrigger = 2;
    private const int Normal = 0;
    private const int Resistance = 13;
    private const int AutomaticGun = 17;

    public static DsxPacket Map(TelemetryFrame telemetry, long elapsedMilliseconds)
    {
        if (!telemetry.InRace || !telemetry.PlayerDriving)
        {
            return InactivePacket();
        }

        var instructions = new List<DsxInstruction>(5);
        instructions.Add(BrakeTrigger(telemetry));
        instructions.Add(ThrottleTrigger(telemetry));
        instructions.Add(Instruction(MicLed, 0, MicMode(telemetry.WaterTemperatureCelsius)));

        var color = EngineColor(telemetry, elapsedMilliseconds);
        instructions.Add(Instruction(RgbUpdate, 0, color.R, color.G, color.B));

        var lights = GearLights(telemetry.Gear);
        instructions.Add(Instruction(PlayerLed, 0, lights[0], lights[1], lights[2], lights[3], lights[4]));
        return new DsxPacket(instructions);
    }

    private static DsxInstruction BrakeTrigger(TelemetryFrame telemetry)
    {
        var lockup = telemetry.TireSlipRatios.Max(slip => MathF.Max(0, -slip));
        if (telemetry.AbsActive)
        {
            return Instruction(TriggerUpdate, 0, LeftTrigger, AutomaticGun, 0,
                ClampForce(telemetry.Brake * 7), 10);
        }
        if (lockup > 0.1f)
        {
            return Instruction(TriggerUpdate, 0, LeftTrigger, AutomaticGun, 0,
                ClampForce(lockup * 7), 30);
        }

        return Instruction(TriggerUpdate, 0, LeftTrigger, Resistance, 0,
            telemetry.EngineRunning ? 1 : 8);
    }

    private static DsxInstruction ThrottleTrigger(TelemetryFrame telemetry)
    {
        if (!telemetry.EngineRunning || telemetry.Clutch > 0.1f)
        {
            return Instruction(TriggerUpdate, 0, RightTrigger, Normal, 0, 0, 0);
        }

        var driven = telemetry.Driveline switch
        {
            DrivelineType.FrontWheelDrive => telemetry.TireSlipRatios[..2],
            DrivelineType.RearWheelDrive => telemetry.TireSlipRatios[2..],
            _ => telemetry.TireSlipRatios
        };
        var wheelspin = driven.Max(slip => MathF.Max(0, slip));
        var strength = ClampForce(wheelspin * 7);
        if (telemetry.TcsActive && strength < 2) strength = 2;
        return Instruction(TriggerUpdate, 0, RightTrigger, AutomaticGun, 0, strength, 30);
    }

    private static (int R, int G, int B) EngineColor(TelemetryFrame telemetry, long timeMs)
    {
        if (!telemetry.EngineRunning || telemetry.EngineMisfiring || telemetry.EngineDamage >= 3)
        {
            var pulse = (Math.Sin(timeMs / 750d * Math.PI * 2) + 1) / 2;
            return ((int)(255 * pulse), 0, 0);
        }

        var limit = telemetry.EngineRpmMax > 0 ? telemetry.EngineRpmMax : telemetry.EngineRpmRedline;
        var rpm = limit > 0 ? Math.Clamp((float)telemetry.EngineRpm / limit, 0, 1) : 0;
        var palette = new[]
        {
            (Position: 0f, R: 30f, G: 85f, B: 215f),
            (Position: .42f, R: 210f, G: 45f, B: 145f),
            (Position: .72f, R: 255f, G: 110f, B: 90f),
            (Position: 1f, R: 177f, G: 0f, B: 11f)
        };
        var lower = palette[0];
        var upper = palette[^1];
        for (var i = 0; i < palette.Length - 1; i++)
        {
            if (rpm >= palette[i].Position && rpm <= palette[i + 1].Position)
            {
                lower = palette[i]; upper = palette[i + 1]; break;
            }
        }
        var mix = (rpm - lower.Position) / (upper.Position - lower.Position);
        var brightness = .35f + .65f * rpm * rpm;
        return (
            (int)(Lerp(lower.R, upper.R, mix) * brightness),
            (int)(Lerp(lower.G, upper.G, mix) * brightness),
            (int)(Lerp(lower.B, upper.B, mix) * brightness));
    }

    private static bool[] GearLights(int gear) =>
    [
        gear is >= 1 and <= 5 or <= -1,
        gear is >= 2 and <= 6 or < -1,
        gear is >= 3 and <= 7 or 10,
        gear is >= 4 and <= 8 or < -1,
        gear is >= 5 and <= 9 or <= -1
    ];

    private static int MicMode(float temperature) => temperature >= 115 ? 0 : temperature >= 108 ? 1 : 2;
    private static int ClampForce(float value) => Math.Clamp((int)MathF.Round(value), 0, 7);
    private static float Lerp(float a, float b, float amount) => a + (b - a) * amount;
    private static DsxInstruction Instruction(int type, params object[] parameters) => new(type, parameters);

    private static DsxPacket InactivePacket() => new DsxPacket(
    [
        Instruction(TriggerUpdate, 0, LeftTrigger, Normal, 0, 0, 0),
        Instruction(TriggerUpdate, 0, RightTrigger, Normal, 0, 0, 0),
        Instruction(MicLed, 0, 2),
        Instruction(RgbUpdate, 0, 0, 0, 0),
        Instruction(PlayerLed, 0, false, false, false, false, false)
    ]);
}
