# WebRTC Gateway

RTP multicast-to-unicast gateway with SDP Offer/Answer exchange.

## Overview

This solution receives H.264 video streams via RTP multicast and relays them to clients via unicast. Clients register with the gateway using SDP Offer/Answer exchange over HTTP.

### Architecture

```
VLC (Screen Capture)
    |
    | Multicast RTP (239.0.0.1:5004, H.264)
    v
UdpRtpGateway (Port 8080)
    |
    | Unicast RTP (Port 5006 → Client Port)
    v
RtpClientSipsorcery
```

## Prerequisites

- .NET 8.0 SDK
- VLC Media Player
- Windows (tested on Windows 11)

## Projects

### UdpRtpGateway
- Receives RTP multicast stream (239.0.0.1:5004)
- HTTP server for SDP Offer/Answer exchange (port 8080)
- Relays RTP packets to registered clients via unicast

### RtpClientSipsorcery
- Sends SDP Offer to gateway via HTTP
- Receives RTP stream using SIPSorcery library
- Displays packet statistics (rate, bitrate, sequence, timestamp)

### MulticastSender
- Test utility for sending multicast packets
- Simulates VLC multicast stream

## Build

```bash
# Build gateway
cd csharp/UdpRtpGateway
dotnet build

# Build client
cd csharp/RtpClientSipsorcery
dotnet build
```

## Usage

### 1. Start VLC with H.264 RTP multicast

**PowerShell:**
```powershell
& "C:\Program Files\VideoLAN\VLC\vlc.exe" `
  screen:// `
  --screen-fps=60 `
  --screen-top=0 --screen-left=0 --screen-width=640 --screen-height=360 `
  --sout '#transcode{vcodec=h264,acodec=none}:rtp{dst=239.0.0.1,port=5004,proto=udp}' `
  --sout-keep
```

**Important:** Use `vcodec=h264` (not TS) to match Payload Type 96.

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
[FIRST RTP PACKET] From=127.0.0.1:5006, Media=video, PT=96, Seq=11189, TS=1271181839, Len=11
Total: 23 packets | Rate: 21.8 pps | Bitrate: 123.4 kbps | Last Seq: 11211 | Last TS: 1271204367
```

## Key Configuration

### Gateway
- Multicast receive: `239.0.0.1:5004`
- HTTP server: `http://+:8080/offer`
- RTP send port: `5006` (matches SDP Answer)
- Payload Type: `96` (H.264)

### Client
- Gateway URL: `http://127.0.0.1:8080/offer`
- Payload Type: `96` (H.264)
- Stream direction: RecvOnly

## Troubleshooting

### Port 8080 Access Denied

Run as Administrator or set up URL reservation:
```powershell
netsh http add urlacl url=http://+:8080/ user=Everyone
```

### No packets received

**Check Payload Type mismatch:**
- VLC must use H.264 RTP (`vcodec=h264`)
- Do NOT use MPEG-TS (`Video - H264 + MP3 (TS)`)
- Client expects PT=96 (H.264), not PT=33 (MPEG-TS)

**Verify with Wireshark:**
- RTP packets from Gateway (127.0.0.1:5006 → 127.0.0.1:client_port)
- Check RTP header: `0x80 0x60` (PT=96 for H.264)

### Check listening ports (PowerShell)

```powershell
Get-NetUDPEndpoint | Where-Object {$_.OwningProcess -in (Get-Process dotnet).Id}
```

## Technical Details

### SDP Exchange
1. Client generates SDP Offer (RecvOnly, PT=96)
2. Client sends Offer to Gateway via HTTP POST
3. Gateway responds with SDP Answer (SendOnly, PT=96)
4. Client applies Answer and starts RTP session

### RTP Relay
- Gateway binds send socket to port 5006 (matches SDP Answer)
- Gateway sends from 127.0.0.1:5006 to client's port
- Client accepts RTP from 127.0.0.1:5006 (SDP validation)

## License

This is a sample implementation for educational purposes.
