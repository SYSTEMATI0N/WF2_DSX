using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wf2Dsx.Core;

public sealed record TelemetryConfigurationResult(
    IReadOnlyList<string> ConfigPaths,
    bool Changed,
    IReadOnlyList<string> Errors)
{
    public bool Found => ConfigPaths.Count > 0;
}

public static class TelemetryConfigurator
{
    public static TelemetryConfigurationResult EnsureEnabled(string documentsPath, int port)
    {
        var gameRoot = Path.Combine(documentsPath, "My Games", "Wreckfest 2");
        if (!Directory.Exists(gameRoot))
        {
            return new TelemetryConfigurationResult([], false, []);
        }

        var candidates = Directory.EnumerateDirectories(gameRoot)
            .Select(account => Path.Combine(account, "savegame", "telemetry", "config.json"))
            .Append(Path.Combine(gameRoot, "savegame", "telemetry", "config.json"))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var changed = false;
        var configured = new List<string>();
        var errors = new List<string>();
        foreach (var configPath in candidates)
        {
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject
                    ?? throw new JsonException("root is not an object");
                var udp = root["udp"] as JsonArray
                    ?? throw new JsonException("udp is not an array");
                var endpoint = udp.FirstOrDefault() as JsonObject
                    ?? throw new JsonException("udp endpoint is missing");

                var needsUpdate = endpoint["enabled"]?.ToString() != "1"
                    || endpoint["ip"]?.ToString() != "127.0.0.1"
                    || endpoint["port"]?.ToString() != port.ToString();
                if (needsUpdate)
                {
                    endpoint["enabled"] = 1;
                    endpoint["ip"] = "127.0.0.1";
                    endpoint["port"] = port.ToString();
                    WriteAtomically(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    changed = true;
                }
                configured.Add(configPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                errors.Add($"{configPath}: {exception.Message}");
            }
        }

        return new TelemetryConfigurationResult(configured, changed, errors);
    }

    private static void WriteAtomically(string path, string contents)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
