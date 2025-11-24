namespace MpegTsRtpGateway;

/// <summary>
/// Assembles PES packets from TS packets and extracts PTS
/// </summary>
public class PesAssembler
{
    private readonly int _pid;
    private readonly List<byte> _currentPes = new();
    private bool _started = false;
    private ulong? _currentPts90k;

    public event Action<byte[], ulong?>? OnPesCompleted;

    public PesAssembler(int pid)
    {
        _pid = pid;
    }

    public void ProcessTsPacket(TsPacket packet)
    {
        if (packet.PID != _pid)
            return;

        var payload = packet.GetPayloadSpan();

        if (payload.Length == 0)
            return;

        if (packet.PayloadUnitStart)
        {
            // Complete previous PES if exists
            if (_started && _currentPes.Count > 0)
            {
                OnPesCompleted?.Invoke(_currentPes.ToArray(), _currentPts90k);
            }

            _currentPes.Clear();
            _currentPts90k = null;
            _started = true;

            // Parse PES header
            int offset = 0;
            if (
                payload.Length >= 6
                && payload[0] == 0x00
                && payload[1] == 0x00
                && payload[2] == 0x01
            )
            {
                byte streamId = payload[3];
                ushort pesPacketLength = (ushort)((payload[4] << 8) | payload[5]);
                offset = 6;

                // Parse PES header flags
                if (offset + 3 <= payload.Length)
                {
                    byte flags1 = payload[offset];
                    byte flags2 = payload[offset + 1];
                    byte headerLen = payload[offset + 2];
                    offset += 3;

                    bool ptsPresent = (flags2 & 0x80) != 0;
                    bool dtsPresent = (flags2 & 0x40) != 0;

                    if (ptsPresent && offset + 5 <= payload.Length)
                    {
                        // Extract 33-bit PTS from 5 bytes
                        ulong pts = 0;
                        pts |= ((ulong)((uint)(payload[offset + 0] >> 1) & 0x07)) << 30;
                        pts |= ((ulong)((uint)payload[offset + 1])) << 22;
                        pts |= ((ulong)((uint)(payload[offset + 2] >> 1) & 0x7F)) << 15;
                        pts |= ((ulong)((uint)payload[offset + 3])) << 7;
                        pts |= ((ulong)((uint)(payload[offset + 4] >> 1) & 0x7F));

                        _currentPts90k = pts;
                    }

                    offset += headerLen;
                }
            }

            // Add payload after header
            if (offset < payload.Length)
            {
                _currentPes.AddRange(payload.Slice(offset).ToArray());
            }
        }
        else
        {
            if (!_started)
                return;

            _currentPes.AddRange(payload.ToArray());
        }
    }
}
