using LiteNetLib;
using LiteNetLib.Utils;
using MelonLoader;
using System;
using System.Collections;
using UnityEngine;
using VRC.Playables;
using AvatarKind = VRCAvatarManager.EnumNPublicSealedvaUnLoErBlSaPeSuFaCuUnique;
using AvatarParameterAccessEnum = ObjectPublicIAnimParameterAccessObStInBoSiAcInBoOb2Unique.EnumNPublicSealedvaUnBoInFl5vUnique;

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
                senderPlayerData.playerTransform == null)
            {
                return;
            }

            UpdateAllowedToSend(senderPlayerData);
            if (IsSendingBlocked)
                return;

            netRotations = new System.Numerics.Quaternion[senderPlayerData.boneCount];

            int index = -1;
            foreach (bool bone in senderPlayerData.boneList)
            {
                if (!bone)
                    continue;
                index++;

                if (!senderPlayerData.transforms[index])
                    continue;

                netRotations[index] = senderPlayerData.preQinvArray[index] * senderPlayerData.transforms[index].localRotation.ToSystem() * senderPlayerData.postQArray[index];
            }

            senderPacketData.boneRotations = netRotations;
            senderPacketData.boneList = senderPlayerData.boneList;
            senderPacketData.playerPosition = senderPlayerData.playerTransform.position.ToSystem();
            senderPacketData.playerRotation = senderPlayerData.playerTransform.rotation.ToSystem();
            senderPacketData.photonId = currentPhotonId;
            senderPacketData.ping = serverPeer.RoundTripTime;
            senderPacketData.frozen = IsFrozen;
            senderPacketData.avatarKind = senderPlayerData.avatarKind;

            if (senderPlayerData.transforms.Length > 0 && senderPlayerData.transforms[0] != null)
            {
                senderPacketData.hipPosition = senderPlayerData.transforms[0].position.ToSystem();
                senderPacketData.hipRotation = senderPlayerData.transforms[0].rotation.ToSystem();
            }

            MelonCoroutines.Start(SendData());
        }

        public IEnumerator SendData()
        {
            if (serverPeer == null) yield break;

            // Send IK
            NetDataWriter writer = new NetDataWriter();
            netPacketProcessor.Write(writer, senderPacketData);
            var maxMtu = serverPeer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced);
            if (writer.Length > maxMtu)
                // split packet when MTU is too small
                serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
            else
                serverPeer.Send(writer, DeliveryMethod.Sequenced);

            // Send params
            var paramCount = (short)senderPlayerData.parameters.Count;
            if (paramCount == 0)
                yield break;

            var byteIndex = 0;
            var paramData = senderParamData.paramData;
            var paramCountBytes = new Serializers.ShortBytesUnion(paramCount);
            paramData[byteIndex++] = paramCountBytes.byte0;
            paramData[byteIndex++] = paramCountBytes.byte1;
            foreach (var parameter in senderPlayerData.parameters)
            {
                var type = parameter.field_Public_EnumNPublicSealedvaUnBoInFl5vUnique_0;
                if (floatPrecision && type == AvatarParameterAccessEnum.Float)
                    type = (AvatarParameterAccessEnum)10; // 2 byte float

                paramData[byteIndex++] = (byte)type;
                switch (type)
                {
                    case AvatarParameterAccessEnum.Bool:
                        paramData[byteIndex++] = Convert.ToByte((bool)parameter.field_Protected_Boolean_0);
                        break;

                    case AvatarParameterAccessEnum.Int:
                        paramData[byteIndex++] = (byte)parameter.field_Protected_Int32_1;
                        break;

                    case AvatarParameterAccessEnum.Float:
                        paramData[byteIndex++] = Serializers.SerializeFloat(parameter.field_Protected_Single_0);
                        break;

                    case (AvatarParameterAccessEnum)10:
                        Serializers.SerializeFloatAsShortBytes(ref paramData, ref byteIndex, parameter.field_Protected_Single_0);
                        break;
                }
            }
            senderParamData.photonId = currentPhotonId;
            senderParamData.paramData = paramData;

            writer.Reset();
            netPacketProcessor.Write(writer, senderParamData);
            if (writer.Length > maxMtu)
                // split packet when MTU is too small
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

        public void StopSending()
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

        public void UpdateAllowedToSend(PlayerData playerData)
        {
            // playerAvatarManager.field_Public_VRC_StationInternal_0?.prop_PhotonView_0 != null
            if (playerData.isSdk2 && playerData.avatarKind == (short)AvatarKind.Custom)
            {
                // check if seated in station and if it's synced
                // check if custom avatar is SDk2
                if (!IsSendingBlocked)
                {
                    IsSendingBlocked = true;
                    StopSending();
                }
            }
            else if (IsSendingBlocked)
            {
                IsSendingBlocked = false;
            }
        }
    }
}