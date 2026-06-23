using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Wf2Dsx.Core;

const int dsxPort = 6969;
const int sendIntervalMilliseconds = 50;
const int telemetryTimeoutMilliseconds = 500;
const int collisionFlashMilliseconds = 250;

// Everything after --play is the game command line that Steam passes via %command%.
var playIndex = Array.FindIndex(args, argument => argument.Equals("--play", StringComparison.OrdinalIgnoreCase));
var ownArgs = playIndex >= 0 ? args[..playIndex] : args;
var gameCommand = playIndex >= 0 ? args[(playIndex + 1)..] : Array.Empty<string>();

var diagnosticMode = ownArgs.Any(argument => argument.Equals("--diagnostic", StringComparison.OrdinalIgnoreCase));
var watchProcessName = ReadWatchGame(ownArgs);
var hideConsole = gameCommand.Length > 0 ||
    ownArgs.Any(argument => argument.Equals("--hidden", StringComparison.OrdinalIgnoreCase));

// Mirror console output to a log file next to the exe so hidden sessions still leave a trace.
try
{
    var logWriter = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "wf2_dsx.log"), append: false)
    {
        AutoFlush = true
    };
    Console.SetOut(new TeeTextWriter(Console.Out, logWriter));
}
catch (Exception)
{
    // Logging is best effort; never block startup on it.
}

if (hideConsole) NativeMethods.HideConsoleWindow();

// Load user settings from config.json next to the exe (created with sensible defaults on first
// run). CLI --pino-port still wins so power users can override per launch.
var settings = ConfigFile.Load(Path.Combine(AppContext.BaseDirectory, "config.json"));
DsxMapper.Settings = settings;
var pinoPort = ReadPinoPort(ownArgs, settings.PinoPort);

var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
var telemetryConfiguration = TelemetryConfigurator.EnsureEnabled(documentsPath, pinoPort);
using var telemetryReceiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, pinoPort));
using var dsxSender = new UdpClient();
var dsxEndpoint = new IPEndPoint(IPAddress.Loopback, dsxPort);

// Sending UDP to a port nobody is listening on (DSX not started yet) makes Windows raise an
// ICMP "port unreachable", which would otherwise throw ConnectionReset on the next socket
// call. Disabling SIO_UDP_CONNRESET lets WF2_DSX run quietly and connect whenever DSX appears.
DisableUdpConnReset(telemetryReceiver);
DisableUdpConnReset(dsxSender);
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
};

if (OperatingSystem.IsWindows())
{
    try { Console.Title = "WF2_DSX - DualSense for Wreckfest 2"; } catch { /* headless */ }
}

Console.WriteLine("======================================================");
Console.WriteLine("   WF2_DSX  -  DualSense for Wreckfest 2");
Console.WriteLine("======================================================");
Console.WriteLine("   Keep this window open while you play.");
Console.WriteLine("   1) Start DSX   2) Start Wreckfest 2   3) Drive!");
Console.WriteLine("------------------------------------------------------");
if (telemetryConfiguration.Found)
{
    Console.WriteLine(telemetryConfiguration.Changed
        ? "[setup] Telemetry turned on. If the game is already running, restart it once."
        : "[setup] Telemetry is on.");
}
else
{
    Console.WriteLine("[setup] Wreckfest 2 settings not found yet.");
    Console.WriteLine("        Start the game once, close it, then run WF2_DSX again.");
}
foreach (var error in telemetryConfiguration.Errors)
    Console.WriteLine($"[setup] Note: {error}");
Console.WriteLine("[setup] Effects can be customised in config.json (next to this app).");

