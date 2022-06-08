using LiteNetLib;
using MelonLoader;
using ReMod.Core.Notification;
using System;
using System.Collections;

namespace AltNetIk
{
    public partial class AltNetIk : MelonMod
    {
        private IEnumerator Connect()
        {
            if (String.IsNullOrEmpty(serverIP))
                yield break;

            try
            {
                listener = new EventBasedNetListener();
                client = new NetManager(listener);

                client.Start();
                client.Connect(serverIP, serverPort, "AltNetIk");

                listener.NetworkReceiveEvent += OnNetworkReceive;
                listener.PeerConnectedEvent += OnPeerConnected;
                listener.PeerDisconnectedEvent += OnPeerDisconnected;

                Buttons.UpdateAllButtons();
            }
            catch (Exception e)
            {
                Logger.Msg("Connection Error: " + e);
                DisconnectSilent();
            }
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            netPacketProcessor.ReadAllPackets(reader);
            reader.Recycle();
        }

        private void OnPeerConnected(NetPeer peer)
        {
            serverPeer = peer;
            IsConnected = true;
            ReconnectTimer = 1000;
            ReconnectLastAttempt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Logger.Msg(ConsoleColor.Green, "Connected");
            MelonCoroutines.Start(SendLocationData());
            Buttons.UpdateAllButtons();
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (disconnectInfo.Reason == DisconnectReason.RemoteConnectionClose)
            {
                ReconnectLastAttempt = 0; // disable auto reconnect
            }
            string message;
            if (disconnectInfo.AdditionalData != null && disconnectInfo.AdditionalData.RawDataSize > 0)
            {
                NetPacketReader reader = disconnectInfo.AdditionalData;
                message = $"Server Disconnected: {disconnectInfo.Reason} ({reader.GetString()})";
            }
            else
            {
                message = $"Server Disconnected: {disconnectInfo.Reason}";
            }
            Logger.Msg(ConsoleColor.Red, message);
            NotificationSystem.EnqueueNotification("AltNetIk", message);

            DisconnectSilent();
        }

        private void AutoReconnect()
        {
            if (IsConnected || ReconnectLastAttempt == 0)
                return;

            var date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (date - ReconnectLastAttempt >= ReconnectTimer)
            {
                ReconnectTimer *= 2;
                if (ReconnectTimer > 3600000)
                    ReconnectTimer = 3600000; // 1 hour max
                ReconnectLastAttempt = date;
                Logger.Msg(ConsoleColor.Cyan, "Attempting to reconnect");
                MelonCoroutines.Start(Connect());
            }
        }

        public void ConnectToggle()
        {
            ReconnectLastAttempt = 0;
            if (IsConnected)
                Disconnect();
            else
                MelonCoroutines.Start(Connect());
        }

        private void DisconnectSilent()
        {
            IsConnected = false;
            if (client != null)
            {
                client.DisconnectAll();
                client.Stop();
                client = null;
            }
            DisableReceivers();
            Buttons.UpdateAllButtons();
        }

        private void Disconnect()
        {
            StopSending();

            ReconnectLastAttempt = 0;
            bool wasConnected = client != null;

            DisconnectSilent();

            if (wasConnected)
            {
                Logger.Msg("Disconnected");
            }
        }
    }
}