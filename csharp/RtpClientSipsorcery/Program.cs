using System.Net;
using System.Text;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

/// <summary>
/// Gateway HTTP URL for sending SDP offers
/// </summary>
const string GATEWAY_HTTP_URL = "http://127.0.0.1:8080/offer";

/// <summary>
/// H.264 payload type
/// </summary>
const int H264_PAYLOAD_TYPE = 96;

Console.WriteLine("RTP Client (SIPSorcery) starting...");
Console.WriteLine($"Gateway URL: {GATEWAY_HTTP_URL}");
Console.WriteLine();

// Create RTP session
var rtpSession = CreateRtpSession();

// Setup packet receive handler
SetupPacketHandler(rtpSession);

// Generate and send SDP offer
var localIp = IPAddress.Loopback;
var offerSdp = rtpSession.CreateOffer(localIp);
var offerSdpString = offerSdp.ToString();

Console.WriteLine("=== SDP Offer ===");
Console.WriteLine(offerSdpString);
Console.WriteLine();

// Send offer to gateway and get answer
var answerSdpString = await SendOfferToGateway(offerSdpString);

Console.WriteLine("=== SDP Answer ===");
Console.WriteLine(answerSdpString);
Console.WriteLine();

// Apply SDP answer
var answerSdp = SDP.ParseSDPDescription(answerSdpString);
var setResult = rtpSession.SetRemoteDescription(SdpType.answer, answerSdp);
Console.WriteLine($"SetRemoteDescription result: {setResult}");
Console.WriteLine();

// Start RTP session
await rtpSession.Start();

// Show session info
Console.WriteLine("RTP session started. Receiving packets...");
Console.WriteLine("Press Ctrl+C to exit.");
Console.WriteLine();

// Wait for packets (or Ctrl+C)
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // User pressed Ctrl+C
}

// Cleanup
rtpSession.Close("user exit");
Console.WriteLine("RTP session closed.");

return;

/// <summary>
/// Creates and configures RTP session with H.264 RecvOnly track
/// </summary>
/// <returns>Configured RTP session</returns>
RTPSession CreateRtpSession()
{
    var session = new RTPSession(false, false, false);

    var videoFormat = new SDPAudioVideoMediaFormat(
        SDPMediaTypesEnum.video,
        H264_PAYLOAD_TYPE,
        "H264",
        90000
    );

    var videoTrack = new MediaStreamTrack(
        SDPMediaTypesEnum.video,
        false,
        new List<SDPAudioVideoMediaFormat> { videoFormat },
        MediaStreamStatusEnum.RecvOnly
    );

    session.addTrack(videoTrack);
    return session;
}

/// <summary>
/// Sets up RTP packet receive handler
/// </summary>
/// <param name="session">RTP session</param>
void SetupPacketHandler(RTPSession session)
{
    var packetCount = 0;
    var lastReportTime = DateTime.UtcNow;
    var packetsSinceLastReport = 0;
    long bytesSinceLastReport = 0;

    session.OnRtpPacketReceived += (remoteEp, mediaType, rtpPacket) =>
    {
        if (mediaType != SDPMediaTypesEnum.video)
        {
            return;
        }

        packetCount++;
        packetsSinceLastReport++;
        bytesSinceLastReport += rtpPacket.Payload.Length;

        // First packet logging
        if (packetCount == 1)
        {
            Console.WriteLine(
                $"[FIRST RTP PACKET] From={remoteEp}, Media={mediaType}, "
                    + $"PT={rtpPacket.Header.PayloadType}, "
                    + $"Seq={rtpPacket.Header.SequenceNumber}, TS={rtpPacket.Header.Timestamp}, "
                    + $"Len={rtpPacket.Payload.Length}"
            );
        }

        var now = DateTime.UtcNow;
        var elapsed = (now - lastReportTime).TotalSeconds;

        if (elapsed >= 1.0)
        {
            var pps = packetsSinceLastReport / elapsed;
            var bps = (bytesSinceLastReport * 8) / elapsed; // bit/s
            var kbps = bps / 1000.0;

            Console.WriteLine(
                $"Total: {packetCount} packets | Rate: {pps:F1} pps | "
                    + $"Bitrate: {kbps:F1} kbps | "
                    + $"Last Seq: {rtpPacket.Header.SequenceNumber} | "
                    + $"Last TS: {rtpPacket.Header.Timestamp}"
            );

            lastReportTime = now;
            packetsSinceLastReport = 0;
            bytesSinceLastReport = 0;
        }
    };
}

/// <summary>
/// Sends SDP offer to gateway and receives SDP answer
/// </summary>
/// <param name="offerSdp">SDP offer string</param>
/// <returns>SDP answer string</returns>
async Task<string> SendOfferToGateway(string offerSdp)
{
    using var httpClient = new HttpClient();
    var content = new StringContent(offerSdp, Encoding.UTF8, "application/sdp");

    var response = await httpClient.PostAsync(GATEWAY_HTTP_URL, content);
    response.EnsureSuccessStatusCode();

    return await response.Content.ReadAsStringAsync();
}
