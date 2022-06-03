using MelonLoader;
using System.Collections.Generic;
using VRC;
using VRC.Playables;
using System.Numerics;
using ReMod.Core;

namespace AltNetIk
{
    public partial class AltNetIk : ModComponent
    {
        public void SetReceiverBones(VRCPlayer player, VRCAvatarManager avatarManager, short avatarKind)
        {
            int photonId = player.prop_PhotonView_0.field_Private_Int32_0;
            int boneCount = 0;
            bool[] boneList = new bool[UnityEngine.HumanTrait.BoneCount];

            var avatarParams = avatarManager.field_Private_AvatarPlayableController_0?.field_Private_Dictionary_2_Int32_AvatarParameter_0;
            var parameters = new List<AvatarParameter>();
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

                    var type = param.field_Public_ParameterType_0;
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

                    parameters.Add(param);
                }
            }

            var animationController = player.field_Private_AnimatorControllerManager_0;

            UnityEngine.Animator animator = avatarManager.field_Private_Animator_0;
            bool isSdk2 = avatarManager.prop_VRCAvatarDescriptor_0 == null;
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                PlayerData emptyBoneData = new PlayerData
                {
                    photonId = photonId,
                    playerTransform = player.transform,
                    playerAvatarManager = avatarManager,
                    playerPoseRecorder = animationController.GetComponent<PoseRecorder>(),
                    playerHandGestureController = animationController.GetComponent<HandGestureController>(),
                    playerVRCVrIkController = animationController.GetComponentInChildren<VRCVrIkController>(),
                    preQArray = new Quaternion[boneCount],
                    preQinvArray = new Quaternion[boneCount],
                    postQArray = new Quaternion[boneCount],
                    postQinvArray = new Quaternion[boneCount],
                    transforms = new UnityEngine.Transform[boneCount],
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
            foreach (UnityEngine.HumanBone humanBone in avatar.humanDescription.human)
            {
                if (humanBone.humanName.EndsWith("Eye"))
                    continue;

                int boneIndex = UnityEngine.HumanTrait.BoneName.IndexOf(humanBone.humanName);
                if (boneIndex < 0)
                    continue;

                boneCount++;
                boneList[boneIndex] = true;
            }
            if (boneCount == 0)
            {
                // legacy fallback
                int bodyBoneIndex = -1;
                for (int i = 0; i < UnityEngine.HumanTrait.BoneCount; i++)
                {
                    UnityEngine.HumanBodyBones bodyBone = (UnityEngine.HumanBodyBones)i;
                    UnityEngine.Transform bone = animator.GetBoneTransform(bodyBone);
                    if (bone == null || i == (int)UnityEngine.HumanBodyBones.LeftEye || i == (int)UnityEngine.HumanBodyBones.RightEye)
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
                playerAvatarManager = avatarManager,
                playerPoseRecorder = animationController.GetComponent<PoseRecorder>(),
                playerHandGestureController = animationController.GetComponent<HandGestureController>(),
                playerVRCVrIkController = animationController.GetComponentInChildren<VRCVrIkController>(),
                preQArray = new Quaternion[boneCount],
                preQinvArray = new Quaternion[boneCount],
                postQArray = new Quaternion[boneCount],
                postQinvArray = new Quaternion[boneCount],
                transforms = new UnityEngine.Transform[boneCount],
                boneCount = boneCount,
                boneList = boneList,
                parameters = parameters,
                avatarKind = avatarKind,
                active = false,
                isSdk2 = isSdk2
            };

            int index = -1;
            for (int i = 0; i < UnityEngine.HumanTrait.BoneCount; i++)
            {
                if (!boneList[i])
                    continue;
                index++;

                Quaternion preQ = boneData.preQArray[index] = avatar.GetPreRotation(i).ToSystem();
                Quaternion postQ = boneData.postQArray[index] = avatar.GetPostRotation(i).ToSystem();
                boneData.preQinvArray[index] = Quaternion.Inverse(preQ);
                boneData.postQinvArray[index] = Quaternion.Inverse(postQ);
                boneData.transforms[index] = animator.GetBoneTransform((UnityEngine.HumanBodyBones)i);
            }

            bool hasPacketData = receiverPacketData.TryGetValue(photonId, out ReceiverPacketData packetData);
            if (hasPacketData)
            {
                boneData.active = true;
                boneData.playerPoseRecorder.enabled = false;
                boneData.playerHandGestureController.enabled = false;
                boneData.playerVRCVrIkController.enabled = false;
            }

            receiverPlayerData.AddOrUpdate(photonId, boneData, (k, v) => boneData);
        }

        public void SetSenderBones(VRCPlayer player, VRCAvatarManager avatarManager, short avatarKind)
        {
            int photonId = player.prop_PhotonView_0.field_Private_Int32_0;
            int boneCount = 0;
            bool[] boneList = new bool[UnityEngine.HumanTrait.BoneCount];

            var avatarParams = avatarManager.field_Private_AvatarPlayableController_0?.field_Private_Dictionary_2_Int32_AvatarParameter_0;
            var parameters = new List<AvatarParameter>();
            senderParamData = new ParamData();
            if (avatarParams != null)
            {
                var paramCount = 0;
                foreach (var param in avatarParams.Values)
                {
                    // don't want to send our IsLocal status to others. that makes no sense.
                    var parameterName = param.field_Private_String_0;
                    if (parameterName == "IsLocal")
                        continue;

                    paramCount++;
                    parameters.Add(param);
                }
                senderParamData = new ParamData
                {
                    photonId = photonId,
                    paramData = new byte[paramCount * 2]
                };
            }

            senderPacketData = new PacketData();
            UnityEngine.Animator animator = avatarManager.field_Private_Animator_0;
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
                    transforms = new UnityEngine.Transform[boneCount],
                    boneCount = boneCount,
                    boneList = boneList,
                    parameters = parameters,
                    avatarKind = avatarKind,
                    isSdk2 = isSdk2
                };
                return;
            }

