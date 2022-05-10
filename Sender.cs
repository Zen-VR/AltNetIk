using LiteNetLib;
using LiteNetLib.Utils;
using MelonLoader;
using System.Collections;
using UnityEngine;
using VRC.Playables;

namespace AltNetIk
{
    public partial class AltNetIk : MelonMod
    {
        public void OnVeryLateUpdate(Camera camera)
        {
            if (camera != Camera.main ||
                !IsConnected ||
                !IsSending ||
                currentPhotonId == 0 ||
                senderPlayerData.playerTransform == null ||
                (senderPlayerData.isSdk2 && senderPlayerData.avatarKind == 8))
            {
                return;
            }

            netRotations = new PacketData.Quaternion[senderPlayerData.boneCount];

            int index = -1;
            for (int i = 0; i < 55; i++)
            {
                if (!senderPlayerData.boneList[i])
                    continue;
                index++;

                if (!senderPlayerData.transforms[index])
                    continue;

                netRotations[index] = senderPlayerData.preQinvArray[index] * senderPlayerData.transforms[index].localRotation * senderPlayerData.postQArray[index];
            }

            senderPacketData.boneRotations = netRotations;
            senderPacketData.boneList = senderPlayerData.boneList;
            senderPacketData.boneCount = (byte)senderPlayerData.boneCount;
            senderPacketData.playerPosition = senderPlayerData.playerTransform.position;
            senderPacketData.playerRotation = senderPlayerData.playerTransform.rotation;
            senderPacketData.photonId = currentPhotonId;
            senderPacketData.ping = serverPeer.RoundTripTime;
            senderPacketData.frozen = IsFrozen;
            senderPacketData.avatarKind = senderPlayerData.avatarKind;

            if (senderPlayerData.transforms.Length > 0 && senderPlayerData.transforms[0] != null)
            {
                senderPacketData.hipPosition = senderPlayerData.transforms[0].position;
                senderPacketData.hipRotation = senderPlayerData.preQinvArray[0] * senderPlayerData.transforms[0].rotation * senderPlayerData.postQArray[0];
            }

            MelonCoroutines.Start(SendData());
        }

        public IEnumerator SendData()
        {
            if (serverPeer == null) yield break;

            NetDataWriter writer = new NetDataWriter();
            netPacketProcessor.Write(writer, senderPacketData);
            if (writer.Length > serverPeer.Mtu)
                Logger.Error($"IK packet too large {writer.Length}/{serverPeer.Mtu}");
            serverPeer.Send(writer, DeliveryMethod.Sequenced);

            // Send params
            if (senderPlayerData.parameters.Count == 0)
                yield break;

            short index = 0;
            short boolIndex = 0;
            short intIndex = 0;
            short floatIndex = 0;
            foreach (var parameter in senderPlayerData.parameters.Values)
            {
                var type = parameter.field_Private_ParameterType_0;
                switch (type)
                {
                    case AvatarParameter.ParameterType.Bool:
                        senderParamData.boolParams[boolIndex] = parameter.field_Private_Boolean_0;
                        boolIndex++;
                        break;

                    case AvatarParameter.ParameterType.Int:
                        senderParamData.intParams[intIndex] = (short)parameter.field_Private_Int32_1;
                        intIndex++;
                        break;

                    case AvatarParameter.ParameterType.Float:
                        senderParamData.floatParams[floatIndex] = parameter.field_Private_Single_0;
                        floatIndex++;
                        break;
                }
                senderParamData.paramType[index] = (short)type;
                senderParamData.paramName[index] = parameter.field_Private_String_0;
                index++;
            }
            senderParamData.photonId = currentPhotonId;

            writer.Reset();
            netPacketProcessor.Write(writer, senderParamData);
            serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);

            yield break;
        }

        public IEnumerator SendLocationData()
        {
            if (serverPeer == null) yield break;
            NetDataWriter writer = new NetDataWriter();
            EventData eventData = new EventData
            {
                version = short.Parse(BuildInfo.Version.Substring(0, BuildInfo.Version.LastIndexOf(".")).Replace(".", "")),
                lobbyHash = currentInstanceIdHash,
                photonId = currentPhotonId,
                eventName = "LocationUpdate"
            };
            netPacketProcessor.Write(writer, eventData);
            serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);

            yield break;
        }

        public void SendDisconnect()
        {
            if (serverPeer == null) return;
            NetDataWriter writer = new NetDataWriter();
            EventData eventData = new EventData
            {
                version = short.Parse(BuildInfo.Version.Substring(0, BuildInfo.Version.LastIndexOf(".")).Replace(".", "")),
                lobbyHash = currentInstanceIdHash,
                photonId = currentPhotonId,
                eventName = "PlayerDisconnect"
            };
            netPacketProcessor.Write(writer, eventData);
            serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
    }
}