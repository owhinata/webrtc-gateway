using System.Diagnostics;
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

Console.WriteLine("RTP Viewer (SIPSorcery + FFmpeg) starting...");
Console.WriteLine($"Gateway URL: {GATEWAY_HTTP_URL}");
Console.WriteLine();

// Check if ffplay is available
if (!IsFFmpegAvailable())
{
    Console.WriteLine("ERROR: ffplay not found in PATH!");
    Console.WriteLine("Please install FFmpeg: sudo apt install ffmpeg");
    return 1;
}

// Create RTP session
var rtpSession = CreateRtpSession();

// Setup packet handler with H.264 stream writer
var h264Writer = new H264StreamWriter();
SetupPacketHandler(rtpSession, h264Writer);

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

Console.WriteLine("RTP session started. Starting FFmpeg player...");
Console.WriteLine("Press Ctrl+C to exit.");
Console.WriteLine();

// Start FFmpeg player in background
var ffmpegProcess = StartFFmpegPlayer();

// Open FIFO for writing (blocks until ffplay connects)
h264Writer.OpenFifo();

// Wait for Ctrl+C
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
Console.WriteLine("\nShutting down...");
h264Writer.Close();
rtpSession.Close("user exit");

if (ffmpegProcess != null && !ffmpegProcess.HasExited)
{
    ffmpegProcess.Kill();
    ffmpegProcess.WaitForExit(1000);
}

Console.WriteLine("RTP viewer closed.");
return 0;

/// <summary>
/// Creates and configures RTP session with H.264 RecvOnly track
/// </summary>
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
void SetupPacketHandler(RTPSession session, H264StreamWriter writer)
{
    session.OnRtpPacketReceived += (remoteEp, mediaType, rtpPacket) =>
    {
        if (mediaType != SDPMediaTypesEnum.video)
            return;

        writer.WriteRtpPacket(rtpPacket);
    };
}

/// <summary>
/// Sends SDP offer to gateway and receives SDP answer
/// </summary>
async Task<string> SendOfferToGateway(string offerSdp)
{
    using var httpClient = new HttpClient();
    var content = new StringContent(offerSdp, Encoding.UTF8, "application/sdp");

    var response = await httpClient.PostAsync(GATEWAY_HTTP_URL, content);
    response.EnsureSuccessStatusCode();

    return await response.Content.ReadAsStringAsync();
}

/// <summary>
/// Check if ffplay is available
/// </summary>
bool IsFFmpegAvailable()
{
    try
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffplay",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.Start();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

/// <summary>
/// Start FFmpeg player to display H.264 stream
/// </summary>
Process? StartFFmpegPlayer()
{
    try
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffplay",
                Arguments =
                    "-f h264 "
                    + "-probesize 32 "
                    + "-analyzeduration 0 "
                    + "-fflags nobuffer "
                    + "-flags low_delay "
                    + "-framedrop "
                    + "-an "
                    + "-window_title \"RTP Viewer\" "
                    + "/tmp/rtp_stream.h264",
                UseShellExecute = false,
                CreateNoWindow = false,
            },
        };

        process.Start();
        Console.WriteLine("FFmpeg player started.");
        return process;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to start FFmpeg: {ex.Message}");
        return null;
    }
}

/// <summary>
/// Writes RTP packets to H.264 FIFO for FFmpeg playback
/// </summary>
class H264StreamWriter
{
    private readonly string _fifoPath = "/tmp/rtp_stream.h264";
    private FileStream? _fileStream;
    private byte[]? _currentNal = null;

    public H264StreamWriter()
    {
        try
        {
            // Remove existing FIFO or file if it exists
            if (File.Exists(_fifoPath))
            {
                File.Delete(_fifoPath);
            }

            // Create FIFO using mkfifo command
            var mkfifoProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "mkfifo",
                    Arguments = _fifoPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            mkfifoProcess.Start();
            mkfifoProcess.WaitForExit();

            if (mkfifoProcess.ExitCode != 0)
            {
                var error = mkfifoProcess.StandardError.ReadToEnd();
                Console.WriteLine($"Failed to create FIFO: {error}");
                return;
            }

            Console.WriteLine($"Created FIFO: {_fifoPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create FIFO: {ex.Message}");
        }
    }

    public void OpenFifo()
    {
        try
        {
            Console.WriteLine("Opening FIFO for writing (will block until ffplay connects)...");
            // Open FIFO for writing (this will block until ffplay opens it for reading)
            _fileStream = new FileStream(
                _fifoPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.Read
            );
            Console.WriteLine($"FIFO connected! Writing H.264 stream to: {_fifoPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open FIFO: {ex.Message}");
        }
    }