            var avatar = animator.avatar;
            foreach (UnityEngine.HumanBone humanBone in avatar.humanDescription.human)
            {
                string boneName = humanBone.humanName;

                if (boneName.EndsWith("Eye"))
                    continue;

                int boneIndex = UnityEngine.HumanTrait.BoneName.IndexOf(boneName);
                if (boneIndex < 0)
                    continue;

                boneCount++;
                boneList[boneIndex] = true;
            }
            if (boneCount == 0)
            {
                // legacy fallback
                int bodyBoneIndex = -1;
                for (int i = 0; i < UnityEngine.HumanTrait.BoneCount; i++)
                {
                    if (i == (int)UnityEngine.HumanBodyBones.LeftEye || i == (int)UnityEngine.HumanBodyBones.RightEye)
                        continue;

                    UnityEngine.HumanBodyBones bodyBone = (UnityEngine.HumanBodyBones)i;
                    UnityEngine.Transform bone = animator.GetBoneTransform(bodyBone);
                    if (bone == null)
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
                transforms = new UnityEngine.Transform[boneCount],
                boneCount = boneCount,
                boneList = boneList,
                parameters = parameters,
                avatarKind = avatarKind,
                isSdk2 = isSdk2
            };

            int index = -1;
            for (int i = 0; i < UnityEngine.HumanTrait.BoneCount; i++)
            {
                if (!boneList[i])
                    continue;
                index++;

                Quaternion preQ = senderPlayerData.preQArray[index] = avatar.GetPreRotation(i).ToSystem();
                Quaternion postQ = senderPlayerData.postQArray[index] = avatar.GetPostRotation(i).ToSystem();
                senderPlayerData.preQinvArray[index] = Quaternion.Inverse(preQ);
                senderPlayerData.postQinvArray[index] = Quaternion.Inverse(postQ);
                senderPlayerData.transforms[index] = animator.GetBoneTransform((UnityEngine.HumanBodyBones)i);
            }
        }
    }
}