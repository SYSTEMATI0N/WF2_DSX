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
