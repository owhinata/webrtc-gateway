# MpegTsRtpGateway

MPEG-TS to H.264 RTP conversion gateway with full TS demuxing and RFC 6184 compliant RTP packetization.

## Overview

MpegTsRtpGateway is a sophisticated gateway that receives MPEG-TS streams over UDP multicast, extracts H.264 video, and converts it to RFC 6184 compliant H.264 RTP packets for unicast delivery.

### Key Features
- **Full TS demuxing**: PAT/PMT parsing to detect H.264 streams
- **PES assembly**: Reconstructs PES packets from TS packets
- **PTS extraction**: Accurate RTP timestamps from MPEG-TS PTS
- **RFC 6184 compliant**: Single NAL and FU-A fragmentation modes
- **Low CPU usage**: ~0.33% average (optimized pipeline)
- **Clean shutdown**: Proper Ctrl+C handling

## Architecture

```
VLC (MPEG-TS over UDP Multicast)
    |
    | UDP 239.0.0.1:5004
    | MPEG-TS (H.264 video inside)
    v
MpegTsRtpGateway
    |
    ├─> TsDemuxer (PAT/PMT → Video PID)
    ├─> PesAssembler (TS → PES + PTS)
    ├─> H264NalParser (PES → NAL units)
    └─> H264RtpPacker (NAL → RTP packets)
    |
    | HTTP POST /offer (SDP Offer/Answer)
    | UDP 127.0.0.1:5006 → Client Port
    v
RtpClientSipsorcery
```

## Processing Pipeline

```
MPEG-TS packets (188 bytes)
    ↓ TsDemuxer
Video PID TS packets
    ↓ PesAssembler
PES packets + PTS (90kHz)
    ↓ H264NalParser
NAL units (Annex B)
    ↓ H264RtpPacker
RTP packets (RFC 6184)
```

## Configuration

### Gateway Settings
```csharp
// MPEG-TS multicast input
const string TS_MULTICAST_ADDRESS = "239.0.0.1";
const int TS_MULTICAST_PORT = 5004;

// HTTP server for SDP exchange
const int GATEWAY_HTTP_PORT = 8080;

// RTP send port
const int GATEWAY_SDP_VIDEO_PORT = 5006;

// H.264 payload type
const int H264_PAYLOAD_TYPE = 96;

// RTP SSRC
const uint RTP_SSRC = 0x12345678;
```

## Usage

### 1. Start VLC with MPEG-TS Multicast

**Linux/macOS:**
```bash
cvlc screen:// --screen-fps=60 \
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 \
  --sout '#transcode{vcodec=h264,acodec=none}:udp{mux=ts,dst=239.0.0.1:5004}' \
  --sout-keep
```

**Windows PowerShell:**
```powershell
& "C:\Program Files\VideoLAN\VLC\vlc.exe" `
  screen:// --screen-fps=60 `
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 `
  --sout '#transcode{vcodec=h264,acodec=none}:udp{mux=ts,dst=239.0.0.1:5004}' `
  --sout-keep
```

**Key points:**
- Use `mux=ts` to wrap H.264 in MPEG-TS
- `--sout-keep` ensures continuous streaming

### 2. Start Gateway

```bash
cd csharp/MpegTsRtpGateway
dotnet run
```

Expected output:
```
=== MpegTsRtpGateway starting ===
TS Input: udp://239.0.0.1:5004
HTTP Server: http://localhost:8080/offer

HTTP server listening on port 8080
Listening for TS multicast on 239.0.0.1:5004
Gateway is running. Press Ctrl+C to exit.
```

### 3. Start Client

```bash
cd csharp/RtpClientSipsorcery
dotnet run
```

Expected output:
```
RTP Client (SIPSorcery) starting...

=== SDP Offer ===
v=0
o=- 1190952695 0 IN IP4 127.0.0.1
...

Registered client: abc123: 127.0.0.1:36576, LastSeen=2025-11-24T06:32:29Z

