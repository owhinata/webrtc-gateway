namespace MpegTsRtpGateway;

/// <summary>
/// Packs H.264 NAL units into RTP packets (RFC 6184)
/// </summary>
public class H264RtpPacker
{
    private const int MAX_RTP_PAYLOAD = 1200;
    private const int H264_PAYLOAD_TYPE = 96;

    private readonly uint _ssrc;
    private ushort _seq;
    private uint _lastTimestamp;

    public event Func<byte[], Task>? OnRtpPacketReady;

    public H264RtpPacker(uint ssrc)
    {
        _ssrc = ssrc;
        _seq = 1;
    }

    public void SendAccessUnit(IEnumerable<byte[]> nalus, ulong? pts90k)
    {
        // Use PTS as RTP timestamp, or increment from last
        uint timestamp = pts90k.HasValue
            ? (uint)(pts90k.Value & 0xFFFFFFFF)
            : _lastTimestamp + 3600;
        _lastTimestamp = timestamp;

        var nalList = nalus.ToList();
        if (nalList.Count == 0)
            return;

        for (int idx = 0; idx < nalList.Count; idx++)
        {
            var nal = nalList[idx];
            bool isLastNalOfAu = (idx == nalList.Count - 1);

            if (nal.Length <= MAX_RTP_PAYLOAD)
            {
                // Single NAL unit mode
                bool marker = isLastNalOfAu;
                var rtp = BuildSingleNalRtpPacket(nal, timestamp, marker);
                OnRtpPacketReady?.Invoke(rtp);
            }
            else
            {
                // Fragmentation Unit A (FU-A) mode
                foreach (var rtp in BuildFuAPackets(nal, timestamp, isLastNalOfAu))
                {
                    OnRtpPacketReady?.Invoke(rtp);
                }
            }
        }
    }

    private byte[] BuildSingleNalRtpPacket(byte[] nal, uint timestamp, bool marker)
    {
        byte payloadType = H264_PAYLOAD_TYPE;
        ushort seq = _seq++;

        var header = new byte[12];
        header[0] = 0x80; // V=2, P=0, X=0, CC=0
        header[1] = (byte)((marker ? 0x80 : 0x00) | (payloadType & 0x7F));
        header[2] = (byte)(seq >> 8);
        header[3] = (byte)(seq & 0xFF);
        header[4] = (byte)(timestamp >> 24);
        header[5] = (byte)(timestamp >> 16);
        header[6] = (byte)(timestamp >> 8);
        header[7] = (byte)(timestamp & 0xFF);
        header[8] = (byte)(_ssrc >> 24);
        header[9] = (byte)(_ssrc >> 16);
        header[10] = (byte)(_ssrc >> 8);
        header[11] = (byte)(_ssrc & 0xFF);

        var packet = new byte[header.Length + nal.Length];
        Buffer.BlockCopy(header, 0, packet, 0, header.Length);
        Buffer.BlockCopy(nal, 0, packet, header.Length, nal.Length);
        return packet;
    }

    private IEnumerable<byte[]> BuildFuAPackets(byte[] nal, uint timestamp, bool isLastNalOfAu)
    {
        byte nalHeader = nal[0];
        byte forbidden = (byte)(nalHeader & 0x80);
        byte nri = (byte)(nalHeader & 0x60);
        byte nalType = (byte)(nalHeader & 0x1F);

        byte fuIndicator = (byte)(forbidden | nri | 28); // FU-A type = 28
        int offset = 1; // Skip original NAL header
        int payloadRemain = nal.Length - 1;

        bool first = true;
        while (payloadRemain > 0)
        {
            int chunk = Math.Min(MAX_RTP_PAYLOAD - 2, payloadRemain); // -2 for FU indicator + FU header
            bool lastFragment = (payloadRemain - chunk) == 0;

            byte fuHeader = nalType;
            if (first)
                fuHeader |= 0x80; // Start bit
            if (lastFragment)
                fuHeader |= 0x40; // End bit

            ushort seq = _seq++;
            byte payloadType = H264_PAYLOAD_TYPE;
            byte marker = (lastFragment && isLastNalOfAu) ? (byte)0x80 : (byte)0x00;

            var header = new byte[12];
            header[0] = 0x80; // V=2, P=0, X=0, CC=0
            header[1] = (byte)(marker | (payloadType & 0x7F));
            header[2] = (byte)(seq >> 8);
            header[3] = (byte)(seq & 0xFF);
            header[4] = (byte)(timestamp >> 24);
            header[5] = (byte)(timestamp >> 16);
            header[6] = (byte)(timestamp >> 8);
            header[7] = (byte)(timestamp & 0xFF);
            header[8] = (byte)(_ssrc >> 24);
            header[9] = (byte)(_ssrc >> 16);
            header[10] = (byte)(_ssrc >> 8);
            header[11] = (byte)(_ssrc & 0xFF);

            var packet = new byte[header.Length + 2 + chunk];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length);
            packet[12] = fuIndicator;
            packet[13] = fuHeader;
            Buffer.BlockCopy(nal, offset, packet, 14, chunk);

            yield return packet;

            first = false;
            offset += chunk;
            payloadRemain -= chunk;
        }
    }
}
