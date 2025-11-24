namespace MpegTsRtpGateway;

/// <summary>
/// Represents a 188-byte MPEG-TS packet
/// </summary>
public class TsPacket
{
    public byte[] RawData { get; }
    public bool IsValid { get; }
    public bool PayloadUnitStart { get; }
    public int PID { get; }
    public int ContinuityCounter { get; }
    public int AdaptationFieldControl { get; }
    public int PayloadOffset { get; }

    public TsPacket(byte[] data)
    {
        if (data.Length != 188)
            throw new ArgumentException("TS packet must be 188 bytes.", nameof(data));

        RawData = data;

        // Check sync byte
        if (data[0] != 0x47)
        {
            IsValid = false;
            return;
        }

        IsValid = true;

        // Parse header
        PayloadUnitStart = (data[1] & 0x40) != 0;
        PID = ((data[1] & 0x1F) << 8) | data[2];

        AdaptationFieldControl = (data[3] & 0x30) >> 4;
        ContinuityCounter = data[3] & 0x0F;

        // Calculate payload offset
        int offset = 4;
        if (AdaptationFieldControl == 2 || AdaptationFieldControl == 3)
        {
            int adaptationLength = data[offset];
            offset += 1 + adaptationLength;
        }

        PayloadOffset = offset;
    }

    public ReadOnlySpan<byte> GetPayloadSpan()
    {
        if (PayloadOffset >= RawData.Length)
            return ReadOnlySpan<byte>.Empty;

        return new ReadOnlySpan<byte>(RawData, PayloadOffset, RawData.Length - PayloadOffset);
    }
}
