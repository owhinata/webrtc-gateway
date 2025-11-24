# RtpViewerSipsorcery

A C# RTP viewer application that receives H.264 video streams via SDP Offer/Answer exchange and displays them using ffplay through a FIFO pipe.

## Overview

RtpViewerSipsorcery is a lightweight H.264 RTP stream viewer that:
1. Creates an RTP session with RecvOnly video track
2. Sends an SDP Offer to a gateway (default: `http://127.0.0.1:8080/offer`)
3. Receives RTP packets and writes H.264 NAL units to a FIFO pipe
4. Displays the video stream using ffplay

This viewer is designed to work with RTP gateways like `MpegTsRtpGateway` and `UdpRtpGateway`.

## Features

- **SDP-based session negotiation**: Automatic Offer/Answer exchange
- **FIFO-based streaming**: Uses named pipes for efficient process communication
- **H.264 NAL unit handling**: Supports Single NAL, STAP-A, and FU-A packets
- **Real-time playback**: Low-latency video display with ffplay
- **Detailed logging**: Per-packet logging with sequence numbers, timestamps, and NAL types
- **Graceful error handling**: Proper cleanup when ffplay exits or pipe breaks

## Requirements

- **.NET 8.0 SDK** or later
- **ffmpeg/ffplay** installed and available in PATH
- **Linux/macOS** (FIFO pipes are Unix-specific)
- **SIPSorcery** NuGet package (automatically restored)

### Installing ffmpeg

**Ubuntu/Debian:**
```bash
sudo apt install ffmpeg
```

**macOS:**
```bash
brew install ffmpeg
```

## Building

```bash
cd csharp/RtpViewerSipsorcery
dotnet build -c Release
```

## Usage

### Basic Usage

1. Start a gateway (e.g., MpegTsRtpGateway or UdpRtpGateway):
   ```bash
   cd csharp/MpegTsRtpGateway
   dotnet run -c Release
   ```

2. Start the viewer:
   ```bash
   cd csharp/RtpViewerSipsorcery
   dotnet run -c Release
   ```

