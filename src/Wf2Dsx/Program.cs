using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Wf2Dsx.Core;

const int defaultPinoPort = 23123;
const int dsxPort = 6969;
const int sendIntervalMilliseconds = 50;
const int telemetryTimeoutMilliseconds = 500;

var pinoPort = ReadPinoPort(args, defaultPinoPort);
var diagnosticMode = args.Any(argument => argument.Equals("--diagnostic", StringComparison.OrdinalIgnoreCase));
var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
var telemetryConfiguration = TelemetryConfigurator.EnsureEnabled(documentsPath, pinoPort);
using var telemetryReceiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, pinoPort));
using var dsxSender = new UdpClient();
var dsxEndpoint = new IPEndPoint(IPAddress.Loopback, dsxPort);
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
};

Console.WriteLine("WF2_DSX");
Console.WriteLine($"Wreckfest 2 Pino: UDP {pinoPort}");
Console.WriteLine($"DSX: 127.0.0.1:{dsxPort} (fixed)");
if (telemetryConfiguration.Found)
{
    foreach (var path in telemetryConfiguration.ConfigPaths)
        Console.WriteLine($"Telemetry config: {path}");
    Console.WriteLine(telemetryConfiguration.Changed
        ? "Telemetry was enabled automatically. Restart Wreckfest 2 if it is already running."
        : "Telemetry config is enabled.");
}
else
{
    Console.WriteLine($"Telemetry config not found below: {documentsPath}");
    Console.WriteLine("Launch and exit Wreckfest 2 once, then restart WF2_DSX.");
}
foreach (var error in telemetryConfiguration.Errors)
    Console.WriteLine($"Telemetry config error: {error}");
StreamWriter? diagnosticWriter = null;
var diagnosticRows = 0;
if (diagnosticMode)
{
    try
    {
        var diagnosticsDirectory = Path.Combine(AppContext.BaseDirectory, "diagnostics");
        Directory.CreateDirectory(diagnosticsDirectory);
        var diagnosticPath = Path.Combine(diagnosticsDirectory, $"wf2_telemetry_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        diagnosticWriter = new StreamWriter(diagnosticPath, false, new UTF8Encoding(false), 65_536);
        diagnosticWriter.WriteLine(DiagnosticCsv.Header);
        diagnosticWriter.Flush();
        Console.WriteLine($"DIAGNOSTIC MODE: {diagnosticPath}");
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        Console.WriteLine($"Diagnostic mode unavailable: {exception.Message}");
    }
}
Console.WriteLine("Waiting for Pino Main telemetry. Press Ctrl+C to stop.");

var clock = Stopwatch.StartNew();
long lastSend = -sendIntervalMilliseconds;
long lastPacket = clock.ElapsedMilliseconds;
var connected = false;
var resetSent = false;

try
{
    while (!stopping.IsCancellationRequested)
    {
        using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
        receiveTimeout.CancelAfter(telemetryTimeoutMilliseconds);
        try
        {
            var datagram = await telemetryReceiver.ReceiveAsync(receiveTimeout.Token);
            if (!PinoMainDecoder.TryDecode(datagram.Buffer, out var telemetry)) continue;

            if (diagnosticWriter is not null)
            {
                diagnosticWriter.WriteLine(DiagnosticCsv.FormatRow(telemetry, clock.ElapsedMilliseconds));
                diagnosticRows++;
                if (diagnosticRows % 60 == 0) diagnosticWriter.Flush();
                if (diagnosticRows % 120 == 0)
                {
                    var peakSlip = telemetry.TireSlipRatios.Max(slip => MathF.Abs(slip));
                    var acceleration = telemetry.AccelerationLocal ?? [0, 0, 0];
                    var accelerationMagnitude = MathF.Sqrt(acceleration.Sum(axis => axis * axis));
                    Console.WriteLine($"DIAG rows={diagnosticRows} speed={telemetry.SpeedMetersPerSecond * 3.6f:F0}km/h " +
                        $"rpm={telemetry.EngineRpm} slip={peakSlip:F2} accel={accelerationMagnitude:F1}m/s² " +
                        $"health={telemetry.Health}");
                }
            }

            lastPacket = clock.ElapsedMilliseconds;
            resetSent = false;
            if (!connected)
            {
                connected = true;
                Console.WriteLine("Telemetry connected.");
            }

            if (clock.ElapsedMilliseconds - lastSend < sendIntervalMilliseconds) continue;
            await SendAsync(DsxMapper.Map(telemetry, clock.ElapsedMilliseconds), stopping.Token);
            lastSend = clock.ElapsedMilliseconds;
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
            if (!resetSent && clock.ElapsedMilliseconds - lastPacket >= telemetryTimeoutMilliseconds)
            {
                await SendAsync(DsxMapper.Reset(), stopping.Token);
                resetSent = true;
                if (connected) Console.WriteLine("Telemetry lost; controller effects reset.");
                connected = false;
            }
        }
    }
}
finally
{
    diagnosticWriter?.Dispose();
    await SendAsync(DsxMapper.Reset(), CancellationToken.None);
}

async Task SendAsync(DsxPacket packet, CancellationToken cancellationToken)
{
    var bytes = Encoding.UTF8.GetBytes(DsxJson.Serialize(packet));
    await dsxSender.SendAsync(bytes, dsxEndpoint, cancellationToken);
}

static int ReadPinoPort(string[] arguments, int fallback)
{
    for (var index = 0; index < arguments.Length - 1; index++)
    {
        if (arguments[index] == "--pino-port" &&
            int.TryParse(arguments[index + 1], out var port) &&
            port is > 0 and <= 65535)
        {
            return port;
        }
    }
    return fallback;
}
