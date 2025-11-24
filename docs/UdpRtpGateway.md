# UdpRtpGateway

Direct RTP multicast-to-unicast relay gateway with SDP Offer/Answer signaling.

## Overview

UdpRtpGateway is a simple and efficient gateway that receives H.264 RTP packets from a multicast group and relays them to registered clients via unicast. It performs minimal processing, acting as a direct relay.

### Key Features
- **Direct relay**: No transcoding or re-encoding
- **Low latency**: Minimal processing overhead
- **SDP Offer/Answer**: Standard signaling via HTTP
- **Multi-client support**: Multiple clients can connect simultaneously
- **Clean shutdown**: Proper Ctrl+C handling

## Architecture

```
VLC (H.264 RTP Multicast)
    |
    | UDP 239.0.0.1:5004
    | RTP PT=96 (H.264)
    v
UdpRtpGateway
    |
    | HTTP POST /offer (SDP Offer/Answer)
    | UDP 127.0.0.1:5006 → Client Port
    v
RtpClientSipsorcery
```

## Configuration

### Gateway Settings
```csharp
// Multicast input
const string MULTICAST_ADDRESS = "239.0.0.1";
const int MULTICAST_PORT = 5004;

// HTTP server for SDP exchange
const int HTTP_PORT = 8080;

// RTP send port
const int SEND_PORT = 5006;

// H.264 payload type
const int H264_PAYLOAD_TYPE = 96;
```

### Client Settings
```csharp
// Gateway HTTP endpoint
const string GATEWAY_HTTP_URL = "http://127.0.0.1:8080/offer";

// H.264 payload type (must match gateway)
const int H264_PAYLOAD_TYPE = 96;
```

## Usage

### 1. Start VLC with H.264 RTP Multicast

**Linux/macOS:**
```bash
cvlc screen:// --screen-fps=60 \
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 \
  --sout '#transcode{vcodec=h264,acodec=none}:rtp{dst=239.0.0.1,port=5004,proto=udp}' \
  --sout-keep
```

**Windows PowerShell:**
```powershell
& "C:\Program Files\VideoLAN\VLC\vlc.exe" `
  screen:// --screen-fps=60 `
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 `
  --sout '#transcode{vcodec=h264,acodec=none}:rtp{dst=239.0.0.1,port=5004,proto=udp}' `
  --sout-keep
```

**Important:** Use `vcodec=h264` for RTP payload type 96, not MPEG-TS.

### 2. Start Gateway

```bash
cd csharp/UdpRtpGateway
dotnet run
```

Expected output:
```
UDP RTP Gateway starting...
Multicast: 239.0.0.1:5004
HTTP Server: http://+:8080/
Joined multicast group 239.0.0.1:5004
Send client bound to port 5006
```

### 3. Start Client

```bash
cd csharp/RtpClientSipsorcery
dotnet run
```

Expected output:
```
RTP Client (SIPSorcery) starting...
Gateway URL: http://127.0.0.1:8080/offer

=== SDP Offer ===
v=0
o=- 1234567890 0 IN IP4 127.0.0.1
...

=== SDP Answer ===
v=0
o=- 9876543210 0 IN IP4 0.0.0.0
...

[FIRST RTP PACKET] From=127.0.0.1:5006, Media=video, PT=96, Seq=1234, TS=567890, Len=1200
Total: 100 packets | Rate: 30.0 pps | Bitrate: 2048.5 kbps
```

## SDP Exchange Flow

```
Client                          Gateway
  |                                |
  | 1. Generate SDP Offer         |
  |    (RecvOnly, PT=96)          |
  |                                |
  | 2. POST /offer                 |
  |------------------------------->|
  |                                |
  |                                | 3. Register client
  |                                | 4. Generate SDP Answer
  |                                |    (SendOnly, PT=96, port 5006)
  |                                |
  | 5. SDP Answer                  |
  |<-------------------------------|
  |                                |
  | 6. Start RTP session          |
  |                                |
  | 7. RTP packets                 |
  |<==============================|
  |   127.0.0.1:5006 → client_port|
```

## Troubleshooting

### Port 8080 Access Denied (Windows)

Run as Administrator or set up URL reservation:
```powershell
netsh http add urlacl url=http://+:8080/ user=Everyone
```

### No Packets Received

**Check payload type mismatch:**
- VLC must use H.264 RTP (`vcodec=h264`)
- Do NOT use MPEG-TS (`mux=ts`)
- Client expects PT=96 (H.264)

**Verify with Wireshark:**
```
Filter: udp.port == 5006
Check: RTP header starts with 0x80 0x60 (PT=96)
Source: 127.0.0.1:5006
Destination: 127.0.0.1:<client_port>
```

### Firewall Issues (Linux)

```bash
# Allow multicast
sudo iptables -A INPUT -d 239.0.0.0/8 -j ACCEPT

# Allow HTTP
sudo ufw allow 8080/tcp

# Allow RTP
sudo ufw allow 5006/udp
```

## Performance

### Benchmarks
- **CPU Usage**: < 0.5% average
- **Memory**: ~40-50 MB
- **Latency**: < 5ms
- **Throughput**: Up to 20 Mbps tested

### Optimization Tips
- Use appropriate video bitrate for your network
- Adjust VLC FPS to match source
- Consider using wired network for best results

## Technical Details

### RTP Relay Process
1. Receive RTP packet from multicast group
2. Look up registered clients
3. Send identical packet to each client's unicast address
4. No modification to RTP headers or payload

### Session Management
- Clients register by sending SDP Offer
- Gateway stores client IP and RTP port
- Sessions remain active until gateway restart
- Future: Add session timeout and keepalive

## Building from Source

```bash
cd csharp/UdpRtpGateway
dotnet build -c Release
dotnet run -c Release
```

## Dependencies
- .NET 8.0
- SIPSorcery 8.0.23 (for SDP handling)

## See Also
- [MpegTsRtpGateway](MpegTsRtpGateway.md) - For MPEG-TS input
- [Main Documentation](../README.md)
