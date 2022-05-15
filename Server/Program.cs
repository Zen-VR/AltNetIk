using LiteNetLib;
using LiteNetLib.Utils;

namespace AltNetIk
{
    public static class Server
    {
        public static string version = "1.3.0";
        public static short versionNum = short.Parse(version.Substring(0, version.LastIndexOf(".")).Replace(".", "")); // 1.3.0 -> 13

        public class LobbyUser
        {
            public NetPeer peer;
            public string lobbyId;
            public int photonId;
            public int exceptionCount;
        }

        private static Dictionary<int, LobbyUser> players = new Dictionary<int, LobbyUser>();
        private static Dictionary<string, Dictionary<int, LobbyUser>> instances = new Dictionary<string, Dictionary<int, LobbyUser>>();
        public static readonly NetPacketProcessor netPacketProcessor = new NetPacketProcessor();
        public static readonly StreamWriter logFile = File.AppendText("EventLog.log");
        private static bool _running;

        private static void Main(string[] args)
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            NetManager Server = new NetManager(listener);
            Server.Start(9052);

            netPacketProcessor.RegisterNestedType<PacketData.Quaternion>();
            netPacketProcessor.RegisterNestedType<PacketData.Vector3>();
            netPacketProcessor.Subscribe<PacketData, NetPeer>(OnPacketReceived, () => new PacketData());
            netPacketProcessor.Subscribe<ParamData, NetPeer>(OnParamPacketReceived, () => new ParamData());
            netPacketProcessor.Subscribe<EventData, NetPeer>(OnEventPacketReceived, () => new EventData());

            listener.ConnectionRequestEvent += request =>
            {
                request.AcceptIfKey("AltNetIk");
            };

            listener.PeerConnectedEvent += peer =>
            {
                LogEntry($"Peer connected: {peer.EndPoint}");
                players.Add(peer.Id, new LobbyUser() { peer = peer, lobbyId = "", photonId = -1 });
            };

            listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
            {
                LogEntry($"Peer disconnected: {disconnectInfo.Reason} {peer.EndPoint}");
                if (players.ContainsKey(peer.Id))
                {
                    var lobbyId = players[peer.Id].lobbyId;
                    if (!String.IsNullOrEmpty(lobbyId) && instances.ContainsKey(lobbyId))
                    {
                        instances[lobbyId].Remove(peer.Id);
                        if (instances[lobbyId].Count == 0)
                        {
                            instances.Remove(lobbyId);
                        }
                        else
                        {
                            NetDataWriter writer = new NetDataWriter();
                            EventData eventData = new EventData
                            {
                                lobbyHash = lobbyId,
                                photonId = players[peer.Id].photonId,
                                eventName = "PlayerDisconnect"
                            };
                            netPacketProcessor.Write(writer, eventData);
                            foreach (LobbyUser player in instances[lobbyId].Values)
                            {
                                player.peer.Send(writer, DeliveryMethod.ReliableOrdered);
                            }
                            UpdateSenderStates(lobbyId);
                        }
                    }
                    players.Remove(peer.Id);
                }
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
                    if (players.ContainsKey(fromPeer.Id))
                    {
                        players[fromPeer.Id].exceptionCount++;
                        if (players[fromPeer.Id].exceptionCount > 5)
                        {
                            LogEntry($"Kicked for too many exceptions: {e.Message} {fromPeer.EndPoint}");
                            fromPeer.Disconnect();
                        }
                        else
                        {
                            LogEntry($"Exception: {e.Message} {fromPeer.EndPoint}");
                        }
                    }
                    else
                    {
                        LogEntry($"Kicked for exception without location: {e.Message} {fromPeer.EndPoint}");
                        fromPeer.Disconnect();
                    }
                }
            };

            LogEntry($"Server started");
            _running = true;

            new Thread(() =>
            {
                while (_running)
                {
                    Thread.Sleep(5);
                    Server.PollEvents();
                }

                LogEntry($"Server stopped");
                Server.Stop();
            }).Start();

