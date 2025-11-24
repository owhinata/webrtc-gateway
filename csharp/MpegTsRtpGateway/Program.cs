using System.Net;
using System.Net.Sockets;
using SIPSorcery.Net;

namespace MpegTsRtpGateway;

/// <summary>
/// MPEG-TS to H.264/RTP Gateway
/// Receives MPEG-TS over UDP multicast and converts to H.264 RTP unicast
/// </summary>
class Program
{
    // TS Multicast input configuration
    private const string TS_MULTICAST_ADDRESS = "239.0.0.1";
    private const int TS_MULTICAST_PORT = 5004;

    // Gateway HTTP server configuration
    private const int GATEWAY_HTTP_PORT = 8080;

    // SDP configuration
    private const int GATEWAY_SDP_VIDEO_PORT = 5006;
    private const int H264_PAYLOAD_TYPE = 96;

    // RTP configuration
    private const uint RTP_SSRC = 0x12345678;

    // Client session management
    private static readonly List<ClientSession> _clients = new();
    private static readonly object _clientsLock = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== MpegTsRtpGateway starting ===");
        Console.WriteLine($"TS Input: udp://{TS_MULTICAST_ADDRESS}:{TS_MULTICAST_PORT}");
        Console.WriteLine($"HTTP Server: http://localhost:{GATEWAY_HTTP_PORT}/offer");
        Console.WriteLine();

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        // UDP client for sending RTP packets to clients (bind to port 5006)
        using var sendClient = new UdpClient(GATEWAY_SDP_VIDEO_PORT);

        // Build the TS → PES → H.264 → RTP pipeline
        var tsDemuxer = new TsDemuxer();
        PesAssembler? pesAssembler = null;
        var nalParser = new H264NalParser();
        var rtpPacker = new H264RtpPacker(RTP_SSRC);

        // TsDemuxer → PesAssembler connection
        tsDemuxer.OnPesPacketFragment += (pid, tsPacket) =>
        {
            if (pesAssembler == null)
            {
                pesAssembler = new PesAssembler(pid);
                pesAssembler.OnPesCompleted += (pesPayload, pts90k) =>
                {
                    var nalus = nalParser.ParseAnnexBNalus(pesPayload).ToList();
                    if (nalus.Count > 0)
                    {
                        // Debug: Log NAL types in this access unit
                        var nalTypes = nalus
                            .Select(n => n.Length > 0 ? (n[0] & 0x1F) : -1)
                            .ToList();
                        var nalTypeNames = nalTypes
                            .Select(t =>
                                t switch
                                {
                                    1 => "Non-IDR",
                                    5 => "IDR",
                                    6 => "SEI",
                                    7 => "SPS",
                                    8 => "PPS",
                                    9 => "AUD",
                                    _ => $"Type{t}",
                                }
                            )
                            .ToList();
                        Console.WriteLine(
                            $"[AU] PTS={pts90k}, NALs={nalus.Count}: {string.Join(", ", nalTypeNames)}"
                        );

                        rtpPacker.SendAccessUnit(nalus, pts90k);
                    }
                };
            }

            pesAssembler.ProcessTsPacket(tsPacket);
        };

        // RTP packet distribution to clients
        rtpPacker.OnRtpPacketReady += (rtpBytes) =>
        {
            List<ClientSession> clientsSnapshot;
            lock (_clientsLock)
            {
                clientsSnapshot = _clients.ToList();
            }

            foreach (var client in clientsSnapshot)
            {
                try
                {
                    sendClient.SendAsync(rtpBytes, rtpBytes.Length, client.RtpEndPoint).Wait();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Send error to {client}: {ex.Message}");
                }
            }

            return Task.CompletedTask;
        };

        // Start HTTP server for SDP Offer/Answer
        var httpTask = RunHttpServerAsync(cts.Token);

        // Start TS multicast receiver
        var tsTask = RunTsReceiveLoopAsync(tsDemuxer, cts.Token);

        Console.WriteLine("Gateway is running. Press Ctrl+C to exit.");
        Console.WriteLine();

        try
        {
            await Task.WhenAll(httpTask, tsTask);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        Console.WriteLine("=== MpegTsRtpGateway stopped ===");
    }

    /// <summary>
    /// HTTP server for handling SDP Offer/Answer exchange
    /// </summary>
    static async Task RunHttpServerAsync(CancellationToken ct)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://*:{GATEWAY_HTTP_PORT}/");
        listener.Start();

        Console.WriteLine($"HTTP server listening on port {GATEWAY_HTTP_PORT}");