3. Start streaming video (see [Verified Stream Sources](#verified-stream-sources))

### Configuration

Edit `Program.cs` to customize:

- **Gateway URL** (line 11):
  ```csharp
  const string GATEWAY_HTTP_URL = "http://127.0.0.1:8080/offer";
  ```

- **H.264 Payload Type** (line 16):
  ```csharp
  const int H264_PAYLOAD_TYPE = 96;
  ```

- **FIFO Path** (line 225 in `H264StreamWriter`):
  ```csharp
  private readonly string _fifoPath = "/tmp/rtp_stream.h264";
  ```

## Verified Stream Sources

The following stream sources have been tested and verified:

### MPEG-TS over UDP Multicast

#### Test Pattern (ffmpeg)
```bash
ffmpeg -f lavfi -i testsrc=size=640x360:rate=30 \
  -c:v libx264 -preset ultrafast -tune zerolatency \
  -g 30 -keyint_min 30 -x264-params "repeat-headers=1:bframes=0" \
  -f mpegts udp://239.0.0.1:5004
```

#### Video File - No Transcode (ffmpeg)
```bash
ffmpeg -re -stream_loop -1 -i video.mp4 \
  -c:v copy -an -f mpegts udp://239.0.0.1:5004
```

#### Video File - No Transcode (cvlc)
```bash
cvlc --loop video.mp4 \
  --sout '#std{access=udp,mux=ts,dst=239.0.0.1:5004}' \
  --sout-keep
```

### RTP Multicast

#### Test Pattern (ffmpeg)
```bash
ffmpeg -f lavfi -i testsrc=size=640x360:rate=30 \
  -c:v libx264 -preset ultrafast -tune zerolatency \
  -g 30 -keyint_min 30 -x264-params "repeat-headers=1:bframes=0" \
  -f rtp rtp://239.0.0.1:5004
```

#### Video File - With Transcode (ffmpeg)
```bash
ffmpeg -re -stream_loop -1 -i video.mp4 \
  -c:v libx264 -preset ultrafast -tune zerolatency \
  -g 30 -x264-params "repeat-headers=1" \
  -an -f rtp rtp://239.0.0.1:5004
```

**Note:** For RTP multicast, transcoding with `repeat-headers=1` is recommended to ensure SPS/PPS are sent regularly.

## Architecture

### Components

```
┌─────────────────────────────────────────────────────────┐
│                  RtpViewerSipsorcery                    │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  ┌──────────────┐  SDP Offer   ┌──────────────────┐   │
│  │  RTPSession  │──────────────>│  HTTP Client     │   │
│  │  (RecvOnly)  │<──────────────│  (Gateway)       │   │
│  └──────┬───────┘  SDP Answer   └──────────────────┘   │
│         │                                               │
│         │ RTP Packets                                   │
│         v                                               │
│  ┌──────────────────────────────────────────────────┐  │
│  │        H264StreamWriter                          │  │
│  │  - Creates FIFO: /tmp/rtp_stream.h264           │  │
│  │  - Handles Single NAL, STAP-A, FU-A             │  │
│  │  - Writes H.264 byte stream with start codes    │  │
│  └──────────────────────────────────────────────────┘  │
│         │                                               │
│         │ FIFO Pipe                                     │
│         v                                               │
│  ┌──────────────────────────────────────────────────┐  │
│  │  ffplay Process                                  │  │
│  │  - Reads from FIFO                               │  │
│  │  - Decodes H.264                                 │  │
│  │  - Displays video                                │  │
│  └──────────────────────────────────────────────────┘  │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### H.264 NAL Unit Handling

The viewer supports three RTP packetization modes:

1. **Single NAL Unit**: Complete NAL unit in one RTP packet
   - Written directly with 0x00000001 start code

2. **STAP-A (Single-Time Aggregation Packet)**: Multiple NAL units in one RTP packet
   - Each NAL unit is prefixed with 2-byte size
   - All NAL units are extracted and written individually

3. **FU-A (Fragmentation Unit)**: Large NAL unit split across multiple RTP packets
   - Start, middle, and end fragments are reassembled
   - Complete NAL unit is written when end fragment arrives

### FIFO (Named Pipe)

The application uses a FIFO instead of a regular file for:
- **No disk accumulation**: Data is consumed immediately by ffplay
- **Synchronization**: Writer blocks until reader is ready
- **Streaming**: Proper stream semantics for real-time video

**FIFO Lifecycle:**
1. Create FIFO using `mkfifo` command
2. Start ffplay to open FIFO for reading
3. Open FIFO for writing (blocks until ffplay connects)
4. Stream H.264 data through FIFO
5. Clean up FIFO on exit

## Troubleshooting

### ffplay not found

**Error:**
```
ERROR: ffplay not found in PATH!
```

**Solution:**
```bash
# Ubuntu/Debian
sudo apt install ffmpeg

# macOS
brew install ffmpeg
```

### Broken pipe error

**Error:**
```
System.IO.IOException: Broken pipe
```

**Cause:** This occurs when ffplay exits before the viewer finishes writing.

**Solution:** This is now handled gracefully in the code. The error is caught and ignored during cleanup.

### No video display

**Possible causes:**

1. **Gateway not running**: Ensure MpegTsRtpGateway or UdpRtpGateway is running first
2. **No stream source**: Start an ffmpeg/cvlc stream source
3. **SPS/PPS missing**: Use transcoding with `repeat-headers=1` for reliable playback
4. **Firewall blocking**: Check that UDP multicast is not blocked

### Decoder errors

**Error:**
```
[h264 @ ...] non-existing PPS 0 referenced
```

**Solution:**
- Use MPEG-TS over UDP (more reliable than raw RTP)
- Or use transcoding with `-x264-params "repeat-headers=1"`

## Related Projects

- **MpegTsRtpGateway**: Converts MPEG-TS over UDP to H.264 RTP unicast
- **UdpRtpGateway**: Relays RTP multicast to RTP unicast

## License

This project uses the [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) library, which is licensed under the BSD 3-Clause License.
