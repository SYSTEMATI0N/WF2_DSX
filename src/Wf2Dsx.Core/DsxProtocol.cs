using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wf2Dsx.Core;

public sealed record DsxPacket(
    [property: JsonPropertyName("instructions")] IReadOnlyList<DsxInstruction> Instructions);

public sealed record DsxInstruction(
    [property: JsonPropertyName("type")] int Type,
    [property: JsonPropertyName("parameters")] object[] Parameters);

public static class DsxJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false
    };

    public static string Serialize(DsxPacket packet) => JsonSerializer.Serialize(packet, Options);
}
