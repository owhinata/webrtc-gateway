using System.Net;
using System.Net.Sockets;
using System.Text;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

/// <summary>
/// Multicast address for receiving RTP stream from VLC
/// </summary>
const string MULTICAST_ADDRESS = "239.0.0.1";

/// <summary>
/// Multicast port for receiving RTP stream
/// </summary>
const int MULTICAST_PORT = 5004;

/// <summary>
/// HTTP server port for receiving SDP offers from clients
/// </summary>
const int GATEWAY_HTTP_PORT = 8080;

/// <summary>
/// Formal port number written in SDP Answer (not used for actual sending)
/// </summary>
const int GATEWAY_SDP_VIDEO_PORT = 5006;

/// <summary>
/// H.264 payload type
/// </summary>
const int H264_PAYLOAD_TYPE = 96;

/// <summary>
/// List of client sessions
/// </summary>
var clients = new List<ClientSession>();

/// <summary>
/// Lock object for client sessions list
/// </summary>
var clientsLock = new object();

/// <summary>
/// Cancellation token source for graceful shutdown
/// </summary>
var cts = new CancellationTokenSource();

Console.WriteLine("UDP RTP Gateway starting...");
Console.WriteLine($"Multicast: {MULTICAST_ADDRESS}:{MULTICAST_PORT}");
Console.WriteLine($"HTTP Server: http://+:{GATEWAY_HTTP_PORT}/");

// Handle Ctrl+C for graceful shutdown
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\nShutting down...");
    cts.Cancel();
};

try
{
    // Initialize UDP clients for multicast receive and unicast send
    var multicastClient = CreateMulticastClient();
    // Bind send client to SDP video port (5006) to match SDP Answer
    var sendClient = new UdpClient(GATEWAY_SDP_VIDEO_PORT);
    Console.WriteLine($"Send client bound to port {GATEWAY_SDP_VIDEO_PORT}");

    // Start HTTP server
    var httpTask = Task.Run(() => RunHttpServer(cts.Token), cts.Token);

    // Start multicast receiver
    var multicastTask = Task.Run(
        () => RunMulticastReceiver(multicastClient, sendClient, cts.Token),
        cts.Token
    );

    Console.WriteLine("Press Ctrl+C to exit.");

    // Wait for cancellation
    await Task.Delay(Timeout.Infinite, cts.Token);

    // Cleanup
    multicastClient.Close();
    sendClient.Close();
}
catch (OperationCanceledException)
{
    Console.WriteLine("Gateway stopped.");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

return;

/// <summary>
/// Creates and configures UDP client for multicast reception
/// </summary>
/// <returns>Configured UDP client</returns>
UdpClient CreateMulticastClient()
{
    var client = new UdpClient(AddressFamily.InterNetwork);
    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    client.Client.Bind(new IPEndPoint(IPAddress.Any, MULTICAST_PORT));

    var multicastAddress = IPAddress.Parse(MULTICAST_ADDRESS);
    client.JoinMulticastGroup(multicastAddress);

    Console.WriteLine($"Joined multicast group {MULTICAST_ADDRESS}:{MULTICAST_PORT}");
    return client;
}

/// <summary>
/// Runs multicast receiver and relays RTP packets to registered clients
/// </summary>
/// <param name="multicastClient">UDP client for receiving multicast</param>
/// <param name="sendClient">UDP client for sending to clients</param>
/// <param name="ct">Cancellation token</param>
async Task RunMulticastReceiver(
    UdpClient multicastClient,
    UdpClient sendClient,
    CancellationToken ct
)
{
    var packetCount = 0;
    Console.WriteLine("Multicast receiver started");

    try
    {
        while (!ct.IsCancellationRequested)
        {
            var result = await multicastClient.ReceiveAsync(ct);
            packetCount++;

            if (packetCount % 100 == 0)
            {
                Console.WriteLine($"Received {packetCount} RTP packets from multicast");
            }

            // Relay to all registered clients
            await RelayRtpPacket(result.Buffer, sendClient);
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"Multicast receiver stopped. Total packets received: {packetCount}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Multicast receiver error: {ex.Message}");
    }
}

/// <summary>
/// Relays RTP packet to all registered clients
/// </summary>
/// <param name="rtpPacket">RTP packet data</param>
/// <param name="sendClient">UDP client for sending</param>
async Task RelayRtpPacket(byte[] rtpPacket, UdpClient sendClient)
{
    List<ClientSession> clientsSnapshot;
    lock (clientsLock)
    {
        clientsSnapshot = clients.ToList();
    }

    foreach (var client in clientsSnapshot)
    {
        try
        {
            await sendClient.SendAsync(rtpPacket, rtpPacket.Length, client.RtpEndPoint);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send to {client}: {ex.Message}");
        }
    }
}

/// <summary>
/// Runs HTTP server to receive SDP offers from clients
/// </summary>
/// <param name="ct">Cancellation token</param>
async Task RunHttpServer(CancellationToken ct)
{
    var listener = new HttpListener();
    listener.Prefixes.Add($"http://+:{GATEWAY_HTTP_PORT}/");

    try
    {
        listener.Start();
        Console.WriteLine($"HTTP server started on port {GATEWAY_HTTP_PORT}");

        while (!ct.IsCancellationRequested)
        {
            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, ct));

            if (completedTask != contextTask)
                break;

            var context = await contextTask;
            _ = Task.Run(() => HandleHttpRequest(context), ct);
        }
    }
    catch (Exception) when (ct.IsCancellationRequested)
    {
        // Expected when shutting down
    }
    catch (Exception ex)
    {
        Console.WriteLine($"HTTP server error: {ex.Message}");
    }
    finally
    {
        listener.Stop();
        Console.WriteLine("HTTP server stopped");
    }
}