    public void WriteRtpPacket(RTPPacket rtpPacket)
    {
        if (_fileStream == null)
            return;

        try
        {
            var payload = rtpPacket.Payload;
            if (payload.Length == 0)
                return;

            // Parse NAL unit type
            byte nalHeader = payload[0];
            byte nalType = (byte)(nalHeader & 0x1F);

            // Debug: Log what we're writing
            string nalTypeName = nalType switch
            {
                1 => "Non-IDR",
                5 => "IDR",
                6 => "SEI",
                7 => "SPS",
                8 => "PPS",
                9 => "AUD",
                24 => "STAP-A",
                28 => "FU-A",
                _ => $"Type{nalType}",
            };
            Console.WriteLine(
                $"[Viewer] Seq={rtpPacket.Header.SequenceNumber}, TS={rtpPacket.Header.Timestamp}, NAL={nalType} ({nalTypeName}), Size={payload.Length}"
            );

            if (nalType == 24) // STAP-A
            {
                HandleStapA(payload);
            }
            else if (nalType == 28) // FU-A
            {
                HandleFuA(payload);
            }
            else // Single NAL
            {
                WriteNalUnit(payload);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing RTP packet: {ex.Message}");
        }
    }

    private void HandleStapA(byte[] payload)
    {
        if (payload.Length < 3)
            return;

        int offset = 1; // Skip STAP-A NAL header

        while (offset + 2 < payload.Length)
        {
            // Read NAL unit size (2 bytes, big-endian)
            ushort nalSize = (ushort)((payload[offset] << 8) | payload[offset + 1]);
            offset += 2;

            if (offset + nalSize > payload.Length)
                break;

            // Extract and write NAL unit
            byte[] nalUnit = payload[offset..(offset + nalSize)];
            WriteNalUnit(nalUnit);

            offset += nalSize;
        }
    }

    private void HandleFuA(byte[] payload)
    {
        if (payload.Length < 2)
            return;

        byte fuIndicator = payload[0];
        byte fuHeader = payload[1];

        bool start = (fuHeader & 0x80) != 0;
        bool end = (fuHeader & 0x40) != 0;
        byte nalType = (byte)(fuHeader & 0x1F);

        if (start)
        {
            // Start of new fragmented NAL unit
            byte reconstructedHeader = (byte)((fuIndicator & 0xE0) | nalType);
            _currentNal = new byte[payload.Length - 1];
            _currentNal[0] = reconstructedHeader;
            Array.Copy(payload, 2, _currentNal, 1, payload.Length - 2);
        }
        else if (_currentNal != null)
        {
            // Continuation or end fragment
            var fragment = new byte[payload.Length - 2];
            Array.Copy(payload, 2, fragment, 0, fragment.Length);

            // Append to current NAL
            var combined = new byte[_currentNal.Length + fragment.Length];
            Array.Copy(_currentNal, 0, combined, 0, _currentNal.Length);
            Array.Copy(fragment, 0, combined, _currentNal.Length, fragment.Length);
            _currentNal = combined;

            if (end)
            {
                // Complete NAL unit - write it
                WriteNalUnit(_currentNal);
                _currentNal = null;
            }
        }
    }

    private void WriteNalUnit(byte[] nalUnit)
    {
        if (_fileStream == null || nalUnit.Length == 0)
            return;

        try
        {
            // Write start code (0x00000001)
            _fileStream.Write(new byte[] { 0x00, 0x00, 0x00, 0x01 });

            // Write NAL unit
            _fileStream.Write(nalUnit);
            _fileStream.Flush();
        }
        catch (IOException)
        {
            // FIFO broken (ffplay closed) - silently stop writing
            _fileStream = null;
        }
        catch (ObjectDisposedException)
        {
            // Stream already closed - silently ignore
            _fileStream = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing NAL unit: {ex.Message}");
        }
    }

    public void Close()
    {
        try
        {
            _fileStream?.Close();
        }
        catch (IOException)
        {
            // Broken pipe when closing - this is expected if ffplay exited first
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error closing stream: {ex.Message}");
        }

        try
        {
            _fileStream?.Dispose();
        }
        catch { }

        _fileStream = null;

        // Clean up FIFO
        try
        {
            if (File.Exists(_fifoPath))
            {
                File.Delete(_fifoPath);
            }
        }
        catch { }
    }
}
