using MelonLoader;
using System;
using System.Collections.Generic;
using System.Numerics;
using TMPro;
using VRC;
using VRC.Networking;
using VRC.Playables;
using GameObject = UnityEngine.GameObject;
using Transform = UnityEngine.Transform;

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
            public Transform namePlateStatusLine;
            public Transform namePlateAvatarProgress;
        }

        // Large structure, must be class to avoid deepcopies
        public class PlayerData
        {
            public int photonId;
            public bool frozen;
            public short avatarKind;
            public Transform playerTransform;
            public PoseRecorder playerPoseRecorder;
            public HandGestureController playerHandGestureController;
            public VRCVrIkController playerVRCVrIkController;
            public VRCAvatarManager playerAvatarManager;
            public Quaternion[] preQArray;
            public Quaternion[] preQinvArray;
            public Quaternion[] postQArray;
            public Quaternion[] postQinvArray;
            public Transform[] transforms;
            public int boneCount;
            public bool[] boneList;
            public List<AvatarParameter> parameters;
            public bool active;
            public bool isSdk2;
            public AvatarParameter inStationParam;
        }

        public struct ReceiverPacketData
        {
            public int photonId;
            public int ping;
            public bool frozen;
            public short avatarKind;
            public int boneCount;
            public bool[] boneList;
            public DataBank dataBankA;
            public DataBank dataBankB;

            public int packetsPerSecond;
            public long lastTimeReceived;

            public void SwapDataBanks(DataBank dataBank)
            {
                (dataBankA, dataBankB) = (dataBank, dataBankA);
            }
        }

        // Large structure, must be class to avoid deepcopies
        public class DataBank
        {
            public long timestamp;
            public int deltaTime;
            public Quaternion[] boneRotations;
            public Vector3 hipPosition;
            public Quaternion hipRotation;
            public Vector3 playerPosition;
            public Quaternion playerRotation;
        }
    }
}