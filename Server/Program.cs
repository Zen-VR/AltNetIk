using LiteNetLib;
using LiteNetLib.Utils;

public class Server
{
    public class LobbyUser
    {
        public NetPeer peer;
        public string lobbyId;
        public int photonId;
    }
    private static Dictionary<int, LobbyUser> players = new Dictionary<int, LobbyUser>();
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
            players.Remove(peer.Id);
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
        if (!hasLobbyUser)
            return;

        foreach (LobbyUser player in players.Values)
        {
            if (lobbyUser.lobbyId == player.lobbyId && peer != player.peer)
                player.peer.Send(writer, DeliveryMethod.Sequenced);
        }
    }

    public static void OnParamPacketReceived(ParamData packet, NetPeer peer)
    {
        NetDataWriter writer = new NetDataWriter();
        netPacketProcessor.Write(writer, packet);

        bool hasLobbyUser = players.TryGetValue(peer.Id, out LobbyUser lobbyUser);
        if (!hasLobbyUser)
            return;

        foreach (LobbyUser player in players.Values)
        {
            if (lobbyUser.lobbyId == player.lobbyId && peer != player.peer)
                player.peer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
    }

    public static void OnEventPacketReceived(EventData packet, NetPeer peer)
    {
        NetDataWriter writer = new NetDataWriter();
        netPacketProcessor.Write(writer, packet);

        if (!players.ContainsKey(peer.Id))
            return;

        if (packet.eventName == "LocationUpdate")
        {
            players[peer.Id].photonId = packet.photonId;
            players[peer.Id].lobbyId = packet.lobbyHash;
            Console.WriteLine($"[{GetDateTime()}] Location: {packet.lobbyHash} {packet.photonId} {peer.EndPoint}");
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