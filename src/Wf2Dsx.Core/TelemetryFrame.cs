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
    float[] TireSlipRatios);