        // Register cancellation to stop the listener
        ct.Register(() =>
        {
            try
            {
                listener.Stop();
            }
            catch
            {
                // Ignore errors during shutdown
            }
        });

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    _ = Task.Run(() => HandleHttpRequestAsync(ctx), ct);
                }
                catch (HttpListenerException)
                {
                    // Listener stopped or cancelled
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Listener was disposed
                    break;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Console.WriteLine($"HTTP server error: {ex.Message}");
                }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Normal shutdown, ignore
        }
        finally
        {
            try
            {
                listener.Close();
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }
    }

    /// <summary>
    /// Handle HTTP request for SDP Offer/Answer
    /// </summary>
    static async Task HandleHttpRequestAsync(HttpListenerContext ctx)
    {
        try
        {
            var request = ctx.Request;
            var response = ctx.Response;

            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/offer")
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var offerSdpText = await reader.ReadToEndAsync();

                Console.WriteLine("Received SDP Offer:");
                Console.WriteLine(offerSdpText);
                Console.WriteLine();

                // Parse SDP Offer
                var offer = SDP.ParseSDPDescription(offerSdpText);

                // Register client from offer
                var clientRemoteEp = request.RemoteEndPoint;
                RegisterClientFromOffer(offer, clientRemoteEp);

                // Create SDP Answer
                var answer = CreateAnswerSdp();
                var answerSdpText = answer.ToString();

                Console.WriteLine("Sending SDP Answer:");
                Console.WriteLine(answerSdpText);
                Console.WriteLine();

                // Send response
                response.ContentType = "application/sdp";
                response.StatusCode = 200;
                var buffer = System.Text.Encoding.UTF8.GetBytes(answerSdpText);
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
            }
            else
            {
                response.StatusCode = 404;
                response.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HTTP request error: {ex.Message}");
            try
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.Close();
            }
            catch { }
        }
    }

    /// <summary>
    /// Register client from SDP Offer
    /// </summary>
    static void RegisterClientFromOffer(SDP offer, IPEndPoint httpRemoteEndPoint)
    {
        // Get connection address from SDP or use HTTP remote address
        var connAddr = offer.Connection?.ConnectionAddress ?? httpRemoteEndPoint.Address.ToString();

        // Find video media
        var videoMedia = offer.Media.FirstOrDefault(m => m.Media == SDPMediaTypesEnum.video);
        if (videoMedia == null)
        {
            Console.WriteLine("Warning: No video media found in SDP Offer");
            return;
        }

        int clientRtpPort = videoMedia.Port;

        var clientEp = new IPEndPoint(IPAddress.Parse(connAddr), clientRtpPort);

        var session = new ClientSession
        {
            SessionId = Guid.NewGuid().ToString(),
            RtpEndPoint = clientEp,
            LastSeen = DateTime.UtcNow,
        };

        lock (_clientsLock)
        {
            // For simplicity, support single client (clear before adding)
            _clients.Clear();
            _clients.Add(session);
        }

        Console.WriteLine($"Registered client: {session}");
    }

    /// <summary>
    /// Create SDP Answer for H.264 video
    /// </summary>
    static SDP CreateAnswerSdp()
    {
        var sdp = new SDP
        {
            Version = 0,
            SessionId = new Random().Next().ToString(),
            Username = "-",
            SessionName = "MpegTsRtpGateway",
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
    /// Receive MPEG-TS packets from UDP multicast
    /// </summary>
    static async Task RunTsReceiveLoopAsync(TsDemuxer demuxer, CancellationToken ct)
    {
        using var client = new UdpClient();
        client.Client.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress,
            true
        );
        client.Client.Bind(new IPEndPoint(IPAddress.Any, TS_MULTICAST_PORT));

        var multicastAddr = IPAddress.Parse(TS_MULTICAST_ADDRESS);
        client.JoinMulticastGroup(multicastAddr);

        Console.WriteLine(
            $"Listening for TS multicast on {TS_MULTICAST_ADDRESS}:{TS_MULTICAST_PORT}"
        );

        // Register cancellation to close the socket
        ct.Register(() =>
        {
            try
            {
                client.Close();
            }
            catch
            {
                // Ignore errors during shutdown
            }
        });

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync();
                demuxer.Feed(result.Buffer, result.Buffer.Length);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (SocketException)
        {
            // Socket closed during cancellation
        }
        catch (ObjectDisposedException)
        {
            // Client was disposed during cancellation
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine($"TS receive error: {ex.Message}");
        }
        finally
        {
            try
            {
                client.DropMulticastGroup(multicastAddr);
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }
    }
}

/// <summary>
/// Represents a client session
/// </summary>
class ClientSession
{
    public string SessionId { get; set; } = string.Empty;
    public IPEndPoint RtpEndPoint { get; set; } = null!;
    public DateTime LastSeen { get; set; }

    public override string ToString() => $"{SessionId}: {RtpEndPoint}, LastSeen={LastSeen:o}";
}
