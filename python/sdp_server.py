#!/usr/bin/env python3
"""
SDP Server - Multicast Information Server

Provides SDP Offer/Answer exchange to inform clients about multicast stream information.
Does not relay RTP packets - clients join multicast directly.
"""

import asyncio
import logging

from aiohttp import web


# Configuration
MULTICAST_ADDRESS = "239.0.0.1"  # VLC multicast address
MULTICAST_PORT = 5004  # VLC multicast port
HTTP_PORT = 8080
H264_PAYLOAD_TYPE = 96

# Setup logging
logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)


class SdpServer:
    """SDP Server for providing multicast stream information."""

    def __init__(self):
        self.client_count = 0

    async def handle_offer(self, request: web.Request) -> web.Response:
        """Handle SDP offer request from client."""
        try:
            # Read SDP offer from request body
            offer_sdp_text = await request.text()

            self.client_count += 1
            logger.info(f"=== Received SDP Offer from Client #{self.client_count} ===")
            logger.info(offer_sdp_text)

            # Create and send SDP answer with multicast information
            answer_sdp_text = self.create_answer_sdp()

            logger.info("=== Sending SDP Answer ===")
            logger.info(answer_sdp_text)

            return web.Response(
                text=answer_sdp_text, content_type="application/sdp", status=200
            )
        except Exception as e:
            logger.error(f"Error handling offer: {e}")
            return web.Response(text=str(e), status=500)

    def create_answer_sdp(self) -> str:
        """Create SDP answer with multicast stream information."""
        import random

        session_id = random.randint(1000000000, 9999999999)

        # SDP answer pointing to VLC's multicast stream
        sdp_lines = [
            "v=0",
            f"o=- {session_id} 0 IN IP4 {MULTICAST_ADDRESS}",
            "s=Multicast Stream",
            f"c=IN IP4 {MULTICAST_ADDRESS}",
            "t=0 0",
            f"m=video {MULTICAST_PORT} RTP/AVP {H264_PAYLOAD_TYPE}",
            "a=sendonly",
            f"a=rtpmap:{H264_PAYLOAD_TYPE} H264/90000",
        ]

        return "\r\n".join(sdp_lines) + "\r\n"

    async def start(self):
        """Start the SDP server."""
        logger.info("SDP Server starting...")
        logger.info(f"Multicast Stream: {MULTICAST_ADDRESS}:{MULTICAST_PORT}")
        logger.info(f"HTTP Server: http://0.0.0.0:{HTTP_PORT}/")

        # Create HTTP application
        app = web.Application()
        app.router.add_post('/offer', self.handle_offer)

        # Create runner
        runner = web.AppRunner(app)
        await runner.setup()

        site = web.TCPSite(runner, '0.0.0.0', HTTP_PORT)
        await site.start()

        logger.info(f"HTTP server started on port {HTTP_PORT}")
        logger.info("Press Ctrl+C to exit.")
        logger.info("")

        try:
            # Wait forever
            await asyncio.Event().wait()
        except asyncio.CancelledError:
            pass
        finally:
            # Cleanup
            await runner.cleanup()
            logger.info("SDP server stopped.")


async def main():
    """Main entry point."""
    server = SdpServer()

    try:
        await server.start()
    except KeyboardInterrupt:
        logger.info("\nShutting down...")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
