using MelonLoader;
using System;
using System.Collections.Generic;
using UnhollowerRuntimeLib;
using VRC;
using VRC.Dynamics;
using VRC.Networking.Pose;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Component = UnityEngine.Component;
using GameObject = UnityEngine.GameObject;
using Object = UnityEngine.Object;
using Quaternion = System.Numerics.Quaternion;
using Transform = UnityEngine.Transform;
using Vector3 = System.Numerics.Vector3;

namespace AltNetIk
{
    public partial class AltNetIk : MelonMod
    {
        public static void OnPacketReceived(PacketData packet)
        {
            if (!IsReceiving)
                return;

            var date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            DataBank dataBank = new DataBank
            {
                timestamp = date,
                boneRotations = packet.boneRotations,
                hipPosition = packet.hipPosition,
                hipRotation = packet.hipRotation,
                playerPosition = packet.playerPosition,
                playerRotation = packet.playerRotation,
            };

            bool hasBoneData = receiverPlayerData.TryGetValue(packet.photonId, out PlayerData boneData);

            bool hasPacketData = receiverPacketData.TryGetValue(packet.photonId, out ReceiverPacketData packetData);
            if (hasPacketData)
            {
                packetData.packetsPerSecond++;
                packetData.photonId = packet.photonId;
                packetData.ping = packet.ping;
                packetData.boneList = packet.boneList;

                if (packetData.frozen != packet.frozen)
                {
                    packetData.frozen = packet.frozen;
                    if (packet.frozen)
                    {
                        if (hasBoneData && packet.avatarKind == (short)VRCAvatarManager.EnumNPublicSealedvaUnLoErBlSaPeSuFaCuUnique.Custom && boneData.avatarKind == (short)VRCAvatarManager.EnumNPublicSealedvaUnLoErBlSaPeSuFaCuUnique.Custom)
                            ToggleFreeze(packet.photonId, packet.frozen);
                    }
                    else
                    {
                        ToggleFreeze(packet.photonId, packet.frozen);
                    }
                }
                if (date != packetData.lastTimeReceived)
                {
                    packetData.lastTimeReceived = date;
                }

                if (packetData.avatarKind != packet.avatarKind || packetData.boneCount != packet.boneCount)
                {
                    packetData.avatarKind = packet.avatarKind;
                    packetData.boneCount = packet.boneCount;
                    packetData.dataBankA = dataBank;
                    packetData.dataBankB = dataBank;
                }
                else
                {
                    packetData.SwapDataBanks(dataBank);
                }

                receiverPacketData.AddOrUpdate(packet.photonId, packetData, (k, v) => packetData);
            }
            else
            {
                if (hasBoneData && packet.frozen && packet.avatarKind == (short)VRCAvatarManager.EnumNPublicSealedvaUnLoErBlSaPeSuFaCuUnique.Custom && boneData.avatarKind == (short)VRCAvatarManager.EnumNPublicSealedvaUnLoErBlSaPeSuFaCuUnique.Custom)
                    ToggleFreeze(packet.photonId, packet.frozen);

                ReceiverPacketData newPacketData = new ReceiverPacketData
                {
                    photonId = packet.photonId,
                    ping = packet.ping,
                    avatarKind = packet.avatarKind,
                    frozen = packet.frozen,
                    boneCount = packet.boneCount,
                    boneList = packet.boneList,
                    dataBankA = dataBank,
                    dataBankB = dataBank,
                    packetsPerSecond = 1,
                    lastTimeReceived = date
                };
                receiverPacketData.AddOrUpdate(packet.photonId, newPacketData, (k, v) => newPacketData);
            }
        }