/// <summary>
/// Handles individual HTTP requests
/// </summary>
/// <param name="context">HTTP listener context</param>
async Task HandleHttpRequest(HttpListenerContext context)
{
    var request = context.Request;
    var response = context.Response;

    try
    {
        if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/offer")
        {
            await HandleOfferRequest(request, response);
        }
        else
        {
            response.StatusCode = 404;
            var errorMsg = Encoding.UTF8.GetBytes("Not Found");
            await response.OutputStream.WriteAsync(errorMsg, 0, errorMsg.Length);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error handling request: {ex.Message}");
        response.StatusCode = 500;
    }
    finally
    {
        response.Close();
    }
}

/// <summary>
/// Handles SDP offer request from client
/// </summary>
/// <param name="request">HTTP request</param>
/// <param name="response">HTTP response</param>
async Task HandleOfferRequest(HttpListenerRequest request, HttpListenerResponse response)
{
    // Read SDP offer from request body
    using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
    var offerSdpString = await reader.ReadToEndAsync();

    Console.WriteLine("=== Received SDP Offer ===");
    Console.WriteLine(offerSdpString);

    // Parse SDP offer and register client
    var offerSdp = SDP.ParseSDPDescription(offerSdpString);
    var session = RegisterClient(offerSdp);

    Console.WriteLine($"Client registered: {session}");

    // Create and send SDP answer
    var answerSdp = CreateAnswerSdp();
    var answerSdpString = answerSdp.ToString();

    Console.WriteLine("=== Sending SDP Answer ===");
    Console.WriteLine(answerSdpString);

    response.ContentType = "application/sdp";
    var buffer = Encoding.UTF8.GetBytes(answerSdpString);
    response.ContentLength64 = buffer.Length;
    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    response.StatusCode = 200;
}

/// <summary>
/// Registers a client session from SDP offer
/// </summary>
/// <param name="offerSdp">SDP offer</param>
/// <returns>Created client session</returns>
ClientSession RegisterClient(SDP offerSdp)
{
    var clientIp = IPAddress.Parse(offerSdp.Connection.ConnectionAddress);
    var clientPort = offerSdp.Media[0].Port;
    var clientEndPoint = new IPEndPoint(clientIp, clientPort);

    var session = new ClientSession
    {
        SessionId = Guid.NewGuid().ToString().Substring(0, 8),
        RtpEndPoint = clientEndPoint,
        LastSeen = DateTime.UtcNow,
    };

    lock (clientsLock)
    {
        clients.Add(session);
    }

    return session;
}

/// <summary>
/// Creates SDP answer with sendonly video stream
/// </summary>
/// <returns>SDP answer</returns>
SDP CreateAnswerSdp()
{
    var sdp = new SDP
    {
        Version = 0,
        SessionId = new Random().Next().ToString(),
        Username = "-",
        SessionName = "Gateway",
        Connection = new SDPConnectionInformation(IPAddress.Any),
        Timing = "0 0",
    };

    var videoFormat = new SDPAudioVideoMediaFormat(
        SDPMediaTypesEnum.video,
        H264_PAYLOAD_TYPE,
        "H264",
        90000
    );

    var videoMedia = new SDPMediaAnnouncement
    {
        Media = SDPMediaTypesEnum.video,
        Port = GATEWAY_SDP_VIDEO_PORT,
        Transport = "RTP/AVP",
        MediaFormats = new Dictionary<int, SDPAudioVideoMediaFormat>
        {
            { H264_PAYLOAD_TYPE, videoFormat },
        },
        MediaStreamStatus = MediaStreamStatusEnum.SendOnly,
    };

    sdp.Media.Add(videoMedia);

    return sdp;
}

/// <summary>
/// Client session information
/// </summary>
class ClientSession
{
    /// <summary>
    /// Client's RTP receiving endpoint (IP + Port)
    /// </summary>
    public IPEndPoint RtpEndPoint { get; set; } = null!;

    /// <summary>
    /// Last time the offer was received
    /// </summary>
    public DateTime LastSeen { get; set; }

    /// <summary>
    /// Session ID for logging
    /// </summary>
    public string SessionId { get; set; } = null!;

    /// <summary>
    /// Returns string representation of session information
    /// </summary>
    public override string ToString() => $"{SessionId}: {RtpEndPoint}, LastSeen={LastSeen:o}";
}
