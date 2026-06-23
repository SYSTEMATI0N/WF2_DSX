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

    // A parked car reports slip saturated at -1 with the brake held, which the raw value
    // would read as a permanent wheel lock. The brake (lock-up) effect is therefore gated on
    // real motion. Wheelspin is gated on throttle instead, so launch spin at 1-2 km/h survives.
    private const float MovingMetersPerSecond = 2.0f; // ~7.2 km/h
    private const float BrakeEngaged = 0.15f;
    private const float ThrottleEngaged = 0.12f;

    // Muscle-car / derby brake pedal base resistance lives in DsxSettings (BrakeForceRunning /
    // BrakeForceEngineOff). ABS and lock-up pulses below override the base when wheels slip.

    // Thresholds and spans tuned from recorded telemetry, biased a little crisper than before:
    // real lock-up while braking sits around 0.10-0.26, wheelspin fans out to ~0.7.
    private const float LockupThreshold = 0.12f;
    private const float LockupSpan = 0.35f;
    private const float WheelspinThreshold = 0.12f;
    private const float WheelspinSpan = 0.45f;

    // Road texture comes straight from suspension velocity, not the surface label, so it never
    // lies: smooth tarmac (median ~0.19 m/s) stays below the deadzone and is silent, while every
    // bump/pebble that actually moves a wheel pushes through proportionally. Mapped to the
    // throttle trigger (otherwise idle) as a light, fast buzz.
    private const float SurfaceDeadzone = 0.35f;
    private const float SurfaceSpan = 1.6f;
    private const int SurfaceFrequency = 35;

    // Rev-limiter / shift flash: blink the lightbar off near the redline as a shift cue.
    private const float RevLimiterBlinkStart = 0.97f;
    private const int RevLimiterBlinkIntervalMs = 60;

    /// <summary>
    /// Active user settings. Program replaces this from config.json at startup; tests and the
    /// default build use the built-in defaults.
    /// </summary>
    public static DsxSettings Settings { get; set; } = new();

    public static DsxPacket Map(TelemetryFrame telemetry, long elapsedMilliseconds, float collisionImpact = 0f)
    {
        if (!telemetry.InRace || !telemetry.PlayerDriving)
        {
            return InactivePacket();
        }

        var settings = Settings;
        var instructions = new List<DsxInstruction>(5);
        instructions.Add(BrakeTrigger(telemetry, settings));
        instructions.Add(ThrottleTrigger(telemetry, settings));
        instructions.Add(Instruction(MicLed, 0,
            settings.TemperatureWarning ? MicMode(telemetry.WaterTemperatureCelsius) : 2));

        if (settings.Lightbar)
        {
            var impact = settings.CollisionFlash ? collisionImpact : 0f;
            var color = ApplyBrightness(EngineColor(telemetry, elapsedMilliseconds, impact), settings.LightbarBrightness);
            instructions.Add(Instruction(RgbUpdate, 0, color.R, color.G, color.B));
        }
        else
        {
            instructions.Add(Instruction(RgbUpdate, 0, 0, 0, 0));
        }

        if (settings.GearLeds)
        {
            var lights = GearLights(telemetry.Gear);
            instructions.Add(Instruction(PlayerLed, 0, lights[0], lights[1], lights[2], lights[3], lights[4]));
        }
        else
        {
            instructions.Add(Instruction(PlayerLed, 0, false, false, false, false, false));
        }
        return new DsxPacket(instructions);
    }

    public static DsxPacket Reset() => InactivePacket();

    private static DsxInstruction BrakeTrigger(TelemetryFrame telemetry, DsxSettings settings)
    {
        // Brake disabled entirely: leave the trigger free.
        if (!settings.Brake)
        {
            return Instruction(TriggerUpdate, 0, LeftTrigger, Normal, 0, 0, 0);
        }

        // ABS is an authoritative game signal; pulse fast while it intervenes.
        if (settings.Abs && telemetry.AbsActive)
        {
            return Instruction(TriggerUpdate, 0, LeftTrigger, AutomaticGun, 0,
                ClampForce(telemetry.Brake * 7), 10);
        }

        // Slip-based lock-up is only trustworthy while braking and actually rolling.
        if (settings.WheelLock &&
            telemetry.SpeedMetersPerSecond > MovingMetersPerSecond && telemetry.Brake > BrakeEngaged)
        {
            var lockup = telemetry.TireSlipRatios.Max(slip => MathF.Max(0, -slip));
            if (lockup > LockupThreshold)
            {
                return Instruction(TriggerUpdate, 0, LeftTrigger, AutomaticGun, 0,
                    Scale(lockup, LockupThreshold, LockupSpan), 30);
            }
        }

        // Firm base pedal at all times (muscle-car feel), harder with the engine off.
        return Instruction(TriggerUpdate, 0, LeftTrigger, Resistance, 0,
            telemetry.EngineRunning ? settings.BrakeForceRunning : settings.BrakeForceEngineOff);
    }

    private static DsxInstruction ThrottleTrigger(TelemetryFrame telemetry, DsxSettings settings)
    {
        if (!telemetry.EngineRunning || telemetry.Clutch > 0.1f)
        {
            return Instruction(TriggerUpdate, 0, RightTrigger, Normal, 0, 0, 0);
        }

        // TCS is authoritative: pulse whenever it actually cuts power.
        if (settings.TractionControl && telemetry.TcsActive)
        {
            var spin = DrivenWheelspin(telemetry);
            return Instruction(TriggerUpdate, 0, RightTrigger, AutomaticGun, 0,
                Math.Max(2, Scale(spin, WheelspinThreshold, WheelspinSpan)), 30);
        }

        // Slip-based wheelspin is a useful hint, including launch spin at 1-2 km/h. The parked
        // slip artifact (-1, and +1 on the rear-right) almost always occurs with no throttle,
        // so gating on applied throttle keeps real wheelspin while dropping the standstill noise.
        // No speed gate here on purpose: a 7 km/h gate would swallow launch wheelspin.
        if (settings.Wheelspin && telemetry.Throttle > ThrottleEngaged)
        {
            var spin = DrivenWheelspin(telemetry);
            if (spin > WheelspinThreshold)
            {
                return Instruction(TriggerUpdate, 0, RightTrigger, AutomaticGun, 0,
                    Scale(spin, WheelspinThreshold, WheelspinSpan), 30);
            }
        }

        // No wheelspin: let the road speak through the throttle. Driven purely by how much the
        // wheels are actually moving over the ground, so it conveys real bumps, not a surface tag.
        if (settings.SurfaceFeel)
        {
            var roughness = SurfaceRoughness(telemetry) * settings.SurfaceIntensity;
            if (roughness > SurfaceDeadzone)
            {
                return Instruction(TriggerUpdate, 0, RightTrigger, AutomaticGun, 0,
                    Scale(roughness, SurfaceDeadzone, SurfaceSpan), SurfaceFrequency);
            }
        }

        return Instruction(TriggerUpdate, 0, RightTrigger, Normal, 0, 0, 0);
    }

    private static float DrivenWheelspin(TelemetryFrame telemetry)
    {
        var driven = telemetry.Driveline switch
        {
            DrivelineType.FrontWheelDrive => telemetry.TireSlipRatios[..2],
            DrivelineType.RearWheelDrive => telemetry.TireSlipRatios[2..],
            _ => telemetry.TireSlipRatios
        };
        return driven.Max(slip => MathF.Max(0, slip));
    }

    // Peak absolute suspension velocity across the wheels: the sharpest single-wheel hit, i.e.
    // the pebble you just rolled over. Zero while essentially parked (no meaningful road feel).
    private static float SurfaceRoughness(TelemetryFrame telemetry)
    {
        var velocities = telemetry.SuspensionVelocities;
        if (velocities is null || velocities.Length == 0) return 0f;
        if (telemetry.SpeedMetersPerSecond <= MovingMetersPerSecond) return 0f;
        return velocities.Max(MathF.Abs);
    }

    private static (int R, int G, int B) EngineColor(TelemetryFrame telemetry, long timeMs, float collisionImpact)
    {
        var baseColor = BaseEngineColor(telemetry, timeMs);
        if (collisionImpact <= 0)
        {
            return baseColor;
        }

        // Brief white flash blended over the engine colour to mark an impact.
        var amount = Math.Clamp(collisionImpact, 0f, 1f);
        return (
            (int)(baseColor.R + (255 - baseColor.R) * amount),
            (int)(baseColor.G + (255 - baseColor.G) * amount),
            (int)(baseColor.B + (255 - baseColor.B) * amount));
    }

    private static (int R, int G, int B) ApplyBrightness((int R, int G, int B) color, float brightness)
    {
        if (brightness == 1.0f) return color;
        return (
            Math.Clamp((int)(color.R * brightness), 0, 255),
            Math.Clamp((int)(color.G * brightness), 0, 255),
            Math.Clamp((int)(color.B * brightness), 0, 255));
    }

    private static (int R, int G, int B) BaseEngineColor(TelemetryFrame telemetry, long timeMs)
    {
        if (!telemetry.EngineRunning || telemetry.EngineMisfiring || telemetry.EngineDamage >= 3)
        {
            var pulse = (Math.Sin(timeMs / 750d * Math.PI * 2) + 1) / 2;
            return ((int)(255 * pulse), 0, 0);
        }

        var limit = telemetry.EngineRpmMax > 0 ? telemetry.EngineRpmMax : telemetry.EngineRpmRedline;
        var rpm = limit > 0 ? Math.Clamp((float)telemetry.EngineRpm / limit, 0, 1) : 0;

        // Shift cue: flash the bar off/on near the redline.
        if (rpm >= RevLimiterBlinkStart && (timeMs / RevLimiterBlinkIntervalMs) % 2 == 1)
        {
            return (0, 0, 0);
        }

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

    private static int Scale(float value, float threshold, float span) =>
        Math.Clamp((int)MathF.Round((value - threshold) / span * 7f), 1, 7);

    private static float Lerp(float a, float b, float amount) => a + (b - a) * amount;
    private static DsxInstruction Instruction(int type, params object[] parameters) => new(type, parameters);

    // Idle/menu state: keep adaptive triggers neutral but leave a dim glow on the bar instead of
    // turning it fully off, so the lightbar never looks "dead" between races.
    private static DsxPacket InactivePacket() => new DsxPacket(
    [
        Instruction(TriggerUpdate, 0, LeftTrigger, Normal, 0, 0, 0),
        Instruction(TriggerUpdate, 0, RightTrigger, Normal, 0, 0, 0),
        Instruction(MicLed, 0, 2),
        Instruction(RgbUpdate, 0, 0, Settings.Lightbar ? 30 : 0, Settings.Lightbar ? 70 : 0),
        Instruction(PlayerLed, 0, false, false, false, false, false)
    ]);
}
