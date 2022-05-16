using MelonLoader;
using System.Collections.Generic;
using UnityEngine;
using VRC;
using VRC.Networking;
using VRC.Playables;

namespace AltNetIk
{
    public partial class AltNetIk : MelonMod
    {
        public void SetReceiverBones(VRCPlayer player, VRCAvatarManager avatarManager, short avatarKind)
        {
            int photonId = player.prop_PhotonView_0.field_Private_Int32_0;
            int boneCount = 0;
            bool[] boneList = new bool[55];

            var avatarParams = avatarManager.field_Private_AvatarPlayableController_0?.field_Private_Dictionary_2_Int32_AvatarParameter_0;
            var parameters = new Dictionary<string, AvatarParameter>();
            short boolParams = 0;
            short intParams = 0;
            short floatParams = 0;
            if (avatarParams != null)
            {
                foreach (var param in avatarParams.Values)
                {
                    // don't want to set IsLocal status of others. that makes no sense.
                    var parameterName = param.field_Private_String_0;
                    if (parameterName == "IsLocal")
                        continue;

                    var type = param.field_Private_ParameterType_0;
                    switch (type)
                    {
                        case AvatarParameter.ParameterType.Bool:
                            boolParams++;
                            break;

                        case AvatarParameter.ParameterType.Int:
                            intParams++;
                            break;

                        case AvatarParameter.ParameterType.Float:
                            floatParams++;
                            break;
                    }

                    parameters.Add(parameterName, param);
                }
            }

            var animationController = player.field_Private_AnimatorControllerManager_0;

            Animator animator = avatarManager.field_Private_Animator_0;
            bool isSdk2 = avatarManager.prop_VRCAvatarDescriptor_0 == null;
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                PlayerData emptyBoneData = new PlayerData
                {
                    photonId = photonId,
                    playerTransform = player.transform,
                    playerPoseRecorder = animationController.GetComponent<PoseRecorder>(),
                    playerHandGestureController = animationController.GetComponent<HandGestureController>(),
                    playerAnimControlNetSerializer = animationController.GetComponentInChildren<FlatBufferNetworkSerializer>(),
                    playerIkController = animationController.GetComponentInChildren<IkController>(),
                    playerVRCVrIkController = animationController.GetComponentInChildren<VRCVrIkController>(),
                    preQArray = new Quaternion[boneCount],
                    preQinvArray = new Quaternion[boneCount],
                    postQArray = new Quaternion[boneCount],
                    postQinvArray = new Quaternion[boneCount],
                    transforms = new Transform[boneCount],
                    boneCount = boneCount,
                    boneList = boneList,
                    parameters = parameters,
                    avatarKind = avatarKind,
                    active = false,
                    isSdk2 = isSdk2
                };
                receiverPlayerData.AddOrUpdate(photonId, emptyBoneData, (k, v) => emptyBoneData);
                return;
            }

            var avatar = animator.avatar;
            var hd = avatar.humanDescription;
            var human = hd.human;
            for (int i = 0; i < human.Length; i++)
            {
                HumanBone humanBone = human[i];
                int boneIndex = boneNames.FindIndex(a => string.Equals(a, humanBone.humanName));
                if (boneIndex < 0 || humanBone.humanName == "LeftEye" || humanBone.humanName == "RightEye")
                    continue;

                boneCount++;
                boneList[boneIndex] = true;
            }
            if (boneCount == 0)
            {
                // legacy fallback
                int bodyBoneIndex = -1;
                for (int i = 0; i < 55; i++)
                {
                    HumanBodyBones bodyBone = (HumanBodyBones)i;
                    Transform bone = animator.GetBoneTransform(bodyBone);
                    if (bone == null || i == (int)HumanBodyBones.LeftEye || i == (int)HumanBodyBones.RightEye)
                        continue;

                    bodyBoneIndex++;
                    boneCount++;
                    boneList[bodyBoneIndex] = true;
                }
            }

            PlayerData boneData = new PlayerData
            {
                photonId = photonId,
                playerTransform = player.transform,
                playerPoseRecorder = animationController.GetComponent<PoseRecorder>(),
                playerHandGestureController = animationController.GetComponent<HandGestureController>(),
                playerAnimControlNetSerializer = animationController.GetComponentInChildren<FlatBufferNetworkSerializer>(),
                playerIkController = animationController.GetComponentInChildren<IkController>(),
                playerVRCVrIkController = animationController.GetComponentInChildren<VRCVrIkController>(),
                preQArray = new Quaternion[boneCount],
                preQinvArray = new Quaternion[boneCount],
                postQArray = new Quaternion[boneCount],
                postQinvArray = new Quaternion[boneCount],
                transforms = new Transform[boneCount],
                boneCount = boneCount,
                boneList = boneList,
                parameters = parameters,
                avatarKind = avatarKind,
                active = false,
                isSdk2 = isSdk2
            };

            int index = -1;
            for (int i = 0; i < 55; i++)
            {
                if (!boneList[i])
                    continue;
                index++;

                Quaternion preQ = boneData.preQArray[index] = avatar.GetPreRotation(i);
                Quaternion postQ = boneData.postQArray[index] = avatar.GetPostRotation(i);
                boneData.preQinvArray[index] = Quaternion.Inverse(preQ);
                boneData.postQinvArray[index] = Quaternion.Inverse(postQ);
                boneData.transforms[index] = animator.GetBoneTransform((HumanBodyBones)i);
            }

