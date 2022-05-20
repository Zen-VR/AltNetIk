using LiteNetLib;
using LiteNetLib.Utils;
using MelonLoader;
using System;
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
                (senderPlayerData.isSdk2 && senderPlayerData.avatarKind == (short)VRCAvatarManager.AvatarKind.Custom))
            {
                return;
            }

            netRotations = new Quaternion[senderPlayerData.boneCount];

            int index = -1;
            foreach (bool bone in senderPlayerData.boneList)
            {
                if (!bone)
                    continue;
                index++;

                if (!senderPlayerData.transforms[index])
                    continue;

                netRotations[index] = senderPlayerData.preQinvArray[index] * senderPlayerData.transforms[index].localRotation * senderPlayerData.postQArray[index];
            }

            senderPacketData.boneRotations = netRotations;
            senderPacketData.boneList = senderPlayerData.boneList;
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
            if (writer.Length > serverPeer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced))
                serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            else
                serverPeer.Send(writer, DeliveryMethod.Sequenced);

            // Send params
            if (senderPlayerData.parameters.Count == 0)
                yield break;

            var byteIndex = 0;
            foreach (var parameter in senderPlayerData.parameters)
            {
                var type = parameter.field_Private_ParameterType_0;
                senderParamData.paramData[byteIndex++] = (byte)type;
                switch (type)
                {
                    case AvatarParameter.ParameterType.Bool:
                        senderParamData.paramData[byteIndex++] = Convert.ToByte(parameter.field_Private_Boolean_0);
                        break;

                    case AvatarParameter.ParameterType.Int:
                        senderParamData.paramData[byteIndex++] = (byte)parameter.field_Private_Int32_1;
                        break;

                    case AvatarParameter.ParameterType.Float:
                        senderParamData.paramData[byteIndex++] = (byte)((parameter.field_Private_Single_0 + 1f) * 127f);
                        break;
                }
            }
            senderParamData.photonId = currentPhotonId;

            writer.Reset();
            netPacketProcessor.Write(writer, senderParamData);
            if (writer.Length > serverPeer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced))
                serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            else
                serverPeer.Send(writer, DeliveryMethod.Sequenced);
        }

        public IEnumerator SendLocationData()
        {
            if (serverPeer == null) yield break;
            NetDataWriter writer = new NetDataWriter();
            EventData eventData = new EventData
            {
                version = short.Parse(BuildInfo.Version.Substring(0, BuildInfo.Version.LastIndexOf('.')).Replace(".", "")),
                lobbyHash = currentInstanceIdHash,
                photonId = currentPhotonId,
                eventName = "LocationUpdate"
            };
            netPacketProcessor.Write(writer, eventData);
            serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendDisconnect()
        {
            if (serverPeer == null) return;
            NetDataWriter writer = new NetDataWriter();
            EventData eventData = new EventData
            {
                version = short.Parse(BuildInfo.Version.Substring(0, BuildInfo.Version.LastIndexOf('.')).Replace(".", "")),
                lobbyHash = currentInstanceIdHash,
                photonId = currentPhotonId,
                eventName = "PlayerDisconnect"
            };
            netPacketProcessor.Write(writer, eventData);
            serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
    }
}