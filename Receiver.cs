using MelonLoader;
using System;
using UnhollowerRuntimeLib;
using UnityEngine;
using VRC.Dynamics;
using VRC.Playables;
using VRC.SDK3.Dynamics.PhysBone.Components;
using Object = UnityEngine.Object;

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
                        if (hasBoneData && packet.avatarKind == (short)VRCAvatarManager.AvatarKind.Custom && boneData.avatarKind == (short)VRCAvatarManager.AvatarKind.Custom)
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
                    packetData.dataBank1 = dataBank;
                    packetData.dataBank2 = dataBank;
                }

                if (packetData.bankSelector == 1)
                {
                    packetData.bankSelector = 2;
                    packetData.dataBank2 = dataBank;
                }
                else
                {
                    packetData.bankSelector = 1;
                    packetData.dataBank1 = dataBank;
                }

                receiverPacketData.AddOrUpdate(packet.photonId, packetData, (k, v) => packetData);
            }
            else
            {
                if (hasBoneData && packet.frozen && packet.avatarKind == (short)VRCAvatarManager.AvatarKind.Custom && boneData.avatarKind == (short)VRCAvatarManager.AvatarKind.Custom)
                    ToggleFreeze(packet.photonId, packet.frozen);

                ReceiverPacketData newPacketData = new ReceiverPacketData
                {
                    photonId = packet.photonId,
                    ping = packet.ping,
                    avatarKind = packet.avatarKind,
                    frozen = packet.frozen,
                    boneCount = packet.boneCount,
                    boneList = packet.boneList,
                    dataBank1 = dataBank,
                    dataBank2 = dataBank,
                    bankSelector = 1,
                    packetsPerSecond = 1,
                    lastTimeReceived = date
                };
                receiverPacketData.AddOrUpdate(packet.photonId, newPacketData, (k, v) => newPacketData);
            }
        }

        public override void OnLateUpdate()
        {
            if (!IsConnected || !IsReceiving) return;

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
                    boneData.active = true;
                    boneData.playerPoseRecorder.enabled = false;
                    boneData.playerHandGestureController.enabled = false;
                    boneData.playerAnimControlNetSerializer.enabled = false;
                    receiverPlayerData.AddOrUpdate(photonId, boneData, (k, v) => boneData);
                }

                Int64 date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                float deltaFloat;
                if (packetData.dataBank1.timestamp == packetData.dataBank2.timestamp)
                {
                    deltaFloat = 1.0f;
                }
                else if (packetData.bankSelector == 1)
                {
                    double date0 = date - packetData.dataBank1.timestamp;
                    long newDeltaTime = packetData.dataBank1.timestamp - packetData.dataBank2.timestamp;
                    double deltaTime = GetDeltaTime(ref lastDataBank.deltaTime, (int)newDeltaTime);
                    newDataBank.deltaTime = lastDataBank.deltaTime;
                    double delta = Math.Round((deltaTime - date0) / deltaTime * 100, 2) / 100;
                    deltaFloat = (float)-delta + 1;
                }
                else
                {
                    double date1 = date - packetData.dataBank2.timestamp;
                    long newDeltaTime = packetData.dataBank2.timestamp - packetData.dataBank1.timestamp;
                    double deltaTime = GetDeltaTime(ref lastDataBank.deltaTime, (int)newDeltaTime);
                    newDataBank.deltaTime = lastDataBank.deltaTime;
                    double delta = Math.Round((deltaTime - date1) / deltaTime * 100, 2) / 100;
                    deltaFloat = (float)-delta + 1;
                }

                // clamp delta
                if (!enableLerp || !(deltaFloat >= 0f && deltaFloat <= 1.0f))
                    deltaFloat = 1.0f;

                int index = -1;
                for (int i = 0; i < HumanTrait.BoneCount; i++)
                {
                    if (!boneData.boneList[i])
                        continue;
                    index++;

                    if (packetData.boneCount <= index || boneData.boneCount <= index)
                        break;
                    if (!packetData.boneList[i] || !boneData.transforms[index] || i == 0)
                        continue;

                    Quaternion boneRotation;
                    if (packetData.bankSelector == 1)
                    {
                        boneRotation = LerpUnclamped(packetData.dataBank2.boneRotations[index], packetData.dataBank1.boneRotations[index], deltaFloat);
                    }
                    else
                    {
                        boneRotation = LerpUnclamped(packetData.dataBank1.boneRotations[index], packetData.dataBank2.boneRotations[index], deltaFloat);
                    }

                    newDataBank.boneRotations[index] = boneRotation;
                    boneData.transforms[index].localRotation = boneData.preQArray[index] * boneRotation * boneData.postQinvArray[index];
                }

                if (packetData.bankSelector == 1)
                {
                    Quaternion playerRotation = LerpUnclamped(packetData.dataBank2.playerRotation, packetData.dataBank1.playerRotation, deltaFloat);
                    newDataBank.playerRotation = playerRotation;
                    Vector3 playerPosition = LerpUnclamped(packetData.dataBank2.playerPosition, packetData.dataBank1.playerPosition, deltaFloat);
                    newDataBank.playerPosition = playerPosition;
                    Vector3 hipPosition = LerpUnclamped(packetData.dataBank2.hipPosition, packetData.dataBank1.hipPosition, deltaFloat);
                    newDataBank.hipPosition = hipPosition;
                    boneData.playerTransform.SetPositionAndRotation(playerPosition, playerRotation);
                    if (boneData.transforms.Length > 0 && boneData.transforms[0] != null)
                    {
                        if (packetData.avatarKind == (short)VRCAvatarManager.AvatarKind.Custom && boneData.avatarKind == (short)VRCAvatarManager.AvatarKind.Custom)
                        {
                            boneData.transforms[0].position = hipPosition;
                            Quaternion hipRotation = LerpUnclamped(packetData.dataBank2.boneRotations[0], packetData.dataBank1.boneRotations[0], deltaFloat);
                            newDataBank.boneRotations[0] = hipRotation;
                            boneData.transforms[0].localRotation = boneData.preQArray[0] * hipRotation * boneData.postQinvArray[0];
                        }
                        else
                        {
                            Quaternion boneRotation = LerpUnclamped(packetData.dataBank2.hipRotation, packetData.dataBank1.hipRotation, deltaFloat);
                            Quaternion hipRotation = boneData.preQArray[0] * boneRotation * boneData.postQinvArray[0];
                            newDataBank.hipRotation = hipRotation;
                            boneData.transforms[0].SetPositionAndRotation(hipPosition, hipRotation);
                        }
                    }
                }
                else
                {
                    Quaternion playerRotation = LerpUnclamped(packetData.dataBank1.playerRotation, packetData.dataBank2.playerRotation, deltaFloat);
                    newDataBank.playerRotation = playerRotation;
                    Vector3 playerPosition = LerpUnclamped(packetData.dataBank1.playerPosition, packetData.dataBank2.playerPosition, deltaFloat);
                    newDataBank.playerPosition = playerPosition;
                    Vector3 hipPosition = LerpUnclamped(packetData.dataBank1.hipPosition, packetData.dataBank2.hipPosition, deltaFloat);
                    newDataBank.hipPosition = hipPosition;
                    boneData.playerTransform.SetPositionAndRotation(playerPosition, playerRotation);
                    if (boneData.transforms.Length > 0 && boneData.transforms[0] != null)
                    {
                        if (packetData.avatarKind == (short)VRCAvatarManager.AvatarKind.Custom && boneData.avatarKind == (short)VRCAvatarManager.AvatarKind.Custom)
                        {
                            boneData.transforms[0].position = hipPosition;
                            Quaternion hipRotation = LerpUnclamped(packetData.dataBank1.boneRotations[0], packetData.dataBank2.boneRotations[0], deltaFloat);
                            newDataBank.boneRotations[0] = hipRotation;
                            boneData.transforms[0].localRotation = boneData.preQArray[0] * hipRotation * boneData.postQinvArray[0];
                        }
                        else
                        {
                            Quaternion boneRotation = LerpUnclamped(packetData.dataBank1.hipRotation, packetData.dataBank2.hipRotation, deltaFloat);
                            Quaternion hipRotation = boneData.preQArray[0] * boneRotation * boneData.postQinvArray[0];
                            newDataBank.hipRotation = hipRotation;
                            boneData.transforms[0].SetPositionAndRotation(hipPosition, hipRotation);
                        }
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

            bool hasPlayerData = receiverPlayerData.TryGetValue(packet.photonId, out PlayerData playerData);
            if (!hasPlayerData)
                return;

            short boolIndex = 0;
            short intIndex = 0;
            short floatIndex = 0;
            for (int i = 0; i < packet.paramName.Length; i++)
            {
                bool hasParamName = playerData.parameters.TryGetValue(packet.paramName[i], out AvatarParameter parameter);
                if (!hasParamName || parameter == null)
                    continue;

                var type = parameter.field_Private_ParameterType_0;
                var senderType = (AvatarParameter.ParameterType)packet.paramType[i];
                if (type != senderType)
                    continue;

                switch (type)
                {
                    case AvatarParameter.ParameterType.Bool:
                        _boolPropertySetterDelegate(parameter.Pointer, packet.boolParams[boolIndex]);
                        boolIndex++;
                        break;

                    case AvatarParameter.ParameterType.Int:
                        _intPropertySetterDelegate(parameter.Pointer, packet.intParams[intIndex]);
                        intIndex++;
                        break;

                    case AvatarParameter.ParameterType.Float:
                        _floatPropertySetterDelegate(parameter.Pointer, packet.floatParams[floatIndex]);
                        floatIndex++;
                        break;
                }

                // Fix avatar limb grabber
                if (packet.paramName[i] == "GestureLeft")
                {
                    if (packet.intParams[intIndex - 1] == 1)
                    {
                        if (playerData.playerHandGestureController.field_Private_Gesture_0 != HandGestureController.Gesture.Fist)
                            playerData.playerHandGestureController.field_Private_Gesture_0 = HandGestureController.Gesture.Fist;
                    }
                    else
                    {
                        if (playerData.playerHandGestureController.field_Private_Gesture_0 == HandGestureController.Gesture.Fist)
                            playerData.playerHandGestureController.field_Private_Gesture_0 = HandGestureController.Gesture.None;
                    }
                }
                else if (packet.paramName[i] == "GestureRight")
                {
                    if (packet.intParams[intIndex - 1] == 1)
                    {
                        if (playerData.playerHandGestureController.field_Private_Gesture_2 != HandGestureController.Gesture.Fist)
                            playerData.playerHandGestureController.field_Private_Gesture_2 = HandGestureController.Gesture.Fist;
                    }
                    else
                    {
                        if (playerData.playerHandGestureController.field_Private_Gesture_2 == HandGestureController.Gesture.Fist)
                            playerData.playerHandGestureController.field_Private_Gesture_2 = HandGestureController.Gesture.None;
                    }
                }
            }
        }

        public static void OnEventPacketReceived(EventData packet)
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

                case "Message":
                    VRCUiManager.prop_VRCUiManager_0.field_Private_Single_0 = 0f;
                    VRCUiManager.prop_VRCUiManager_0.field_Private_Single_1 = 0f;
                    VRCUiManager.prop_VRCUiManager_0.field_Private_Single_2 = 0f;
                    VRCUiManager.prop_VRCUiManager_0.field_Private_List_1_String_0.Add($"[AltNetIk]: {packet.data}");
                    Logger.Warning($"Server message: {packet.data}");
                    break;
            }
            Buttons.UpdateAllButtons();
        }

        private void TimeoutCheck()
        {
            var date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (ReceiverPacketData packetData in receiverPacketData.Values)
            {
                if (packetData.lastTimeReceived > 0 && date - packetData.lastTimeReceived >= 3000)
                {
                    //player connection died
                    receiverPacketData.TryRemove(packetData.photonId, out _);
                    bool hasReceiverPlayerData = receiverPlayerData.TryGetValue(packetData.photonId, out PlayerData playerData);
                    if (playerData.playerTransform == null)
                        continue;
                    if (hasReceiverPlayerData)
                    {
                        playerData.active = false;
                        playerData.playerPoseRecorder.enabled = true;
                        playerData.playerHandGestureController.enabled = true;
                        playerData.playerAnimControlNetSerializer.enabled = true;
                        receiverPlayerData.AddOrUpdate(packetData.photonId, playerData, (k, v) => playerData);
                    }
                    bool hasPlayerNamePlate = playerNamePlates.TryGetValue(packetData.photonId, out NamePlateInfo namePlateInfo);
                    if (hasPlayerNamePlate)
                    {
                        namePlateInfo.namePlate.SetActive(false);
                        namePlateInfo.namePlateStatusLine.localPosition = new Vector3(0.0066f, -58f, 0f);
                    }
                    RemoveFreeze(packetData.photonId);
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
                var avatarTransfrom = boneData.playerTransform.Find("ForwardDirection/Avatar");
                if (avatarTransfrom == null)
                    return;
                GameObject avatar = avatarTransfrom.gameObject;
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
            UpdateNamePlates();
        }

        private static void DisableReceiver(int photonId)
        {
            receiverPacketData.TryRemove(photonId, out _);
            bool hasReceiverPlayerData = receiverPlayerData.TryGetValue(photonId, out PlayerData playerData);
            if (playerData.playerTransform == null)
                return;
            if (hasReceiverPlayerData)
            {
                playerData.active = false;
                playerData.playerPoseRecorder.enabled = true;
                playerData.playerHandGestureController.enabled = true;
                playerData.playerAnimControlNetSerializer.enabled = true;
                receiverPlayerData.AddOrUpdate(photonId, playerData, (k, v) => playerData);
            }
            bool hasPlayerNamePlate = playerNamePlates.TryGetValue(photonId, out NamePlateInfo namePlateInfo);
            if (hasPlayerNamePlate)
            {
                namePlateInfo.namePlate.SetActive(false);
                namePlateInfo.namePlateStatusLine.localPosition = new Vector3(0.0066f, -58f, 0f);
            }
            RemoveFreeze(photonId);
        }
    }
}