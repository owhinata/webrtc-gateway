using System.Net;
using System.Net.Sockets;
using System.Text;

const string MULTICAST_ADDRESS = "239.0.0.1";
const int MULTICAST_PORT = 5004;
const int PACKET_COUNT = 200;
const int DELAY_MS = 50;

Console.WriteLine("Multicast Test Sender");
Console.WriteLine($"Sending to: {MULTICAST_ADDRESS}:{MULTICAST_PORT}");
Console.WriteLine($"Packet count: {PACKET_COUNT}");
Console.WriteLine();

var client = new UdpClient();
var multicastEndPoint = new IPEndPoint(IPAddress.Parse(MULTICAST_ADDRESS), MULTICAST_PORT);

for (int i = 0; i < PACKET_COUNT; i++)
{
    var message = $"Test RTP packet #{i:D4}";
    var data = Encoding.UTF8.GetBytes(message);

    await client.SendAsync(data, data.Length, multicastEndPoint);

    if ((i + 1) % 50 == 0)
    {
        Console.WriteLine($"Sent {i + 1} packets...");
    }

    await Task.Delay(DELAY_MS);
}

Console.WriteLine($"\nCompleted! Sent {PACKET_COUNT} packets total.");
client.Close();