// Integrated launcher: start the game ourselves and tie our lifetime to it. Steam can then
// point its launch options straight at WF2_DSX.exe (no extra console window or .cmd wrapper).
Process? launchedGame = null;
if (gameCommand.Length > 0)
{
    try
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = gameCommand[0],
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(gameCommand[0]) ?? string.Empty
        };
        for (var i = 1; i < gameCommand.Length; i++) startInfo.ArgumentList.Add(gameCommand[i]);
        launchedGame = Process.Start(startInfo);
        Console.WriteLine($"Launched game: {gameCommand[0]}");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Failed to launch game: {exception.Message}");
    }
}

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
if (watchProcessName is not null)
    Console.WriteLine($"[info]  Will close automatically when {watchProcessName} closes.");
Console.WriteLine("[wait]  Waiting for Wreckfest 2... start a race to feel the triggers.");
Console.WriteLine("        (You can close this window any time to stop.)");

var clock = Stopwatch.StartNew();
long lastSend = -sendIntervalMilliseconds;
long lastPacket = clock.ElapsedMilliseconds;
long lastGameCheck = 0;
var gameSeen = false;
var connected = false;
var resetSent = false;
var dsxReachable = true;
var collisionMarker = int.MinValue;
long lastImpact = long.MinValue;

try
{
    await CancellationGuard.RunAsync(async cancellationToken =>
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Tie the bridge's lifetime to the game. If we launched it ourselves, watch that exact
            // process; otherwise fall back to watching by name (once seen running, exit when gone).
            // This is more reliable than a launcher killing us, since some games return early
            // through a loader.
            if (launchedGame is not null)
            {
                if (launchedGame.HasExited)
                {
                    Console.WriteLine("Game exited; shutting down.");
                    break;
                }
            }
            else if (watchProcessName is not null && clock.ElapsedMilliseconds - lastGameCheck >= 1000)
            {
                lastGameCheck = clock.ElapsedMilliseconds;
                if (IsProcessRunning(watchProcessName))
                {
                    gameSeen = true;
                }
                else if (gameSeen)
                {
                    Console.WriteLine($"{watchProcessName} closed; shutting down.");
                    break;
                }
            }

            using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
                    Console.WriteLine("[ok]    Connected - DualSense effects are active.");
                }

                // LastCollisionTime increases on every impact within a session and resets
                // (decreases) between sessions, so only a strictly larger value is a new hit.
                if (telemetry.InRace)
                {
                    if (collisionMarker != int.MinValue && telemetry.LastCollisionTime > collisionMarker)
                        lastImpact = clock.ElapsedMilliseconds;
                    collisionMarker = telemetry.LastCollisionTime;
                }
                else
                {
                    collisionMarker = int.MinValue;
                }

                if (clock.ElapsedMilliseconds - lastSend < sendIntervalMilliseconds) continue;
                var collisionImpact = lastImpact == long.MinValue
                    ? 0f
                    : Math.Clamp(1f - (clock.ElapsedMilliseconds - lastImpact) / (float)collisionFlashMilliseconds, 0f, 1f);
                await SendAsync(DsxMapper.Map(telemetry, clock.ElapsedMilliseconds, collisionImpact), cancellationToken);
                lastSend = clock.ElapsedMilliseconds;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!resetSent && clock.ElapsedMilliseconds - lastPacket >= telemetryTimeoutMilliseconds)
                {
                    await SendAsync(DsxMapper.Reset(), cancellationToken);
                    resetSent = true;
                    if (connected) Console.WriteLine("[wait]  Race ended or game closed - effects paused.");
                    connected = false;
                }
            }
        }
    }, stopping.Token);
}
finally
{
    diagnosticWriter?.Dispose();
    launchedGame?.Dispose();
    try
    {
        await SendAsync(DsxMapper.Reset(), CancellationToken.None);
    }
    catch (SocketException) when (stopping.IsCancellationRequested)
    {
        // DSX may already be closed during an otherwise clean user shutdown.
    }
}
Console.WriteLine("[exit]  WF2_DSX stopped. See you on the track!");
Console.Out.Flush();

