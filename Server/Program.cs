using LiteNetLib;
using LiteNetLib.Utils;

namespace AltNetIk
{
    public class Server
    {
        public class LobbyUser
        {
            public NetPeer peer;
            public string lobbyId;
            public int photonId;
        }

        private static Dictionary<int, LobbyUser> players = new Dictionary<int, LobbyUser>();
        private static Dictionary<string, Dictionary<int, LobbyUser>> instances = new Dictionary<string, Dictionary<int, LobbyUser>>();
        public static readonly NetPacketProcessor netPacketProcessor = new NetPacketProcessor();

        private static bool _running;

        static void Main(string[] args)
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            NetManager Server = new NetManager(listener);
            Server.Start(9050);

            netPacketProcessor.RegisterNestedType<PacketData.Quaternion>();
            netPacketProcessor.RegisterNestedType<PacketData.Vector3>();
            netPacketProcessor.Subscribe<PacketData, NetPeer>(OnPacketReceived, () => new PacketData());
            netPacketProcessor.Subscribe<ParamData, NetPeer>(OnParamPacketReceived, () => new ParamData());
            netPacketProcessor.Subscribe<EventData, NetPeer>(OnEventPacketReceived, () => new EventData());

            listener.ConnectionRequestEvent += request =>
            {
                request.AcceptIfKey("");
                //request.Reject();
            };

            listener.PeerConnectedEvent += peer =>
            {
                Console.WriteLine($"[{GetDateTime()}] Connected: {peer.EndPoint}");
                players.Add(peer.Id, new LobbyUser() { peer = peer, lobbyId = "", photonId = -1 });
                PrintLobby();
            };

            listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
            {
                Console.WriteLine($"[{GetDateTime()}] Disconnected: {disconnectInfo.Reason} {peer.EndPoint}");
                if (players.ContainsKey(peer.Id))
                {
                    var lobbyId = players[peer.Id].lobbyId;
                    if (!String.IsNullOrEmpty(lobbyId) && instances.ContainsKey(lobbyId))
                    {
                        instances[lobbyId].Remove(peer.Id);
                        if (instances[lobbyId].Count == 0)
                            instances.Remove(lobbyId);
                        else
                            UpdateSenderStates(lobbyId);
                    }
                    players.Remove(peer.Id);
                }
                PrintLobby();
            };

            listener.NetworkReceiveEvent += (fromPeer, dataReader, channel, deliveryMethod) =>
            {
                try
                {
                    netPacketProcessor.ReadAllPackets(dataReader, fromPeer);
                    dataReader.Recycle();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[{GetDateTime()}] {e}");
                }
            };

            Console.WriteLine($"[{GetDateTime()}] Server started");
            _running = true;

            new Thread(() =>
            {
                while (_running)
                {
                    Thread.Sleep(5);
                    Server.PollEvents();
                }

                Server.Stop();
            }).Start();

            while (_running)
            {
                var input = Console.ReadLine();
                if (input == "stop")
                {
                    _running = false;
                }
            }
        }

        public static void OnPacketReceived(PacketData packet, NetPeer peer)
        {
            NetDataWriter writer = new NetDataWriter();
            netPacketProcessor.Write(writer, packet);

            if (writer.Length > peer.Mtu)
            {
                Console.WriteLine($"[{GetDateTime()}] IK packet too large {writer.Length}/{peer.Mtu} {peer.EndPoint}");
                return;
            }

            bool hasLobbyUser = players.TryGetValue(peer.Id, out LobbyUser lobbyUser);
            if (!hasLobbyUser || String.IsNullOrEmpty(lobbyUser.lobbyId) || !instances.ContainsKey(lobbyUser.lobbyId))
                return;

            foreach (LobbyUser player in instances[lobbyUser.lobbyId].Values)
            {
                if (peer != player.peer)
                    player.peer.Send(writer, DeliveryMethod.Sequenced);
            }
        }

        public static void OnParamPacketReceived(ParamData packet, NetPeer peer)
        {
            NetDataWriter writer = new NetDataWriter();
            netPacketProcessor.Write(writer, packet);

            bool hasLobbyUser = players.TryGetValue(peer.Id, out LobbyUser lobbyUser);
            if (!hasLobbyUser || String.IsNullOrEmpty(lobbyUser.lobbyId) || !instances.ContainsKey(lobbyUser.lobbyId))
                return;

            foreach (LobbyUser player in instances[lobbyUser.lobbyId].Values)
            {
                if (peer != player.peer)
                    player.peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public static void OnEventPacketReceived(EventData packet, NetPeer peer)
        {
            if (!players.ContainsKey(peer.Id))
                return;

            if (packet.eventName == "LocationUpdate")
            {
                players[peer.Id].photonId = packet.photonId;
                var oldLobbyId = players[peer.Id].lobbyId;
                if (oldLobbyId == packet.lobbyHash)
                    return;

                if (!String.IsNullOrEmpty(packet.lobbyHash))
                {
                    if (!instances.ContainsKey(packet.lobbyHash))
                        instances.Add(packet.lobbyHash, new Dictionary<int, LobbyUser>());
                    instances[packet.lobbyHash].Add(peer.Id, players[peer.Id]);

                    UpdateSenderStates(packet.lobbyHash);
                }

                if (!String.IsNullOrEmpty(oldLobbyId) && instances.ContainsKey(oldLobbyId) && instances[oldLobbyId].ContainsKey(peer.Id))
                {
                    instances[oldLobbyId].Remove(peer.Id);
                    if (instances[oldLobbyId].Count == 0)
                        instances.Remove(oldLobbyId);
                    else
                        UpdateSenderStates(oldLobbyId);
                }

                players[peer.Id].lobbyId = packet.lobbyHash;
                Console.WriteLine($"[{GetDateTime()}] Location: {packet.lobbyHash} {packet.photonId} {peer.EndPoint}");
            }
        }

        public static void UpdateSenderStates(string lobbyHash)
        {
            if (!instances.ContainsKey(lobbyHash))
                return;

            NetDataWriter writer = new NetDataWriter();
            EventData eventData = new EventData
            {
                eventName = instances[lobbyHash].Count > 1 ? "EnableSender" : "DisableSender"
            };
            netPacketProcessor.Write(writer, eventData);
            foreach (LobbyUser player in instances[lobbyHash].Values)
            {
                player.peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public static void PrintLobby()
        {
            Console.WriteLine($"[{GetDateTime()}] User Count: {players.Count}");
        }

        public static string GetDateTime()
        {
            var dt = DateTime.Now;
            return dt.ToString("dd/MM/yyyy hh:mm:ss tt");
        }
    }
}