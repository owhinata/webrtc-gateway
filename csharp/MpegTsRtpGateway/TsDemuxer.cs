namespace MpegTsRtpGateway;

/// <summary>
/// Demuxes MPEG-TS packets, extracts PAT/PMT to identify video PID
/// </summary>
public class TsDemuxer
{
    private const byte SYNC_BYTE = 0x47;
    private readonly List<byte> _buffer = new();

    private int? _pmtPid;
    private int? _videoPid;

    public int? VideoPid => _videoPid;

    public event Action<int, TsPacket>? OnPesPacketFragment;

    public void Feed(byte[] data, int length)
    {
        _buffer.AddRange(data.AsSpan(0, length).ToArray());

        while (_buffer.Count >= 188)
        {
            var packetBytes = _buffer.GetRange(0, 188).ToArray();
            _buffer.RemoveRange(0, 188);

            var packet = new TsPacket(packetBytes);
            if (!packet.IsValid)
                continue;

            if (packet.PID == 0)
            {
                ParsePat(packet);
            }
            else if (_pmtPid.HasValue && packet.PID == _pmtPid.Value)
            {
                ParsePmt(packet);
            }
            else if (_videoPid.HasValue && packet.PID == _videoPid.Value)
            {
                OnPesPacketFragment?.Invoke(packet.PID, packet);
            }
        }
    }

    private void ParsePat(TsPacket packet)
    {
        // PAT: payload starts with pointer_field
        var payload = packet.GetPayloadSpan();
        if (payload.Length < 1)
            return;

        int offset = 0;
        int pointerField = payload[offset];
        offset += 1 + pointerField;

        if (offset + 8 > payload.Length)
            return;

        // Skip table_id, section_syntax_indicator, section_length, transport_stream_id, version, etc.
        // For simplicity, jump to program associations
        offset += 8;

        // Read program_number and program_map_PID
        // Format: program_number(2 bytes) + reserved(3 bits) + program_map_PID(13 bits)
        if (offset + 4 > payload.Length)
            return;

        ushort programNumber = (ushort)((payload[offset] << 8) | payload[offset + 1]);
        ushort programMapPidRaw = (ushort)((payload[offset + 2] << 8) | payload[offset + 3]);
        int programMapPid = programMapPidRaw & 0x1FFF;

        _pmtPid = programMapPid;
    }

    private void ParsePmt(TsPacket packet)
    {
        var payload = packet.GetPayloadSpan();
        if (payload.Length < 1)
            return;

        int offset = 0;
        int pointerField = payload[offset];
        offset += 1 + pointerField;

        if (offset + 12 > payload.Length)
            return;

        // Read table_id
        byte tableId = payload[offset];
        if (tableId != 0x02) // PMT table_id should be 0x02
            return;

        // Read section_length
        ushort sectionLengthRaw = (ushort)((payload[offset + 1] << 8) | payload[offset + 2]);
        int sectionLength = sectionLengthRaw & 0x0FFF;

        // Calculate end position
        int sectionEnd = offset + 3 + sectionLength;

        // Skip: table_id(1), section_length(2), program_number(2), reserved/version/current(1), section_number(1), last_section_number(1) = 8 bytes
        offset += 8;

        // Skip PCR_PID (2 bytes)
        offset += 2;

        // Read program_info_length (next 2 bytes)
        ushort programInfoLengthRaw = (ushort)((payload[offset] << 8) | payload[offset + 1]);
        int programInfoLength = programInfoLengthRaw & 0x0FFF;
        offset += 2;

        // Skip program descriptors
        offset += programInfoLength;

        // Parse elementary streams until section end - 4 (CRC32)
        while (offset + 5 <= sectionEnd - 4 && offset + 5 <= payload.Length)
        {
            byte streamType = payload[offset];
            ushort elementaryPidRaw = (ushort)((payload[offset + 1] << 8) | payload[offset + 2]);
            int elementaryPid = elementaryPidRaw & 0x1FFF;
            ushort esInfoLengthRaw = (ushort)((payload[offset + 3] << 8) | payload[offset + 4]);
            int esInfoLength = esInfoLengthRaw & 0x0FFF;

            // H.264/AVC video stream_type == 0x1B
            if (streamType == 0x1B)
            {
                _videoPid = elementaryPid;
                break;
            }

            offset += 5 + esInfoLength;
        }
    }
}
