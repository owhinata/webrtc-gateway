# WebRTC Gateway

A collection of high-performance RTP gateways for converting multicast streams to unicast with SDP Offer/Answer signaling.

## Projects

### [UdpRtpGateway](docs/UdpRtpGateway.md)
Direct RTP multicast-to-unicast relay gateway.
- **Input**: H.264 RTP multicast (239.0.0.1:5004)
- **Output**: H.264 RTP unicast (port 5006)
- **Use case**: Simple relay of pre-encoded H.264 RTP streams

### [MpegTsRtpGateway](docs/MpegTsRtpGateway.md)
MPEG-TS to H.264 RTP conversion gateway.
- **Input**: MPEG-TS over UDP multicast (239.0.0.1:5004)
- **Output**: H.264 RTP unicast (port 5006)
- **Use case**: Convert MPEG-TS streams (e.g., from broadcast) to RTP
- **Features**: TS demuxing, PES assembly, H.264 parsing, RFC 6184 RTP packetization

### [RtpViewerSipsorcery](docs/RtpViewerSipsorcery.md)
SIPSorcery-based RTP viewer with ffplay integration.
- **Input**: H.264 RTP unicast from gateway (port 5006)
- **Output**: Live video display using ffplay
- **Features**: SDP Offer/Answer exchange, FIFO-based streaming, NAL unit handling

### RtpClientSipsorcery
SIPSorcery-based RTP client for testing gateways.
- Sends SDP Offer via HTTP
- Receives RTP streams
- Displays packet statistics

### MulticastSender
Test utility for sending multicast packets.

## Architecture

```
Video Source (VLC/ffmpeg)
    |
    | UDP Multicast
    v
Gateway (UdpRtpGateway or MpegTsRtpGateway)
    |
    | HTTP: SDP Offer/Answer
    | UDP: RTP Unicast
    v
RTP Client (RtpClientSipsorcery)
```

## Quick Start

### Prerequisites
- .NET 8.0 SDK
- VLC Media Player or ffmpeg
- Linux/Windows/macOS

### 1. Choose a Gateway

**For H.264 RTP input:**
```bash
cd csharp/UdpRtpGateway
dotnet run
```

**For MPEG-TS input:**
```bash
cd csharp/MpegTsRtpGateway
dotnet run
```

### 2. Start Client
```bash
cd csharp/RtpClientSipsorcery
dotnet run
```

### 3. Send Video Stream

**H.264 RTP (for UdpRtpGateway):**
```bash
cvlc screen:// --screen-fps=60 \
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 \
  --sout '#transcode{vcodec=h264,acodec=none}:rtp{dst=239.0.0.1,port=5004,proto=udp}' \
  --sout-keep
```

**MPEG-TS (for MpegTsRtpGateway):**
```bash
cvlc screen:// --screen-fps=60 \
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 \
  --sout '#transcode{vcodec=h264,acodec=none}:udp{mux=ts,dst=239.0.0.1:5004}' \
  --sout-keep
```

## Documentation

- [UdpRtpGateway Documentation](docs/UdpRtpGateway.md)
- [MpegTsRtpGateway Documentation](docs/MpegTsRtpGateway.md)
- [RtpViewerSipsorcery Documentation](docs/RtpViewerSipsorcery.md)

## Performance

Both gateways are highly optimized:
- **CPU Usage**: < 1% average
- **Memory**: ~50-60 MB
- **Latency**: Minimal (<10ms)

## License

This is a sample implementation for educational purposes.