[FIRST RTP PACKET] From=127.0.0.1:5006, Media=video, PT=96, Seq=3555, TS=89555808, Len=14
Total: 100 packets | Rate: 60.0 pps | Bitrate: 2500.0 kbps
```

## Components

### TsPacket
Represents a 188-byte MPEG-TS packet.
- Parses TS header (sync byte, PID, continuity counter)
- Handles adaptation field
- Extracts payload

### TsDemuxer
Demuxes MPEG-TS stream to find H.264 video.
- Parses PAT (Program Association Table) to find PMT PID
- Parses PMT (Program Map Table) to find video PID
- Filters TS packets by video PID
- Supports stream type 0x1B (H.264/AVC)

### PesAssembler
Assembles PES packets from TS packets.
- Detects PES start (payload_unit_start_indicator)
- Extracts PTS (Presentation Time Stamp) in 90kHz
- Assembles complete PES payload (H.264 ES)

### H264NalParser
Parses H.264 Annex B format.
- Detects start codes (0x000001 or 0x00000001)
- Extracts NAL units
- Supports all NAL types (SPS, PPS, IDR, non-IDR, etc.)

### H264RtpPacker
Packetizes H.264 NAL units into RTP (RFC 6184).
- **Single NAL mode**: For small NAL units (≤1200 bytes)
- **FU-A mode**: Fragmentation for large NAL units
- Generates proper RTP headers (sequence, timestamp, marker)
- Uses PTS as RTP timestamp

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
  |                                |    (Store IP:Port)
  |                                | 4. Generate SDP Answer
  |                                |    (SendOnly, PT=96, port 5006)
  |                                |
  | 5. SDP Answer                  |
  |<-------------------------------|
  |                                |
  | 6. Start RTP session          |
  |                                |
  |                                | 7. TS → RTP conversion
  |                                |    (ongoing)
  |                                |
  | 8. RTP packets                 |
  |<==============================|
  |   127.0.0.1:5006 → client_port|
```

## RTP Packetization Details

### RTP Header Format
```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|V=2|P|X|  CC   |M|     PT      |       sequence number         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                           timestamp                           |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|           synchronization source (SSRC) identifier            |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### Single NAL Unit Mode
Small NAL units (≤1200 bytes) are sent as-is:
```
RTP Header (12 bytes) + NAL Unit
```

### FU-A Fragmentation Mode
Large NAL units are fragmented:
```
RTP Header (12 bytes) + FU Indicator + FU Header + NAL Fragment
```

FU Indicator:
```
+---------------+
|0|1|2|3|4|5|6|7|
+-+-+-+-+-+-+-+-+
|F|NRI|  Type=28|
+---------------+
```

FU Header:
```
+---------------+
|0|1|2|3|4|5|6|7|
+-+-+-+-+-+-+-+-+
|S|E|R|  Type   |
+---------------+
```

## Performance

### Benchmarks
- **CPU Usage**: 0.33% average, 2% peak
- **Memory**: ~55-60 MB RSS
- **Processing**: < 5ms per frame
- **Throughput**: Up to 20 Mbps tested

### Performance Characteristics
- Zero-copy where possible
- Efficient buffer management
- Minimal allocations in hot path
- Optimized TS parsing

## Troubleshooting

### No Video PID Detected

Check VLC output format:
```bash
# Correct: Uses MPEG-TS with H.264
--sout '#transcode{vcodec=h264}:udp{mux=ts,dst=239.0.0.1:5004}'

# Wrong: Direct RTP (use UdpRtpGateway instead)
--sout '#transcode{vcodec=h264}:rtp{dst=239.0.0.1,port=5004}'
```

### Verify with Wireshark

**TS Input (239.0.0.1:5004):**
```
Filter: udp.port == 5004
Decode as: MPEG-TS
Check: PMT shows stream_type = 0x1B (H.264)
```

**RTP Output (127.0.0.1:5006):**
```
Filter: udp.port == 5006
Protocol: RTP
Check: PT=96, proper sequence numbers
```

### Port 8080 Access Denied (Windows)

```powershell
netsh http add urlacl url=http://+:8080/ user=Everyone
```

### Gateway Hangs on Ctrl+C

This should be fixed in the current version. If it still happens:
```bash
# Find process
ps aux | grep MpegTsRtpGateway

# Kill
kill <PID>
```

## Technical Details

### MPEG-TS Structure
```
TS Packet (188 bytes)
├─ Sync Byte (0x47)
├─ Header (4 bytes)
│  ├─ PID
│  ├─ Payload Unit Start Indicator
│  └─ Continuity Counter
├─ Adaptation Field (optional)
└─ Payload
```

### PES Packet Structure
```
PES Packet
├─ Start Code (0x000001)
├─ Stream ID
├─ PES Packet Length
├─ PES Header
│  ├─ PTS (33 bits, 90kHz)
│  └─ DTS (optional)
└─ ES Data (H.264 Annex B)
```

### H.264 Annex B Format
```
Start Code + NAL Unit + Start Code + NAL Unit + ...
│            │          │            │
0x000001    SPS        0x000001     PPS  ...
```

## Building from Source

```bash
cd csharp/MpegTsRtpGateway
dotnet build -c Release
dotnet run -c Release
```

## Dependencies
- .NET 8.0
- SIPSorcery 8.0.23 (for SDP handling)

## See Also
- [UdpRtpGateway](UdpRtpGateway.md) - For direct H.264 RTP input
- [RFC 6184](https://tools.ietf.org/html/rfc6184) - RTP Payload Format for H.264 Video
- [ISO 13818-1](https://www.iso.org/standard/74427.html) - MPEG-TS Specification
- [Main Documentation](../README.md)
