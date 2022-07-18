using LiteNetLib;
using LiteNetLib.Utils;
using System.Diagnostics;

namespace AltNetIk
{
    public static class Server
    {
        public const string version = "1.8.2";
        public static readonly short versionNum = short.Parse(version[..version.LastIndexOf('.')].Replace(".", "")); // 1.3.0 -> 13

        public class LobbyUser
        {
            public LobbyUser(NetPeer peer)
            {
                this.peer = peer;
                lobbyId = "";
                photonId = -1;
                exceptionCount = 0;
                locationsPerSecond = 0;
            }

            public NetPeer peer;
            public string lobbyId;
            public int photonId;
            public short exceptionCount;
            public short locationsPerSecond;
        }

        private static readonly Dictionary<int, LobbyUser> players = new Dictionary<int, LobbyUser>();
        private static readonly Dictionary<string, Dictionary<int, LobbyUser>> instances = new Dictionary<string, Dictionary<int, LobbyUser>>();
        public static readonly NetPacketProcessor netPacketProcessor = new NetPacketProcessor();
        public static readonly StreamWriter logFile = File.AppendText("EventLog.log");
        private static bool _running;
        private static bool _consoleLogging = true;
        private static readonly Stopwatch _stopWatch = new Stopwatch();

        private static void Main(string[] args)
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            NetManager Server = new NetManager(listener);
            Server.Start(9052);

            netPacketProcessor.RegisterNestedType<System.Numerics.Quaternion>(Serializers.SerializeQuaternion, Serializers.DeserializeQuaternion);
            netPacketProcessor.RegisterNestedType<System.Numerics.Vector3>(Serializers.SerializeVector3, Serializers.DeserializeVector3);
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
                if (players.ContainsKey(peer.Id))
                {
                    LogEntry($"Dupe peer! {peer.Id} {peer.EndPoint}");
                    return;
                }
                players.Add(peer.Id, new LobbyUser(peer));
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
                    if (dataReader.RawDataSize > 2000)
                    {
                        LogEntry($"Kicked for packet size larger than 2000 bytes {fromPeer.EndPoint}");
                        fromPeer.Disconnect();
                        return;
                    }
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
            _stopWatch.Start();

            new Thread(() =>
            {
                while (_running)
                {
                    Server.PollEvents();
                    if (_stopWatch.Elapsed > TimeSpan.FromSeconds(1))
                    {
                        _stopWatch.Restart();
                        CheckLocationLimit();
                    }
                    Thread.Sleep(1);
                }

                LogEntry($"Server stopped");
                Server.Stop(false);
            }).Start();

            while (_running)
            {
                var input = Console.ReadLine();
                switch (input)
                {
                    case "help":
                        Console.WriteLine("help: this is help\n" +
                        "stop: stop the server\n" +
                        "count: user & instance count\n" +
                        "plist: player list\n" +
                        "ilist: instance list\n" +
                        "msgAll <message>: message everyone\n" +
                        "msgInstance <message>: message instance\n" +
                        "renegotiateServer: contact API to find a new server\n");
                        break;

                    case "stop":
                        _running = false;
                        break;

                    case "count":
                        UserCount();
                        break;

                    case "plist":
                        PlayerList();
                        break;

                    case "ilist":
                        InstanceList();
                        break;

                    case "renegotiateServer":
                        RenegotiateServer();
                        break;

                    case "logging":
                        _consoleLogging = !_consoleLogging;
                        Console.WriteLine($"Console logging set to: {_consoleLogging}");
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
                if (peer == player.peer)
                    continue;

                if (writer.Length > player.peer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced))
                {
                    // split packet when receiver MTU is too small
                    player.peer.Send(writer, DeliveryMethod.ReliableOrdered);
                    continue;
                }

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
                LogEntry($"Kicked for param location/packet photonId mismatch {lobbyUser.photonId}/{packet.photonId} {peer.EndPoint}");
                peer.Disconnect();
                return;
            }

            foreach (LobbyUser player in instances[lobbyUser.lobbyId].Values)
            {
                if (peer == player.peer)
                    continue;

                if (writer.Length > player.peer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced))
                {
                    // split packet when receiver MTU is too small
                    player.peer.Send(writer, DeliveryMethod.ReliableOrdered);
                    continue;
                }

                player.peer.Send(writer, DeliveryMethod.Sequenced);
            }
        }

        public static void OnEventPacketReceived(EventData packet, NetPeer peer)
        {
            if (!players.ContainsKey(peer.Id))
                return;

            if (packet.version != versionNum)
            {
                string message = $"Version mismatch, mod:{packet.version} server:{versionNum} please update mod/server to latest version";
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
                                if (player.photonId == packet.photonId && player.peer.EndPoint.Address.ToString() != peer.EndPoint.Address.ToString())
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
                    players[peer.Id].locationsPerSecond++;
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

        public static void CheckLocationLimit()
        {
            // Check if any user has exceeded the location limit of 3 per second
            foreach (LobbyUser player in players.Values)
            {
                if (player.locationsPerSecond > 3)
                {
                    LogEntry($"Kicked for exceeding location limit {player.locationsPerSecond}/3 {player.peer.EndPoint}");
                    player.peer.Disconnect();
                    continue;
                }
                if (player.locationsPerSecond > 0)
                    player.locationsPerSecond = 0;
            }
        }

        public static void LogEntry(string msg)
        {
            string line = $"[{GetDateTime()}] {msg}";
            if (_consoleLogging)
                Console.WriteLine(line);
            logFile.WriteLine(line);
            logFile.Flush();
        }

        public static void UserCount()
        {
            int instancesWithUsers = 0;
            int activeUserCount = 0;
            foreach (Dictionary<int, LobbyUser> instance in instances.Values)
            {
                if (instance.Count > 1)
                {
                    instancesWithUsers++;
                    activeUserCount += instance.Count;
                }
            }
            Console.WriteLine($"[{GetDateTime()}] Users: {players.Count}\nActive Users: {activeUserCount}\nInstances: {instances.Count}\nActive Instances: {instancesWithUsers}");
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

        public static void RenegotiateServer()
        {
            NetDataWriter writer = new NetDataWriter();
            EventData eventData = new EventData
            {
                eventName = "RenegotiateServer"
            };
            netPacketProcessor.Write(writer, eventData);
            foreach (LobbyUser player in players.Values)
            {
                player.peer.Send(writer, DeliveryMethod.ReliableOrdered);
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