            while (_running)
            {
                var input = Console.ReadLine();
                switch (input)
                {
                    case "stop":
                        _running = false;
                        break;

                    case "uc":
                        UserCount();
                        break;

                    case "pl":
                        PlayerList();
                        break;

                    case "il":
                        InstanceList();
                        break;

                    case string x when x.StartsWith("msgAll "):
                        if (input.Length > 7)
                        {
                            var msg = input.Substring(7);
                            MsgAll(msg);
                            LogEntry($"Sent message to everyone: {msg}");
                        }
                        break;

                    case string x when x.StartsWith("msgInstance "):
                        // msgInstance fzN14oWVtPcJi34Hn+xQng== test message
                        if (input.Length > 12)
                        {
                            var line = input.Substring(12);
                            var index = line.IndexOf(" ");
                            var instanceId = line.Substring(0, index);
                            var msg = line.Substring(index + 1);
                            MsgAll(msg);
                            LogEntry($"Sent message to instance: {instanceId} message: {msg}");
                        }
                        break;

                    default:
                        Console.WriteLine($"Command \"{input}\" not found");
                        break;
                }
            }
        }

        public static void OnPacketReceived(PacketData packet, NetPeer peer)
        {
            NetDataWriter writer = new NetDataWriter();
            netPacketProcessor.Write(writer, packet);

            if (writer.Length > peer.Mtu)
            {
                LogEntry($"Kicked for IK packet too large {writer.Length}/{peer.Mtu} {peer.EndPoint}");
                peer.Disconnect();
                return;
            }

            bool hasLobbyUser = players.TryGetValue(peer.Id, out LobbyUser lobbyUser);
            if (!hasLobbyUser || String.IsNullOrEmpty(lobbyUser.lobbyId) || !instances.ContainsKey(lobbyUser.lobbyId))
                return;

            if (lobbyUser.photonId != packet.photonId)
            {
                LogEntry($"Kicked for IK location/packet photonId mismatch {lobbyUser.photonId}/{packet.photonId} {peer.EndPoint}");
                peer.Disconnect();
                return;
            }

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

            if (lobbyUser.photonId != packet.photonId)
            {
                LogEntry($"Param location/packet photonId mismatch {lobbyUser.photonId}/{packet.photonId} {peer.EndPoint}");
                peer.Disconnect();
                return;
            }

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

            if (packet.version != versionNum)
            {
                string message = $"Version mismatch: {packet.version}/{versionNum} please update mod/server to latest version";
                LogEntry($"{message} {peer.EndPoint}");
                NetDataWriter writer = new NetDataWriter();
                writer.Put(message);
                peer.Disconnect(writer);
            }

            switch (packet.eventName)
            {
                case "LocationUpdate":
                    players[peer.Id].photonId = packet.photonId;
                    var oldLobbyId = players[peer.Id].lobbyId;

                    // Remove user from old lobby
                    if (!String.IsNullOrEmpty(oldLobbyId) && instances.ContainsKey(oldLobbyId) && instances[oldLobbyId].ContainsKey(peer.Id))
                    {
                        instances[oldLobbyId].Remove(peer.Id);
                        if (instances[oldLobbyId].Count == 0)
                            instances.Remove(oldLobbyId);
                        else
                            UpdateSenderStates(oldLobbyId);
                    }

                    // Add user to new lobby
                    if (!String.IsNullOrEmpty(packet.lobbyHash))
                    {
                        if (!instances.ContainsKey(packet.lobbyHash))
                        {
                            instances.Add(packet.lobbyHash, new Dictionary<int, LobbyUser>());
                        }
                        else
                        {
                            // Check for any existing user with same photonId
                            foreach (LobbyUser player in instances[packet.lobbyHash].Values)
                            {
                                if (player.photonId == packet.photonId && player.peer != peer)
                                {
                                    LogEntry($"Kicked for duplicate photonId, Instance: {packet.lobbyHash} PhotonId: {player.photonId} Current: {player.peer.EndPoint} Joining: {peer.EndPoint}");
                                    peer.Disconnect();
                                    return;
                                }
                            }
                        }
                        instances[packet.lobbyHash].Add(peer.Id, players[peer.Id]);

                        UpdateSenderStates(packet.lobbyHash);
                    }

                    players[peer.Id].lobbyId = packet.lobbyHash;
                    LogEntry($"Peer location: {packet.lobbyHash} {packet.photonId} {peer.EndPoint}");
                    break;

                case "PlayerDisconnect":
                    bool hasLobbyUser = players.TryGetValue(peer.Id, out LobbyUser lobbyUser);
                    if (!hasLobbyUser || String.IsNullOrEmpty(lobbyUser.lobbyId) || !instances.ContainsKey(lobbyUser.lobbyId))
                        return;

                    NetDataWriter writer = new NetDataWriter();
                    netPacketProcessor.Write(writer, packet);
                    foreach (LobbyUser player in instances[lobbyUser.lobbyId].Values)
                    {
                        if (peer != player.peer)
                            player.peer.Send(writer, DeliveryMethod.ReliableOrdered);
                    }
                    break;
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

        public static void LogEntry(string msg)
        {
            string line = $"[{GetDateTime()}] {msg}";
            Console.WriteLine(line);
            logFile.WriteLine(line);
            logFile.Flush();
        }

        public static void UserCount()
        {
            Console.WriteLine($"[{GetDateTime()}] User Count: {players.Count}");
        }

        public static void PlayerList()
        {
            // sort players by lobbyId
            foreach (LobbyUser player in players.Values.OrderBy(x => x.lobbyId).ToList())
            {
                Console.WriteLine($"{player.lobbyId} {player.photonId} {player.peer.EndPoint}");
            }
        }

        public static void InstanceList()
        {
            // sort lobbies by player count
            foreach (KeyValuePair<string, Dictionary<int, LobbyUser>> lobby in instances.OrderByDescending(x => x.Value.Count).ToList())
            {
                Console.WriteLine($"{lobby.Key} {lobby.Value.Count}");
            }
        }

        public static void MsgAll(string msg)
        {
            NetDataWriter writer = new NetDataWriter();
            EventData eventData = new EventData
            {
                eventName = "Message",
                data = msg
            };
            netPacketProcessor.Write(writer, eventData);
            foreach (LobbyUser player in players.Values)
            {
                player.peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public static void MsgInstance(string instanceId, string msg)
        {
            if (!instances.ContainsKey(instanceId))
            {
                Console.WriteLine($"Instance \"{instanceId}\" not found");
                return;
            }
            NetDataWriter writer = new NetDataWriter();
            EventData eventData = new EventData
            {
                eventName = "Message",
                data = msg
            };
            netPacketProcessor.Write(writer, eventData);
            foreach (LobbyUser player in instances[instanceId].Values)
            {
                player.peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public static string GetDateTime()
        {
            var dt = DateTime.Now;
            return dt.ToString("dd/MM/yyyy hh:mm:ss tt");
        }
    }
}