async Task SendAsync(DsxPacket packet, CancellationToken cancellationToken)
{
    var bytes = Encoding.UTF8.GetBytes(DsxJson.Serialize(packet));
    try
    {
        await dsxSender.SendAsync(bytes, dsxEndpoint, cancellationToken);
        if (!dsxReachable)
        {
            dsxReachable = true;
            Console.WriteLine("[ok]    DSX connected.");
        }
    }
    catch (SocketException) when (!cancellationToken.IsCancellationRequested)
    {
        // DSX is not running yet. Keep the bridge alive and retry on the next tick.
        if (dsxReachable)
        {
            dsxReachable = false;
            Console.WriteLine("[wait]  Waiting for DSX - open the DSX app.");
        }
    }
}

static void DisableUdpConnReset(UdpClient client)
{
    if (!OperatingSystem.IsWindows()) return;
    const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
    try
    {
        client.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
    }
    catch (SocketException)
    {
        // Best effort: if the control code is unavailable the send path still swallows resets.
    }
}

static string? ReadWatchGame(string[] arguments)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!arguments[index].Equals("--watch-game", StringComparison.OrdinalIgnoreCase)) continue;

        var name = "Wreckfest2";
        if (index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--"))
            name = arguments[index + 1];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }
    return null;
}

static bool IsProcessRunning(string name)
{
    try
    {
        var processes = Process.GetProcessesByName(name);
        var running = processes.Length > 0;
        foreach (var process in processes) process.Dispose();
        return running;
    }
    catch
    {
        return false;
    }
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

static class ConfigFile
{
    public static DsxSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, Template);
                return new DsxSettings();
            }

            var options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<DsxSettings>(File.ReadAllText(path), options) ?? new DsxSettings();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[setup] config.json could not be read ({exception.Message}); using defaults.");
            return new DsxSettings();
        }
    }

    // Written verbatim on first run. Comments are allowed because the loader skips them.
    private const string Template = """
        {
          // WF2_DSX settings. Edit a value, save, and restart WF2_DSX.
          // Delete this file to reset everything back to defaults.

          // Wreckfest 2 telemetry port (leave at 23123 unless you changed the game's config).
          "pinoPort": 23123,

          // Turn individual effects on or off (true / false):
          "brake": true,              // firm brake-pedal resistance
          "abs": true,               // fast pulse while ABS works
          "wheelLock": true,         // pulse when the wheels lock under braking
          "wheelspin": true,         // pulse on wheelspin
          "tractionControl": true,   // pulse while traction control cuts power
          "surfaceFeel": true,       // buzz the throttle over bumps and gravel
          "lightbar": true,          // RPM colour + warnings on the light bar
          "collisionFlash": true,    // white flash on impacts (needs lightbar)
          "gearLeds": true,          // show the current gear on the player LEDs
          "temperatureWarning": true,// mic LED warns on engine overheat

          // Fine tuning:
          "brakeForceRunning": 4,    // brake stiffness, engine on   (0-8)
          "brakeForceEngineOff": 8,  // brake stiffness, engine dead (0-8)
          "surfaceIntensity": 1.0,   // road-texture strength (e.g. 0.5 softer, 2.0 stronger)
          "lightbarBrightness": 1.0  // light bar brightness (0.0 - 1.0)
        }
        """;
}

sealed class TeeTextWriter(TextWriter primary, TextWriter secondary) : TextWriter
{
    public override Encoding Encoding => primary.Encoding;
    public override void Write(char value) { primary.Write(value); secondary.Write(value); }
    public override void Write(string? value) { primary.Write(value); secondary.Write(value); }
    public override void WriteLine(string? value) { primary.WriteLine(value); secondary.WriteLine(value); }
    public override void Flush() { primary.Flush(); secondary.Flush(); }
}

static class NativeMethods
{
    private const int SW_HIDE = 0;

    public static void HideConsoleWindow()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero) ShowWindow(handle, SW_HIDE);
        }
        catch
        {
            // No console to hide (e.g. already detached); nothing to do.
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
