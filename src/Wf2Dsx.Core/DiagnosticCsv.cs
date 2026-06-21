using System.Globalization;

namespace Wf2Dsx.Core;

public static class DiagnosticCsv
{
    private static readonly string[] Wheels = ["fl", "fr", "rl", "rr"];

    public static readonly string Header = BuildHeader();

    public static string FormatRow(TelemetryFrame telemetry, long elapsedMilliseconds)
    {
        var fields = new List<string>(59)
        {
            elapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
            telemetry.SessionTime.ToString(CultureInfo.InvariantCulture),
            telemetry.RaceTime.ToString(CultureInfo.InvariantCulture),
            Number(telemetry.SpeedMetersPerSecond * 3.6f),
            telemetry.Gear.ToString(CultureInfo.InvariantCulture),
            telemetry.EngineRpm.ToString(CultureInfo.InvariantCulture),
            telemetry.EngineRpmMax.ToString(CultureInfo.InvariantCulture),
            telemetry.EngineRpmRedline.ToString(CultureInfo.InvariantCulture),
            Number(telemetry.EngineTorque),
            Number(telemetry.EnginePower),
            Number(telemetry.Throttle),
            Number(telemetry.Brake),
            Number(telemetry.Clutch),
            Number(telemetry.Handbrake),
            Number(telemetry.Steering),
            telemetry.AbsActive ? "1" : "0",
            telemetry.TcsActive ? "1" : "0",
            Number(telemetry.FfbForce),
            telemetry.Health.ToString(CultureInfo.InvariantCulture),
            telemetry.LastCollisionTime.ToString(CultureInfo.InvariantCulture),
            telemetry.EngineRunning ? "1" : "0",
            telemetry.EngineDamage.ToString(CultureInfo.InvariantCulture),
            telemetry.GearboxDamage.ToString(CultureInfo.InvariantCulture),
            Number(telemetry.WaterTemperatureCelsius),
            Number(ValueAt(telemetry.AccelerationLocal, 0)),
            Number(ValueAt(telemetry.AccelerationLocal, 1)),
            Number(ValueAt(telemetry.AccelerationLocal, 2))
        };

        for (var wheel = 0; wheel < Wheels.Length; wheel++)
        {
            fields.Add(Number(ValueAt(telemetry.TireSlipRatios, wheel)));
            fields.Add(Number(ValueAt(telemetry.TireSlipAngles, wheel)));
            fields.Add(Number(ValueAt(telemetry.TireLoadsVertical, wheel)));
            fields.Add(Number(ValueAt(telemetry.TireForcesLateral, wheel)));
            fields.Add(Number(ValueAt(telemetry.TireForcesLongitudinal, wheel)));
            fields.Add(Number(ValueAt(telemetry.SuspensionVelocities, wheel)));
            fields.Add(Number(ValueAt(telemetry.SuspensionDisplacementsNormalized, wheel)));
            fields.Add(ValueAt(telemetry.SurfaceTypes, wheel).ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(',', fields);
    }

    private static string BuildHeader()
    {
        var fields = new List<string>
        {
            "elapsed_ms", "session_ms", "race_ms", "speed_kmh", "gear", "rpm", "rpm_max", "rpm_redline",
            "engine_torque_nm", "engine_power_w", "throttle", "brake", "clutch", "handbrake", "steering",
            "abs", "tcs", "ffb", "health", "collision_ms", "engine_running", "engine_damage", "gearbox_damage",
            "water_c", "accel_x", "accel_y", "accel_z"
        };
        foreach (var wheel in Wheels)
        {
            fields.AddRange([
                $"{wheel}_slip_ratio", $"{wheel}_slip_angle", $"{wheel}_load_n",
                $"{wheel}_force_lat_n", $"{wheel}_force_long_n", $"{wheel}_susp_vel_mps",
                $"{wheel}_susp_norm", $"{wheel}_surface"
            ]);
        }
        return string.Join(',', fields);
    }

    private static string Number(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static float ValueAt(float[]? values, int index) => values is { Length: > 0 } && index < values.Length ? values[index] : 0;
    private static byte ValueAt(byte[]? values, int index) => values is { Length: > 0 } && index < values.Length ? values[index] : (byte)0;
}
