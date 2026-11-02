using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OniOnline.Network
{
    public sealed class RoomListing
    {
        public string Name;
        public string Address;
        public int Port;
        public bool HasPassword;
        public int Players;
        public string Code;
        public bool Started;
        public bool CanJoin = true;
    }

    public sealed class LanDiscovery : IDisposable
    {
        public const int DiscoveryPort = 27141;
        private const string Query = "ONIONLINE_DISCOVER_2";
        private const string LegacyQuery = "ONIONLINE_DISCOVER_1";
        private readonly UdpClient socket;
        private readonly ConcurrentQueue<RoomListing> found = new ConcurrentQueue<RoomListing>();
        private readonly bool host;
        private string advertisement;
        private int disposed;
        private readonly int discoveryPort;
        public int Port { get { return ((IPEndPoint)socket.Client.LocalEndPoint).Port; } }

        public LanDiscovery(bool host, int discoveryPort = DiscoveryPort)
        {
            this.host = host;
            this.discoveryPort = discoveryPort;
            socket = new UdpClient(new IPEndPoint(IPAddress.Any, host ? discoveryPort : 0));
            socket.EnableBroadcast = true;
            Task.Run(() => ReceiveLoop());
        }

        public void Advertise(string name, int port, int players, bool hasPassword, bool started = false)
        {
            if (!Protocol.ValidText(name, 64)) throw new ArgumentException("invalid_room_name");
            if (port < 1 || port > 65535 || players < 1) throw new ArgumentOutOfRangeException();
            Volatile.Write(ref advertisement, "ONIONLINE_ROOM_1\n" + port + "\n" + players + "\n" +
                (hasPassword ? "1" : "0") + "\n" + name + "\n" + (started ? "1" : "0"));
        }

        public void Search()
        {
            if (host || Volatile.Read(ref disposed) != 0) return;
            byte[] query = Encoding.UTF8.GetBytes(Query);
            socket.Send(query, query.Length, new IPEndPoint(IPAddress.Broadcast, discoveryPort));
        }

        private async Task ReceiveLoop()
        {
            while (Volatile.Read(ref disposed) == 0)
            {
                try
                {
                    var result = await socket.ReceiveAsync().ConfigureAwait(false);
                    if (result.Buffer.Length > 512) continue;
                    string text = Encoding.UTF8.GetString(result.Buffer);
                    if (host)
                    {
                        string advert = Volatile.Read(ref advertisement);
                        if ((text != Query && text != LegacyQuery) || advert == null) continue;
                        if (text == LegacyQuery) advert = advert.Substring(0, advert.LastIndexOf('\n'));
                        byte[] bytes = Encoding.UTF8.GetBytes(advert);
                        await socket.SendAsync(bytes, bytes.Length, result.RemoteEndPoint).ConfigureAwait(false);
                    }
                    else
                    {
                        string[] parts = text.Split('\n');
                        int port, players;
                        if ((parts.Length != 5 && parts.Length != 6) || parts[0] != "ONIONLINE_ROOM_1" ||
                            !int.TryParse(parts[1], out port) || port < 1 || port > 65535 ||
                            !int.TryParse(parts[2], out players) || players < 1 ||
                            (parts[3] != "0" && parts[3] != "1") || !Protocol.ValidText(parts[4], 64) ||
                            (parts.Length == 6 && parts[5] != "0" && parts[5] != "1") || found.Count >= 64)
                            continue;
                        string address = result.RemoteEndPoint.Address.ToString();
                        found.Enqueue(new RoomListing { Name = parts[4], Address = address, Port = port,
                            Players = players, HasPassword = parts[3] == "1", Started = parts.Length == 6 && parts[5] == "1",
                            Code = "oni://" + address + ":" + port });
                    }
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (Volatile.Read(ref disposed) != 0) return; }
            }
        }

        public bool TryReceive(out RoomListing room) { return found.TryDequeue(out room); }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) socket.Close();
        }
    }
}