        public override void OnLateUpdate()
        {
            if (!IsReceiving)
                return;

            foreach (ReceiverPacketData packetData in receiverPacketData.Values)
            {
                int photonId = packetData.photonId;

                bool hasBoneData = receiverPlayerData.TryGetValue(photonId, out PlayerData boneData);
                if (!hasBoneData)
                    continue;

                if (boneData.playerTransform == null)
                    continue;

                int lastDeltaTime = 0;
                bool hasLastDataBank = receiverLastPacket.TryGetValue(photonId, out DataBank lastDataBank);

                if (hasLastDataBank)
                    lastDeltaTime = lastDataBank.deltaTime;

                var newDataBank = new DataBank
                {
                    boneRotations = new Quaternion[boneData.boneCount]
                };

                if (!boneData.active)
                {
                    EnableReceiver(boneData);
                }
                if (boneData.playerPoseAV3Update != null)
                    boneData.playerPoseAV3Update.enabled = false;

                var dataBankA = packetData.dataBankA;
                var dataBankB = packetData.dataBankB;

                long date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                float deltaFloat;
                if (dataBankA.timestamp == dataBankB.timestamp)
                {
                    deltaFloat = 1.0f;
                }
                else
                {
                    double oldDeltaTime = date - dataBankA.timestamp;
                    long newDeltaTime = dataBankA.timestamp - dataBankB.timestamp;
                    double deltaTime = GetDeltaTime(ref lastDeltaTime, (int)newDeltaTime);
                    double delta = Math.Round((deltaTime - oldDeltaTime) / deltaTime * 100, 2) / 100;
                    deltaFloat = (float)-delta + 1;
                }
                newDataBank.deltaTime = lastDeltaTime;

                // clamp delta
                if (!enableLerp || !(deltaFloat >= 0f && deltaFloat <= 1.0f))
                    deltaFloat = 1.0f;

                int index = -1;
                for (int i = 0; i < UnityEngine.HumanTrait.BoneCount; i++)
                {
                    if (!boneData.boneList[i])
                        continue;

                    index++;

                    if (packetData.boneCount <= index || boneData.boneCount <= index)
                        break;

                    if (!packetData.boneList[i] || !boneData.transforms[index] || i == 0)
                        continue;

                    Quaternion boneRotation = Quaternion.Slerp(dataBankB.boneRotations[index], dataBankA.boneRotations[index], deltaFloat);

                    newDataBank.boneRotations[index] = boneRotation;
                    boneData.transforms[index].localRotation = (boneData.preQArray[index] * boneRotation * boneData.postQinvArray[index]).ToUnity();
                }

                var hipPosition = Vector3.Lerp(dataBankB.hipPosition, dataBankA.hipPosition, deltaFloat);
                var playerPosition = Vector3.Lerp(dataBankB.playerPosition, dataBankA.playerPosition, deltaFloat);
                var playerRotation = Quaternion.Slerp(dataBankB.playerRotation, dataBankA.playerRotation, deltaFloat);

                newDataBank.hipPosition = hipPosition;
                newDataBank.playerPosition = playerPosition;
                newDataBank.playerRotation = playerRotation;

                boneData.playerTransform.SetPositionAndRotation(playerPosition.ToUnity(), playerRotation.ToUnity());
                if (boneData.transforms.Length > 0 && boneData.transforms[0] != null && packetData.boneCount > 0)
                {
                    if (packetData.avatarKind == (short)VRCAvatarManager.EnumNPublicSealedvaUnLoErBlSaPeSuFaCuUnique.Custom && boneData.avatarKind == (short)VRCAvatarManager.EnumNPublicSealedvaUnLoErBlSaPeSuFaCuUnique.Custom)
                    {
                        Quaternion hipRotation = Quaternion.Slerp(dataBankB.boneRotations[0], dataBankA.boneRotations[0], deltaFloat);

                        newDataBank.boneRotations[0] = hipRotation;

                        boneData.transforms[0].position = hipPosition.ToUnity();
                        boneData.transforms[0].localRotation = (boneData.preQArray[0] * hipRotation * boneData.postQinvArray[0]).ToUnity();
                    }
                    else
                    {
                        Quaternion boneRotation = Quaternion.Slerp(dataBankB.hipRotation, dataBankA.hipRotation, deltaFloat);

                        newDataBank.hipRotation = boneRotation;
                        boneData.transforms[0].SetPositionAndRotation(hipPosition.ToUnity(), boneRotation.ToUnity());
                    }
                }

                newDataBank.timestamp = date;
                receiverLastPacket.AddOrUpdate(photonId, newDataBank, (k, v) => newDataBank);
            }
        }

        public static void OnParamPacketReceived(ParamData packet)
        {
            if (!IsReceiving)
                return;

            receiverParamData.AddOrUpdate(packet.photonId, packet, (k, v) => packet);
        }

