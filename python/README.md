# RTP Multicast Gateway - Python Implementation

Python implementation of SDP-based RTP multicast streaming system.

## Features

- **SDP Server (`sdp_server.py`)**: Provides multicast stream information via SDP Offer/Answer exchange
- **RTP Multicast Client (`rtp_multicast_client.py`)**: Receives RTP multicast stream after SDP negotiation

## Architecture

```
VLC → Multicast (239.0.0.1:5004)
         ↑
         └─ Client (joins multicast directly)
              ↑
              └─ Gets multicast info via SDP exchange (sdp_server)
```

The system uses SDP (Session Description Protocol) to inform clients about the multicast stream location. Clients then join the multicast group directly - no RTP packet relay is performed by the server.

## Requirements

- Python 3.8 or higher
- Dependencies listed in `requirements.txt`

## Installation

Install dependencies:

```bash
pip install -r requirements.txt
```

Or using a virtual environment (recommended):

```bash
python3 -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate
pip install -r requirements.txt
```

## Usage

### 1. Start VLC Multicast Streaming

```bash
vlc your-video.mp4 --sout '#rtp{dst=239.0.0.1,port=5004,mux=ts}' --loop
```

This streams the video to multicast address `239.0.0.1:5004`.

### 2. Start the SDP Server

```bash
python sdp_server.py
```

The server will:
- Start HTTP server on port `8080` for SDP negotiation
- Provide multicast stream information (`239.0.0.1:5004`) to clients

### 3. Start the Client

```bash
python rtp_multicast_client.py
```

The client will:
- Send SDP offer to server at `http://127.0.0.1:8080/offer`
- Receive SDP answer with multicast information
- Join the multicast group
- Start receiving and displaying RTP packet statistics

## Configuration

You can modify the following constants in the source files:

### sdp_server.py

- `MULTICAST_ADDRESS`: Multicast address for RTP stream (default: `239.0.0.1`)
- `MULTICAST_PORT`: Multicast port (default: `5004`)
- `HTTP_PORT`: HTTP server port for SDP negotiation (default: `8080`)
- `H264_PAYLOAD_TYPE`: H.264 payload type (default: `96`)

### rtp_multicast_client.py

- `GATEWAY_HTTP_URL`: SDP server URL (default: `http://127.0.0.1:8080/offer`)
- `MULTICAST_ADDRESS`: Default multicast address (default: `239.0.0.1`)
- `MULTICAST_PORT`: Default multicast port (default: `5004`)
- `H264_PAYLOAD_TYPE`: H.264 payload type (default: `96`)

## Development

### Code Formatting

Format code with black:

```bash
black sdp_server.py rtp_multicast_client.py
```

### Code Linting

Run flake8:

```bash
flake8 sdp_server.py rtp_multicast_client.py
```

Run pylint:

```bash
pylint sdp_server.py rtp_multicast_client.py
```

Run mypy:

```bash
mypy sdp_server.py rtp_multicast_client.py
```

### Import Sorting

Sort imports with isort:

```bash
isort sdp_server.py rtp_multicast_client.py
```

### Run All Checks

```bash
black sdp_server.py rtp_multicast_client.py && \
isort sdp_server.py rtp_multicast_client.py && \
flake8 sdp_server.py rtp_multicast_client.py && \
pylint sdp_server.py rtp_multicast_client.py && \
mypy sdp_server.py rtp_multicast_client.py
```

## How It Works

### SDP Server

1. Listens for HTTP POST requests at `/offer`
2. Receives SDP offer from client
3. Returns SDP answer containing multicast stream information
4. Does NOT relay any RTP packets

### Client

1. Creates SDP offer with default multicast information
2. Sends offer to SDP server via HTTP POST
3. Receives SDP answer with actual multicast address/port
4. Parses the answer to extract connection details
5. Creates UDP socket and joins the multicast group
6. Receives RTP packets and displays statistics

## Comparison with C# Implementation

This Python implementation provides similar functionality to the C# version but with a simplified architecture:

- **C# UdpRtpGateway** → **Python sdp_server.py** (simplified, no RTP relay)
- **C# RtpClientSipsorcery** → **Python rtp_multicast_client.py**

Key differences:
- No RTP packet relay - clients join multicast directly
- Simpler SDP exchange without WebRTC features
- Uses `aiohttp` for HTTP server/client
- Async/await pattern with `asyncio`
- Python's built-in `socket` module for multicast UDP

## Troubleshooting

### Server not receiving connections

- Check firewall settings
- Ensure port 8080 is available
- Verify the server is listening on `0.0.0.0`

### Client not receiving multicast packets

- Check firewall settings for multicast traffic
- Ensure multicast routing is enabled on your network
- Verify VLC is streaming to the correct address/port
- Check that SDP negotiation completes successfully

### VLC streaming issues

- Verify the video file path is correct
- Check that VLC is installed and accessible from command line
- Ensure the multicast address is in the valid range (224.0.0.0 - 239.255.255.255)

## License

Same as parent project.
