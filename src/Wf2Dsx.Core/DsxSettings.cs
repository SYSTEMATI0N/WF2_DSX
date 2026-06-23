namespace Wf2Dsx.Core;

/// <summary>
/// User-tunable settings loaded from config.json next to the executable. Every property has a
/// default that reproduces the built-in behaviour, so a missing or partial file is always safe.
/// </summary>
public sealed class DsxSettings
{
    // Network
    public int PinoPort { get; set; } = 23123;

    // Effect toggles
    public bool Brake { get; set; } = true;
    public bool Abs { get; set; } = true;
    public bool WheelLock { get; set; } = true;
    public bool Wheelspin { get; set; } = true;
    public bool TractionControl { get; set; } = true;
    public bool SurfaceFeel { get; set; } = true;
    public bool Lightbar { get; set; } = true;
    public bool CollisionFlash { get; set; } = true;
    public bool GearLeds { get; set; } = true;
    public bool TemperatureWarning { get; set; } = true;

    // Tuning (0-8 force for the pedal, multipliers default to 1.0)
    public int BrakeForceRunning { get; set; } = 4;
    public int BrakeForceEngineOff { get; set; } = 8;
    public float SurfaceIntensity { get; set; } = 1.0f;
    public float LightbarBrightness { get; set; } = 1.0f;
}
