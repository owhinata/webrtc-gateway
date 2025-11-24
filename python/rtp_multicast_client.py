#!/usr/bin/env python3
"""
RTP Multicast Client with SDP Offer/Answer

Performs SDP exchange with gateway, then joins multicast group to receive RTP stream.
"""

import asyncio
import logging
import socket
import struct
import time

import aiohttp


# Configuration
GATEWAY_HTTP_URL = "http://127.0.0.1:8080/offer"
MULTICAST_ADDRESS = "239.0.0.1"
MULTICAST_PORT = 5004
H264_PAYLOAD_TYPE = 96

# Setup logging
logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)


class RtpMulticastClient:
    """RTP Multicast Client with SDP exchange for receiving video stream."""

    def __init__(self):
        self.multicast_socket: socket.socket = None
        self.packet_count = 0
        self.last_report_time = time.time()
        self.packets_since_last_report = 0
        self.bytes_since_last_report = 0
        self.multicast_address = MULTICAST_ADDRESS
        self.multicast_port = MULTICAST_PORT

    def create_sdp_offer(self) -> str:
        """Create SDP offer for multicast reception."""
        import random

        session_id = random.randint(1000000000, 9999999999)

        sdp_lines = [
            "v=0",
            f"o=- {session_id} {session_id} IN IP4 {self.multicast_address}",
            "s=RTP Multicast Client",
            f"c=IN IP4 {self.multicast_address}",
            "t=0 0",
            f"m=video {self.multicast_port} RTP/AVP {H264_PAYLOAD_TYPE}",
            "a=recvonly",
            f"a=rtpmap:{H264_PAYLOAD_TYPE} H264/90000",
        ]

        return "\r\n".join(sdp_lines) + "\r\n"

    async def send_offer_to_gateway(self, offer_sdp: str) -> str:
        """Send SDP offer to gateway and receive SDP answer."""
        async with aiohttp.ClientSession() as session:
            async with session.post(
                GATEWAY_HTTP_URL, data=offer_sdp, headers={"Content-Type": "application/sdp"}
            ) as response:
                response.raise_for_status()
                return await response.text()

    def parse_sdp_answer(self, answer_sdp: str):
        """Parse SDP answer to extract multicast address and port."""
        lines = answer_sdp.strip().split('\n')

        for line in lines:
            line = line.strip()
            # Extract connection info (c=IN IP4 239.0.0.1)
            if line.startswith("c=IN IP4 "):
                self.multicast_address = line.split()[-1]
            # Extract media info (m=video 5004 RTP/AVP 96)
            elif line.startswith("m=video "):
                parts = line.split()
                self.multicast_port = int(parts[1])

        logger.info(
            f"Parsed SDP Answer: multicast={self.multicast_address}:{self.multicast_port}"
        )

    def create_multicast_socket(self) -> socket.socket:
        """Create and configure socket for multicast reception."""
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)

        # Bind to INADDR_ANY on the multicast port
        sock.bind(('', self.multicast_port))

        # Join multicast group
        mreq = struct.pack(
            '4sL', socket.inet_aton(self.multicast_address), socket.INADDR_ANY
        )
        sock.setsockopt(socket.IPPROTO_IP, socket.IP_ADD_MEMBERSHIP, mreq)

        # Set to non-blocking
        sock.setblocking(False)

        logger.info(
            f"Joined multicast group {self.multicast_address}:{self.multicast_port}"
        )
        return sock

    def parse_rtp_header(self, data: bytes) -> dict:
        """Parse RTP header (simplified)."""
        if len(data) < 12:
            return None

        # RTP header format (first 12 bytes)
        byte0 = data[0]
        byte1 = data[1]

        version = (byte0 >> 6) & 0x03
        padding = (byte0 >> 5) & 0x01
        extension = (byte0 >> 4) & 0x01
        csrc_count = byte0 & 0x0F

        marker = (byte1 >> 7) & 0x01
        payload_type = byte1 & 0x7F

        sequence_number = struct.unpack("!H", data[2:4])[0]
        timestamp = struct.unpack("!I", data[4:8])[0]
        ssrc = struct.unpack("!I", data[8:12])[0]

        header_len = 12 + (csrc_count * 4)

        return {
            "version": version,
            "payload_type": payload_type,
            "sequence_number": sequence_number,
            "timestamp": timestamp,
            "ssrc": ssrc,
            "marker": marker,
            "header_len": header_len,
            "payload_len": len(data) - header_len,
        }

    async def receive_rtp_packets(self):
        """Receive and log RTP packets from multicast."""
        loop = asyncio.get_event_loop()

        try:
            while True:
                data = await loop.sock_recv(self.multicast_socket, 65536)

                self.packet_count += 1
                self.packets_since_last_report += 1
                self.bytes_since_last_report += len(data)

                # Parse RTP header
                rtp_header = self.parse_rtp_header(data)

                # First packet logging
                if self.packet_count == 1 and rtp_header:
                    logger.info(
                        f"[FIRST RTP PACKET] "
                        f"PT={rtp_header['payload_type']}, "
                        f"Seq={rtp_header['sequence_number']}, "
                        f"TS={rtp_header['timestamp']}, "
                        f"PayloadLen={rtp_header['payload_len']}"
                    )

                # Report statistics every second
                now = time.time()
                elapsed = now - self.last_report_time

                if elapsed >= 1.0:
                    pps = self.packets_since_last_report / elapsed
                    bps = (self.bytes_since_last_report * 8) / elapsed
                    kbps = bps / 1000.0

                    if rtp_header:
                        logger.info(
                            f"Total: {self.packet_count} packets | "
                            f"Rate: {pps:.1f} pps | "
                            f"Bitrate: {kbps:.1f} kbps | "
                            f"Last Seq: {rtp_header['sequence_number']} | "
                            f"Last TS: {rtp_header['timestamp']}"
                        )

                    self.last_report_time = now
                    self.packets_since_last_report = 0
                    self.bytes_since_last_report = 0

        except asyncio.CancelledError:
            logger.info(f"Multicast receiver stopped. Total packets: {self.packet_count}")
            raise

    async def start(self):
        """Start the multicast client."""
        logger.info("RTP Multicast Client starting...")
        logger.info(f"Gateway URL: {GATEWAY_HTTP_URL}")
        logger.info("")

        # Create and send SDP offer
        offer_sdp = self.create_sdp_offer()

        logger.info("=== SDP Offer ===")
        logger.info(offer_sdp)
        logger.info("")

        # Send offer to gateway and get answer
        answer_sdp = await self.send_offer_to_gateway(offer_sdp)

        logger.info("=== SDP Answer ===")
        logger.info(answer_sdp)
        logger.info("")

        # Parse answer to get multicast address/port
        self.parse_sdp_answer(answer_sdp)

        logger.info("Press Ctrl+C to exit.")
        logger.info("")

        # Create multicast socket
        self.multicast_socket = self.create_multicast_socket()

        # Start receiving packets
        await self.receive_rtp_packets()

    def stop(self):
        """Stop the multicast client."""
        if self.multicast_socket:
            self.multicast_socket.close()
        logger.info("Multicast client stopped.")


async def main():
    """Main entry point."""
    client = RtpMulticastClient()

    try:
        await client.start()
    except KeyboardInterrupt:
        logger.info("\nUser pressed Ctrl+C")
    finally:
        client.stop()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
