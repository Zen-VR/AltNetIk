using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using VRC;
using TMPro;
using VRC.Playables;
using VRC.Networking;

namespace AltNetIk
{
    public partial class AltNetIk : MelonMod
    {
        public struct NamePlateInfo
        {
            public int photonId;
            public Player player;
            public GameObject namePlate;
            public TextMeshProUGUI namePlateText;
        }

        public struct PlayerData
        {
            public int photonId;
            public bool frozen;
            public short avatarKind;
            public Transform playerTransform;
            public PoseRecorder playerPoseRecorder;
            public HandGestureController playerHandGestureController;
            public FlatBufferNetworkSerializer playerAnimControlNetSerializer;
            public IkController playerIkController;
            public VRCVrIkController playerVRCVrIkController;
            public VRCAvatarManager playerAvatarManager;
            public Quaternion[] preQArray;
            public Quaternion[] preQinvArray;
            public Quaternion[] postQArray;
            public Quaternion[] postQinvArray;
            public Transform[] transforms;
            public int boneCount;
            public bool[] boneList;
            public Dictionary<string, AvatarParameter> parameters;
            public bool active;
            public bool isSdk2;
        }

        public struct ReceiverPacketData
        {
            public int photonId;
            public int ping;
            public bool frozen;
            public short avatarKind;
            public int boneCount;
            public bool[] boneList;
            public DataBank dataBank1;
            public DataBank dataBank2;
            public short bankSelector;

            public int packetsPerSecond;
            public Int64 lastTimeReceived;
        }

        public struct DataBank
        {
            public Int64 timestamp;
            public int deltaTime;
            public PacketData.Quaternion[] boneRotations;
            public PacketData.Vector3 hipPosition;
            public PacketData.Quaternion hipRotation;
            public PacketData.Vector3 playerPosition;
            public PacketData.Quaternion playerRotation;
        }
    }
}
