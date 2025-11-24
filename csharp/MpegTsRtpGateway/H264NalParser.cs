namespace MpegTsRtpGateway;

/// <summary>
/// Parses H.264 Annex B format NAL units
/// </summary>
public class H264NalParser
{
    /// <summary>
    /// Extracts NAL units from Annex B byte stream (with start codes 0x000001 or 0x00000001)
    /// </summary>
    public IEnumerable<byte[]> ParseAnnexBNalus(byte[] data)
    {
        int i = 0;
        int start = -1;

        while (i < data.Length)
        {
            // Check for 4-byte start code: 0x00000001
            if (
                i + 4 <= data.Length
                && data[i] == 0x00
                && data[i + 1] == 0x00
                && data[i + 2] == 0x00
                && data[i + 3] == 0x01
            )
            {
                if (start >= 0)
                {
                    yield return data[start..i];
                }
                start = i + 4;
                i += 4;
            }
            // Check for 3-byte start code: 0x000001
            else if (
                i + 3 <= data.Length
                && data[i] == 0x00
                && data[i + 1] == 0x00
                && data[i + 2] == 0x01
            )
            {
                if (start >= 0)
                {
                    yield return data[start..i];
                }
                start = i + 3;
                i += 3;
            }
            else
            {
                i++;
            }
        }

        // Return the last NAL unit
        if (start >= 0 && start < data.Length)
        {
            yield return data[start..data.Length];
        }
    }
}