            bool hasPacketData = receiverPacketData.TryGetValue(photonId, out ReceiverPacketData packetData);
            if (hasPacketData)
            {
                boneData.active = true;
                boneData.playerPoseRecorder.enabled = false;
                boneData.playerHandGestureController.enabled = false;
                boneData.playerAnimControlNetSerializer.enabled = false;
            }

            receiverPlayerData.AddOrUpdate(photonId, boneData, (k, v) => boneData);
        }

        public void SetSenderBones(VRCPlayer player, VRCAvatarManager avatarManager, short avatarKind)
        {
            int photonId = player.prop_PhotonView_0.field_Private_Int32_0;
            int boneCount = 0;
            bool[] boneList = new bool[55];

            var avatarParams = avatarManager.field_Private_AvatarPlayableController_0?.field_Private_Dictionary_2_Int32_AvatarParameter_0;
            var parameters = new Dictionary<string, AvatarParameter>();
            senderParamData = new ParamData();
            short totalParams = 0;
            short boolParams = 0;
            short intParams = 0;
            short floatParams = 0;
            if (avatarParams != null)
            {
                foreach (var param in avatarParams.Values)
                {
                    // don't want to send our IsLocal status to others. that makes no sense.
                    var parameterName = param.field_Private_String_0;
                    if (parameterName == "IsLocal")
                        continue;

                    var type = param.field_Private_ParameterType_0;
                    switch (type)
                    {
                        case AvatarParameter.ParameterType.Bool:
                            boolParams++;
                            break;

                        case AvatarParameter.ParameterType.Int:
                            intParams++;
                            break;

                        case AvatarParameter.ParameterType.Float:
                            floatParams++;
                            break;
                    }

                    parameters.Add(parameterName, param);
                    totalParams++;
                }
                senderParamData = new ParamData
                {
                    paramName = new string[totalParams],
                    paramType = new short[totalParams],
                    boolParams = new bool[boolParams],
                    intParams = new short[intParams],
                    floatParams = new float[floatParams]
                };
            }

            senderPacketData = new PacketData();
            Animator animator = avatarManager.field_Private_Animator_0;
            bool isSdk2 = avatarManager.prop_VRCAvatarDescriptor_0 == null;

            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Logger.Msg("Avatar is null");
                senderPlayerData = new PlayerData
                {
                    playerTransform = player.transform,
                    playerAvatarManager = avatarManager,
                    preQArray = new Quaternion[boneCount],
                    preQinvArray = new Quaternion[boneCount],
                    postQArray = new Quaternion[boneCount],
                    postQinvArray = new Quaternion[boneCount],
                    transforms = new Transform[boneCount],
                    boneCount = boneCount,
                    boneList = boneList,
                    parameters = parameters,
                    avatarKind = avatarKind,
                    isSdk2 = isSdk2
                };
                return;
            }

            var avatar = animator.avatar;
            var hd = avatar.humanDescription;
            var human = hd.human;
            for (int i = 0; i < human.Length; i++)
            {
                HumanBone humanBone = human[i];
                int boneIndex = boneNames.FindIndex(a => string.Equals(a, humanBone.humanName));
                if (boneIndex < 0 || humanBone.humanName == "LeftEye" || humanBone.humanName == "RightEye")
                    continue;

                boneCount++;
                boneList[boneIndex] = true;
            }
            if (boneCount == 0)
            {
                // legacy fallback
                int bodyBoneIndex = -1;
                for (int i = 0; i < 55; i++)
                {
                    HumanBodyBones bodyBone = (HumanBodyBones)i;
                    Transform bone = animator.GetBoneTransform(bodyBone);
                    if (bone == null || i == (int)HumanBodyBones.LeftEye || i == (int)HumanBodyBones.RightEye)
                        continue;

                    bodyBoneIndex++;
                    boneCount++;
                    boneList[bodyBoneIndex] = true;
                }
            }

            senderPlayerData = new PlayerData
            {
                playerTransform = player.transform,
                playerAvatarManager = avatarManager,
                preQArray = new Quaternion[boneCount],
                preQinvArray = new Quaternion[boneCount],
                postQArray = new Quaternion[boneCount],
                postQinvArray = new Quaternion[boneCount],
                transforms = new Transform[boneCount],
                boneCount = boneCount,
                boneList = boneList,
                parameters = parameters,
                avatarKind = avatarKind,
                isSdk2 = isSdk2
            };

            int index = -1;
            for (int i = 0; i < 55; i++)
            {
                if (!boneList[i])
                    continue;
                index++;

                Quaternion preQ = senderPlayerData.preQArray[index] = avatar.GetPreRotation(i);
                Quaternion postQ = senderPlayerData.postQArray[index] = avatar.GetPostRotation(i);
                senderPlayerData.preQinvArray[index] = Quaternion.Inverse(preQ);
                senderPlayerData.postQinvArray[index] = Quaternion.Inverse(postQ);
                senderPlayerData.transforms[index] = animator.GetBoneTransform((HumanBodyBones)i);
            }
        }
    }
}