        public void ApplyAvatarParams()
        {
            if (!IsReceiving)
                return;

            skipSettingParam = false;

            foreach (var packet in receiverParamData.Values)
            {
                bool hasPlayerData = receiverPlayerData.TryGetValue(packet.photonId, out PlayerData playerData);
                if (!hasPlayerData || playerData.parameters.Count < 19) // 20 total default params -1 for IsLocal equals 19
                    return;

                var byteIndex = 0;
                var byte0 = packet.paramData[byteIndex++];
                var byte1 = packet.paramData[byteIndex++];
                var paramCount = new Serializers.ShortBytesUnion(byte0, byte1).value;
                if (paramCount < 19)
                    return;

                var isFallback = playerData.avatarKind == (short)VRCAvatarManager.EnumNPublicSealedvaUnLoErBlSaPeSuFaCuUnique.Fallback;
                if (playerData.parameters.Count != paramCount)
                    isFallback = true;

                for (int i = 0; i < paramCount; i++)
                {
                    if (isFallback && i > 18) // Only apply 19 default parameters to fallback avatars
                    {
                        return;
                    }

                    var parameter = playerData.parameters[i];
                    var type = parameter.field_Public_EnumNPublicSealedvaUnBoInFl5vUnique_0;
                    var senderType = (AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique)packet.paramData[byteIndex++];
                    if (type != senderType && senderType != (AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique)10)
                    {
                        return;
                    }

                    switch (senderType)
                    {
                        case AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique.Bool:
                            if (type != senderType)
                                return;
                            var boolParam = packet.paramData[byteIndex++] != 0;
                            BoolPropertySetter(parameter.Pointer, boolParam);
                            break;

                        case AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique.Int:
                            if (type != senderType)
                                return;
                            var intParam = packet.paramData[byteIndex++];
                            IntPropertySetter(parameter.Pointer, intParam);

                            // Fix avatar limb grabber
                            //if (i == 2) // GestureLeft
                            //    playerData.playerHandGestureController.field_Private_Gesture_0 = (HandGestureController.Gesture)intParam;
                            //else if (i == 4) // GestureRight
                            //    playerData.playerHandGestureController.field_Private_Gesture_2 = (HandGestureController.Gesture)intParam;
                            break;

                        case AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique.Float:
                            if (type != senderType)
                                return;
                            var floatParam = Serializers.DeserializeFloat(packet.paramData[byteIndex++]);
                            FloatPropertySetter(parameter.Pointer, floatParam);
                            break;

                        case (AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique)10:
                            if (type != AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique.Float)
                                return;

                            var b0 = packet.paramData[byteIndex++];
                            var b1 = packet.paramData[byteIndex++];
                            var precisionFloatParam = Serializers.DeserializeFloatFromShortBytes(b0, b1);
                            FloatPropertySetter(parameter.Pointer, precisionFloatParam);
                            break;
                    }
                }
            }

            skipSettingParam = true;
        }

        public void OnEventPacketReceived(EventData packet)
        {
            switch (packet.eventName)
            {
                case "DisableSender":
                    IsSending = false;
                    break;

                case "EnableSender":
                    IsSending = true;
                    break;

                case "PlayerDisconnect":
                    DisableReceiver(packet.photonId);
                    break;

                case "RenegotiateServer":
                    if (currentPhotonId != 0)
                        NegotiateServer();
                    break;

                case "Message":
                    Logger.Warning($"Server message: {packet.data}");
                    break;
            }
        }

        private void TimeoutCheck()
        {
            var date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (ReceiverPacketData packetData in receiverPacketData.Values)
            {
                if (packetData.lastTimeReceived > 0 && date - packetData.lastTimeReceived >= 6000)
                {
                    //player connection died
                    DisableReceiver(packetData.photonId);
                }
            }
        }

        private static void ToggleFreeze(int photonId, bool frozen)
        {
            bool hasBoneData = receiverPlayerData.TryGetValue(photonId, out PlayerData boneData);
            if (!hasBoneData)
                return;

            RemoveFreeze(photonId);
            if (frozen)
            {
                var avatarTransform = boneData.playerTransform.Find("ForwardDirection/Avatar");
                if (avatarTransform == null)
                    return;
                GameObject avatar = avatarTransform.gameObject;
                GameObject avatarClone = Object.Instantiate(avatar);
                foreach (Component component in avatarClone.GetComponents<Component>())
                {
                    if (component == null)
                        continue;

                    if (component.GetIl2CppType() != Il2CppType.Of<Transform>())
                    {
                        Object.Destroy(component);
                    }
                }

                avatarClone.transform.SetPositionAndRotation(boneData.playerTransform.position, boneData.playerTransform.rotation);

                var pbComponents = avatarClone.GetComponentsInChildren<VRCPhysBone>();
                foreach (var pb in pbComponents)
                {
                    var byteArray = Guid.NewGuid().ToByteArray();
                    pb.chainId = BitConverter.ToUInt64(byteArray, 0);
                    PhysBoneManager.Inst.AddPhysBone(pb);
                }

                frozenPlayers.Add(photonId, avatarClone);
            }
        }

        private static void RemoveFreeze(int photonId)
        {
            bool hasFrozenPlayer = frozenPlayers.TryGetValue(photonId, out GameObject frozenPlayer);
            if (hasFrozenPlayer)
            {
                if (frozenPlayer != null)
                    Object.Destroy(frozenPlayer);
                frozenPlayers.Remove(photonId);
            }
        }

