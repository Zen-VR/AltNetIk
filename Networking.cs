using LiteNetLib;
using MelonLoader;
using ReMod.Core.Notification;
using System;
using System.Collections;
using ReMod.Core;

namespace AltNetIk
{
    public partial class AltNetIk : ModComponent
    {
        private IEnumerator Connect()
        {
            if (String.IsNullOrEmpty(currentServerIP))
                yield break;

            try
            {
                listener = new EventBasedNetListener();
                client = new NetManager(listener);

                client.Start();
                client.Connect(currentServerIP, currentServerPort, "AltNetIk");

                listener.NetworkReceiveEvent += OnNetworkReceive;
                listener.PeerConnectedEvent += OnPeerConnected;
                listener.PeerDisconnectedEvent += OnPeerDisconnected;

                UpdateAllButtons();
            }
            catch (Exception e)
            {
                Logger.Msg("Connection Error: " + e);
                DisconnectSilent();
            }
            yield break;
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
            UpdateAllButtons();
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            string message;
            if (disconnectInfo.Reason == DisconnectReason.RemoteConnectionClose)
            {
                ReconnectLastAttempt = 0; // disable auto reconnect
            }
            if (disconnectInfo.AdditionalData?.RawDataSize > 0)
            {
                NetPacketReader reader = disconnectInfo.AdditionalData;
                var disconnectReason = reader.GetString();
                message = $"Server Disconnected: {disconnectInfo.Reason} ({disconnectReason})";
            }
            else
            {
                message = $"Server Disconnected: {disconnectInfo.Reason}";
            }

            Logger.Msg(ConsoleColor.Red, message);
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
            {
                Disconnect();
            }
            else
            {
                NegotiateServer();
            }
        }

        private void DisconnectSilent()
        {
            IsConnected = false;
            skipSettingParam = false;
            if (client != null)
            {
                client.DisconnectAll();
                client.Stop();
                client = null;
            }
            DisableReceivers();
            UpdateAllButtons();
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