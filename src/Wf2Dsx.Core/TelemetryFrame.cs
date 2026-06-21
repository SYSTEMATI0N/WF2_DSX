namespace Wf2Dsx.Core;

public enum DrivelineType : byte
{
    FrontWheelDrive = 0,
    RearWheelDrive = 1,
    AllWheelDrive = 2
}

public sealed record TelemetryFrame(
    bool InRace,
    bool PlayerDriving,
    bool EngineRunning,
    bool EngineMisfiring,
    bool AbsActive,
    bool TcsActive,
    DrivelineType Driveline,
    int Gear,
    int EngineRpm,
    int EngineRpmMax,
    int EngineRpmRedline,
    float Throttle,
    float Brake,
    float Clutch,
    float WaterTemperatureCelsius,
    byte EngineDamage,
    byte GearboxDamage,
    float[] TireSlipRatios,
    int SessionTime = 0,
    int RaceTime = 0,
    float SpeedMetersPerSecond = 0,
    float EngineTorque = 0,
    float EnginePower = 0,
    float Handbrake = 0,
    float Steering = 0,
    float FfbForce = 0,
    byte Health = 0,
    int LastCollisionTime = 0,
    float[]? AccelerationLocal = null,
    float[]? TireSlipAngles = null,
    float[]? TireLoadsVertical = null,
    float[]? TireForcesLateral = null,
    float[]? TireForcesLongitudinal = null,
    float[]? SuspensionVelocities = null,
    float[]? SuspensionDisplacementsNormalized = null,
    byte[]? SurfaceTypes = null);