        private void DisableReceivers()
        {
            foreach (ReceiverPacketData packetData in receiverPacketData.Values)
            {
                DisableReceiver(packetData.photonId);
            }
            boolParamsInUse.Clear();
            intParamsInUse.Clear();
            floatParamsInUse.Clear();
            UpdateNamePlates();
        }

        private void DisableReceiver(int photonId)
        {
            RemoveFreeze(photonId);
            receiverLastPacket.TryRemove(photonId, out _);
            receiverParamData.TryRemove(photonId, out _);
            receiverPacketData.TryRemove(photonId, out _);
            bool hasReceiverPlayerData = receiverPlayerData.TryGetValue(photonId, out PlayerData playerData);
            if (hasReceiverPlayerData)
            {
                RemoveParamsInUse(playerData.parameters);
                if (playerData.playerTransform == null)
                    return;

                playerData.active = false;

                var poseRecorder = playerData.playerAnimationController.GetComponent<PoseRecorder>();
                if (poseRecorder != null) poseRecorder.enabled = true;

                var handGestureController = playerData.playerAnimationController.GetComponent<HandGestureController>();
                if (handGestureController != null) handGestureController.enabled = true;

                var poseAv3Update = playerData.playerAnimationController.GetComponent<PoseAV3Update>();
                if (poseAv3Update != null) poseAv3Update.enabled = true;

                //var vrcVrIkController = playerData.playerAnimationController.GetComponentInChildren<VRCVrIkController>();
                //if (vrcVrIkController != null) vrcVrIkController.enabled = true;

                receiverPlayerData.AddOrUpdate(photonId, playerData, (k, v) => playerData);
            }
            bool hasPlayerNamePlate = playerNamePlates.TryGetValue(photonId, out NamePlateInfo namePlateInfo);
            if (hasPlayerNamePlate)
            {
                namePlateInfo.namePlate.SetActive(false);
            }
            Logger.Msg($"Disable receiver {photonId}");
        }

        private void EnableReceiver(PlayerData playerData)
        {
            Logger.Msg($"Enabling receiver {playerData.photonId}");
            var poseRecorder = playerData.playerAnimationController.GetComponent<PoseRecorder>();
            if (poseRecorder == null) return;
            poseRecorder.enabled = false;

            Logger.Msg("Pose recorder disabled");

            var handGestureController = playerData.playerAnimationController.GetComponent<HandGestureController>();
            if (handGestureController == null) return;
            handGestureController.enabled = false;

            Logger.Msg("Hand gesture controller disabled");

            var poseAv3Update = playerData.playerAnimationController.GetComponent<PoseAV3Update>();
            if (poseAv3Update != null)
            {
                poseAv3Update.enabled = false;
                Logger.Msg("Pose av3 update disabled");
            }

            //var vrcVrIkController = playerData.playerAnimationController.GetComponentInChildren<VRCVrIkController>();
            //if (vrcVrIkController == null) return;
            //vrcVrIkController.enabled = false;

            Logger.Msg("VRC VR IK controller disabled");

            playerData.active = true;
            AddParamsInUse(playerData.parameters);
            receiverPlayerData.AddOrUpdate(playerData.photonId, playerData, (k, v) => playerData);

            Logger.Msg("Enabled receiver");
        }

        private void AddParamsInUse(List<AvatarParameterAccess> parameters)
        {
            foreach (var parameter in parameters)
            {
                switch (parameter.field_Public_EnumNPublicSealedvaUnBoInFl5vUnique_0)
                {
                    case AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique.Bool:
                        if (!boolParamsInUse.Contains(parameter.Pointer))
                            boolParamsInUse.Add(parameter.Pointer);
                        break;

                    case AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique.Int:
                        if (!intParamsInUse.Contains(parameter.Pointer))
                            intParamsInUse.Add(parameter.Pointer);
                        break;

                    case AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique.Float:
                        if (!floatParamsInUse.Contains(parameter.Pointer))
                            floatParamsInUse.Add(parameter.Pointer);
                        break;
                }
            }
        }

        private void RemoveParamsInUse(List<AvatarParameterAccess> parameters)
        {
            foreach (var parameter in parameters)
            {
                switch (parameter.field_Public_EnumNPublicSealedvaUnBoInFl5vUnique_0)
                {
                    case AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique.Bool:
                        boolParamsInUse.Remove(parameter.Pointer);
                        break;

                    case AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique.Int:
                        intParamsInUse.Remove(parameter.Pointer);
                        break;

                    case AvatarParameterAccess.EnumNPublicSealedvaUnBoInFl5vUnique.Float:
                        floatParamsInUse.Remove(parameter.Pointer);
                        break;
                }
            }
        }
    }
}